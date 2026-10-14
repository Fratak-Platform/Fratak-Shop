using Fratak_Shop.Core.DTOs.Pages.Command;
using Fratak_Shop.Core.DTOs.Pages.Query;
using Fratak_Shop.Core.Mappers.Pages;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Pages;
using Fratak_Shop.Data.Utilities.SeoManagement;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Core.Services.Pages;

/// <summary>
/// مدیریت سئوی صفحات مستقلی که محتوای Entity‌ای ندارند (صفحه اصلی، تماس با ما،
/// سوالات متداول و ...). هر صفحه فقط یک رکورد سئو دارد.
/// </summary>
public class PageSeoService(FratakShopDbContext context) : IPageSeoService
{
    public async Task<OperationResult> CreatePageSeoAsync(CreatePageSeoCommand command)
    {
        if (command.Page == SitePage.None)
            return OperationResult.Error("صفحه مورد نظر را انتخاب کنید.");

        var pageExists = await context.PageSeos.AnyAsync(x => x.Page == command.Page);
        if (pageExists)
            return OperationResult.Error("برای این صفحه قبلاً اطلاعات سئو ثبت شده است.");

        var model = new PageSeo
        {
            Page = command.Page,
            Seo = command.Seo,
        };

        context.PageSeos.Add(model);
        await context.SaveChangesAsync();

        return OperationResult.Success("اطلاعات سئوی صفحه با موفقیت ثبت شد.");
    }

    public async Task<OperationResult> EditPageSeoAsync(EditPageSeoCommand command)
    {
        var pageSeo = await context.PageSeos.AsTracking().FirstOrDefaultAsync(x => x.Id == command.Id);
        if (pageSeo is null)
            return OperationResult.NotFound("اطلاعات سئوی صفحه مورد نظر یافت نشد.");

        if (command.Page == SitePage.None)
            return OperationResult.Error("صفحه مورد نظر را انتخاب کنید.");

        var pageExists = await context.PageSeos
            .AnyAsync(x => x.Id != command.Id && x.Page == command.Page);

        if (pageExists)
            return OperationResult.Error("برای این صفحه قبلاً اطلاعات سئو ثبت شده است.");

        pageSeo.Page = command.Page;
        pageSeo.Seo = command.Seo;

        await context.SaveChangesAsync();

        return OperationResult.Success("اطلاعات سئوی صفحه با موفقیت ویرایش شد.");
    }

    public async Task<OperationResult> SavePageSeoAsync(SavePageSeoCommand command)
    {
        if (command.Page == SitePage.None)
            return OperationResult.Error("صفحه مورد نظر را انتخاب کنید.");

        if (command.Seo is null)
            return OperationResult.Error("اطلاعات سئو را وارد کنید.");

        var pageSeo = await context.PageSeos.AsTracking().FirstOrDefaultAsync(x => x.Page == command.Page);

        if (pageSeo is null)
        {
            context.PageSeos.Add(new PageSeo
            {
                Page = command.Page,
                Seo = command.Seo,
            });

            await context.SaveChangesAsync();

            return OperationResult.Success("اطلاعات سئوی صفحه با موفقیت ثبت شد.");
        }

        pageSeo.Seo = MergeSeo(pageSeo.Seo, command.Seo);

        await context.SaveChangesAsync();

        return OperationResult.Success("اطلاعات سئوی صفحه با موفقیت به‌روزرسانی شد.");
    }

    public async Task<OperationResult> DeletePageSeoAsync(Guid id)
    {
        var pageSeo = await context.PageSeos.AsTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (pageSeo is null)
            return OperationResult.NotFound("اطلاعات سئوی صفحه مورد نظر یافت نشد.");

        context.PageSeos.Remove(pageSeo);
        await context.SaveChangesAsync();

        return OperationResult.Success("اطلاعات سئوی صفحه با موفقیت حذف شد.");
    }

    public List<PageSeoDto> GetAllPageSeosAsync()
    {
        return context.PageSeos
            .OrderBy(x => x.Page)
            .ToList()
            .Map();
    }

