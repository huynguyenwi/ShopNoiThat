using FurnitureStore.Application.Catalog;
using FurnitureStore.Domain.Enums;

namespace FurnitureStore.Application.AI;

/// <summary>Products found for an intent, plus the constraints that had to be relaxed to find them (e.g. "màu").</summary>
public sealed record ProductMatch(IReadOnlyList<ProductFact> Products, IReadOnlyList<string> RelaxedConstraints)
{
    public static readonly ProductMatch Empty = new([], []);
}

/// <summary>
/// Finds real catalog products for a <see cref="ShoppingIntent"/>: queries the catalog with the customer's filters,
/// relaxes them step by step when nothing matches, then ranks candidates by budget, size, capacity and popularity.
/// </summary>
public sealed class ProductMatcher(ICatalogService catalog, IProductFactRepository facts)
{
    private const int CandidatePoolSize = 24;

    /// <summary>How a room budget is split between its main pieces (in order of importance).</summary>
    private static readonly Dictionary<string, (string Category, decimal Share)[]> RoomSets = new()
    {
        ["phong-khach"] = [("sofa", 0.5m), ("ban-tra", 0.2m), ("ke-tivi", 0.2m), ("ghe-thu-gian", 0.1m)],
        ["phong-ngu"] = [("giuong-ngu", 0.45m), ("tu-quan-ao", 0.35m), ("tab-dau-giuong", 0.1m), ("ban-trang-diem", 0.1m)],
        ["phong-an"] = [("bo-ban-an", 0.75m), ("tu-ruou", 0.25m)],
        ["phong-lam-viec"] = [("ban-lam-viec", 0.5m), ("ghe-van-phong", 0.35m), ("ke-sach", 0.15m)],
        ["trang-tri-tien-ich"] = [("tu-giay", 0.4m), ("ke-trang-tri", 0.3m), ("guong", 0.3m)]
    };

    public async Task<ProductMatch> FindAsync(ShoppingIntent intent, int take, CancellationToken cancellationToken = default)
    {
        var categorySlugs = intent.Categories.Count > 0
            ? intent.Categories.Select(c => (string?)c.Slug).ToList()
            : [intent.RoomSlug];

        // Drop the least important constraints one by one until a few products are found.
        var relaxed = new List<string>();
        var ids = await SearchAsync(intent, categorySlugs, relaxed, cancellationToken);
        foreach (var constraint in RelaxationOrder)
        {
            if (ids.Count >= Math.Min(3, take)) break;
            if (!Applies(constraint, intent)) continue;
            relaxed.Add(constraint);
            ids = await SearchAsync(intent, categorySlugs, relaxed, cancellationToken);
        }

        // Nothing in the requested category even after relaxing: try close alternatives (a dining set → table + chairs).
        if (ids.Count == 0 && intent.Categories.Count > 0)
        {
            var alternatives = intent.Categories.SelectMany(c => CategoryAlternatives.GetValueOrDefault(c.Slug) ?? []).Distinct().Select(s => (string?)s).ToList();
            if (alternatives.Count > 0)
            {
                relaxed.Add("loại sản phẩm");
                ids = await SearchAsync(intent, alternatives, relaxed, cancellationToken);
            }
        }

        if (ids.Count == 0)
        {
            return new ProductMatch([], relaxed);
        }

        var candidates = await facts.GetFactsAsync(ids, cancellationToken);
        var ranked = candidates.OrderByDescending(p => Score(p, intent)).ThenBy(p => p.Price).Take(take).ToList();

        // Report only the constraints that some suggested product really does not meet.
        var unmet = relaxed.Where(constraint => ranked.Any(p => Violates(p, constraint, intent))).ToList();
        return new ProductMatch(ranked, unmet);
    }

