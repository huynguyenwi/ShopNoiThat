using FurnitureStore.Application.Common.Models;
using FurnitureStore.Application.Quotes;
using FurnitureStore.Domain.Entities;
using FurnitureStore.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace FurnitureStore.Infrastructure.Persistence.Repositories;

public sealed class PriceRuleRepository(ApplicationDbContext context) : EfRepository<PriceRule>(context), IPriceRuleRepository
{
    /// <summary>Active rules; effective dates are checked by the calculator against its clock.</summary>
    public async Task<IReadOnlyList<PriceRule>> GetActiveAsync(CancellationToken cancellationToken = default) =>
        await Context.PriceRules.AsNoTracking().Where(r => r.IsActive).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<PriceRuleDto>> ListAsync(PriceRuleType? type, CancellationToken cancellationToken = default)
    {
        var query = Context.PriceRules.AsNoTracking();
        if (type.HasValue) query = query.Where(r => r.RuleType == type);
        return await query
            .OrderBy(r => r.RuleType).ThenBy(r => r.FurnitureType).ThenBy(r => r.Code)
            .Select(r => new PriceRuleDto(r.Id, r.Code, r.Name, r.RuleType, r.FurnitureType, r.MaterialId, r.Material != null ? r.Material.Name : null,
                r.FinishType, r.Value, r.Unit, r.Priority, r.IsActive, r.Description, r.EffectiveFrom, r.EffectiveTo))
            .ToListAsync(cancellationToken);
    }

    public Task<bool> CodeExistsAsync(string code, int? excludeId, CancellationToken cancellationToken = default) =>
        Context.PriceRules.AnyAsync(r => r.Code == code && (excludeId == null || r.Id != excludeId), cancellationToken);
}

public sealed class QuoteRepository(ApplicationDbContext context) : EfRepository<QuoteRequest>(context), IQuoteRepository
{
    public Task<QuoteRequest?> GetByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        var normalized = code.Trim().ToUpperInvariant();
        return Context.QuoteRequests.FirstOrDefaultAsync(q => q.QuoteCode == normalized, cancellationToken);
    }

    public Task<bool> CodeExistsAsync(string code, CancellationToken cancellationToken = default) =>
        Context.QuoteRequests.AnyAsync(q => q.QuoteCode == code, cancellationToken);

    public async Task<PagedResult<QuoteListItemDto>> SearchAsync(AdminQuoteQuery query, string? userId, CancellationToken cancellationToken = default)
    {
        var quotes = Context.QuoteRequests.AsNoTracking();
        if (userId is not null) quotes = quotes.Where(q => q.UserId == userId);
        if (query.Status.HasValue) quotes = quotes.Where(q => q.Status == query.Status);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            var upper = term.ToUpperInvariant();
            quotes = quotes.Where(q => q.QuoteCode.Contains(upper) || q.CustomerName.Contains(term) || q.Phone.Contains(term)
                                       || q.ProductTypeName.Contains(term) || q.RawRequest.Contains(term));
        }

        var total = await quotes.CountAsync(cancellationToken);
        var rows = await quotes
            .OrderByDescending(q => q.CreatedAt).ThenByDescending(q => q.Id)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(q => new QuoteListItemDto(q.Id, q.QuoteCode, q.CustomerName, q.Phone, q.ProductTypeName,
                q.LengthMm + " x " + q.WidthMm + " x " + q.HeightMm + " mm", q.MaterialName, q.Quantity,
                q.EstimatedTotal, q.FinalQuotedPrice, q.Status, q.CreatedAt, q.UserId == null))
            .ToListAsync(cancellationToken);
        return new PagedResult<QuoteListItemDto>(rows, total, query.Page, query.PageSize);
    }

    public Task<int> CountNewAsync(CancellationToken cancellationToken = default) =>
        Context.QuoteRequests.CountAsync(q => q.Status == QuoteStatus.New, cancellationToken);
}
