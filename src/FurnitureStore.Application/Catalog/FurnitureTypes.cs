using FurnitureStore.Domain.Enums;

namespace FurnitureStore.Application.Catalog;

/// <summary>Vietnamese labels and URL keys of <see cref="FurnitureType"/>.</summary>
public static class FurnitureTypes
{
    public static readonly IReadOnlyList<(FurnitureType Type, string Key, string Name)> All =
    [
        (FurnitureType.Table, "ban", "Bàn"),
        (FurnitureType.Chair, "ghe", "Ghế"),
        (FurnitureType.Sofa, "sofa", "Sofa"),
        (FurnitureType.Bed, "giuong", "Giường"),
        (FurnitureType.Cabinet, "tu", "Tủ"),
        (FurnitureType.Shelf, "ke", "Kệ"),
        (FurnitureType.Decor, "trang-tri", "Trang trí"),
        (FurnitureType.Other, "khac", "Khác")
    ];

    public static string NameOf(FurnitureType type) => All.FirstOrDefault(t => t.Type == type).Name ?? type.ToString();

    public static string KeyOf(FurnitureType type) => All.FirstOrDefault(t => t.Type == type).Key ?? type.ToString().ToLowerInvariant();

    public static FurnitureType? FromKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return null;
        foreach (var item in All)
        {
            if (string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase)
                || string.Equals(item.Type.ToString(), key, StringComparison.OrdinalIgnoreCase))
            {
                return item.Type;
            }
        }

        return null;
    }
}
