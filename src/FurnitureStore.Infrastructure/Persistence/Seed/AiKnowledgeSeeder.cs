using FurnitureStore.Application.Common.Settings;
using FurnitureStore.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FurnitureStore.Infrastructure.Persistence.Seed;

/// <summary>
/// Default content of the chatbot knowledge base (policies and FAQ). Admins edit it at /admin/ai-knowledge.
/// Seeded once when the table is empty; shipping amounts come from the Shipping settings.
/// </summary>
public sealed class AiKnowledgeSeeder(ApplicationDbContext context, IOptions<ShippingSettings> shippingOptions, ILogger<AiKnowledgeSeeder> logger)
{
    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        if (await context.AIKnowledgeEntries.AnyAsync(cancellationToken))
        {
            return;
        }

        var shipping = shippingOptions.Value;
        var entries = new[]
        {
            Entry(1, "Chính sách", "Giao hàng & lắp đặt",
                $"Miễn phí giao hàng và lắp đặt cho đơn từ {Money(shipping.FreeShippingThreshold)}; đơn nhỏ hơn phí giao và lắp đặt là {Money(shipping.StandardFee)}. "
                + "Nội thành thường giao trong 2 - 4 ngày, các tỉnh 3 - 7 ngày. Đồ đặt đóng theo yêu cầu cần thêm thời gian sản xuất, cửa hàng báo cụ thể khi xác nhận.",
                "giao hàng, vận chuyển, ship, lắp đặt, phí giao, bao lâu, mấy ngày"),
            Entry(2, "Chính sách", "Bảo hành",
                "Thời gian bảo hành ghi trong mục Thông số của từng sản phẩm (phần lớn 12 - 24 tháng) cho khung, kết cấu và lỗi sản xuất. "
                + "Không bảo hành hư hỏng do sử dụng sai cách, ngâm nước hoặc va đập. Liên hệ hotline hoặc chat để được hỗ trợ bảo hành.",
                "bảo hành, sửa chữa, hỏng, lỗi, bảo trì"),
            Entry(3, "Chính sách", "Đổi trả",
                "Đổi trả trong 7 ngày nếu sản phẩm lỗi do sản xuất hoặc giao sai mẫu. Hàng đặt đóng theo kích thước riêng không áp dụng đổi trả vì lý do cá nhân.",
                "đổi trả, trả hàng, hoàn tiền, đổi hàng"),
            Entry(4, "Chính sách", "Thanh toán",
                "Hỗ trợ thanh toán khi nhận hàng (COD) và chuyển khoản ngân hàng. Với chuyển khoản, ghi nội dung theo mã đơn hàng hiển thị sau khi đặt.",
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

    private static AIKnowledgeEntry Entry(int order, string category, string title, string content, string keywords) => new()
    {
        Title = title,
        Content = content,
        Category = category,
        Keywords = keywords,
        IsActive = true,
        DisplayOrder = order
    };

    private static string Money(decimal amount) => amount.ToString("#,0", System.Globalization.CultureInfo.InvariantCulture).Replace(',', '.') + "đ";
}
