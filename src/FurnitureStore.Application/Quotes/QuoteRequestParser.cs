using System.Globalization;
using System.Text.RegularExpressions;
using FurnitureStore.Application.AI;
using FurnitureStore.Application.Catalog;
using FurnitureStore.Domain.Enums;

namespace FurnitureStore.Application.Quotes;

/// <summary>A kind of custom piece (a catalog category) with its furniture type and typical size.</summary>
public sealed record QuoteKind(string Slug, string Name, FurnitureType FurnitureType, int LengthMm, int WidthMm, int HeightMm, FinishType DefaultFinish, string DefaultMaterialSlug);

/// <summary>Kinds of furniture the workshop builds to order, derived from the catalog categories.</summary>
public static class QuoteKinds
{
    private static readonly Dictionary<string, (int L, int W, int H)> TypicalSizes = new()
    {
        ["sofa"] = (2100, 850, 800),
        ["ban-tra"] = (1000, 550, 450),
        ["ke-tivi"] = (1600, 400, 500),
        ["ghe-thu-gian"] = (750, 800, 850),
        ["giuong-ngu"] = (2000, 1600, 1000),
        ["tu-quan-ao"] = (1600, 600, 2000),
        ["tab-dau-giuong"] = (450, 400, 500),
        ["ban-trang-diem"] = (1000, 450, 750),
        ["ban-an"] = (1600, 850, 750),
        ["ghe-an"] = (450, 500, 850),
        ["bo-ban-an"] = (1600, 850, 750),
        ["tu-ruou"] = (1200, 450, 1800),
        ["ban-lam-viec"] = (1200, 600, 750),
        ["ghe-van-phong"] = (650, 650, 1100),
        ["ke-sach"] = (800, 300, 1800),
        ["tu-giay"] = (900, 350, 1000),
        ["ke-trang-tri"] = (1000, 250, 1200),
        ["guong"] = (600, 50, 1600)
    };

    public static IReadOnlyList<QuoteKind> From(FilterOptionsDto options) =>
        options.Categories.SelectMany(room => room.Children).Select(c => Create(c.Slug, c.Name)).ToList();

    public static QuoteKind Create(string slug, string name)
    {
        var type = TypeOf(slug);
        var (l, w, h) = TypicalSizes.TryGetValue(slug, out var size) ? size : type switch
        {
            FurnitureType.Table => (1200, 700, 750),
            FurnitureType.Chair => (500, 500, 850),
            FurnitureType.Sofa => (2100, 850, 800),
            FurnitureType.Bed => (2000, 1600, 1000),
            FurnitureType.Cabinet => (1200, 450, 900),
            FurnitureType.Shelf => (1000, 350, 1200),
            _ => (800, 400, 800)
        };
        var defaultMaterial = type == FurnitureType.Sofa ? "vai-bo" : slug == "ghe-van-phong" ? "vai-luoi" : "go-soi";
        return new QuoteKind(slug, name, type, l, w, h, FinishType.PU, defaultMaterial);
    }

    /// <summary>Furniture type of a category slug (works for categories added later by admins too).</summary>
    public static FurnitureType TypeOf(string slug) => slug switch
    {
        "sofa" => FurnitureType.Sofa,
        "giuong-ngu" => FurnitureType.Bed,
        "tab-dau-giuong" or "tu-ruou" => FurnitureType.Cabinet,
        "guong" => FurnitureType.Decor,
        _ when slug.StartsWith("ban", StringComparison.Ordinal) || slug.StartsWith("bo-ban", StringComparison.Ordinal) => FurnitureType.Table,
        _ when slug.StartsWith("ghe", StringComparison.Ordinal) => FurnitureType.Chair,
        _ when slug.StartsWith("giuong", StringComparison.Ordinal) => FurnitureType.Bed,
        _ when slug.StartsWith("tu", StringComparison.Ordinal) => FurnitureType.Cabinet,
        _ when slug.StartsWith("ke", StringComparison.Ordinal) => FurnitureType.Shelf,
        _ => FurnitureType.Other
    };
}

/// <summary>What could be read from a free-text request. Null = not mentioned.</summary>
public sealed record ParsedQuoteRequest(
    string? Kind,
    int? LengthMm,
    int? WidthMm,
    int? HeightMm,
    int? MaterialId,
    bool MaterialIsExplicit,
    int? ColorId,
    int? StyleId,
    FinishType? Finish,
    int? Quantity);

