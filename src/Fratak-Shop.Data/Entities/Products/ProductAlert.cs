using System.ComponentModel.DataAnnotations.Schema;
using Fratak_Shop.Data.Entities.Users;
using Fratak_Shop.Data.Utilities;

namespace Fratak_Shop.Data.Entities.Products;

/// <summary>
/// "Notify me" subscription of a customer for a product: back in stock or price drop.
/// Sent once (then deactivated) through the channels of the event's notification template
/// (in-site, push, SMS), so the admin decides how customers are informed.
/// </summary>
public class ProductAlert : BaseEntity
{
    public Guid UserId { get; set; }

    public Guid ProductId { get; set; }

    public ProductAlertType Type { get; set; }

    /// <summary>Final price when the customer subscribed (price-drop alerts fire below it).</summary>
    public decimal PriceAtSubscribe { get; set; }

    /// <summary>Optional desired price: the alert fires when the final price reaches it.</summary>
    public decimal? TargetPrice { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTimeOffset? NotifiedAt { get; set; }

    /// <summary>Final price at the moment of notification.</summary>
    public decimal? NotifiedPrice { get; set; }

    [ForeignKey(nameof(UserId))]
    public User User { get; set; } = null!;

    [ForeignKey(nameof(ProductId))]
    public Product Product { get; set; } = null!;
}

public enum ProductAlertType
{
    None = 0,
    BackInStock = 1,   // موجود شد خبرم کن
    PriceDrop = 2,     // کاهش قیمت خبرم کن
}
