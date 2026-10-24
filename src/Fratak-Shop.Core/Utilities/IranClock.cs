namespace Fratak_Shop.Core.Utilities;

/// <summary>
/// Local (Iran) calendar helpers. Delivery days and time slots are local concepts,
/// while everything is stored in UTC-aware <see cref="DateTimeOffset"/>.
/// </summary>
public static class IranClock
{
    private static readonly TimeSpan FallbackOffset = TimeSpan.FromHours(3.5);

    public static readonly TimeZoneInfo TimeZone = ResolveTimeZone();

    private static TimeZoneInfo ResolveTimeZone()
    {
        foreach (var id in new[] { "Asia/Tehran", "Iran Standard Time" })
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (TimeZoneNotFoundException)
            {
            }
            catch (InvalidTimeZoneException)
            {
            }
        }

        // Containers without tzdata: Iran has no daylight saving time since 2022.
        return TimeZoneInfo.CreateCustomTimeZone("Iran", FallbackOffset, "Iran", "Iran");
    }

    public static DateTimeOffset ToLocal(DateTimeOffset value) => TimeZoneInfo.ConvertTime(value, TimeZone);

    public static DateOnly Today(DateTimeOffset now) => DateOnly.FromDateTime(ToLocal(now).DateTime);

    /// <summary>Local midnight of the given date as an absolute point in time.</summary>
    public static DateTimeOffset StartOfDay(DateOnly date)
    {
        var local = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, TimeZone.GetUtcOffset(local));
    }

    /// <summary>The given local date and time as an absolute point in time.</summary>
    public static DateTimeOffset At(DateOnly date, TimeOnly time)
    {
        var local = date.ToDateTime(time, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, TimeZone.GetUtcOffset(local));
    }
}
