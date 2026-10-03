using Fratak_Shop.Core.DTOs.Pages.Query;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Entities.Pages;
using Fratak_Shop.Data.Utilities.SeoManagement;

namespace Fratak_Shop.Core.Services.Pages;

/// <summary>
/// SEO of the pages that have no content entity of their own (home page, product list,
/// search results, cart, ...). The record is stored per <see cref="SitePage"/>, so the
/// storefront can always ask «سئوی این صفحه چیست؟» with the enum value.
/// </summary>
public interface IPageSeoService
{
    Task<List<PageSeoDto>> GetAllAsync();

    Task<PageSeoDto?> GetAsync(SitePage page);

    Task<OperationResult> SaveAsync(SitePage page, SeoMetaData seo, string? slug = null);

    Task<OperationResult> MarkReviewedAsync(SitePage page, Guid? userId);

    /// <summary>ساخت رکوردهای سئوی خالی برای همه صفحه‌های سایت در اولین اجرا.</summary>
    Task EnsureDefaultsAsync(CancellationToken cancellationToken = default);
}
