using Fratak_Shop.Core.DTOs.Admin;
using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Core.Services.Notifications;
using Fratak_Shop.Core.Services.Settings;
using Fratak_Shop.Data.Entities.Admin;
using Fratak_Shop.Web.Infrastructure.Admin;
using Fratak_Shop.Web.Infrastructure.Engagement;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Areas.Admin.Pages.Inbox;

/// <summary>
/// صفحهٔ «اعلان‌های من» در پنل (آدرس <c>/admin/inbox</c>).
/// <para>
/// هر کارمند (بدون نیاز به مجوز خاص) اعلان‌های خودش را می‌بیند: سفارش جدید، تیکت، پیام، منشن و ...
/// همچنین اینجا می‌تواند نوع‌هایی را بی‌صدا کند، وضعیت اعلان مرورگر این دستگاه را ببیند و یک اعلان آزمایشی بفرستد
/// (برای عیب‌یابی «چرا پوش نمی‌آید؟»).
/// </para>
/// لینک هر اعلان به <c>?open={id}</c> می‌رود تا اول «خوانده شد» ثبت شود و بعد به صفحهٔ مربوط منتقل شود.
/// </summary>
public class IndexModel(IStaffNotificationService staffNotifications, IWebPushService push, IStoreSettingService settings, WebPushDispatcher dispatcher) : AdminPageModel
{
    [FromQuery(Name = "page")] public int PageNumber { get; set; } = 1;
    [FromQuery(Name = "unread")] public bool UnreadOnly { get; set; }

    public PagedList<StaffNotificationDto> Items { get; private set; } = new([], 0, 1, 30);
    public HashSet<StaffNotificationKind> Muted { get; private set; } = [];
    public PushStats MyPush { get; private set; } = new(0, 0, 0, null);
    public bool PushEnabled { get; private set; }
    public PushDeliveryResult? LastPush => WebPushDispatcher.LastResult;

    /// <summary>نتیجهٔ تک‌تک دستگاه‌ها در آخرین «اعلان آزمایشی» (هر خط یک دستگاه)؛ از TempData خوانده می‌شود.</summary>
    public string[] PushTestLines => TempData["PushTest"] is string text && text.Length > 0 ? text.Split('\n') : Array.Empty<string>();

    /// <summary>نوع‌هایی که کارمند می‌تواند بی‌صدا کند، با عنوان فارسی.</summary>
    public static readonly (StaffNotificationKind Kind, string Title)[] MutableKinds =
    [
        (StaffNotificationKind.NewOrder, "سفارش جدید (پرداخت در محل / کارت به کارت)"),
        (StaffNotificationKind.OrderPaid, "سفارش جدید پرداخت‌شده"),
        (StaffNotificationKind.NewTicket, "تیکت جدید"),
        (StaffNotificationKind.TicketReply, "پاسخ مشتری در تیکت"),
        (StaffNotificationKind.NewMessage, "پیام تماس با ما"),
        (StaffNotificationKind.NewComment, "دیدگاه و پرسش جدید"),
        (StaffNotificationKind.NewReturn, "درخواست مرجوعی"),
        (StaffNotificationKind.NewWithdrawal, "درخواست برداشت از کیف پول"),
        (StaffNotificationKind.BlogReview, "مقالهٔ آمادهٔ بازبینی"),
        (StaffNotificationKind.SupplierReport, "گزارش بررسی تأمین‌کننده"),
        (StaffNotificationKind.ShipmentIssue, "مشکل مرسوله (برگشت / رهگیری طولانی)"),
        (StaffNotificationKind.ExpenseDue, "سررسید هزینه‌های تکراری"),
        (StaffNotificationKind.ReportFailed, "خطای ارسال گزارش خودکار"),
        (StaffNotificationKind.SeoSuggestions, "پیشنهادهای موتور سئو"),
        (StaffNotificationKind.SmartResult, "نتیجهٔ کارهای هوشمند (عکس، مشخصات، توضیحات محصول، مقاله)"),
        (StaffNotificationKind.Alert, "هشدارهای سیستم"),
    ];

