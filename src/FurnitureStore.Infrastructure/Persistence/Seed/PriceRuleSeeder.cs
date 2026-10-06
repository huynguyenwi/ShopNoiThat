using FurnitureStore.Domain.Entities;
using FurnitureStore.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FurnitureStore.Infrastructure.Persistence.Seed;

/// <summary>
/// Default parameters of the custom-furniture price calculator (seeded once, edited at /admin/price-rules).
/// Calibrated so that custom pieces land close to the catalog prices of similar products,
/// e.g. a walnut dining table 1800 x 900 x 750 ≈ 18 million, a fabric sofa 2400 x 900 x 800 ≈ 19 million.
/// Material-specific prices are only created for materials that exist when seeding runs.
/// </summary>
public sealed class PriceRuleSeeder(ApplicationDbContext context, ILogger<PriceRuleSeeder> logger)
{
    private static readonly Dictionary<string, decimal> MaterialCostPerM2 = new()
    {
        ["go-oc-cho"] = 1_900_000,
        ["go-soi-nga"] = 1_050_000,
        ["go-soi"] = 1_200_000,
        ["go-tan-bi"] = 950_000,
        ["go-cao-su"] = 550_000,
        ["go-thong"] = 450_000,
        ["mdf-chong-am"] = 350_000,
        ["vai-bo"] = 250_000,
        ["vai-nhung"] = 320_000,
        ["vai-luoi"] = 180_000,
        ["da-that"] = 1_600_000,
        ["da-pu"] = 450_000,
        ["thep-son-tinh-dien"] = 400_000,
        ["da-marble"] = 2_800_000,
        ["da-ceramic"] = 1_500_000,
        ["kinh-cuong-luc"] = 600_000,
        ["may-tre"] = 500_000
    };

    // Per furniture type: (material usage factor, labor base, labor per m², accessories)
    private static readonly Dictionary<FurnitureType, (decimal Usage, decimal LaborBase, decimal LaborPerM2, decimal Accessories)> TypeRules = new()
    {
        [FurnitureType.Table] = (0.55m, 1_500_000, 350_000, 300_000),
        [FurnitureType.Chair] = (0.70m, 600_000, 500_000, 100_000),
        [FurnitureType.Sofa] = (0.90m, 3_500_000, 450_000, 600_000),
        [FurnitureType.Bed] = (0.60m, 2_500_000, 300_000, 500_000),
        [FurnitureType.Cabinet] = (1.10m, 3_000_000, 400_000, 900_000),
        [FurnitureType.Shelf] = (0.90m, 1_200_000, 300_000, 250_000),
        [FurnitureType.Decor] = (0.50m, 500_000, 400_000, 150_000),
        [FurnitureType.Other] = (0.80m, 1_000_000, 350_000, 300_000)
    };

    private static readonly Dictionary<FinishType, decimal> PaintPerM2 = new()
    {
        [FinishType.NaturalOil] = 90_000,
        [FinishType.PU] = 120_000,
        [FinishType.NC] = 70_000,
        [FinishType.TwoK] = 180_000,
        [FinishType.Lacquer] = 220_000
    };

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        if (await context.PriceRules.AnyAsync(cancellationToken))
        {
            return;
        }

        var rules = new List<PriceRule>();
        var materials = await context.ProductMaterials.AsNoTracking().ToDictionaryAsync(m => m.Slug, cancellationToken);

        rules.Add(Rule("MAT-DEFAULT", "Vật liệu (mặc định)", PriceRuleType.MaterialCostPerSquareMeter, 800_000, "đ/m²",
            description: "Dùng khi chất liệu chưa có đơn giá riêng."));
        foreach (var (slug, price) in MaterialCostPerM2)
        {
            if (materials.TryGetValue(slug, out var material))
            {
                rules.Add(Rule(MaterialCode(slug), $"Vật liệu: {material.Name}", PriceRuleType.MaterialCostPerSquareMeter, price, "đ/m²", materialId: material.Id));
            }
        }

