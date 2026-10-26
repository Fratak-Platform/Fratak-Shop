using System.Globalization;

namespace Fratak_Shop.Web.Helpers;

/// <summary>
/// Formatting helpers used by the views. Digits are rendered with the
/// brand font (Vazirmatn, digits mapped to Persian shapes – wwwroot/assets/font/README.md), so plain Latin digits show up as Persian digits.
/// </summary>
public static class Format
{
    private static readonly PersianCalendar PersianCalendar = new();

    private static readonly string[] PersianMonths =
    [
        "فروردین", "اردیبهشت", "خرداد", "تیر", "مرداد", "شهریور",
        "مهر", "آبان", "آذر", "دی", "بهمن", "اسفند",
    ];

    private static readonly TimeZoneInfo IranTimeZone = Fratak_Shop.Core.Utilities.IranClock.TimeZone;

    /// <summary>1350000 → "1,350,000"</summary>
    public static string Price(decimal amount) => amount.ToString("#,0", CultureInfo.InvariantCulture);

    /// <summary>1350000 → "1,350,000 تومان"</summary>
    public static string Toman(decimal amount) => $"{Price(amount)} تومان";

    public static string Number(int value) => value.ToString("#,0", CultureInfo.InvariantCulture);
    public static string Number(long value) => value.ToString("#,0", CultureInfo.InvariantCulture);

    /// <summary>Solar Hijri date, e.g. "1405/08/01".</summary>
    public static string Date(DateTimeOffset date)
    {
        var local = ToIranTime(date);
        return $"{PersianCalendar.GetYear(local):0000}/{PersianCalendar.GetMonth(local):00}/{PersianCalendar.GetDayOfMonth(local):00}";
    }

    public static string Date(DateTimeOffset? date) => date.HasValue ? Date(date.Value) : "-";

    public static string DateTime(DateTimeOffset date) => $"{Date(date)} - {ToIranTime(date):HH:mm}";

    public static string DateTime(DateTimeOffset? date) => date.HasValue ? DateTime(date.Value) : "-";

    /// <summary>Local calendar day (delivery days): "شنبه ۳ آبان".</summary>
    public static string Day(DateOnly date)
    {
        var value = date.ToDateTime(TimeOnly.MinValue);
        return $"{DayName(value.DayOfWeek)} {PersianCalendar.GetDayOfMonth(value)} {PersianMonths[PersianCalendar.GetMonth(value) - 1]}";
    }

    /// <summary>Signed wallet amount: "+150,000" / "-90,000".</summary>
    public static string SignedPrice(decimal amount) => amount > 0 ? $"+{Price(amount)}" : $"-{Price(Math.Abs(amount))}";

    /// <summary>Long Solar Hijri date, e.g. "1 آبان 1405".</summary>
    public static string LongDate(DateTimeOffset date)
    {
        var local = ToIranTime(date);
        return $"{PersianCalendar.GetDayOfMonth(local)} {PersianMonths[PersianCalendar.GetMonth(local) - 1]} {PersianCalendar.GetYear(local)}";
    }

    /// <summary>Month and year, e.g. "آبان 1405".</summary>
    public static string MonthYear(DateTimeOffset date)
    {
        var local = ToIranTime(date);
        return $"{PersianMonths[PersianCalendar.GetMonth(local) - 1]} {PersianCalendar.GetYear(local)}";
    }

    public static string DayName(DateTimeOffset date) => DayName(ToIranTime(date).DayOfWeek);

    public static string DayName(DayOfWeek day) => day switch
    {
        DayOfWeek.Saturday => "شنبه",
        DayOfWeek.Sunday => "یکشنبه",
        DayOfWeek.Monday => "دوشنبه",
        DayOfWeek.Tuesday => "سه‌شنبه",
        DayOfWeek.Wednesday => "چهارشنبه",
        DayOfWeek.Thursday => "پنجشنبه",
        _ => "جمعه",
    };

    /// <summary>Solar Hijri year, e.g. "1405".</summary>
    public static string Year(DateTimeOffset date) => PersianCalendar.GetYear(ToIranTime(date)).ToString();

    public static string DayOfMonth(DateTimeOffset date) => PersianCalendar.GetDayOfMonth(ToIranTime(date)).ToString();

    public static string MonthName(DateTimeOffset date) => PersianMonths[PersianCalendar.GetMonth(ToIranTime(date)) - 1];

    /// <summary>Estimated reading time of an HTML article in minutes.</summary>
    public static int ReadingMinutes(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return 1;

        var text = System.Text.RegularExpressions.Regex.Replace(html, "<[^>]+>", " ");
        var words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
        return Math.Max(1, (int)Math.Ceiling(words / 200d));
    }

    /// <summary>Plain-text excerpt of an HTML string.</summary>
    public static string Excerpt(string? html, int length = 140)
    {
        if (string.IsNullOrWhiteSpace(html))
            return string.Empty;

        var text = System.Text.RegularExpressions.Regex.Replace(html, "<[^>]+>", " ");
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();
        return text.Length <= length ? text : text[..length].TrimEnd() + "...";
    }

    /// <summary>Unix milliseconds, for the JavaScript countdown timers.</summary>
    public static long UnixMilliseconds(DateTimeOffset date) => date.ToUnixTimeMilliseconds();

    /// <summary>
    /// زمان نسبی کوتاه برای فهرست اعلان‌ها و فعالیت‌ها.
    /// مثال: «همین حالا»، «۵ دقیقه پیش»، «۳ ساعت پیش»، «دیروز»؛ قدیمی‌تر از یک هفته → تاریخ شمسی.
    /// </summary>
    public static string Relative(DateTimeOffset date)
    {
        var elapsed = DateTimeOffset.UtcNow - date;
        if (elapsed < TimeSpan.FromMinutes(1))
            return "همین حالا";
        if (elapsed < TimeSpan.FromHours(1))
            return $"{(int)elapsed.TotalMinutes} دقیقه پیش";
        if (elapsed < TimeSpan.FromDays(1))
            return $"{(int)elapsed.TotalHours} ساعت پیش";
        if (elapsed < TimeSpan.FromDays(2))
            return "دیروز";
        if (elapsed < TimeSpan.FromDays(7))
            return $"{(int)elapsed.TotalDays} روز پیش";
        return Date(date);
    }

    private static System.DateTime ToIranTime(DateTimeOffset date) => TimeZoneInfo.ConvertTime(date, IranTimeZone).DateTime;
}
