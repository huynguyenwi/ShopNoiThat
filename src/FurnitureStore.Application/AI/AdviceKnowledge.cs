namespace FurnitureStore.Application.AI;

/// <summary>
/// Interior-design know-how used by the assistant: palettes per wall tone and profiles of the 8 styles sold by the shop.
/// It guides the AI model, and is the whole answer when no AI provider is configured. Colors and styles are referenced by
/// the catalog slugs so every suggestion can be turned into real products.
/// </summary>
public static class AdviceKnowledge
{
    public sealed record StyleProfile(
        string Slug,
        string Name,
        string Description,
        IReadOnlyList<string> KeyPoints,
        IReadOnlyList<string> ColorSlugs,
        IReadOnlyList<string> MaterialSlugs,
        int BudgetLevel, // 1 = tiết kiệm, 2 = trung bình, 3 = cao
        IReadOnlyList<string> GoodFor);

    public static readonly IReadOnlyList<StyleProfile> Styles =
    [
        new("hien-dai", "Hiện đại", "Đường nét gọn gàng, phối nhiều vật liệu, ưu tiên công năng.",
            ["Đường thẳng, bề mặt phẳng", "Phối gỗ với kim loại, kính, đá", "Tông trung tính điểm màu nhấn"],
            ["xam", "trang", "den", "nau-oc-cho", "xanh-navy"], ["mdf-chong-am", "thep-son-tinh-dien", "kinh-cuong-luc", "da-pu"], 2,
            ["gia đình trẻ", "căn hộ chung cư", "dễ vệ sinh"]),
        new("toi-gian", "Tối giản", "Ít chi tiết, màu trung tính, tối ưu không gian.",
            ["Ít đồ nhưng chất lượng", "Màu trắng, kem, gỗ sáng", "Tủ kệ đóng kín, gọn gàng"],
            ["trang", "kem", "soi-tu-nhien", "xam"], ["go-soi", "mdf-chong-am", "vai-bo"], 1,
            ["phòng nhỏ", "làm việc tập trung", "tiết kiệm"]),
        new("co-dien", "Cổ điển", "Chi tiết tinh xảo, gỗ màu trầm, ấm cúng.",
            ["Gỗ tự nhiên màu trầm", "Đường cong, phào chỉ", "Vải nhung, da"],
            ["nau-oc-cho", "nau-cognac", "kem"], ["go-oc-cho", "vai-nhung", "da-that"], 3,
            ["không gian rộng", "tiếp khách", "gia đình nhiều thế hệ"]),
        new("sang-trong", "Sang trọng", "Vật liệu cao cấp như da thật, đá marble, gỗ óc chó.",
            ["Da thật, đá marble, gỗ óc chó", "Tông trầm kết hợp ánh kim", "Ít món nhưng nổi bật"],
            ["nau-oc-cho", "den", "trang-van-da", "nau-cognac", "xanh-navy"], ["go-oc-cho", "da-that", "da-marble"], 3,
            ["không gian rộng", "tiếp khách", "ngân sách cao"]),
        new("cong-nghiep", "Công nghiệp", "Kết hợp gỗ và kim loại, tông màu tối, cá tính.",
            ["Chân sắt sơn tĩnh điện", "Gỗ vân rõ, màu trầm", "Tông đen, xám, nâu"],
            ["den", "xam", "nau-oc-cho", "go-tu-nhien"], ["thep-son-tinh-dien", "go-thong", "go-cao-su"], 1,
            ["cá tính", "góc làm việc", "căn hộ loft"]),
        new("bac-au", "Bắc Âu", "Gỗ sáng màu, đường cong mềm, không gian sáng và ấm.",
            ["Gỗ sồi, tần bì sáng màu", "Vải bố, len màu pastel", "Đường cong mềm, chân côn"],
            ["soi-tu-nhien", "trang", "kem", "xam", "xanh-duong-nhat"], ["go-soi", "go-tan-bi", "vai-bo"], 2,
            ["gia đình có trẻ nhỏ", "phòng nhỏ", "căn hộ chung cư"]),
        new("japandi", "Japandi", "Giao thoa Nhật Bản và Bắc Âu: mộc, thấp, tĩnh lặng.",
            ["Nội thất thấp, gọn", "Gỗ tự nhiên, mây tre", "Tông đất, be, xanh rêu"],
            ["soi-tu-nhien", "go-tu-nhien", "kem", "xanh-reu"], ["go-soi", "go-tan-bi", "may-tre", "vai-bo"], 2,
            ["thư giãn", "phòng ngủ", "yêu thiên nhiên"]),
        new("moc-mac", "Mộc mạc", "Gỗ thô, mây tre, gần gũi thiên nhiên.",
            ["Gỗ thông, cao su vân mắt", "Mây tre đan", "Tông gỗ ấm, xanh rêu"],
            ["go-tu-nhien", "soi-tu-nhien", "xanh-reu", "kem"], ["go-thong", "go-cao-su", "may-tre"], 1,
            ["yêu thiên nhiên", "nhà phố, nhà vườn", "tiết kiệm"])
    ];

