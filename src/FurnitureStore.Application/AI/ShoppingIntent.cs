using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using FurnitureStore.Application.Catalog;
using FurnitureStore.Application.Common.Utilities;

namespace FurnitureStore.Application.AI;

/// <summary>What a customer is looking for, extracted from free Vietnamese text ("bàn ăn 6 người, khoảng 10 triệu, màu nâu").</summary>
public sealed class ShoppingIntent
{
    public decimal? MinBudget { get; set; }
    public decimal? MaxBudget { get; set; }

    /// <summary>"khoảng 10 triệu": the budget is a target, not a hard cap.</summary>
    public bool BudgetIsApproximate { get; set; }

    public string? RoomSlug { get; set; }
    public string? RoomName { get; set; }
    public List<(string Slug, string Name)> Categories { get; } = [];
    public List<(string Slug, string Name)> Colors { get; } = [];
    public List<(string Slug, string Name)> Materials { get; } = [];
    public List<(string Slug, string Name)> Styles { get; } = [];
    public int? People { get; set; }
    public decimal? RoomAreaM2 { get; set; }
    public int? LengthMm { get; set; }
    public int? WidthMm { get; set; }
    public int? HeightMm { get; set; }

    public bool HasProductNeeds =>
        Categories.Count > 0 || RoomSlug is not null || MaxBudget.HasValue || MinBudget.HasValue || Colors.Count > 0
        || Materials.Count > 0 || Styles.Count > 0 || LengthMm.HasValue || People.HasValue;

    /// <summary>Budget used as a search cap: approximate budgets allow ~15% over.</summary>
    public decimal? SearchMaxPrice => MaxBudget is null ? null : BudgetIsApproximate ? Math.Round(MaxBudget.Value * 1.15m, 0) : MaxBudget;

    /// <summary>Short Vietnamese description, e.g. "bàn ăn · 6 người · màu nâu óc chó · khoảng 10 triệu".</summary>
    public string Describe()
    {
        var parts = new List<string>();
        if (Categories.Count > 0) parts.Add(string.Join(", ", Categories.Select(c => c.Name.ToLowerInvariant())));
        else if (RoomName is not null) parts.Add(RoomName.ToLowerInvariant());
        if (People.HasValue) parts.Add($"{People} người");
        if (RoomAreaM2.HasValue) parts.Add($"phòng {RoomAreaM2.Value.ToString("0.#", CultureInfo.InvariantCulture)}m²");
        if (LengthMm.HasValue) parts.Add($"dài khoảng {LengthMm.Value / 10}cm");
        if (Colors.Count > 0) parts.Add("màu " + string.Join(" / ", Colors.Select(c => c.Name.ToLowerInvariant())));
        if (Materials.Count > 0) parts.Add(string.Join(" / ", Materials.Select(m => m.Name.ToLowerInvariant())));
        if (Styles.Count > 0) parts.Add("phong cách " + string.Join(" / ", Styles.Select(s => s.Name.ToLowerInvariant())));
        if (MinBudget.HasValue && MaxBudget.HasValue) parts.Add($"{Money(MinBudget.Value)} - {Money(MaxBudget.Value)}");
        else if (MaxBudget.HasValue) parts.Add((BudgetIsApproximate ? "khoảng " : "dưới ") + Money(MaxBudget.Value));
        else if (MinBudget.HasValue) parts.Add("từ " + Money(MinBudget.Value));
        return string.Join(" · ", parts);
    }

    public static string Money(decimal value) =>
        value >= 1_000_000
            ? (value / 1_000_000m).ToString("0.#", CultureInfo.InvariantCulture).Replace('.', ',') + " triệu"
            : (value / 1000m).ToString("0", CultureInfo.InvariantCulture) + " nghìn";
}

/// <summary>
/// Words of the catalog (rooms, categories, colors, materials, styles) with Vietnamese synonyms, built from the database
/// so that attributes added by admins are recognised too.
/// </summary>
public sealed class CatalogVocabulary
{
    public sealed record Term(string Slug, string Name, IReadOnlyList<string> Phrases);

