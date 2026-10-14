using Fratak_Shop.Core.DTOs.SeoRedirects.Command;
using Fratak_Shop.Core.DTOs.SeoRedirects.Query;
using Fratak_Shop.Core.Utilities;

namespace Fratak_Shop.Core.Services.SeoRedirects;

public interface ISeoRedirectService
{
    Task<OperationResult> CreateRedirectAsync(CreateSeoRedirectCommand command);
    Task<OperationResult> EditRedirectAsync(EditSeoRedirectCommand command);
    Task<OperationResult> DeleteRedirectAsync(Guid id);
    Task<OperationResult> ChangeRedirectActivationAsync(Guid id, bool isActive);

    Task<SeoRedirectFilterResult> GetRedirectsByFilterAsync(SeoRedirectFilterParams filterParams);
    List<SeoRedirectDto> GetAllRedirectsAsync();
    Task<SeoRedirectDto?> GetRedirectByIdAsync(Guid id);

    /// <summary>
    /// ریدایرکت فعال یک مسیر را برمی‌گرداند تا لایه وب درخواست‌های قدیمی را
    /// به آدرس جدید هدایت کند.
    /// </summary>
    Task<SeoRedirectDto?> GetRedirectByPathAsync(string fromPath);
}
