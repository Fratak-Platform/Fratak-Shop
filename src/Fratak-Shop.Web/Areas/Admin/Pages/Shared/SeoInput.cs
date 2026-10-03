using Fratak_Shop.Core.DTOs.Blogs.Posts.Query;
using Fratak_Shop.Data.Utilities.SeoManagement;

namespace Fratak_Shop.Web.Areas.Admin.Pages;

/// <summary>
/// فرم مشترک تنظیمات سئو در پنل مدیریت؛ بین محصول، مقاله و برگه‌های سایت یکسان است.
/// مقادیر پیشنهادی «سئوی هوشمند» فقط در همین فرم قرار می‌گیرند و تا ذخیره‌نشدن اثری ندارند.
/// </summary>
public class SeoInput
{
    [System.ComponentModel.DataAnnotations.StringLength(70, ErrorMessage = "عنوان سئو بهتر است حداکثر ۷۰ کاراکتر باشد.")]
    public string? Title { get; set; }

    [System.ComponentModel.DataAnnotations.StringLength(170, ErrorMessage = "توضیحات متا بهتر است حداکثر ۱۷۰ کاراکتر باشد.")]
    public string? MetaDescription { get; set; }

    public string? MetaKeywords { get; set; }
    public string? FocusKeyword { get; set; }
    public string? CanonicalUrl { get; set; }
    public string? BreadcrumbTitle { get; set; }
    public string? OgTitle { get; set; }
    public string? OgDescription { get; set; }
    public string? OgType { get; set; }
    public string? OgImageAlt { get; set; }
    public string? XCard { get; set; }
    public string? XTitle { get; set; }
    public string? XDescription { get; set; }
    public string? SchemaType { get; set; }
    public string? StructuredDataJsonLd { get; set; }
    public string? ImageAlt { get; set; }
    public string? SitemapChangeFrequency { get; set; }
    public decimal? SitemapPriority { get; set; }
    public bool IncludeInSitemap { get; set; } = true;
    public bool NoIndex { get; set; }
    public bool NoFollow { get; set; }
    public string? Notes { get; set; }

    public SeoMetaData ToEntity() => new()
    {
        Title = Title,
        MetaDescription = MetaDescription,
        MetaKeywords = MetaKeywords,
        FocusKeyword = FocusKeyword,
        CanonicalUrl = CanonicalUrl,
        BreadcrumbTitle = BreadcrumbTitle,
        OgTitle = OgTitle,
        OgDescription = OgDescription,
        OgType = OgType,
        OgImageAlt = OgImageAlt,
        XCard = XCard,
        XTitle = XTitle,
        XDescription = XDescription,
        SchemaType = SchemaType,
        StructuredDataJsonLd = StructuredDataJsonLd,
        SitemapChangeFrequency = SitemapChangeFrequency,
        SitemapPriority = SitemapPriority,
        IncludeInSitemap = IncludeInSitemap,
        NoIndex = NoIndex,
        NoFollow = NoFollow,
        Notes = Notes,
    };

    public static SeoInput FromEntity(SeoMetaData? seo) => new()
    {
        Title = seo?.Title,
        MetaDescription = seo?.MetaDescription,
        MetaKeywords = seo?.MetaKeywords,
        FocusKeyword = seo?.FocusKeyword,
        CanonicalUrl = seo?.CanonicalUrl,
        BreadcrumbTitle = seo?.BreadcrumbTitle,
        OgTitle = seo?.OgTitle,
        OgDescription = seo?.OgDescription,
        OgType = seo?.OgType,
        OgImageAlt = seo?.OgImageAlt,
        XCard = seo?.XCard,
        XTitle = seo?.XTitle,
        XDescription = seo?.XDescription,
        SchemaType = seo?.SchemaType,
        StructuredDataJsonLd = seo?.StructuredDataJsonLd,
        SitemapChangeFrequency = seo?.SitemapChangeFrequency,
        SitemapPriority = seo?.SitemapPriority,
        IncludeInSitemap = seo?.IncludeInSitemap ?? true,
        NoIndex = seo?.NoIndex ?? false,
        NoFollow = seo?.NoFollow ?? false,
        Notes = seo?.Notes,
    };

    public static SeoInput FromDto(SeoMetaDataDto? seo) => new()
    {
        Title = seo?.Title,
        MetaDescription = seo?.MetaDescription,
        MetaKeywords = seo?.MetaKeywords,
        FocusKeyword = seo?.FocusKeyword,
        CanonicalUrl = seo?.CanonicalUrl,
        BreadcrumbTitle = seo?.BreadcrumbTitle,
        OgTitle = seo?.OgTitle,
        OgDescription = seo?.OgDescription,
        OgType = seo?.OgType,
        OgImageAlt = seo?.OgImageAlt,
        XCard = seo?.XCard,
        XTitle = seo?.XTitle,
        XDescription = seo?.XDescription,
        SchemaType = seo?.SchemaType,
        StructuredDataJsonLd = seo?.StructuredDataJsonLd,
        SitemapChangeFrequency = seo?.SitemapChangeFrequency,
        SitemapPriority = seo?.SitemapPriority,
        IncludeInSitemap = seo?.IncludeInSitemap ?? true,
        NoIndex = seo?.NoIndex ?? false,
        NoFollow = seo?.NoFollow ?? false,
        Notes = seo?.Notes,
    };
}
