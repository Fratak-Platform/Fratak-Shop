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
public class IndexModel(IStaffNotificationService staffNotifications, IWebPushService push, IStoreSettingService settings) : AdminPageModel
{
    [FromQuery(Name = "page")] public int PageNumber { get; set; } = 1;
    [FromQuery(Name = "unread")] public bool UnreadOnly { get; set; }

    public PagedList<StaffNotificationDto> Items { get; private set; } = new([], 0, 1, 30);
    public HashSet<StaffNotificationKind> Muted { get; private set; } = [];
    public PushStats MyPush { get; private set; } = new(0, 0, 0, null);
    public bool PushEnabled { get; private set; }
    public PushDeliveryResult? LastPush => WebPushSender.LastResult;

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
    public async Task<IActionResult> OnPostTestAsync()
    {
        await staffNotifications.NotifyUserAsync(ActorId, StaffNotificationKind.System,
            "اعلان آزمایشی", "اگر این پیام را می‌بینید، اعلان زنده درست کار می‌کند.", "/admin/inbox");
        TempData["SuccessMessage"] = "اعلان آزمایشی ارسال شد. اگر پوش مرورگر فعال باشد، چند ثانیه بعد روی دستگاه هم نمایش داده می‌شود.";
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
        _ => "سیستم",
    };

    public static string KindBadge(StaffNotificationKind kind) => kind switch
    {
        StaffNotificationKind.NewOrder or StaffNotificationKind.OrderPaid => "a-badge-green",
        StaffNotificationKind.Alert => "a-badge-red",
        StaffNotificationKind.NewTicket or StaffNotificationKind.TicketReply or StaffNotificationKind.NewMessage => "a-badge-blue",
        StaffNotificationKind.Mention or StaffNotificationKind.Assignment or StaffNotificationKind.Approval => "a-badge-purple",
        StaffNotificationKind.NewReturn or StaffNotificationKind.NewWithdrawal => "a-badge-yellow",
        _ => "a-badge-gray",
    };
}
