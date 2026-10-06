using System.Globalization;

namespace FurnitureStore.Web.Infrastructure;

/// <summary>
/// Display formatting for Vietnamese users. Parsing (model binding) uses the invariant culture - see Program.cs -
/// so number inputs behave the same on every server locale; only output is localized here.
/// </summary>
public static class Format
{
    private static readonly CultureInfo Vietnamese = CultureInfo.GetCultureInfo("vi-VN");
    private static readonly TimeZoneInfo VietnamTime = ResolveTimeZone();

    /// <summary>16900000 → "16.900.000₫".</summary>
    public static string Money(decimal value) => value.ToString("#,##0", Vietnamese) + "₫";

    public static string Money(decimal? value) => value.HasValue ? Money(value.Value) : string.Empty;

    public static string Number(long value) => value.ToString("#,##0", Vietnamese);

    /// <summary>UTC → Vietnam local time "dd/MM/yyyy HH:mm".</summary>
    public static string DateTimeText(DateTime utc) => ToVietnamTime(utc).ToString("dd/MM/yyyy HH:mm", Vietnamese);

    public static string DateText(DateTime utc) => ToVietnamTime(utc).ToString("dd/MM/yyyy", Vietnamese);

    public static string Dimensions(int? length, int? width, int? height) =>
        length.HasValue && width.HasValue && height.HasValue
            ? $"{Number(length.Value)} x {Number(width.Value)} x {Number(height.Value)} mm"
            : string.Empty;

    public static System.DateTime ToVietnamTime(System.DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(System.DateTime.SpecifyKind(utc, DateTimeKind.Utc), VietnamTime);

    private static TimeZoneInfo ResolveTimeZone()
    {
        foreach (var id in new[] { "Asia/Ho_Chi_Minh", "SE Asia Standard Time" })
        {
            if (TimeZoneInfo.TryFindSystemTimeZoneById(id, out var zone))
            {
                return zone;
            }
        }

        return TimeZoneInfo.CreateCustomTimeZone("ICT", TimeSpan.FromHours(7), "Indochina Time", "Indochina Time");
    }
}
