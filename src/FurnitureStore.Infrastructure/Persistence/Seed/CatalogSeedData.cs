using FurnitureStore.Domain.Enums;

namespace FurnitureStore.Infrastructure.Persistence.Seed;

internal sealed record CategorySeed(string Slug, string Name, string? ParentSlug, int Order, string Description, string? Icon = null, bool ShowOnHome = false);

internal sealed record ColorSeed(string Slug, string Name, string Hex);

internal sealed record MaterialSeed(string Slug, string Name, MaterialGroup Group, string Description);

internal sealed record StyleSeed(string Code, string Slug, string Name, string Description);

internal sealed record SizeSeed(string Slug, string Name, int LengthMm, int WidthMm, int HeightMm, FurnitureType? Type);

/// <summary>A selectable option (material / color / size) and the price difference it adds to the base price.</summary>
internal sealed record OptionSeed(string Slug, decimal PriceDelta = 0);

/// <summary>A secondary (non-selectable) material or color describing a component, e.g. oak frame.</summary>
internal sealed record PartSeed(string Slug, string Part);

internal sealed record ProductSeed
{
    public required string Sku { get; init; }
    public required string Name { get; init; }
    public required string CategorySlug { get; init; }
    public required FurnitureType Type { get; init; }
    public required string StyleCode { get; init; }
    public required string Shape { get; init; }
    public required decimal BasePrice { get; init; }
    public int SalePercent { get; init; }
    public required string ShortDescription { get; init; }
    public required string Description { get; init; }
    public required int LengthMm { get; init; }
    public required int WidthMm { get; init; }
    public required int HeightMm { get; init; }
    public required decimal WeightKg { get; init; }
    public int WarrantyMonths { get; init; } = 12;
    public bool Featured { get; init; }
    public int Sold { get; init; }
    public required OptionSeed[] Materials { get; init; }
    public required OptionSeed[] Colors { get; init; }
    public OptionSeed[] Sizes { get; init; } = [];
    public PartSeed[] SecondaryMaterials { get; init; } = [];
    public PartSeed[] SecondaryColors { get; init; } = [];
}

/// <summary>
/// Demo catalog for local development and testing (search, filters, cart, orders, AI recommendations).
/// Store name, prices and descriptions are fictional.
/// </summary>
internal static class CatalogSeedData
{
    public static readonly CategorySeed[] Categories =
    [
        new("phong-khach", "Phòng khách", null, 1, "Sofa, bàn trà, kệ tivi và ghế thư giãn cho không gian sinh hoạt chung.", "bi-lamp", true),
        new("phong-ngu", "Phòng ngủ", null, 2, "Giường, tủ quần áo, tab đầu giường và bàn trang điểm.", "bi-moon-stars", true),
        new("phong-an", "Phòng ăn", null, 3, "Bàn ăn, ghế ăn, bộ bàn ăn và tủ rượu.", "bi-cup-hot", true),
        new("phong-lam-viec", "Phòng làm việc", null, 4, "Bàn làm việc, ghế văn phòng và kệ sách.", "bi-laptop", true),
        new("trang-tri-tien-ich", "Trang trí & Tiện ích", null, 5, "Tủ giày, kệ trang trí, gương và phụ kiện nội thất.", "bi-flower1"),

        new("sofa", "Sofa", "phong-khach", 1, "Sofa băng, sofa góc, sofa giường bọc vải, nhung và da."),
        new("ban-tra", "Bàn trà", "phong-khach", 2, "Bàn trà gỗ tự nhiên, mặt đá và bàn trà nhiều tầng."),
        new("ke-tivi", "Kệ tivi", "phong-khach", 3, "Kệ tivi đặt sàn và treo tường."),
        new("ghe-thu-gian", "Ghế thư giãn", "phong-khach", 4, "Ghế đọc sách, ghế lounge thư giãn."),
        new("giuong-ngu", "Giường ngủ", "phong-ngu", 1, "Giường gỗ tự nhiên, giường bọc nệm, giường kiểu Nhật."),
        new("tu-quan-ao", "Tủ quần áo", "phong-ngu", 2, "Tủ cánh mở, tủ cửa lùa, tủ âm tường."),
        new("tab-dau-giuong", "Tab đầu giường", "phong-ngu", 3, "Tab đầu giường nhiều ngăn kéo."),
        new("ban-trang-diem", "Bàn trang điểm", "phong-ngu", 4, "Bàn trang điểm kèm gương."),
        new("ban-an", "Bàn ăn", "phong-an", 1, "Bàn ăn gỗ, mặt đá cho gia đình 4 - 8 người."),
        new("ghe-an", "Ghế ăn", "phong-an", 2, "Ghế ăn gỗ, bọc nệm, mây tre."),
        new("bo-ban-an", "Bộ bàn ăn", "phong-an", 3, "Trọn bộ bàn và ghế ăn đồng bộ."),
        new("tu-ruou", "Tủ rượu & Tủ bếp", "phong-an", 4, "Tủ rượu, tủ trưng bày, tủ bếp đứng."),
        new("ban-lam-viec", "Bàn làm việc", "phong-lam-viec", 1, "Bàn làm việc gỗ, chân sắt, bàn nâng hạ."),
        new("ghe-van-phong", "Ghế văn phòng", "phong-lam-viec", 2, "Ghế công thái học và ghế làm việc."),
        new("ke-sach", "Kệ sách", "phong-lam-viec", 3, "Kệ sách gỗ, kệ chân sắt nhiều tầng."),
        new("tu-giay", "Tủ giày", "trang-tri-tien-ich", 1, "Tủ giày cho lối vào."),
        new("ke-trang-tri", "Kệ trang trí", "trang-tri-tien-ich", 2, "Kệ treo tường, kệ góc trang trí."),
        new("guong", "Gương", "trang-tri-tien-ich", 3, "Gương đứng, gương treo khung gỗ.")
    ];

    public static readonly ColorSeed[] Colors =
    [
        new("nau-oc-cho", "Nâu óc chó", "#5C4033"),
        new("go-tu-nhien", "Màu gỗ tự nhiên", "#B98A5A"),
        new("soi-tu-nhien", "Sồi tự nhiên", "#D2B48C"),
        new("van-soi", "Vân sồi", "#C9A57A"),
        new("trang", "Trắng", "#F4F1EA"),
        new("kem", "Kem be", "#E6D8BF"),
        new("xam", "Xám", "#8A8984"),
        new("den", "Đen", "#2B2B2B"),
        new("xanh-reu", "Xanh rêu", "#6B7F59"),
        new("xanh-navy", "Xanh navy", "#26354F"),
        new("vang-mu-tat", "Vàng mù tạt", "#C99A2E"),
        new("hong-dat", "Hồng đất", "#C27C6B"),
        new("nau-cognac", "Nâu cognac", "#9A5B34"),
        new("trang-van-da", "Trắng vân đá", "#E9E6E0"),
        new("xanh-duong-nhat", "Xanh dương nhạt", "#9DB4C8")
    ];

