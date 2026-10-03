using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Entities.Orders;

namespace Fratak_Shop.Core.DTOs.Orders.Query;

public class OrderDto : BaseDto
{
    public string OrderNumber { get; set; } = null!;
    public OrderStatus Status { get; set; }
    public PaymentMethod PaymentMethod { get; set; }
    public PaymentStatus PaymentStatus { get; set; }
    public string? TransactionId { get; set; }
    public string? TrackingCode { get; set; }
    public string? TrackingUrl { get; set; }
    public decimal SubtotalAmount { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal ShippingAmount { get; set; }
    public decimal PayableAmount { get; set; }
    public string? ShippingMethod { get; set; }
    public Guid? ShippingMethodId { get; set; }
    public string? CouponCode { get; set; }
    public string CustomerName { get; set; } = null!;
    public string CustomerPhone { get; set; } = null!;
    public string ShippingProvince { get; set; } = null!;
    public string ShippingCity { get; set; } = null!;
    public string ShippingAddress { get; set; } = null!;
    public string? ShippingPostalCode { get; set; }
    public string? ShippingPhone { get; set; }
    public string? CustomerNote { get; set; }
    public string? AdminNote { get; set; }
    public DateTimeOffset? PaidAt { get; set; }
    public DateTimeOffset? CancelledAt { get; set; }
    public string? CancelReason { get; set; }
    public DateTimeOffset? RequestedDeliveryDate { get; set; }
    public Guid? UserId { get; set; }
    public string? UserPhoneNumber { get; set; }

    public List<OrderItemDto> Items { get; set; } = [];
    public List<OrderStatusHistoryDto> StatusHistory { get; set; } = [];
    public PaymentInfoDto? Payment { get; set; }
    public BankTransferInfoDto? BankTransfer { get; set; }
    public int ItemCount => Items.Sum(item => item.Quantity);
}

public class OrderItemDto
{
    public Guid Id { get; set; }
    public Guid? ProductId { get; set; }
    public string ProductName { get; set; } = null!;
    public string SKU { get; set; } = null!;
    public string? ImageName { get; set; }
    public string? Slug { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal DiscountAmount { get; set; }
    public int Quantity { get; set; }
    public decimal TotalAmount { get; set; }
}

public class OrderStatusHistoryDto
{
    public Guid Id { get; set; }
    public OrderStatus Status { get; set; }
    public OrderStatus? PreviousStatus { get; set; }
    public string? Note { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public bool IsCustomerVisible { get; set; }
    public string? ChangedBy { get; set; }
}

public class PaymentInfoDto
{
    public Guid Id { get; set; }
    public decimal Amount { get; set; }
    public string Gateway { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? ReferenceId { get; set; }
    public string? CardMask { get; set; }
    public DateTimeOffset? PaidAt { get; set; }
    public string? ErrorMessage { get; set; }
}

public class BankTransferInfoDto
{
    public Guid Id { get; set; }
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
    public string PayerName { get; set; } = string.Empty;
    public string PayerCardNumber { get; set; } = string.Empty;
    public string? PayerBankName { get; set; }
    public string? TrackingNumber { get; set; }
    public DateTimeOffset? TransferDate { get; set; }
    public DateTimeOffset? SubmittedAt { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
    public string? RejectReason { get; set; }
    public string? AdminNote { get; set; }
    public string? CardNumber { get; set; }
    public string? BankName { get; set; }
    public List<string> ReceiptFiles { get; set; } = [];
}

public class OrderFilterParams : BaseFilterParam
{
    public string? Search { get; set; }
    public OrderStatus? Status { get; set; }
    public PaymentStatus? PaymentStatus { get; set; }
    public PaymentMethod? PaymentMethod { get; set; }
    public DateTimeOffset? FromDate { get; set; }
    public DateTimeOffset? ToDate { get; set; }
    public Guid? UserId { get; set; }
    public bool? PendingReviewOnly { get; set; }
}

public class OrderFilterResult : BaseFilter<OrderDto, OrderFilterParams>;

/// <summary>آمار خلاصه سفارش‌ها برای داشبورد پنل مدیریت.</summary>
public class OrderStatsDto
{
    public int TotalOrders { get; set; }
    public int TodayOrders { get; set; }
    public int PendingOrders { get; set; }
    public int PaidOrders { get; set; }
    public decimal TotalRevenue { get; set; }
    public decimal TodayRevenue { get; set; }
    public decimal MonthRevenue { get; set; }
    public int AwaitingBankTransferReview { get; set; }
    public List<OrderStatusCountDto> StatusCounts { get; set; } = [];
    public List<DailySalesStat> DailySales { get; set; } = [];
}

public class OrderStatusCountDto
{
    public OrderStatus Status { get; set; }
    public int Count { get; set; }
}

public class DailySalesStat
{
    public string PersianLabel { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public int Count { get; set; }
}
