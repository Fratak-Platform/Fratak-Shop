using Fratak_Shop.Core.DTOs.Pages.Command;
using Fratak_Shop.Core.DTOs.Pages.Query;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Entities.Pages;

namespace Fratak_Shop.Core.Services.Pages;

public interface IPageAlertService
{
    Task<OperationResult> CreateAlertAsync(CreatePageAlertCommand command);
    Task<OperationResult> EditAlertAsync(EditPageAlertCommand command);
    Task<OperationResult> DeleteAlertAsync(Guid id);
    Task<OperationResult> ChangeAlertActivationAsync(Guid id, bool isActive);

    List<PageAlertDto> GetAllAlertsAsync();
    Task<List<PageAlertDto>> GetAlertsByPageAsync(SitePage page, AlertPosition? position = null);
    Task<List<PageAlertDto>> GetActiveAlertsAsync(SitePage page, AlertPosition? position = null);
    Task<PageAlertDto?> GetAlertByIdAsync(Guid id);

    /// <summary>
    /// Alerts to show now for the visitor on the page (alerts of the page and of "all pages",
    /// matching audience, product / category and display window). Served from a short cache.
    /// </summary>
    Task<List<PageAlertDto>> GetVisibleAlertsAsync(PageAlertContext alertContext);
}
