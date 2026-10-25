using Fratak_Shop.Data.Entities.Orders;
using Fratak_Shop.Data.Entities.Payments;

namespace Fratak_Shop.Core.DTOs.Admin;

public sealed class AdminOrderFilter
{
    public string? Search { get; set; }
    public OrderStatus? Status { get; set; }
    public PaymentStatus? PaymentStatus { get; set; }
    public PaymentMethod? PaymentMethod { get; set; }
    public DateTimeOffset? From { get; set; }
    public DateTimeOffset? To { get; set; }
    /// <summary>true = only orders that have / false = that lack a purchase invoice.</summary>
    public bool? HasPurchaseInvoice { get; set; }
    public Guid? UserId { get; set; }
    public string? Province { get; set; }
    public string Sort { get; set; } = "newest";
    public int Page { get; set; } = 1;
    public int Take { get; set; } = 25;
}

public sealed class AdminOrderRow
{
    public Guid Id { get; set; }
    public string OrderNumber { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? UserId { get; set; }
    public string CustomerName { get; set; } = null!;
    public string CustomerPhone { get; set; } = null!;
    public string ShippingProvince { get; set; } = null!;
    public string ShippingCity { get; set; } = null!;
    public OrderStatus Status { get; set; }
    public PaymentStatus PaymentStatus { get; set; }
    public PaymentMethod PaymentMethod { get; set; }
    public decimal PayableAmount { get; set; }
    public decimal DiscountTotal { get; set; }
    public decimal ShippingAmount { get; set; }
    public int ItemCount { get; set; }
    public string? ShippingMethod { get; set; }
    public string? TrackingCode { get; set; }
    public int PurchaseInvoiceCount { get; set; }
    public decimal PurchaseCost { get; set; }
}

public sealed class AdminOrderList
{
    public PagedList<AdminOrderRow> Orders { get; set; } = null!;
    public Dictionary<OrderStatus, int> StatusCounts { get; set; } = [];
    public int AwaitingInvoiceCount { get; set; }
    public decimal FilteredTotal { get; set; }
}

public sealed class AdminOrderItem
{
    public Guid Id { get; set; }
    public Guid? ProductId { get; set; }
    public string ProductName { get; set; } = null!;
    public string? ProductSlug { get; set; }
    public string? ImageName { get; set; }
    public string SKU { get; set; } = null!;
    public decimal UnitPrice { get; set; }
    public decimal DiscountAmount { get; set; }
    public int Quantity { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal? UnitCostPrice { get; set; }
    public int? CurrentStock { get; set; }
    public string? SupplierName { get; set; }
    public string? SupplierUrl { get; set; }
    public string? SupplierSku { get; set; }
    public string? SupplierNote { get; set; }
}

public sealed class OrderNoteDto
{
    public Guid Id { get; set; }
    public OrderNoteKind Kind { get; set; }
    public string? AuthorName { get; set; }
    public string Content { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class PurchaseInvoiceDto
{
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
    public string? OrderNumber { get; set; }
    public Guid? SupplierId { get; set; }
    public string? SupplierName { get; set; }
    public string? InvoiceNumber { get; set; }
    public decimal Amount { get; set; }
    public DateTimeOffset? PurchasedAt { get; set; }
    public string FileName { get; set; } = null!;
    public string ContentType { get; set; } = null!;
    public int SizeBytes { get; set; }
    public string? Notes { get; set; }
    public string? UploadedByName { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class AdminPaymentRow
{
    public Guid Id { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string Gateway { get; set; } = null!;
    public PaymentPurpose Purpose { get; set; }
    public decimal Amount { get; set; }
    public PaymentTransactionStatus Status { get; set; }
    public string? RefId { get; set; }
    public string? CardPan { get; set; }
    public string? Authority { get; set; }
    public int? ResultCode { get; set; }
    public string? Message { get; set; }
    public DateTimeOffset? VerifiedAt { get; set; }
    public Guid? UserId { get; set; }
    public string? UserName { get; set; }
    public Guid? OrderId { get; set; }
    public string? OrderNumber { get; set; }
}

public sealed class AdminOrderDetail
{
    public Order Order { get; set; } = null!;
    public List<AdminOrderItem> Items { get; set; } = [];
    public List<OrderNoteDto> Notes { get; set; } = [];
    public List<PurchaseInvoiceDto> PurchaseInvoices { get; set; } = [];
    public List<AdminPaymentRow> Payments { get; set; } = [];
    public List<(string Number, ReturnStatus Status, decimal Amount)> Returns { get; set; } = [];
    public List<(string Number, string Subject)> Tickets { get; set; } = [];
    public int CustomerOrderCount { get; set; }
    public decimal CustomerTotalSpent { get; set; }
    public bool CustomerIsBlocked { get; set; }
    public decimal PurchaseCost => PurchaseInvoices.Sum(i => i.Amount);
    public decimal EstimatedCost => Items.Sum(i => (i.UnitCostPrice ?? 0) * i.Quantity);
    public IReadOnlyList<OrderStatus> NextStatuses { get; set; } = [];
    public bool RequiresPurchaseInvoice { get; set; }
}

public sealed class AddPurchaseInvoiceCommand
{
    public Guid OrderId { get; set; }
    public Guid? SupplierId { get; set; }
    public string? SupplierName { get; set; }
    public string? InvoiceNumber { get; set; }
    public decimal Amount { get; set; }
    public DateTimeOffset? PurchasedAt { get; set; }
    public string FileName { get; set; } = null!;
    public string ContentType { get; set; } = null!;
    public int SizeBytes { get; set; }
    public string? Notes { get; set; }
    public Guid? UploadedByUserId { get; set; }
    public string? UploadedByName { get; set; }
}

public sealed class PurchaseInvoiceFilter
{
    public string? Search { get; set; }
    public Guid? SupplierId { get; set; }
    public DateTimeOffset? From { get; set; }
    public DateTimeOffset? To { get; set; }
    public int Page { get; set; } = 1;
    public int Take { get; set; } = 30;
}
