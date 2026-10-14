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
}
