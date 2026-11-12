using System.ComponentModel.DataAnnotations.Schema;
using Fratak_Shop.Data.Utilities;

namespace Fratak_Shop.Data.Entities.Products;

/// <summary>
/// Price change log of a product, written automatically whenever the price / sale price
/// / sale window changes (DbContext) and by the daily snapshot when a sale starts or ends.
/// Drives the price history chart of the product page.
/// </summary>
public class ProductPriceHistory : BaseEntity
{
    public Guid ProductId { get; set; }

    /// <summary>List price at that moment.</summary>
    public decimal Price { get; set; }

    /// <summary>Price the customer paid at that moment (sale price applied).</summary>
    public decimal FinalPrice { get; set; }

    public bool IsInStock { get; set; }

    [ForeignKey(nameof(ProductId))]
    public Product Product { get; set; } = null!;
}
