using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Core.Services.Seo.Engine;
using Fratak_Shop.Core.Services.Seo.Tools;
using Fratak_Shop.Core.Services.Settings;
using Fratak_Shop.Web.Helpers;
using Fratak_Shop.Web.Infrastructure.SeoEngine;
using Microsoft.EntityFrameworkCore;
using Fratak_Shop.Data.Entities.SeoEngine;
using Fratak_Shop.Web.Infrastructure.Admin;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Areas.Admin.Pages.Seo;

/// <summary>
/// تحلیل زندهٔ سئوی یک صفحه (مرحلهٔ هفتم – فاز ۸). آدرس: /admin/seo/analyze?kind=Product&amp;id=…
/// <para>نمره و همهٔ بررسی‌ها (گروه‌بندی‌شده با توضیح و راه اصلاح)، پیش‌نمایش گوگل، کلمه‌های کلیدی نامزد
/// و تاریخچهٔ پیشنهادهای این صفحه. دکمهٔ «ساخت پیشنهاد» همین الان پیشنهاد تازه می‌سازد و به صفحهٔ بررسی می‌برد.</para>
/// </summary>
[AdminPermission(AdminPermissions.SeoManage)]
public class AnalyzeModel(ISeoEngineService engine, ISearchConsoleService gsc, IAdminDataService data, IStoreSettingService settings) : AdminPageModel
{
    [BindProperty(SupportsGet = true)] public SeoEntityKind Kind { get; set; }
    [BindProperty(SupportsGet = true)] public Guid Id { get; set; }

    public SeoAnalysisView Analysis { get; private set; } = null!;

    /// <summary>پیش‌نمایش اشتراک در شبکه‌های اجتماعی و پیام‌رسان‌ها (فاز ۸ب).</summary>
    public SharePreviewModel Share { get; private set; } = null!;

    /// <summary>عبارت‌های واقعی گوگل این صفحه از Search Console (اگر وصل باشد).</summary>
    public IReadOnlyList<SearchConsoleQueryStat> GoogleQueries { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync()
    {
        SeoEngineHost.Current = Request.Host.Host;
        var view = await engine.AnalyzeAsync(Kind, Id, HttpContext.RequestAborted);
        if (view is null)
        {
            ErrorMessage = "صفحه پیدا نشد (حذف، غیرفعال یا منتشرنشده است).";
            return LocalRedirect("/admin/seo/engine?tab=pages");
        }
        Analysis = view;
        Share = await BuildShareAsync(view.Subject, view.SiteName);
        GoogleQueries = await gsc.PageQueriesAsync(view.Subject.Path, 15, HttpContext.RequestAborted);
        return Page();
    }

    /// <summary>ساخت فوری پیشنهاد (پیشنهاد منتظر قبلی همین صفحه جایگزین می‌شود).</summary>
    public async Task<IActionResult> OnPostGenerateAsync()
    {
        var result = await engine.GenerateAsync(Kind, Id, HttpContext.RequestAborted);
        ShowResult(result);
        if (Succeeded(result))
            return LocalRedirect($"/admin/seo/review?focus={result.Data}");
        return LocalRedirect($"/admin/seo/analyze?kind={Kind}&id={Id}");
    }
    /// <summary>
    /// عنوان، توضیح و تصویر اشتراک (og) همان‌طور که سایت می‌سازد: og:title/og:description دستی یا عنوان و توضیح سئو،
    /// تصویر اصلی صفحه یا تصویر پیش‌فرض تنظیمات. منطق سمت سایت: Web/Services/PageMetaService.Apply.
    /// </summary>
    private async Task<SharePreviewModel> BuildShareAsync(SeoSubject subject, string siteName)
    {
        var ct = HttpContext.RequestAborted;
        string? ogTitle = null, ogDescription = null, image = null;
        switch (subject.Kind)
        {
            case SeoEntityKind.Product:
            {
                var row = await data.Query<global::Fratak_Shop.Data.Entities.Products.Product>().Where(p => p.Id == subject.EntityId)
                    .Select(p => new { p.Seo!.OgTitle, p.Seo!.OgDescription, Image = p.Images.OrderByDescending(i => i.IsPrimary).ThenBy(i => i.DisplayOrder).Select(i => i.ImageName).FirstOrDefault() })
                    .FirstOrDefaultAsync(ct);
                (ogTitle, ogDescription, image) = (row?.OgTitle, row?.OgDescription, row?.Image is { } name ? ImageHelper.Product(name) : null);
                break;
            }
            case SeoEntityKind.Category:
            {
                var row = await data.Query<global::Fratak_Shop.Data.Entities.Categories.Category>().Where(c => c.Id == subject.EntityId)
                    .Select(c => new { c.Seo!.OgTitle, c.Seo!.OgDescription, c.ImageName }).FirstOrDefaultAsync(ct);
                (ogTitle, ogDescription, image) = (row?.OgTitle, row?.OgDescription, row?.ImageName is { } name ? ImageHelper.Category(name) : null);
                break;
            }
            case SeoEntityKind.Brand:
            {
                var row = await data.Query<global::Fratak_Shop.Data.Entities.Brands.Brand>().Where(b => b.Id == subject.EntityId)
                    .Select(b => new { b.Seo!.OgTitle, b.Seo!.OgDescription, b.LogoName }).FirstOrDefaultAsync(ct);
                (ogTitle, ogDescription, image) = (row?.OgTitle, row?.OgDescription, row?.LogoName is { } name ? ImageHelper.Brand(name) : null);
                break;
            }
            case SeoEntityKind.BlogPost:
            {
                var row = await data.Query<global::Fratak_Shop.Data.Entities.Blogs.BlogPost>().Where(p => p.Id == subject.EntityId)
                    .Select(p => new { p.Seo!.OgTitle, p.Seo!.OgDescription, p.ImageName }).FirstOrDefaultAsync(ct);
                (ogTitle, ogDescription, image) = (row?.OgTitle, row?.OgDescription, row?.ImageName is { } name ? ImageHelper.Blog(name) : null);
                break;
            }
            case SeoEntityKind.ContentPage:
            {
                var row = await data.Query<global::Fratak_Shop.Data.Entities.Contents.ContentPage>().Where(p => p.Id == subject.EntityId)
                    .Select(p => new { p.Seo!.OgTitle, p.Seo!.OgDescription, p.ImageUrl }).FirstOrDefaultAsync(ct);
                (ogTitle, ogDescription, image) = (row?.OgTitle, row?.OgDescription, row?.ImageUrl);
                break;
            }
        }

        var store = await settings.GetAsync(ct);
        var title = string.IsNullOrWhiteSpace(ogTitle) ? SeoEngineUi.FullTitle(subject.EffectiveTitle, siteName) : ogTitle;
        var description = string.IsNullOrWhiteSpace(ogDescription) ? subject.EffectiveDescription : ogDescription;
        return new SharePreviewModel(title, description, image ?? store.DefaultOgImageUrl ?? Fratak_Shop.Web.Infrastructure.BrandIdentity.OgImage, Request.Host.Host, subject.Path);
    }
}
