using Fratak_Shop.Core.DTOs.Shipping;
using Fratak_Shop.Core.Utilities;

namespace Fratak_Shop.Core.Services.Shipping;

public interface IShippingService
{
    /// <summary>Active shipping methods with their active time slots (cached).</summary>
    Task<List<ShippingMethodDto>> GetActiveMethodsAsync(CancellationToken cancellationToken = default);

    /// <summary>Active province / city rules (cached).</summary>
    Task<List<ShippingRateDto>> GetActiveRatesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Methods available for the destination with the cost for the given cart subtotal
    /// (fixed or by province / city) and, for courier methods, the selectable delivery days/slots.
    /// Methods blocked for the destination are left out.
    /// </summary>
    Task<List<ShippingOption>> GetOptionsAsync(ShippingDestination destination, decimal subtotal, DateTimeOffset now, CancellationToken cancellationToken = default);

    /// <summary>Active methods that don't deliver to the destination, with the reason.</summary>
    Task<List<UnavailableShippingMethod>> GetUnavailableMethodsAsync(ShippingDestination destination, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates the customer's choice on the server (destination rules, capacity, off days, cut-off)
    /// and returns the values to store on the order.
    /// </summary>
    Task<OperationResult<ShippingSelection?>> ValidateSelectionAsync(
        Guid shippingMethodId, ShippingDestination destination, decimal subtotal,
        DateOnly? deliveryDate, Guid? timeSlotId, DateTimeOffset now,
        CancellationToken cancellationToken = default);

    // ── Admin panel ──
    Task<List<ShippingMethodDto>> GetAllMethodsAsync();
    Task<ShippingMethodDto?> GetMethodByIdAsync(Guid id);
    Task<OperationResult<Guid>> SaveMethodAsync(ShippingMethodCommand command);
    Task<OperationResult> DeleteMethodAsync(Guid id);
    Task<OperationResult<Guid>> SaveTimeSlotAsync(DeliveryTimeSlotCommand command);
    Task<OperationResult> DeleteTimeSlotAsync(Guid id);

    /// <summary>Province / city rules (of one method plus the rules of all methods).</summary>
    Task<List<ShippingRateDto>> GetRatesAsync(Guid? shippingMethodId = null);
    Task<OperationResult<Guid>> SaveRateAsync(ShippingRateCommand command);
    Task<OperationResult> DeleteRateAsync(Guid id);
}
