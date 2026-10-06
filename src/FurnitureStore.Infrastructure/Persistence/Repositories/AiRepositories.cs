using FurnitureStore.Application.AI;
using FurnitureStore.Application.Common.Models;
using FurnitureStore.Domain.Entities;
using FurnitureStore.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace FurnitureStore.Infrastructure.Persistence.Repositories;

/// <summary>Product facts given to the assistant: prices of every variant, colors, materials and sizes - straight from the catalog.</summary>
public sealed class ProductFactRepository(ApplicationDbContext context) : IProductFactRepository
{
    private const int MaxVariantsPerProduct = 12;

    public async Task<IReadOnlyList<ProductFact>> GetFactsAsync(IReadOnlyCollection<int> productIds, CancellationToken cancellationToken = default)
    {
        if (productIds.Count == 0)
        {
            return [];
        }

        var ids = productIds.Distinct().ToList();
        var products = await context.Products.AsNoTracking()
            .Where(p => ids.Contains(p.Id) && p.Status == ProductStatus.Active)
            .Select(p => new
            {
                p.Id, p.Name, p.Slug,
                CategoryName = p.Category.Name,
                CategorySlug = p.Category.Slug,
                RoomName = p.Category.Parent != null ? p.Category.Parent.Name : null,
                StyleName = p.Style != null ? p.Style.Name : null,
                p.FurnitureType, p.ShortDescription, p.BasePrice, p.DiscountPrice, p.StockQuantity,
                p.AverageRating, p.ReviewCount, p.SoldCount, p.LengthMm, p.WidthMm, p.HeightMm,
                Image = p.Images.Where(i => i.ProductVariantId == null).OrderByDescending(i => i.IsPrimary).ThenBy(i => i.DisplayOrder)
                    .Select(i => i.Url).FirstOrDefault()
            })
            .ToListAsync(cancellationToken);

        // Colors, materials and sizes are three collections: split queries avoid a cartesian product.
        var variants = await context.ProductVariants.AsNoTracking()
            .AsSplitQuery()
            .Where(v => ids.Contains(v.ProductId) && v.IsActive)
            .OrderBy(v => v.DisplayOrder).ThenBy(v => v.Id)
            .Select(v => new
            {
                v.Id, v.ProductId, v.Name, v.Price, v.StockQuantity,
                Colors = v.Colors.OrderByDescending(c => c.IsPrimary).Select(c => new { c.Color.Name, c.Color.Slug }).ToList(),
                Materials = v.Materials.OrderByDescending(m => m.IsPrimary).Select(m => new { m.Material.Name, m.Material.Slug }).ToList(),
                Sizes = v.Sizes.Where(s => s.IsPrimary).Select(s => new { s.Size.Name, s.Size.LengthMm, s.Size.WidthMm, s.Size.HeightMm }).ToList()
            })
            .ToListAsync(cancellationToken);

        var byProduct = variants.GroupBy(v => v.ProductId).ToDictionary(g => g.Key, g => g.ToList());
        return products
            .OrderBy(p => ids.IndexOf(p.Id))
            .Select(p =>
            {
                var own = byProduct.GetValueOrDefault(p.Id) ?? [];
                var price = p.DiscountPrice ?? p.BasePrice;
                var sizes = own.SelectMany(v => v.Sizes).DistinctBy(s => s.Name)
                    .Select(s => new ProductFactSize(s.Name, s.LengthMm, s.WidthMm, s.HeightMm)).ToList();
                if (sizes.Count == 0 && p.LengthMm.HasValue)
                {
                    sizes.Add(new ProductFactSize("Tiêu chuẩn", p.LengthMm.Value, p.WidthMm ?? 0, p.HeightMm ?? 0));
                }

                return new ProductFact(
                    p.Id, p.Name, p.Slug, p.CategoryName, p.CategorySlug, p.RoomName, p.StyleName, p.FurnitureType, p.ShortDescription,
                    price,
                    own.Count > 0 ? own.Max(v => v.Price) : price,
                    p.DiscountPrice.HasValue && p.DiscountPrice < p.BasePrice ? p.BasePrice : null,
                    p.StockQuantity > 0,
                    p.AverageRating, p.ReviewCount, p.SoldCount, p.Image,
                    own.SelectMany(v => v.Colors).Select(c => c.Name).Distinct().ToList(),
                    own.SelectMany(v => v.Colors).Select(c => c.Slug).Distinct().ToList(),
                    own.SelectMany(v => v.Materials).Select(m => m.Name).Distinct().ToList(),
                    own.SelectMany(v => v.Materials).Select(m => m.Slug).Distinct().ToList(),
                    sizes,
                    own.Take(MaxVariantsPerProduct).Select(v => new ProductFactVariant(v.Id, v.Name, v.Price, v.StockQuantity > 0)).ToList());
            })
            .ToList();
    }
}