    public async Task<PageSeoDto?> GetPageSeoByIdAsync(Guid id)
    {
        var pageSeo = await context.PageSeos.FirstOrDefaultAsync(x => x.Id == id);
        return pageSeo?.Map();
    }

    public async Task<PageSeoDto?> GetPageSeoByPageAsync(SitePage page)
    {
        if (page == SitePage.None)
            return null;

        var pageSeo = await context.PageSeos.FirstOrDefaultAsync(x => x.Page == page);
        return pageSeo?.Map();
    }

    /// <summary>
    /// فیلدهای ارسال‌شده را روی اطلاعات موجود می‌نشاند تا فرم‌هایی که فقط بخشی از
    /// اطلاعات سئو را پر می‌کنند، بقیه مقادیر را از دست ندهند.
    /// </summary>
    private static SeoMetaData MergeSeo(SeoMetaData? current, SeoMetaData filled)
    {
        if (current is null)
            return filled;

        current.Title = filled.Title;
        current.MetaDescription = filled.MetaDescription;
        current.CanonicalUrl = filled.CanonicalUrl;
        current.MetaKeywords = filled.MetaKeywords;
        current.FocusKeyword = filled.FocusKeyword;
        current.BreadcrumbTitle = filled.BreadcrumbTitle;
        current.AuthorName = filled.AuthorName;
        current.PublisherName = filled.PublisherName;
        current.Language = filled.Language;
        current.Topic = filled.Topic;

        current.NoIndex = filled.NoIndex;
        current.NoFollow = filled.NoFollow;
        current.NoSnippet = filled.NoSnippet;
        current.NoImageIndex = filled.NoImageIndex;
        current.NoTranslate = filled.NoTranslate;
        current.IndexIfEmbedded = filled.IndexIfEmbedded;
        current.MaxSnippet = filled.MaxSnippet;
        current.MaxImagePreview = filled.MaxImagePreview;
        current.MaxVideoPreview = filled.MaxVideoPreview;

        current.OgTitle = filled.OgTitle;
        current.OgDescription = filled.OgDescription;
        current.OgUrl = filled.OgUrl;
        current.OgType = filled.OgType;
        current.OgSiteName = filled.OgSiteName;
        current.OgLocale = filled.OgLocale;
        current.OgLocaleAlternatesJson = filled.OgLocaleAlternatesJson;
        current.OgImageName = filled.OgImageName;
        current.OgImageType = filled.OgImageType;
        current.OgImageWidth = filled.OgImageWidth;
        current.OgImageHeight = filled.OgImageHeight;
        current.OgImageAlt = filled.OgImageAlt;
        current.OgVideoUrl = filled.OgVideoUrl;
        current.OgAudioUrl = filled.OgAudioUrl;

        current.XCard = filled.XCard;
        current.XTitle = filled.XTitle;
        current.XDescription = filled.XDescription;
        current.XUrl = filled.XUrl;
        current.XImageName = filled.XImageName;
        current.XImageAlt = filled.XImageAlt;
        current.XSite = filled.XSite;
        current.XCreator = filled.XCreator;

        current.ArticleSection = filled.ArticleSection;
        current.ArticlePublishedTime = filled.ArticlePublishedTime;
        current.ArticleModifiedTime = filled.ArticleModifiedTime;
        current.ArticleExpirationTime = filled.ArticleExpirationTime;
        current.ArticleAuthorUrl = filled.ArticleAuthorUrl;
        current.ArticleTagsJson = filled.ArticleTagsJson;

        current.SchemaType = filled.SchemaType;
        current.StructuredDataJsonLd = filled.StructuredDataJsonLd;

        current.IncludeInSitemap = filled.IncludeInSitemap;
        current.SitemapLastModified = filled.SitemapLastModified;
        current.SitemapPriority = filled.SitemapPriority;
        current.SitemapChangeFrequency = filled.SitemapChangeFrequency;

        current.SocialImageName = filled.SocialImageName;
        current.SocialImageAlt = filled.SocialImageAlt;

        current.Notes = filled.Notes;
        current.IsReviewed = filled.IsReviewed;
        current.ReviewedAt = filled.ReviewedAt;
        current.ReviewedByUserId = filled.ReviewedByUserId;

        return current;
    }
}