        foreach (var (type, r) in TypeRules)
        {
            var name = TypeName(type);
            var code = type.ToString().ToUpperInvariant();
            rules.Add(Rule($"USAGE-{code}", $"Hệ số vật liệu: {name}", PriceRuleType.MaterialUsageFactor, r.Usage, "hệ số", type,
                description: "Tỷ lệ diện tích vật liệu thực tế so với diện tích bao ngoài."));
            rules.Add(Rule($"LABOR-BASE-{code}", $"Công cơ bản: {name}", PriceRuleType.LaborBaseCost, r.LaborBase, "đ/sản phẩm", type));
            rules.Add(Rule($"LABOR-M2-{code}", $"Công gia công theo m²: {name}", PriceRuleType.LaborCostPerSquareMeter, r.LaborPerM2, "đ/m²", type));
            rules.Add(Rule($"ACC-{code}", $"Phụ kiện: {name}", PriceRuleType.AccessoryCost, r.Accessories, "đ/sản phẩm", type,
                description: "Bản lề, ray, ốc vít, chân tăng chỉnh..."));
        }

        rules.Add(Rule("FINISH-DEFAULT", "Hoàn thiện (chà nhám, lắp ráp)", PriceRuleType.FinishingCostPerSquareMeter, 150_000, "đ/m²"));
        rules.Add(Rule("FINISH-SOFA", "Hoàn thiện sofa (may bọc, đệm)", PriceRuleType.FinishingCostPerSquareMeter, 250_000, "đ/m²", FurnitureType.Sofa));

        foreach (var (finish, price) in PaintPerM2)
        {
            rules.Add(Rule($"PAINT-{finish.ToString().ToUpperInvariant()}", $"Sơn phủ: {FinishName(finish)}", PriceRuleType.PaintCostPerSquareMeter, price, "đ/m²", finish: finish));
        }

        // Sofas are mostly upholstered: only the frame is painted, whatever the finish.
        rules.Add(Rule("PAINT-SOFA", "Sơn phủ khung sofa", PriceRuleType.PaintCostPerSquareMeter, 40_000, "đ/m²", FurnitureType.Sofa, priority: 10));

        rules.Add(Rule("OVERHEAD", "Chi phí chung của xưởng", PriceRuleType.OverheadPercent, 15, "%", description: "Mặt bằng, điện, khấu hao máy, vận hành."));
        rules.Add(Rule("PROFIT", "Lợi nhuận", PriceRuleType.ProfitMarginPercent, 30, "%", description: "Tính trên tổng chi phí (trực tiếp + chi phí chung)."));

        context.PriceRules.AddRange(rules);
        await context.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Seeded {Count} price rules for custom quotes", rules.Count);
    }

    /// <summary>
    /// Price of a material created after the price list was seeded (see <see cref="CatalogSeeder"/>), or null when it has
    /// no default price.
    /// </summary>
    internal static PriceRule? MaterialRuleFor(ProductMaterial material)
    {
        if (!MaterialCostPerM2.TryGetValue(material.Slug, out var price))
        {
            return null;
        }

        var rule = Rule(MaterialCode(material.Slug), $"Vật liệu: {material.Name}", PriceRuleType.MaterialCostPerSquareMeter, price, "đ/m²");
        rule.Material = material;
        return rule;
    }

    private static string MaterialCode(string slug) => $"MAT-{slug.ToUpperInvariant()}";

    private static PriceRule Rule(string code, string name, PriceRuleType type, decimal value, string unit, FurnitureType? furnitureType = null,
        int? materialId = null, FinishType? finish = null, int priority = 0, string? description = null) => new()
    {
        Code = code,
        Name = name,
        RuleType = type,
        FurnitureType = furnitureType,
        MaterialId = materialId,
        FinishType = finish,
        Value = value,
        Unit = unit,
        Priority = priority,
        IsActive = true,
        Description = description
    };

    private static string TypeName(FurnitureType type) => type switch
    {
        FurnitureType.Table => "Bàn",
        FurnitureType.Chair => "Ghế",
        FurnitureType.Sofa => "Sofa",
        FurnitureType.Bed => "Giường",
        FurnitureType.Cabinet => "Tủ",
        FurnitureType.Shelf => "Kệ",
        FurnitureType.Decor => "Đồ trang trí",
        _ => "Khác"
    };

    private static string FinishName(FinishType finish) => finish switch
    {
        FinishType.NaturalOil => "Lau dầu tự nhiên",
        FinishType.PU => "Sơn PU",
        FinishType.NC => "Sơn NC",
        FinishType.TwoK => "Sơn 2K",
        FinishType.Lacquer => "Sơn bóng cao cấp",
        _ => finish.ToString()
    };
}
