namespace FurnitureStore.Application.Common.Validation;

public static class ValidationPatterns
{
    /// <summary>Vietnamese mobile numbers: 0 or +84 followed by 3/5/7/8/9 and 8 digits, e.g. 0912345678.</summary>
    public const string VietnamesePhone = @"^(0|\+84)(3|5|7|8|9)\d{8}$";

    public const string VietnamesePhoneMessage = "Số điện thoại không hợp lệ (ví dụ: 0912345678).";

    /// <summary>Removes spaces, dots and dashes users often type: "091 234.5678" → "0912345678".</summary>
    public static string NormalizePhone(string? phone) =>
        new((phone ?? string.Empty).Where(c => char.IsDigit(c) || c == '+').ToArray());
}
