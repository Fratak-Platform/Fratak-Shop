using Fratak_Shop.Core.DTOs.Settings.Query;
using Fratak_Shop.Core.Services.Media;
using Fratak_Shop.Core.Services.Messaging;
using Fratak_Shop.Core.Services.Navigation;
using Fratak_Shop.Core.Services.Notifications;
using Fratak_Shop.Core.Services.Payments;
using Fratak_Shop.Core.Services.Seo;
using Fratak_Shop.Core.Services.Settings;
using Fratak_Shop.Data.Entities.Settings;
using Fratak_Shop.Web.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Areas.Admin.Pages.Settings;

/// <summary>
/// تنظیمات سایت: هر مقدار از جدول تنظیمات خوانده و ذخیره می‌شود و هیچ بخشی از
/// هدر، فوتر، تماس و پرداخت در کد ثابت نیست.
/// </summary>
[Authorize(Policy = PolicyNames.SuperAdminOnly)]
public class IndexModel(
    ISiteSettingService settings,
    IMenuService menus,
    INotificationService notifications,
    IBankTransferService bankTransfers,
    ISeoAuditService seoAudits,
    ISmsSender smsSender,
    IMediaService media)
    : AdminPageModel(settings, menus, notifications, bankTransfers, seoAudits, smsSender)
{
    public List<SettingGroupDto> Groups { get; private set; } = [];

    [BindProperty]
    public List<SettingValueInput> Values { get; set; } = [];

    [BindProperty]
    public IFormFile? Logo { get; set; }

    [BindProperty]
    public IFormFile? Favicon { get; set; }

    [BindProperty]
    public IFormFile? FooterLogo { get; set; }

    public async Task OnGetAsync()
    {
        ViewData["Title"] = "تنظیمات سایت";
        await settings.EnsureDefaultsAsync();
        Groups = await settings.GetGroupedAsync();
    }

    public async Task<IActionResult> OnPostAsync(string? group)
    {
        var values = new Dictionary<string, string?>();

        foreach (var item in Values)
        {
            if (string.IsNullOrWhiteSpace(item.Key))
                continue;

            values[item.Key] = item.ValueType switch
            {
                SettingValueType.Bool => item.BoolValue ? "true" : "false",
                _ => item.Value,
            };
        }

        var logo = await SaveImageAsync(Logo, MediaFolders.Social, "Header.LogoImageName", values);
        var favicon = await SaveImageAsync(Favicon, MediaFolders.Social, "Header.FaviconImageName", values);
        var footerLogo = await SaveImageAsync(FooterLogo, MediaFolders.Social, "Footer.LogoImageName", values);

        if (!logo || !favicon || !footerLogo)
            return RedirectToPage("/Settings/Index", new { area = "Admin", group });

        var result = await settings.SetManyAsync(values);
        Notify(result.Message, result.IsSuccess() ? "success" : "error");

        return RedirectToPage("/Settings/Index", new { area = "Admin", group });
    }

    private async Task<bool> SaveImageAsync(IFormFile? file, string folder, string key, IDictionary<string, string?> values)
    {
        if (file is not { Length: > 0 })
            return true;

        var saved = await media.SaveImageAsync(file, folder);
        if (!saved.IsSuccess())
        {
            Notify(saved.Message, "error");
            return false;
        }

        values[key] = saved.Data;
        return true;
    }

    public class SettingValueInput
    {
        public string Key { get; set; } = null!;
        public string? Value { get; set; }
        public bool BoolValue { get; set; }
        public SettingValueType ValueType { get; set; }
    }
}