    public IReadOnlyList<Term> Rooms { get; }
    public IReadOnlyList<Term> Categories { get; }
    public IReadOnlyList<Term> Colors { get; }
    public IReadOnlyList<Term> Materials { get; }
    public IReadOnlyList<Term> Styles { get; }

    /// <summary>Child category slug → room (parent) slug.</summary>
    public IReadOnlyDictionary<string, string> RoomOfCategory { get; }

    public CatalogVocabulary(FilterOptionsDto options)
    {
        Rooms = options.Categories.Select(r => Build(r.Slug, r.Name, RoomSynonyms)).ToList();
        Categories = options.Categories.SelectMany(r => r.Children).Select(c => Build(c.Slug, c.Name, CategorySynonyms)).ToList();
        RoomOfCategory = options.Categories.SelectMany(r => r.Children.Select(c => (c.Slug, Room: r.Slug))).ToDictionary(x => x.Slug, x => x.Room);
        Colors = options.Colors.Select(c => Build(c.Slug, c.Name, ColorSynonyms)).ToList();
        Materials = options.Materials.Select(m => Build(m.Slug, m.Name, MaterialSynonyms)).ToList();
        Styles = options.Styles.Select(s => Build(s.Slug, s.Name, StyleSynonyms)).ToList();
    }

