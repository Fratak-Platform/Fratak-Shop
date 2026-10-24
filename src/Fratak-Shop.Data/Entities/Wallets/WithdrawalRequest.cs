using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Fratak_Shop.Data.Entities.Users;
using Fratak_Shop.Data.Utilities;

namespace Fratak_Shop.Data.Entities.Wallets;

/// <summary>
/// Customer request to transfer wallet balance to a bank account (IBAN). The
/// amount is held (debited) when the request is created and returned to the
/// wallet if the request is rejected or cancelled.
/// </summary>
public class WithdrawalRequest : BaseEntity
{
    public Guid UserId { get; set; }

    public decimal Amount { get; set; }

    /// <summary>IBAN (شبا) in the IR + 24 digits format.</summary>
    [Required]
    [MaxLength(26)]
    public string Iban { get; set; } = null!;

    [Required]
    [MaxLength(200)]
    public string AccountHolder { get; set; } = null!;

    public WithdrawalStatus Status { get; set; } = WithdrawalStatus.Pending;

    [MaxLength(1000)]
    public string? AdminNote { get; set; }

    [MaxLength(100)]
    public string? BankTrackingNumber { get; set; }

    public DateTimeOffset? ProcessedAt { get; set; }

    [ForeignKey(nameof(UserId))]
    public User User { get; set; } = null!;
}

public enum WithdrawalStatus
{
    None = 0,
    Pending = 1,     // در انتظار بررسی
    Approved = 2,    // تایید شده (در صف پرداخت)
    Paid = 3,        // واریز شده
    Rejected = 4,    // رد شده
    Cancelled = 5,   // لغو توسط کاربر
}
