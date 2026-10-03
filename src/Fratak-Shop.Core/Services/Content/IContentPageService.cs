using Fratak_Shop.Core.DTOs.Content.Command;
using Fratak_Shop.Core.DTOs.Content.Query;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Entities.Pages;

namespace Fratak_Shop.Core.Services.Content;

/// <summary>
/// Editable body of the standalone pages (درباره ما، تماس با ما، قوانین، حریم خصوصی،
/// گارانتی و پرسش‌های متداول). Content and SEO of a page are edited in one screen but
/// stored independently: the body lives here, the SEO record lives in
/// <see cref="Data.Entities.Pages.PageSeo"/>.
/// </summary>
public interface IContentPageService
{
    Task<List<ContentPageDto>> GetAllAsync();

    Task<ContentPageDto?> GetAsync(SitePage page);

    Task<ContentPageDto?> GetByIdAsync(Guid id);

    Task<OperationResult> SaveAsync(SaveContentPageCommand command);

    Task<OperationResult> TogglePublishedAsync(Guid id);

    /// <summary>ساخت صفحه‌های پیش‌فرض فارسی در اولین اجرا تا هیچ صفحه‌ای خالی نماند.</summary>
    Task EnsureDefaultsAsync(CancellationToken cancellationToken = default);
}
