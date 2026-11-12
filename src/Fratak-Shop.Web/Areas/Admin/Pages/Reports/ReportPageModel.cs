using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Web.Infrastructure.Admin;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Areas.Admin.Pages.Reports;

/// <summary>
/// پایهٔ صفحه‌های «گزارش‌ها» (مرحلهٔ هفتم – فاز ۷): انتخاب بازه (دکمه‌های آماده + از/تا تاریخ شمسی) را یک‌جا نگه می‌دارد.
/// <para>نمای مشترک بازه: Shared/_ReportPeriod.cshtml. استفاده: Reports/Index، Products، Demand، Campaigns.</para>
/// </summary>
public abstract class ReportPageModel : AdminPageModel
{
    /// <summary>کلید بازهٔ آماده. مثال: «30d»، «month». پیش‌فرض هر صفحه با <see cref="DefaultPreset"/>.</summary>
    [BindProperty(SupportsGet = true, Name = "period")] public string? Preset { get; set; }

    /// <summary>از تاریخ شمسی دلخواه. مثال: «1405/07/01».</summary>
    [BindProperty(SupportsGet = true, Name = "from")] public string? From { get; set; }

    /// <summary>تا تاریخ شمسی دلخواه (همان روز هم شامل است).</summary>
    [BindProperty(SupportsGet = true, Name = "to")] public string? To { get; set; }

    public ResolvedPeriod Period { get; private set; } = null!;

    /// <summary>نشانی همین صفحه برای دکمه‌های بازه. مثال: «/admin/reports/products».</summary>
    public abstract string BasePath { get; }

    protected virtual string DefaultPreset => "30d";

    /// <summary>آیا کاربر سود و قیمت خرید را می‌بیند (گزارش مالی یا دسترسی تأمین‌کننده).</summary>
    public bool CanSeeProfit => CanAny(AdminPermissions.FinanceView, AdminPermissions.ProductsSupplier);

    protected void ResolvePeriod() => Period = AdminPeriods.Resolve(Preset, From, To, DefaultPreset);
}
