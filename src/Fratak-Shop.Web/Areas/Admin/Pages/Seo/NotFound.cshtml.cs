using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Core.Services.Messaging;
using Fratak_Shop.Core.Services.Navigation;
using Fratak_Shop.Core.Services.Notifications;
using Fratak_Shop.Core.Services.Payments;
using Fratak_Shop.Core.Services.Seo;
using Fratak_Shop.Core.Services.Settings;
using Fratak_Shop.Data.Entities.Seo;
using Fratak_Shop.Web.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Areas.Admin.Pages.Seo;

[Authorize(Policy = PolicyNames.Seo)]
public class NotFoundModel(
    ISiteSettingService settings,
    IMenuService menus,
    INotificationService notifications,
    IBankTransferService bankTransfers,
    ISeoAuditService seoAudits,
    ISmsSender smsSender,
    ISeoService seo)
    : AdminPageModel(settings, menus, notifications, bankTransfers, seoAudits, smsSender)
{
    public List<NotFoundLog> Items { get; private set; } = [];

    [BindProperty(SupportsGet = true)]
    public bool All { get; set; }

    public async Task OnGetAsync()
    {
        ViewData["Title"] = "خطاهای ۴۰۴";
        Items = await seo.GetNotFoundLogsAsync(onlyUnresolved: !All, take: 100);
    }

    /// <summary>ساخت ریدایرکت از یک خطای ۴۰۴ و بستن آن.</summary>
    public async Task<IActionResult> OnPostResolveAsync(string path, string toUrl)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(toUrl))
        {
            Notify("مسیر مبدأ و مقصد الزامی است.", "error");
            return RedirectToPage("/Seo/NotFound", new { area = "Admin" });
        }

        var save = await seo.SaveRedirectAsync(null, path, toUrl, Data.Entities.SeoRedirects.RedirectType.MovedPermanently,
            "ایجاد از گزارش خطاهای ۴۰۴", true);

        if (save.IsSuccess())
        {
            var redirects = await seo.GetRedirectsAsync(path);
            var created = redirects.FirstOrDefault(item => item.FromPath == path);
            if (created is not null)
                await seo.ResolveNotFoundAsync(path, created.Id);
        }

        Notify(save.Message, save.IsSuccess() ? "success" : "error");
        return RedirectToPage("/Seo/NotFound", new { area = "Admin" });
    }
}
