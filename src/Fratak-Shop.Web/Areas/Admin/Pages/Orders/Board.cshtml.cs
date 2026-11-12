using Fratak_Shop.Core.DTOs.Admin;
using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Core.Services.Orders;
using Fratak_Shop.Core.Services.Settings;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Entities.Orders;
using Fratak_Shop.Web.Infrastructure.Admin;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Areas.Admin.Pages.Orders;

/// <summary>
/// کانبان سفارش‌ها (<c>/admin/orders/board</c>): هر ستون یک وضعیت است و کارت سفارش با کشیدن به ستون بعد جابه‌جا می‌شود.
/// <para>
/// فقط انتقال‌های مجاز «یک قدم جلو» (منتظر ← تأییدشده ← آماده‌سازی ← ارسال‌شده ← تحویل) از روی تخته انجام می‌شود؛
/// لغو و مرجوعی عمداً از اینجا ممکن نیست (نیاز به دلیل و تأیید دارد). انتقال‌هایی که ورودی می‌خواهند
/// (فاکتور خرید برای آماده‌سازی، کد رهگیری برای ارسال) کارمند را به صفحهٔ جزئیات سفارش می‌برند.
/// قوانین واقعی را همان سرویس سفارش بررسی می‌کند؛ این صفحه فقط رابط است.
/// </para>
/// </summary>
[AdminPermission(AdminPermissions.OrdersView)]
public class BoardModel(IAdminOrderService orders, IOrderSlaService sla, IStoreSettingService settings) : AdminPageModel
{
    /// <summary>حداکثر کارت هر ستون (قدیمی‌ترین‌ها اول؛ بقیه در فهرست سفارش‌ها).</summary>
    public const int PerColumn = 40;

    /// <summary>ستون‌های تخته به ترتیب جریان کار.</summary>
    public static readonly (OrderStatus Status, string Title)[] Stages =
    [
        (OrderStatus.Pending, "در انتظار"),
        (OrderStatus.Confirmed, "تأییدشده (خرید از تأمین‌کننده)"),
        (OrderStatus.Processing, "در حال آماده‌سازی"),
        (OrderStatus.Shipped, "ارسال‌شده"),
        (OrderStatus.Delivered, "تحویل (۷ روز اخیر)"),
    ];

    public sealed record BoardColumn(OrderStatus Status, string Title, List<AdminOrderRow> Rows, int Total);

    public List<BoardColumn> Columns { get; private set; } = [];
    public Dictionary<Guid, OrderSlaResult> SlaMap { get; private set; } = [];
    public bool RequiresInvoice { get; private set; }

    public async Task OnGetAsync()
    {
        RequiresInvoice = (await settings.GetAsync()).RequirePurchaseInvoiceForProcessing;
        var weekAgo = DateTimeOffset.UtcNow.AddDays(-7);
        foreach (var (status, title) in Stages)
        {
            var list = await orders.GetListAsync(new AdminOrderFilter
            {
                Status = status,
                // تحویل‌شده‌ها فقط هفتهٔ اخیر (وگرنه ستون بی‌انتها می‌شود)؛ بقیه قدیمی‌ترین اول تا عقب‌افتاده‌ها دیده شوند.
                From = status == OrderStatus.Delivered ? weekAgo : null,
                Sort = status == OrderStatus.Delivered ? "newest" : "oldest",
                Page = 1,
                Take = PerColumn,
            });
            Columns.Add(new BoardColumn(status, title, list.Orders.Items, list.Orders.Total));
        }

        SlaMap = await sla.EvaluateAsync(Columns.SelectMany(c => c.Rows.Select(r => r.Id)).ToList(), HttpContext.RequestAborted);
    }

    /// <summary>
    /// جابه‌جایی کارت (JSON). پاسخ: <c>{ ok, message, redirect? }</c>؛ اگر <c>redirect</c> باشد، انتقال به ورودی نیاز دارد
    /// و اسکریپت کارمند را (با تأیید) به صفحهٔ سفارش می‌برد.
    /// </summary>
    [AdminPermission(AdminPermissions.OrdersManage)]
    public async Task<IActionResult> OnPostMoveAsync(Guid id, OrderStatus fromStatus, OrderStatus toStatus)
    {
        if (!IsForwardStep(fromStatus, toStatus))
            return new JsonResult(new { ok = false, message = "از روی تخته فقط می‌توان سفارش را یک مرحله جلو برد؛ برای لغو یا مرجوعی صفحهٔ سفارش را باز کنید." });

        if (toStatus == OrderStatus.Shipped)
            return new JsonResult(new { ok = false, message = "برای ارسال، کد رهگیری را در صفحهٔ سفارش ثبت کنید (سفارش خودکار «ارسال‌شده» می‌شود).", redirect = $"/admin/orders/{id}" });

        var result = await orders.ChangeStatusAsync(id, toStatus, "از کانبان", ActorId, ActorName);
        if (result.IsSuccess())
            return new JsonResult(new { ok = true, message = result.Message });

        // مثلاً فاکتور خرید ثبت نشده: کارمند به صفحهٔ سفارش می‌رود تا فاکتور را همراه تغییر وضعیت بارگذاری کند.
        return new JsonResult(new { ok = false, message = result.Message, redirect = toStatus == OrderStatus.Processing ? $"/admin/orders/{id}" : null });
    }

    /// <summary>فقط یک قدم جلو در جریان اصلی کار. مثال: Confirmed → Processing مجاز، Pending → Shipped غیرمجاز.</summary>
    public static bool IsForwardStep(OrderStatus current, OrderStatus next)
    {
        var a = Array.FindIndex(Stages, s => s.Status == current);
        var b = Array.FindIndex(Stages, s => s.Status == next);
        return a >= 0 && b == a + 1;
    }
}
