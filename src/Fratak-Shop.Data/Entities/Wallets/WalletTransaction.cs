using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Fratak_Shop.Data.Entities.Users;
using Fratak_Shop.Data.Utilities;

namespace Fratak_Shop.Data.Entities.Wallets;

/// <summary>
/// Immutable ledger row of a customer wallet. <see cref="Amount"/> is signed:
/// credits are positive, debits negative; <see cref="BalanceAfter"/> keeps the
/// running balance so the history can be audited.
/// </summary>
public class WalletTransaction : BaseEntity
{
    public Guid UserId { get; set; }

    public WalletTransactionType Type { get; set; }

    public decimal Amount { get; set; }

    public decimal BalanceAfter { get; set; }

    [MaxLength(500)]
    public string? Description { get; set; }

    public Guid? OrderId { get; set; }

    public Guid? PaymentTransactionId { get; set; }

    public Guid? WithdrawalRequestId { get; set; }

    [ForeignKey(nameof(UserId))]
    public User User { get; set; } = null!;
}

public enum WalletTransactionType
{
    None = 0,
    TopUp = 1,               // شارژ کیف پول
    OrderPayment = 2,        // پرداخت سفارش
    OrderRefund = 3,         // بازگشت وجه سفارش
    Withdrawal = 4,          // برداشت
    WithdrawalReversal = 5,  // برگشت درخواست برداشت
    AdminCredit = 6,         // افزایش توسط مدیر
    AdminDebit = 7,          // کاهش توسط مدیر
}
