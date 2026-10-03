using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Fratak_Shop.Data.Entities.Users;
using Fratak_Shop.Data.Utilities;

namespace Fratak_Shop.Data.Entities.Orders;

/// <summary>
/// Audit trail of the order lifecycle. Every status change is appended here so the
/// customer tracking page and the admin order page can show «چه اتفاقی، کِی و توسط کی».
/// </summary>
public class OrderStatusHistory : BaseEntity
{
    public Guid OrderId { get; set; }

    public OrderStatus Status { get; set; }

    public OrderStatus? PreviousStatus { get; set; }

    [MaxLength(1000)]
    public string? Note { get; set; }

    /// <summary>
    /// Who changed the status (null for the customer's own actions in checkout).
    /// </summary>
    public Guid? ChangedByUserId { get; set; }

    /// <summary>
    /// Whether the note may be shown to the customer on the tracking page.
    /// </summary>
    public bool IsCustomerVisible { get; set; } = true;

    [ForeignKey(nameof(OrderId))]
    public Order Order { get; set; } = null!;

    [ForeignKey(nameof(ChangedByUserId))]
    public User? ChangedByUser { get; set; }
}
