using System.Net;

namespace FurnitureStore.Application.Common.Emails;

/// <summary>
/// Minimal, inline-styled HTML emails. Every dynamic value is HTML-encoded.
/// </summary>
public static class EmailTemplates
{
    public static string Welcome(string siteName, string fullName, string shopUrl) => Layout(siteName,
        $"""
        <p>Xin chào <strong>{E(fullName)}</strong>,</p>
        <p>Cảm ơn bạn đã đăng ký tài khoản tại {E(siteName)}. Từ nay bạn có thể lưu sản phẩm yêu thích,
        theo dõi đơn hàng và nhận tư vấn nội thất nhanh hơn.</p>
        {Button("Bắt đầu mua sắm", shopUrl)}
        """);

    public static string PasswordReset(string siteName, string fullName, string resetUrl, int validHours) => Layout(siteName,
        $"""
        <p>Xin chào <strong>{E(fullName)}</strong>,</p>
        <p>Chúng tôi nhận được yêu cầu đặt lại mật khẩu cho tài khoản của bạn. Nhấn nút bên dưới để tạo mật khẩu mới.
        Liên kết có hiệu lực trong {validHours} giờ.</p>
        {Button("Đặt lại mật khẩu", resetUrl)}
        <p style="color:#7a6d62;font-size:13px">Nếu bạn không yêu cầu, hãy bỏ qua email này - mật khẩu hiện tại vẫn giữ nguyên.</p>
        """);

    public static string PasswordChanged(string siteName, string fullName) => Layout(siteName,
        $"""
        <p>Xin chào <strong>{E(fullName)}</strong>,</p>
        <p>Mật khẩu tài khoản của bạn vừa được thay đổi. Nếu không phải bạn thực hiện, vui lòng liên hệ cửa hàng ngay.</p>
        """);

    /// <param name="qrImageUrl">Absolute address of the order QR image (PNG: e-mail clients do not show SVG).</param>
    public static string OrderPlaced(string siteName, Domain.Entities.Order order, Sales.PaymentInstructionsDto? instructions, string orderUrl, string? qrImageUrl = null)
    {
        static string Money(decimal value) => value.ToString("#,0", System.Globalization.CultureInfo.GetCultureInfo("vi-VN")) + "₫";

        var rows = string.Concat(order.Items.Select(i =>
            $"""<tr><td style="padding:8px 0;border-bottom:1px solid #eee">{E(i.ProductName)}<br><span style="color:#7a6d62;font-size:13px">{E(i.VariantName)} × {i.Quantity}</span></td><td style="padding:8px 0;border-bottom:1px solid #eee;text-align:right;white-space:nowrap">{Money(i.LineTotal)}</td></tr>"""));

        var payment = instructions is null
            ? "<p>Phương thức thanh toán: <strong>Thanh toán khi nhận hàng (COD)</strong>.</p>"
            : $"""
              <p>Vui lòng chuyển khoản <strong>{Money(instructions.Amount)}</strong> tới:</p>
              <p style="background:#faf7f2;padding:12px 16px;border-radius:8px">Ngân hàng: <strong>{E(instructions.BankName)}</strong><br>
              Số tài khoản: <strong>{E(instructions.AccountNumber)}</strong><br>Chủ tài khoản: <strong>{E(instructions.AccountName)}</strong><br>
              Nội dung: <strong>{E(instructions.TransferNote)}</strong></p>
              """;

        return Layout(siteName,
            $"""
            <p>Xin chào <strong>{E(order.CustomerName)}</strong>,</p>
            <p>Cảm ơn bạn đã đặt hàng. Mã đơn hàng của bạn là <strong>{E(order.OrderCode)}</strong>. Chúng tôi sẽ gọi xác nhận trong thời gian sớm nhất.</p>
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="font-size:14px">{rows}
              <tr><td style="padding:6px 0">Tạm tính</td><td style="text-align:right">{Money(order.Subtotal)}</td></tr>
              <tr><td style="padding:6px 0">Giảm giá{(order.CouponCode is null ? "" : $" (mã {E(order.CouponCode)})")}</td><td style="text-align:right">-{Money(order.DiscountAmount)}</td></tr>
              <tr><td style="padding:6px 0">Phí giao hàng</td><td style="text-align:right">{Money(order.ShippingFee)}</td></tr>
              <tr><td style="padding:8px 0;font-weight:700">Tổng cộng</td><td style="text-align:right;font-weight:700">{Money(order.TotalAmount)}</td></tr>
            </table>
            {payment}
            {(qrImageUrl is null ? "" : $"""
              <p style="text-align:center;margin:20px 0 4px"><img src="{E(qrImageUrl)}" width="150" height="150" alt="Mã QR đơn hàng {E(order.OrderCode)}" style="border:1px solid #eee;border-radius:8px"></p>
              <p style="text-align:center;color:#7a6d62;font-size:13px;margin:0 0 16px">Mã QR của đơn hàng: quét để mở nhanh đơn này (cần đăng nhập).</p>
              """)}
            {Button("Xem đơn hàng", orderUrl)}
            """);
    }

