using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Fratak_Shop.Data.Entities.Users;
using Fratak_Shop.Data.Utilities;

namespace Fratak_Shop.Data.Entities.Shopping;

public class Cart : BaseEntity
{
    public Guid? UserId { get; set; }

    /// <summary>
    /// Guest-session key used to keep an anonymous cart between visits.
    /// </summary>
    [MaxLength(100)]
    public string? SessionKey { get; set; }

    /// <summary>Discount code applied to the cart (re-validated at checkout).</summary>
    [MaxLength(50)]
    public string? CouponCode { get; set; }

    /// <summary>Last time an item was added, changed or removed (abandoned-cart detection).</summary>
    public DateTimeOffset? LastActivityAt { get; set; }

    [ForeignKey(nameof(UserId))]
    public User? User { get; set; }

    public List<CartItem> Items { get; set; } = [];

    /// <summary>"Next purchase" list (not part of the cart totals).</summary>
    public List<SavedCartItem> SavedItems { get; set; } = [];
}
