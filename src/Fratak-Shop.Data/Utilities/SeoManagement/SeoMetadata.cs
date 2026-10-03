using System.ComponentModel.DataAnnotations;

namespace Fratak_Shop.Data.Utilities.SeoManagement
{
    /// <summary>
    /// Central SEO metadata shared by every SEO-managed entity (products, categories,
    /// blog posts, blog categories and site pages). Every field is optional so an admin
    /// can create content without touching SEO and an SEO specialist can fill the
    /// details later.
    /// </summary>
    public class SeoMetaData
    {
        // =========================================================
        // General SEO
        // =========================================================

        /// <summary>
        /// SEO title shown in the HTML <title> element.
        /// </summary>
        [MaxLength(200)]
        public string? Title { get; set; }

        /// <summary>
        /// Meta description of the page.
        /// </summary>
        [MaxLength(500)]
        public string? MetaDescription { get; set; }

        /// <summary>
        /// Preferred canonical URL of the page.
        /// </summary>
        [MaxLength(2048)]
        public string? CanonicalUrl { get; set; }

        /// <summary>
        /// Meta keywords for the page. Kept as a management tool for the SEO
        /// specialist even though modern search engines ignore the keywords tag.
        /// </summary>
        [MaxLength(500)]
        public string? MetaKeywords { get; set; }

        /// <summary>
        /// Main keyword or key phrase used internally by the SEO manager.
        /// This is not rendered as a meta keywords tag.
        /// </summary>
        [MaxLength(200)]
        public string? FocusKeyword { get; set; }

        /// <summary>
        /// Custom breadcrumb title override for the page.
        /// </summary>
        [MaxLength(200)]
        public string? BreadcrumbTitle { get; set; }

        /// <summary>
        /// Author or content creator name.
        /// </summary>
        [MaxLength(200)]
        public string? AuthorName { get; set; }

        /// <summary>
        /// Publisher / organization name.
        /// </summary>
        [MaxLength(200)]
        public string? PublisherName { get; set; }

        /// <summary>
        /// Preferred language of the page, e.g. fa-IR.
        /// </summary>
        [MaxLength(20)]
        public string? Language { get; set; }

        /// <summary>
        /// Main topic/category of the page for internal SEO management.
        /// </summary>
        [MaxLength(200)]
        public string? Topic { get; set; }


        // =========================================================
        // Robots / Indexing
        // =========================================================

        /// <summary>
        /// Prevents the page from being indexed.
        /// </summary>
        public bool? NoIndex { get; set; }

        /// <summary>
        /// Prevents crawlers from following links on the page.
        /// </summary>
        public bool? NoFollow { get; set; }

        /// <summary>
        /// Prevents text snippets from being shown.
        /// </summary>
        public bool? NoSnippet { get; set; }

        /// <summary>
        /// Prevents images on this page from being indexed.
        /// </summary>
        public bool? NoImageIndex { get; set; }

        /// <summary>
        /// Prevents Google from offering translation for the page.
        /// </summary>
        public bool? NoTranslate { get; set; }

        /// <summary>
        /// Allows indexing of embedded content when combined with noindex.
        /// </summary>
        public bool? IndexIfEmbedded { get; set; }

        /// <summary>
        /// Maximum text snippet length.
        /// -1 means no limit when rendered.
        /// </summary>
        public int? MaxSnippet { get; set; }

        /// <summary>
        /// Maximum image preview setting.
        /// none / standard / large
        /// </summary>
        [MaxLength(20)]
        public string? MaxImagePreview { get; set; }

        /// <summary>
        /// Maximum video preview duration in seconds.
        /// -1 means no limit.
        /// </summary>
        public int? MaxVideoPreview { get; set; }


        // =========================================================
        // Open Graph
        // =========================================================

        [MaxLength(200)]
        public string? OgTitle { get; set; }

        [MaxLength(500)]
        public string? OgDescription { get; set; }

        [MaxLength(2048)]
        public string? OgUrl { get; set; }

        [MaxLength(100)]
        public string? OgType { get; set; }

        [MaxLength(200)]
        public string? OgSiteName { get; set; }

        [MaxLength(20)]
        public string? OgLocale { get; set; }

        /// <summary>
        /// JSON array/string containing alternate Open Graph locales.
        /// </summary>
        public string? OgLocaleAlternatesJson { get; set; }

        /// <summary>
        /// File name of the Open Graph image stored on the server.
        /// </summary>
        [MaxLength(250)]
        public string? OgImageName { get; set; }

        [MaxLength(100)]
        public string? OgImageType { get; set; }