/// <summary>Rule-based reading of requests such as "Tôi muốn bàn gỗ óc chó dài 2m2 rộng 1m cao 75cm, sơn PU, 2 cái".</summary>
public static partial class QuoteRequestParser
{
    public static ParsedQuoteRequest Parse(string? text, CatalogVocabulary vocabulary, FilterOptionsDto options)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new ParsedQuoteRequest(null, null, null, null, null, false, null, null, null, null);
        }

        var intent = ShoppingIntentParser.Parse(text, vocabulary);
        var plain = ShoppingIntentParser.Plain(text);
        var lower = text.ToLowerInvariant();

        var kind = intent.Categories.Count > 0 ? intent.Categories[0].Slug : GenericKind(plain, intent);
        int? length = intent.LengthMm, width = intent.WidthMm, height = intent.HeightMm;

        // "giường 1m6": the single size of a bed is its width (the length is the standard 2m).
        if (kind == "giuong-ngu" && length is <= 1900 && width is null)
        {
            width = length;
            length = 2000;
        }

        // Materials: prefer one written explicitly ("gỗ óc chó") over a generic phrase ("gỗ tự nhiên").
        int? materialId = null;
        var explicitMaterial = false;
        var explicitTerm = vocabulary.Materials.FirstOrDefault(m =>
            lower.Contains(m.Name.ToLowerInvariant(), StringComparison.Ordinal)
            || plain.Contains(ShoppingIntentParser.Plain(m.Name), StringComparison.Ordinal));
        if (explicitTerm is not null)
        {
            materialId = options.Materials.FirstOrDefault(m => m.Slug == explicitTerm.Slug)?.Id;
            explicitMaterial = materialId is not null;
        }
        else if (intent.Materials.Count > 0)
        {
            var slug = intent.Materials.Any(m => m.Slug == "go-soi") ? "go-soi" : intent.Materials[0].Slug;
            materialId = options.Materials.FirstOrDefault(m => m.Slug == slug)?.Id;
        }

        var colorId = intent.Colors.Count > 0 ? options.Colors.FirstOrDefault(c => c.Slug == intent.Colors[0].Slug)?.Id : null;
        var styleId = intent.Styles.Count > 0 ? options.Styles.FirstOrDefault(s => s.Slug == intent.Styles[0].Slug)?.Id : null;

        int? quantity = null;
        var q = QuantityRegex().Match(plain);
        if (q.Success && int.TryParse(q.Groups["n"].Value, CultureInfo.InvariantCulture, out var n) && n is > 0 and <= PriceCalculatorService.MaxQuantity)
        {
            quantity = n;
        }

        return new ParsedQuoteRequest(kind, length, width, height, materialId, explicitMaterial, colorId, styleId, FinishOf(plain), quantity);
    }

    /// <summary>"bàn", "tủ", "kệ", "ghế" without a precise kind: decided by the size.</summary>
    private static string? GenericKind(string plain, ShoppingIntent intent)
    {
        bool Has(string word) => Regex.IsMatch(plain, $@"(?<![\p{{L}}]){word}(?![\p{{L}}])");

        if (Has("ban"))
        {
            if (intent.HeightMm is <= 500) return "ban-tra";
            return intent.LengthMm is >= 1400 && intent.WidthMm is null or >= 750 ? "ban-an" : "ban-lam-viec";
        }

        if (Has("tu")) return intent.HeightMm is >= 1500 ? "tu-quan-ao" : "tu-giay";
        if (Has("ke")) return intent.HeightMm is >= 1100 ? "ke-sach" : "ke-tivi";
        if (Has("ghe")) return "ghe-an";
        if (Has("giuong")) return "giuong-ngu";
        return null;
    }

    private static FinishType? FinishOf(string plain)
    {
        if (Regex.IsMatch(plain, @"\b2k\b")) return FinishType.TwoK;
        if (Regex.IsMatch(plain, @"\bpu\b")) return FinishType.PU;
        if (Regex.IsMatch(plain, @"\bnc\b")) return FinishType.NC;
        if (Regex.IsMatch(plain, @"lau dau|dau tu nhien|dau lau|\boil\b|phu dau")) return FinishType.NaturalOil;
        if (Regex.IsMatch(plain, @"son bong|son mai|lacquer|bong guong")) return FinishType.Lacquer;
        return null;
    }

    [GeneratedRegex(@"(?:so luong\s*:?\s*(?<n>\d{1,2}))|(?<![\p{L}\d.,])(?<n>\d{1,2})\s*(?:cai|chiec|bo|mon|san pham|ban|tu|ke|giuong)(?![\p{L}])")]
    private static partial Regex QuantityRegex();
}