    private static Term Build(string slug, string name, IReadOnlyDictionary<string, string[]> synonyms)
    {
        var phrases = new List<string>();
        foreach (var part in name.Split('&', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            phrases.Add(part.ToLowerInvariant());
        }

        if (synonyms.TryGetValue(slug, out var extra))
        {
            phrases.AddRange(extra);
        }

        return new Term(slug, name, phrases.Distinct().OrderByDescending(p => p.Length).ToList());
    }

    // Synonyms are written with Vietnamese accents; the parser also matches them without accents when the customer
    // types without diacritics. Ambiguous short words (đen/đến, bàn/bạn, tủ/từ...) only appear inside longer phrases.
    private static readonly Dictionary<string, string[]> RoomSynonyms = new()
    {
        ["phong-khach"] = ["phòng khách", "living room"],
        ["phong-ngu"] = ["phòng ngủ", "bedroom"],
        ["phong-an"] = ["phòng ăn", "phòng bếp", "nhà bếp", "dining"],
        ["phong-lam-viec"] = ["phòng làm việc", "góc làm việc", "văn phòng", "home office", "góc học tập"],
        ["trang-tri-tien-ich"] = ["trang trí", "lối vào"]
    };

    private static readonly Dictionary<string, string[]> CategorySynonyms = new()
    {
        ["sofa"] = ["sofa", "sô pha", "sô-pha", "ghế sofa", "so pha"],
        ["ban-tra"] = ["bàn trà", "bàn sofa", "bàn nước", "bàn cafe"],
        ["ke-tivi"] = ["kệ tivi", "kệ ti vi", "kệ tv", "tủ tivi", "kệ tv"],
        ["ghe-thu-gian"] = ["ghế thư giãn", "ghế đọc sách", "ghế lười", "ghế lounge", "armchair"],
        ["giuong-ngu"] = ["giường ngủ", "giường"],
        ["tu-quan-ao"] = ["tủ quần áo", "tủ áo", "tủ đồ"],
        ["tab-dau-giuong"] = ["tab đầu giường", "táp đầu giường", "tủ đầu giường"],
        ["ban-trang-diem"] = ["bàn trang điểm", "bàn phấn"],
        ["ban-an"] = ["bàn ăn", "bàn bếp"],
        ["ghe-an"] = ["ghế ăn", "ghế bàn ăn"],
        ["bo-ban-an"] = ["bộ bàn ăn", "bộ bàn ghế ăn", "bàn ghế ăn"],
        ["tu-ruou"] = ["tủ rượu", "tủ bếp", "tủ trưng bày"],
        ["ban-lam-viec"] = ["bàn làm việc", "bàn học", "bàn máy tính", "bàn văn phòng"],
        ["ghe-van-phong"] = ["ghế văn phòng", "ghế làm việc", "ghế xoay", "ghế công thái học", "ghế gaming"],
        ["ke-sach"] = ["kệ sách", "giá sách", "tủ sách"],
        ["tu-giay"] = ["tủ giày", "kệ giày", "tủ dép"],
        ["ke-trang-tri"] = ["kệ trang trí", "kệ treo tường", "kệ góc"],
        ["guong"] = ["gương", "gương soi", "gương đứng"]
    };

    private static readonly Dictionary<string, string[]> ColorSynonyms = new()
    {
        ["nau-oc-cho"] = ["nâu óc chó", "màu óc chó", "nâu đậm", "nâu trầm", "màu nâu", "nâu"],
        ["nau-cognac"] = ["nâu cognac", "cognac", "nâu bò", "màu nâu", "nâu"],
        ["go-tu-nhien"] = ["màu gỗ tự nhiên", "màu gỗ", "gỗ tự nhiên", "nâu"],
        ["soi-tu-nhien"] = ["sồi tự nhiên", "màu sồi", "gỗ sáng", "màu gỗ sáng", "màu gỗ"],
        ["van-soi"] = ["vân sồi", "vân gỗ"],
        ["trang"] = ["màu trắng", "trắng"],
        ["kem"] = ["kem be", "màu kem", "màu be", "kem", "be", "beige"],
        ["xam"] = ["màu xám", "xám", "ghi", "grey", "gray"],
        ["den"] = ["màu đen", "đen", "black"],
        ["xanh-reu"] = ["xanh rêu", "xanh lá", "xanh olive", "màu xanh", "xanh"],
        ["xanh-navy"] = ["xanh navy", "navy", "xanh đậm", "xanh than", "màu xanh", "xanh"],
        ["vang-mu-tat"] = ["vàng mù tạt", "màu vàng", "vàng"],
        ["hong-dat"] = ["hồng đất", "màu hồng", "hồng"],
        ["trang-van-da"] = ["trắng vân đá", "vân đá", "vân marble"],
        ["xanh-duong-nhat"] = ["xanh dương nhạt", "xanh dương", "xanh pastel", "xanh da trời", "màu xanh", "xanh"]
    };

    private static readonly Dictionary<string, string[]> MaterialSynonyms = new()
    {
        ["go-oc-cho"] = ["gỗ óc chó", "óc chó", "walnut", "gỗ tự nhiên"],
        ["go-soi"] = ["gỗ sồi", "sồi", "oak", "gỗ tự nhiên"],
        ["go-tan-bi"] = ["gỗ tần bì", "tần bì", "ash", "gỗ tự nhiên"],
        ["go-cao-su"] = ["gỗ cao su", "cao su", "gỗ tự nhiên"],
        ["go-thong"] = ["gỗ thông", "pine", "gỗ tự nhiên"],
        ["mdf-chong-am"] = ["mdf", "gỗ công nghiệp", "ván ép", "melamine"],
        ["vai-bo"] = ["vải bố", "vải linen", "linen", "vải"],
        ["vai-nhung"] = ["vải nhung", "nhung", "velvet", "vải"],
        ["vai-luoi"] = ["vải lưới", "lưới"],
        ["da-that"] = ["da thật", "da bò", "bọc da", "ghế da", "sofa da"],
        ["da-pu"] = ["da pu", "giả da", "da công nghiệp", "bọc da", "sofa da"],
        ["thep-son-tinh-dien"] = ["chân sắt", "khung sắt", "sắt", "thép", "kim loại"],
        ["da-marble"] = ["đá marble", "marble", "cẩm thạch", "mặt đá"],
        ["da-ceramic"] = ["đá ceramic", "ceramic", "đá nung", "mặt đá"],
        ["kinh-cuong-luc"] = ["kính cường lực", "mặt kính", "kính"],
        ["may-tre"] = ["mây tre", "mây", "tre", "rattan"]
    };

    private static readonly Dictionary<string, string[]> StyleSynonyms = new()
    {
        ["hien-dai"] = ["hiện đại", "modern", "trẻ trung"],
        ["toi-gian"] = ["tối giản", "minimalist", "minimal", "đơn giản"],
        ["co-dien"] = ["cổ điển", "tân cổ điển", "classic"],
        ["sang-trong"] = ["sang trọng", "luxury", "cao cấp", "đẳng cấp"],
        ["cong-nghiep"] = ["phong cách công nghiệp", "industrial", "loft"],
        ["bac-au"] = ["bắc âu", "scandinavian", "scandi"],
        ["japandi"] = ["japandi", "kiểu nhật", "phong cách nhật", "zen"],
        ["moc-mac"] = ["mộc mạc", "rustic", "vintage", "đồng quê", "mộc"]
    };
}

public static partial class ShoppingIntentParser
{
    public static ShoppingIntent Parse(string? text, CatalogVocabulary vocabulary)
    {
        var intent = new ShoppingIntent();
        if (string.IsNullOrWhiteSpace(text))
        {
            return intent;
        }

        var lower = Collapse(text.ToLowerInvariant());
        var plain = Plain(text);
        var typedWithoutAccents = !HasVietnameseAccents(text);

        plain = ParseArea(plain, intent);
        plain = ParseBudget(plain, intent);
        ParseDimensions(plain, intent);
        ParsePeople(plain, intent);

        // Categories: longest phrases first; matched spans are blanked so "bộ bàn ăn" does not also yield "bàn ăn".
        var categoryText = (Lower: lower, Plain: plain);
        foreach (var (term, phrase) in vocabulary.Categories.SelectMany(t => t.Phrases.Select(p => (t, p))).OrderByDescending(x => x.p.Length))
        {
            if (intent.Categories.Any(c => c.Slug == term.Slug)) continue;
            if (TryConsume(ref categoryText, phrase, typedWithoutAccents))
            {
                intent.Categories.Add((term.Slug, term.Name));
            }
        }

        foreach (var room in vocabulary.Rooms)
        {
            if (room.Phrases.Any(p => Contains(lower, plain, p, typedWithoutAccents)))
            {
                intent.RoomSlug = room.Slug;
                intent.RoomName = room.Name;
                break;
            }
        }

        if (intent.RoomSlug is null && intent.Categories.Count > 0 && vocabulary.RoomOfCategory.TryGetValue(intent.Categories[0].Slug, out var roomSlug))
        {
            intent.RoomSlug = roomSlug;
            intent.RoomName = vocabulary.Rooms.FirstOrDefault(r => r.Slug == roomSlug)?.Name;
        }

        // "gỗ công nghiệp" is a material, not the industrial style.
        var styleLower = lower.Replace("gỗ công nghiệp", " ");
        var stylePlain = plain.Replace("go cong nghiep", " ");
        AddMatches(vocabulary.Styles, intent.Styles, styleLower, stylePlain, typedWithoutAccents);
        AddMatches(vocabulary.Materials, intent.Materials, lower, plain, typedWithoutAccents);
        AddMatches(vocabulary.Colors, intent.Colors, lower, plain, typedWithoutAccents);
        return intent;
    }

