using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Core.Services.Assistant;
using Fratak_Shop.Core.Services.Insights;
using Fratak_Shop.Core.Services.Products;
using Fratak_Shop.Core.Services.Seo.Engine;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Entities.Orders;
using Fratak_Shop.Data.Entities.Products;
using Fratak_Shop.Web.Infrastructure.Admin;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Web.Areas.Admin.Pages.SmartContent;

/// <summary>یک سطر جواب «دستیار مدیریت»؛ ConfirmId یعنی دکمهٔ «تأیید» (انتشار/غیرفعال‌سازی همان کالا).</summary>
public sealed record OpsRow(string Title, string? Detail, string? Url, Guid? ConfirmId = null);

/// <summary>جواب کامل: متن خلاصه، سطرها و پیشنهادهای بعدی.</summary>
public sealed record OpsAnswer(string Text, IReadOnlyList<OpsRow> Rows, IReadOnlyList<string> Next);

/// <summary>
/// دور ۲۸ – «دستیار مدیریت» (/admin/smart/assistant/ops): کارکنان به زبان ساده می‌پرسند یا فرمان می‌دهند و سیستم با داده‌های واقعی جواب می‌دهد.
/// <list type="bullet">
/// <item>فهم پیام: <see cref="StaffCommands.Parse"/> (خالص و آزمون‌شده).</item>
/// <item>هر بخش فقط با دسترسی همان بخش (فروش ← گزارش‌ها، سفارش ← دیدن سفارش‌ها، کالا ← دیدن کالاها). بی‌دسترسی ← «اجازه ندارید».</item>
/// <item>فرمان تغییردهنده (انتشار/غیرفعال) هیچ‌وقت مستقیم انجام نمی‌شود: فهرست کالاهای پیدا‌شده با دکمهٔ «تأیید» جدا (POST + ضدجعل + بررسی دوبارهٔ دسترسی).</item>
/// </list>
/// GET فقط فرم را نشان می‌دهد؛ پرسش با POST فرستاده می‌شود (چیزی تغییر نمی‌کند).
/// </summary>
[AdminPermission(AdminPermissions.OrdersView, AdminPermissions.ProductsView, AdminPermissions.ReportsView)]
public class OpsModel(IAdminDataService data, IAdminReportService reports, IProductService products) : AdminPageModel
{
    /// <summary>بیشترین سطر هر جواب.</summary>
    public const int MaxRows = 10;

    /// <summary>پرسش‌های پیشنهادی (دکمه‌های آماده).</summary>
    public static readonly string[] Examples = ["کارهای امروز", "فروش امروز", "فروش این هفته", "سفارش‌های منتظر", "کالاهای ناموجود", "موجودی کم", "آماده انتشار", "تغییرات تامین کننده"];

    public string? Question { get; private set; }
    public OpsAnswer? Answer { get; private set; }

    public void OnGet()
    {
    }

    /// <summary>پرسش/فرمان کارمند ← جواب (بدون تغییر داده).</summary>
    public async Task<IActionResult> OnPostAskAsync(string? q)
    {
        Question = (q ?? "").Trim() is { Length: > 200 } longText ? longText[..200] : (q ?? "").Trim();
        Answer = await AnswerAsync(StaffCommands.Parse(Question), HttpContext.RequestAborted);
        return Page();
    }

    /// <summary>تأیید انتشار/غیرفعال‌سازی یک کالا (دسترسی مدیریت کالا لازم است؛ همان قواعد دکمهٔ انتشار: بدون دسته‌بندی منتشر نمی‌شود).</summary>
    public async Task<IActionResult> OnPostConfirmAsync(Guid id, bool activate)
    {
        if (!Can(AdminPermissions.ProductsManage))
            return LocalRedirect("/admin/denied");
        ShowResult(await products.ChangeProductActivationAsync(id, activate));
        return LocalRedirect("/admin/smart/assistant/ops");
    }

    private static OpsAnswer Say(string text, params string[] next) => new(text, [], next);
    private static OpsAnswer Denied() => Say("برای این بخش اجازه ندارید. اگر لازم است، از مدیر بخواهید دسترسی‌اش را بدهد.");

