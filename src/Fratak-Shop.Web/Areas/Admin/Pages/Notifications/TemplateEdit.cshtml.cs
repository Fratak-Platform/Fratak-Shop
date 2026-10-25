using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Core.Services.Notifications;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Entities.Notifications;
using Fratak_Shop.Web.Infrastructure.Admin;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Web.Areas.Admin.Pages.Notifications;

/// <summary>فرم ویرایش قالب یک رویداد.</summary>
public sealed class TemplateInput
{
    public string? Title { get; set; }
    public string? Body { get; set; }
    public string? LinkText { get; set; }
    public string? SmsText { get; set; }
    public bool IsActive { get; set; } = true;
    public bool SendPush { get; set; } = true;
    public bool SendSms { get; set; }
}

/// <summary>
/// ویرایشگر قالب پیام یک رویداد (آدرس <c>/admin/notifications/templates/{event}</c>).
/// <para>
/// - کنار فرم، برچسب‌های معتبر همین رویداد به‌صورت دکمه نمایش داده می‌شوند؛ کلیک روی هر برچسب
///   (مثلاً «نام» → <c>{FirstName}</c>) آن را در محل نشانگر متن (یا انتهای متن) درج می‌کند.
/// - پیش‌نمایش زنده با اطلاعات واقعی خود کارمند و آخرین سفارش او ساخته می‌شود (handler <c>Preview</c>).
/// - اگر برچسبی در متن باشد که برای این رویداد وجود ندارد (اشتباه تایپی)، ذخیره انجام نمی‌شود و هشدار داده می‌شود.
/// </para>
/// </summary>
[AdminPermission(AdminPermissions.NotificationsSend, AdminPermissions.SettingsManage)]
public class TemplateEditModel(IAdminDataService data, INotificationService notifications, IMonitoringService monitoring) : AdminPageModel
{
    [FromRoute(Name = "event")] public NotificationEvent Event { get; set; }
    [BindProperty] public TemplateInput Input { get; set; } = new();

    public IReadOnlyList<NotificationPlaceholderGroup> Groups { get; private set; } = [];
    public string EventTitle => NotificationPlaceholders.EventTitle(Event);
    public bool IsNew { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        if (!IsKnownEvent())
            return NotFound();

        var template = await data.Query<NotificationTemplate>().FirstOrDefaultAsync(t => t.Event == Event);
        IsNew = template is null;
        if (template is not null)
        {
            Input = new TemplateInput
            {
                Title = template.Title,
                Body = template.Body,
                LinkText = template.LinkText,
                SmsText = template.SmsText,
                IsActive = template.IsActive,
                SendPush = template.SendPush,
                SendSms = template.SendSms,
            };
        }

        Groups = NotificationPlaceholders.For(Event);
        return Page();
    }

    /// <summary>پیش‌نمایش زنده (درخواست fetch از admin.js) – خروجی JSON.</summary>
    public async Task<IActionResult> OnPostPreviewAsync()
    {
        if (!IsKnownEvent())
            return NotFound();

        var preview = await notifications.PreviewAsync(Event, ActorId, Input.Title, Input.Body, Input.SmsText);
        return new JsonResult(new { title = preview.Title, body = preview.Body, sms = preview.Sms, source = preview.Source, unknown = preview.UnknownKeys });
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!IsKnownEvent())
            return NotFound();

        Groups = NotificationPlaceholders.For(Event);
        var title = Input.Title.ToPlainText(200);
        var body = Input.Body.ToPlainText(2000);
        var sms = Input.SmsText.ToPlainText(500);

        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(body))
            ModelState.AddModelError("Input.Body", "عنوان و متن پیام را وارد کنید.");
        if (Input.SendSms && string.IsNullOrWhiteSpace(sms))
            ModelState.AddModelError("Input.SmsText", "برای ارسال پیامک، متن پیامک را بنویسید.");

        // برچسب ناشناخته (مثلاً {OrderNumbr}) در پیام نهایی خالی می‌شود؛ پس قبل از ذخیره جلویش گرفته می‌شود.
        var preview = await notifications.PreviewAsync(Event, ActorId, title, body, sms);
        if (preview.UnknownKeys.Count > 0)
            ModelState.AddModelError("Input.Body", "این برچسب‌ها برای این رویداد وجود ندارند: " + string.Join("، ", preview.UnknownKeys.Select(k => "{" + k + "}")));

        if (!ModelState.IsValid)
            return Page();

        var template = await data.Query<NotificationTemplate>().FirstOrDefaultAsync(t => t.Event == Event);
        var isNew = template is null;
        template ??= new NotificationTemplate { Event = Event };
        template.Title = title;
        template.Body = body;
        template.LinkText = Input.LinkText.ToPlainText(100) is { Length: > 0 } link ? link : null;
        template.SmsText = string.IsNullOrWhiteSpace(sms) ? null : sms;
        template.IsActive = Input.IsActive;
        template.SendPush = Input.SendPush;
        template.SendSms = Input.SendSms && template.SmsText is not null;

        var result = isNew ? await data.AddAsync(template) : await data.UpdateAsync(template);
        if (!result.IsSuccess())
        {
            ModelState.AddModelError(string.Empty, result.Message);
            return Page();
        }

        await monitoring.LogActionAsync("notification-template.save", "NotificationTemplate", template.Id, EventTitle);
        TempData["SuccessMessage"] = $"قالب «{EventTitle}» ذخیره شد.";
        return Redirect("/admin/notifications/templates");
    }

    private bool IsKnownEvent() => Event != NotificationEvent.None && Enum.IsDefined(Event);
}
