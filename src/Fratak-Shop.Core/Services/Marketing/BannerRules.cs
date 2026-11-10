using System.Globalization;
using Fratak_Shop.Core.DTOs.Banners.Query;
using Fratak_Shop.Data.Entities.Marketing;

namespace Fratak_Shop.Core.Services.Marketing;

/// <summary>
/// تقویم روزهای سوگواری رسمی ایران (دور ۲۳) برای تنظیم «توقف بنرهای تبلیغاتی در روزهای سوگواری» (پیش‌فرض خاموش).
/// <para>روزهای قمری با <see cref="UmAlQuraCalendar"/> حساب می‌شوند (ممکن است با تقویم رسمی ایران یک روز اختلاف داشته باشد؛
/// برای همین روز قبل و بعدِ رسمی را نمی‌گیریم تا بی‌دلیل بنرها خاموش نمانند). روزهای شمسی با <see cref="PersianCalendar"/>.</para>
/// <para>مثال: ۱۰ محرم ← «عاشورا»؛ ۱۴ خرداد ← «رحلت امام خمینی»؛ روز عادی ← null.</para>
/// <para>محل استفاده: BannerService.GetActiveBannersAsync و ایده‌ساز بنر (روز سوگواری ایدهٔ جشنواره‌ای نمی‌سازد).</para>
/// </summary>
public static class MourningCalendar
{
    private static readonly UmAlQuraCalendar Hijri = new();
    private static readonly PersianCalendar Persian = new();
    private static readonly TimeSpan Tehran = TimeSpan.FromHours(3.5);

    // (ماه قمری، روز، نام)
    private static readonly (int Month, int Day, string Name)[] Lunar =
    [
        (1, 9, "تاسوعای حسینی"),
        (1, 10, "عاشورای حسینی"),
        (2, 20, "اربعین حسینی"),
        (2, 28, "رحلت پیامبر اکرم (ص) و شهادت امام حسن مجتبی (ع)"),
        (2, 30, "شهادت امام رضا (ع)"),
        (3, 8, "شهادت امام حسن عسکری (ع)"),
        (6, 3, "شهادت حضرت فاطمه زهرا (س)"),
        (9, 21, "شهادت حضرت علی (ع)"),
        (10, 25, "شهادت امام جعفر صادق (ع)"),
    ];

    // (ماه شمسی، روز، نام)
    private static readonly (int Month, int Day, string Name)[] Solar =
    [
        (3, 14, "رحلت امام خمینی"),
    ];

    /// <summary>نام سوگواری امروز (به وقت ایران) یا null. مثال: عاشورا ← «عاشورای حسینی».</summary>
    public static string? Today(DateTimeOffset now)
    {
        var local = now.ToOffset(Tehran).DateTime;
        int hMonth, hDay;
        try
        {
            hMonth = Hijri.GetMonth(local);
            hDay = Hijri.GetDayOfMonth(local);
        }
        catch (ArgumentOutOfRangeException)
        {
            hMonth = 0;
            hDay = 0;
        }

        foreach (var (month, day, name) in Lunar)
        {
            // صفر گاهی ۲۹ روزه است؛ «۳۰ صفر» در آن سال = روز آخر ماه.
            if (month == hMonth && (day == hDay || (day == 30 && hDay == 29 && Hijri.GetDaysInMonth(Hijri.GetYear(local), month) == 29)))
                return name;
        }

        var pMonth = Persian.GetMonth(local);
        var pDay = Persian.GetDayOfMonth(local);
        foreach (var (month, day, name) in Solar)
        {
            if (month == pMonth && day == pDay)
                return name;
        }
        return null;
    }

    /// <summary>آیا امروز روز سوگواری رسمی است؟</summary>
    public static bool IsMourning(DateTimeOffset now) => Today(now) is not null;
}

/// <summary>
/// قانون‌های نمایش بنر (دور ۲۳) – بدون پایگاه داده تا با آزمون واحد کامل سنجیده شود.
/// <list type="bullet">
/// <item><b>A/B</b> (<see cref="Rotate"/>): بنرهای هم‌جایگاه با «گروه نسخه» یکسان فقط یکی‌شان در هر نمایش دیده می‌شود.
/// تا وقتی هر نسخه کمتر از <see cref="MinViewsForWinner"/> نمایش دارد، کم‌نمایش‌ترین نشان داده می‌شود (دادهٔ منصفانه)؛
/// بعد ۸۰٪ نسخهٔ با نرخ کلیک بهتر و ۲۰٪ بقیه (تا اگر سلیقهٔ مشتری عوض شد، دیده شود).</item>
/// <item><b>سوگواری</b> (<see cref="ForMourning"/>): فقط بنرهای گروه «mourning» و سربرگ دسته‌ها می‌مانند.</item>
/// </list>
/// <para>محل استفاده: BannerService.GetActiveBannersAsync (صفحهٔ نخست).</para>
/// </summary>
public static class BannerRules
{
    /// <summary>گروه نسخهٔ ویژهٔ روزهای سوگواری.</summary>
    public const string MourningGroup = "mourning";

