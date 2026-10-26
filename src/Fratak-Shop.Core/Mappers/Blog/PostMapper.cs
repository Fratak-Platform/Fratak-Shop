using Fratak_Shop.Core.DTOs.Blogs.Posts.Query;
using Fratak_Shop.Core.DTOs.Comments.Query;
using Fratak_Shop.Core.Mappers.Users;
using Fratak_Shop.Data.Entities.Blogs;
using Fratak_Shop.Data.Utilities.SeoManagement;

namespace Fratak_Shop.Core.Mappers.Blog;

public static class PostMapper
{
    public static PostDto Map(this BlogPost post)
    {
        return new PostDto
        {
            Id = post.Id,
            CreatedAt = post.CreatedAt,
            UpdatedAt = post.UpdatedAt,
            IsDeleted = post.IsDeleted,
            DeletedAt = post.DeletedAt,
            AuthorId = post.AuthorId,
            AuthorName = post.Author.DisplayName() ?? string.Empty,
            BlogCategoryId = post.BlogCategoryId,
            BlogCategoryName = post.BlogCategory?.Name,
            BlogCategorySlug = post.BlogCategory?.Slug,
            Title = post.Title,
            Slug = post.Slug,
            Excerpt = post.Excerpt,
            Content = post.Content,
            ImageName = post.ImageName,
            ImageAlt = post.ImageAlt,
            ViewCount = post.ViewCount,
            IsPublished = post.IsPublished,
            PublishedAt = post.PublishedAt,
            Seo = post.Seo.MapSeoMetaData(),
            Comments = new List<CommentDto>()
        };
    }

    public static List<BlogPostFilterData> MapFilterData(this List<BlogPost> posts)
    {
        var postsDto = new List<BlogPostFilterData>();
        posts.ForEach(p =>
        {
            postsDto.Add(new BlogPostFilterData()
            {
                Id = p.Id,
                CreatedAt = p.CreatedAt,
                UpdatedAt = p.UpdatedAt,
                IsDeleted = p.IsDeleted,
                DeletedAt = p.DeletedAt,
                AuthorId = p.AuthorId,
                AuthorName = p.Author.DisplayName() ?? string.Empty,
                Title = p.Title,
                Slug = p.Slug,
                Excerpt = p.Excerpt,
                Content = p.Content,
                ImageName = p.ImageName,
                ImageAlt = p.ImageAlt,
                ViewCount = p.ViewCount,
                IsPublished = p.IsPublished,
                PublishedAt = p.PublishedAt,
                CategoryPostDto = new BlogCategoryPostDto
                {
                    Slug = p.BlogCategory?.Slug ?? string.Empty,
                    Id = p.BlogCategory?.Id ?? Guid.Empty,
                    Name = p.BlogCategory?.Name ?? string.Empty,
                }
            });
        });
        return postsDto;
    }


    public static List<PostDto> Map(this List<BlogPost> posts)
    {
        var postsDto = new List<PostDto>();
        posts.ForEach(p =>
        {
            postsDto.Add(p.Map());
        });
        return postsDto;
    }

    public static SeoMetaDataDto? MapSeoMetaData(this SeoMetaData? metaData)
    {
        if (metaData == null)
            return null;

        return new SeoMetaDataDto
        {
            Title = metaData.Title,
            MetaDescription = metaData.MetaDescription,
            CanonicalUrl = metaData.CanonicalUrl,
            MetaKeywords = metaData.MetaKeywords,
            FocusKeyword = metaData.FocusKeyword,
            BreadcrumbTitle = metaData.BreadcrumbTitle,
            AuthorName = metaData.AuthorName,
            PublisherName = metaData.PublisherName,
            Language = metaData.Language,
            Topic = metaData.Topic,
            NoIndex = metaData.NoIndex,
            NoFollow = metaData.NoFollow,
            NoSnippet = metaData.NoSnippet,
            NoImageIndex = metaData.NoImageIndex,
            NoTranslate = metaData.NoTranslate,
            IndexIfEmbedded = metaData.IndexIfEmbedded,
            MaxSnippet = metaData.MaxSnippet,
            MaxImagePreview = metaData.MaxImagePreview,
            MaxVideoPreview = metaData.MaxVideoPreview,

            OgTitle = metaData.OgTitle,
            OgDescription = metaData.OgDescription,
            OgUrl = metaData.OgUrl,
            OgType = metaData.OgType,
            OgSiteName = metaData.OgSiteName,
            OgLocale = metaData.OgLocale,
            OgLocaleAlternatesJson = metaData.OgLocaleAlternatesJson,
            OgImageName = metaData.OgImageName,
            OgImageType = metaData.OgImageType,
            OgImageWidth = metaData.OgImageWidth,
            OgImageHeight = metaData.OgImageHeight,
            OgImageAlt = metaData.OgImageAlt,
            OgVideoUrl = metaData.OgVideoUrl,
            OgAudioUrl = metaData.OgAudioUrl,

            XCard = metaData.XCard,
            XTitle = metaData.XTitle,
            XDescription = metaData.XDescription,
            XUrl = metaData.XUrl,
            XImageName = metaData.XImageName,
            XImageAlt = metaData.XImageAlt,
            XSite = metaData.XSite,
            XCreator = metaData.XCreator,

            ArticleSection = metaData.ArticleSection,
            ArticlePublishedTime = metaData.ArticlePublishedTime,
            ArticleModifiedTime = metaData.ArticleModifiedTime,
            ArticleExpirationTime = metaData.ArticleExpirationTime,
            ArticleAuthorUrl = metaData.ArticleAuthorUrl,
            ArticleTagsJson = metaData.ArticleTagsJson,

            SchemaType = metaData.SchemaType,
            StructuredDataJsonLd = metaData.StructuredDataJsonLd,
            FaqJson = metaData.FaqJson,

            IncludeInSitemap = metaData.IncludeInSitemap,
            SitemapLastModified = metaData.SitemapLastModified,
            SitemapPriority = metaData.SitemapPriority,
            SitemapChangeFrequency = metaData.SitemapChangeFrequency,

            SocialImageName = metaData.SocialImageName,
            SocialImageAlt = metaData.SocialImageAlt,

            Notes = metaData.Notes,
            IsReviewed = metaData.IsReviewed,
            ReviewedAt = metaData.ReviewedAt,
            ReviewedByUserId = metaData.ReviewedByUserId
        };
    }
}
