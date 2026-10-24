using Fratak_Shop.Core.DTOs.Shipping;
using Fratak_Shop.Core.Utilities;

namespace Fratak_Shop.Core.Services.Shipping;

public interface IShippingService
{
    /// <summary>Active shipping methods with their active time slots (cached).</summary>
    Task<List<ShippingMethodDto>> GetActiveMethodsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Methods available for the destination province with the cost for the given
    /// cart subtotal and, for courier methods, the selectable delivery days/slots.
    /// </summary>
    Task<List<ShippingOption>> GetOptionsAsync(Guid? provinceId, decimal subtotal, DateTimeOffset now, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates the customer's choice on the server (province, capacity, off days, cut-off)
    /// and returns the values to store on the order.
    /// </summary>
    Task<OperationResult<ShippingSelection?>> ValidateSelectionAsync(
        Guid shippingMethodId, Guid? provinceId, decimal subtotal,
        DateOnly? deliveryDate, Guid? timeSlotId, DateTimeOffset now,
        CancellationToken cancellationToken = default);

    // ── Admin panel ──
    Task<List<ShippingMethodDto>> GetAllMethodsAsync();
    Task<ShippingMethodDto?> GetMethodByIdAsync(Guid id);
    Task<OperationResult<Guid>> SaveMethodAsync(ShippingMethodCommand command);
    Task<OperationResult> DeleteMethodAsync(Guid id);
    Task<OperationResult<Guid>> SaveTimeSlotAsync(DeliveryTimeSlotCommand command);
    Task<OperationResult> DeleteTimeSlotAsync(Guid id);
}