    private static bool Violates(ProductFact p, string constraint, ShoppingIntent intent) => constraint switch
    {
        "màu sắc" => !intent.Colors.Any(c => p.ColorSlugs.Contains(c.Slug)),
        "chất liệu" => !intent.Materials.Any(m => p.MaterialSlugs.Contains(m.Slug)),
        "phong cách" => p.StyleName is null || !intent.Styles.Any(s => s.Name == p.StyleName),
        "ngân sách" => (intent.SearchMaxPrice is decimal max && p.Price > max) || (intent.MinBudget is decimal min && p.Price < min),
        "loại sản phẩm" => !intent.Categories.Any(c => c.Slug == p.CategorySlug),
        _ => false
    };

    private static readonly string[] RelaxationOrder = ["màu sắc", "chất liệu", "phong cách", "ngân sách"];

    /// <summary>Categories that can replace each other when the requested one has nothing suitable.</summary>
    private static readonly Dictionary<string, string[]> CategoryAlternatives = new()
    {
        ["bo-ban-an"] = ["ban-an", "ghe-an"],
        ["ban-an"] = ["bo-ban-an"],
        ["ghe-thu-gian"] = ["sofa"],
        ["tab-dau-giuong"] = ["ke-trang-tri"],
        ["ke-trang-tri"] = ["ke-sach"]
    };

    private async Task<List<int>> SearchAsync(ShoppingIntent intent, IReadOnlyList<string?> categorySlugs, IReadOnlyCollection<string> relaxed, CancellationToken cancellationToken)
    {
        var ids = new List<int>();
        foreach (var slug in categorySlugs)
        {
            var query = BuildQuery(intent, slug);
            if (relaxed.Contains("màu sắc")) query.ColorSlugs = [];
            if (relaxed.Contains("chất liệu")) query.MaterialSlugs = [];
            if (relaxed.Contains("phong cách")) query.StyleSlugs = [];
            if (relaxed.Contains("ngân sách"))
            {
                query.MaxPrice = query.MaxPrice is null ? null : Math.Round(query.MaxPrice.Value * 1.3m, 0);
                query.MinPrice = null;
            }

            var page = await catalog.GetProductsAsync(query, cancellationToken);
            ids.AddRange(page.Items.Select(p => p.Id).Where(id => !ids.Contains(id)));
        }

        return ids;
    }

    /// <summary>
    /// A furnished room within the budget: the best piece of each main category ("Sofa A, Bàn B, Kệ C").
    /// Falls back to <see cref="FindAsync"/> when the intent is not about a whole room.
    /// </summary>
    public async Task<ProductMatch> FindRoomSetAsync(ShoppingIntent intent, CancellationToken cancellationToken = default)
    {
        if (intent.RoomSlug is null || intent.Categories.Count > 0 || !RoomSets.TryGetValue(intent.RoomSlug, out var set))
        {
            return await FindAsync(intent, 6, cancellationToken);
        }

        var budget = intent.MaxBudget;
        var chosen = new List<ProductFact>();
        var relaxed = new HashSet<string>();
        decimal spent = 0, planned = 0;
        foreach (var (category, share) in set)
        {
            var pieceIntent = CloneFor(intent, category);
            if (budget.HasValue)
            {
                // This piece's share of the budget plus whatever the earlier pieces left unused.
                planned += budget.Value * share;
                var allowance = Math.Min(budget.Value - spent, planned - spent);
                if (allowance <= 0) continue;
                pieceIntent.MaxBudget = allowance;
                pieceIntent.BudgetIsApproximate = false;
            }

            var match = await FindAsync(pieceIntent, 1, cancellationToken);
            var best = match.Products.FirstOrDefault();
            if (best is null || (budget.HasValue && spent + best.Price > budget.Value * 1.05m))
            {
                continue;
            }

            chosen.Add(best);
            spent += best.Price;
            foreach (var r in match.RelaxedConstraints.Where(r => r != "ngân sách")) relaxed.Add(r);
        }

        return chosen.Count > 0 ? new ProductMatch(chosen, relaxed.ToList()) : await FindAsync(intent, 6, cancellationToken);
    }

