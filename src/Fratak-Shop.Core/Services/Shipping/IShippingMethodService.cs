using Fratak_Shop.Core.DTOs.Shipping.Query;
using Fratak_Shop.Core.Utilities;

namespace Fratak_Shop.Core.Services.Shipping;

/// <summary>
/// Shipping options shown in the checkout. Cost and free-shipping threshold are
/// data, so the shipping step of the checkout is fully configurable from the panel.
/// </summary>
public interface IShippingMethodService
{
    Task<List<ShippingMethodDto>> GetActiveAsync(string? city = null);

    Task<List<ShippingMethodDto>> GetAllAsync();

    Task<ShippingMethodDto?> GetAsync(Guid id);

    Task<OperationResult> SaveAsync(Guid? id, ShippingMethodDto model);

    Task<OperationResult> ToggleActiveAsync(Guid id);

    Task<OperationResult> DeleteAsync(Guid id);
}