    public async Task<IActionResult> OnGetAsync(Guid? open)
    {
        // باز کردن یک اعلان از منوی زنگوله: خوانده شود و به صفحهٔ مربوط برود.
        if (open is { } id)
        {
            var target = (await staffNotifications.GetLatestAsync(ActorId, 30)).FirstOrDefault(n => n.Id == id);
            await staffNotifications.MarkReadAsync(ActorId, id);
            if (target?.Url is { } url && Url.IsLocalUrl(url))
                return LocalRedirect(url);
            return RedirectToPage();
        }

        Items = await staffNotifications.GetPageAsync(ActorId, ClampPage(PageNumber), 30, UnreadOnly);
        Muted = await staffNotifications.GetMutedKindsAsync(ActorId);
        MyPush = await push.GetStatsAsync(ActorId);
        PushEnabled = (await settings.GetAsync()).CanUseWebPush;
        return Page();
    }

    public async Task<IActionResult> OnPostReadAsync(Guid id)
    {
        await staffNotifications.MarkReadAsync(ActorId, id);
        return RedirectToPage(new { page = PageNumber, unread = UnreadOnly });
    }

    /// <summary>دور ۲۹ (#۲۸): حذف یک اعلان خودِ همین کارمند (سرویس شرط گیرنده را در دستور حذف می‌گذارد).</summary>
    public async Task<IActionResult> OnPostDeleteAsync(Guid id)
    {
        await staffNotifications.DeleteAsync(ActorId, id);
        return RedirectToPage(new { page = PageNumber, unread = UnreadOnly });
    }

