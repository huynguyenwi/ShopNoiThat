using FurnitureStore.Application.Common.Exceptions;
using FurnitureStore.Application.Quotes;
using FurnitureStore.Domain.Entities;
using FurnitureStore.Domain.Enums;
using FurnitureStore.Infrastructure.Persistence;
using FurnitureStore.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FurnitureStore.Tests.Quotes;

/// <summary>The custom-furniture price is decided by business rules only - checked here with hand-computed numbers.</summary>
public sealed class PriceCalculatorTests : IAsyncLifetime
{
    private ServiceTestHost _host = null!;
    private static readonly DateTime Now = new(2026, 9, 1, 9, 0, 0, DateTimeKind.Utc);

    public async Task InitializeAsync() => _host = await ServiceTestHost.CreateAsync();

    public async Task DisposeAsync() => await _host.DisposeAsync();

    private Task<IReadOnlyList<PriceRule>> SeededRulesAsync() =>
        _host.RunAsync(sp => sp.GetRequiredService<IPriceRuleRepository>().GetActiveAsync());

    private Task<int> MaterialIdAsync(string slug) =>
        _host.RunAsync(sp => sp.GetRequiredService<ApplicationDbContext>().ProductMaterials.Where(m => m.Slug == slug).Select(m => m.Id).SingleAsync());

    [Fact]
    public async Task WalnutDiningTable_IsPricedStepByStep()
    {
        var walnut = await MaterialIdAsync("go-oc-cho");
        var spec = new QuoteSpec(FurnitureType.Table, 1800, 900, 750, walnut, FinishType.PU, 1);

        var b = PriceCalculatorService.Calculate(spec, await SeededRulesAsync(), Now);

        // surface = 2(1,8·0,9 + 1,8·0,75 + 0,9·0,75) = 7,29 m²; material area = 7,29 × 0,55 = 4,01 m²
        Assert.Equal(7.29m, b.SurfaceAreaM2);
        Assert.Equal(4.01m, b.MaterialAreaM2);
        Assert.Equal(7_619_000m, b.MaterialCost);    // 4,01 × 1.900.000
        Assert.Equal(2_904_000m, b.LaborCost);       // 1.500.000 + 4,01 × 350.000 = 2.903.500 → nearest 1.000
        Assert.Equal(602_000m, b.FinishingCost);     // 4,01 × 150.000
        Assert.Equal(481_000m, b.PaintCost);         // 4,01 × 120.000 (PU)
        Assert.Equal(300_000m, b.AccessoryCost);
        Assert.Equal(11_906_000m, b.DirectCost);
        Assert.Equal(1_786_000m, b.OverheadCost);    // 15 %
        Assert.Equal(17_800_000m, b.UnitPrice);      // (13.692.000 × 1,3 = 17.799.600) rounded up to 10.000
        Assert.Equal(4_108_000m, b.ProfitAmount);    // the parts add up to the unit price
        Assert.Equal(b.UnitPrice, b.DirectCost + b.OverheadCost + b.ProfitAmount);
        Assert.Equal(17_800_000m, b.Total);
        Assert.Contains(b.AppliedRules, r => r.StartsWith("MAT-GO-OC-CHO"));
    }

    [Fact]
    public async Task Quantity_MultipliesTheTotal_AndCheaperWoodCostsLess()
    {
        var rules = await SeededRulesAsync();
        var walnut = new QuoteSpec(FurnitureType.Table, 1800, 900, 750, await MaterialIdAsync("go-oc-cho"), FinishType.PU, 3);
        var rubberwood = walnut with { MaterialId = await MaterialIdAsync("go-cao-su") };

        var a = PriceCalculatorService.Calculate(walnut, rules, Now);
        var b = PriceCalculatorService.Calculate(rubberwood, rules, Now);

        Assert.Equal(a.UnitPrice * 3, a.Total);
        Assert.True(b.UnitPrice < a.UnitPrice);
        Assert.Equal(a.LaborCost, b.LaborCost); // only the material changes
    }