    // ------------------------------------------------------------------ money

    /// <summary>Reads the budget and returns the text with the amounts blanked out.</summary>
    private static string ParseBudget(string plain, ShoppingIntent intent)
    {
        // "từ 5 đến 8 triệu", "5 - 8tr", "500k - 1 triệu"
        var range = RangeRegex().Matches(plain).LastOrDefault();
        if (range is not null && TryNumber(range.Groups["a"].Value, out var a) && TryNumber(range.Groups["b"].Value, out var b))
        {
            var bUnit = range.Groups["bu"].Value;
            var aUnit = range.Groups["au"].Length > 0 ? range.Groups["au"].Value : bUnit;
            var aValue = a * Multiplier(aUnit);
            var bValue = b * Multiplier(bUnit);
            intent.MinBudget = Math.Min(aValue, bValue);
            intent.MaxBudget = Math.Max(aValue, bValue);
            return Blank(plain, range.Index, range.Length);
        }

        var amounts = new List<(int Start, int Length, decimal Value)>();
        foreach (Match m in MoneyUnitRegex().Matches(plain))
        {
            if (!TryNumber(m.Groups["n"].Value, out var number)) continue;
            var unit = m.Groups["u"].Value;
            var value = number * Multiplier(unit);
            var tail = m.Groups["t"].Value;
            if (tail.Length > 0 && unit is "trieu" or "tr" or "cu")
            {
                // "12tr5" = 12.500.000, "10 triệu 500" = 10.500.000
                value += int.Parse(tail, CultureInfo.InvariantCulture) * (decimal)Math.Pow(10, 6 - tail.Length);
            }

            amounts.Add((m.Index, m.Length, value));
        }

        foreach (Match m in PlainAmountRegex().Matches(plain))
        {
            var overlaps = amounts.Any(x => m.Index < x.Start + x.Length && x.Start < m.Index + m.Length);
            if (!overlaps && TryNumber(m.Groups["n"].Value, out var value) && value >= 100_000)
            {
                amounts.Add((m.Index, m.Length, value));
            }
        }

        if (amounts.Count == 0)
        {
            return plain;
        }

        var rest = plain;
        foreach (var x in amounts)
        {
            rest = Blank(rest, x.Start, x.Length);
        }

        var last = amounts.OrderBy(x => x.Start).Last();
        var before = plain[Math.Max(0, last.Start - 20)..last.Start];
        if (MaxQualifierRegex().IsMatch(before))
        {
            intent.MaxBudget = last.Value;
        }
        else if (MinQualifierRegex().IsMatch(before))
        {
            intent.MinBudget = last.Value;
        }
        else
        {
            intent.MaxBudget = last.Value;
            intent.BudgetIsApproximate = true;
        }

        return rest;
    }

