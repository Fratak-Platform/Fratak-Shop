using Fratak_Shop.Core.DTOs.Seo.Query;
using Fratak_Shop.Core.Services.Navigation;
using Fratak_Shop.Core.Services.Notifications;
using Fratak_Shop.Core.Services.Pages;
using Fratak_Shop.Core.Services.Seo;
using Fratak_Shop.Core.Services.Settings;
using Fratak_Shop.Core.Services.Shopping;
using Fratak_Shop.Data.Entities.Pages;
using Fratak_Shop.Data.Entities.Seo;
using Fratak_Shop.Web.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Pages;

/// <summary>صفحه وضعیت (۴۰۴، ۴۰۳، ۵۰۰) با محتوای سئوشده و پیشنهاد مسیر.</summary>
public class StatusModel(
    ISiteSettingService settings,
    IMenuService menus,
    ICartService cart,
    INotificationService notifications,
    ISeoService seoService,
    IPageSeoService pageSeo,
    IPageAlertService pageAlerts)
    : FratakPageModel(settings, menus, cart, notifications, seoService)
{
    public new int StatusCode { get; private set; }

    public string Title { get; private set; } = string.Empty;

    public string Message { get; private set; } = string.Empty;

    public async Task<IActionResult> OnGetAsync(int code)
    {
        StatusCode = code;

        (Title, Message) = code switch
        {
            404 => ("صفحه مورد نظر پیدا نشد", "ممکن است آدرس تغییر کرده باشد یا کالا حذف شده باشد. از جستجوی زیر استفاده کنید."),
            403 => ("دسترسی به این صفحه مجاز نیست", "برای مشاهده این بخش باید با حساب کاربری مناسب وارد شوید."),
            500 => ("خطای غیرمنتظره در سرور", "تیم فنی ما از این خطا باخبر شده است؛ لطفاً کمی بعد دوباره تلاش کنید."),
            _ => ("وضعیت نامشخص", "درخواست شما قابل پردازش نبود."),
        };

        var page = code == 404 ? SitePage.NotFound : SitePage.None;
        var seo = page == SitePage.None ? null : await pageSeo.GetAsync(page);

        ApplySeo(seo?.Seo, new SeoFallback
        {
            SiteName = Site.SiteName,
            SiteUrl = Site.SiteUrl,
            Path = $"/status/{code}",
            ContentTitle = Title,
            ShortDescription = Message,
            EntityType = SeoEntityType.Page,
            ForceNoIndex = true,
        });

        if (page != SitePage.None)
        {
            var alerts = await pageAlerts.GetVisibleAsync(page);
            if (alerts.Count > 0)
                ViewData["PageAlertsTop"] = alerts;
        }

        Response.StatusCode = code;
        return Page();
    }
}
