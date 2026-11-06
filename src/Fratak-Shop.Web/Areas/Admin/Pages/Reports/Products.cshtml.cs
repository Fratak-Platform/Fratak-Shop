using Fratak_Shop.Core.DTOs.Admin;
using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Core.Services.Reports;
using Fratak_Shop.Web.Infrastructure.Admin;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Areas.Admin.Pages.Reports;

/// <summary>
/// صفحهٔ «عملکرد محصولات» (/admin/reports/products، مرحلهٔ هفتم – فاز ۷):
/// برای هر محصول در بازه: بازدید، افزودن به سبد، سفارش، تعداد و مبلغ فروش، نرخ تبدیل، مرجوعی و (برای مالی) سود.
/// فیلتر «پربازدید بی‌فروش» محصول‌هایی را نشان می‌دهد که احتمالاً قیمت، عکس یا توضیحشان مشکل دارد.
/// </summary>
[AdminPermission(AdminPermissions.ReportsView, AdminPermissions.FinanceView, AdminPermissions.MarketingManage)]
public class ProductsModel(IAnalyticsReportService reports) : ReportPageModel
{
    public override string BasePath => "/admin/reports/products";

    /// <summary>مرتب‌سازی: revenue | units | views | carts | conversion | profit | returns.</summary>
    [BindProperty(SupportsGet = true)] public string Sort { get; set; } = "revenue";

    /// <summary>فیلتر: all | sold | nosales | out.</summary>
    [BindProperty(SupportsGet = true)] public string Filter { get; set; } = "all";

    [BindProperty(SupportsGet = true, Name = "q")] public string? Search { get; set; }

    public PagedList<ProductPerformanceRow> Rows { get; private set; } = new([], 0, 1, 50);

    public static readonly (string Key, string Title)[] Sorts =
    [
        ("revenue", "بیشترین فروش (مبلغ)"), ("units", "بیشترین تعداد فروش"), ("views", "بیشترین بازدید"),
        ("carts", "بیشترین افزودن به سبد"), ("conversion", "بهترین نرخ تبدیل"), ("profit", "بیشترین سود"), ("returns", "بیشترین مرجوعی"),
    ];

    public static readonly (string Key, string Title)[] Filters =
    [
        ("all", "همهٔ محصولات دارای فعالیت"), ("sold", "فروش‌رفته"), ("nosales", "بازدید دارد، فروش ندارد"), ("out", "ناموجود"),
    ];

    public async Task OnGetAsync([FromQuery(Name = "page")] int pageNumber = 1)
    {
        ResolvePeriod();
        if (!Sorts.Any(s => s.Key == Sort) || (Sort == "profit" && !CanSeeProfit))
            Sort = "revenue";
        if (!Filters.Any(f => f.Key == Filter))
            Filter = "all";
        Rows = await reports.GetProductPerformanceAsync(
            new ProductPerformanceQuery(Period.Current, Sort, Filter, Search, Math.Max(1, pageNumber), 50), CanSeeProfit, HttpContext.RequestAborted);
    }
}
