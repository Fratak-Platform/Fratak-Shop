using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Entities.Payments;

namespace Fratak_Shop.Core.DTOs.Payments;

public class PaymentTransactionDto : BaseDto
{
    public Guid? UserId { get; set; }
    public Guid? OrderId { get; set; }
    public string? OrderNumber { get; set; }
    public PaymentPurpose Purpose { get; set; }
    public string Gateway { get; set; } = null!;

    /// <summary>Amount in Toman.</summary>
    public decimal Amount { get; set; }

    public string? Authority { get; set; }
    public string? RefId { get; set; }
    public string? CardPan { get; set; }
    public PaymentTransactionStatus Status { get; set; }
    public int? ResultCode { get; set; }
    public string? Message { get; set; }
    public DateTimeOffset? VerifiedAt { get; set; }
}

public class CreatePaymentCommand
{
    public PaymentPurpose Purpose { get; set; }
    public Guid? UserId { get; set; }
    public Guid? OrderId { get; set; }
    public decimal Amount { get; set; }
    public string Gateway { get; set; } = null!;
}

public class PaymentCompletionResult
{
    public bool Succeeded { get; set; }
    public PaymentPurpose Purpose { get; set; }
    public string Message { get; set; } = null!;
    public string? OrderNumber { get; set; }
    public string? RefId { get; set; }

    /// <summary>The order could not accept the payment (cancelled/expired) and the money went to the wallet.</summary>
    public bool CreditedToWallet { get; set; }
}
