using FurnitureStore.Application.AI;
using FurnitureStore.Application.Common.Models;
using FurnitureStore.Domain.Entities;
using FurnitureStore.Domain.Enums;

namespace FurnitureStore.Application.Quotes;

// ================================================================== calculator

/// <summary>What to price: one piece of custom furniture (per unit) and how many.</summary>
public sealed record QuoteSpec(
    FurnitureType FurnitureType,
    int LengthMm,
    int WidthMm,
    int HeightMm,
    int? MaterialId,
    FinishType FinishType,
    int Quantity);

/// <summary>Cost breakdown computed by <see cref="PriceCalculatorService"/> from the PriceRules table (per unit unless stated).</summary>
public sealed record PriceBreakdown(
    decimal SurfaceAreaM2,
    decimal MaterialAreaM2,
    decimal MaterialCost,
    decimal LaborCost,
    decimal FinishingCost,
    decimal PaintCost,
    decimal AccessoryCost,
    decimal DirectCost,
    decimal OverheadPercent,
    decimal OverheadCost,
    decimal ProfitPercent,
    decimal ProfitAmount,
    decimal UnitPrice,
    int Quantity,
    decimal Total,
    IReadOnlyList<string> AppliedRules)
{
    /// <summary>All amounts of this breakdown (the only money figures an AI explanation may quote).</summary>
    public IEnumerable<decimal> Amounts() =>
        [MaterialCost, LaborCost, FinishingCost, PaintCost, AccessoryCost, DirectCost, OverheadCost, ProfitAmount, UnitPrice, Total,
         MaterialCost + LaborCost, OverheadCost + ProfitAmount, UnitPrice - ProfitAmount];
}

// ================================================================== estimate (no persistence)

/// <summary>
/// Free text ("bàn gỗ óc chó dài 2m2 rộng 1m cao 75cm") and/or structured fields. Structured fields win over what is parsed
/// from the text. Prices are never taken from the client: they are always recomputed on the server.
/// </summary>
public class QuoteEstimateRequest
{
    public string? Text { get; set; }

    /// <summary>Catalog category slug of the piece ("ban-an", "tu-quan-ao"...).</summary>
    public string? Kind { get; set; }
    public int? LengthMm { get; set; }
    public int? WidthMm { get; set; }
    public int? HeightMm { get; set; }
    public int? MaterialId { get; set; }
    public int? ColorId { get; set; }
    public int? StyleId { get; set; }
    public FinishType? Finish { get; set; }
    public int? Quantity { get; set; }
}

public sealed record QuoteSpecDto(
    string Kind,
    string TypeName,
    FurnitureType FurnitureType,
    int LengthMm,
    int WidthMm,
    int HeightMm,
    int? MaterialId,
    string? MaterialName,
    int? ColorId,
    string? ColorName,
    int? StyleId,
    string? StyleName,
    FinishType Finish,
    string FinishName,
    int Quantity);

public sealed record QuoteAlternativeDto(int MaterialId, string MaterialName, decimal UnitPrice, decimal Total);

public sealed record QuoteEstimateDto(
    QuoteSpecDto Spec,
    IReadOnlyList<string> Assumptions,
    PriceBreakdown Breakdown,
    string Explanation,
    IReadOnlyList<QuoteAlternativeDto> Alternatives,
    IReadOnlyList<ProductRecommendationDto> SimilarProducts,
    bool AiEnabled);

// ================================================================== quote requests

public sealed class QuoteSubmitCommand : QuoteEstimateRequest
{
    public string Name { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Note { get; set; }
}

public sealed record QuoteSubmittedDto(int Id, string Code, decimal EstimatedUnitPrice, decimal EstimatedTotal);

public sealed record QuoteListItemDto(
    int Id,
    string Code,
    string CustomerName,
    string Phone,
    string TypeName,
    string Dimensions,
    string? MaterialName,
    int Quantity,
    decimal EstimatedTotal,
    decimal? FinalQuotedPrice,
    QuoteStatus Status,
    DateTime CreatedAt,
    bool IsGuest);

public sealed record QuoteDetailDto(
    int Id,
    string Code,
    string? UserId,
    string CustomerName,
    string Phone,
    string? Email,
    string RawRequest,
    QuoteSpecDto Spec,
    PriceBreakdown Breakdown,
    decimal? FinalQuotedPrice,
    QuoteStatus Status,
    string? AiExplanation,
    string? CustomerNote,
    string? AdminNote,
    DateTime CreatedAt,
    DateTime? QuotedAt,
    string? QuotedBy,
    Guid Version,
    IReadOnlyList<QuoteStatus> NextStatuses);

public sealed class AdminQuoteQuery
{
    public string? Search { get; set; }
    public QuoteStatus? Status { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

public sealed class AdminQuoteUpdateCommand
{
    public QuoteStatus Status { get; set; }
    public decimal? FinalQuotedPrice { get; set; }
    public string? AdminNote { get; set; }
    public Guid Version { get; set; }
}

// ================================================================== price rules (admin)

public sealed record PriceRuleDto(
    int Id,
    string Code,
    string Name,
    PriceRuleType RuleType,
    FurnitureType? FurnitureType,
    int? MaterialId,
    string? MaterialName,
    FinishType? FinishType,
    decimal Value,
    string Unit,
    int Priority,
    bool IsActive,
    string? Description,
    DateTime? EffectiveFrom,
    DateTime? EffectiveTo);

public sealed class PriceRuleCommand
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public PriceRuleType RuleType { get; set; }
    public FurnitureType? FurnitureType { get; set; }
    public int? MaterialId { get; set; }
    public FinishType? FinishType { get; set; }
    public decimal Value { get; set; }
    public string Unit { get; set; } = string.Empty;
    public int Priority { get; set; }
    public bool IsActive { get; set; } = true;
    public string? Description { get; set; }
    public DateTime? EffectiveFrom { get; set; }
    public DateTime? EffectiveTo { get; set; }
}

// ================================================================== repositories

public interface IPriceRuleRepository : Common.Interfaces.IRepository<PriceRule>
{
    Task<IReadOnlyList<PriceRule>> GetActiveAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PriceRuleDto>> ListAsync(PriceRuleType? type, CancellationToken cancellationToken = default);
    Task<bool> CodeExistsAsync(string code, int? excludeId, CancellationToken cancellationToken = default);
}

public interface IQuoteRepository : Common.Interfaces.IRepository<QuoteRequest>
{
    Task<QuoteRequest?> GetByCodeAsync(string code, CancellationToken cancellationToken = default);
    Task<bool> CodeExistsAsync(string code, CancellationToken cancellationToken = default);
    Task<PagedResult<QuoteListItemDto>> SearchAsync(AdminQuoteQuery query, string? userId, CancellationToken cancellationToken = default);
    Task<int> CountNewAsync(CancellationToken cancellationToken = default);
}
