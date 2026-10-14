using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Fratak_Shop.Data.Entities.Products;
using Fratak_Shop.Data.Utilities;

namespace Fratak_Shop.Data.Entities.Orders;

/// <summary>
/// Snapshot line of an order. Name, SKU and prices are copied at purchase time
/// so the history is preserved even when the product later changes or is removed.
/// </summary>
public class OrderItem : BaseEntity
{
    public Guid OrderId { get; set; }

    public Guid? ProductId { get; set; }

    [Required]
    [MaxLength(250)]
    public string ProductName { get; set; } = null!;

    [Required]
    [MaxLength(100)]
    public string SKU { get; set; } = null!;

    public decimal UnitPrice { get; set; }

    public decimal DiscountAmount { get; set; }

    public int Quantity { get; set; }

    public decimal TotalAmount { get; set; }

    [ForeignKey(nameof(OrderId))]
    public Order Order { get; set; } = null!;

    [ForeignKey(nameof(ProductId))]
    public Product? Product { get; set; }
}
