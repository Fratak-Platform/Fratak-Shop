using Fratak_Shop.Core.DTOs.Comments.Query;
using Fratak_Shop.Core.Utilities;

namespace Fratak_Shop.Core.DTOs.Blogs.Posts.Query;

public class PostDto : BaseDto
{
    public Guid? AuthorId { get; set; }
    public string AuthorName { get; set; } = null!;
    public Guid BlogCategoryId { get; set; }
    public string? BlogCategoryName { get; set; }
    public string? BlogCategorySlug { get; set; }
    public string Title { get; set; } = null!;
    public string Slug { get; set; } = null!;
    public string? Excerpt { get; set; }
    public string Content { get; set; } = null!;
    public string? ImageName { get; set; }
    public string? ImageAlt { get; set; }
    public int ViewCount { get; set; }
    public bool IsPublished { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public SeoMetaDataDto? Seo { get; set; }
    public List<CommentDto> Comments { get; set; } = [];
}

public class SeoMetaDataDto
{
    public string? Title { get; set; }
    public string? MetaDescription { get; set; }
    public string? CanonicalUrl { get; set; }
    public string? MetaKeywords { get; set; }
    public string? FocusKeyword { get; set; }
    public string? BreadcrumbTitle { get; set; }
    public string? AuthorName { get; set; }
    public string? PublisherName { get; set; }
    public string? Language { get; set; }
    public string? Topic { get; set; }
    public bool? NoIndex { get; set; }
    public bool? NoFollow { get; set; }
    public bool? NoSnippet { get; set; }
    public bool? NoImageIndex { get; set; }
    public bool? NoTranslate { get; set; }
    public bool? IndexIfEmbedded { get; set; }
    public int? MaxSnippet { get; set; }
    public string? MaxImagePreview { get; set; }
    public int? MaxVideoPreview { get; set; }
    public string? OgTitle { get; set; }
    public string? OgDescription { get; set; }
    public string? OgUrl { get; set; }
    public string? OgType { get; set; }
    public string? OgSiteName { get; set; }
    public string? OgLocale { get; set; }
    public string? OgLocaleAlternatesJson { get; set; }
    public string? OgImageName { get; set; }
    public string? OgImageType { get; set; }
    public int? OgImageWidth { get; set; }
    public int? OgImageHeight { get; set; }
    public string? OgImageAlt { get; set; }
    public string? OgVideoUrl { get; set; }
    public string? OgAudioUrl { get; set; }
    public string? XCard { get; set; }
    public string? XTitle { get; set; }
    public string? XDescription { get; set; }
    public string? XUrl { get; set; }
    public string? XImageName { get; set; }
    public string? XImageAlt { get; set; }
    public string? XSite { get; set; }
    public string? XCreator { get; set; }
    public string? ArticleSection { get; set; }
    public DateTimeOffset? ArticlePublishedTime { get; set; }
    public DateTimeOffset? ArticleModifiedTime { get; set; }
    public DateTimeOffset? ArticleExpirationTime { get; set; }
    public string? ArticleAuthorUrl { get; set; }
    public string? ArticleTagsJson { get; set; }
    public string? SchemaType { get; set; }
    public string? StructuredDataJsonLd { get; set; }
    public bool? IncludeInSitemap { get; set; }
    public DateTimeOffset? SitemapLastModified { get; set; }
    public decimal? SitemapPriority { get; set; }
    public string? SitemapChangeFrequency { get; set; }
    public string? SocialImageName { get; set; }
    public string? SocialImageAlt { get; set; }
    public string? Notes { get; set; }
    public bool? IsReviewed { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
    public Guid? ReviewedByUserId { get; set; }
}