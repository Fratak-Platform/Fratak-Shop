using Fratak_Shop.Core.DTOs.Blogs.Posts.Query;
using Fratak_Shop.Core.DTOs.Pages.Query;
using Fratak_Shop.Core.Services.Pages;
using Fratak_Shop.Data.Entities.Pages;
using Fratak_Shop.Web.Infrastructure;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Options;

namespace Fratak_Shop.Web.Services;

/// <summary>
/// Fills the SEO metadata the layout renders (title, description, canonical,
/// robots, Open Graph) from the SEO records managed in the admin panel, with
/// sensible fallbacks when nothing has been configured yet.
/// </summary>
public interface IPageMetaService
{
    /// <summary>Static pages: reads the <see cref="SitePage"/> SEO record and the page alerts.</summary>
    Task ApplyAsync(ViewDataDictionary viewData, SitePage page, string defaultTitle, string? defaultDescription = null);

    /// <summary>Content pages (product, category, post): uses the entity SEO metadata.</summary>
    void Apply(ViewDataDictionary viewData, SeoMetaDataDto? seo, string defaultTitle, string? defaultDescription = null,
        string? imageUrl = null, string ogType = "website");
}

public static class PageMetaKeys
{
    public const string Title = "Title";
    public const string Description = "MetaDescription";
    public const string Keywords = "MetaKeywords";
    public const string Canonical = "Canonical";
    public const string Robots = "Robots";
    public const string OgTitle = "OgTitle";
    public const string OgDescription = "OgDescription";
    public const string OgImage = "OgImage";
    public const string OgType = "OgType";
    public const string JsonLd = "JsonLd";
    public const string Alerts = "PageAlerts";
}

public class PageMetaService(
    IPageSeoService pageSeoService,
    IPageAlertService pageAlertService,
    IHttpContextAccessor httpContextAccessor,
    IOptions<ShopOptions> shopOptions) : IPageMetaService
{
    public async Task ApplyAsync(ViewDataDictionary viewData, SitePage page, string defaultTitle, string? defaultDescription = null)
    {
        PageSeoDto? pageSeo = null;
        List<PageAlertDto> alerts = [];

        try
        {
            pageSeo = await pageSeoService.GetPageSeoByPageAsync(page);
            alerts = await pageAlertService.GetActiveAlertsAsync(page);
        }
        catch (Exception)
        {
            // SEO data is optional – a failure here must never break the page.
        }

        Apply(viewData, pageSeo?.Seo, defaultTitle, defaultDescription);
        viewData[PageMetaKeys.Alerts] = alerts;
    }

    public void Apply(ViewDataDictionary viewData, SeoMetaDataDto? seo, string defaultTitle, string? defaultDescription = null,
        string? imageUrl = null, string ogType = "website")
    {
        var siteName = shopOptions.Value.SiteName;
        var title = string.IsNullOrWhiteSpace(seo?.Title) ? defaultTitle : seo!.Title!;
        var description = string.IsNullOrWhiteSpace(seo?.MetaDescription) ? defaultDescription : seo!.MetaDescription;

        viewData[PageMetaKeys.Title] = title;
        viewData[PageMetaKeys.Description] = Truncate(description, 300);
        viewData[PageMetaKeys.Keywords] = seo?.MetaKeywords;
        viewData[PageMetaKeys.Canonical] = string.IsNullOrWhiteSpace(seo?.CanonicalUrl) ? CurrentUrl() : seo!.CanonicalUrl;
        viewData[PageMetaKeys.Robots] = BuildRobots(seo);
        viewData[PageMetaKeys.OgTitle] = string.IsNullOrWhiteSpace(seo?.OgTitle) ? $"{title} | {siteName}" : seo!.OgTitle;
        viewData[PageMetaKeys.OgDescription] = string.IsNullOrWhiteSpace(seo?.OgDescription) ? Truncate(description, 300) : seo!.OgDescription;
        viewData[PageMetaKeys.OgImage] = AbsoluteUrl(imageUrl);
        viewData[PageMetaKeys.OgType] = string.IsNullOrWhiteSpace(seo?.OgType) ? ogType : seo!.OgType;
        viewData[PageMetaKeys.JsonLd] = seo?.StructuredDataJsonLd;
    }

    private static string? BuildRobots(SeoMetaDataDto? seo)
    {
        if (seo is null)
            return null;

        var parts = new List<string>
        {
            seo.NoIndex == true ? "noindex" : "index",
            seo.NoFollow == true ? "nofollow" : "follow",
        };

        if (seo.NoSnippet == true) parts.Add("nosnippet");
        if (seo.NoImageIndex == true) parts.Add("noimageindex");
        if (seo.MaxSnippet.HasValue) parts.Add($"max-snippet:{seo.MaxSnippet}");
        if (!string.IsNullOrWhiteSpace(seo.MaxImagePreview)) parts.Add($"max-image-preview:{seo.MaxImagePreview}");

        return string.Join(", ", parts);
    }

    private string? CurrentUrl()
    {
        var request = httpContextAccessor.HttpContext?.Request;
        return request is null ? null : $"{request.Scheme}://{request.Host}{request.PathBase}{request.Path}";
    }

    private string? AbsoluteUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            return url;

        var request = httpContextAccessor.HttpContext?.Request;
        return request is null ? url : $"{request.Scheme}://{request.Host}{request.PathBase}{url}";
    }

    private static string? Truncate(string? value, int length)
    {
        if (string.IsNullOrWhiteSpace(value))
            return value;

        var text = value.Trim();
        return text.Length <= length ? text : text[..length].TrimEnd() + "…";
    }
}
