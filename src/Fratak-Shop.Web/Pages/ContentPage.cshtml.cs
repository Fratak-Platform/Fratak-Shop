using Fratak_Shop.Core.DTOs.Contents;
using Fratak_Shop.Core.Services.Contents;
using Fratak_Shop.Core.Services.Pages;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Entities.Pages;
using Fratak_Shop.Web.Infrastructure;
using Fratak_Shop.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Pages;

/// <summary>
/// Database driven static pages (about us, terms, privacy, return policy, ...).
/// Pages can be added, edited or disabled from the admin panel without code changes.
/// </summary>
public class ContentPageModel(IContentPageService contentPages, IPageAlertService pageAlerts, IPageMetaService pageMeta) : ShopPageModel
{
    public ContentPageDto Content { get; private set; } = null!;

    public async Task<IActionResult> OnGetAsync(string slug)
    {
        var page = await contentPages.GetActiveBySlugAsync(slug, HttpContext.RequestAborted);
        if (page is null)
            return NotFound();

        Content = page;

        var description = page.Summary ?? page.Content.ToPlainText(160);
        pageMeta.Apply(ViewData, page.Seo, page.Title, description, page.ImageUrl, "article");
        pageMeta.AddBreadcrumbs(ViewData, (page.Title, null));

        if (ViewData[PageMetaKeys.JsonLd] is null)
        {
            pageMeta.AddJsonLd(ViewData, new Dictionary<string, object?>
            {
                ["@context"] = "https://schema.org",
                ["@type"] = page.PageKey == SitePage.AboutUs ? "AboutPage" : "WebPage",
                ["name"] = page.Title,
                ["description"] = description,
                ["url"] = ViewData[PageMetaKeys.Canonical],
                ["dateModified"] = page.UpdatedAt.ToString("O"),
                ["inLanguage"] = "fa-IR",
            });
        }

        if (page.PageKey is { } key)
            ViewData[PageMetaKeys.Alerts] = await pageAlerts.GetActiveAlertsAsync(key);

        return Page();
    }
}
