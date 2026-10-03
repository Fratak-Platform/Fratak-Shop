using Fratak_Shop.Core.Services.Messaging;
using Fratak_Shop.Core.Services.Navigation;
using Fratak_Shop.Core.Services.Notifications;
using Fratak_Shop.Core.Services.Payments;
using Fratak_Shop.Core.Services.Seo;
using Fratak_Shop.Core.Services.Settings;
using Fratak_Shop.Data.Entities.Seo;
using Fratak_Shop.Data.Entities.SeoRedirects;
using Fratak_Shop.Web.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Areas.Admin.Pages.Seo;

[Authorize(Policy = PolicyNames.Seo)]
public class RedirectsModel(
    ISiteSettingService settings,
    IMenuService menus,
    INotificationService notifications,
    IBankTransferService bankTransfers,
    ISeoAuditService seoAudits,
    ISmsSender smsSender,
    ISeoService seo)
    : AdminPageModel(settings, menus, notifications, bankTransfers, seoAudits, smsSender)
{
    public List<SeoRedirect> Items { get; private set; } = [];

    public List<NotFoundLog> NotFoundLogs { get; private set; } = [];

    [BindProperty(SupportsGet = true, Name = "q")]
    public string? Search { get; set; }

    [BindProperty]
    public RedirectInput Input { get; set; } = new();

    public async Task OnGetAsync()
    {
        ViewData["Title"] = "مدیریت ریدایرکت‌ها";
        Items = await seo.GetRedirectsAsync(Search);
        NotFoundLogs = await seo.GetNotFoundLogsAsync(onlyUnresolved: true, take: 10);
    }

    public async Task<IActionResult> OnPostSaveAsync()
    {
        if (!ModelState.IsValid)
        {
            Notify("اطلاعات ریدایرکت کامل نیست.", "error");
            return RedirectToPage("/Seo/Redirects", new { area = "Admin" });
        }

        var result = await seo.SaveRedirectAsync(Input.Id, Input.FromPath, Input.ToUrl, Input.Type, Input.Notes, Input.IsActive);
        Notify(result.Message, result.IsSuccess() ? "success" : "error");
        return RedirectToPage("/Seo/Redirects", new { area = "Admin" });
    }

    public async Task<IActionResult> OnPostDeleteAsync(Guid id)
    {
        var result = await seo.DeleteRedirectAsync(id);
        Notify(result.Message, result.IsSuccess() ? "success" : "error");
        return RedirectToPage("/Seo/Redirects", new { area = "Admin" });
    }

    /// <summary>تبدیل یک خطای ۴۰۴ ثبت‌شده به ریدایرکت دائمی.</summary>
    public async Task<IActionResult> OnPostResolveAsync(string path, string toUrl)
    {
        var save = await seo.SaveRedirectAsync(null, path, toUrl, RedirectType.MovedPermanently, "ایجاد خودکار از گزارش ۴۰۴", true);
        if (!save.IsSuccess())
        {
            Notify(save.Message, "error");
            return RedirectToPage("/Seo/Redirects", new { area = "Admin" });
        }

        var redirects = await seo.GetRedirectsAsync(path);
        var created = redirects.FirstOrDefault(item => item.FromPath == path);
        if (created is not null)
            await seo.ResolveNotFoundAsync(path, created.Id);

        Notify("ریدایرکت ساخته و خطای ۴۰۴ بسته شد.");
        return RedirectToPage("/Seo/Redirects", new { area = "Admin" });
    }

    public class RedirectInput
    {
        public Guid? Id { get; set; }

        [System.ComponentModel.DataAnnotations.Required(ErrorMessage = "مسیر مبدأ را وارد کنید.")]
        public string FromPath { get; set; } = null!;

        [System.ComponentModel.DataAnnotations.Required(ErrorMessage = "مسیر مقصد را وارد کنید.")]
        public string ToUrl { get; set; } = null!;

        public RedirectType Type { get; set; } = RedirectType.MovedPermanently;
        public string? Notes { get; set; }
        public bool IsActive { get; set; } = true;
    }
}
