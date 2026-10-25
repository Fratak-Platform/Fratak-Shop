using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Core.Services.Notifications;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Entities.Notifications;
using Fratak_Shop.Web.Infrastructure.Admin;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Web.Areas.Admin.Pages.Notifications;

/// <summary>یک ردیف فهرست قالب‌ها: رویداد + قالب ذخیره‌شده (اگر هنوز ساخته نشده null).</summary>
public sealed record TemplateRow(NotificationEvent Event, string Title, string Group, NotificationTemplate? Template);

/// <summary>
/// فهرست «قالب‌های پیام رویدادها» (آدرس <c>/admin/notifications/templates</c>).
/// <para>
/// برای همهٔ رویدادهای سیستم (ثبت سفارش، ارسال، شارژ کیف پول، پاسخ تیکت و ...) یک ردیف نشان می‌دهد؛
/// هر کانال (اعلان سایت، پوش مرورگر، پیامک) با یک کلیک روشن/خاموش می‌شود و دکمهٔ «ویرایش» به
/// ویرایشگر قالب با برچسب‌های قابل کلیک و پیش‌نمایش زنده می‌رود (<see cref="TemplateEditModel"/>).
/// </para>
/// </summary>
[AdminPermission(AdminPermissions.NotificationsSend, AdminPermissions.SettingsManage)]
public class TemplatesModel(IAdminDataService data, IMonitoringService monitoring) : AdminPageModel
{
    public List<TemplateRow> Rows { get; private set; } = [];

    public async Task OnGetAsync()
    {
        var templates = await data.Query<NotificationTemplate>().ToListAsync();
        var byEvent = templates.GroupBy(t => t.Event).ToDictionary(g => g.Key, g => g.First());
        Rows = NotificationPlaceholders.Events
            .Select(e => new TemplateRow(e, NotificationPlaceholders.EventTitle(e), NotificationPlaceholders.EventGroup(e), byEvent.GetValueOrDefault(e)))
            .ToList();
    }

    /// <summary>روشن/خاموش کردن سریع یک کانال: site (اعلان سایت)، push یا sms.</summary>
    public async Task<IActionResult> OnPostChannelAsync(NotificationEvent @event, string channel)
    {
        var template = await data.Query<NotificationTemplate>().FirstOrDefaultAsync(t => t.Event == @event);
        if (template is null)
        {
            TempData["ErrorMessage"] = "برای این رویداد هنوز قالبی ساخته نشده؛ ابتدا آن را ویرایش و ذخیره کنید.";
            return RedirectToPage();
        }

        switch (channel)
        {
            case "site":
                template.IsActive = !template.IsActive;
                break;
            case "push":
                template.SendPush = !template.SendPush;
                break;
            case "sms" when !string.IsNullOrWhiteSpace(template.SmsText):
                template.SendSms = !template.SendSms;
                break;
            case "sms":
                TempData["ErrorMessage"] = "ابتدا متن پیامک این رویداد را بنویسید.";
                return RedirectToPage();
            default:
                return BadRequest();
        }

        var result = await data.UpdateAsync(template);
        if (result.IsSuccess())
        {
            await monitoring.LogActionAsync("notification-template.channel", "NotificationTemplate", template.Id, $"{@event}: {channel}");
            TempData["SuccessMessage"] = "تغییر ذخیره شد.";
        }
        else
        {
            TempData["ErrorMessage"] = result.Message;
        }

        return RedirectToPage();
    }
}
