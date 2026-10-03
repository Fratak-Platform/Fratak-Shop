using Fratak_Shop.Core.DTOs.Pages.Query;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Entities.Pages;

namespace Fratak_Shop.Core.Services.Pages;

/// <summary>
/// Positioned alerts/notices of the storefront pages, fully managed from the panel
/// (page + position + type + display window), so the layout never hard-codes a notice.
/// </summary>
public interface IPageAlertService
{
    Task<List<PageAlertDto>> GetAllAsync(SitePage? page = null);

    /// <summary>هشدارهای فعال و در بازه زمانی یک صفحه، برای رندر در قالب.</summary>
    Task<List<PageAlertDto>> GetVisibleAsync(SitePage page, CancellationToken cancellationToken = default);

    Task<PageAlertDto?> GetAsync(Guid id);

    Task<OperationResult> SaveAsync(PageAlertDto model);

    Task<OperationResult> DeleteAsync(Guid id);

    Task<OperationResult> ToggleActiveAsync(Guid id);
}