    public static string QuoteReceived(string siteName, string customerName, string code, string summary, decimal unitPrice, int quantity, decimal total, string? quoteUrl)
    {
        static string Money(decimal value) => value.ToString("#,0", System.Globalization.CultureInfo.GetCultureInfo("vi-VN")) + "₫";
        return Layout(siteName,
            $"""
            <p>Xin chào <strong>{E(customerName)}</strong>,</p>
            <p>Cửa hàng đã nhận yêu cầu báo giá <strong>{E(code)}</strong>: {E(summary)}.</p>
            <p>Giá dự kiến theo bảng giá của xưởng: <strong>{Money(unitPrice)}</strong>/sản phẩm × {quantity} = <strong>{Money(total)}</strong>.</p>
            <p>Đây là giá tham khảo. Nhân viên sẽ liên hệ để thống nhất chi tiết và gửi giá chính thức.</p>
            {(quoteUrl is null ? "" : Button("Theo dõi báo giá", quoteUrl))}
            """);
    }

    public static string QuoteAnswered(string siteName, string customerName, string code, decimal finalPrice, string? note, string? quoteUrl)
    {
        static string Money(decimal value) => value.ToString("#,0", System.Globalization.CultureInfo.GetCultureInfo("vi-VN")) + "₫";
        return Layout(siteName,
            $"""
            <p>Xin chào <strong>{E(customerName)}</strong>,</p>
            <p>Cửa hàng đã gửi giá chính thức cho yêu cầu <strong>{E(code)}</strong>: <strong>{Money(finalPrice)}</strong>.</p>
            {(string.IsNullOrWhiteSpace(note) ? "" : $"<p>Ghi chú: {E(note)}</p>")}
            {(quoteUrl is null ? "<p>Vui lòng liên hệ hotline để xác nhận.</p>" : Button("Xem và xác nhận báo giá", quoteUrl))}
            """);
    }

    public static string Layout(string siteName, string body) =>
        $"""
        <!DOCTYPE html>
        <html lang="vi"><head><meta charset="utf-8"><title>{E(siteName)}</title></head>
        <body style="margin:0;background:#faf7f2;font-family:Segoe UI,Arial,sans-serif;color:#2b2521">
          <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="padding:24px 0">
            <tr><td align="center">
              <table role="presentation" width="560" cellpadding="0" cellspacing="0" style="background:#ffffff;border:1px solid #e8e0d4;border-radius:12px">
                <tr><td style="padding:20px 28px;border-bottom:1px solid #e8e0d4;font-size:20px;font-weight:700;color:#5b3a24">{E(siteName)}</td></tr>
                <tr><td style="padding:24px 28px;font-size:15px;line-height:1.6">{body}</td></tr>
                <tr><td style="padding:16px 28px;border-top:1px solid #e8e0d4;font-size:12px;color:#7a6d62">Email tự động, vui lòng không trả lời.</td></tr>
              </table>
            </td></tr>
          </table>
        </body></html>
        """;

    private static string Button(string text, string url) =>
        $"""<p style="margin:24px 0"><a href="{E(url)}" style="background:#5b3a24;color:#ffffff;padding:12px 22px;border-radius:999px;text-decoration:none;font-weight:600">{E(text)}</a></p>""";

    private static string E(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
}