    /// <summary>اجرای فرمان فهمیده‌شده با داده‌های واقعی و دسترسی کارمند.</summary>
    private async Task<OpsAnswer> AnswerAsync(StaffCommand command, CancellationToken ct)
    {
        switch (command.Kind)
        {
            case StaffCommandKind.Help:
                return Say("این‌ها را می‌توانم: «کارهای امروز»، «فروش امروز/دیروز/این هفته/این ماه»، «سفارش‌های منتظر»، «سفارش ۱۴۰۵-۱۲۳»، «کالاهای ناموجود»، «موجودی کم»، «آماده انتشار»، «تغییرات تامین کننده»، «موجودی هدفون»، «منتشر کن …»، «غیرفعال کن …».", Examples);

            case StaffCommandKind.Today:
            {
                var d = await reports.GetDashboardAsync(ct);
                var orders = Can(AdminPermissions.OrdersView);
                int? ready = Can(AdminPermissions.ProductsManage) ? await data.Query<Product>().CountAsync(ProductCompleteness.ReadyToPublish, ct) : null;
                int? supplier = CanAny(AdminPermissions.ProductsSupplier, AdminPermissions.ProductsManage)
                    ? await data.Query<SupplierChange>().CountAsync(c => c.Status == SupplierChangeStatus.Pending, ct) : null;
                var tasks = TodayPlan.Build(new TodayInputs(
                    OrdersToConfirm: orders ? d.OrdersToConfirm : null,
                    OrdersAwaitingInvoice: orders ? d.OrdersAwaitingInvoice : null,
                    OrdersToShip: orders ? d.OrdersToShip : null,
                    PendingReturns: Can(AdminPermissions.ReturnsManage) ? d.PendingReturns : null,
                    OpenTickets: Can(AdminPermissions.TicketsManage) ? d.OpenTickets : null,
                    PendingWithdrawals: CanAny(AdminPermissions.WithdrawalsManage, AdminPermissions.FinanceView) ? d.PendingWithdrawals : null,
                    NewMessages: Can(AdminPermissions.MessagesManage) ? d.NewMessages : null,
                    ReadyToPublish: ready,
                    SupplierPending: supplier,
                    OutOfStock: Can(AdminPermissions.ProductsView) ? d.OutOfStock : null,
                    PendingComments: Can(AdminPermissions.CommentsModerate) ? d.PendingComments : null));
                return tasks.Count == 0
                    ? Say("برای امروز کار عقب‌افتاده‌ای نمی‌بینم. 🌿", "فروش امروز", "آماده انتشار")
                    : new OpsAnswer("به ترتیب اولویت:", tasks.Select(t => new OpsRow(t.Title, t.Hint, t.Url)).ToList(), ["فروش امروز", "سفارش‌های منتظر"]);
            }

            case StaffCommandKind.Sales:
            {
                if (!Can(AdminPermissions.ReportsView))
                    return Denied();
                var d = await reports.GetDashboardAsync(ct);
                var text = command.Period switch
                {
                    StaffPeriod.Yesterday => $"فروش دیروز: {PersianText.Money(d.YesterdaySales)} تومان از {PersianText.Digits(d.YesterdayOrders)} سفارش.",
                    StaffPeriod.Week => $"فروش ۷ روز اخیر: {PersianText.Money(d.Last7Sales)} تومان.",
                    StaffPeriod.Month => $"فروش ۳۰ روز اخیر: {PersianText.Money(d.Last30Sales)} تومان از {PersianText.Digits(d.Last30Orders)} سفارش" + Trend(d.Last30Sales, d.Previous30Sales),
                    _ => $"فروش امروز تا این لحظه: {PersianText.Money(d.TodaySales)} تومان از {PersianText.Digits(d.TodayOrders)} سفارش (دیروز: {PersianText.Money(d.YesterdaySales)} تومان).",
                };
                return Say(text, "فروش این ماه", "کارهای امروز");
            }

            case StaffCommandKind.PendingOrders:
            {
                if (!Can(AdminPermissions.OrdersView))
                    return Denied();
                var rows = await data.Query<Order>()
                    .Where(o => o.Status == OrderStatus.Pending || o.Status == OrderStatus.Confirmed || o.Status == OrderStatus.Processing)
                    .Where(o => o.Status != OrderStatus.Pending || o.PaymentStatus == PaymentStatus.Paid || o.PaymentMethod != PaymentMethod.OnlineGateway)
                    .OrderBy(o => o.CreatedAt).Take(MaxRows)
                    .Select(o => new { o.Id, o.OrderNumber, o.Status, o.CustomerName, o.PayableAmount, o.CreatedAt })
                    .ToListAsync(ct);
                return rows.Count == 0
                    ? Say("سفارش منتظری نیست. 👌", "فروش امروز")
                    : new OpsAnswer("سفارش‌های در جریان (قدیمی‌ترها اول):",
                        rows.Select(o => new OpsRow($"{o.OrderNumber} – {AdminUi.OrderStatus(o.Status)}", $"{o.CustomerName} · {PersianText.Money(o.PayableAmount)} تومان · {AdminUi.Ago(o.CreatedAt)}", $"/admin/orders/{o.Id}")).ToList(),
                        ["کارهای امروز"]);
            }

            case StaffCommandKind.OrderLookup:
            {
                if (!Can(AdminPermissions.OrdersView))
                    return Denied();
                var number = command.OrderNumber!;
                var order = await data.Query<Order>().Where(o => o.OrderNumber == number)
                    .Select(o => new { o.Id, o.OrderNumber, o.Status, o.PaymentStatus, o.CustomerName, o.PayableAmount, o.CreatedAt, o.TrackingCode })
                    .FirstOrDefaultAsync(ct);
                if (order is null)
                    return Say($"سفارشی با شمارهٔ «{number}» پیدا نکردم. شماره را دوباره بررسی کنید.", "سفارش‌های منتظر");
                var detail = $"{order.CustomerName} · {PersianText.Money(order.PayableAmount)} تومان · {AdminUi.Ago(order.CreatedAt)}"
                             + (string.IsNullOrWhiteSpace(order.TrackingCode) ? "" : $" · کد رهگیری {order.TrackingCode}");
                return new OpsAnswer($"وضعیت سفارش {order.OrderNumber}: {AdminUi.OrderStatus(order.Status)}.", [new OpsRow(order.OrderNumber, detail, $"/admin/orders/{order.Id}")], ["سفارش‌های منتظر"]);
            }

            case StaffCommandKind.OutOfStock:
            case StaffCommandKind.LowStock:
            {
                if (!Can(AdminPermissions.ProductsView))
                    return Denied();
                var outOnly = command.Kind == StaffCommandKind.OutOfStock;
                var query = data.Query<Product>().Where(p => p.IsActive);
                query = outOnly ? query.Where(p => p.StockQuantity <= 0) : query.Where(p => p.StockQuantity > 0 && p.StockQuantity <= 3);
                var rows = await query.OrderByDescending(p => p.SalesCount).ThenByDescending(p => p.ViewCount).Take(MaxRows)
                    .Select(p => new { p.Id, p.Name, p.StockQuantity, p.SalesCount }).ToListAsync(ct);
                if (rows.Count == 0)
                    return Say(outOnly ? "کالای منتشرشدهٔ ناموجودی نداریم. 👌" : "کالایی با موجودی کم نداریم.", "کارهای امروز");
                return new OpsAnswer(outOnly ? "کالاهای ناموجود (پرفروش‌ترها اول):" : "کالاهای رو به اتمام (پرفروش‌ترها اول):",
                    rows.Select(p => new OpsRow(p.Name, $"موجودی {PersianText.Digits(p.StockQuantity)} · فروش {PersianText.Digits(p.SalesCount)}", $"/admin/products/edit/{p.Id}")).ToList(),
                    ["تغییرات تامین کننده"]);
            }

            case StaffCommandKind.ReadyList:
            {
                if (!Can(AdminPermissions.ProductsManage))
                    return Denied();
                var rows = await data.Query<Product>().Where(ProductCompleteness.ReadyToPublish).OrderByDescending(p => p.CreatedAt).Take(MaxRows)
                    .Select(p => new { p.Id, p.Name, p.Price }).ToListAsync(ct);
                return rows.Count == 0
                    ? Say("فعلاً کالای «آمادهٔ انتشار» نداریم؛ تکمیل خودکار در پس‌زمینه ادامه دارد.", "کارهای امروز")
                    : new OpsAnswer("کالاهای آمادهٔ انتشار؛ بعد از بازبینی، «تأیید» را بزنید تا منتشر شوند:",
                        rows.Select(p => new OpsRow(p.Name, $"{PersianText.Money(p.Price)} تومان", $"/admin/products/edit/{p.Id}", p.Id)).ToList(), ["کارهای امروز"]);
            }

            case StaffCommandKind.SupplierPending:
            {
                if (!CanAny(AdminPermissions.ProductsSupplier, AdminPermissions.ProductsManage))
                    return Denied();
                var count = await data.Query<SupplierChange>().CountAsync(c => c.Status == SupplierChangeStatus.Pending, ct);
                return count == 0
                    ? Say("تغییر تأیید‌نشده‌ای از تأمین‌کننده نیست.")
                    : new OpsAnswer($"{PersianText.Digits(count)} تغییر از تأمین‌کننده منتظر بررسی است.", [new OpsRow("بررسی تغییرات تأمین‌کننده", null, "/admin/products/supplier-sync")], ["کارهای امروز"]);
            }

            case StaffCommandKind.ProductLookup:
            case StaffCommandKind.Publish:
            case StaffCommandKind.Unpublish:
            {
                if (!Can(AdminPermissions.ProductsView))
                    return Denied();
                if (command.Changes && !Can(AdminPermissions.ProductsManage))
                    return Denied();
                var term = command.Term!;
                var activate = command.Kind == StaffCommandKind.Publish;
                // همهٔ واژه‌ها (به هر ترتیب) باید در نام فارسی/انگلیسی یا کد کالا باشند؛ «ا» آغازین با «آ» هم سنجیده می‌شود («ازمون» = «آزمون»).
                var query = data.Query<Product>();
                foreach (var word in term.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(5))
                {
                    var alt = word.StartsWith('ا') ? "آ" + word[1..] : word;
                    query = query.Where(p => p.Name.Contains(word) || p.Name.Contains(alt) || (p.EnglishName != null && p.EnglishName.Contains(word)) || p.SKU.Contains(word));
                }
                if (command.Kind == StaffCommandKind.Publish)
                    query = query.Where(p => !p.IsActive);
                else if (command.Kind == StaffCommandKind.Unpublish)
                    query = query.Where(p => p.IsActive);
                var rows = await query.OrderByDescending(p => p.IsActive).ThenByDescending(p => p.SalesCount).Take(MaxRows)
                    .Select(p => new { p.Id, p.Name, p.Price, p.StockQuantity, p.IsActive }).ToListAsync(ct);
                if (rows.Count == 0)
                    return Say($"کالایی با «{term}» پیدا نکردم" + (command.Changes ? (activate ? " که منتشرنشده باشد." : " که منتشر شده باشد.") : "."), "کالاهای ناموجود");
                var intro = command.Kind switch
                {
                    StaffCommandKind.Publish => "این کالاها پیدا شد؛ برای انتشار هرکدام «تأیید» را بزنید (بدون دسته‌بندی منتشر نمی‌شود):",
                    StaffCommandKind.Unpublish => "این کالاها پیدا شد؛ برای غیرفعال کردن هرکدام «تأیید» را بزنید:",
                    _ => $"نتیجه برای «{term}»:",
                };
                return new OpsAnswer(intro,
                    rows.Select(p => new OpsRow(p.Name, $"{PersianText.Money(p.Price)} تومان · موجودی {PersianText.Digits(p.StockQuantity)} · {(p.IsActive ? "منتشرشده" : "منتشرنشده")}",
                        $"/admin/products/edit/{p.Id}", command.Changes ? p.Id : null)).ToList(), []);
            }

            default:
                return Say("متوجه نشدم. یکی از این‌ها را امتحان کنید یا «راهنما» را بنویسید.", Examples);
        }
    }

    /// <summary>روند نسبت به ۳۰ روز قبل. مثال: (۱۲۰، ۱۰۰) ← « (۲۰٪ بیشتر از ۳۰ روز قبل).»</summary>
    public static string Trend(decimal current, decimal previous)
    {
        if (previous <= 0)
            return ".";
        var percent = (int)Math.Round((current - previous) / previous * 100);
        return percent == 0 ? " (هم‌اندازهٔ ۳۰ روز قبل)." : $" ({PersianText.Digits(Math.Abs(percent))}٪ {(percent > 0 ? "بیشتر" : "کمتر")} از ۳۰ روز قبل).";
    }

    /// <summary>این سطر دکمهٔ تأیید انتشار دارد یا غیرفعال‌سازی؟ (پرسش اصلی از نوع «غیرفعال» بود؟)</summary>
    public bool ConfirmActivates => StaffCommands.Parse(Question).Kind != StaffCommandKind.Unpublish;
}