    private static decimal Multiplier(string unit) => unit switch
    {
        "ty" => 1_000_000_000m,
        "trieu" or "tr" or "cu" => 1_000_000m,
        "k" or "nghin" or "ngan" => 1_000m,
        _ => 1m
    };

    private static string Blank(string text, int start, int length) =>
        text[..start] + new string(' ', length) + text[(start + length)..];

    /// <summary>"1.5" / "1,5" → 1.5; "12.500.000" / "12,500,000" → 12500000.</summary>
    private static bool TryNumber(string raw, out decimal value)
    {
        var text = ThousandsRegex().IsMatch(raw)
            ? raw.Replace(".", string.Empty).Replace(",", string.Empty)
            : raw.Replace(',', '.');
        return decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value);
    }

    // ------------------------------------------------------------------ sizes & people

    private static string ParseArea(string plain, ShoppingIntent intent)
    {
        var m = AreaRegex().Match(plain);
        if (!m.Success)
        {
            return plain;
        }

        // "dài 2m2" is a length (2,2 m), not an area.
        var sizeContext = SizeWordBeforeRegex().IsMatch(plain[Math.Max(0, m.Index - 10)..m.Index]);
        if (!sizeContext && TryNumber(m.Groups["n"].Value, out var area) && area is >= 4 and < 1000)
        {
            intent.RoomAreaM2 = area;
            return Blank(plain, m.Index, m.Length);
        }

        return plain;
    }