    public enum WallTone { Light, Gray, Dark, Colorful, Wood }

    /// <summary>Wall tone from the colors the customer mentioned (catalog slugs).</summary>
    public static WallTone ToneOf(IReadOnlyCollection<string> colorSlugs, string plainText)
    {
        if (colorSlugs.Any(s => s is "den" or "xanh-navy") || plainText.Contains("toi mau") || plainText.Contains("dam")) return WallTone.Dark;
        if (colorSlugs.Any(s => s is "xam")) return WallTone.Gray;
        if (colorSlugs.Any(s => s is "xanh-reu" or "xanh-duong-nhat" or "vang-mu-tat" or "hong-dat")) return WallTone.Colorful;
        if (colorSlugs.Any(s => s is "nau-oc-cho" or "go-tu-nhien" or "soi-tu-nhien" or "van-soi" or "nau-cognac")) return WallTone.Wood;
        return WallTone.Light;
    }

    public sealed record PaletteEntry(string Furniture, IReadOnlyList<(string Slug, string Reason)> Colors);

    /// <summary>Recommended furniture colors per wall tone.</summary>
    public static IReadOnlyList<PaletteEntry> PaletteFor(WallTone tone) => tone switch
    {
        WallTone.Gray =>
        [
            new("Gỗ chủ đạo", [("nau-oc-cho", "Óc chó trầm ấm cân bằng tường xám lạnh"), ("soi-tu-nhien", "Gỗ sồi sáng giúp phòng xám bớt nặng")]),
            new("Bàn", [("nau-oc-cho", "Mặt gỗ tối tạo điểm nhấn sang"), ("trang-van-da", "Mặt đá vân trắng hợp tông xám hiện đại")]),
            new("Ghế", [("den", "Ghế đen tạo tương phản gọn gàng"), ("kem", "Nệm kem làm mềm không gian")]),
            new("Sofa", [("kem", "Sofa kem nổi bật trên nền xám"), ("xanh-navy", "Xanh navy sâu, hợp tường xám"), ("nau-cognac", "Da cognac thêm ấm áp")]),
            new("Tủ", [("trang", "Tủ trắng giữ phòng sáng"), ("van-soi", "Vân sồi thêm chất liệu tự nhiên")])
        ],
        WallTone.Dark =>
        [
            new("Gỗ chủ đạo", [("soi-tu-nhien", "Gỗ sáng tương phản với tường tối, phòng không bị bí"), ("go-tu-nhien", "Màu gỗ ấm làm dịu tông tối")]),
            new("Bàn", [("soi-tu-nhien", "Mặt gỗ sáng nổi trên nền tối"), ("trang-van-da", "Mặt đá sáng tăng độ phản chiếu ánh sáng")]),
            new("Ghế", [("kem", "Ghế kem nhẹ nhàng, dễ phối"), ("vang-mu-tat", "Vàng mù tạt làm điểm nhấn ấm")]),
            new("Sofa", [("kem", "Sofa sáng giúp phòng tối thoáng hơn"), ("nau-cognac", "Da cognac sang trọng trên nền tối")]),
            new("Tủ", [("soi-tu-nhien", "Tủ gỗ sáng giảm cảm giác nặng nề"), ("trang", "Tủ trắng phản sáng tốt")])
        ],
        WallTone.Colorful =>
        [
            new("Gỗ chủ đạo", [("soi-tu-nhien", "Gỗ sáng trung tính không 'đánh nhau' với tường màu"), ("go-tu-nhien", "Màu gỗ ấm hợp hầu hết tường màu")]),
            new("Bàn", [("soi-tu-nhien", "Gỗ sáng giữ tổng thể hài hòa"), ("trang", "Bàn trắng để tường màu làm nhân vật chính")]),
            new("Ghế", [("trang", "Ghế trắng sạch sẽ, không rối mắt"), ("kem", "Kem be nhẹ nhàng")]),
            new("Sofa", [("kem", "Sofa kem trung tính dễ phối tường màu"), ("xam", "Xám nhạt giữ cân bằng")]),
            new("Tủ", [("trang", "Tủ trắng gọn gàng"), ("soi-tu-nhien", "Gỗ sồi tự nhiên, ấm áp")])
        ],
        WallTone.Wood =>
        [
            new("Gỗ chủ đạo", [("soi-tu-nhien", "Chọn tông gỗ khác biệt với tường để có chiều sâu"), ("nau-oc-cho", "Óc chó đậm tạo tương phản với ốp gỗ sáng")]),
            new("Bàn", [("trang-van-da", "Mặt đá phá vỡ sự đơn điệu của gỗ"), ("den", "Bàn đen tạo điểm nhấn")]),
            new("Ghế", [("kem", "Nệm kem mềm mại"), ("xanh-reu", "Xanh rêu gần gũi thiên nhiên")]),
            new("Sofa", [("kem", "Sofa kem thanh lịch"), ("xanh-reu", "Xanh rêu hài hòa với gỗ"), ("xam", "Xám trung tính")]),
            new("Tủ", [("trang", "Tủ trắng giúp không gian bớt 'toàn gỗ'"), ("van-soi", "Vân sồi đồng bộ nhẹ nhàng")])
        ],
        _ =>
        [
            new("Gỗ chủ đạo", [("soi-tu-nhien", "Gỗ sồi sáng giữ căn phòng thoáng, rộng"), ("nau-oc-cho", "Óc chó tạo điểm nhấn ấm, sang trên nền trắng")]),
            new("Bàn", [("soi-tu-nhien", "Bàn gỗ sáng tự nhiên, dễ phối"), ("nau-oc-cho", "Bàn óc chó nổi bật trên tường trắng"), ("trang-van-da", "Mặt đá vân trắng hiện đại")]),
            new("Ghế", [("kem", "Ghế kem be ấm áp"), ("soi-tu-nhien", "Ghế gỗ đồng bộ với bàn"), ("xam", "Ghế xám trung tính")]),
            new("Sofa", [("xam", "Sofa xám an toàn, bền màu"), ("kem", "Kem be tạo cảm giác ấm"), ("xanh-reu", "Xanh rêu tạo điểm nhấn tự nhiên"), ("xanh-navy", "Xanh navy nổi bật, sang")]),
            new("Tủ", [("trang", "Tủ trắng hòa vào tường, phòng rộng hơn"), ("soi-tu-nhien", "Gỗ sồi tự nhiên ấm áp"), ("van-soi", "Vân sồi dễ chăm sóc")])
        ]
    };

    /// <summary>Furniture groups of the palette and the words customers use for them (unaccented).</summary>
    public static readonly IReadOnlyDictionary<string, string[]> FurnitureWords = new Dictionary<string, string[]>
    {
        ["Gỗ chủ đạo"] = ["mau go", "tong go", "go"],
        ["Bàn"] = ["ban an", "ban tra", "ban lam viec", "ban"],
        ["Ghế"] = ["ghe"],
        ["Sofa"] = ["sofa", "so pha"],
        ["Tủ"] = ["tu quan ao", "tu giay", "tu", "ke"]
    };

    /// <summary>Catalog categories that match a palette furniture group (used to fetch example products).</summary>
    public static readonly IReadOnlyDictionary<string, string[]> FurnitureCategories = new Dictionary<string, string[]>
    {
        ["Bàn"] = ["ban-an", "ban-tra", "ban-lam-viec"],
        ["Ghế"] = ["ghe-an", "ghe-thu-gian", "ghe-van-phong"],
        ["Sofa"] = ["sofa"],
        ["Tủ"] = ["tu-quan-ao", "ke-tivi", "tu-giay", "ke-sach"],
        ["Gỗ chủ đạo"] = []
    };
}