    public static readonly MaterialSeed[] Materials =
    [
        new("go-oc-cho", "Gỗ óc chó", MaterialGroup.NaturalWood, "Gỗ óc chó (Walnut) nhập khẩu, vân đẹp, màu nâu sô-cô-la sang trọng, rất bền."),
        new("go-soi", "Gỗ sồi", MaterialGroup.NaturalWood, "Gỗ sồi trắng (Oak) nhập khẩu, vân thẳng, cứng chắc, ít cong vênh."),
        new("go-tan-bi", "Gỗ tần bì", MaterialGroup.NaturalWood, "Gỗ tần bì (Ash) màu sáng, dẻo dai, phù hợp đồ uốn cong."),
        new("go-cao-su", "Gỗ cao su", MaterialGroup.NaturalWood, "Gỗ cao su ghép đã qua xử lý, giá hợp lý, thân thiện môi trường."),
        new("go-thong", "Gỗ thông", MaterialGroup.NaturalWood, "Gỗ thông nhẹ, vân mắt đặc trưng, phong cách mộc mạc."),
        new("mdf-chong-am", "MDF chống ẩm", MaterialGroup.EngineeredWood, "Ván MDF lõi xanh chống ẩm, phủ melamine chống trầy."),
        new("vai-bo", "Vải bố", MaterialGroup.Fabric, "Vải bố (linen blend) dệt dày, thoáng khí, chống bám bụi."),
        new("vai-nhung", "Vải nhung", MaterialGroup.Fabric, "Vải nhung mềm mịn, màu sắc sang trọng, chống thấm nhẹ."),
        new("vai-luoi", "Vải lưới", MaterialGroup.Fabric, "Lưới đàn hồi thoáng khí dùng cho ghế công thái học."),
        new("da-that", "Da bò thật", MaterialGroup.Leather, "Da bò thật nhập khẩu, càng dùng càng lên màu đẹp."),
        new("da-pu", "Da PU cao cấp", MaterialGroup.Leather, "Da PU cao cấp, dễ vệ sinh, chống nứt nẻ."),
        new("thep-son-tinh-dien", "Thép sơn tĩnh điện", MaterialGroup.Metal, "Thép hộp sơn tĩnh điện chống gỉ."),
        new("da-marble", "Đá marble", MaterialGroup.Stone, "Đá marble tự nhiên, vân mây độc bản."),
        new("da-ceramic", "Đá ceramic", MaterialGroup.Stone, "Đá ceramic chịu nhiệt, chống trầy xước, chống thấm."),
        new("kinh-cuong-luc", "Kính cường lực", MaterialGroup.Glass, "Kính cường lực an toàn, dày 5 - 8 mm."),
        new("may-tre", "Mây tre đan", MaterialGroup.Rattan, "Mây tự nhiên đan thủ công, nhẹ và thoáng.")
    ];

    public static readonly StyleSeed[] Styles =
    [
        new("Modern", "hien-dai", "Hiện đại", "Đường nét gọn gàng, phối vật liệu đa dạng, công năng cao."),
        new("Minimalist", "toi-gian", "Tối giản", "Ít chi tiết, màu trung tính, tối ưu không gian."),
        new("Classic", "co-dien", "Cổ điển", "Chi tiết tinh xảo, gỗ màu trầm, cảm giác ấm cúng lâu đời."),
        new("Luxury", "sang-trong", "Sang trọng", "Vật liệu cao cấp như da thật, đá marble, gỗ óc chó."),
        new("Industrial", "cong-nghiep", "Công nghiệp", "Kết hợp gỗ và kim loại, tông màu tối, cá tính."),
        new("Scandinavian", "bac-au", "Bắc Âu", "Gỗ sáng màu, đường cong mềm, không gian sáng và ấm."),
        new("Japandi", "japandi", "Japandi", "Giao thoa Nhật Bản và Bắc Âu: mộc, thấp, tĩnh lặng."),
        new("Rustic", "moc-mac", "Mộc mạc", "Gỗ thô, mây tre, gần gũi thiên nhiên.")
    ];

    public static readonly SizeSeed[] Sizes =
    [
        new("sofa-2-cho-160", "2 chỗ - 1m6", 1600, 850, 800, FurnitureType.Sofa),
        new("sofa-3-cho-210", "3 chỗ - 2m1", 2100, 850, 800, FurnitureType.Sofa),
        new("sofa-3-cho-240", "3 chỗ - 2m4", 2400, 900, 800, FurnitureType.Sofa),
        new("sofa-goc-280", "Góc L - 2m8 x 1m7", 2800, 1700, 800, FurnitureType.Sofa),
        new("sofa-goc-320", "Góc L - 3m2 x 1m8", 3200, 1800, 800, FurnitureType.Sofa),
        new("sofa-giuong-190", "Sofa giường - 1m9", 1900, 900, 850, FurnitureType.Sofa),
        new("ban-tra-tron-80", "Tròn Ø80cm", 800, 800, 420, FurnitureType.Table),
        new("ban-tra-tron-100", "Tròn Ø100cm", 1000, 1000, 420, FurnitureType.Table),
        new("ban-tra-100", "1m x 55cm", 1000, 550, 450, FurnitureType.Table),
        new("ban-tra-120", "1m2 x 60cm", 1200, 600, 420, FurnitureType.Table),
        new("ke-tivi-160", "Dài 1m6", 1600, 400, 500, FurnitureType.Shelf),
        new("ke-tivi-180", "Dài 1m8", 1800, 400, 500, FurnitureType.Shelf),
        new("ke-tivi-200", "Dài 2m", 2000, 400, 500, FurnitureType.Shelf),
        new("giuong-140", "1m4 x 2m", 2000, 1400, 1000, FurnitureType.Bed),
        new("giuong-160", "1m6 x 2m", 2000, 1600, 1000, FurnitureType.Bed),
        new("giuong-180", "1m8 x 2m", 2000, 1800, 1000, FurnitureType.Bed),
        new("tu-3-canh-120", "3 cánh - 1m2", 1200, 600, 2000, FurnitureType.Cabinet),
        new("tu-4-canh-160", "4 cánh - 1m6", 1600, 600, 2000, FurnitureType.Cabinet),
        new("tu-lua-180", "Cửa lùa - 1m8", 1800, 600, 2200, FurnitureType.Cabinet),
        new("tu-lua-240", "Cửa lùa - 2m4", 2400, 600, 2200, FurnitureType.Cabinet),
        new("ban-an-140", "1m4 (4 người)", 1400, 800, 750, FurnitureType.Table),
        new("ban-an-160", "1m6 (6 người)", 1600, 850, 750, FurnitureType.Table),
        new("ban-an-180", "1m8 (6 - 8 người)", 1800, 900, 750, FurnitureType.Table),
        new("ban-an-200", "2m (8 người)", 2000, 950, 750, FurnitureType.Table),
        new("ban-an-tron-120", "Tròn Ø1m2 (6 người)", 1200, 1200, 750, FurnitureType.Table),
        new("ban-lv-120", "1m2 x 60cm", 1200, 600, 750, FurnitureType.Table),
        new("ban-lv-140", "1m4 x 70cm", 1400, 700, 750, FurnitureType.Table),
        new("ban-lv-160", "1m6 x 80cm", 1600, 800, 750, FurnitureType.Table)
    ];

