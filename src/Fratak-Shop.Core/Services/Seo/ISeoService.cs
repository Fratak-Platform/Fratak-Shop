using Fratak_Shop.Core.DTOs.Seo.Query;
using Fratak_Shop.Core.Utilities;

namespace Fratak_Shop.Core.Services.Seo;

/// <summary>
/// Technical SEO of the shop: building the meta/structured-data of every page,
/// generating sitemap.xml and robots.txt, resolving redirects and tracking 404s.
/// The storefront pages only hand the entity SEO record plus a fallback to this
/// service and get back a fully populated model — which is why even content
/// without SEO data renders a complete, valid set of tags.
/// </summary>
public interface ISeoService
{
    /// <summary>ساخت مدل کامل سئو برای یک صفحه.</summary>
    SeoRenderModel Build(Data.Utilities.SeoManagement.SeoMetaData? seo, SeoFallback fallback);

    /// <summary>نقشه سایت XML (شامل محصولات، دسته‌بندی‌ها، مقالات و صفحات محتوایی).</summary>
    Task<string> GetSitemapXmlAsync(CancellationToken cancellationToken = default);

    /// <summary>فایل robots.txt با آدرس نقشه سایت و تنظیمات سفارشی مدیر سئو.</summary>
    Task<string> GetRobotsTxtAsync(CancellationToken cancellationToken = default);

    /// <summary>یافتن ریدایرکت فعال برای یک مسیر (درخواست ناموجود).</summary>
    Task<Data.Entities.SeoRedirects.SeoRedirect?> ResolveRedirectAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>ثبت بازدید یک ریدایرکت برای گزارش پنل سئو.</summary>
    Task RegisterRedirectHitAsync(Guid redirectId, CancellationToken cancellationToken = default);

    /// <summary>ثبت خطای ۴۰۴ (تجمیعی) برای گزارش «خطاهای ۴۰۴».</summary>
    Task LogNotFoundAsync(string path, string? referrer, string? userAgent, CancellationToken cancellationToken = default);

    // ---------------------------------------------------------------- admin
    Task<List<Data.Entities.SeoRedirects.SeoRedirect>> GetRedirectsAsync(string? search = null);

    Task<Data.Entities.SeoRedirects.SeoRedirect?> GetRedirectAsync(Guid id);

    Task<OperationResult> SaveRedirectAsync(Guid? id, string fromPath, string toUrl, Data.Entities.SeoRedirects.RedirectType type, string? notes, bool isActive);

    Task<OperationResult> DeleteRedirectAsync(Guid id);

    Task<List<Data.Entities.Seo.NotFoundLog>> GetNotFoundLogsAsync(bool onlyUnresolved = true, int take = 50);

    /// <summary>علامت‌گذاری یک خطای ۴۰۴ به‌عنوان حل‌شده (پس از ساخت ریدایرکت).</summary>
    Task<OperationResult> ResolveNotFoundAsync(string path, Guid redirectId, CancellationToken cancellationToken = default);
}
