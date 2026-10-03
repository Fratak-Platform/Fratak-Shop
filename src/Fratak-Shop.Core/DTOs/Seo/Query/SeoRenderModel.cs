using Fratak_Shop.Data.Entities.Seo;
using Fratak_Shop.Data.Utilities.SeoManagement;

namespace Fratak_Shop.Core.DTOs.Seo.Query;

/// <summary>
/// Everything the layout needs to render the &lt;head&gt; of a page. The web layer
/// never reads SEO fields directly: it asks the SEO service, which fills every
/// missing value with a sensible smart default (title from the name, description
/// from the summary, image from the content, schema from the entity type).
/// </summary>
public class SeoRenderModel
{
    public string Title { get; set; } = string.Empty;
    public string? MetaDescription { get; set; }
    public string? MetaKeywords { get; set; }
    public string? CanonicalUrl { get; set; }
    public string Robots { get; set; } = "index, follow";
    public bool NoIndex { get; set; }
    public bool NoFollow { get; set; }
    public string? Language { get; set; } = "fa-IR";
    public string? AuthorName { get; set; }
    public string? PublisherName { get; set; }
    public string? BreadcrumbTitle { get; set; }
    public bool IncludeInSitemap { get; set; } = true;
    public int? SeoScore { get; set; }

    public string? OgTitle { get; set; }
    public string? OgDescription { get; set; }
    public string? OgUrl { get; set; }
    public string? OgType { get; set; }
    public string? OgSiteName { get; set; }
    public string? OgLocale { get; set; } = "fa_IR";
    public string? OgImageUrl { get; set; }
    public string? OgImageAlt { get; set; }
    public int? OgImageWidth { get; set; }
    public int? OgImageHeight { get; set; }

    public string? XCard { get; set; }
    public string? XTitle { get; set; }
    public string? XDescription { get; set; }
    public string? XImageUrl { get; set; }
    public string? XSite { get; set; }
    public string? XCreator { get; set; }

    public string? StructuredDataJsonLd { get; set; }
    public string? HreflangJson { get; set; }
    public string? PrevPageUrl { get; set; }
    public string? NextPageUrl { get; set; }

    /// <summary>تصویر پیش‌فرض اشتراک‌گذاری سایت (وقتی محتوا تصویری ندارد).</summary>
    public string? DefaultShareImageUrl { get; set; }

    public string EffectiveImageUrl => OgImageUrl ?? XImageUrl ?? DefaultShareImageUrl ?? string.Empty;

    public string EffectiveDescription => MetaDescription ?? OgDescription ?? string.Empty;

    public string EffectiveTitle => string.IsNullOrWhiteSpace(Title) ? "فروشگاه" : Title;

    /// <summary>آیا مقدار سئوی این صفحه توسط کارشناس بررسی شده است؟</summary>
    public bool HasCustomSeo { get; set; }

    public static SeoRenderModel FromEntity(SeoMetaData? seo) => new()
    {
        Title = seo?.Title ?? string.Empty,
        MetaDescription = seo?.MetaDescription,
        MetaKeywords = seo?.MetaKeywords,
        CanonicalUrl = seo?.CanonicalUrl,
        NoIndex = seo?.NoIndex ?? false,
        NoFollow = seo?.NoFollow ?? false,
        Language = seo?.Language ?? "fa-IR",
        AuthorName = seo?.AuthorName,
        PublisherName = seo?.PublisherName,
        BreadcrumbTitle = seo?.BreadcrumbTitle,
        IncludeInSitemap = seo?.IncludeInSitemap ?? true,
        SeoScore = seo?.SeoScore,
        OgTitle = seo?.OgTitle,
        OgDescription = seo?.OgDescription,
        OgUrl = seo?.OgUrl,
        OgType = seo?.OgType,
        OgSiteName = seo?.OgSiteName,
        OgLocale = seo?.OgLocale ?? "fa_IR",
        XCard = seo?.XCard,
        XTitle = seo?.XTitle,
        XDescription = seo?.XDescription,
        StructuredDataJsonLd = seo?.StructuredDataJsonLd,
        HreflangJson = seo?.HreflangJson,
        HasCustomSeo = seo is not null && (!string.IsNullOrWhiteSpace(seo.Title) || !string.IsNullOrWhiteSpace(seo.MetaDescription)),
    };
}

/// <summary>اطلاعات تکمیلی که سرویس سئو برای ساخت مقادیر پیش‌فرض لازم دارد.</summary>
public class SeoFallback
{
    public string SiteName { get; set; } = "فراتک";
    public string SiteUrl { get; set; } = string.Empty;
    public string? DefaultDescription { get; set; }
    public string? DefaultKeywords { get; set; }
    public string? DefaultShareImageName { get; set; }

    /// <summary>عنوان محتوا (نام محصول، تیتر مقاله، ...).</summary>
    public string? ContentTitle { get; set; }

    /// <summary>توضیح کوتاه محتوا برای ساخت meta description.</summary>
    public string? ShortDescription { get; set; }

    /// <summary>مسیر نسبی صفحه، مثل /product/headphone-x10</summary>
    public string Path { get; set; } = "/";

    public string? ImageName { get; set; }
    public string? ImageFolder { get; set; } = "products";
    public string? ImageAlt { get; set; }
    public SeoEntityType EntityType { get; set; }

    /// <summary>اسکیمای پیشنهادی وقتی محتوا داده ساختاریافته ندارد.</summary>
    public string? AutoSchemaJsonLd { get; set; }

    public string? PrevPageUrl { get; set; }
    public string? NextPageUrl { get; set; }

    /// <summary>وقتی true است، صفحه هرگز ایندکس نمی‌شود (سبد خرید، تسویه، پروفایل).</summary>
    public bool ForceNoIndex { get; set; }
}

public class SitemapEntry
{
    public string Url { get; set; } = string.Empty;
    public DateTimeOffset LastModified { get; set; }
    public decimal Priority { get; set; } = 0.5m;
    public string ChangeFrequency { get; set; } = "monthly";
    public string? ImageUrl { get; set; }
    public string? ImageTitle { get; set; }
}
