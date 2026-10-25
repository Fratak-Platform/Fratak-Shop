using Fratak_Shop.Core.DTOs.Admin;
using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Core.Services.Orders;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Entities.Orders;
using Fratak_Shop.Web.Helpers;
using Fratak_Shop.Web.Infrastructure.Admin;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Areas.Admin.Pages.Orders;

[AdminPermission(AdminPermissions.OrdersView)]
public class IndexModel(IAdminOrderService orders, IOrderSlaService sla) : AdminPageModel
{
    [BindProperty(SupportsGet = true, Name = "q")] public string? Search { get; set; }
    [BindProperty(SupportsGet = true)] public OrderStatus? Status { get; set; }
    [BindProperty(SupportsGet = true, Name = "payment")] public PaymentStatus? Payment { get; set; }
    [BindProperty(SupportsGet = true, Name = "method")] public PaymentMethod? Method { get; set; }
    [BindProperty(SupportsGet = true)] public string? From { get; set; }
    [BindProperty(SupportsGet = true)] public string? To { get; set; }
    [BindProperty(SupportsGet = true)] public string? Invoice { get; set; }
    [BindProperty(SupportsGet = true)] public string? Province { get; set; }
    [BindProperty(SupportsGet = true)] public Guid? Customer { get; set; }
    [BindProperty(SupportsGet = true)] public string Sort { get; set; } = "newest";

    /// <summary>فیلتر زمان‌بندی: <c>overdue</c> (دیرکرد) یا <c>soon</c> (نزدیک به پایان مهلت).</summary>
    [BindProperty(SupportsGet = true)] public string? Sla { get; set; }

    /// <summary>منبع سفارش: <c>staff</c> (ثبت‌شده در پنل) یا <c>site</c>.</summary>
    [BindProperty(SupportsGet = true)] public string? Source { get; set; }

    /// <summary>نشان زمان‌بندی ردیف‌های همین صفحه (کلید: شناسهٔ سفارش).</summary>
    public Dictionary<Guid, OrderSlaResult> SlaMap { get; private set; } = [];

    /// <summary>تعداد دیرکرد / نزدیک به پایان مهلت برای دکمه‌های بالای صفحه.</summary>
    public OrderSlaSummary SlaSummary { get; private set; } = OrderSlaSummary.Empty;

    public AdminOrderList List { get; private set; } = new() { Orders = new PagedList<AdminOrderRow>([], 0, 1, 25) };

    public async Task<IActionResult> OnGetAsync([FromQuery(Name = "page")] int pageNumber = 1)
    {
        // Exact order number typed in the search box → straight to the order.
        if (!string.IsNullOrWhiteSpace(Search) && Status is null && pageNumber == 1)
        {
            var id = await orders.FindIdByNumberAsync(SearchText.NormalizeDigits(Search));
            if (id is { } orderId)
                return Redirect($"/admin/orders/{orderId}");
        }

        List = await orders.GetListAsync(await BuildFilterAsync(pageNumber, 25));
        SlaMap = await sla.EvaluateAsync(List.Orders.Items.Select(o => o.Id).ToList(), HttpContext.RequestAborted);
        SlaSummary = await sla.GetSummaryAsync(HttpContext.RequestAborted);
        return Page();
    }

    public async Task<IActionResult> OnGetExportAsync(string format = "xlsx")
    {
        if (!Can(AdminPermissions.FinanceExport) && !Can(AdminPermissions.OrdersManage))
            return Redirect("/admin/denied?need=" + AdminPermissions.FinanceExport);

        var rows = await orders.GetExportRowsAsync(await BuildFilterAsync(1, 50_000));
        var table = new ExportTable("سفارش‌ها", "شماره سفارش", "تاریخ", "مشتری", "موبایل", "استان", "شهر", "وضعیت", "وضعیت پرداخت", "روش پرداخت",
            "تعداد اقلام", "تخفیف", "هزینه ارسال", "مبلغ قابل پرداخت", "روش ارسال", "کد رهگیری", "تعداد فاکتور خرید", "بهای خرید", "سود ناخالص");
        foreach (var r in rows)
        {
            table.Add(r.OrderNumber, r.CreatedAt, r.CustomerName, r.CustomerPhone, r.ShippingProvince, r.ShippingCity, AdminUi.OrderStatus(r.Status),
                Display.PaymentStatus(r.PaymentStatus), Display.PaymentMethod(r.PaymentMethod), r.ItemCount, r.DiscountTotal, r.ShippingAmount,
                r.PayableAmount, r.ShippingMethod, r.TrackingCode, r.PurchaseInvoiceCount, r.PurchaseCost,
                r.PurchaseInvoiceCount > 0 ? r.PayableAmount - r.ShippingAmount - r.PurchaseCost : null);
        }
        return Exporter.File(table, format, "orders");
    }

    [AdminPermission(AdminPermissions.OrdersManage)]
    public async Task<IActionResult> OnPostBulkAsync(List<Guid> ids, OrderStatus status)
    {
        if (ids.Count == 0)
        {
            ErrorMessage = "هیچ سفارشی انتخاب نشده است.";
            return BackToList();
        }

        if (status is OrderStatus.Cancelled or OrderStatus.Returned)
        {
            ErrorMessage = "لغو و مرجوعی گروهی مجاز نیست؛ این کارها را از صفحه هر سفارش انجام دهید.";
            return BackToList();
        }

        var done = 0;
        var errors = new List<string>();
        foreach (var id in ids.Distinct().Take(100))
        {
            var result = await orders.ChangeStatusAsync(id, status, "تغییر گروهی", ActorId, ActorName);
            if (Succeeded(result))
                done++;
            else if (errors.Count < 3)
                errors.Add(result.Message ?? "");
        }

        if (done > 0)
            SuccessMessage = $"وضعیت {done} سفارش به «{AdminUi.OrderStatus(status)}» تغییر کرد.";
        if (errors.Count > 0)
            ErrorMessage = $"{ids.Count - done} سفارش تغییر نکرد: " + string.Join(" | ", errors.Distinct());
        return BackToList();
    }

    private async Task<AdminOrderFilter> BuildFilterAsync(int page, int take)
    {
        var period = PersianDateInput.Period(From, To);
        // فیلتر زمان‌بندی: شناسهٔ سفارش‌های دیرکرد / نزدیک به پایان مهلت از سرویس SLA گرفته می‌شود
        // تا منطق فهرست دقیقاً همان منطق نشان‌ها و هشدارها باشد.
        IReadOnlyCollection<Guid>? onlyIds = Sla switch
        {
            "overdue" => await sla.GetIdsAsync(OrderSlaLevel.Overdue, HttpContext.RequestAborted),
            "soon" => await sla.GetIdsAsync(OrderSlaLevel.Soon, HttpContext.RequestAborted),
            _ => null,
        };
        return new AdminOrderFilter
        {
            Search = Search,
            Status = Status,
            PaymentStatus = Payment,
            PaymentMethod = Method,
            From = period.From,
            To = period.To,
            HasPurchaseInvoice = Invoice switch { "has" => true, "missing" => false, _ => null },
            Province = Province,
            UserId = Customer,
            OnlyIds = onlyIds,
            StaffCreated = Source switch { "staff" => true, "site" => false, _ => null },
            Sort = Sort,
            Page = page,
            Take = take,
        };
    }
}
