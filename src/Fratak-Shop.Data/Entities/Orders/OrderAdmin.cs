using System.ComponentModel.DataAnnotations;
using Fratak_Shop.Data.Entities.Products;
using Fratak_Shop.Data.Utilities;

namespace Fratak_Shop.Data.Entities.Orders;

/// <summary>Internal timeline of an order (staff notes and status changes). Never shown to the customer.</summary>
public class OrderNote : BaseEntity
{
    public Guid OrderId { get; set; }

    public OrderNoteKind Kind { get; set; }

    public Guid? AuthorUserId { get; set; }

    [MaxLength(150)]
    public string? AuthorName { get; set; }

    [Required]
    [MaxLength(2000)]
    public string Content { get; set; } = null!;

    public Order Order { get; set; } = null!;
}

public enum OrderNoteKind
{
    None = 0,
    Note = 1,
    StatusChange = 2,
    System = 3,
}

/// <summary>
/// Invoice of the purchase from the supplier that fulfils an order. Required before the order can move to
/// "processing"; the file is stored privately (App_Data) and is only reachable from the admin panel.
/// </summary>
public class PurchaseInvoice : BaseEntity
{
    public Guid OrderId { get; set; }

    public Guid? SupplierId { get; set; }

    [MaxLength(150)]
    public string? SupplierName { get; set; }

    /// <summary>Order / invoice number at the supplier.</summary>
    [MaxLength(100)]
    public string? InvoiceNumber { get; set; }

    /// <summary>What the shop paid (Toman).</summary>
    public decimal Amount { get; set; }

    public DateTimeOffset? PurchasedAt { get; set; }

    [Required]
    [MaxLength(100)]
    public string FileName { get; set; } = null!;

    [MaxLength(100)]
    public string ContentType { get; set; } = "application/octet-stream";

    public int SizeBytes { get; set; }

    [MaxLength(1000)]
    public string? Notes { get; set; }

    public Guid? UploadedByUserId { get; set; }

    [MaxLength(150)]
    public string? UploadedByName { get; set; }

    public Order Order { get; set; } = null!;

    public Supplier? Supplier { get; set; }
}
