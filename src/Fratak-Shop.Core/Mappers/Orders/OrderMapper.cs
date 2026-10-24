using Fratak_Shop.Core.DTOs.Orders.Query;
using Fratak_Shop.Data.Entities.Orders;

namespace Fratak_Shop.Core.Mappers.Orders;

public static class OrderMapper
{
    public static OrderDto Map(this Order order)
    {
        return new OrderDto
        {
            Id = order.Id,
            CreatedAt = order.CreatedAt,
            UpdatedAt = order.UpdatedAt,
            IsDeleted = order.IsDeleted,
            DeletedAt = order.DeletedAt,
            UserId = order.UserId,
            OrderNumber = order.OrderNumber,
            Status = order.Status,
            PaymentMethod = order.PaymentMethod,
            PaymentStatus = order.PaymentStatus,
            TransactionId = order.TransactionId,
            TrackingCode = order.TrackingCode,
            SubtotalAmount = order.SubtotalAmount,
            DiscountAmount = order.DiscountAmount,
            CouponCode = order.CouponCode,
            CouponDiscountAmount = order.CouponDiscountAmount,
            ShippingAmount = order.ShippingAmount,
            PayableAmount = order.PayableAmount,
            ShippingMethod = order.ShippingMethod,
            ShippingMethodId = order.ShippingMethodId,
            RequestedDeliveryDate = order.RequestedDeliveryDate,
            DeliveryTimeSlotId = order.DeliveryTimeSlotId,
            DeliveryTimeSlotTitle = order.DeliveryTimeSlotTitle,
            PaymentDeadline = order.PaymentDeadline,
            CancelledAt = order.CancelledAt,
            CancelledBy = order.CancelledBy,
            CancellationReason = order.CancellationReason,
            RefundedAmount = order.RefundedAmount,
            RefundedAt = order.RefundedAt,
            WalletBonusAmount = order.WalletBonusAmount,
            RefundedBonusAmount = order.RefundedBonusAmount,
            InvoiceNumber = order.InvoiceNumber,
            InvoiceIssuedAt = order.InvoiceIssuedAt,
            CustomerName = order.CustomerName,
            CustomerPhone = order.CustomerPhone,
            ShippingProvince = order.ShippingProvince,
            ShippingCity = order.ShippingCity,
            ShippingAddress = order.ShippingAddress,
            ShippingBuildingNumber = order.ShippingBuildingNumber,
            ShippingUnit = order.ShippingUnit,
            ShippingPostalCode = order.ShippingPostalCode,
            ShippingPhone = order.ShippingPhone,
            CustomerNote = order.CustomerNote,
            PaidAt = order.PaidAt,
            Items = order.Items
                .OrderBy(item => item.CreatedAt)
                .Select(item => item.Map())
                .ToList(),
        };
    }

    public static List<OrderDto> Map(this List<Order> orders)
    {
        return orders.Select(order => order.Map()).ToList();
    }

    public static OrderItemDto Map(this OrderItem item)
    {
        return new OrderItemDto
        {
            Id = item.Id,
            CreatedAt = item.CreatedAt,
            UpdatedAt = item.UpdatedAt,
            IsDeleted = item.IsDeleted,
            DeletedAt = item.DeletedAt,
            OrderId = item.OrderId,
            ProductId = item.ProductId,
            ProductName = item.ProductName,
            SKU = item.SKU,
            ProductSlug = item.Product?.Slug,
            ProductImageName = item.Product?.Images
                .OrderByDescending(image => image.IsPrimary)
                .ThenBy(image => image.DisplayOrder)
                .Select(image => image.ImageName)
                .FirstOrDefault(),
            UnitPrice = item.UnitPrice,
            DiscountAmount = item.DiscountAmount,
            Quantity = item.Quantity,
            TotalAmount = item.TotalAmount,
        };
    }

    public static List<OrderItemDto> Map(this List<OrderItem> items)
    {
        return items.Select(item => item.Map()).ToList();
    }

    public static OrderFilterData MapFilterData(this Order order)
    {
        return new OrderFilterData
        {
            Id = order.Id,
            CreatedAt = order.CreatedAt,
            UpdatedAt = order.UpdatedAt,
            IsDeleted = order.IsDeleted,
            DeletedAt = order.DeletedAt,
            UserId = order.UserId,
            OrderNumber = order.OrderNumber,
            Status = order.Status,
            PaymentMethod = order.PaymentMethod,
            PaymentStatus = order.PaymentStatus,
            SubtotalAmount = order.SubtotalAmount,
            DiscountAmount = order.DiscountAmount,
            ShippingAmount = order.ShippingAmount,
            PayableAmount = order.PayableAmount,
            CustomerName = order.CustomerName,
            CustomerPhone = order.CustomerPhone,
            TrackingCode = order.TrackingCode,
            ItemCount = order.Items.Count,
            PaidAt = order.PaidAt,
            PaymentDeadline = order.PaymentDeadline,
            RefundedAmount = order.RefundedAmount,
        };
    }

    public static List<OrderFilterData> MapFilterData(this List<Order> orders)
    {
        return orders.Select(order => order.MapFilterData()).ToList();
    }
}
