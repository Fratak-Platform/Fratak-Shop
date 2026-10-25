using Fratak_Shop.Core.DTOs.Admin;
using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Core.Services.Orders;
using Fratak_Shop.Data.Entities.Orders;
using Fratak_Shop.Web.Infrastructure.Admin;
using Fratak_Shop.Web.Infrastructure.Shipping;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Areas.Admin.Pages.Orders;

/// <summary>
/// صفحهٔ «رهگیری مرسوله‌ها» (مرحلهٔ هفتم – فاز ۶): همهٔ مرسوله‌های ثبت‌شده با وضعیت آخرین رویداد پست،
/// زبانهٔ «نیازمند توجه» (برگشتی، خطای پیاپی، ثبت‌نشده در سامانه، متوقف‌شده بدون تحویل) و دکمهٔ «همین الان بررسی کن».
/// منطق در Core/Services/Orders/ShipmentTrackingService.cs؛ زمان‌بندی در Web/Infrastructure/Shipping.
/// </summary>
[AdminPermission(AdminPermissions.OrdersView)]
public class ShipmentsModel(IShipmentTrackingService shipments, PostTrackingQueue queue) : AdminPageModel
{
    private const int PageSize = 30;

    [BindProperty(SupportsGet = true)] public string Tab { get; set; } = "active";
    [BindProperty(SupportsGet = true)] public ShippingCarrier? Carrier { get; set; }
    [BindProperty(SupportsGet = true, Name = "q")] public string? Search { get; set; }

    public ShipmentStats Stats { get; private set; } = null!;
    public PagedList<ShipmentRow> Rows { get; private set; } = new([], 0, 1, PageSize);

    /// <summary>درخواست «بررسی همین حالا» در صف است و هنوز شروع نشده.</summary>
    public bool RunPending => queue.HasPending;

    /// <summary>زبانه‌ها: (کلید آدرس، عنوان، مقدار).</summary>
    public static readonly (string Key, string Title, ShipmentTab Value)[] Tabs =
    [
        ("active", "در حال رهگیری", ShipmentTab.Active),
        ("problems", "نیازمند توجه", ShipmentTab.Problems),
        ("delivered", "تحویل‌شده", ShipmentTab.Delivered),
        ("all", "همه", ShipmentTab.All),
    ];

    public async Task OnGetAsync([FromQuery(Name = "page")] int pageNumber = 1)
    {
        var tab = Tabs.FirstOrDefault(t => t.Key == Tab);
        if (tab.Key is null)
        {
            tab = Tabs[0];
            Tab = tab.Key;
        }

        var ct = HttpContext.RequestAborted;
        Stats = await shipments.GetStatsAsync(ct);
        Rows = await shipments.ListAsync(new ShipmentQuery(tab.Value, Carrier, Search, ClampPage(pageNumber), PageSize), ct);
    }

    /// <summary>بررسی فوری همهٔ مرسوله‌های فعال در پس‌زمینه (صفحه منتظر نمی‌ماند).</summary>
    [AdminPermission(AdminPermissions.OrdersManage)]
    public IActionResult OnPostRun()
    {
        if (queue.Request())
            SuccessMessage = "بررسی همهٔ مرسوله‌های فعال شروع شد؛ نتیجه تا چند دقیقهٔ دیگر در همین صفحه دیده می‌شود.";
        else
            ErrorMessage = "یک درخواست بررسی در صف است؛ کمی صبر کنید.";
        return RedirectToPage(new { tab = Tab });
    }
}
