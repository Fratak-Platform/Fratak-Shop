using Fratak_Shop.Core.DTOs.Wallets;
using Fratak_Shop.Core.Utilities;

namespace Fratak_Shop.Core.Services.Wallets;

/// <summary>
/// Customer wallet: an append-only ledger (<c>WalletTransactions</c>) plus the cached
/// balance on the user row. The balance has two parts: the customer's own money
/// (withdrawable) and promotional credit (club rewards, gifts – purchases only). Balance changes are atomic SQL updates, so concurrent
/// requests can never spend the same money twice or push the balance below zero.
/// </summary>
public interface IWalletService
{
    Task<decimal> GetBalanceAsync(Guid userId);
    Task<WalletSummaryDto> GetSummaryAsync(Guid userId);

    /// <summary>
    /// Adds money to the wallet. Joins the ambient database transaction when there is one
    /// (pending tracked changes of the caller are saved together with the ledger row).
    /// <see cref="WalletEntry.NonWithdrawableAmount"/> marks promotional credit (never withdrawable).
    /// </summary>
    Task<OperationResult<WalletMovement>> CreditAsync(WalletEntry entry);

    /// <summary>
    /// Takes money from the wallet; fails without side effects when the balance is too low.
    /// <see cref="WalletEntry.DebitSource"/> decides whether promotional credit may be used.
    /// </summary>
    Task<OperationResult<WalletMovement>> DebitAsync(WalletEntry entry);

    Task<WalletTransactionFilterResult> GetTransactionsAsync(WalletTransactionFilterParams filterParams);

    // ── Withdrawals ──
    /// <summary>Creates a withdrawal request and holds (debits) the amount until the admin decides.</summary>
    Task<OperationResult> RequestWithdrawalAsync(CreateWithdrawalCommand command);

    /// <summary>Customer cancels a pending request; the held amount returns to the wallet.</summary>
    Task<OperationResult> CancelWithdrawalAsync(Guid userId, Guid requestId);

    Task<WithdrawalFilterResult> GetWithdrawalsAsync(WithdrawalFilterParams filterParams);

    /// <summary>Admin: approve / mark paid / reject (rejecting returns the amount to the wallet).</summary>
    Task<OperationResult> ChangeWithdrawalStatusAsync(ChangeWithdrawalStatusCommand command);

    /// <summary>
    /// Admin: manual correction of a balance (positive = credit, negative = debit).
    /// <paramref name="promotional"/>: credits are gifts (not withdrawable) and debits use promotional credit first.
    /// </summary>
    Task<OperationResult> AdjustBalanceAsync(Guid userId, decimal amount, string description, bool promotional = true);

    /// <summary>Campaign / gift credit: usable for purchases only, never withdrawable.</summary>
    Task<OperationResult> GrantPromotionalCreditAsync(Guid userId, decimal amount, string description);
}