    /// <summary>دور ۲۹ (#۲۸): حذف همهٔ اعلان‌های خوانده‌شدهٔ همین کارمند.</summary>
    public async Task<IActionResult> OnPostDeleteReadAsync()
    {
        var removed = await staffNotifications.DeleteReadAsync(ActorId);
        TempData["SuccessMessage"] = removed > 0 ? $"{removed} اعلان خوانده‌شده حذف شد." : "اعلان خوانده‌شده‌ای وجود نداشت.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostReadAllAsync()
    {
        await staffNotifications.MarkAllReadAsync(ActorId);
        TempData["SuccessMessage"] = "همهٔ اعلان‌ها خوانده شدند.";
        return RedirectToPage();
    }

    /// <summary>ذخیرهٔ نوع‌های بی‌صدا (چک‌باکس‌های «دریافت شود» که تیک ندارند بی‌صدا می‌شوند).</summary>
    public async Task<IActionResult> OnPostMuteAsync(List<StaffNotificationKind> receive)
    {
        var muted = MutableKinds.Select(k => k.Kind).Where(k => !receive.Contains(k));
        await staffNotifications.SetMutedKindsAsync(ActorId, muted);
        TempData["SuccessMessage"] = "تنظیمات اعلان ذخیره شد.";
        return RedirectToPage();
    }

    /// <summary>ارسال اعلان آزمایشی به خود کارمند: زنگوله + صدا (اگر پنل باز است) + پوش مرورگر (اگر فعال است).</summary>
    /// <remarks>
    /// مرحلهٔ هفتم – اصلاحیه: علاوه بر صف معمول، پوش همین لحظه مستقیم فرستاده می‌شود و نتیجهٔ <b>هر دستگاه</b> (سرویس مقصد،
    /// کد پاسخ و توضیح فارسی) زیر کارت «این دستگاه» نمایش داده می‌شود تا علت «اعلان وقتی سایت بسته است نمی‌آید» روشن شود.
    /// برچسب (tag) پوش مستقیم و پوش صف یکی است؛ مرورگر دومی را جایگزین اولی می‌کند و دو اعلان دیده نمی‌شود.
    /// </remarks>
    public async Task<IActionResult> OnPostTestAsync()
    {
        await staffNotifications.NotifyUserAsync(ActorId, StaffNotificationKind.System,
            "اعلان آزمایشی", "اگر این پیام را می‌بینید، اعلان زنده درست کار می‌کند.", "/admin/inbox");

        if (!(await settings.GetAsync()).CanUseWebPush)
        {
            TempData["SuccessMessage"] = "اعلان آزمایشی داخل پنل ارسال شد. پوش مرورگر در تنظیمات فروشگاه خاموش است.";
            return RedirectToPage();
        }

        var results = await dispatcher.SendNowAsync(
            new PushMessage(ActorId, "اعلان آزمایشی", "اگر این پیام را می‌بینید، اعلان زنده درست کار می‌کند.", "/admin/inbox", "staff-system"),
            HttpContext.RequestAborted);
        if (results.Count == 0)
        {
            TempData["ErrorMessage"] = "اعلان داخل پنل ارسال شد ولی هیچ دستگاه فعالی برای پوش شما ثبت نیست. روی هر دستگاه «فعال‌سازی اعلان مرورگر» را بزنید و اجازه بدهید.";
            return RedirectToPage();
        }

        var ok = results.Count(r => r.StatusCode is >= 200 and < 300);
        TempData["PushTest"] = string.Join('\n', results.Take(5).Select(r => $"{r.ServiceTitle}: {r.Explain()}"));
        if (ok == results.Count)
            TempData["SuccessMessage"] = $"پوش به {ok} دستگاه تحویل سرویس شد. جزئیات زیر کارت «این دستگاه».";
        else
            TempData["ErrorMessage"] = $"پوش فقط به {ok} از {results.Count} دستگاه رسید. علت هر دستگاه زیر کارت «این دستگاه» آمده است.";
        return RedirectToPage();
    }

    /// <summary>عنوان فارسی نوع اعلان برای برچسب کنار هر ردیف.</summary>
    public static string KindTitle(StaffNotificationKind kind) => kind switch
    {
        StaffNotificationKind.NewOrder or StaffNotificationKind.OrderPaid => "سفارش",
        StaffNotificationKind.NewTicket or StaffNotificationKind.TicketReply => "تیکت",
        StaffNotificationKind.NewMessage => "پیام",
        StaffNotificationKind.NewComment => "دیدگاه",
        StaffNotificationKind.NewReturn => "مرجوعی",
        StaffNotificationKind.NewWithdrawal => "برداشت",
        StaffNotificationKind.BlogReview => "وبلاگ",
        StaffNotificationKind.Mention => "منشن",
        StaffNotificationKind.Assignment => "ارجاع",
        StaffNotificationKind.SupplierReport => "تأمین‌کننده",
        StaffNotificationKind.Alert => "هشدار",
        StaffNotificationKind.Task => "کار",
        StaffNotificationKind.OrderMessageAnswered => "پاسخ مشتری",
        StaffNotificationKind.Approval => "تأیید",
        StaffNotificationKind.ShipmentIssue => "مرسوله",
        StaffNotificationKind.ExpenseDue => "هزینه",
        StaffNotificationKind.ReportFailed => "گزارش",
        StaffNotificationKind.SeoSuggestions => "سئو",
        StaffNotificationKind.SmartResult => "هوشمند",
        _ => "سیستم",
    };

    public static string KindBadge(StaffNotificationKind kind) => kind switch
    {
        StaffNotificationKind.NewOrder or StaffNotificationKind.OrderPaid => "a-badge-green",
        StaffNotificationKind.Alert => "a-badge-red",
        StaffNotificationKind.NewTicket or StaffNotificationKind.TicketReply or StaffNotificationKind.NewMessage => "a-badge-blue",
        StaffNotificationKind.Mention or StaffNotificationKind.Assignment or StaffNotificationKind.Approval => "a-badge-purple",
        StaffNotificationKind.NewReturn or StaffNotificationKind.NewWithdrawal or StaffNotificationKind.ShipmentIssue => "a-badge-yellow",
        StaffNotificationKind.SmartResult => "a-badge-sky",
        _ => "a-badge-gray",
    };
}
