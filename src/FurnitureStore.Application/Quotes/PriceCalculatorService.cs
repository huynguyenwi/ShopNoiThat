using FurnitureStore.Application.Common.Exceptions;
using FurnitureStore.Domain.Entities;
using FurnitureStore.Domain.Enums;

namespace FurnitureStore.Application.Quotes;

/// <summary>
/// Prices custom furniture from the PriceRules table - the only place where quote prices are decided (the AI never does).
/// <code>
/// surface      = 2 (L·W + L·H + W·H)                      (bounding box, m²)
/// materialArea = surface × MaterialUsageFactor[type]
/// material     = materialArea × MaterialCostPerM²[material]
/// labor        = LaborBaseCost[type] + materialArea × LaborCostPerM²[type]
/// finishing    = materialArea × FinishingCostPerM²
/// paint        = materialArea × PaintCostPerM²[finish]
/// accessories  = AccessoryCost[type]
/// direct       = material + labor + finishing + paint + accessories
/// overhead     = direct × Overhead%
/// profit       = (direct + overhead) × ProfitMargin%
/// unit price   = direct + overhead + profit, rounded up to 10.000đ;   total = unit price × quantity
/// </code>
/// For each parameter the most specific active rule wins (furniture type / material / finish), then the highest priority.
/// </summary>
public sealed class PriceCalculatorService(IPriceRuleRepository rules, TimeProvider timeProvider)
{
    public const int MinLengthMm = 200, MaxLengthMm = 4000;
    public const int MinWidthMm = 150, MaxWidthMm = 2500;
    public const int MinHeightMm = 50, MaxHeightMm = 2800;
    public const int MaxQuantity = 50;

    public async Task<PriceBreakdown> CalculateAsync(QuoteSpec spec, CancellationToken cancellationToken = default) =>
        Calculate(spec, await rules.GetActiveAsync(cancellationToken), timeProvider.GetUtcNow().UtcDateTime);

    public static PriceBreakdown Calculate(QuoteSpec spec, IReadOnlyList<PriceRule> priceRules, DateTime utcNow)
    {
        Validate(spec);
        var applied = new List<string>();

        decimal Value(PriceRuleType type)
        {
            var rule = Select(priceRules, type, spec, utcNow)
                       ?? throw new BusinessRuleException($"Bảng giá chưa có tham số \"{RuleTypeName(type)}\" phù hợp. Vui lòng liên hệ cửa hàng để được báo giá.");
            applied.Add($"{rule.Code}: {rule.Name} = {rule.Value:0.####} {rule.Unit}");
            return rule.Value;
        }

        decimal l = spec.LengthMm / 1000m, w = spec.WidthMm / 1000m, h = spec.HeightMm / 1000m;
        var surface = Math.Round(2 * (l * w + l * h + w * h), 2);
        var materialArea = Math.Round(surface * Value(PriceRuleType.MaterialUsageFactor), 2);

        var material = Thousands(materialArea * Value(PriceRuleType.MaterialCostPerSquareMeter));
        var labor = Thousands(Value(PriceRuleType.LaborBaseCost) + materialArea * Value(PriceRuleType.LaborCostPerSquareMeter));
        var finishing = Thousands(materialArea * Value(PriceRuleType.FinishingCostPerSquareMeter));
        var paint = Thousands(materialArea * Value(PriceRuleType.PaintCostPerSquareMeter));
        var accessories = Thousands(Value(PriceRuleType.AccessoryCost));
        var direct = material + labor + finishing + paint + accessories;

        var overheadPercent = Value(PriceRuleType.OverheadPercent);
        var overhead = Thousands(direct * overheadPercent / 100m);
        var profitPercent = Value(PriceRuleType.ProfitMarginPercent);
        var unitPrice = Math.Ceiling((direct + overhead + (direct + overhead) * profitPercent / 100m) / 10_000m) * 10_000m;
        var profit = unitPrice - direct - overhead; // absorbs the rounding so the parts add up to the unit price

        return new PriceBreakdown(surface, materialArea, material, labor, finishing, paint, accessories, direct,
            overheadPercent, overhead, profitPercent, profit, unitPrice, spec.Quantity, unitPrice * spec.Quantity, applied);
    }

    /// <summary>Most specific effective rule for the spec (null conditions match anything), then highest priority, then newest.</summary>
    public static PriceRule? Select(IReadOnlyList<PriceRule> priceRules, PriceRuleType type, QuoteSpec spec, DateTime utcNow) =>
        priceRules
            .Where(r => r.RuleType == type && r.IsEffectiveAt(utcNow)
                        && (r.FurnitureType == null || r.FurnitureType == spec.FurnitureType)
                        && (r.MaterialId == null || r.MaterialId == spec.MaterialId)
                        && (r.FinishType == null || r.FinishType == spec.FinishType))
            .OrderByDescending(r => (r.FurnitureType is null ? 0 : 1) + (r.MaterialId is null ? 0 : 1) + (r.FinishType is null ? 0 : 1))
            .ThenByDescending(r => r.Priority)
            .ThenByDescending(r => r.Id)
            .FirstOrDefault();

    public static void Validate(QuoteSpec spec)
    {
        var errors = new Dictionary<string, string[]>();
        if (spec.LengthMm is < MinLengthMm or > MaxLengthMm) errors["LengthMm"] = [$"Chiều dài từ {MinLengthMm} đến {MaxLengthMm} mm."];
        if (spec.WidthMm is < MinWidthMm or > MaxWidthMm) errors["WidthMm"] = [$"Chiều rộng / sâu từ {MinWidthMm} đến {MaxWidthMm} mm."];
        if (spec.HeightMm is < MinHeightMm or > MaxHeightMm) errors["HeightMm"] = [$"Chiều cao từ {MinHeightMm} đến {MaxHeightMm} mm."];
        if (spec.Quantity is < 1 or > MaxQuantity) errors["Quantity"] = [$"Số lượng từ 1 đến {MaxQuantity}."];
        if (!Enum.IsDefined(spec.FinishType)) errors["Finish"] = ["Kiểu hoàn thiện không hợp lệ."];
        if (errors.Count > 0)
        {
            throw new AppValidationException(errors);
        }
    }

    private static decimal Thousands(decimal value) => Math.Round(value / 1000m, MidpointRounding.AwayFromZero) * 1000m;

    public static string RuleTypeName(PriceRuleType type) => type switch
    {
        PriceRuleType.MaterialCostPerSquareMeter => "Giá vật liệu / m²",
        PriceRuleType.MaterialUsageFactor => "Hệ số vật liệu",
        PriceRuleType.LaborBaseCost => "Công cơ bản",
        PriceRuleType.LaborCostPerSquareMeter => "Công gia công / m²",
        PriceRuleType.FinishingCostPerSquareMeter => "Hoàn thiện / m²",
        PriceRuleType.PaintCostPerSquareMeter => "Sơn phủ / m²",
        PriceRuleType.AccessoryCost => "Phụ kiện",
        PriceRuleType.OverheadPercent => "Chi phí chung (%)",
        PriceRuleType.ProfitMarginPercent => "Lợi nhuận (%)",
        _ => type.ToString()
    };

    public static string FinishName(FinishType finish) => finish switch
    {
        FinishType.NaturalOil => "Lau dầu tự nhiên",
        FinishType.PU => "Sơn PU",
        FinishType.NC => "Sơn NC",
        FinishType.TwoK => "Sơn 2K",
        FinishType.Lacquer => "Sơn bóng cao cấp (lacquer)",
        _ => finish.ToString()
    };
}
