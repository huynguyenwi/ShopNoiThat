using FurnitureStore.Application.Admin;
using FurnitureStore.Application.Common.Models;
using FurnitureStore.Application.Engagement;
using FurnitureStore.Domain.Constants;
using FurnitureStore.Domain.Entities;
using FurnitureStore.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace FurnitureStore.Infrastructure.Persistence.Repositories;

public sealed class AdminReportRepository(ApplicationDbContext context) : IAdminReportRepository
{
    public async Task<IReadOnlyList<OrderFact>> GetOrderFactsAsync(DateTime fromUtc, CancellationToken cancellationToken = default) =>
        await context.Orders.AsNoTracking()
            .Where(o => o.PlacedAt >= fromUtc)
            .Select(o => new OrderFact(o.PlacedAt, o.TotalAmount, o.Status))
            .ToListAsync(cancellationToken);

    public async Task<decimal> GetTotalRevenueAsync(IReadOnlyCollection<OrderStatus> revenueStatuses, CancellationToken cancellationToken = default)
    {
        var statuses = revenueStatuses.ToList();
        // Summed client-side over a projected column: portable across SQL Server and SQLite (tests).
        var totals = await context.Orders.AsNoTracking().Where(o => statuses.Contains(o.Status)).Select(o => o.TotalAmount).ToListAsync(cancellationToken);
        return totals.Sum();
    }

    public async Task<IReadOnlyList<StatusCountDto>> GetOrderStatusCountsAsync(CancellationToken cancellationToken = default)
    {
        var counts = await context.Orders.AsNoTracking()
            .GroupBy(o => o.Status)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        return Enum.GetValues<OrderStatus>()
            .Select(status => new StatusCountDto(status, counts.FirstOrDefault(c => c.Key == status)?.Count ?? 0))
            .ToList();
    }

    public async Task<(int Total, int NewSince)> CountCustomersAsync(DateTime newSinceUtc, CancellationToken cancellationToken = default)
    {
        var customers =
            from user in context.Users
            join userRole in context.UserRoles on user.Id equals userRole.UserId
            join role in context.Roles on userRole.RoleId equals role.Id
            where role.Name == AppRoles.User
            select user;

        return (await customers.CountAsync(cancellationToken), await customers.CountAsync(u => u.CreatedAt >= newSinceUtc, cancellationToken));
    }

    public async Task<(int Total, int Active)> CountProductsAsync(CancellationToken cancellationToken = default) =>
        (await context.Products.CountAsync(cancellationToken), await context.Products.CountAsync(p => p.Status == ProductStatus.Active, cancellationToken));

    public async Task<(int Count, IReadOnlyList<LowStockItemDto> Items)> GetLowStockAsync(int take, CancellationToken cancellationToken = default)
    {
        var lowStock = context.ProductVariants.AsNoTracking()
            .Where(v => v.IsActive && v.Product.Status == ProductStatus.Active && v.StockQuantity <= v.LowStockThreshold);

        var items = await lowStock
            .OrderBy(v => v.StockQuantity).ThenBy(v => v.Product.Name)
            .Take(take)
            .Select(v => new LowStockItemDto(v.ProductId, v.Id, v.Product.Name, v.Name, v.Sku, v.StockQuantity, v.LowStockThreshold))
            .ToListAsync(cancellationToken);

        return (await lowStock.CountAsync(cancellationToken), items);
    }

    public async Task<IReadOnlyList<TopProductDto>> GetTopProductsAsync(IReadOnlyCollection<OrderStatus> statuses, DateTime fromUtc, int take, CancellationToken cancellationToken = default)
    {
        var statusList = statuses.ToList();
        var lines = await context.OrderItems.AsNoTracking()
            .Where(i => statusList.Contains(i.Order.Status) && i.Order.PlacedAt >= fromUtc)
            .Select(i => new { i.ProductId, i.ProductName, i.Quantity, i.LineTotal })
            .ToListAsync(cancellationToken);

        return lines
            .GroupBy(i => i.ProductId ?? -1)
            .Select(g => new TopProductDto(g.Key == -1 ? null : g.Key, g.First().ProductName, g.Sum(i => i.Quantity), g.Sum(i => i.LineTotal)))
            .OrderByDescending(t => t.Quantity).ThenByDescending(t => t.Revenue)
            .Take(take)
            .ToList();
    }

    public async Task<PagedResult<AdminOrderListItemDto>> SearchOrdersAsync(AdminOrderQuery query, DateTime? fromUtc, DateTime? toUtc, CancellationToken cancellationToken = default)
    {
        var orders = context.Orders.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            var upper = term.ToUpperInvariant();
            orders = orders.Where(o => o.OrderCode.Contains(upper) || o.CustomerName.Contains(term) || o.CustomerPhone.Contains(term) || o.CustomerEmail.Contains(term));
        }