    /// <summary>کمترین نمایش هر نسخه پیش از انتخاب برنده.</summary>
    public const int MinViewsForWinner = 300;

    /// <summary>
    /// نرخ کلیک هموارشده (برای نمایش کم، عدد پرت نمی‌دهد). مثال: ۰ نمایش ← ۰٫۵؛ ۱۰۰۰ نمایش و ۳۰ کلیک ← ≈۰٫۰۳.
    /// </summary>
    public static double Ctr(int views, int clicks) => (Math.Max(0, clicks) + 1.0) / (Math.Max(0, views) + 2.0);

    /// <summary>نرخ کلیک خوانا برای پنل. مثال: (۱۰۰۰، ۲۵) ← «۲٫۵٪»؛ نمایش صفر ← «—».</summary>
    public static string CtrText(int views, int clicks) =>
        views <= 0 ? "—" : (Math.Min(clicks, views) * 100.0 / views).ToString("0.#", CultureInfo.InvariantCulture) + "%";

    /// <summary>
    /// از هر گروه نسخه یکی را نگه می‌دارد (ترتیب نمایش حفظ می‌شود). بنر بدون گروه همیشه می‌ماند.
    /// <paramref name="random"/> عددی بین ۰ و ۱ می‌دهد (در آزمون ثابت).
    /// </summary>
    public static List<BannerDto> Rotate(IReadOnlyList<BannerDto> banners, Func<double> random)
    {
        ArgumentNullException.ThrowIfNull(banners);
        ArgumentNullException.ThrowIfNull(random);
        var chosen = new Dictionary<(BannerPosition, string), Guid>();
        foreach (var group in banners.Where(b => !string.IsNullOrWhiteSpace(b.VariantGroup))
                     .GroupBy(b => (b.Position, b.VariantGroup!.Trim().ToLowerInvariant())))
        {
            chosen[group.Key] = Choose(group.ToList(), random).Id;
        }

        return banners.Where(b => string.IsNullOrWhiteSpace(b.VariantGroup)
                                  || chosen[(b.Position, b.VariantGroup!.Trim().ToLowerInvariant())] == b.Id)
            .ToList();
    }

    /// <summary>انتخاب یک نسخه از گروه (توضیح در خلاصهٔ کلاس).</summary>
    public static BannerDto Choose(IReadOnlyList<BannerDto> variants, Func<double> random)
    {
        if (variants.Count == 1)
            return variants[0];
        if (variants.Any(v => v.Views < MinViewsForWinner))
            return variants.OrderBy(v => v.Views).ThenBy(v => v.DisplayOrder).ThenBy(v => v.Id).First();
        var best = variants.OrderByDescending(v => Ctr(v.Views, v.Clicks)).ThenBy(v => v.DisplayOrder).First();
        var roll = Math.Clamp(random(), 0, 0.999999);
        if (roll < 0.8)
            return best;
        var others = variants.Where(v => v.Id != best.Id).ToList();
        var index = (int)((roll - 0.8) / 0.2 * others.Count);
        return others[Math.Clamp(index, 0, others.Count - 1)];
    }

    /// <summary>روز سوگواری: فقط بنرهای گروه «mourning» و سربرگ دسته‌ها.</summary>
    public static List<BannerDto> ForMourning(IEnumerable<BannerDto> banners) =>
        banners.Where(b => b.Position == BannerPosition.CategoryHeader
                           || string.Equals(b.VariantGroup?.Trim(), MourningGroup, StringComparison.OrdinalIgnoreCase))
            .ToList();

    /// <summary>
    /// گروه نسخهٔ معتبر: حروف/عدد لاتین، خط تیره و زیرخط، حداکثر ۶۰ نویسه؛ بقیه حذف. خالی ← null.
    /// مثال: « Yalda 1405 » ← «yalda-1405».
    /// </summary>
    public static string? NormalizeGroup(string? group)
    {
        if (string.IsNullOrWhiteSpace(group))
            return null;
        var chars = group.Trim().ToLowerInvariant()
            .Select(c => c is ' ' ? '-' : c)
            .Where(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-' or '_')
            .ToArray();
        var value = new string(chars).Trim('-');
        if (value.Length > 60)
            value = value[..60];
        return value.Length == 0 ? null : value;
    }
}
