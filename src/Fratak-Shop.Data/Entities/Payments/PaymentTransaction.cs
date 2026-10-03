using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Fratak_Shop.Data.Entities.Orders;
using Fratak_Shop.Data.Utilities;

namespace Fratak_Shop.Data.Entities.Payments;

/// <summary>
/// One attempt of an online payment. Every call to the gateway (request, callback,
/// verification) leaves a row here, which makes the payment history auditable
/// without touching the order itself.
/// In development the gateway is the built-in sandbox provider, so the whole flow
/// can be exercised without a real bank account.
/// </summary>
public class PaymentTransaction : BaseEntity
{
    public Guid OrderId { get; set; }

    public PaymentGatewayProvider Gateway { get; set; }

    public decimal Amount { get; set; }

    public PaymentTransactionStatus Status { get; set; }

    /// <summary>
    /// Authority/token returned by the gateway at the beginning of the flow.
    /// </summary>
    [MaxLength(150)]
    public string? Authority { get; set; }

    /// <summary>
    /// Final reference number (شماره پیگیری) returned after a successful verification.
    /// </summary>
    [MaxLength(150)]
    public string? ReferenceId { get; set; }

    /// <summary>
    /// Masked card number reported by the gateway, e.g. 6037-****-****-1234.
    /// </summary>
    [MaxLength(50)]
    public string? CardMask { get; set; }

    /// <summary>
    /// Raw request/response payload kept for support and debugging.
    /// </summary>
    public string? RequestPayload { get; set; }

    public string? VerifyPayload { get; set; }

    [MaxLength(500)]
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Address the customer was redirected to (gateway start page).
    /// </summary>
    [MaxLength(1000)]
    public string? RedirectUrl { get; set; }

    public DateTimeOffset? PaidAt { get; set; }

    public DateTimeOffset? VerifiedAt { get; set; }

    [MaxLength(100)]
    public string? CreatedByIp { get; set; }

    [ForeignKey(nameof(OrderId))]
    public Order Order { get; set; } = null!;
}

public enum PaymentGatewayProvider
{
    None = 0,
    /// <summary>درگاه سندباکس داخلی برای تست کامل فرآیند پرداخت</summary>
    Sandbox = 1,
    Zarinpal = 2,
    Mellat = 3,
    Saman = 4,
    Parsian = 5,
    IdPay = 6,
    NextPay = 7,
    Zibal = 8,
}

public enum PaymentTransactionStatus
{
    None = 0,
    Initiated = 1,
    AwaitingCallback = 2,
    Succeeded = 3,
    Failed = 4,
    Cancelled = 5,
    Refunded = 6,
}
