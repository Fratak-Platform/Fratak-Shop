using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Core.Services.Design;
using Fratak_Shop.Core.Services.Settings;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Web.Infrastructure.Admin;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Areas.Admin.Pages.Campaigns;

/// <summary>
/// فهرست صفحه‌های کمپین (دور ۲۱). آدرس: /admin/campaigns
/// <para>ساخت صفحه از قالب آماده («حراج»، «معرفی کالای تازه»، «مناسبتی»، «خالی») با دادهٔ نمونه؛ صفحهٔ تازه «پیش‌نویس» است تا مدیر
/// در صفحه‌ساز ویرایش و بعد منتشرش کند. قالب‌های حراج و مناسبتی یک هفته پایان پیش‌فرض دارند (شمارش معکوس واقعی؛ قابل تغییر).</para>
/// </summary>
[AdminPermission(AdminPermissions.MarketingManage, AdminPermissions.ContentManage)]
public class IndexModel(ILandingPageService landings, IStoreSettingService settings) : AdminPageModel
{
    public List<LandingListItem> Items { get; private set; } = [];

    public async Task OnGetAsync() => Items = await landings.ListAsync(HttpContext.RequestAborted);

    public async Task<IActionResult> OnPostCreateAsync(string? template, string? title)
    {
        var ct = HttpContext.RequestAborted;
        var key = LandingTemplates.List.Any(t => t.Key == template) ? template! : "campaign";
        var meta = LandingTemplates.List.First(t => t.Key == key);
        DateTimeOffset? endsAt = key is "campaign" or "gift" ? DateTimeOffset.UtcNow.AddDays(7) : null;
        var store = await settings.GetAsync(ct);
        var built = LandingTemplates.Build(key, store.SiteName, endsAt);
        var name = string.IsNullOrWhiteSpace(title) ? meta.Title : title.Trim();
        var result = await landings.SaveAsync(new LandingSaveInput(null, name, null, null, null, null, null, built.Html, built.Css,
            ShowHeader: true, ShowFooter: true, IsActive: false, StartAt: null, EndAt: endsAt, TemplateKey: key), ActorName, ct);
        if (!result.IsSuccess())
        {
            ErrorMessage = result.Message;
            return BackToList();
        }
        return LocalRedirect($"/admin/campaigns/edit/{result.Data}");
    }

    public async Task<IActionResult> OnPostDeleteAsync(Guid id)
    {
        ShowResult(await landings.DeleteAsync(id, HttpContext.RequestAborted));
        return BackToList();
    }
}
