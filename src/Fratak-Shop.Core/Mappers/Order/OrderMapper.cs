using Fratak_Shop.Core.DTOs.Orders.Query;
using Fratak_Shop.Data.Entities.Orders;

namespace Fratak_Shop.Core.Mappers.Order;

/// <summary>
/// Maps an order graph to the read models used by the storefront tracking pages and
/// the admin order management screen. The mapper is the only place that decides how
/// order money is presented, so every screen stays consistent.
/// </summary>
public static class OrderMapper
{
    public static OrderDto MapDto(this Data.Entities.Orders.Order order)
    {
        var dto = new OrderDto
        {
            Id = order.Id,
            CreatedAt = order.CreatedAt,
            UpdatedAt = order.UpdatedAt,
            IsDeleted = order.IsDeleted,
            DeletedAt = order.DeletedAt,
            OrderNumber = order.OrderNumber,
            Status = order.Status,
            PaymentMethod = order.PaymentMethod,
            PaymentStatus = order.PaymentStatus,
            TransactionId = order.TransactionId,
            TrackingCode = order.TrackingCode,
            TrackingUrl = order.TrackingUrl,
            SubtotalAmount = order.SubtotalAmount,
            DiscountAmount = order.DiscountAmount,
            ShippingAmount = order.ShippingAmount,
            PayableAmount = order.PayableAmount,
            ShippingMethod = order.ShippingMethod?.Name ?? order.ShippingMethodName,
            ShippingMethodId = order.ShippingMethodId,
            CouponCode = order.Coupon?.Code,
            CustomerName = order.CustomerName,
            CustomerPhone = order.CustomerPhone,
            ShippingProvince = order.ShippingProvince,
            ShippingCity = order.ShippingCity,
            ShippingAddress = order.ShippingAddress,
            ShippingPostalCode = order.ShippingPostalCode,
            ShippingPhone = order.ShippingPhone,
            CustomerNote = order.CustomerNote,
            AdminNote = order.AdminNote,
            PaidAt = order.PaidAt,
            CancelledAt = order.CancelledAt,
            CancelReason = order.CancelReason,
            RequestedDeliveryDate = order.RequestedDeliveryDate,
            UserId = order.UserId,
            UserPhoneNumber = order.User?.PhoneNumber,
            Items = order.Items
                .OrderBy(item => item.CreatedAt)
                .Select(item => new OrderItemDto
                {
                    Id = item.Id,
                    ProductId = item.ProductId,
                    ProductName = item.ProductName,
                    SKU = item.SKU,
                    Slug = item.Product?.Slug,
                    ImageName = item.Product?.Images
                        .OrderByDescending(image => image.IsPrimary)
                        .ThenBy(image => image.DisplayOrder)
                        .Select(image => image.ImageName)
                        .FirstOrDefault(),
                    UnitPrice = item.UnitPrice,
                    DiscountAmount = item.DiscountAmount,
                    Quantity = item.Quantity,
                    TotalAmount = item.TotalAmount,
                })
                .ToList(),
            StatusHistory = order.StatusHistory
                .OrderBy(history => history.CreatedAt)
                .Select(history => new OrderStatusHistoryDto
                {
                    Id = history.Id,
                    Status = history.Status,
                    PreviousStatus = history.PreviousStatus,
                    Note = history.Note,
                    IsCustomerVisible = history.IsCustomerVisible,
                    CreatedAt = history.CreatedAt,
                    ChangedBy = history.ChangedByUser is null
                        ? null
                        : $"{history.ChangedByUser.FirstName} {history.ChangedByUser.LastName}".Trim(),
                })
                .ToList(),
        };

        var transaction = order.PaymentTransactions
            .OrderByDescending(item => item.CreatedAt)
            .FirstOrDefault();

        if (transaction is not null)
        {
            dto.Payment = new PaymentInfoDto
            {
                Id = transaction.Id,
                Amount = transaction.Amount,
                Gateway = transaction.Gateway.ToString(),
                Status = transaction.Status.ToString(),
                ReferenceId = transaction.ReferenceId,
                CardMask = transaction.CardMask,
                PaidAt = transaction.PaidAt,
                ErrorMessage = transaction.ErrorMessage,
            };
        }

        if (order.BankTransferPayment is not null)
        {
            dto.BankTransfer = new BankTransferInfoDto
            {
                Id = order.BankTransferPayment.Id,
                Amount = order.BankTransferPayment.Amount,
                Status = order.BankTransferPayment.Status.ToString(),
                PayerName = order.BankTransferPayment.PayerName,
                PayerCardNumber = order.BankTransferPayment.PayerCardNumber,
                PayerBankName = order.BankTransferPayment.PayerBankName,
                TrackingNumber = order.BankTransferPayment.TrackingNumber,
                TransferDate = order.BankTransferPayment.TransferDate,
                SubmittedAt = order.BankTransferPayment.SubmittedAt,
                ReviewedAt = order.BankTransferPayment.ReviewedAt,
                RejectReason = order.BankTransferPayment.RejectReason,
                AdminNote = order.BankTransferPayment.AdminNote,
                CardNumber = order.BankTransferPayment.PaymentCard?.CardNumber,
                BankName = order.BankTransferPayment.PaymentCard?.BankName,
                ReceiptFiles = order.BankTransferPayment.Receipts
                    .Select(receipt => receipt.FileName)
                    .ToList(),
            };
        }

        return dto;
    }

    public static List<OrderDto> MapDto(this List<Data.Entities.Orders.Order> orders) =>
        orders.Select(order => order.MapDto()).ToList();
}