    private static void ParseDimensions(string plain, ShoppingIntent intent)
    {
        var box = BoxRegex().Match(plain);
        if (box.Success)
        {
            var values = new[] { box.Groups["a"].Value, box.Groups["b"].Value, box.Groups["c"].Value }
                .Where(v => v.Length > 0).Select(v => int.Parse(v, CultureInfo.InvariantCulture)).ToArray();
            var factor = values.Max() < 400 ? 10 : 1; // "180x90x75" is in cm
            intent.LengthMm = values[0] * factor;
            intent.WidthMm = values.Length > 1 ? values[1] * factor : null;
            intent.HeightMm = values.Length > 2 ? values[2] * factor : null;
            return;
        }

        foreach (Match m in LengthRegex().Matches(plain))
        {
            int? mm = null;
            if (m.Groups["m"].Length > 0)
            {
                // "1m8" → 1800, "1m45" → 1450
                var meters = int.Parse(m.Groups["m"].Value, CultureInfo.InvariantCulture);
                var dec = m.Groups["md"].Value;
                mm = meters * 1000 + int.Parse(dec, CultureInfo.InvariantCulture) * (dec.Length == 1 ? 100 : 10);
            }
            else if (TryNumber(m.Groups["n"].Value, out var number))
            {
                mm = m.Groups["u"].Value switch
                {
                    "cm" => (int)(number * 10),
                    "mm" => (int)number,
                    "m" or "met" => (int)(number * 1000),
                    _ => null
                };
            }

            if (mm is null or < 100 or > 6000) continue;

            var before = plain[Math.Max(0, m.Index - 12)..m.Index];
            if (WidthWordRegex().IsMatch(before)) intent.WidthMm ??= mm;
            else if (HeightWordRegex().IsMatch(before)) intent.HeightMm ??= mm;
            else intent.LengthMm ??= mm;
        }
    }

    private static void ParsePeople(string plain, ShoppingIntent intent)
    {
        var m = PeopleRegex().Match(plain);
        if (m.Success && int.TryParse(m.Groups["n"].Value, CultureInfo.InvariantCulture, out var people) && people is > 0 and <= 20)
        {
            intent.People = people;
        }
    }

    // ------------------------------------------------------------------ vocabulary matching

    private static void AddMatches(IReadOnlyList<CatalogVocabulary.Term> terms, List<(string Slug, string Name)> target, string lower, string plain, bool unaccented)
    {
        foreach (var term in terms)
        {
            if (term.Phrases.Any(p => Contains(lower, plain, p, unaccented)))
            {
                target.Add((term.Slug, term.Name));
            }
        }
    }

    private static bool Contains(string lower, string plain, string phrase, bool unaccented)
    {
        if (WordRegex(phrase).IsMatch(lower)) return true;
        if (!unaccented) return false;

        var plainPhrase = Plain(phrase);
        return IsSafeWithoutAccents(plainPhrase) && WordRegex(plainPhrase).IsMatch(plain);
    }

    private static bool TryConsume(ref (string Lower, string Plain) text, string phrase, bool unaccented)
    {
        var accented = WordRegex(phrase);
        if (accented.IsMatch(text.Lower))
        {
            text = (accented.Replace(text.Lower, m => new string(' ', m.Length)), text.Plain);
            return true;
        }

        var plainPhrase = Plain(phrase);
        if (unaccented && IsSafeWithoutAccents(plainPhrase))
        {
            var regex = WordRegex(plainPhrase);
            if (regex.IsMatch(text.Plain))
            {
                text = (text.Lower, regex.Replace(text.Plain, m => new string(' ', m.Length)));
                return true;
            }
        }

        return false;
    }

    // Without accents some single words are ambiguous (đen/đến, trắng/trang, sồi/sợi, mây/máy, nâu/nấu...).
    private static readonly HashSet<string> AmbiguousPlainWords =
        ["den", "trang", "soi", "may", "tre", "da", "sat", "be", "vang", "hong", "xanh", "go", "moc", "cao", "nau", "ghi", "ban", "tu", "ke", "thong", "luoi", "kinh", "mot", "giuong"];

    private static bool IsSafeWithoutAccents(string plainPhrase) =>
        plainPhrase.Contains(' ') || !AmbiguousPlainWords.Contains(plainPhrase);

    private static Regex WordRegex(string phrase) =>
        new($@"(?<![\p{{L}}\d]){Regex.Escape(phrase)}(?![\p{{L}}\d])", RegexOptions.CultureInvariant);

