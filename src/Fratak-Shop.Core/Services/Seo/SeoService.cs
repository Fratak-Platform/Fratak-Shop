using System.Text;
using System.Xml.Linq;
using Fratak_Shop.Core.DTOs.Seo.Query;
using Fratak_Shop.Core.SeoEngine;
using Fratak_Shop.Core.Services.Settings;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Content;
using Fratak_Shop.Data.Entities.Pages;
using Fratak_Shop.Data.Entities.Seo;
using Fratak_Shop.Data.Entities.SeoRedirects;
using Fratak_Shop.Data.Utilities.SeoManagement;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Core.Services.Seo;

public class SeoService(FratakShopDbContext context, ISiteSettingService settingService) : ISeoService
{
    private const string ImageBasePath = "/uploads";

    public SeoRenderModel Build(SeoMetaData? seo, SeoFallback fallback)
    {
        var model = SeoRenderModel.FromEntity(seo);
        var siteName = string.IsNullOrWhiteSpace(fallback.SiteName) ? "فراتک" : fallback.SiteName;
        var siteUrl = (fallback.SiteUrl ?? string.Empty).TrimEnd('/');

        // ------------------------------------------------------------- title
        if (string.IsNullOrWhiteSpace(model.Title))
            model.Title = string.IsNullOrWhiteSpace(fallback.ContentTitle)
                ? siteName
                : $"{fallback.ContentTitle} | {siteName}";

        // --------------------------------------------------------- description
        if (string.IsNullOrWhiteSpace(model.MetaDescription))
            model.MetaDescription = !string.IsNullOrWhiteSpace(fallback.ShortDescription)
                ? TextAnalyzer.StripHtml(fallback.ShortDescription)
                : fallback.DefaultDescription;

        if (model.MetaDescription is { Length: > 165 })
            model.MetaDescription = model.MetaDescription[..162].TrimEnd(' ', '،') + "…";

        if (string.IsNullOrWhiteSpace(model.MetaKeywords))
            model.MetaKeywords = fallback.DefaultKeywords;

        // ------------------------------------------------------------ canonical
        model.CanonicalUrl ??= $"{siteUrl}{fallback.Path}";

        // --------------------------------------------------------------- robots
        var robots = new List<string>();
        var noIndex = model.NoIndex || fallback.ForceNoIndex;
        robots.Add(noIndex ? "noindex" : "index");
        robots.Add(model.NoFollow ? "nofollow" : "follow");

        if (seo?.NoSnippet == true) robots.Add("nosnippet");
        if (seo?.NoImageIndex == true) robots.Add("noimageindex");
        if (seo?.NoTranslate == true) robots.Add("notranslate");
        if (seo?.MaxSnippet is > 0) robots.Add($"max-snippet:{seo.MaxSnippet}");
        if (!string.IsNullOrWhiteSpace(seo?.MaxImagePreview)) robots.Add($"max-image-preview:{seo.MaxImagePreview}");
        if (seo?.MaxVideoPreview is > 0) robots.Add($"max-video-preview:{seo.MaxVideoPreview}");

        model.Robots = string.Join(", ", robots);
        model.NoIndex = noIndex;

        // ----------------------------------------------------------------- og
        model.OgTitle ??= model.Title;
        model.OgDescription ??= model.MetaDescription;
        model.OgUrl ??= model.CanonicalUrl;
        model.OgType ??= SeoCopyWriter.BuildOgType(fallback.EntityType);
        model.OgSiteName ??= siteName;
        model.OgImageUrl ??= BuildImageUrl(siteUrl, fallback.ImageName, fallback.ImageFolder);
        model.OgImageAlt ??= fallback.ImageAlt ?? model.OgTitle;
        model.XCard ??= fallback.ImageName is null ? "summary" : "summary_large_image";
        model.XTitle ??= model.OgTitle;
        model.XDescription ??= model.OgDescription;
        model.XImageUrl ??= model.OgImageUrl;
        model.PublisherName ??= siteName;
        model.DefaultShareImageUrl ??= BuildImageUrl(siteUrl, fallback.DefaultShareImageName, "seo");
        model.PrevPageUrl = fallback.PrevPageUrl;
        model.NextPageUrl = fallback.NextPageUrl;

        // ------------------------------------------------------- structured data
        if (string.IsNullOrWhiteSpace(model.StructuredDataJsonLd))
            model.StructuredDataJsonLd = fallback.AutoSchemaJsonLd ?? BuildWebPageSchema(siteName, siteUrl, model, fallback);

        return model;
    }

