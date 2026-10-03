using System.ComponentModel.DataAnnotations;
using Fratak_Shop.Data.Utilities;

namespace Fratak_Shop.Data.Entities.Orders;

/// <summary>
/// Shipping option offered in the checkout (پست پیشتاز، تیپاکس، پیک تهران، ...).
/// The cost and the free-shipping threshold are data, so the shipping step of the
/// checkout never has to hard-code a price.
/// </summary>
public class ShippingMethod : BaseEntity
{
    [Required]
    [MaxLength(150)]
    public string Name { get; set; } = null!;

    [MaxLength(1000)]
    public string? Description { get; set; }

    /// <summary>
    /// Cost in Toman.
    /// </summary>
    public decimal Cost { get; set; }

    /// <summary>
    /// When the order total reaches this amount the shipping becomes free.
    /// </summary>
    public decimal? FreeShippingThreshold { get; set; }

    /// <summary>
    /// Estimated delivery time in days, shown to the customer.
    /// </summary>
    public int? EstimatedDeliveryDays { get; set; }

    /// <summary>
    /// Optional city/province restriction (name of the city); empty means all over the country.
    /// </summary>
    [MaxLength(100)]
    public string? RestrictedToCity { get; set; }

    [MaxLength(100)]
    public string? Icon { get; set; }

    public int DisplayOrder { get; set; }

    public bool IsActive { get; set; } = true;
}
