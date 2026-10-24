using Fratak_Shop.Data.Entities.Shipping;

namespace Fratak_Shop.Core.DTOs.Shipping;

public class ShippingMethodDto
{
    public Guid Id { get; set; }
    public string Code { get; set; } = null!;
    public string Title { get; set; } = null!;
    public string? Description { get; set; }
    public ShippingMethodType Type { get; set; }
    public decimal Cost { get; set; }

    /// <summary>null = the store-wide threshold, 0 = never free.</summary>
    public decimal? FreeShippingThreshold { get; set; }

    public int MinDeliveryDays { get; set; }
    public int MaxDeliveryDays { get; set; }
    public bool RequiresTimeSlot { get; set; }
    public bool SupportsCashOnDelivery { get; set; }
    public Guid? ProvinceId { get; set; }
    public string? ProvinceName { get; set; }
    public bool IsActive { get; set; }
    public int DisplayOrder { get; set; }

    public List<DeliveryTimeSlotDto> TimeSlots { get; set; } = [];

    /// <summary>Human readable delivery estimate, e.g. "۲ تا ۴ روز کاری".</summary>
    public string DeliveryEstimate => (MinDeliveryDays, MaxDeliveryDays) switch
    {
        (0, 0) => "همان روز",
        (var min, var max) when min == max => $"{min} روز کاری",
        (0, var max) => $"همان روز تا {max} روز کاری",
        (var min, var max) => $"{min} تا {max} روز کاری",
    };
}

public class DeliveryTimeSlotDto
{
    public Guid Id { get; set; }
    public Guid ShippingMethodId { get; set; }
    public string Title { get; set; } = null!;
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
    public int? Capacity { get; set; }
    public bool IsActive { get; set; }
    public int DisplayOrder { get; set; }
}

/// <summary>A shipping method offered for a specific cart and address.</summary>
public class ShippingOption
{
    public ShippingMethodDto Method { get; set; } = null!;
    public decimal Cost { get; set; }
    public bool IsFree => Cost == 0;

    /// <summary>Remaining amount the cart needs for free shipping (null when not applicable).</summary>
    public decimal? RemainingForFreeShipping { get; set; }

    /// <summary>Selectable delivery days (only for methods with time slots).</summary>
    public List<DeliveryDayOption> Days { get; set; } = [];
}

public class DeliveryDayOption
{
    public DateOnly Date { get; set; }
    public bool IsToday { get; set; }
    public List<DeliverySlotOption> Slots { get; set; } = [];
    public bool HasAvailableSlot => Slots.Any(slot => slot.IsAvailable);
}

public class DeliverySlotOption
{
    public Guid SlotId { get; set; }
    public string Title { get; set; } = null!;
    public bool IsAvailable { get; set; }

    /// <summary>Why the slot can't be chosen ("تکمیل ظرفیت", "گذشته").</summary>
    public string? UnavailableReason { get; set; }
}

/// <summary>A validated shipping choice, ready to be stored on the order.</summary>
public class ShippingSelection
{
    public Guid ShippingMethodId { get; set; }
    public string ShippingMethodTitle { get; set; } = null!;
    public decimal Cost { get; set; }
    public bool SupportsCashOnDelivery { get; set; }
    public Guid? DeliveryTimeSlotId { get; set; }
    public string? DeliveryTimeSlotTitle { get; set; }
    public DateTimeOffset? RequestedDeliveryDate { get; set; }
}

public class ShippingMethodCommand
{
    public Guid Id { get; set; }
    public string Code { get; set; } = null!;
    public string Title { get; set; } = null!;
    public string? Description { get; set; }
    public ShippingMethodType Type { get; set; } = ShippingMethodType.Post;
    public decimal Cost { get; set; }
    public decimal? FreeShippingThreshold { get; set; }
    public int MinDeliveryDays { get; set; } = 1;
    public int MaxDeliveryDays { get; set; } = 3;
    public bool RequiresTimeSlot { get; set; }
    public bool SupportsCashOnDelivery { get; set; } = true;
    public Guid? ProvinceId { get; set; }
    public bool IsActive { get; set; } = true;
    public int DisplayOrder { get; set; }
}

public class DeliveryTimeSlotCommand
{
    public Guid Id { get; set; }
    public Guid ShippingMethodId { get; set; }
    public string Title { get; set; } = null!;
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
    public int? Capacity { get; set; }
    public bool IsActive { get; set; } = true;
    public int DisplayOrder { get; set; }
}
