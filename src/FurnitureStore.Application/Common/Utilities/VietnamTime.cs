namespace FurnitureStore.Application.Common.Utilities;

/// <summary>Vietnam (UTC+7, no DST) calendar helpers: "today" and "this month" are Vietnamese days, not UTC days.</summary>
public static class VietnamTime
{
    public static readonly TimeZoneInfo Zone = Resolve();

    public static DateTime ToLocal(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Zone);

    /// <summary>UTC instant of 00:00 (Vietnam time) of the given local date.</summary>
    public static DateTime StartOfDayUtc(DateOnly localDate) =>
        TimeZoneInfo.ConvertTimeToUtc(localDate.ToDateTime(TimeOnly.MinValue), Zone);

    public static DateOnly Today(DateTime utcNow) => DateOnly.FromDateTime(ToLocal(utcNow));

    /// <summary>UTC instant of a Vietnam wall-clock time (e.g. a datetime-local form value).</summary>
    public static DateTime ToUtc(DateTime local) =>
        TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), Zone);

    private static TimeZoneInfo Resolve()
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
