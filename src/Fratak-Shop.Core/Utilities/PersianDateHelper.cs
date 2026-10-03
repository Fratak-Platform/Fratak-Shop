using System.Globalization;

namespace Fratak_Shop.Core.Utilities;

/// <summary>
/// Persian (Jalali) date helpers for every date shown in the UI.
/// Dates are stored as <see cref="DateTimeOffset"/> in UTC and converted only when rendered.
/// </summary>
public static class PersianDateHelper
{
    private static readonly PersianCalendar Calendar = new();

    /// <summary>۱۴۰۵/۰۷/۱۱</summary>
    public static string ToPersianDate(this DateTimeOffset value) => ToPersianDate(value, includeTime: false);

    /// <summary>۱۴۰۵/۰۷/۱۱ - ۱۴:۳۰</summary>
    public static string ToPersianDateTime(this DateTimeOffset value) => ToPersianDate(value, includeTime: true);

    public static string ToPersianDate(this DateTimeOffset? value) => value.HasValue ? value.Value.ToPersianDate() : "-";

    public static string ToPersianDateTime(this DateTimeOffset? value) => value.HasValue ? value.Value.ToPersianDateTime() : "-";

    private static string ToPersianDate(DateTimeOffset value, bool includeTime)
    {
        var local = value.ToLocalTime().DateTime;
        var year = Calendar.GetYear(local);
        var month = Calendar.GetMonth(local);
        var day = Calendar.GetDayOfMonth(local);

        var result = PriceHelper.ToPersianDigits($"{year:0000}/{month:00}/{day:00}");

        if (includeTime)
            result += " - " + PriceHelper.ToPersianDigits($"{local:HH:mm}");

        return result;
    }

    /// <summary>نام ماه شمسی، برای نمودارها و گزارش‌ها.</summary>
    public static string GetPersianMonthName(int month) => month switch
    {
        1 => "فروردین",
        2 => "اردیبهشت",
        3 => "خرداد",
        4 => "تیر",
        5 => "مرداد",
        6 => "شهریور",
        7 => "مهر",
        8 => "آبان",
        9 => "آذر",
        10 => "دی",
        11 => "بهمن",
        12 => "اسفند",
        _ => "-",
    };

    /// <summary>«۳ روز پیش» — زمان نسبی برای فهرست‌های پنل مدیریت.</summary>
    public static string ToRelativeTime(this DateTimeOffset value)
    {
        var span = DateTimeOffset.UtcNow - value;
        if (span.TotalSeconds < 60)
            return "لحظه‌ای پیش";

        if (span.TotalMinutes < 60)
            return PriceHelper.ToPersianDigits($"{(int)span.TotalMinutes}") + " دقیقه پیش";

        if (span.TotalHours < 24)
            return PriceHelper.ToPersianDigits($"{(int)span.TotalHours}") + " ساعت پیش";

        if (span.TotalDays < 30)
            return PriceHelper.ToPersianDigits($"{(int)span.TotalDays}") + " روز پیش";

        return value.ToPersianDate();
    }

    /// <summary>شروع روز به وقت محلی (برای گزارش‌های روزانه).</summary>
    public static DateTimeOffset StartOfDay(this DateTimeOffset value)
    {
        var local = value.ToLocalTime();
        return new DateTimeOffset(local.Year, local.Month, local.Day, 0, 0, 0, local.Offset);
    }
}