    [Fact]
    public async Task MostSpecificRuleWins_ThenPriority()
    {
        var rules = await SeededRulesAsync();
        var fabric = await MaterialIdAsync("vai-bo");

        // Sofa: the sofa-specific paint rule (priority 10) beats the generic PU rule of the same specificity.
        var sofa = PriceCalculatorService.Calculate(new QuoteSpec(FurnitureType.Sofa, 2400, 900, 800, fabric, FinishType.PU, 1), rules, Now);
        Assert.Contains(sofa.AppliedRules, r => r.StartsWith("PAINT-SOFA"));
        Assert.Contains(sofa.AppliedRules, r => r.StartsWith("FINISH-SOFA"));
        Assert.InRange(sofa.UnitPrice, 17_000_000m, 21_000_000m); // close to the catalog sofa 2m4 (19,4 million)

        // A material without its own price uses the default material rule.
        var noPrice = PriceCalculatorService.Calculate(new QuoteSpec(FurnitureType.Table, 1200, 600, 750, 999_999, FinishType.NC, 1), rules, Now);
        Assert.Contains(noPrice.AppliedRules, r => r.StartsWith("MAT-DEFAULT"));
    }

    [Fact]
    public void EffectiveDatesInactiveRulesAndPriority_AreRespected()
    {
        List<PriceRule> rules =
        [
            .. BaseRules(),
            new() { Id = 100, Code = "MAT-OLD", RuleType = PriceRuleType.MaterialCostPerSquareMeter, Value = 9_000_000, Unit = "đ/m²", IsActive = true, EffectiveTo = Now.AddDays(-1) },
            new() { Id = 101, Code = "MAT-FUTURE", RuleType = PriceRuleType.MaterialCostPerSquareMeter, Value = 8_000_000, Unit = "đ/m²", IsActive = true, EffectiveFrom = Now.AddDays(1) },
            new() { Id = 102, Code = "MAT-OFF", RuleType = PriceRuleType.MaterialCostPerSquareMeter, Value = 7_000_000, Unit = "đ/m²", IsActive = false, Priority = 99 },
            new() { Id = 103, Code = "MAT-PROMO", RuleType = PriceRuleType.MaterialCostPerSquareMeter, Value = 500_000, Unit = "đ/m²", IsActive = true, Priority = 5 }
        ];

        var chosen = PriceCalculatorService.Select(rules, PriceRuleType.MaterialCostPerSquareMeter, new QuoteSpec(FurnitureType.Table, 1000, 500, 500, null, FinishType.PU, 1), Now);

        Assert.Equal("MAT-PROMO", chosen!.Code); // expired / not yet valid / inactive rules are skipped; higher priority wins
    }

    [Fact]
    public void MissingParameter_IsABusinessError_NotAZeroPrice()
    {
        var rules = BaseRules().Where(r => r.RuleType != PriceRuleType.ProfitMarginPercent).ToList();

        var ex = Assert.Throws<BusinessRuleException>(() =>
            PriceCalculatorService.Calculate(new QuoteSpec(FurnitureType.Table, 1000, 500, 500, null, FinishType.PU, 1), rules, Now));

        Assert.Contains("Lợi nhuận", ex.Message);
    }

    [Theory]
    [InlineData(100, 500, 500, 1, "LengthMm")]
    [InlineData(1000, 3000, 500, 1, "WidthMm")]
    [InlineData(1000, 500, 3000, 1, "HeightMm")]
    [InlineData(1000, 500, 500, 0, "Quantity")]
    [InlineData(1000, 500, 500, 51, "Quantity")]
    public void OutOfRangeSpecs_AreRejected(int length, int width, int height, int quantity, string field)
    {
        var ex = Assert.Throws<AppValidationException>(() =>
            PriceCalculatorService.Calculate(new QuoteSpec(FurnitureType.Table, length, width, height, null, FinishType.PU, quantity), BaseRules(), Now));

        Assert.True(ex.FieldErrors.ContainsKey(field));
    }

