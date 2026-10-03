using Fratak_Shop.Core.DTOs.Payments.Query;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Entities.Payments;

namespace Fratak_Shop.Core.Services.Payments;

/// <summary>
/// Online payment of the shop. In development the active provider is the built-in
/// <see cref="PaymentGatewayProvider.Sandbox"/> gateway, so the whole checkout →
/// gateway → callback → verification cycle can be demonstrated without a real bank
/// account. Switching to a real provider only means implementing its request and
/// verify calls here, because the rest of the flow is already provider-agnostic.
/// </summary>
public interface IPaymentService
{
    /// <summary>درگاه فعال سایت (سندباکس تا زمان اتصال درگاه واقعی).</summary>
    Task<PaymentGatewayProvider> GetActiveProviderAsync(CancellationToken cancellationToken = default);

    Task<bool> IsOnlinePaymentEnabledAsync(CancellationToken cancellationToken = default);

    /// <summary>شروع پرداخت آنلاین سفارش و ساخت تراکنش.</summary>
    Task<OperationResult<PaymentStartResult>> StartAsync(Guid orderId, Guid? userId, string? ipAddress,
        CancellationToken cancellationToken = default);

    /// <summary>پردازش بازگشت از درگاه (موفق یا ناموفق) و به‌روزرسانی سفارش.</summary>
    Task<OperationResult<PaymentResultDto>> HandleCallbackAsync(string authority, bool success,
        CancellationToken cancellationToken = default);

    /// <summary>تأیید دستی پرداخت توسط کارشناس (واریز کارت به کارت، پرداخت در محل، اصلاح اشتباه).</summary>
    Task<OperationResult> MarkAsPaidAsync(Guid orderId, string? referenceId, Guid? userId,
        CancellationToken cancellationToken = default);

    Task<List<PaymentTransactionDto>> GetForOrderAsync(Guid orderId, CancellationToken cancellationToken = default);

    Task<PaymentTransactionDto?> GetLatestAsync(Guid orderId, CancellationToken cancellationToken = default);

    Task<PaymentTransaction?> GetByAuthorityAsync(string authority, CancellationToken cancellationToken = default);
}
