using FurnitureStore.Domain.Common;
using FurnitureStore.Domain.Enums;

namespace FurnitureStore.Domain.Entities;

/// <summary>History of price changes for a product / variant (table ProductPriceHistory).</summary>
public class ProductPriceHistory : BaseEntity
{
    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;
    public int? ProductVariantId { get; set; }
    public ProductVariant? ProductVariant { get; set; }

    public decimal OldPrice { get; set; }
    public decimal NewPrice { get; set; }
    public decimal? OldOriginalPrice { get; set; }
    public decimal? NewOriginalPrice { get; set; }
    public string? Reason { get; set; }
    public string? ChangedBy { get; set; }
    public DateTime ChangedAt { get; set; }
}

/// <summary>
/// A parameter of the custom-furniture price calculator (material cost per m², labor, finish,
/// overhead %, profit margin %...). Optional FurnitureType / Material / FinishType narrow where it applies;
/// the most specific active rule with the highest priority wins.
/// </summary>
public class PriceRule : AuditableEntity
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public PriceRuleType RuleType { get; set; }

    public FurnitureType? FurnitureType { get; set; }
    public int? MaterialId { get; set; }
    public ProductMaterial? Material { get; set; }
    public FinishType? FinishType { get; set; }

    public decimal Value { get; set; }
    public string Unit { get; set; } = string.Empty;
    public int Priority { get; set; }
    public bool IsActive { get; set; } = true;
    public string? Description { get; set; }
    public DateTime? EffectiveFrom { get; set; }
    public DateTime? EffectiveTo { get; set; }

    public bool IsEffectiveAt(DateTime utcNow) =>
        IsActive
        && (EffectiveFrom is null || EffectiveFrom <= utcNow)
        && (EffectiveTo is null || EffectiveTo >= utcNow);
}
