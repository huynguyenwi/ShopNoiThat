using FurnitureStore.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FurnitureStore.Infrastructure.Persistence.Seed;

/// <summary>
/// Default content of the chatbot knowledge base (policies and FAQ). Admins edit it at /admin/ai-knowledge.
/// Seeded once when the table is empty; entries still holding an older default text are upgraded to the current one.
/// </summary>
public sealed class AiKnowledgeSeeder(ApplicationDbContext context, ILogger<AiKnowledgeSeeder> logger)
{
    public const string ShippingContent =
        "Phí giao hàng và lắp đặt không tính sẵn trên website: sau khi khách gửi yêu cầu đặt hàng, cửa hàng gọi lại và báo phí theo địa chỉ, "
        + "số món và tầng lầu. Nội thành thường giao trong 2 - 4 ngày, các tỉnh 3 - 7 ngày. Đồ đặt đóng theo yêu cầu cần thêm thời gian sản xuất, "
        + "cửa hàng báo cụ thể khi xác nhận.";

    public const string PaymentContent =
        "Website nhận yêu cầu đặt hàng, không thanh toán trực tuyến. Sau khi cửa hàng liên hệ xác nhận mẫu, giá và phí giao - lắp đặt, "
        + "khách thanh toán khi nhận hàng (COD) hoặc chuyển khoản theo hướng dẫn của nhân viên, nội dung ghi mã đơn hàng.";

    /// <summary>Earlier default texts; an entry still holding one of them was never edited by an admin and is upgraded.</summary>
    private static readonly Dictionary<string, (string Old, string New)[]> Upgrades = new()
    {
        ["Giao hàng & lắp đặt"] =
        [
            ("Miễn phí giao hàng và lắp đặt cho đơn từ 10.000.000đ; đơn nhỏ hơn phí giao và lắp đặt là 300.000đ. "
             + "Nội thành thường giao trong 2 - 4 ngày, các tỉnh 3 - 7 ngày. Đồ đặt đóng theo yêu cầu cần thêm thời gian sản xuất, cửa hàng báo cụ thể khi xác nhận.",
             ShippingContent)
        ],
        ["Thanh toán"] =
        [
            ("Hỗ trợ thanh toán khi nhận hàng (COD) và chuyển khoản ngân hàng. Với chuyển khoản, ghi nội dung theo mã đơn hàng hiển thị sau khi đặt.",
             PaymentContent)
        ]
    };

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        if (await context.AIKnowledgeEntries.AnyAsync(cancellationToken))
        {
            await UpgradeDefaultsAsync(cancellationToken);
            return;
        }

        var entries = new[]
        {
            Entry(1, "Chính sách", "Giao hàng & lắp đặt", ShippingContent,
                "giao hàng, vận chuyển, ship, lắp đặt, phí giao, bao lâu, mấy ngày"),
            Entry(2, "Chính sách", "Bảo hành",
                "Thời gian bảo hành ghi trong mục Thông số của từng sản phẩm (phần lớn 12 - 24 tháng) cho khung, kết cấu và lỗi sản xuất. "
                + "Không bảo hành hư hỏng do sử dụng sai cách, ngâm nước hoặc va đập. Liên hệ hotline hoặc chat để được hỗ trợ bảo hành.",
                "bảo hành, sửa chữa, hỏng, lỗi, bảo trì"),
            Entry(3, "Chính sách", "Đổi trả",
                "Đổi trả trong 7 ngày nếu sản phẩm lỗi do sản xuất hoặc giao sai mẫu. Hàng đặt đóng theo kích thước riêng không áp dụng đổi trả vì lý do cá nhân.",
                "đổi trả, trả hàng, hoàn tiền, đổi hàng"),
            Entry(4, "Chính sách", "Thanh toán", PaymentContent,
                "thanh toán, chuyển khoản, cod, trả tiền, trả góp, cọc"),
            Entry(5, "Dịch vụ", "Đặt đóng theo yêu cầu",
                "Xưởng Nhà Mộc nhận đóng nội thất theo kích thước, chất liệu và màu khách chọn. Khách có thể nhận báo giá dự kiến ở trang Báo giá; "
                + "giá cuối cùng do cửa hàng xác nhận sau khi trao đổi chi tiết.",
                "đặt đóng, theo yêu cầu, custom, kích thước riêng, báo giá, thiết kế riêng"),
            Entry(6, "Hỏi đáp", "Bảo quản đồ gỗ",
                "Lau bằng khăn mềm hơi ẩm rồi lau khô, tránh hóa chất tẩy mạnh; không đặt đồ gỗ sát nguồn nhiệt hoặc nơi nắng gắt; "
                + "dùng lót cốc cho mặt bàn; đồ bọc vải nên hút bụi định kỳ.",
                "bảo quản, vệ sinh, lau chùi, chăm sóc, mối mọt")
        };

        context.AIKnowledgeEntries.AddRange(entries);
        await context.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Seeded {Count} chatbot knowledge entries", entries.Length);
    }

    private async Task UpgradeDefaultsAsync(CancellationToken cancellationToken)
    {
        var titles = Upgrades.Keys.ToList();
        var candidates = await context.AIKnowledgeEntries.Where(e => titles.Contains(e.Title)).ToListAsync(cancellationToken);
        var upgraded = 0;
        foreach (var entry in candidates)
        {
            var match = Upgrades[entry.Title].FirstOrDefault(u => u.Old == entry.Content);
            if (match.New is not null)
            {
                entry.Content = match.New;
                upgraded++;
            }
        }

        if (upgraded > 0)
        {
            await context.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Upgraded {Count} unedited chatbot knowledge entries to the current default text", upgraded);
        }
    }

    private static AIKnowledgeEntry Entry(int order, string category, string title, string content, string keywords) => new()
    {
        Title = title,
        Content = content,
        Category = category,
        Keywords = keywords,
        IsActive = true,
        DisplayOrder = order
    };
}