    public async Task<string> GetSitemapXmlAsync(CancellationToken cancellationToken = default)
    {
        var storefront = await settingService.GetStorefrontSettingsAsync();
        var siteUrl = storefront.SiteUrl.TrimEnd('/');
        var entries = new List<SitemapEntry>();

        // static pages
        entries.Add(new SitemapEntry { Url = $"{siteUrl}/", ChangeFrequency = "daily", Priority = 1.0m, LastModified = DateTimeOffset.UtcNow });
        entries.Add(new SitemapEntry { Url = $"{siteUrl}/products", ChangeFrequency = "daily", Priority = 0.9m, LastModified = DateTimeOffset.UtcNow });
        entries.Add(new SitemapEntry { Url = $"{siteUrl}/blog", ChangeFrequency = "daily", Priority = 0.8m, LastModified = DateTimeOffset.UtcNow });
        entries.Add(new SitemapEntry { Url = $"{siteUrl}/contact-us", ChangeFrequency = "monthly", Priority = 0.5m, LastModified = DateTimeOffset.UtcNow });
        entries.Add(new SitemapEntry { Url = $"{siteUrl}/about-us", ChangeFrequency = "monthly", Priority = 0.5m, LastModified = DateTimeOffset.UtcNow });
        entries.Add(new SitemapEntry { Url = $"{siteUrl}/faq", ChangeFrequency = "monthly", Priority = 0.5m, LastModified = DateTimeOffset.UtcNow });

        // products
        var products = await context.Products
            .Where(product => product.IsActive && (product.Seo == null || product.Seo.IncludeInSitemap == null || product.Seo.IncludeInSitemap == true))
            .Select(product => new
            {
                product.Slug,
                product.UpdatedAt,
                Image = product.Images.Where(image => image.IsPrimary).Select(image => image.ImageName).FirstOrDefault(),
                product.Name,
                Priority = product.Seo != null ? product.Seo.SitemapPriority : null,
                ChangeFrequency = product.Seo != null ? product.Seo.SitemapChangeFrequency : null,
                NoIndex = product.Seo != null && product.Seo.NoIndex == true,
            })
            .ToListAsync(cancellationToken);

        entries.AddRange(products
            .Where(product => !product.NoIndex)
            .Select(product => new SitemapEntry
            {
                Url = $"{siteUrl}/product/{product.Slug}",
                LastModified = product.UpdatedAt,
                Priority = product.Priority ?? 0.8m,
                ChangeFrequency = string.IsNullOrWhiteSpace(product.ChangeFrequency) ? "weekly" : product.ChangeFrequency!,
                ImageUrl = string.IsNullOrWhiteSpace(product.Image) ? null : $"{siteUrl}{ImageBasePath}/products/{product.Image}",
                ImageTitle = product.Name,
            }));

        // categories
        var categories = await context.Categories
            .Where(category => category.IsActive)
            .Select(category => new
            {
                category.Slug,
                category.UpdatedAt,
                Priority = category.Seo != null ? category.Seo.SitemapPriority : null,
                ChangeFrequency = category.Seo != null ? category.Seo.SitemapChangeFrequency : null,
                NoIndex = category.Seo != null && category.Seo.NoIndex == true,
            })
            .ToListAsync(cancellationToken);

        entries.AddRange(categories
            .Where(category => !category.NoIndex)
            .Select(category => new SitemapEntry
            {
                Url = $"{siteUrl}/category/{category.Slug}",
                LastModified = category.UpdatedAt,
                Priority = category.Priority ?? 0.7m,
                ChangeFrequency = string.IsNullOrWhiteSpace(category.ChangeFrequency) ? "weekly" : category.ChangeFrequency!,
            }));

        // blog
        var posts = await context.BlogPosts
            .Where(post => post.IsPublished)
            .Select(post => new
            {
                post.Slug,
                post.UpdatedAt,
                post.ImageName,
                post.Title,
                Priority = post.Seo != null ? post.Seo.SitemapPriority : null,
                ChangeFrequency = post.Seo != null ? post.Seo.SitemapChangeFrequency : null,
                NoIndex = post.Seo != null && post.Seo.NoIndex == true,
            })
            .ToListAsync(cancellationToken);

        entries.AddRange(posts
            .Where(post => !post.NoIndex)
            .Select(post => new SitemapEntry
            {
                Url = $"{siteUrl}/blog/{post.Slug}",
                LastModified = post.UpdatedAt,
                Priority = post.Priority ?? 0.6m,
                ChangeFrequency = string.IsNullOrWhiteSpace(post.ChangeFrequency) ? "monthly" : post.ChangeFrequency!,
                ImageUrl = string.IsNullOrWhiteSpace(post.ImageName) ? null : $"{siteUrl}{ImageBasePath}/blog/{post.ImageName}",
                ImageTitle = post.Title,
            }));

        // content pages
        var contentPages = await context.ContentPages
            .Where(page => page.IsPublished)
            .Select(page => new { page.Page, page.UpdatedAt })
            .ToListAsync(cancellationToken);

        foreach (var page in contentPages)
        {
            var path = PagePath(page.Page);
            if (path is null)
                continue;

            entries.Add(new SitemapEntry
            {
                Url = $"{siteUrl}{path}",
                LastModified = page.UpdatedAt,
                Priority = 0.5m,
                ChangeFrequency = "monthly",
            });
        }

        var ns = XNamespace.Get("http://www.sitemaps.org/schemas/sitemap/0.9");
        var imageNs = XNamespace.Get("http://www.google.com/schemas/sitemap-image/1.1");

        var document = new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            new XElement(ns + "urlset",
                new XAttribute(XNamespace.Xmlns + "image", imageNs),
                entries
                    .GroupBy(entry => entry.Url)
                    .Select(group => group.First())
                    .Select(entry => new XElement(ns + "url",
                        new XElement(ns + "loc", entry.Url),
                        new XElement(ns + "lastmod", entry.LastModified.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:sszzz")),
                        new XElement(ns + "changefreq", entry.ChangeFrequency),
                        new XElement(ns + "priority", entry.Priority.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)),
                        entry.ImageUrl is null
                            ? null
                            : new XElement(imageNs + "image",
                                new XElement(imageNs + "loc", entry.ImageUrl),
                                entry.ImageTitle is null ? null : new XElement(imageNs + "title", entry.ImageTitle))))));

