using System.Globalization;

namespace Fratak_Shop.Core.Services.Seo.Autopilot;

/// <summary>یک مناسبت پیش رو و پیشنهاد محتوای آن.</summary>
/// <param name="Name">نام مناسبت (مثال: «شب یلدا»).</param>
/// <param name="Date">تاریخ میلادی مناسبت.</param>
/// <param name="DaysLeft">روزهای مانده.</param>
/// <param name="Topics">موضوع‌های پیشنهادی صفحه/مقاله (با نام دسته‌های واقعی فروشگاه).</param>
public sealed record SeoOccasion(string Name, DateOnly Date, int DaysLeft, IReadOnlyList<string> Topics);

/// <summary>
/// تقویم سئوی فصلی (مورد ۱۸۶ پیشنهاد سئو): گوگل برای ایندکس و رتبه گرفتن صفحهٔ مناسبتی چند هفته زمان لازم دارد؛
/// این کلاس ۴۵ روز قبل از هر مناسبت مهم ایرانی یادآوری می‌کند تا صفحه از قبل آماده باشد.
/// <para>مناسبت‌های شمسی با <see cref="PersianCalendar"/> و قمری با <see cref="UmAlQuraCalendar"/> محاسبه می‌شوند
/// (تقویم قمری ایران ممکن است یک روز اختلاف داشته باشد که برای یادآوری ۴۵ روزه مهم نیست).</para>
/// <para>کجا استفاده می‌شود؟ کار شبانه (ساخت «کار سئو» از نوع calendar) و داشبورد سئو.</para>
/// </summary>
public static class SeoCalendar
{
    private sealed record Occasion(string Name, Func<int, DateOnly?> Resolve, string[] Templates);

    private static readonly PersianCalendar Persian = new();
    private static readonly UmAlQuraCalendar Hijri = new();

    private static readonly Occasion[] Occasions =
    [
        new("نوروز", y => Shamsi(y, 1, 1), ["راهنمای خرید هدیهٔ نوروز: {0}", "پیشنهادهای ویژهٔ عید برای {0}"]),
        new("بازگشایی مدارس", y => Shamsi(y, 7, 1), ["راهنمای خرید {0} برای دانش‌آموزان", "لوازم ضروری بازگشایی مدارس در دستهٔ {0}"]),
        new("شب یلدا", y => Shamsi(y, 9, 30), ["هدیهٔ شب یلدا: بهترین {0}", "پیشنهادهای یلدایی {0}"]),
        new("روز دانشجو", y => Shamsi(y, 9, 16), ["هدیهٔ روز دانشجو از دستهٔ {0}"]),
        new("سپندارمذگان (روز عشق ایرانی)", y => Shamsi(y, 11, 29), ["هدیهٔ سپندارمذگان: انتخاب {0}"]),
        new("روز معلم", y => Shamsi(y, 2, 12), ["هدیهٔ روز معلم: {0} مناسب"]),
        new("ولنتاین", y => Gregorian(y, 2, 14), ["هدیهٔ ولنتاین از دستهٔ {0}"]),
        new("بلک فرایدی", y => BlackFriday(y), ["حراج بلک فرایدی {0}", "تخفیف‌های جمعهٔ سیاه برای {0}"]),
        new("روز مادر", y => Lunar(y, 6, 20), ["هدیهٔ روز مادر: راهنمای انتخاب {0}"]),
        new("روز پدر", y => Lunar(y, 7, 13), ["هدیهٔ روز پدر: بهترین {0}"]),
    ];

    /// <summary>
    /// مناسبت‌های <paramref name="horizonDays"/> روز آینده (پیش‌فرض ۴۵). <paramref name="categories"/> = نام دسته‌های پرفروش
    /// برای ساخت موضوع (مثال: «هدیهٔ شب یلدا: بهترین هدفون»).
    /// </summary>
    public static List<SeoOccasion> Upcoming(DateOnly today, IReadOnlyList<string> categories, int horizonDays = 45)
    {
        var result = new List<SeoOccasion>();
        var cats = categories.Where(c => !string.IsNullOrWhiteSpace(c)).Take(3).DefaultIfEmpty("محصولات").ToList();
        foreach (var occasion in Occasions)
        {
            DateOnly? next = null;
            foreach (var year in new[] { today.Year - 1, today.Year, today.Year + 1 })
            {
                DateOnly? date;
                try
                {
                    date = occasion.Resolve(year);
                }
                catch (ArgumentOutOfRangeException)
                {
                    date = null;
                }

                if (date is { } d && d >= today && (next is null || d < next))
                    next = d;
            }

            if (next is not { } when)
                continue;
            var days = when.DayNumber - today.DayNumber;
            if (days > horizonDays)
                continue;
            var topics = cats.SelectMany(c => occasion.Templates.Select(t => string.Format(CultureInfo.InvariantCulture, t, c)))
                .Distinct(StringComparer.Ordinal).Take(4).ToList();
            result.Add(new SeoOccasion(occasion.Name, when, days, topics));
        }

        return result.OrderBy(o => o.Date).ToList();
    }

    /// <summary>
    /// تاریخ میلادی روزی از سال شمسی که در سال میلادی <paramref name="gregorianYear"/> می‌افتد.
    /// (سال میلادی ورودی است تا جست‌وجوی «نزدیک‌ترین تاریخ آینده» ساده بماند.)
    /// </summary>
    private static DateOnly? Shamsi(int gregorianYear, int month, int day)
    {
        // سال شمسی‌ای که بیشتر آن در این سال میلادی است (از فروردین تا اسفند = مارس تا مارس).
        var shamsiYear = gregorianYear - 621;
        var date = Persian.ToDateTime(shamsiYear, month, day, 0, 0, 0, 0);
        return DateOnly.FromDateTime(date);
    }

    private static DateOnly? Gregorian(int year, int month, int day) => new DateOnly(year, month, day);

    private static DateOnly? BlackFriday(int year)
    {
        // آخرین جمعهٔ نوامبر (روز بعد از شکرگزاری = چهارمین پنجشنبه)
        var first = new DateOnly(year, 11, 1);
        var thursday = first.AddDays(((int)DayOfWeek.Thursday - (int)first.DayOfWeek + 7) % 7);
        return thursday.AddDays(21 + 1);
    }

    private static DateOnly? Lunar(int gregorianYear, int month, int day)
    {
        // سال قمری‌ای که این روزِ قمری در آن در سال میلادی داده‌شده می‌افتد (هر سال قمری ~۱۱ روز کوتاه‌تر است).
        var start = Hijri.GetYear(new DateTime(gregorianYear, 1, 1));
        foreach (var year in new[] { start, start + 1 })
        {
            DateTime date;
            try
            {
                date = Hijri.ToDateTime(year, month, Math.Min(day, Hijri.GetDaysInMonth(year, month)), 0, 0, 0, 0);
            }
            catch (ArgumentOutOfRangeException)
            {
                continue;
            }

            if (date.Year == gregorianYear)
                return DateOnly.FromDateTime(date);
        }

        return null;
    }
}