    /// <summary>Lowercase, no accents ("đ" → "d"), single spaces; digits and . , x - kept for amounts and sizes.</summary>
    public static string Plain(string text) => Collapse(SlugGenerator.RemoveDiacritics(text.ToLowerInvariant()));

    private static string Collapse(string text)
    {
        var sb = new StringBuilder(text.Length);
        var space = false;
        foreach (var ch in text)
        {
            if (char.IsWhiteSpace(ch))
            {
                if (!space) sb.Append(' ');
                space = true;
            }
            else
            {
                sb.Append(ch);
                space = false;
            }
        }

        return sb.ToString().Trim();
    }

    private static bool HasVietnameseAccents(string text) => text.Any(ch => ch > 127 && char.IsLetter(ch));

    [GeneratedRegex(@"(?<![\d.,])(?<a>\d+(?:[.,]\d+)?)\s*(?<au>trieu|tr|k|nghin|ngan)?\s*(?:-|–|~|den|toi)\s*(?<b>\d+(?:[.,]\d+)?)\s*(?<bu>ty|trieu|tr|cu|k|nghin|ngan)(?![\p{L}\d])", RegexOptions.CultureInvariant)]
    private static partial Regex RangeRegex();

    [GeneratedRegex(@"(?<![\d.,])(?<n>\d+(?:[.,]\d+)*)\s*(?<u>ty|trieu|tr|cu|k|nghin|ngan|dong|vnd|d)(?:\s*(?<t>\d{1,3})(?![\d.,])(?!\s*(?:k|nghin|ngan|d|dong|vnd|cm|mm|m|met|nguoi|cho|ghe)(?![\p{L}])))?(?![\p{L}\d])", RegexOptions.CultureInvariant)]
    private static partial Regex MoneyUnitRegex();

    [GeneratedRegex(@"(?<![\d.,])(?<n>\d{1,3}(?:[.,]\d{3})+|\d{6,})(?![\d.,])(?!\s*(?:cm|mm|m|met|x)(?![\p{L}]))", RegexOptions.CultureInvariant)]
    private static partial Regex PlainAmountRegex();

    [GeneratedRegex(@"\b(duoi|toi da|khong qua|khong vuot qua|max|it hon|<|<=)\s*(?:khoang\s*)?$")]
    private static partial Regex MaxQualifierRegex();

    [GeneratedRegex(@"\b(tren|toi thieu|it nhat|hon|>|>=)\s*$")]
    private static partial Regex MinQualifierRegex();

    [GeneratedRegex(@"^\d{1,3}([.,]\d{3})+$")]
    private static partial Regex ThousandsRegex();

    [GeneratedRegex(@"(?<n>\d+(?:[.,]\d+)?)\s*(?:m2|m²|met vuong|m vuong)(?![\p{L}\d])")]
    private static partial Regex AreaRegex();

    [GeneratedRegex(@"\b(dai|rong|cao|sau|ngang)\s*$")]
    private static partial Regex SizeWordBeforeRegex();

    [GeneratedRegex(@"\b(rong|sau|ngang)\s*(khoang\s*)?$")]
    private static partial Regex WidthWordRegex();

    [GeneratedRegex(@"\bcao\s*(khoang\s*)?$")]
    private static partial Regex HeightWordRegex();

    [GeneratedRegex(@"(?<![\d.])(?<a>\d{2,4})\s*[x×*]\s*(?<b>\d{2,4})(?:\s*[x×*]\s*(?<c>\d{2,4}))?(?![\d])")]
    private static partial Regex BoxRegex();

    [GeneratedRegex(@"(?<![\d.,])(?:(?<m>\d)m(?<md>\d{1,2})(?![\d\p{L}])|(?<n>\d+(?:[.,]\d+)?)\s*(?<u>cm|mm|met|m)(?![\p{L}\d²]))")]
    private static partial Regex LengthRegex();

    [GeneratedRegex(@"(?<n>\d{1,2})\s*(?:nguoi|ghe|cho ngoi|cho|thanh vien)(?![\p{L}])")]
    private static partial Regex PeopleRegex();
}
