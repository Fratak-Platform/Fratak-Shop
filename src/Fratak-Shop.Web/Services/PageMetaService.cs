using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using Fratak_Shop.Core.DTOs.Blogs.Posts.Query;
using Fratak_Shop.Core.DTOs.Pages.Query;
using Fratak_Shop.Core.Services.Pages;
using Fratak_Shop.Data.Entities.Pages;
using Fratak_Shop.Web.Infrastructure;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace Fratak_Shop.Web.Services;

/// <summary>
/// Fills the SEO metadata the layout renders (title, description, canonical, robots,
/// Open Graph, structured data) from the SEO records stored in the database, with
/// professional automatic fallbacks when nothing has been configured.
/// </summary>
public interface IPageMetaService
{
    /// <summary>Static pages: reads the <see cref="SitePage"/> SEO record and marks the page for the page alerts.</summary>
    Task ApplyAsync(ViewDataDictionary viewData, SitePage page, string defaultTitle, string? defaultDescription = null);

    /// <summary>Entity pages (product, category, post, content page): uses the entity SEO metadata.</summary>
    void Apply(ViewDataDictionary viewData, SeoMetaDataDto? seo, string defaultTitle, string? defaultDescription = null,
        string? imageUrl = null, string ogType = "website");

    /// <summary>Adds a JSON-LD block (serialized safely for an inline script element).</summary>
    void AddJsonLd(ViewDataDictionary viewData, object schema);

    /// <summary>Adds a schema.org BreadcrumbList; "خانه" is prepended automatically.</summary>
    void AddBreadcrumbs(ViewDataDictionary viewData, params (string Name, string? Path)[] items);

    /// <summary>Absolute URL (scheme + host) of a site relative path.</summary>
    string? AbsoluteUrl(string? url);
}

/// <summary>Pages that are personal or transactional: never indexed, whatever the SEO settings say.</summary>
public static class PrivatePaths
{
    public static readonly string[] Prefixes =
    [
        "/profile", "/checkout", "/cart", "/payment", "/api/", "/login", "/register",
        "/verify", "/welcome", "/logout", "/compare", "/track-order", "/uploads/returns/",
    ];

    public static bool IsPrivate(Microsoft.AspNetCore.Http.PathString path) =>
        path.HasValue && Prefixes.Any(prefix => prefix.EndsWith('/')
            ? path.Value!.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            : path.StartsWithSegments(prefix, StringComparison.OrdinalIgnoreCase));
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

    /// <summary>Custom JSON-LD from the SEO record (replaces the automatic schema of the page).</summary>
    public const string JsonLd = "JsonLd";

    /// <summary>Automatic JSON-LD blocks (<c>List&lt;string&gt;</c>).</summary>
    public const string JsonLdBlocks = "JsonLdBlocks";

    public const string PrevPage = "PrevPage";
    public const string NextPage = "NextPage";
    /// <summary>The <see cref="Fratak_Shop.Data.Entities.Pages.SitePage"/> of the request (page alerts).</summary>
    public const string SitePage = "SitePage";
    public const string AlertProductId = "AlertProductId";
    public const string AlertCategoryId = "AlertCategoryId";
}

