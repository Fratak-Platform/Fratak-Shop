using System.ComponentModel.DataAnnotations.Schema;
using Fratak_Shop.Data.Entities.Products;
using Fratak_Shop.Data.Utilities;

namespace Fratak_Shop.Data.Entities.Shopping;

/// <summary>
/// "Next purchase" list of a cart: items moved out of the cart (not part of totals or
/// checkout) that the customer can move back with one click.
/// </summary>
public class SavedCartItem : BaseEntity
{
    public Guid CartId { get; set; }

    public Guid ProductId { get; set; }

    public int Quantity { get; set; } = 1;

    [ForeignKey(nameof(CartId))]
    public Cart Cart { get; set; } = null!;

    [ForeignKey(nameof(ProductId))]
    public Product Product { get; set; } = null!;
}