        if (query.Status.HasValue) orders = orders.Where(o => o.Status == query.Status);
        if (query.PaymentStatus.HasValue) orders = orders.Where(o => o.PaymentStatus == query.PaymentStatus);
        if (query.PaymentMethod.HasValue) orders = orders.Where(o => o.PaymentMethod == query.PaymentMethod);
        if (fromUtc.HasValue) orders = orders.Where(o => o.PlacedAt >= fromUtc);
        if (toUtc.HasValue) orders = orders.Where(o => o.PlacedAt < toUtc);

        var total = await orders.CountAsync(cancellationToken);
        var rows = await orders
            .OrderByDescending(o => o.PlacedAt)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(o => new AdminOrderListItemDto(o.Id, o.OrderCode, o.PlacedAt, o.CustomerName, o.CustomerPhone, o.Status, o.PaymentStatus,
                o.PaymentMethod, o.TotalAmount, o.Items.Sum(i => i.Quantity)))
            .ToListAsync(cancellationToken);

        return new PagedResult<AdminOrderListItemDto>(rows, total, query.Page, query.PageSize);
    }

    public async Task<PagedResult<AuditLogDto>> SearchAuditLogsAsync(AuditLogQuery query, DateTime? fromUtc, DateTime? toUtc, CancellationToken cancellationToken = default)
    {
        var logs = context.AuditLogs.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            logs = logs.Where(l => (l.UserName != null && l.UserName.Contains(term)) || (l.Description != null && l.Description.Contains(term)) || l.EntityId == term);
        }

        if (query.Action.HasValue) logs = logs.Where(l => l.Action == query.Action);
        if (!string.IsNullOrWhiteSpace(query.EntityName)) logs = logs.Where(l => l.EntityName == query.EntityName);
        if (fromUtc.HasValue) logs = logs.Where(l => l.CreatedAt >= fromUtc);
        if (toUtc.HasValue) logs = logs.Where(l => l.CreatedAt < toUtc);

        var total = await logs.CountAsync(cancellationToken);
        var rows = await logs
            .OrderByDescending(l => l.CreatedAt).ThenByDescending(l => l.Id)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(l => new AuditLogDto(l.Id, l.CreatedAt, l.UserName, l.Action, l.EntityName, l.EntityId, l.Description, l.OldValues, l.NewValues, l.IpAddress))
            .ToListAsync(cancellationToken);

        return new PagedResult<AuditLogDto>(rows, total, query.Page, query.PageSize);
    }

    public async Task<IReadOnlyList<NotificationDto>> GetRoleNotificationsAsync(string role, int take, CancellationToken cancellationToken = default) =>
        await context.Notifications.AsNoTracking()
            .Where(n => n.RecipientRole == role)
            .OrderByDescending(n => n.CreatedAt).ThenByDescending(n => n.Id)
            .Take(take)
            .Select(n => new NotificationDto(n.Id, n.Type, n.Title, n.Message, n.Link, n.IsRead, n.CreatedAt))
            .ToListAsync(cancellationToken);

    public Task<int> CountUnreadRoleNotificationsAsync(string role, CancellationToken cancellationToken = default) =>
        context.Notifications.CountAsync(n => n.RecipientRole == role && !n.IsRead, cancellationToken);

    public Task MarkRoleNotificationsReadAsync(string role, DateTime readAtUtc, CancellationToken cancellationToken = default) =>
        context.Notifications.Where(n => n.RecipientRole == role && !n.IsRead)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.IsRead, true).SetProperty(n => n.ReadAt, readAtUtc), cancellationToken);

    public async Task<NotificationDto?> ReadRoleNotificationAsync(string role, int id, DateTime readAtUtc, CancellationToken cancellationToken = default)
    {
        var notification = await context.Notifications.FirstOrDefaultAsync(n => n.Id == id && n.RecipientRole == role, cancellationToken);
        if (notification is null)
        {
            return null;
        }

        if (!notification.IsRead)
        {
            notification.IsRead = true;
            notification.ReadAt = readAtUtc;
            await context.SaveChangesAsync(cancellationToken);
        }

        return new NotificationDto(notification.Id, notification.Type, notification.Title, notification.Message, notification.Link, true, notification.CreatedAt);
    }
}

