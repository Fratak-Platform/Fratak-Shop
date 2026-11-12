using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Entities.Wallets;

namespace Fratak_Shop.Core.DTOs.Wallets;

/// <summary>A wallet movement to apply (credit or debit).</summary>
public class WalletEntry
{
    public Guid UserId { get; set; }
    public decimal Amount { get; set; }
    public WalletTransactionType Type { get; set; }
    public string? Description { get; set; }
    public Guid? OrderId { get; set; }
    public Guid? PaymentTransactionId { get; set; }
    public Guid? WithdrawalRequestId { get; set; }

    /// <summary>
    /// Credits only: part of <see cref="Amount"/> that is promotional credit (club reward,
    /// gift, refund of promotional credit). It can be spent on orders but never withdrawn.
    /// </summary>
    public decimal NonWithdrawableAmount { get; set; }

    /// <summary>Debits only: which part of the balance is used.</summary>
    public WalletDebitSource DebitSource { get; set; } = WalletDebitSource.PromotionalFirst;
}

/// <summary>Order in which the two parts of the balance are used by a debit.</summary>
public enum WalletDebitSource
{
    /// <summary>Purchases: promotional credit is spent before the customer's own money.</summary>
    PromotionalFirst = 0,

    /// <summary>Withdrawals: only the customer's own (withdrawable) money.</summary>
    WithdrawableOnly = 1,

    /// <summary>Corrections of real money: own money first, then promotional credit.</summary>
    WithdrawableFirst = 2,
}

/// <summary>Result of a wallet movement.</summary>
public record WalletMovement(decimal Balance, decimal BonusBalance, decimal BonusChange)
{
    public decimal WithdrawableBalance => Math.Max(0, Balance - BonusBalance);
}

public class WalletTransactionDto : BaseDto
{
    public Guid UserId { get; set; }
    public WalletTransactionType Type { get; set; }

    /// <summary>Signed amount: positive for credits, negative for debits.</summary>
    public decimal Amount { get; set; }

    public decimal BalanceAfter { get; set; }

    /// <summary>Signed change of the non-withdrawable part.</summary>
    public decimal BonusAmount { get; set; }

    public decimal BonusBalanceAfter { get; set; }
    public string? Description { get; set; }
    public Guid? OrderId { get; set; }
    public string? OrderNumber { get; set; }

    public bool IsCredit => Amount > 0;
}

public class WalletTransactionFilterParams : BaseFilterParam
{
    public Guid? UserId { get; set; }
    public WalletTransactionType? Type { get; set; }
}

public class WalletTransactionFilterResult : BaseFilter<WalletTransactionDto, WalletTransactionFilterParams>;

public class WithdrawalRequestDto : BaseDto
{
    public Guid UserId { get; set; }
    public string? UserPhoneNumber { get; set; }
    public string? UserFullName { get; set; }
    public decimal Amount { get; set; }
    public string? Iban { get; set; }
    public string? CardNumber { get; set; }
    public string? AccountNumber { get; set; }
    public string AccountHolder { get; set; } = null!;
    public WithdrawalStatus Status { get; set; }
    public string? AdminNote { get; set; }
    public string? BankTrackingNumber { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }

    public bool CanBeCancelled => Status == WithdrawalStatus.Pending;

    /// <summary>IBAN with the middle digits hidden, for lists.</summary>
    public string? MaskedIban => Iban is { Length: > 10 } ? $"{Iban[..6]}••••••••{Iban[^4..]}" : Iban;

    public string? MaskedCard => string.IsNullOrEmpty(CardNumber) ? null : BankAccountValidator.MaskCard(CardNumber);

    /// <summary>Short destination text for lists (masked).</summary>
    public string Destination => MaskedIban ?? MaskedCard ?? "-";
}

public class WithdrawalFilterParams : BaseFilterParam
{
    public Guid? UserId { get; set; }
    public WithdrawalStatus? Status { get; set; }
}

public class WithdrawalFilterResult : BaseFilter<WithdrawalRequestDto, WithdrawalFilterParams>;

public class CreateWithdrawalCommand
{
    public Guid UserId { get; set; }
    public decimal Amount { get; set; }

    /// <summary>Full name of the account owner (required).</summary>
    public string AccountHolder { get; set; } = null!;

    /// <summary>Card number or IBAN is required (both may be given).</summary>
    public string? CardNumber { get; set; }

    public string? Iban { get; set; }

    public string? AccountNumber { get; set; }
}

public class ChangeWithdrawalStatusCommand
{
    public Guid Id { get; set; }
    public WithdrawalStatus Status { get; set; }
    public string? AdminNote { get; set; }
    public string? BankTrackingNumber { get; set; }
}

public class WalletSummaryDto
{
    public decimal Balance { get; set; }

    /// <summary>Promotional credit: spendable on orders, not withdrawable.</summary>
    public decimal BonusBalance { get; set; }

    /// <summary>The customer's own money that can be withdrawn.</summary>
    public decimal WithdrawableBalance => Math.Max(0, Balance - BonusBalance);

    public decimal PendingWithdrawals { get; set; }
    public decimal TotalCredits { get; set; }
    public decimal TotalDebits { get; set; }
}
