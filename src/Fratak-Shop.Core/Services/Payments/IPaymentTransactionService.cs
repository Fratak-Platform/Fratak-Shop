using Fratak_Shop.Core.DTOs.Payments;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Entities.Payments;

namespace Fratak_Shop.Core.Services.Payments;

/// <summary>
/// Persistence and state machine of gateway payments:
/// Pending → (Verifying) → Succeeded | Failed | Cancelled.
/// Every transition is an atomic conditional update, so repeated or parallel
/// gateway callbacks can never apply a payment twice.
/// </summary>
public interface IPaymentTransactionService
{
    Task<OperationResult<Guid>> CreateAsync(CreatePaymentCommand command);
    Task SetAuthorityAsync(Guid id, string authority);
    Task<PaymentTransactionDto?> GetByIdAsync(Guid id);
    Task<PaymentTransactionDto?> GetByAuthorityAsync(string authority);

    /// <summary>Locks a pending transaction for verification; false when another request already did.</summary>
    Task<bool> TryBeginVerifyAsync(Guid id);

    /// <summary>Returns a transaction to pending when the verification call could not be completed (network).</summary>
    Task ReleaseVerifyAsync(Guid id, string message);

    /// <summary>Gateway rejected or the customer cancelled the payment.</summary>
    Task MarkFailedAsync(Guid id, PaymentTransactionStatus status, int? code, string message);

    /// <summary>Applies a verified payment to its order or wallet top-up.</summary>
    Task<PaymentCompletionResult> CompleteAsync(Guid id, string refId, string? cardPan, int code);

    Task<List<PaymentTransactionDto>> GetByOrderAsync(Guid orderId);
    Task<List<PaymentTransactionDto>> GetByUserAsync(Guid userId, int take = 50);

    /// <summary>
    /// دور ۲۸ (#۸۵ – «تطبیق پرداخت‌های بی‌پاسخ»): تراکنش‌هایی که مشتری پرداخت کرده ولی به سایت برنگشته (مرورگر بسته شد، اینترنت قطع شد).
    /// <para>اول قفل‌های «در حال تأیید» که بیش از <paramref name="stuckBefore"/> مانده‌اند (سرور وسط کار خاموش شده) آزاد می‌شوند؛</para>
    /// <para>بعد تراکنش‌های «منتظر» با کد درگاه که بین <paramref name="newerThan"/> و <paramref name="olderThan"/> ساخته شده‌اند برمی‌گردند (قدیمی‌ترین اول).</para>
    /// </summary>
    Task<List<PaymentTransactionDto>> GetReconcileCandidatesAsync(DateTimeOffset olderThan, DateTimeOffset newerThan, DateTimeOffset stuckBefore, int take = 20);
}
