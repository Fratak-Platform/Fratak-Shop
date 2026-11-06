using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Fratak_Shop.Data.Utilities;

namespace Fratak_Shop.Data.Entities.Shipping;

/// <summary>
/// Delivery time window of a shipping method (e.g. 9 to 12). Optional capacity
/// limits the number of orders accepted for the slot on a single day.
/// </summary>
public class DeliveryTimeSlot : BaseEntity
{
    public Guid ShippingMethodId { get; set; }

    [Required]
    [MaxLength(100)]
    public string Title { get; set; } = null!;

    public TimeOnly StartTime { get; set; }

    public TimeOnly EndTime { get; set; }

    /// <summary>Maximum orders per day for this slot (null = unlimited).</summary>
    public int? Capacity { get; set; }

    public bool IsActive { get; set; } = true;

    public int DisplayOrder { get; set; }

    [ForeignKey(nameof(ShippingMethodId))]
    public ShippingMethod ShippingMethod { get; set; } = null!;
}