    private static bool Applies(string constraint, ShoppingIntent intent) => constraint switch
    {
        "màu sắc" => intent.Colors.Count > 0,
        "chất liệu" => intent.Materials.Count > 0,
        "phong cách" => intent.Styles.Count > 0,
        "ngân sách" => intent.SearchMaxPrice.HasValue || intent.MinBudget.HasValue,
        _ => true
    };

    private static ProductQuery BuildQuery(ShoppingIntent intent, string? categorySlug) => new()
    {
        CategorySlug = categorySlug,
        ColorSlugs = intent.Colors.Select(c => c.Slug).ToList(),
        MaterialSlugs = intent.Materials.Select(m => m.Slug).ToList(),
        StyleSlugs = intent.Styles.Select(s => s.Slug).ToList(),
        MaxPrice = intent.SearchMaxPrice,
        MinPrice = intent.MinBudget,
        Sort = ProductSort.BestSelling,
        PageSize = CandidatePoolSize
    };

    private static ShoppingIntent CloneFor(ShoppingIntent source, string categorySlug)
    {
        var clone = new ShoppingIntent
        {
            MinBudget = null,
            MaxBudget = source.MaxBudget,
            BudgetIsApproximate = source.BudgetIsApproximate,
            RoomSlug = source.RoomSlug,
            RoomName = source.RoomName,
            People = source.People,
            RoomAreaM2 = source.RoomAreaM2
        };
        clone.Categories.Add((categorySlug, categorySlug));
        clone.Colors.AddRange(source.Colors);
        clone.Materials.AddRange(source.Materials);
        clone.Styles.AddRange(source.Styles);
        return clone;
    }

    /// <summary>Higher is better. Public for tests.</summary>
    public static double Score(ProductFact p, ShoppingIntent intent)
    {
        double score = 0;
        if (p.InStock) score += 3;
        score += (double)p.AverageRating * 0.4 + Math.Log(p.SoldCount + 1) * 0.3;

        if (intent.MaxBudget is decimal max && max > 0)
        {
            var ratio = (double)(p.Price / max);
            // Best around 60-100% of the budget; over budget is penalised.
            score += ratio <= 1 ? 3 - Math.Abs(0.85 - ratio) * 2 : -6 * (ratio - 1);
        }

        var targetLength = intent.LengthMm ?? LengthForPeople(p, intent.People) ?? LengthForRoom(p, intent.RoomAreaM2);
        if (targetLength is int length && length > 0)
        {
            var lengths = p.Sizes.Select(s => s.LengthMm).Where(l => l > 0).ToList();
            if (lengths.Count > 0)
            {
                var diff = lengths.Min(l => Math.Abs(l - length)) / (double)length;
                score += diff <= 0.1 ? 4 : diff <= 0.2 ? 2 : diff <= 0.35 ? 0 : -2;
            }
        }

        if (intent.Colors.Count > 0 && intent.Colors.Any(c => p.ColorSlugs.Contains(c.Slug))) score += 2;
        if (intent.Materials.Count > 0 && intent.Materials.Any(m => p.MaterialSlugs.Contains(m.Slug))) score += 2;
        if (intent.Styles.Count > 0 && p.StyleName is not null && intent.Styles.Any(s => s.Name == p.StyleName)) score += 2;
        return score;
    }

    /// <summary>Dining tables / sets and sofas: seats → typical length.</summary>
    private static int? LengthForPeople(ProductFact p, int? people)
    {
        if (people is not int n) return null;
        if (p.CategorySlug is "ban-an" or "bo-ban-an")
        {
            return n <= 2 ? 1000 : n <= 4 ? 1400 : n <= 6 ? 1700 : 2000;
        }

        return p.FurnitureType == FurnitureType.Sofa ? (n <= 2 ? 1600 : n == 3 ? 2100 : 2800) : null;
    }

    /// <summary>Small living rooms suit compact sofas; big rooms suit corner sofas.</summary>
    private static int? LengthForRoom(ProductFact p, decimal? areaM2)
    {
        if (areaM2 is not decimal area || p.FurnitureType != FurnitureType.Sofa) return null;
        return area < 15 ? 1700 : area <= 25 ? 2200 : 2900;
    }
}
