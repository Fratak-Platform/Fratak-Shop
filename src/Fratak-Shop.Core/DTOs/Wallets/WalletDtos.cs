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
}

public class WalletTransactionDto : BaseDto
{
    public Guid UserId { get; set; }
    public WalletTransactionType Type { get; set; }

    /// <summary>Signed amount: positive for credits, negative for debits.</summary>
    public decimal Amount { get; set; }

    public decimal BalanceAfter { get; set; }
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
    public string Iban { get; set; } = null!;
    public string AccountHolder { get; set; } = null!;
    public WithdrawalStatus Status { get; set; }
    public string? AdminNote { get; set; }
    public string? BankTrackingNumber { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }

    public bool CanBeCancelled => Status == WithdrawalStatus.Pending;

    /// <summary>IBAN with the middle digits hidden, for lists.</summary>
    public string MaskedIban => Iban.Length > 10 ? $"{Iban[..6]}••••••••{Iban[^4..]}" : Iban;
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
    public string Iban { get; set; } = null!;
    public string AccountHolder { get; set; } = null!;
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
    public decimal PendingWithdrawals { get; set; }
    public decimal TotalCredits { get; set; }
    public decimal TotalDebits { get; set; }
}