    public static readonly ProductSeed[] Products =
    [
        // ---------------- Phòng khách ----------------
        new()
        {
            Sku = "SF-OSLO", Name = "Sofa băng 3 chỗ Oslo khung gỗ sồi", CategorySlug = "sofa", Type = FurnitureType.Sofa,
            StyleCode = "Scandinavian", Shape = "sofa", BasePrice = 16_900_000, Featured = true, Sold = 86, WarrantyMonths = 24,
            LengthMm = 2100, WidthMm = 850, HeightMm = 800, WeightKg = 58,
            ShortDescription = "Sofa 3 chỗ phong cách Bắc Âu, khung gỗ sồi tự nhiên, đệm mút D40 bọc vải bố tháo giặt được.",
            Description = "Sofa Oslo mang tinh thần tối giản Bắc Âu với tay vịn bo tròn và chân gỗ sồi vát côn.\n" +
                          "Khung làm từ gỗ sồi sấy đạt độ ẩm 10 - 12%, liên kết mộng chắc chắn, không ọp ẹp sau nhiều năm sử dụng.\n" +
                          "Đệm ngồi mút D40 kết hợp lò xo túi cho độ êm vừa phải; vỏ vải bố dệt dày có khóa kéo để tháo giặt.",
            Materials = [new("vai-bo")],
            Colors = [new("kem"), new("xam"), new("xanh-reu", 500_000)],
            Sizes = [new("sofa-3-cho-210"), new("sofa-3-cho-240", 2_000_000)],
            SecondaryMaterials = [new("go-soi", "Khung & chân")]
        },
        new()
        {
            Sku = "SF-MILANO", Name = "Sofa góc chữ L Milano da bò thật", CategorySlug = "sofa", Type = FurnitureType.Sofa,
            StyleCode = "Luxury", Shape = "sofa", BasePrice = 42_000_000, Featured = true, Sold = 24, WarrantyMonths = 36,
            LengthMm = 2800, WidthMm = 1700, HeightMm = 800, WeightKg = 95,
            ShortDescription = "Sofa góc L bọc da bò thật nhập khẩu, đệm lông vũ kết hợp mút cao cấp, phù hợp phòng khách rộng.",
            Description = "Milano là mẫu sofa góc dành cho phòng khách từ 25m² trở lên.\n" +
                          "Toàn bộ bề mặt tiếp xúc bọc da bò thật, đường may chỉ nổi thủ công; đệm ngồi mút HD bọc lớp lông vũ cho cảm giác lún êm.\n" +
                          "Khung gỗ sồi kết hợp đai thun Ý, chân kim loại mạ đồng xước.",
            Materials = [new("da-that")],
            Colors = [new("nau-cognac"), new("den")],
            Sizes = [new("sofa-goc-280"), new("sofa-goc-320", 6_000_000)],
            SecondaryMaterials = [new("go-soi", "Khung")]
        },
        new()
        {
            Sku = "SF-VELVET", Name = "Sofa nhung 2 chỗ Velvet Cloud", CategorySlug = "sofa", Type = FurnitureType.Sofa,
            StyleCode = "Modern", Shape = "sofa", BasePrice = 12_500_000, SalePercent = 15, Sold = 132,
            LengthMm = 1600, WidthMm = 850, HeightMm = 780, WeightKg = 38,
            ShortDescription = "Sofa 2 chỗ bọc nhung mềm mịn, form bo tròn trẻ trung, vừa vặn cho căn hộ nhỏ.",
            Description = "Velvet Cloud có dáng bo tròn như đám mây, lớp nhung mịn bắt sáng tạo điểm nhấn cho phòng khách.\n" +
                          "Chiều dài 1m6 phù hợp căn hộ 1 - 2 phòng ngủ; khung gỗ cao su xử lý chống mối mọt.",
            Materials = [new("vai-nhung")],
            Colors = [new("xanh-navy"), new("hong-dat"), new("vang-mu-tat")],
            Sizes = [new("sofa-2-cho-160")],
            SecondaryMaterials = [new("go-cao-su", "Khung")]
        },
        new()
        {
            Sku = "SF-TOKYO", Name = "Sofa giường thông minh Tokyo", CategorySlug = "sofa", Type = FurnitureType.Sofa,
            StyleCode = "Japandi", Shape = "sofa", BasePrice = 9_900_000, Sold = 61,
            LengthMm = 1900, WidthMm = 900, HeightMm = 850, WeightKg = 45,
            ShortDescription = "Sofa kiêm giường ngủ 1m2, gập mở trong 5 giây, có hộc chứa đồ dưới đệm.",
            Description = "Tokyo giải quyết bài toán phòng khách kiêm phòng ngủ cho khách.\n" +
                          "Cơ cấu gập bằng thép sơn tĩnh điện chịu tải 250kg, mở ra thành giường 1m2 x 1m9. Hộc dưới đệm chứa chăn gối gọn gàng.",
            Materials = [new("vai-bo")],
            Colors = [new("xam"), new("kem")],
            Sizes = [new("sofa-giuong-190")],
            SecondaryMaterials = [new("thep-son-tinh-dien", "Cơ cấu gập")]
        },
        new()
        {
            Sku = "BT-LUNA", Name = "Bàn trà tròn gỗ óc chó Luna", CategorySlug = "ban-tra", Type = FurnitureType.Table,
            StyleCode = "Japandi", Shape = "coffee-table", BasePrice = 6_800_000, Featured = true, Sold = 74,
            LengthMm = 800, WidthMm = 800, HeightMm = 420, WeightKg = 18,
            ShortDescription = "Bàn trà tròn mặt gỗ óc chó nguyên tấm, chân trụ tiện tròn, hoàn thiện dầu lau tự nhiên.",
            Description = "Mặt bàn Luna ghép từ những thanh gỗ óc chó chọn vân, dày 3cm, hoàn thiện bằng dầu gỗ tự nhiên an toàn.\n" +
                          "Chân trụ đặc tiện tròn giúp bàn vững và dễ phối cùng sofa màu sáng.",
            Materials = [new("go-oc-cho")],
            Colors = [new("nau-oc-cho")],
            Sizes = [new("ban-tra-tron-80"), new("ban-tra-tron-100", 1_200_000)]
        },
        new()
        {
            Sku = "BT-NERO", Name = "Bàn trà mặt đá marble chân sắt Nero", CategorySlug = "ban-tra", Type = FurnitureType.Table,
            StyleCode = "Luxury", Shape = "coffee-table", BasePrice = 8_500_000, SalePercent = 10, Sold = 40,
            LengthMm = 1200, WidthMm = 600, HeightMm = 420, WeightKg = 32,
            ShortDescription = "Bàn trà chữ nhật mặt đá marble tự nhiên, chân thép sơn tĩnh điện đen mờ.",
            Description = "Mặt đá marble tự nhiên dày 18mm được phủ lớp chống thấm, mỗi tấm đá có vân mây độc bản.\n" +
                          "Chân thép hộp sơn tĩnh điện, đế cao su chống trầy sàn.",
            Materials = [new("da-marble")],
            Colors = [new("trang-van-da"), new("den")],
            Sizes = [new("ban-tra-120")],
            SecondaryMaterials = [new("thep-son-tinh-dien", "Chân")],
            SecondaryColors = [new("den", "Chân")]
        },
        new()
        {
            Sku = "BT-NORDIC", Name = "Bàn trà gỗ sồi 2 tầng Nordic", CategorySlug = "ban-tra", Type = FurnitureType.Table,
            StyleCode = "Scandinavian", Shape = "coffee-table", BasePrice = 4_200_000, SalePercent = 10, Sold = 95,
            LengthMm = 1000, WidthMm = 550, HeightMm = 450, WeightKg = 15,
            ShortDescription = "Bàn trà 2 tầng gỗ sồi, tầng dưới để sách báo, góc bo an toàn cho trẻ nhỏ.",
            Description = "Bàn trà Nordic có tầng dưới tiện lợi để tạp chí, điều khiển.\n" +
                          "Các góc được bo tròn R10 an toàn cho gia đình có trẻ nhỏ; sơn PU gốc nước ít mùi.",
            Materials = [new("go-soi")],
            Colors = [new("soi-tu-nhien"), new("trang", 300_000)],
            Sizes = [new("ban-tra-100")]
        },
        new()
        {
            Sku = "KTV-HANOI", Name = "Kệ tivi gỗ óc chó Hanoi", CategorySlug = "ke-tivi", Type = FurnitureType.Shelf,
            StyleCode = "Modern", Shape = "tv-stand", BasePrice = 11_500_000, Featured = true, Sold = 38,
            LengthMm = 1800, WidthMm = 400, HeightMm = 500, WeightKg = 40,
            ShortDescription = "Kệ tivi gỗ óc chó 2 ngăn kéo, 2 hộc mở, ray giảm chấn, đi dây gọn gàng.",
            Description = "Kệ tivi Hanoi có hệ ray trượt giảm chấn, lỗ đi dây ẩn phía sau giúp mặt kệ luôn gọn.\n" +
                          "Phù hợp tivi 55 - 75 inch.",
            Materials = [new("go-oc-cho")],
            Colors = [new("nau-oc-cho")],
            Sizes = [new("ke-tivi-180"), new("ke-tivi-200", 1_500_000)]
        },
        new()
        {
            Sku = "KTV-SLIM", Name = "Kệ tivi treo tường Slim MDF chống ẩm", CategorySlug = "ke-tivi", Type = FurnitureType.Shelf,
            StyleCode = "Minimalist", Shape = "tv-stand", BasePrice = 3_900_000, Sold = 110,
            LengthMm = 1600, WidthMm = 350, HeightMm = 300, WeightKg = 22,
            ShortDescription = "Kệ tivi treo tường mỏng nhẹ, tiết kiệm diện tích, MDF lõi xanh chống ẩm.",
            Description = "Thiết kế treo tường giúp sàn nhà thoáng, dễ lau dọn.\n" +
                          "Tải trọng tối đa 40kg với bộ pát treo đi kèm; lắp đặt miễn phí nội thành.",
            Materials = [new("mdf-chong-am")],
            Colors = [new("trang"), new("xam"), new("van-soi")],
            Sizes = [new("ke-tivi-160"), new("ke-tivi-200", 900_000)]
        },
        new()
        {
            Sku = "GTG-LOUNGE", Name = "Ghế thư giãn Lounge gỗ uốn cong", CategorySlug = "ghe-thu-gian", Type = FurnitureType.Chair,
            StyleCode = "Modern", Shape = "armchair", BasePrice = 7_900_000, SalePercent = 12, Sold = 33,
            LengthMm = 800, WidthMm = 820, HeightMm = 850, WeightKg = 20,
            ShortDescription = "Ghế thư giãn khung gỗ uốn cong, bọc da PU cao cấp, lưng ngả 15 độ.",
            Description = "Khung gỗ uốn cong nhiều lớp ép nhiệt tạo độ đàn hồi nhẹ khi ngồi.\n" +
                          "Đệm da PU dày 8cm, góc ngả 15 độ lý tưởng để đọc sách, nghe nhạc.",
            Materials = [new("da-pu")],
            Colors = [new("den"), new("nau-cognac")],
            SecondaryMaterials = [new("go-soi", "Khung gỗ uốn")]
        },

        // ---------------- Phòng ngủ ----------------
        new()
        {
            Sku = "GN-NORDIC", Name = "Giường ngủ gỗ sồi Nordic có ngăn kéo", CategorySlug = "giuong-ngu", Type = FurnitureType.Bed,
            StyleCode = "Scandinavian", Shape = "bed", BasePrice = 12_900_000, Featured = true, Sold = 57, WarrantyMonths = 24,
            LengthMm = 2000, WidthMm = 1600, HeightMm = 1000, WeightKg = 60,
            ShortDescription = "Giường gỗ sồi tự nhiên 2 ngăn kéo chứa đồ, dát phản gỗ thông thoáng khí.",
            Description = "Giường Nordic có 2 ngăn kéo lớn dưới gầm, ray bi 3 tầng chịu tải 30kg.\n" +
                          "Dát giường bằng gỗ thông đã sấy, khoảng cách dát 5cm giúp nệm thoáng khí, không ẩm mốc.",
            Materials = [new("go-soi")],
            Colors = [new("soi-tu-nhien"), new("nau-oc-cho", 800_000)],
            Sizes = [new("giuong-160"), new("giuong-180", 1_500_000)]
        },
        new()
        {
            Sku = "GN-PARIS", Name = "Giường bọc nệm đầu giường cao Paris", CategorySlug = "giuong-ngu", Type = FurnitureType.Bed,
            StyleCode = "Luxury", Shape = "bed", BasePrice = 18_500_000, SalePercent = 12, Sold = 29, WarrantyMonths = 24,
            LengthMm = 2000, WidthMm = 1800, HeightMm = 1200, WeightKg = 70,
            ShortDescription = "Giường bọc nhung đầu giường cao 1m2 chần múi, phong cách khách sạn 5 sao.",
            Description = "Đầu giường Paris cao 1m2 chần múi thủ công, đệm mút dày giúp tựa lưng êm ái khi đọc sách.\n" +
                          "Khung gỗ thông sấy, toàn bộ bọc vải nhung chống bám bụi.",
            Materials = [new("vai-nhung")],
            Colors = [new("xam"), new("kem"), new("xanh-navy")],
            Sizes = [new("giuong-160"), new("giuong-180", 2_000_000)],
            SecondaryMaterials = [new("go-thong", "Khung giường")]
        },
        new()
        {
            Sku = "GN-ZEN", Name = "Giường gỗ cao su thấp Zen Japandi", CategorySlug = "giuong-ngu", Type = FurnitureType.Bed,
            StyleCode = "Japandi", Shape = "bed", BasePrice = 7_500_000, Sold = 88,
            LengthMm = 2000, WidthMm = 1600, HeightMm = 750, WeightKg = 45,
            ShortDescription = "Giường bệt kiểu Nhật chân thấp, gỗ cao su ghép, tối giản và ấm cúng.",
            Description = "Chiều cao mặt giường chỉ 25cm tạo cảm giác rộng rãi cho phòng ngủ trần thấp.\n" +
                          "Đầu giường thấp bo cạnh mềm, hoàn thiện sơn PU mờ giữ vân gỗ tự nhiên.",
            Materials = [new("go-cao-su")],
            Colors = [new("go-tu-nhien"), new("nau-oc-cho", 500_000)],
            Sizes = [new("giuong-140", -800_000), new("giuong-160"), new("giuong-180", 1_000_000)]
        },
        new()
        {
            Sku = "TQA-OAK3", Name = "Tủ quần áo gỗ sồi cánh mở", CategorySlug = "tu-quan-ao", Type = FurnitureType.Cabinet,
            StyleCode = "Modern", Shape = "wardrobe", BasePrice = 16_500_000, Sold = 31, WarrantyMonths = 24,
            LengthMm = 1200, WidthMm = 600, HeightMm = 2000, WeightKg = 110,
            ShortDescription = "Tủ quần áo gỗ sồi cánh mở, khoang treo dài, ngăn kéo và bản lề giảm chấn.",
            Description = "Bố trí khoang treo đồ dài, khoang treo ngắn và 2 ngăn kéo; bản lề giảm chấn inox 304.\n" +
                          "Tủ được lắp ráp và cân chỉnh tại nhà khách hàng.",
            Materials = [new("go-soi")],
            Colors = [new("soi-tu-nhien"), new("trang", 500_000)],
            Sizes = [new("tu-3-canh-120"), new("tu-4-canh-160", 3_500_000)]
        },
        new()
        {
            Sku = "TQA-SLIDE", Name = "Tủ quần áo cửa lùa phủ gương", CategorySlug = "tu-quan-ao", Type = FurnitureType.Cabinet,
            StyleCode = "Modern", Shape = "wardrobe", BasePrice = 13_900_000, SalePercent = 8, Featured = true, Sold = 22,
            LengthMm = 1800, WidthMm = 600, HeightMm = 2200, WeightKg = 150,
            ShortDescription = "Tủ cửa lùa tiết kiệm diện tích, một cánh phủ gương soi toàn thân.",
            Description = "Cửa lùa ray nhôm êm, không cần khoảng trống mở cánh, phù hợp phòng ngủ hẹp.\n" +
                          "Một cánh phủ gương cường lực giúp phòng rộng hơn về mặt thị giác.",
            Materials = [new("mdf-chong-am")],
            Colors = [new("trang"), new("xam")],
            Sizes = [new("tu-lua-180"), new("tu-lua-240", 3_200_000)],
            SecondaryMaterials = [new("kinh-cuong-luc", "Cửa gương")]
        },
        new()
        {
            Sku = "TDG-WAL", Name = "Tab đầu giường 2 ngăn gỗ óc chó", CategorySlug = "tab-dau-giuong", Type = FurnitureType.Cabinet,
            StyleCode = "Japandi", Shape = "nightstand", BasePrice = 2_900_000, Sold = 140,
            LengthMm = 450, WidthMm = 400, HeightMm = 500, WeightKg = 12,
            ShortDescription = "Tab đầu giường 2 ngăn kéo gỗ óc chó, tay nắm âm, ray giảm chấn.",
            Description = "Tay nắm khoét âm liền khối tạo mặt tủ phẳng tinh tế; ray giảm chấn đóng êm không gây tiếng động ban đêm.",
            Materials = [new("go-oc-cho")],
            Colors = [new("nau-oc-cho")]
        },
        new()
        {
            Sku = "TDG-IRON", Name = "Tab đầu giường chân sắt Industrial", CategorySlug = "tab-dau-giuong", Type = FurnitureType.Cabinet,
            StyleCode = "Industrial", Shape = "nightstand", BasePrice = 1_650_000, SalePercent = 10, Sold = 165,
            LengthMm = 450, WidthMm = 380, HeightMm = 550, WeightKg = 9,
            ShortDescription = "Tab đầu giường 1 ngăn kéo 1 hộc mở, khung thép sơn tĩnh điện.",
            Description = "Khung thép sơn tĩnh điện đen mờ phối mặt gỗ MDF vân sồi, phong cách công nghiệp cá tính.",
            Materials = [new("mdf-chong-am")],
            Colors = [new("van-soi"), new("den")],
            SecondaryMaterials = [new("thep-son-tinh-dien", "Khung chân")]
        },
        new()
        {
            Sku = "BTD-ROSE", Name = "Bàn trang điểm gương tròn Rose", CategorySlug = "ban-trang-diem", Type = FurnitureType.Table,
            StyleCode = "Modern", Shape = "vanity", BasePrice = 5_400_000, Sold = 47,
            LengthMm = 1000, WidthMm = 450, HeightMm = 760, WeightKg = 25,
            ShortDescription = "Bàn trang điểm kèm gương tròn Ø60cm, 3 ngăn kéo, tặng kèm ghế đôn.",
            Description = "Gương tròn viền mỏng, 3 ngăn kéo chia ô đựng mỹ phẩm; tặng kèm ghế đôn bọc nệm cùng màu.",
            Materials = [new("mdf-chong-am")],
            Colors = [new("trang"), new("hong-dat", 300_000)],
            SecondaryMaterials = [new("kinh-cuong-luc", "Gương")]
        },

        // ---------------- Phòng ăn ----------------
        new()
        {
            Sku = "BA-WALNUT", Name = "Bàn ăn gỗ óc chó mặt liền Walnut", CategorySlug = "ban-an", Type = FurnitureType.Table,
            StyleCode = "Modern", Shape = "dining-table", BasePrice = 24_900_000, SalePercent = 8, Featured = true, Sold = 45, WarrantyMonths = 36,
            LengthMm = 1800, WidthMm = 900, HeightMm = 750, WeightKg = 65,
            ShortDescription = "Bàn ăn mặt liền gỗ óc chó dày 4cm, cạnh live-edge tự nhiên, chân chữ V vững chãi.",
            Description = "Mặt bàn ghép từ các thanh gỗ óc chó Bắc Mỹ dày 4cm, giữ cạnh gỗ tự nhiên (live-edge) ở hai bên.\n" +
                          "Hoàn thiện dầu gỗ gốc thực vật chống thấm, an toàn thực phẩm. Kích thước 1m8 phù hợp gia đình 6 - 8 người.",
            Materials = [new("go-oc-cho")],
            Colors = [new("nau-oc-cho")],
            Sizes = [new("ban-an-160", -3_000_000), new("ban-an-180"), new("ban-an-200", 3_500_000)]
        },
        new()
        {
            Sku = "BA-SCANDI", Name = "Bàn ăn gỗ sồi Scandinavian", CategorySlug = "ban-an", Type = FurnitureType.Table,
            StyleCode = "Scandinavian", Shape = "dining-table", BasePrice = 9_800_000, Featured = true, Sold = 120,
            LengthMm = 1600, WidthMm = 850, HeightMm = 750, WeightKg = 38,
            ShortDescription = "Bàn ăn gỗ sồi chân vát, góc bo mềm, phù hợp gia đình 4 - 6 người.",
            Description = "Thiết kế Bắc Âu kinh điển với chân vát côn và mặt bàn góc bo.\n" +
                          "Sơn PU gốc nước chống thấm, chịu nhiệt nhẹ, dễ lau chùi hằng ngày.",
            Materials = [new("go-soi")],
            Colors = [new("soi-tu-nhien"), new("trang", 400_000)],
            Sizes = [new("ban-an-140", -1_200_000), new("ban-an-160")]
        },
        new()
        {
            Sku = "BA-CERAMIC", Name = "Bàn ăn mặt đá ceramic chân sắt", CategorySlug = "ban-an", Type = FurnitureType.Table,
            StyleCode = "Industrial", Shape = "dining-table", BasePrice = 13_500_000, Sold = 52,
            LengthMm = 1600, WidthMm = 850, HeightMm = 750, WeightKg = 55,
            ShortDescription = "Bàn ăn mặt đá ceramic chịu nhiệt, chống trầy, chân thép chữ X sơn tĩnh điện.",
            Description = "Mặt đá ceramic nung ở 1200°C chịu nhiệt, chống trầy và không thấm dầu mỡ - có thể đặt nồi nóng trực tiếp.\n" +
                          "Chân thép chữ X sơn tĩnh điện đen, đế chỉnh cân bằng.",
            Materials = [new("da-ceramic")],
            Colors = [new("trang-van-da"), new("xam")],
            Sizes = [new("ban-an-160"), new("ban-an-180", 1_800_000)],
            SecondaryMaterials = [new("thep-son-tinh-dien", "Chân bàn")],
            SecondaryColors = [new("den", "Chân bàn")]
        },
        new()
        {
            Sku = "BA-ROUND", Name = "Bàn ăn tròn gỗ tần bì mâm xoay", CategorySlug = "ban-an", Type = FurnitureType.Table,
            StyleCode = "Classic", Shape = "dining-table", BasePrice = 11_200_000, Sold = 36,
            LengthMm = 1200, WidthMm = 1200, HeightMm = 750, WeightKg = 42,
            ShortDescription = "Bàn ăn tròn Ø1m2 gỗ tần bì có mâm xoay, hợp bữa cơm gia đình Việt.",
            Description = "Mâm xoay Ø60cm giúp gắp thức ăn thuận tiện cho cả gia đình 6 người.\n" +
                          "Chân trụ đơn không vướng chân khi ngồi.",
            Materials = [new("go-tan-bi")],
            Colors = [new("go-tu-nhien"), new("nau-oc-cho", 600_000)],
            Sizes = [new("ban-an-tron-120")]
        },
        new()
        {
            Sku = "GA-CURVE", Name = "Ghế ăn gỗ sồi lưng cong", CategorySlug = "ghe-an", Type = FurnitureType.Chair,
            StyleCode = "Scandinavian", Shape = "chair", BasePrice = 1_850_000, Featured = true, Sold = 320,
            LengthMm = 450, WidthMm = 500, HeightMm = 800, WeightKg = 5,
            ShortDescription = "Ghế ăn gỗ sồi tựa lưng cong ôm người, mặt ngồi rộng 45cm.",
            Description = "Tựa lưng uốn cong theo cột sống giúp ngồi lâu không mỏi; mộng liên kết chắc chắn, không dùng đinh vít lộ.",
            Materials = [new("go-soi")],
            Colors = [new("soi-tu-nhien"), new("den"), new("nau-oc-cho")]
        },
        new()
        {
            Sku = "GA-VELVET", Name = "Ghế ăn bọc nhung chân gỗ", CategorySlug = "ghe-an", Type = FurnitureType.Chair,
            StyleCode = "Modern", Shape = "chair", BasePrice = 2_350_000, SalePercent = 10, Sold = 210,
            LengthMm = 460, WidthMm = 540, HeightMm = 820, WeightKg = 6,
            ShortDescription = "Ghế ăn bọc nhung êm ái, chân gỗ cao su vững chắc, nhiều màu trẻ trung.",
            Description = "Lớp nhung dày chống bám bụi, đệm mút D35 êm ái; chân gỗ cao su sơn màu óc chó.",
            Materials = [new("vai-nhung")],
            Colors = [new("xanh-reu"), new("xam"), new("hong-dat")],
            SecondaryMaterials = [new("go-cao-su", "Chân ghế")]
        },
        new()
        {
            Sku = "GA-RATTAN", Name = "Ghế ăn mây tre đan Rattan", CategorySlug = "ghe-an", Type = FurnitureType.Chair,
            StyleCode = "Rustic", Shape = "chair", BasePrice = 1_950_000, Sold = 150,
            LengthMm = 480, WidthMm = 520, HeightMm = 800, WeightKg = 5,
            ShortDescription = "Ghế ăn tựa lưng mây đan thủ công, khung gỗ tần bì, mát mẻ cho mùa hè.",
            Description = "Tựa lưng mây tự nhiên đan tay bởi nghệ nhân làng nghề, khung gỗ tần bì chắc nhẹ.",
            Materials = [new("may-tre")],
            Colors = [new("go-tu-nhien")],
            SecondaryMaterials = [new("go-tan-bi", "Khung")]
        },
        new()
        {
            Sku = "BBA-FAMILY", Name = "Bộ bàn ăn 6 ghế gỗ sồi Family", CategorySlug = "bo-ban-an", Type = FurnitureType.Table,
            StyleCode = "Classic", Shape = "dining-set", BasePrice = 22_500_000, SalePercent = 10, Featured = true, Sold = 64, WarrantyMonths = 24,
            LengthMm = 1600, WidthMm = 850, HeightMm = 750, WeightKg = 90,
            ShortDescription = "Trọn bộ 1 bàn + 6 ghế gỗ sồi đồng bộ màu, tiết kiệm hơn mua lẻ.",
            Description = "Bộ Family gồm 1 bàn ăn chữ nhật và 6 ghế tựa nan, đồng bộ màu sơn.\n" +
                          "Tiết kiệm khoảng 15% so với mua lẻ, giao và lắp đặt miễn phí.",
            Materials = [new("go-soi")],
            Colors = [new("soi-tu-nhien"), new("nau-oc-cho", 1_000_000)],
            Sizes = [new("ban-an-160"), new("ban-an-180", 2_000_000)]
        },
        new()
        {
            Sku = "TR-WALNUT", Name = "Tủ rượu gỗ óc chó cửa kính", CategorySlug = "tu-ruou", Type = FurnitureType.Cabinet,
            StyleCode = "Luxury", Shape = "cabinet", BasePrice = 19_800_000, Sold = 12, WarrantyMonths = 24,
            LengthMm = 1200, WidthMm = 450, HeightMm = 1800, WeightKg = 85,
            ShortDescription = "Tủ rượu gỗ óc chó cửa kính cường lực, giá để 24 chai và đèn LED trưng bày.",
            Description = "Khoang trưng bày có đèn LED ánh vàng, giá gỗ để 24 chai vang nằm ngang, khoang dưới cánh gỗ đựng ly.",
            Materials = [new("go-oc-cho")],
            Colors = [new("nau-oc-cho")],
            SecondaryMaterials = [new("kinh-cuong-luc", "Cửa kính")]
        },

        // ---------------- Phòng làm việc ----------------
        new()
        {
            Sku = "BLV-STUDY", Name = "Bàn làm việc gỗ sồi có hộc kéo Study", CategorySlug = "ban-lam-viec", Type = FurnitureType.Table,
            StyleCode = "Minimalist", Shape = "desk", BasePrice = 4_600_000, Featured = true, Sold = 175,
            LengthMm = 1200, WidthMm = 600, HeightMm = 750, WeightKg = 28,
            ShortDescription = "Bàn làm việc gỗ sồi 2 hộc kéo, lỗ đi dây, phù hợp học tập và làm việc tại nhà.",
            Description = "Mặt bàn gỗ sồi dày 2,5cm, hai hộc kéo dưới mặt bàn để giấy tờ và laptop.\n" +
                          "Lỗ đi dây Ø6cm ở góc bàn giúp gọn gàng dây sạc.",
            Materials = [new("go-soi")],
            Colors = [new("soi-tu-nhien"), new("trang")],
            Sizes = [new("ban-lv-120"), new("ban-lv-140", 700_000)]
        },
        new()
        {
            Sku = "BLV-UFRAME", Name = "Bàn làm việc chân sắt chữ U", CategorySlug = "ban-lam-viec", Type = FurnitureType.Table,
            StyleCode = "Industrial", Shape = "desk", BasePrice = 3_200_000, SalePercent = 15, Sold = 260,
            LengthMm = 1200, WidthMm = 600, HeightMm = 750, WeightKg = 25,
            ShortDescription = "Bàn làm việc chân thép chữ U chịu lực 120kg, mặt MDF chống ẩm vân sồi.",
            Description = "Chân chữ U bằng thép hộp 40x80 sơn tĩnh điện, chịu lực 120kg, không rung khi gõ phím.\n" +
                          "Mặt bàn MDF lõi xanh chống ẩm phủ melamine chống trầy.",
            Materials = [new("mdf-chong-am")],
            Colors = [new("van-soi"), new("den")],
            Sizes = [new("ban-lv-120"), new("ban-lv-140", 500_000), new("ban-lv-160", 1_000_000)],
            SecondaryMaterials = [new("thep-son-tinh-dien", "Chân chữ U")]
        },
        new()
        {
            Sku = "BLV-LIFT", Name = "Bàn nâng hạ điện mặt gỗ óc chó", CategorySlug = "ban-lam-viec", Type = FurnitureType.Table,
            StyleCode = "Modern", Shape = "desk", BasePrice = 12_900_000, Sold = 44, WarrantyMonths = 36,
            LengthMm = 1400, WidthMm = 700, HeightMm = 730, WeightKg = 35,
            ShortDescription = "Bàn nâng hạ điện động cơ kép, chiều cao 62 - 127cm, nhớ 4 vị trí, mặt gỗ óc chó.",
            Description = "Động cơ kép nâng hạ êm (dưới 45dB), tải trọng 120kg, bộ điều khiển nhớ 4 độ cao.\n" +
                          "Mặt gỗ óc chó nguyên tấm dày 2,5cm; khung thép sơn tĩnh điện đen.",
            Materials = [new("go-oc-cho")],
            Colors = [new("nau-oc-cho")],
            Sizes = [new("ban-lv-140"), new("ban-lv-160", 1_300_000)],
            SecondaryMaterials = [new("thep-son-tinh-dien", "Khung nâng hạ")],
            SecondaryColors = [new("den", "Khung nâng hạ")]
        },
        new()
        {
            Sku = "GVP-ERGO", Name = "Ghế công thái học lưới ErgoPro", CategorySlug = "ghe-van-phong", Type = FurnitureType.Chair,
            StyleCode = "Modern", Shape = "office-chair", BasePrice = 5_900_000, SalePercent = 10, Featured = true, Sold = 190, WarrantyMonths = 36,
            LengthMm = 650, WidthMm = 650, HeightMm = 1200, WeightKg = 18,
            ShortDescription = "Ghế công thái học lưng lưới, tựa đầu và tựa tay 3D, đỡ thắt lưng điều chỉnh.",
            Description = "Tựa lưng lưới đàn hồi thoáng khí, đỡ thắt lưng điều chỉnh độ cao và độ sâu.\n" +
                          "Piston class 4, chân nhôm 5 cánh, ngả lưng 135° có khóa.",
            Materials = [new("vai-luoi")],
            Colors = [new("den"), new("xam")],
            SecondaryMaterials = [new("thep-son-tinh-dien", "Chân & khung")]
        },
        new()
        {
            Sku = "GVP-ASH", Name = "Ghế làm việc gỗ tần bì bọc da", CategorySlug = "ghe-van-phong", Type = FurnitureType.Chair,
            StyleCode = "Classic", Shape = "chair", BasePrice = 3_400_000, Sold = 58,
            LengthMm = 550, WidthMm = 550, HeightMm = 900, WeightKg = 9,
            ShortDescription = "Ghế làm việc khung gỗ tần bì, mặt ngồi và tựa lưng bọc da PU.",
            Description = "Phong cách thư phòng cổ điển, phù hợp bàn làm việc gỗ tự nhiên.\n" +
                          "Đệm ngồi da PU dày 5cm, chân gỗ có đế nỉ chống trầy sàn.",
            Materials = [new("da-pu")],
            Colors = [new("nau-cognac"), new("den")],
            SecondaryMaterials = [new("go-tan-bi", "Khung")]
        },
        new()
        {
            Sku = "KS-OAK5", Name = "Kệ sách gỗ sồi 5 tầng", CategorySlug = "ke-sach", Type = FurnitureType.Shelf,
            StyleCode = "Scandinavian", Shape = "bookshelf", BasePrice = 3_800_000, Sold = 98,
            LengthMm = 800, WidthMm = 300, HeightMm = 1800, WeightKg = 30,
            ShortDescription = "Kệ sách gỗ sồi 5 tầng, mỗi tầng chịu tải 25kg, có pát chống đổ.",
            Description = "Thiết kế mở 5 tầng, mỗi tầng chịu tải 25kg; đi kèm pát cố định tường chống đổ an toàn cho trẻ nhỏ.",
            Materials = [new("go-soi")],
            Colors = [new("soi-tu-nhien"), new("trang")]
        },
        new()
        {
            Sku = "KS-IRON", Name = "Kệ sách chân sắt Industrial 4 tầng", CategorySlug = "ke-sach", Type = FurnitureType.Shelf,
            StyleCode = "Industrial", Shape = "bookshelf", BasePrice = 2_450_000, SalePercent = 12, Sold = 143,
            LengthMm = 1000, WidthMm = 350, HeightMm = 1500, WeightKg = 24,
            ShortDescription = "Kệ sách 4 tầng khung thép sơn tĩnh điện, mặt gỗ MDF vân sồi.",
            Description = "Khung thép ống vuông sơn tĩnh điện, lắp ráp dễ dàng trong 15 phút; mặt kệ MDF chống ẩm.",
            Materials = [new("mdf-chong-am")],
            Colors = [new("van-soi"), new("den")],
            SecondaryMaterials = [new("thep-son-tinh-dien", "Khung")]
        },

        // ---------------- Trang trí & Tiện ích ----------------
        new()
        {
            Sku = "TG-SLIM", Name = "Tủ giày gỗ cao su 2 cánh Slim", CategorySlug = "tu-giay", Type = FurnitureType.Cabinet,
            StyleCode = "Minimalist", Shape = "cabinet", BasePrice = 2_800_000, Sold = 76,
            LengthMm = 900, WidthMm = 350, HeightMm = 1000, WeightKg = 22,
            ShortDescription = "Tủ giày 2 cánh sâu 35cm, chứa 18 - 20 đôi, mặt tủ để chìa khóa và đồ trang trí.",
            Description = "Các đợt kệ đặt nghiêng giúp tiết kiệm chiều sâu, lỗ thông gió phía sau chống ẩm mùi.",
            Materials = [new("go-cao-su")],
            Colors = [new("trang"), new("go-tu-nhien")]
        },
        new()
        {
            Sku = "KTT-PINE", Name = "Kệ trang trí treo tường gỗ thông", CategorySlug = "ke-trang-tri", Type = FurnitureType.Shelf,
            StyleCode = "Rustic", Shape = "wall-shelf", BasePrice = 890_000, Sold = 205,
            LengthMm = 800, WidthMm = 200, HeightMm = 600, WeightKg = 5,
            ShortDescription = "Kệ treo tường 3 tầng gỗ thông tự nhiên, trang trí cây xanh và sách nhỏ.",
            Description = "Gỗ thông giữ vân mắt tự nhiên, xử lý chống mối mọt; đi kèm vít nở và hướng dẫn lắp đặt.",
            Materials = [new("go-thong")],
            Colors = [new("go-tu-nhien"), new("nau-oc-cho", 100_000)]
        },
        new()
        {
            Sku = "GU-ARCH", Name = "Gương đứng khung gỗ óc chó mái vòm", CategorySlug = "guong", Type = FurnitureType.Decor,
            StyleCode = "Japandi", Shape = "mirror", BasePrice = 3_200_000, Sold = 67,
            LengthMm = 600, WidthMm = 40, HeightMm = 1700, WeightKg = 15,
            ShortDescription = "Gương soi toàn thân mái vòm, khung gỗ óc chó, có chân đỡ đứng hoặc treo tường.",
            Description = "Mặt gương bạc Bỉ không biến dạng hình ảnh, khung gỗ óc chó bo vòm mềm mại; dùng đứng hoặc treo tường đều được.",
            Materials = [new("go-oc-cho")],
            Colors = [new("nau-oc-cho")],
            SecondaryMaterials = [new("kinh-cuong-luc", "Mặt gương")]
        }
    ];
}
