using Fratak_Shop.Core.DTOs.Shipping;
using Fratak_Shop.Data.Entities.Shipping;

namespace Fratak_Shop.Core.Mappers.Shipping;

public static class ShippingMapper
{
    public static ShippingMethodDto Map(this ShippingMethod method)
    {
        return new ShippingMethodDto
        {
            Id = method.Id,
            Code = method.Code,
            Title = method.Title,
            Description = method.Description,
            Type = method.Type,
            Cost = method.Cost,
            FreeShippingThreshold = method.FreeShippingThreshold,
            MinDeliveryDays = method.MinDeliveryDays,
            MaxDeliveryDays = method.MaxDeliveryDays,
            RequiresTimeSlot = method.RequiresTimeSlot,
            SupportsCashOnDelivery = method.SupportsCashOnDelivery,
            ProvinceId = method.ProvinceId,
            ProvinceName = method.Province?.Name,
            IsActive = method.IsActive,
            DisplayOrder = method.DisplayOrder,
            TimeSlots = method.TimeSlots
                .OrderBy(slot => slot.DisplayOrder)
                .ThenBy(slot => slot.StartTime)
                .Select(slot => slot.Map())
                .ToList(),
        };
    }

    public static DeliveryTimeSlotDto Map(this DeliveryTimeSlot slot)
    {
        return new DeliveryTimeSlotDto
        {
            Id = slot.Id,
            ShippingMethodId = slot.ShippingMethodId,
            Title = slot.Title,
            StartTime = slot.StartTime,
            EndTime = slot.EndTime,
            Capacity = slot.Capacity,
            IsActive = slot.IsActive,
            DisplayOrder = slot.DisplayOrder,
        };
    }

    public static void Apply(this ShippingMethod method, ShippingMethodCommand command)
    {
        method.Code = command.Code.Trim().ToLowerInvariant();
        method.Title = command.Title.Trim();
        method.Description = string.IsNullOrWhiteSpace(command.Description) ? null : command.Description.Trim();
        method.Type = command.Type;
        method.Cost = command.Cost;
        method.FreeShippingThreshold = command.FreeShippingThreshold;
        method.MinDeliveryDays = command.MinDeliveryDays;
        method.MaxDeliveryDays = command.MaxDeliveryDays;
        method.RequiresTimeSlot = command.RequiresTimeSlot;
        method.SupportsCashOnDelivery = command.SupportsCashOnDelivery;
        method.ProvinceId = command.ProvinceId;
        method.IsActive = command.IsActive;
        method.DisplayOrder = command.DisplayOrder;
    }

    public static void Apply(this DeliveryTimeSlot slot, DeliveryTimeSlotCommand command)
    {
        slot.ShippingMethodId = command.ShippingMethodId;
        slot.Title = command.Title.Trim();
        slot.StartTime = command.StartTime;
        slot.EndTime = command.EndTime;
        slot.Capacity = command.Capacity;
        slot.IsActive = command.IsActive;
        slot.DisplayOrder = command.DisplayOrder;
    }
}
