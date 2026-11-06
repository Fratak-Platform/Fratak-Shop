using Fratak_Shop.Core.DTOs.Admin;
using P = Fratak_Shop.Core.Services.Admin.AdminPermissions;

namespace Fratak_Shop.Web.Infrastructure.Admin;

// ═══════════════════════════════════════════════════════════════════════════
//  داشبورد شخصی‌سازی‌شده (مرحلهٔ هفتم – فاز ۷ب)
//  هر کارمند ترتیب و نمایش ویجت‌های داشبورد را خودش انتخاب می‌کند (کشیدن و رها کردن یا دکمهٔ بالا/پایین).
//  ذخیره: تنظیم شخصی AdminUserPreference با کلید «dashboard.layout» (همان سرویس تنظیمات ستون جدول‌ها).
//  نمایش: Areas/Admin/Pages/Index.cshtml ؛ رفتار کشیدن/رها کردن: wwwroot/assets/js/admin.js ([data-sortable]).
//  افزودن ویجت تازه: یک ردیف به Widgets اضافه کنید و در Index.cshtml شاخهٔ نمایشش را بنویسید.
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// تعریف یک ویجت داشبورد.
/// </summary>
/// <param name="Key">کلید ثابت (در تنظیمات ذخیره می‌شود؛ تغییرش ندهید). مثال: «sales-chart».</param>
/// <param name="Span">پهنا روی صفحهٔ بزرگ از ۳ ستون (۱، ۲ یا ۳). روی موبایل همه تمام‌عرض‌اند.</param>
/// <param name="AnyOf">مجوزهایی که هر کدام برای دیدن ویجت کافی است؛ null = همه.</param>
public sealed record DashboardWidget(string Key, string Title, int Span, string[]? AnyOf = null)
{
    public bool IsAvailable(AdminAccess access) => AnyOf is null || access.HasAny(AnyOf);
}

/// <summary>تنظیم ذخیره‌شدهٔ هر کارمند: ترتیب کلیدها و کلیدهای پنهان.</summary>
public sealed class DashboardLayoutPreference
{
    public List<string> Order { get; set; } = [];
    public List<string> Hidden { get; set; } = [];
}

/// <summary>یک ویجت در چیدمان نهایی (برای فرم شخصی‌سازی، پنهان‌ها هم می‌آیند).</summary>
public sealed record ArrangedWidget(DashboardWidget Widget, bool Visible);

public static class DashboardLayout
{
    public const string PreferenceKey = "dashboard.layout";

    /// <summary>ویجت‌ها به ترتیب پیش‌فرض.</summary>
    public static readonly DashboardWidget[] Widgets =
    [
        new("tasks", "کارهای در انتظار", 3),
        new("sales-stats", "آمار فروش", 3, [P.FinanceView, P.OrdersView]),
        new("sales-chart", "نمودار فروش ۳۰ روز", 2, [P.FinanceView, P.OrdersView]),
        new("goal", "هدف فروش ماه", 1, [P.ReportsView, P.FinanceView, P.ExpensesManage]),
        new("top-products", "پرفروش‌ترین‌ها", 1, [P.FinanceView, P.OrdersView]),
        new("visitors-today", "بازدید امروز", 1, [P.ReportsView, P.FinanceView, P.MarketingManage]),
        new("expenses-due", "سررسید هزینه‌ها", 1, [P.FinanceView, P.ExpensesManage]),
        new("latest-orders", "آخرین سفارش‌ها", 2, [P.OrdersView]),
        new("low-stock", "موجودی کم و پرتقاضا", 1, [P.ProductsView]),
    ];

    public static DashboardWidget? Find(string key) => Widgets.FirstOrDefault(w => w.Key == key);

    /// <summary>
    /// چیدمان نهایی: ابتدا کلیدهای ذخیره‌شده به ترتیب کاربر (فقط ویجت‌های در دسترس او)، سپس ویجت‌های تازه‌ای که
    /// هنوز در تنظیمش نیستند به ترتیب پیش‌فرض (پس ویجت جدید خودکار برای همه نمایش داده می‌شود).
    /// کلیدهای ناشناخته یا تکراری نادیده گرفته می‌شوند.
    /// </summary>
    public static List<ArrangedWidget> Arrange(DashboardLayoutPreference? preference, Func<DashboardWidget, bool> isAvailable)
    {
        var available = Widgets.Where(isAvailable).ToList();
        var hidden = new HashSet<string>(preference?.Hidden ?? [], StringComparer.Ordinal);
        var result = new List<ArrangedWidget>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var key in preference?.Order ?? [])
        {
            var widget = available.FirstOrDefault(w => w.Key == key);
            if (widget is not null && seen.Add(key))
                result.Add(new ArrangedWidget(widget, !hidden.Contains(key)));
        }
        foreach (var widget in available.Where(w => seen.Add(w.Key)))
            result.Add(new ArrangedWidget(widget, !hidden.Contains(widget.Key)));
        return result;
    }

    /// <summary>
    /// ساخت تنظیم از فرم: <paramref name="order"/> = همهٔ کلیدها به ترتیب فهرست، <paramref name="visible"/> = کلیدهای تیک‌خورده.
    /// فقط کلیدهای معتبر نگه داشته می‌شوند (حداکثر به تعداد ویجت‌ها) تا مقدار دلخواه در تنظیمات ننشیند.
    /// </summary>
    public static DashboardLayoutPreference FromForm(IEnumerable<string>? order, IEnumerable<string>? visible)
    {
        var known = Widgets.Select(w => w.Key).ToHashSet(StringComparer.Ordinal);
        var ordered = (order ?? []).Where(known.Contains).Distinct(StringComparer.Ordinal).Take(Widgets.Length).ToList();
        var shown = (visible ?? []).Where(known.Contains).ToHashSet(StringComparer.Ordinal);
        return new DashboardLayoutPreference
        {
            Order = ordered,
            Hidden = ordered.Where(k => !shown.Contains(k)).ToList(),
        };
    }

    /// <summary>کلاس پهنای ویجت در شبکهٔ داشبورد.</summary>
    public static string SpanClass(int span) => span switch
    {
        >= 3 => "a-w-3",
        2 => "a-w-2",
        _ => "a-w-1",
    };
}
