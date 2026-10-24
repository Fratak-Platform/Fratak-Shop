using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Fratak_Shop.Data.Entities.Locations;
using Fratak_Shop.Data.Utilities;

namespace Fratak_Shop.Data.Entities.Shipping;

/// <summary>
/// Location rule of a shipping method: a province or city specific cost, or a
/// block (no delivery to that location). A rule without a method applies to every
/// method (e.g. "no shipping to this city at all").
/// The most specific active rule wins: city before province, method before all methods.
/// </summary>
public class ShippingRate : BaseEntity
{
    /// <summary>Null = the rule applies to every shipping method.</summary>
    public Guid? ShippingMethodId { get; set; }

    public Guid? ProvinceId { get; set; }

    /// <summary>When set, the rule is for this city only (overrides the province rule).</summary>
    public Guid? CityId { get; set; }

    /// <summary>Cost for this location (used by methods priced by location; null = method default).</summary>
    public decimal? Cost { get; set; }

    /// <summary>Free-shipping threshold for this location (null = method / store threshold).</summary>
    public decimal? FreeShippingThreshold { get; set; }

    /// <summary>Extra days added to the delivery estimate for this location.</summary>
    public int ExtraDeliveryDays { get; set; }

    /// <summary>No delivery to this location (with this method, or with any method when the method is empty).</summary>
    public bool IsBlocked { get; set; }

    /// <summary>Reason shown to the customer when blocked (optional).</summary>
    [MaxLength(200)]
    public string? Note { get; set; }

    public bool IsActive { get; set; } = true;

    [ForeignKey(nameof(ShippingMethodId))]
    public ShippingMethod? ShippingMethod { get; set; }

    [ForeignKey(nameof(ProvinceId))]
    public Location? Province { get; set; }

    [ForeignKey(nameof(CityId))]
    public Location? City { get; set; }
}
