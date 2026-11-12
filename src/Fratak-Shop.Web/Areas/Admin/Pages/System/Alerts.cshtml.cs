using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Core.Services.Ops;
using Fratak_Shop.Data.Entities.Ops;
using Fratak_Shop.Web.Infrastructure.Admin;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Areas.Admin.Pages.SystemPages;

/// <summary>
/// صفحهٔ «فنی ← هشدارها» (مرحلهٔ هفتم – فاز ۹): آستانهٔ هر شاخص (خطاها، فضای دیسک، پشتیبان، پرداخت گیرکرده و …)،
/// فاصلهٔ تکرار، ارسال بیرونی (ایمیل/بله/تلگرام) و تاریخچهٔ هشدارها. سنجش هر ۵ دقیقه توسط OpsWorker.
/// </summary>
[AdminPermission(AdminPermissions.MonitoringView, AdminPermissions.SystemManage)]
public class AlertsModel(IAlertService alerts, IMonitoringService monitoring) : AdminPageModel
{
    public List<AlertRuleRow> Rules { get; private set; } = [];
    public List<AlertEvent> Events { get; private set; } = [];
    public AlertRecipients Recipients { get; private set; } = new(null, null, null);

    /// <summary>یک ردیف فرم قانون‌ها (rules[i].*).</summary>
    public sealed class RuleForm
    {
        public Guid Id { get; set; }
        public bool Enabled { get; set; }
        /// <summary>متن خام (ممکن است رقم فارسی یا «٫» داشته باشد)؛ با ParseThreshold خوانده می‌شود.</summary>
        public string? Threshold { get; set; }
        public int CooldownMinutes { get; set; }
        public bool SendExternal { get; set; }
    }

    public async Task OnGetAsync()
    {
        var ct = HttpContext.RequestAborted;
        await alerts.EnsureDefaultsAsync(ct);
        Rules = await alerts.ListAsync(ct);
        Events = await alerts.RecentEventsAsync(50, ct);
        Recipients = await alerts.GetRecipientsAsync(ct);
    }

    [AdminPermission(AdminPermissions.SystemManage)]
    public async Task<IActionResult> OnPostSaveAsync([FromForm(Name = "rules")] List<RuleForm> rules)
    {
        var inputs = new List<AlertRuleInput>();
        foreach (var r in rules)
        {
            if (ParseThreshold(r.Threshold) is not { } threshold)
            {
                ErrorMessage = "آستانه باید عدد باشد.";
                return Redirect("/admin/system/alerts");
            }
            inputs.Add(new AlertRuleInput(r.Id, r.Enabled, threshold, r.CooldownMinutes, r.SendExternal));
        }
        var result = await alerts.SaveAsync(inputs, HttpContext.RequestAborted);
        if (Succeeded(result))
            await monitoring.LogActionAsync("AlertRulesSave", "System", null, "ویرایش قانون‌های هشدار");
        ShowResult(result);
        return Redirect("/admin/system/alerts");
    }

    /// <summary>عدد با رقم فارسی/عربی و جداکنندهٔ «٫» یا «.». مثال: «۱٫۵» → 1.5؛ «۱٬۰۰۰» → 1000؛ «abc» → null.</summary>
    public static double? ParseThreshold(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        // جداکنندهٔ هزارگان («,» و «٬») حذف و جداکنندهٔ اعشار فارسی به «.» تبدیل می‌شود.
        var chars = text.Trim().Where(c => c is not (',' or '٬')).Select(c => c switch
        {
            >= '۰' and <= '۹' => (char)('0' + (c - '۰')),
            >= '٠' and <= '٩' => (char)('0' + (c - '٠')),
            '٫' or '/' => '.',
            _ => c,
        }).ToArray();
        return double.TryParse(new string(chars), global::System.Globalization.NumberStyles.Float, global::System.Globalization.CultureInfo.InvariantCulture, out var value)
            && double.IsFinite(value) ? value : null;
    }

    [AdminPermission(AdminPermissions.SystemManage)]
    public async Task<IActionResult> OnPostRecipientsAsync(string? emailTo, string? baleChatId, string? telegramChatId)
    {
        var result = await alerts.SaveRecipientsAsync(new AlertRecipients(emailTo, baleChatId, telegramChatId), HttpContext.RequestAborted);
        if (Succeeded(result))
            await monitoring.LogActionAsync("AlertRecipientsSave", "System", null, "ویرایش گیرندگان هشدار");
        ShowResult(result);
        return Redirect("/admin/system/alerts#recipients");
    }

    [AdminPermission(AdminPermissions.SystemManage)]
    public async Task<IActionResult> OnPostTestAsync()
    {
        ShowResult(await alerts.SendTestAsync(HttpContext.RequestAborted));
        return Redirect("/admin/system/alerts#recipients");
    }
}