        public int? OgImageWidth { get; set; }

        public int? OgImageHeight { get; set; }

        [MaxLength(500)]
        public string? OgImageAlt { get; set; }

        [MaxLength(2048)]
        public string? OgVideoUrl { get; set; }

        [MaxLength(2048)]
        public string? OgAudioUrl { get; set; }


        // =========================================================
        // X / Twitter Card
        // =========================================================

        /// <summary>
        /// Example: summary or summary_large_image.
        /// </summary>
        [MaxLength(50)]
        public string? XCard { get; set; }

        [MaxLength(200)]
        public string? XTitle { get; set; }

        [MaxLength(500)]
        public string? XDescription { get; set; }

        [MaxLength(2048)]
        public string? XUrl { get; set; }

        /// <summary>
        /// File name of the X / Twitter card image stored on the server.
        /// </summary>
        [MaxLength(250)]
        public string? XImageName { get; set; }

        [MaxLength(500)]
        public string? XImageAlt { get; set; }

        [MaxLength(100)]
        public string? XSite { get; set; }

        [MaxLength(100)]
        public string? XCreator { get; set; }


        // =========================================================
        // Article / Blog SEO
        // =========================================================

        [MaxLength(200)]
        public string? ArticleSection { get; set; }

        public DateTimeOffset? ArticlePublishedTime { get; set; }

        public DateTimeOffset? ArticleModifiedTime { get; set; }

        public DateTimeOffset? ArticleExpirationTime { get; set; }

        /// <summary>
        /// Main article author URL/profile.
        /// </summary>
        [MaxLength(2048)]
        public string? ArticleAuthorUrl { get; set; }

        /// <summary>
        /// JSON array of article tags.
        /// </summary>
        public string? ArticleTagsJson { get; set; }


        // =========================================================
        // Structured Data / Schema.org
        // =========================================================

        /// <summary>
        /// Schema.org type such as:
        /// Product, Article, BlogPosting, Organization,
        /// BreadcrumbList, FAQPage, WebPage, etc.
        /// </summary>
        [MaxLength(100)]
        public string? SchemaType { get; set; }

        /// <summary>
        /// Complete JSON-LD markup.
        /// </summary>
        public string? StructuredDataJsonLd { get; set; }


        // =========================================================
        // Sitemap
        // =========================================================

        /// <summary>
        /// Controls whether the page should be included in the sitemap.
        /// </summary>
        public bool? IncludeInSitemap { get; set; }

        /// <summary>
        /// Last modification date used when generating sitemap data.
        /// </summary>
        public DateTimeOffset? SitemapLastModified { get; set; }

        /// <summary>
        /// Optional sitemap priority used by the application's sitemap generator.
        /// </summary>
        public decimal? SitemapPriority { get; set; }

        /// <summary>
        /// Optional sitemap change frequency used by the application's sitemap generator.
        /// </summary>
        [MaxLength(20)]
        public string? SitemapChangeFrequency { get; set; }


        // =========================================================
        // Social / Sharing
        // =========================================================

        /// <summary>
        /// File name of the default image used when sharing the page.
        /// </summary>
        [MaxLength(250)]
        public string? SocialImageName { get; set; }

        [MaxLength(500)]
        public string? SocialImageAlt { get; set; }


        // =========================================================
        // SEO Management
        // =========================================================

        /// <summary>
        /// JSON array of secondary/supporting keywords the content also targets.
        /// </summary>
        public string? SecondaryKeywordsJson { get; set; }

        /// <summary>
        /// JSON array of hreflang entries ({"lang":"en-US","url":"https://..."})
        /// for multilingual pages.
        /// </summary>
        public string? HreflangJson { get; set; }

        /// <summary>
        /// Cached SEO score (0..100) of the last audit so list screens can sort
        /// content by SEO quality without reading the audit payload.
        /// </summary>
        public int? SeoScore { get; set; }

        /// <summary>
        /// Moment the SEO record was audited last.
        /// </summary>
        public DateTimeOffset? LastAuditedAt { get; set; }

        /// <summary>
        /// Internal SEO notes. Not rendered on the website.
        /// </summary>
        public string? Notes { get; set; }

        /// <summary>
        /// Indicates whether the page has been reviewed by an SEO specialist.
        /// </summary>
        public bool? IsReviewed { get; set; }

        public DateTimeOffset? ReviewedAt { get; set; }

        /// <summary>
        /// User ID of the person who reviewed the SEO metadata.
        /// </summary>
        public Guid? ReviewedByUserId { get; set; }
    }
}
