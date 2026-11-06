using Fratak_Shop.Core.DTOs.Admin;
using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Core.Services.Products;
using Fratak_Shop.Data.Entities.Products;
using Fratak_Shop.Web.Infrastructure.Admin;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Areas.Admin.Pages.Products;

/// <summary>
/// صفحهٔ «انبار و موجودی» (مرحلهٔ هفتم – فاز ۵):
/// زبانهٔ «پیش‌بینی اتمام» (چند روز دیگر هر کالا تمام می‌شود و چقدر باید سفارش داد) و
/// زبانهٔ «گردش موجودی» (دفتر همهٔ کم و زیاد شدن‌ها با دلیل، سفارش و کاربر).
/// منطق در Core/Services/Products/InventoryService.cs است.
/// </summary>
[AdminPermission(AdminPermissions.ProductsView)]
public class InventoryModel(IInventoryService inventory) : AdminPageModel
{
    /// <summary>بازه‌های مجاز محاسبهٔ سرعت فروش (روز).</summary>
    public static readonly int[] Windows = [14, 30, 60, 90];

    private const int PageSize = 50;

    [BindProperty(SupportsGet = true)] public string Tab { get; set; } = "forecast";
    [BindProperty(SupportsGet = true)] public int Days { get; set; } = 30;
    [BindProperty(SupportsGet = true)] public StockForecastLevel? Level { get; set; }
    [BindProperty(SupportsGet = true, Name = "q")] public string? Search { get; set; }
    [BindProperty(SupportsGet = true)] public StockMovementReason? Reason { get; set; }
    [BindProperty(SupportsGet = true)] public Guid? Product { get; set; }
    [BindProperty(SupportsGet = true)] public string? From { get; set; }
    [BindProperty(SupportsGet = true)] public string? To { get; set; }

    public InventorySummary Summary { get; private set; } = null!;
    public PagedList<StockForecastRow> Forecast { get; private set; } = new([], 0, 1, PageSize);
    public PagedList<StockMovementRow> Movements { get; private set; } = new([], 0, 1, PageSize);

    public async Task OnGetAsync([FromQuery(Name = "page")] int pageNumber = 1)
    {
        if (!Windows.Contains(Days))
            Days = 30;
        if (Tab is not ("forecast" or "ledger"))
            Tab = "forecast";
        var page = ClampPage(pageNumber);
        var ct = HttpContext.RequestAborted;

        Summary = await inventory.SummaryAsync(Days, ct);
        if (Tab == "forecast")
        {
            var rows = await inventory.ForecastAsync(Days, Level, Search, ct);
            Forecast = new PagedList<StockForecastRow>(rows.Skip((page - 1) * PageSize).Take(PageSize).ToList(), rows.Count, page, PageSize);
        }
        else
        {
            var period = PersianDateInput.Period(From, To);
            var (items, total) = await inventory.ListMovementsAsync(
                new StockMovementQuery(Product, Reason, Search, period.From, period.To, page, PageSize), ct);
            Movements = new PagedList<StockMovementRow>(items, total, page, PageSize);
        }
    }

    // ── کمک‌های نمایش ────────────────────────────────────────────────────

    public static string LevelTitle(StockForecastLevel level) => level switch
    {
        StockForecastLevel.OutOfStock => "تمام شده",
        StockForecastLevel.Critical => "بحرانی",
        StockForecastLevel.Soon => "به‌زودی",
        StockForecastLevel.Ok => "کافی",
        _ => "بدون فروش",
    };

    public static string LevelCss(StockForecastLevel level) => level switch
    {
        StockForecastLevel.OutOfStock => "a-badge-red",
        StockForecastLevel.Critical => "a-badge-red",
        StockForecastLevel.Soon => "a-badge-yellow",
        StockForecastLevel.Ok => "a-badge-green",
        _ => "a-badge-gray",
    };

    public static string DeltaCss(int delta) => delta > 0 ? "text-emerald-600" : "text-rose-600";

    public static string DeltaText(int delta) => (delta > 0 ? "+" : "−") + Math.Abs(delta).ToString("N0");

    public static string RateText(decimal rate) => rate.ToString("0.##", global::System.Globalization.CultureInfo.InvariantCulture);
}
