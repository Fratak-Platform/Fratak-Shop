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
[Microsoft.AspNetCore.OutputCaching.OutputCache(PolicyName = Fratak_Shop.Web.Infrastructure.PageCache.PageCachePolicy.Name)]
public class ContentPageModel(IContentPageService contentPages, IPageMetaService pageMeta,
    Fratak_Shop.Core.Services.Seo.Autopilot.ISeoLinkTargetProvider linkTargets) : ShopPageModel
{
    /// <summary>
    /// متن صفحه برای نمایش: پاک‌سازی‌شده (اسکریپت/رویداد/فرم حذف) + لینک داخلی خودکار (دور سئوی خودکار).
    /// متن ذخیره‌شده دست نمی‌خورد؛ لینک‌ها فقط هنگام نمایش اضافه می‌شوند.
    /// </summary>
    public string BodyHtml { get; private set; } = string.Empty;

    public ContentPageDto PageContent { get; private set; } = null!;

    public async Task<IActionResult> OnGetAsync(string slug)
    {
        var page = await contentPages.GetActiveBySlugAsync(slug, HttpContext.RequestAborted);
        if (page is null)
            return NotFound();

        PageContent = page;
        var (targets, maxLinks) = await linkTargets.ForRenderAsync(HttpContext.RequestAborted);
        BodyHtml = Fratak_Shop.Core.Services.Seo.Autopilot.InternalLinker.Link(Fratak_Shop.Core.Utilities.XssSecurity.SanitizeText(page.Content ?? string.Empty),
            Request.Path.Value ?? "/", targets, maxLinks);

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

        ViewData[PageMetaKeys.SitePage] = page.PageKey ?? SitePage.ContentPage;

        return Page();
    }
}
