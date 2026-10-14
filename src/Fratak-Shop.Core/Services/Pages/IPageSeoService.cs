using Fratak_Shop.Core.DTOs.Pages.Command;
using Fratak_Shop.Core.DTOs.Pages.Query;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Entities.Pages;

namespace Fratak_Shop.Core.Services.Pages;

public interface IPageSeoService
{
    Task<OperationResult> CreatePageSeoAsync(CreatePageSeoCommand command);
    Task<OperationResult> EditPageSeoAsync(EditPageSeoCommand command);

    /// <summary>
    /// ثبت یا به‌روزرسانی سئوی یک صفحه؛ اگر صفحه از قبل سئو داشته باشد ویرایش می‌شود.
    /// </summary>
    Task<OperationResult> SavePageSeoAsync(SavePageSeoCommand command);

    Task<OperationResult> DeletePageSeoAsync(Guid id);

    List<PageSeoDto> GetAllPageSeosAsync();
    Task<PageSeoDto?> GetPageSeoByIdAsync(Guid id);
    Task<PageSeoDto?> GetPageSeoByPageAsync(SitePage page);
}