    [Fact]
    public async Task EditingARule_ChangesTheNextEstimate()
    {
        var request = new QuoteEstimateRequest { Kind = "ban-an", LengthMm = 1800, WidthMm = 900, HeightMm = 750, MaterialId = await MaterialIdAsync("go-oc-cho"), Finish = FinishType.PU };
        var before = await _host.RunAsync(sp => sp.GetRequiredService<IQuoteService>().EstimateAsync(request, false));

        var profitRuleId = await _host.RunAsync(sp => sp.GetRequiredService<ApplicationDbContext>().PriceRules.Where(r => r.Code == "PROFIT").Select(r => r.Id).SingleAsync());
        await _host.RunAsync(async sp =>
        {
            var admin = sp.GetRequiredService<IPriceRuleAdminService>();
            var command = await admin.GetAsync(profitRuleId);
            command.Value = 40;
            await admin.SaveAsync(profitRuleId, command);
        });
        var after = await _host.RunAsync(sp => sp.GetRequiredService<IQuoteService>().EstimateAsync(request, false));

        Assert.Equal(40m, after.Breakdown.ProfitPercent);
        Assert.True(after.Breakdown.UnitPrice > before.Breakdown.UnitPrice);
        Assert.True(await _host.RunAsync(sp => sp.GetRequiredService<ApplicationDbContext>().AuditLogs.AnyAsync(l => l.EntityName == "PriceRule" && l.EntityId == profitRuleId.ToString())));
    }

    [Theory]
    [InlineData(PriceRuleType.OverheadPercent, 150, "Value")]
    [InlineData(PriceRuleType.MaterialUsageFactor, 12, "Value")]
    [InlineData(PriceRuleType.LaborBaseCost, -1, "Value")]
    public async Task PriceRuleValidation(PriceRuleType type, decimal value, string field)
    {
        var ex = await Assert.ThrowsAsync<AppValidationException>(() => _host.RunAsync(sp => sp.GetRequiredService<IPriceRuleAdminService>().SaveAsync(null,
            new PriceRuleCommand { Code = "TEST-RULE", Name = "Test", RuleType = type, Value = value, Unit = "x" })));

        Assert.True(ex.FieldErrors.ContainsKey(field));
    }

    [Fact]
    public async Task PriceRuleCodes_AreUnique()
    {
        var ex = await Assert.ThrowsAsync<AppValidationException>(() => _host.RunAsync(sp => sp.GetRequiredService<IPriceRuleAdminService>().SaveAsync(null,
            new PriceRuleCommand { Code = "profit", Name = "Trùng mã", RuleType = PriceRuleType.ProfitMarginPercent, Value = 10, Unit = "%" })));

        Assert.True(ex.FieldErrors.ContainsKey("Code"));
    }

    /// <summary>A complete minimal rule set (one generic rule per parameter).</summary>
    private static List<PriceRule> BaseRules() =>
    [
        new() { Id = 1, Code = "MAT", Name = "Vật liệu", RuleType = PriceRuleType.MaterialCostPerSquareMeter, Value = 1_000_000, Unit = "đ/m²", IsActive = true },
        new() { Id = 2, Code = "USAGE", Name = "Hệ số", RuleType = PriceRuleType.MaterialUsageFactor, Value = 0.5m, Unit = "hệ số", IsActive = true },
        new() { Id = 3, Code = "LABOR", Name = "Công", RuleType = PriceRuleType.LaborBaseCost, Value = 1_000_000, Unit = "đ", IsActive = true },
        new() { Id = 4, Code = "LABOR-M2", Name = "Công m²", RuleType = PriceRuleType.LaborCostPerSquareMeter, Value = 300_000, Unit = "đ/m²", IsActive = true },
        new() { Id = 5, Code = "FINISH", Name = "Hoàn thiện", RuleType = PriceRuleType.FinishingCostPerSquareMeter, Value = 100_000, Unit = "đ/m²", IsActive = true },
        new() { Id = 6, Code = "PAINT", Name = "Sơn", RuleType = PriceRuleType.PaintCostPerSquareMeter, Value = 100_000, Unit = "đ/m²", IsActive = true },
        new() { Id = 7, Code = "ACC", Name = "Phụ kiện", RuleType = PriceRuleType.AccessoryCost, Value = 200_000, Unit = "đ", IsActive = true },
        new() { Id = 8, Code = "OVERHEAD", Name = "Chi phí chung", RuleType = PriceRuleType.OverheadPercent, Value = 10, Unit = "%", IsActive = true },
        new() { Id = 9, Code = "PROFIT", Name = "Lợi nhuận", RuleType = PriceRuleType.ProfitMarginPercent, Value = 20, Unit = "%", IsActive = true }
    ];
}
