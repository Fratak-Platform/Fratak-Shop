using Fratak_Shop.Core.DTOs.Seo.Query;
using Fratak_Shop.Core.DTOs.Content.Query;
using Fratak_Shop.Core.DTOs.Faqs.Query;
using Fratak_Shop.Core.Services.Content;
using Fratak_Shop.Core.Services.Faqs;
using Fratak_Shop.Core.Services.Navigation;
using Fratak_Shop.Core.Services.Notifications;
using Fratak_Shop.Core.Services.Pages;
using Fratak_Shop.Core.Services.Seo;
using Fratak_Shop.Core.Services.Settings;
using Fratak_Shop.Core.Services.Shopping;
using Fratak_Shop.Data.Entities.Pages;
using Fratak_Shop.Data.Entities.Seo;
using Fratak_Shop.Web.Infrastructure;
using Fratak_Shop.Web.Utilities;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Pages;

/// <summary>
/// صفحه‌های محتوایی پویا (درباره ما، تماس با ما، قوانین، گارانتی، پرسش‌های متداول و ...)
/// که مدیر سایت خودش متن آن‌ها را از پنل ویرایش می‌کند.
/// </summary>
public class PageModel(
    ISiteSettingService settings,
    IMenuService menus,
    ICartService cart,
    INotificationService notifications,
    ISeoService seoService,
    IContentPageService contentPages,
    IPageSeoService pageSeo,
    IPageAlertService pageAlerts,
    IFaqItemService faqs)
    : FratakPageModel(settings, menus, cart, notifications, seoService)
{
    public new ContentPageDto Content { get; private set; } = null!;

    public List<FaqItemDto> FaqItems { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(string? slug)
    {
        slug ??= HttpContext.Request.Path.Value?.Trim('/').ToLowerInvariant() switch
        {
            "about" or "about-us" => "about",
            "contact" or "contact-us" => "contact",
            "rules" or "terms" => "rules",
            "faq" or "faqs" => "faq",
            _ => "faq",
        };

        var page = ResolvePage(slug);
        if (page is null)
            return NotFound();

        var content = await contentPages.GetAsync(page.Value);
        if (content is null || !content.IsPublished)
            return NotFound();

        Content = content;

        if (page == SitePage.Faq)
            FaqItems = await faqs.GetActiveFaqItemsAsync();

        var seo = await pageSeo.GetAsync(page.Value);
        ApplySeo(seo?.Seo, new SeoFallback
        {
            SiteName = Site.SiteName,
            SiteUrl = Site.SiteUrl,
            DefaultDescription = Site.DefaultMetaDescription,
            DefaultKeywords = Site.DefaultMetaKeywords,
            Path = $"/{slug}",
            ContentTitle = content.Title,
            ShortDescription = content.Summary,
            EntityType = SeoEntityType.Page,
            AutoSchemaJsonLd = page == SitePage.Faq && FaqItems.Count > 0
                ? SeoJsonLd.FaqPage(FaqItems.Select(item => (item.Question, item.Answer)))
                : null,
        });

        var alerts = await pageAlerts.GetVisibleAsync(page.Value);
        if (alerts.Count > 0)
            ViewData["PageAlertsTop"] = alerts;

        return Page();
    }

    private static SitePage? ResolvePage(string slug) => slug.ToLowerInvariant() switch
    {
        "about" or "about-us" => SitePage.AboutUs,
        "contact" or "contact-us" => SitePage.ContactUs,
        "faq" => SitePage.Faq,
        "warranty" => SitePage.Warranty,
        "rules" or "terms" => SitePage.Terms,
        "privacy" => SitePage.Privacy,
        _ => null,
    };
}