        return document.Declaration + Environment.NewLine + document.ToString();
    }

    public async Task<string> GetRobotsTxtAsync(CancellationToken cancellationToken = default)
    {
        var storefront = await settingService.GetStorefrontSettingsAsync();
        var siteUrl = storefront.SiteUrl.TrimEnd('/');

        var builder = new StringBuilder();
        builder.AppendLine("User-agent: *");
        builder.AppendLine("Allow: /");
        builder.AppendLine("Disallow: /cart");
        builder.AppendLine("Disallow: /checkout");
        builder.AppendLine("Disallow: /profile");
        builder.AppendLine("Disallow: /admin");
        builder.AppendLine("Disallow: /payment");
        builder.AppendLine("Disallow: /search");
        builder.AppendLine("Disallow: /*?*");
        builder.AppendLine();

        builder.AppendLine("User-agent: Bingbot");
        builder.AppendLine("Crawl-delay: 2");
        builder.AppendLine();

        builder.AppendLine($"Sitemap: {siteUrl}/sitemap.xml");

        if (!string.IsNullOrWhiteSpace(storefront.RobotsExtra))
        {
            builder.AppendLine();
            builder.AppendLine(storefront.RobotsExtra);
        }

        return builder.ToString();
    }

    public async Task<SeoRedirect?> ResolveRedirectAsync(string path, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        var normalized = Normalize(path);

        return await context.SeoRedirects
            .FirstOrDefaultAsync(redirect => redirect.IsActive && redirect.FromPath == normalized, cancellationToken);
    }

    public async Task RegisterRedirectHitAsync(Guid redirectId, CancellationToken cancellationToken = default)
    {
        var redirect = await context.SeoRedirects.AsTracking().FirstOrDefaultAsync(item => item.Id == redirectId, cancellationToken);
        if (redirect is null)
            return;

        redirect.HitCount++;
        redirect.LastHitAt = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task LogNotFoundAsync(string path, string? referrer, string? userAgent, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        var normalized = Normalize(path);
        if (normalized.Length > 500)
            normalized = normalized[..500];

        var log = await context.NotFoundLogs.AsTracking()
            .FirstOrDefaultAsync(item => item.Path == normalized, cancellationToken);

        if (log is null)
        {
            context.NotFoundLogs.Add(new NotFoundLog
            {
                Path = normalized,
                Referrer = referrer?.Length > 1000 ? referrer[..1000] : referrer,
                UserAgent = userAgent?.Length > 300 ? userAgent[..300] : userAgent,
                HitCount = 1,
                LastHitAt = DateTimeOffset.UtcNow,
            });
        }
        else
        {
            log.HitCount++;
            log.LastHitAt = DateTimeOffset.UtcNow;
            log.IsResolved = false;
            log.ResolvedRedirectId = null;
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<List<SeoRedirect>> GetRedirectsAsync(string? search = null)
    {
        var query = context.SeoRedirects.AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(redirect => redirect.FromPath.Contains(term) || redirect.ToUrl.Contains(term));
        }

        return await query
            .OrderByDescending(redirect => redirect.HitCount)
            .ThenByDescending(redirect => redirect.CreatedAt)
            .ToListAsync();
    }

    public async Task<SeoRedirect?> GetRedirectAsync(Guid id) =>
        await context.SeoRedirects.FirstOrDefaultAsync(redirect => redirect.Id == id);

    public async Task<OperationResult> SaveRedirectAsync(Guid? id, string fromPath, string toUrl, RedirectType type,
        string? notes, bool isActive)
    {
        if (string.IsNullOrWhiteSpace(fromPath))
            return OperationResult.Error("آدرس مبدأ را وارد کنید.");

        if (string.IsNullOrWhiteSpace(toUrl))
            return OperationResult.Error("آدرس مقصد را وارد کنید.");

        var normalizedFrom = Normalize(fromPath);

        if (id.HasValue)
        {
            var redirect = await context.SeoRedirects.AsTracking().FirstOrDefaultAsync(item => item.Id == id.Value);
            if (redirect is null)
                return OperationResult.NotFound("ریدایرکت مورد نظر یافت نشد.");

            var duplicate = await context.SeoRedirects.AnyAsync(item =>
                item.Id != id.Value && item.FromPath == normalizedFrom);
            if (duplicate)
                return OperationResult.Error("برای این آدرس قبلاً ریدایرکت ثبت شده است.");

            redirect.FromPath = normalizedFrom;
            redirect.ToUrl = toUrl.Trim();
            redirect.Type = type;
            redirect.Notes = notes;
            redirect.IsActive = isActive;

            await context.SaveChangesAsync();
            return OperationResult.Success("ریدایرکت ویرایش شد.");
        }

        var exists = await context.SeoRedirects.AnyAsync(item => item.FromPath == normalizedFrom);
        if (exists)
            return OperationResult.Error("برای این آدرس قبلاً ریدایرکت ثبت شده است.");

        context.SeoRedirects.Add(new SeoRedirect
        {
            FromPath = normalizedFrom,
            ToUrl = toUrl.Trim(),
            Type = type,
            Notes = notes,
            IsActive = isActive,
        });

        await context.SaveChangesAsync();
        return OperationResult.Success("ریدایرکت ثبت شد.");
    }

    public async Task<OperationResult> DeleteRedirectAsync(Guid id)
    {
        var redirect = await context.SeoRedirects.AsTracking().FirstOrDefaultAsync(item => item.Id == id);
        if (redirect is null)
            return OperationResult.NotFound("ریدایرکت مورد نظر یافت نشد.");

        context.SeoRedirects.Remove(redirect);
        await context.SaveChangesAsync();
        return OperationResult.Success("ریدایرکت حذف شد.");
    }

    public async Task<List<NotFoundLog>> GetNotFoundLogsAsync(bool onlyUnresolved = true, int take = 50)
    {
        var query = context.NotFoundLogs.AsQueryable();
        if (onlyUnresolved)
            query = query.Where(log => !log.IsResolved);

        return await query
            .OrderByDescending(log => log.HitCount)
            .ThenByDescending(log => log.LastHitAt)
            .Take(take)
            .ToListAsync();
    }

    /// <summary>
    /// Marks the 404 entries a redirect was created for, so the report only shows
    /// the links that still need attention.
    /// </summary>
    public async Task<OperationResult> ResolveNotFoundAsync(string path, Guid redirectId, CancellationToken cancellationToken = default)
    {
        var normalized = Normalize(path);
        var log = await context.NotFoundLogs.AsTracking().FirstOrDefaultAsync(item => item.Path == normalized, cancellationToken);
        if (log is null)
            return OperationResult.NotFound("موردی برای این مسیر ثبت نشده است.");

        log.IsResolved = true;
        log.ResolvedRedirectId = redirectId;
        await context.SaveChangesAsync(cancellationToken);

        return OperationResult.Success("خطای ۴۰۴ به‌عنوان حل‌شده علامت‌گذاری شد.");
    }

    private static string Normalize(string path)
    {
        var normalized = path.Trim().ToLowerInvariant();
        if (!normalized.StartsWith('/'))
            normalized = "/" + normalized;

        var queryIndex = normalized.IndexOf('?');
        if (queryIndex > 0)
            normalized = normalized[..queryIndex];

        return normalized;
    }

    private static string? BuildImageUrl(string siteUrl, string? imageName, string? folder)
    {
        if (string.IsNullOrWhiteSpace(imageName) || string.IsNullOrWhiteSpace(folder))
            return null;

        if (imageName.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            return imageName;

        return $"{siteUrl}{ImageBasePath}/{folder.Trim('/')}/{imageName}";
    }

    private static string BuildWebPageSchema(string siteName, string siteUrl, SeoRenderModel model, SeoFallback fallback)
    {
        var graph = new List<object>
        {
            new Dictionary<string, object?>
            {
                ["@type"] = "WebPage",
                ["@id"] = model.CanonicalUrl + "#webpage",
                ["name"] = model.OgTitle,
                ["description"] = model.EffectiveDescription,
                ["url"] = model.CanonicalUrl,
                ["inLanguage"] = model.Language,
                ["isPartOf"] = new Dictionary<string, object?>
                {
                    ["@type"] = "WebSite",
                    ["name"] = siteName,
                    ["url"] = siteUrl,
                },
            },
        };

        if (!string.IsNullOrWhiteSpace(fallback.ContentTitle) && fallback.EntityType != SeoEntityType.None)
        {
            graph.Add(new Dictionary<string, object?>
            {
                ["@type"] = "BreadcrumbList",
                ["@id"] = model.CanonicalUrl + "#breadcrumb",
                ["itemListElement"] = new List<object>
                {
                    new Dictionary<string, object?> { ["@type"] = "ListItem", ["position"] = 1, ["name"] = siteName, ["item"] = siteUrl },
                    new Dictionary<string, object?> { ["@type"] = "ListItem", ["position"] = 2, ["name"] = fallback.ContentTitle, ["item"] = model.CanonicalUrl },
                },
            });
        }

        return System.Text.Json.JsonSerializer.Serialize(
            new Dictionary<string, object?> { ["@context"] = "https://schema.org", ["@graph"] = graph },
            new System.Text.Json.JsonSerializerOptions
            {
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            });
    }

    private static string? PagePath(SitePage page) => page switch
    {
        SitePage.AboutUs => "/about-us",
        SitePage.ContactUs => "/contact-us",
        SitePage.Faq => "/faq",
        SitePage.Terms => "/terms",
        SitePage.Privacy => "/privacy",
        SitePage.Warranty => "/warranty",
        _ => null,
    };
}
