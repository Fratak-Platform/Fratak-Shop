using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Core.Services.Ops;
using Fratak_Shop.Core.Services.Settings;
using Fratak_Shop.Data.Entities.Ops;
using Fratak_Shop.Web.Infrastructure.Admin;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Areas.Admin.Pages.SystemPages;

/// <summary>
/// صفحهٔ «فنی ← فایروال» (مرحلهٔ هفتم – فاز ۹): مسدود/مجاز کردن IP و بازه (CIDR)، مسدود کردن ربات‌های مخرب با User-Agent،
/// و IPهای مشکوک (ورود ناموفق زیاد) با دکمهٔ مسدودسازی سریع.
/// </summary>
[AdminPermission(AdminPermissions.MonitoringView, AdminPermissions.SystemManage)]
public class FirewallModel(IFirewallService firewall, IStoreSettingService settings, IMonitoringService monitoring) : AdminPageModel
{
    public List<IpRule> IpRules { get; private set; } = [];
    public List<BotRule> BotRules { get; private set; } = [];
    public List<SuspiciousIp> Suspicious { get; private set; } = [];
    public bool AutoBlockEnabled { get; private set; }
    public int AutoBlockThreshold { get; private set; }
    public int AutoBlockHours { get; private set; }
    public string? CurrentIp { get; private set; }

    public async Task OnGetAsync()
    {
        var ct = HttpContext.RequestAborted;
        IpRules = await firewall.ListIpRulesAsync(ct);
        BotRules = await firewall.ListBotRulesAsync(ct);
        Suspicious = await firewall.SuspiciousAsync(5, ct);
        var s = await settings.GetAsync(ct);
        AutoBlockEnabled = s.IsIpAutoBlockEnabled;
        AutoBlockThreshold = s.IpAutoBlockThreshold;
        AutoBlockHours = s.IpAutoBlockHours;
        CurrentIp = HttpContext.Connection.RemoteIpAddress is { } ip ? (ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4() : ip).ToString() : null;
    }

    [AdminPermission(AdminPermissions.SystemManage)]
    public async Task<IActionResult> OnPostAddIpAsync(string value, IpRuleAction action, string? reason, int? hours)
    {
        var result = await firewall.AddIpRuleAsync(new IpRuleInput(value ?? "", action, reason, hours), ActorName, HttpContext.Connection.RemoteIpAddress, HttpContext.RequestAborted);
        if (Succeeded(result))
            await monitoring.LogActionAsync(action == IpRuleAction.Block ? "IpBlock" : "IpAllow", "Firewall", null, $"{(action == IpRuleAction.Block ? "مسدود کردن" : "مجاز کردن")} IP {value}{(hours is { } h ? $" برای {h} ساعت" : "")}");
        ShowResult(result);
        return Redirect("/admin/system/firewall");
    }

    [AdminPermission(AdminPermissions.SystemManage)]
    public async Task<IActionResult> OnPostDeleteIpAsync(Guid id)
    {
        var result = await firewall.DeleteIpRuleAsync(id, HttpContext.RequestAborted);
        if (Succeeded(result))
            await monitoring.LogActionAsync("IpRuleDelete", "Firewall", id, "حذف قانون IP");
        ShowResult(result);
        return Redirect("/admin/system/firewall");
    }

    [AdminPermission(AdminPermissions.SystemManage)]
    public async Task<IActionResult> OnPostAddBotAsync(string pattern, string? reason)
    {
        var result = await firewall.AddBotRuleAsync(pattern ?? "", reason, HttpContext.RequestAborted);
        if (Succeeded(result))
            await monitoring.LogActionAsync("BotBlock", "Firewall", null, "مسدود کردن ربات: " + pattern);
        ShowResult(result);
        return Redirect("/admin/system/firewall#bots");
    }

    [AdminPermission(AdminPermissions.SystemManage)]
    public async Task<IActionResult> OnPostToggleBotAsync(Guid id, bool active)
    {
        ShowResult(await firewall.SetBotRuleActiveAsync(id, active, HttpContext.RequestAborted));
        return Redirect("/admin/system/firewall#bots");
    }

    [AdminPermission(AdminPermissions.SystemManage)]
    public async Task<IActionResult> OnPostDeleteBotAsync(Guid id)
    {
        var result = await firewall.DeleteBotRuleAsync(id, HttpContext.RequestAborted);
        if (Succeeded(result))
            await monitoring.LogActionAsync("BotRuleDelete", "Firewall", id, "حذف قانون ربات");
        ShowResult(result);
        return Redirect("/admin/system/firewall#bots");
    }
}