public class PageMetaService(
    IPageSeoService pageSeoService,
    IHttpContextAccessor httpContextAccessor,
    ISiteContext site,
    ILogger<PageMetaService> logger) : IPageMetaService
{
    public static readonly JsonSerializerOptions JsonLdOptions = new()
    {
        // Escapes <, >, & and quotes as \uXXXX, so the JSON can't close the script element.
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    public async Task ApplyAsync(ViewDataDictionary viewData, SitePage page, string defaultTitle, string? defaultDescription = null)
    {
        PageSeoDto? pageSeo = null;

        try
        {
            pageSeo = await pageSeoService.GetPageSeoByPageAsync(page);
        }
        catch (Exception exception)
        {
            // SEO data is optional – a failure here must never break the page.
            logger.LogWarning(exception, "Page SEO of {Page} could not be loaded.", page);
        }

        Apply(viewData, pageSeo?.Seo, defaultTitle, defaultDescription);
        viewData[PageMetaKeys.SitePage] = page;  // page alerts (rendered by the layout)
    }

    public void Apply(ViewDataDictionary viewData, SeoMetaDataDto? seo, string defaultTitle, string? defaultDescription = null,
        string? imageUrl = null, string ogType = "website")
    {
        var settings = site.IsLoaded ? site.Settings : null;
        var title = string.IsNullOrWhiteSpace(seo?.Title) ? defaultTitle : seo!.Title!;
        var description = Truncate(PlainText(string.IsNullOrWhiteSpace(seo?.MetaDescription)
            ? defaultDescription ?? settings?.SiteDescription
            : seo!.MetaDescription), 300);

        viewData[PageMetaKeys.Title] = title;
        viewData[PageMetaKeys.Description] = description;
        viewData[PageMetaKeys.Keywords] = string.IsNullOrWhiteSpace(seo?.MetaKeywords) ? settings?.DefaultMetaKeywords : seo!.MetaKeywords;
        viewData[PageMetaKeys.Canonical] = string.IsNullOrWhiteSpace(seo?.CanonicalUrl) ? CurrentUrl() : AbsoluteUrl(seo!.CanonicalUrl);
        viewData[PageMetaKeys.Robots] = BuildRobots(seo);
        viewData[PageMetaKeys.OgTitle] = string.IsNullOrWhiteSpace(seo?.OgTitle) ? (site.IsLoaded ? site.Title(title) : title) : seo!.OgTitle;
        viewData[PageMetaKeys.OgDescription] = string.IsNullOrWhiteSpace(seo?.OgDescription) ? description : seo!.OgDescription;
        viewData[PageMetaKeys.OgImage] = AbsoluteUrl(imageUrl ?? settings?.DefaultOgImageUrl ?? "/assets/image/logo.png");
        viewData[PageMetaKeys.OgType] = string.IsNullOrWhiteSpace(seo?.OgType) ? ogType : seo!.OgType;
        viewData[PageMetaKeys.JsonLd] = SafeJsonLd(seo?.StructuredDataJsonLd);
    }

    public void AddJsonLd(ViewDataDictionary viewData, object schema)
    {
        if (viewData[PageMetaKeys.JsonLdBlocks] is not List<string> blocks)
        {
            blocks = [];
            viewData[PageMetaKeys.JsonLdBlocks] = blocks;
        }

        blocks.Add(JsonSerializer.Serialize(schema, JsonLdOptions));
    }

    public void AddBreadcrumbs(ViewDataDictionary viewData, params (string Name, string? Path)[] items)
    {
        var all = new List<(string Name, string? Path)> { ("خانه", "/") };
        all.AddRange(items.Where(item => !string.IsNullOrWhiteSpace(item.Name)));

        AddJsonLd(viewData, new Dictionary<string, object>
        {
            ["@context"] = "https://schema.org",
            ["@type"] = "BreadcrumbList",
            ["itemListElement"] = all.Select((item, index) =>
            {
                var element = new Dictionary<string, object>
                {
                    ["@type"] = "ListItem",
                    ["position"] = index + 1,
                    ["name"] = item.Name,
                };
                // The last crumb is the current page; Google allows omitting its URL.
                var url = item.Path is null ? (index == all.Count - 1 ? CurrentUrl() : null) : AbsoluteUrl(item.Path);
                if (url is not null)
                    element["item"] = url;
                return element;
            }).ToList(),
        });
    }

    public string? AbsoluteUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return url;

        if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return url;

        var request = httpContextAccessor.HttpContext?.Request;
        if (request is null)
            return url;

        var path = url.StartsWith("~/") ? url[1..] : url.StartsWith('/') ? url : "/" + url;
        return $"{request.Scheme}://{request.Host}{request.PathBase}{path}";
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

    /// <summary>Canonical URL of the current request: lower-case path, no query string.</summary>
    private string? CurrentUrl()
    {
        var request = httpContextAccessor.HttpContext?.Request;
        if (request is null)
            return null;

        var path = (request.PathBase + request.Path).Value ?? "/";
        if (path.Length > 1)
            path = path.TrimEnd('/');

        return $"{request.Scheme}://{request.Host}{path.ToLowerInvariant()}";
    }

    /// <summary>Admin supplied JSON-LD: must be JSON and must not be able to close the script element.</summary>
    private static string? SafeJsonLd(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            using var document = JsonDocument.Parse(json);
            return JsonSerializer.Serialize(document.RootElement, JsonLdOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? PlainText(string? value) =>
        string.IsNullOrWhiteSpace(value) ? value : Fratak_Shop.Core.Utilities.XssSecurity.ToPlainText(value);

    private static string? Truncate(string? value, int length)
    {
        if (string.IsNullOrWhiteSpace(value))
            return value;

        var text = value.Trim();
        return text.Length <= length ? text : text[..length].TrimEnd() + "…";
    }
}
