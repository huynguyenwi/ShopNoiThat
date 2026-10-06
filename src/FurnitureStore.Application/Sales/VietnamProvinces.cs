using System.Globalization;
using System.Text.Json;

namespace FurnitureStore.Application.Sales;

/// <summary>A ward, commune or special zone ("Phường", "Xã", "Đặc khu") of a province.</summary>
/// <param name="ShortName">The name without its type, e.g. "Bến Thành" for "Phường Bến Thành" (for sorting).</param>
/// <param name="Label">Text of a pick list grouped by type: the short name, or the full name when the province has a
/// ward and a commune with the same short name.</param>
public sealed record WardInfo(int Code, string Name, string Type, string ShortName, string Label);

public sealed record ProvinceInfo(int Code, string Name, IReadOnlyList<WardInfo> Wards);

/// <summary>
/// The 34 provincial-level units and their 3,321 wards / communes in effect since 01/07/2025 (Resolution 202/2025/QH15):
/// administration is two-level (province → ward / commune), there are no districts any more.
/// Data: Sales/Data/vietnam-administrative-units.json (embedded), from the open dataset of provinces.open-api.vn (v2,
/// codes of the General Statistics Office). Cities keep the short "TP. " prefix used across the site.
/// </summary>
public static class VietnamProvinces
{
    public static readonly string[] WardTypes = ["Phường", "Xã", "Đặc khu"];

    private static readonly Lazy<Catalog> Data = new(Load);

    private sealed record Catalog(
        IReadOnlyList<ProvinceInfo> Provinces,
        Dictionary<string, ProvinceInfo> ByName,
        Dictionary<int, ProvinceInfo> ByCode,
        Dictionary<int, HashSet<string>> WardNames);

    public static IReadOnlyList<ProvinceInfo> Provinces => Data.Value.Provinces;

    /// <summary>Province names, cities first.</summary>
    public static IReadOnlyList<string> All => Data.Value.Provinces.Select(p => p.Name).ToList();

    public static ProvinceInfo? Find(string? name) =>
        string.IsNullOrWhiteSpace(name) ? null : Data.Value.ByName.GetValueOrDefault(Normalize(name));

    public static ProvinceInfo? FindByCode(int code) => Data.Value.ByCode.GetValueOrDefault(code);

    public static bool IsValid(string? province) => Find(province) is not null;

    /// <summary>Whether <paramref name="ward"/> is a ward / commune of <paramref name="province"/>.</summary>
    public static bool IsValidWard(string? province, string? ward) =>
        Find(province) is { } p && !string.IsNullOrWhiteSpace(ward) && Data.Value.WardNames[p.Code].Contains(Normalize(ward));

    private static string Normalize(string value) => string.Join(' ', value.Split(' ', StringSplitOptions.RemoveEmptyEntries));

    private sealed record ProvinceRow(int Code, string Name, JsonElement[][] Wards);

    private static Catalog Load()
    {
        const string resource = "FurnitureStore.Application.Sales.Data.vietnam-administrative-units.json";
        using var stream = typeof(VietnamProvinces).Assembly.GetManifestResourceStream(resource)
                           ?? throw new InvalidOperationException($"Embedded resource {resource} is missing.");
        var rows = JsonSerializer.Deserialize<ProvinceRow[]>(stream, new JsonSerializerOptions(JsonSerializerDefaults.Web))
                   ?? throw new InvalidOperationException($"Embedded resource {resource} is empty.");

        var collation = StringComparer.Create(CultureInfo.GetCultureInfo("vi-VN"), ignoreCase: true);
        var provinces = rows.Select(row => new ProvinceInfo(row.Code, row.Name, WithLabels(row.Wards
                .Select(w => ToWard(w[0].GetInt32(), w[1].GetString()!))
                .OrderBy(w => Array.IndexOf(WardTypes, w.Type))
                .ThenBy(w => w.ShortName, collation)
                .ToList())))
            .ToList();

        return new Catalog(
            provinces,
            provinces.ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase),
            provinces.ToDictionary(p => p.Code),
            provinces.ToDictionary(p => p.Code, p => p.Wards.Select(w => w.Name).ToHashSet(StringComparer.OrdinalIgnoreCase)));
    }

    private static WardInfo ToWard(int code, string name)
    {
        var type = WardTypes.First(t => name.StartsWith(t + " ", StringComparison.Ordinal));
        var shortName = name[(type.Length + 1)..];
        return new WardInfo(code, name, type, shortName, shortName);
    }

    private static IReadOnlyList<WardInfo> WithLabels(List<WardInfo> wards)
    {
        var repeated = wards.GroupBy(w => w.ShortName, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return wards.Select(w => repeated.Contains(w.ShortName) ? w with { Label = w.Name } : w).ToList();
    }
}
