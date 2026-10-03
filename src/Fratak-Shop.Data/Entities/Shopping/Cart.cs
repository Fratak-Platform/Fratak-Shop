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

    /// <summary>
    /// کد تخفیف اعمال‌شده روی سبد خرید؛ در مرحله تسویه حساب دوباره اعتبارسنجی می‌شود.
    /// </summary>
    [MaxLength(50)]
    public string? CouponCode { get; set; }

    [ForeignKey(nameof(UserId))]
    public User? User { get; set; }

    public List<CartItem> Items { get; set; } = [];
}