public sealed class AiKnowledgeRepository(ApplicationDbContext context) : EfRepository<AIKnowledgeEntry>(context), IAiKnowledgeRepository
{
    public async Task<IReadOnlyList<AIKnowledgeEntry>> GetActiveAsync(CancellationToken cancellationToken = default) =>
        await Context.AIKnowledgeEntries.AsNoTracking()
            .Where(k => k.IsActive)
            .OrderBy(k => k.DisplayOrder).ThenBy(k => k.Id)
            .ToListAsync(cancellationToken);
}

public sealed class AiConversationRepository(ApplicationDbContext context) : EfRepository<AIConversation>(context), IAiConversationRepository
{
    public Task<AIConversation?> GetOwnedAsync(int id, AiCaller caller, CancellationToken cancellationToken = default) =>
        Owned(Context.AIConversations, caller).FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    public async Task<IReadOnlyList<AiChatMessage>> GetRecentMessagesAsync(int conversationId, int take, CancellationToken cancellationToken = default)
    {
        var rows = await Context.AIMessages.AsNoTracking()
            .Where(m => m.ConversationId == conversationId && (m.Role == AIMessageRole.User || m.Role == AIMessageRole.Assistant) && !m.IsError)
            .OrderByDescending(m => m.Id)
            .Take(take)
            .Select(m => new AiChatMessage(m.Role, m.Content))
            .ToListAsync(cancellationToken);
        rows.Reverse();
        return rows;
    }

    public async Task<IReadOnlyList<string>> GetRecentUserMessagesAsync(int conversationId, int take, CancellationToken cancellationToken = default)
    {
        var rows = await Context.AIMessages.AsNoTracking()
            .Where(m => m.ConversationId == conversationId && m.Role == AIMessageRole.User)
            .OrderByDescending(m => m.Id)
            .Take(take)
            .Select(m => m.Content)
            .ToListAsync(cancellationToken);
        rows.Reverse();
        return rows;
    }

    public async Task AddMessageAsync(AIMessage message, CancellationToken cancellationToken = default) =>
        await Context.AIMessages.AddAsync(message, cancellationToken);

    public async Task<IReadOnlyList<AiConversationListItemDto>> ListForCallerAsync(AiCaller caller, int take, CancellationToken cancellationToken = default) =>
        await Project(Owned(Context.AIConversations.AsNoTracking(), caller).OrderByDescending(c => c.LastMessageAt).Take(take))
            .ToListAsync(cancellationToken);

    public async Task<PagedResult<AiConversationListItemDto>> SearchAsync(AdminAiConversationQuery query, CancellationToken cancellationToken = default)
    {
        var conversations = Context.AIConversations.AsNoTracking();
        if (query.Type.HasValue) conversations = conversations.Where(c => c.Type == query.Type);
        if (query.ErrorsOnly) conversations = conversations.Where(c => c.Messages.Any(m => m.IsError));
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            conversations = conversations.Where(c => c.Title.Contains(term) || c.Messages.Any(m => m.Content.Contains(term)));
        }

        var total = await conversations.CountAsync(cancellationToken);
        var rows = await Project(conversations.OrderByDescending(c => c.LastMessageAt).ThenByDescending(c => c.Id)
                .Skip((query.Page - 1) * query.PageSize).Take(query.PageSize))
            .ToListAsync(cancellationToken);
        return new PagedResult<AiConversationListItemDto>(rows, total, query.Page, query.PageSize);
    }

    public Task<AiConversationListItemDto?> GetListItemAsync(int id, CancellationToken cancellationToken = default) =>
        Project(Context.AIConversations.AsNoTracking().Where(c => c.Id == id)).FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<AIMessage>> GetMessagesAsync(int conversationId, CancellationToken cancellationToken = default) =>
        await Context.AIMessages.AsNoTracking()
            .Where(m => m.ConversationId == conversationId && m.Role != AIMessageRole.System)
            .OrderBy(m => m.Id)
            .ToListAsync(cancellationToken);

    public Task<int> ClaimAsync(string anonymousId, string userId, CancellationToken cancellationToken = default) =>
        Context.AIConversations
            .Where(c => c.UserId == null && c.AnonymousId == anonymousId)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.UserId, userId).SetProperty(c => c.AnonymousId, (string?)null), cancellationToken);

    private static IQueryable<AIConversation> Owned(IQueryable<AIConversation> query, AiCaller caller) =>
        caller.UserId is not null
            ? query.Where(c => c.UserId == caller.UserId)
            : caller.AnonymousId is not null
                ? query.Where(c => c.UserId == null && c.AnonymousId == caller.AnonymousId)
                : query.Where(c => false);

    private IQueryable<AiConversationListItemDto> Project(IQueryable<AIConversation> query) =>
        from c in query
        join u in Context.Users on c.UserId equals u.Id into users
        from u in users.DefaultIfEmpty()
        select new AiConversationListItemDto(
            c.Id, c.Type, c.Title, c.UserId, u != null ? u.FullName : null, c.UserId == null,
            c.Product != null ? c.Product.Name : null, c.MessageCount, c.TotalTokens,
            c.Messages.Any(m => m.IsError), c.CreatedAt, c.LastMessageAt);
}
