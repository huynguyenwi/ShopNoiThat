namespace FurnitureStore.Application.Sales;

/// <summary>
/// The 34 provincial-level units in effect since 01/07/2025 (Resolution 202/2025/QH15).
/// Since then administration is two-level (province → ward/commune), so District is optional in addresses.
/// </summary>
public static class VietnamProvinces
{
    public static readonly IReadOnlyList<string> All =
    [
        "TP. Hà Nội",
        "TP. Hồ Chí Minh",
        "TP. Hải Phòng",
        "TP. Đà Nẵng",
        "TP. Cần Thơ",
        "TP. Huế",
        "An Giang",
        "Bắc Ninh",
        "Cà Mau",
        "Cao Bằng",
        "Đắk Lắk",
        "Điện Biên",
        "Đồng Nai",
        "Đồng Tháp",
        "Gia Lai",
        "Hà Tĩnh",
        "Hưng Yên",
        "Khánh Hòa",
        "Lai Châu",
        "Lâm Đồng",
        "Lạng Sơn",
        "Lào Cai",
        "Nghệ An",
        "Ninh Bình",
        "Phú Thọ",
        "Quảng Ngãi",
        "Quảng Ninh",
        "Quảng Trị",
        "Sơn La",
        "Tây Ninh",
        "Thái Nguyên",
        "Thanh Hóa",
        "Tuyên Quang",
        "Vĩnh Long"
    ];

    public static bool IsValid(string? province) =>
        !string.IsNullOrWhiteSpace(province) && All.Contains(province.Trim(), StringComparer.OrdinalIgnoreCase);
}
