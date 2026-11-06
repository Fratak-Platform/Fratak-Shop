using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Fratak_Shop.Data.Entities.Payments;
using Fratak_Shop.Data.Entities.Users;
using Fratak_Shop.Data.Utilities;

namespace Fratak_Shop.Data.Entities.Marketing;

/// <summary>
/// Purchasable gift card. The buyer pays (online gateway or own wallet money) and gets a
/// secret code to give to anyone; whoever redeems it gets the amount in their wallet.
/// Only the SHA-256 hash of the code is stored for lookups; <see cref="CodeLast4"/> is
/// shown in lists. The full code is shown to the buyer from <see cref="ProtectedCode"/>
/// (encrypted with the application's data-protection key).
/// </summary>
public class GiftCard : BaseEntity
{
    /// <summary>Public reference shown in lists and support (GC-XXXXXX).</summary>
    [Required]
    [MaxLength(20)]
    public string Reference { get; set; } = null!;

    /// <summary>SHA-256 (hex) of the normalized code.</summary>
    [Required]
    [MaxLength(64)]
    public string CodeHash { get; set; } = null!;

    [Required]
    [MaxLength(4)]
    public string CodeLast4 { get; set; } = null!;

    /// <summary>Code encrypted for the buyer (data protection); null after the card is cancelled.</summary>
    [MaxLength(1000)]
    public string? ProtectedCode { get; set; }

    public decimal Amount { get; set; }

    public GiftCardStatus Status { get; set; } = GiftCardStatus.PendingPayment;

    public Guid PurchaserUserId { get; set; }

    public GiftCardPaymentMethod PaymentMethod { get; set; }

    public Guid? PaymentTransactionId { get; set; }

    [MaxLength(100)]
    public string? RecipientName { get; set; }

    [MaxLength(500)]
    public string? Message { get; set; }

    public DateTimeOffset? ActivatedAt { get; set; }

    public DateTimeOffset? ExpiresAt { get; set; }

    public Guid? RedeemedByUserId { get; set; }

    public DateTimeOffset? RedeemedAt { get; set; }

    /// <summary>Snapshot of the setting when redeemed: the credit is withdrawable money (false = purchase only).</summary>
    public bool RedeemedAsWithdrawable { get; set; }

    [ForeignKey(nameof(PurchaserUserId))]
    public User Purchaser { get; set; } = null!;

    [ForeignKey(nameof(RedeemedByUserId))]
    public User? RedeemedBy { get; set; }

    [ForeignKey(nameof(PaymentTransactionId))]
    public PaymentTransaction? PaymentTransaction { get; set; }
}

public enum GiftCardStatus
{
    None = 0,
    PendingPayment = 1,  // در انتظار پرداخت
    Active = 2,          // فعال (قابل استفاده)
    Redeemed = 3,        // استفاده شده
    Cancelled = 4,       // لغو (پرداخت ناموفق / مدیر)
    Expired = 5,         // منقضی
}

public enum GiftCardPaymentMethod
{
    None = 0,
    OnlineGateway = 1,
    Wallet = 2,
}
