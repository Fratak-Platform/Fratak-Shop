using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Fratak_Shop.Data.Entities.Locations;
using Fratak_Shop.Data.Utilities;

namespace Fratak_Shop.Data.Entities.Shipping;

/// <summary>
/// A delivery option offered at checkout (post, store courier, express delivery, ...).
/// Cost, availability and delivery window are managed from the admin panel.
/// </summary>
public class ShippingMethod : BaseEntity
{
    /// <summary>Stable machine name (used in forms and URLs).</summary>
    [Required]
    [MaxLength(50)]
    public string Code { get; set; } = null!;

    [Required]
    [MaxLength(200)]
    public string Title { get; set; } = null!;

    [MaxLength(500)]
    public string? Description { get; set; }

    public ShippingMethodType Type { get; set; } = ShippingMethodType.Post;

    /// <summary>Default cost; with <see cref="ShippingPricingMode.ByLocation"/> province / city rates override it.</summary>
    public decimal Cost { get; set; }

    public ShippingPricingMode PricingMode { get; set; } = ShippingPricingMode.Fixed;

    /// <summary>The method is offered only in locations that have a (non-blocked) rate.</summary>
    public bool OnlyListedLocations { get; set; }

    /// <summary>Method specific free-shipping threshold; null = the store-wide threshold applies, 0 = never free.</summary>
    public decimal? FreeShippingThreshold { get; set; }

    /// <summary>Earliest delivery day offset from today.</summary>
    public int MinDeliveryDays { get; set; } = 1;

    /// <summary>Latest delivery day offset from today (shown to the customer).</summary>
    public int MaxDeliveryDays { get; set; } = 3;

    /// <summary>The customer chooses a delivery day and a time slot (store courier / express).</summary>
    public bool RequiresTimeSlot { get; set; }

    public bool SupportsCashOnDelivery { get; set; } = true;

    /// <summary>When set, the method is only offered for addresses in this province.</summary>
    public Guid? ProvinceId { get; set; }

    public bool IsActive { get; set; } = true;

    public int DisplayOrder { get; set; }

    [ForeignKey(nameof(ProvinceId))]
    public Location? Province { get; set; }

    public List<DeliveryTimeSlot> TimeSlots { get; set; } = [];

    public List<ShippingRate> Rates { get; set; } = [];
}

public enum ShippingPricingMode
{
    Fixed = 0,       // یک هزینه ثابت برای کل کشور
    ByLocation = 1,  // هزینه بر اساس استان / شهر
}

public enum ShippingMethodType
{
    None = 0,
    Post = 1,          // پست
    Courier = 2,       // پیک فروشگاه
    Express = 3,       // ارسال فوری
    StorePickup = 4,   // تحویل حضوری
}
