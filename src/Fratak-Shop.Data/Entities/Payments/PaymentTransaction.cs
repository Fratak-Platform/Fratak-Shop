using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Fratak_Shop.Data.Entities.Orders;
using Fratak_Shop.Data.Entities.Users;
using Fratak_Shop.Data.Utilities;

namespace Fratak_Shop.Data.Entities.Payments;

/// <summary>
/// One online payment attempt at a gateway (order payment or wallet top-up).
/// The gateway authority links the callback to this record; the amount is always
/// taken from here – never from the callback – when verifying.
/// </summary>
public class PaymentTransaction : BaseEntity
{
    public Guid? UserId { get; set; }

    public Guid? OrderId { get; set; }

    public PaymentPurpose Purpose { get; set; }

    [Required]
    [MaxLength(50)]
    public string Gateway { get; set; } = null!;

    /// <summary>Amount in Toman.</summary>
    public decimal Amount { get; set; }

    [MaxLength(100)]
    public string? Authority { get; set; }

    [MaxLength(100)]
    public string? RefId { get; set; }

    [MaxLength(50)]
    public string? CardPan { get; set; }

    public PaymentTransactionStatus Status { get; set; } = PaymentTransactionStatus.Pending;

    public int? ResultCode { get; set; }

    [MaxLength(500)]
    public string? Message { get; set; }

    public DateTimeOffset? VerifiedAt { get; set; }

    [ForeignKey(nameof(UserId))]
    public User? User { get; set; }

    [ForeignKey(nameof(OrderId))]
    public Order? Order { get; set; }
}

public enum PaymentPurpose
{
    None = 0,
    OrderPayment = 1,
    WalletTopUp = 2,
}

public enum PaymentTransactionStatus
{
    None = 0,
    Pending = 1,     // ارسال به درگاه
    Verifying = 2,   // در حال تایید (قفل در برابر callback تکراری)
    Succeeded = 3,
    Failed = 4,
    Cancelled = 5,
}