public sealed class ReviewRepository(ApplicationDbContext context) : EfRepository<Review>(context), IReviewRepository
{
    public async Task<PagedResult<ReviewDto>> GetVisibleForProductAsync(int productId, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var query = Context.Reviews.AsNoTracking().Where(r => r.ProductId == productId && !r.IsHidden);
        var total = await query.CountAsync(cancellationToken);
        var rows = await query
            .OrderByDescending(r => r.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(r => new ReviewDto(r.Id, r.ProductId, r.ReviewerName, r.Rating, r.Title, r.Comment, r.IsVerifiedPurchase, r.CreatedAt,
                r.AdminReply, r.AdminRepliedAt, r.Images.OrderBy(i => i.DisplayOrder).Select(i => i.Url).ToList()))
            .ToListAsync(cancellationToken);
        return new PagedResult<ReviewDto>(rows, total, page, pageSize);
    }

    public async Task<ReviewStatsDto> GetStatsAsync(int productId, CancellationToken cancellationToken = default)
    {
        var groups = await Context.Reviews.AsNoTracking()
            .Where(r => r.ProductId == productId && !r.IsHidden)
            .GroupBy(r => r.Rating)
            .Select(g => new { Rating = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var distribution = Enumerable.Range(1, 5).Select(star => groups.FirstOrDefault(g => g.Rating == star)?.Count ?? 0).ToList();
        var count = distribution.Sum();
        var average = count == 0 ? 0 : Math.Round((decimal)groups.Sum(g => g.Rating * g.Count) / count, 2);
        return new ReviewStatsDto(average, count, distribution);
    }

    public Task<Review?> GetByUserAndProductAsync(string userId, int productId, CancellationToken cancellationToken = default) =>
        Context.Reviews.Include(r => r.Images).FirstOrDefaultAsync(r => r.UserId == userId && r.ProductId == productId, cancellationToken);

    public Task<Review?> GetWithImagesAsync(int id, CancellationToken cancellationToken = default) =>
        Context.Reviews.Include(r => r.Images).FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

    public async Task<PagedResult<AdminReviewDto>> SearchAdminAsync(AdminReviewQuery query, CancellationToken cancellationToken = default)
    {
        var reviews = Context.Reviews.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            reviews = reviews.Where(r => r.Comment.Contains(term) || r.ReviewerName.Contains(term) || r.Product.Name.Contains(term));
        }

        if (query.Rating.HasValue) reviews = reviews.Where(r => r.Rating == query.Rating);
        if (query.Hidden.HasValue) reviews = reviews.Where(r => r.IsHidden == query.Hidden);

        var total = await reviews.CountAsync(cancellationToken);
        var rows = await reviews
            .OrderByDescending(r => r.CreatedAt)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(r => new AdminReviewDto(r.Id, r.ProductId, r.Product.Name, r.Product.Slug, r.UserId, r.ReviewerName, r.Rating, r.Title, r.Comment,
                r.IsHidden, r.IsVerifiedPurchase, r.CreatedAt, r.AdminReply, r.Images.OrderBy(i => i.DisplayOrder).Select(i => i.Url).ToList()))
            .ToListAsync(cancellationToken);
        return new PagedResult<AdminReviewDto>(rows, total, query.Page, query.PageSize);
    }

    public async Task<IReadOnlyList<HomeReviewDto>> GetLatestAsync(int take, CancellationToken cancellationToken = default) =>
        await Context.Reviews.AsNoTracking()
            .Where(r => !r.IsHidden && r.Rating >= 4 && r.Product.Status == ProductStatus.Active)
            .OrderByDescending(r => r.CreatedAt)
            .Take(take)
            .Select(r => new HomeReviewDto(r.ReviewerName, r.Rating, r.Comment, r.Product.Name, r.Product.Slug, r.CreatedAt))
            .ToListAsync(cancellationToken);

    public Task UpdateProductRatingAsync(int productId, decimal average, int count, CancellationToken cancellationToken = default) =>
        Context.Products.IgnoreQueryFilters().Where(p => p.Id == productId)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.AverageRating, average).SetProperty(p => p.ReviewCount, count), cancellationToken);
}

public sealed class ContactRepository(ApplicationDbContext context) : EfRepository<ContactMessage>(context), IContactRepository
{
    public async Task<PagedResult<ContactMessageDto>> SearchAsync(ContactMessageStatus? status, string? search, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var messages = Context.ContactMessages.AsNoTracking();
        if (status.HasValue) messages = messages.Where(m => m.Status == status);
        if (!string.IsNullOrWhiteSpace(search))
        {
            messages = messages.Where(m => m.FullName.Contains(search) || m.Email.Contains(search) || m.Phone.Contains(search) || m.Message.Contains(search));
        }

        var total = await messages.CountAsync(cancellationToken);
        var rows = await messages
            .OrderByDescending(m => m.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(m => new ContactMessageDto(m.Id, m.FullName, m.Phone, m.Email, m.Subject, m.Message, m.Status, m.AdminNote, m.IpAddress, m.CreatedAt, m.RepliedAt))
            .ToListAsync(cancellationToken);
        return new PagedResult<ContactMessageDto>(rows, total, page, pageSize);
    }

    public Task<int> CountNewAsync(CancellationToken cancellationToken = default) =>
        Context.ContactMessages.CountAsync(m => m.Status == ContactMessageStatus.New, cancellationToken);
}
