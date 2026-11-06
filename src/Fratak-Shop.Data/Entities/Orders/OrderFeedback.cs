using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Fratak_Shop.Data.Entities.Users;
using Fratak_Shop.Data.Utilities;

namespace Fratak_Shop.Data.Entities.Orders;

/// <summary>Customer's rating of the shopping experience of a delivered order (one per order).</summary>
public class OrderFeedback : BaseEntity
{
    public Guid OrderId { get; set; }

    public Guid UserId { get; set; }

    /// <summary>1–5</summary>
    public byte OverallRating { get; set; }

    /// <summary>Delivery speed, 1–5.</summary>
    public byte DeliveryRating { get; set; }

    /// <summary>Packaging, 1–5.</summary>
    public byte PackagingRating { get; set; }

    /// <summary>Products matched the description, 1–5.</summary>
    public byte ProductMatchRating { get; set; }

    public bool? WouldRecommend { get; set; }

    [MaxLength(1000)]
    public string? Comment { get; set; }

    [ForeignKey(nameof(OrderId))]
    public Order Order { get; set; } = null!;

    [ForeignKey(nameof(UserId))]
    public User User { get; set; } = null!;
}
