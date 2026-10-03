using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Entities.Payments;

namespace Fratak_Shop.Core.DTOs.Payments.Query;

/// <summary>اطلاعات یک تراکنش پرداخت برای صفحه نتیجه و پنل مدیریت.</summary>
public class PaymentTransactionDto : BaseDto
{
    public Guid OrderId { get; set; }
    public string? OrderNumber { get; set; }
    public PaymentGatewayProvider Gateway { get; set; }
    public decimal Amount { get; set; }
    public PaymentTransactionStatus Status { get; set; }
    public string? Authority { get; set; }
    public string? ReferenceId { get; set; }
    public string? CardMask { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTimeOffset? PaidAt { get; set; }
    public DateTimeOffset? VerifiedAt { get; set; }

    public bool IsSuccessful => Status == PaymentTransactionStatus.Succeeded;
}

/// <summary>خروجی شروع پرداخت: آدرس درگاه و شناسه تراکنش.</summary>
public class PaymentStartResult
{
    public Guid TransactionId { get; set; }
    public Guid OrderId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public PaymentGatewayProvider Gateway { get; set; }
    public string RedirectUrl { get; set; } = string.Empty;
    public bool IsSandbox => Gateway == PaymentGatewayProvider.Sandbox;
}

/// <summary>نتیجه نهایی پرداخت که در صفحه نتیجه نمایش داده می‌شود.</summary>
public class PaymentResultDto
{
    public bool IsSuccessful { get; set; }
    public Guid OrderId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string? ReferenceId { get; set; }
    public string Message { get; set; } = string.Empty;
}

/// <summary>یک کارت مقصد «کارت به کارت» به‌همراه اطلاعات نمایشی.</summary>
public class PaymentCardDto : BaseDto
{
    public string BankName { get; set; } = null!;
    public string CardNumber { get; set; } = null!;
    public string AccountHolder { get; set; } = null!;
    public string? ShebaNumber { get; set; }
    public string? BranchName { get; set; }
    public string? Instructions { get; set; }
    public bool IsDefault { get; set; }
    public int DisplayOrder { get; set; }
    public bool IsActive { get; set; }

    /// <summary>شماره کارت گروه‌بندی‌شده برای نمایش: ۶۰۳۷-۹۹۷۵-... </summary>
    public string FormattedCardNumber => string.Join("-", Enumerable.Range(0, (CardNumber.Length + 3) / 4)
        .Select(index => CardNumber.Substring(index * 4, Math.Min(4, CardNumber.Length - index * 4))));

    /// <summary>شماره کارت با ستاره برای لاگ و پیام‌ها.</summary>
    public string MaskedCardNumber => CardNumber.Length < 8
        ? CardNumber
        : $"{CardNumber[..4]}-****-****-{CardNumber[^4..]}";
}

public class CardToCardInstructionsDto
{
    public List<PaymentCardDto> Cards { get; set; } = [];
    public string? Instructions { get; set; }
    public int DeadlineHours { get; set; } = 24;
    public string DeadlineTitle => PriceHelper.ToPersianDigits(DeadlineHours.ToString());
}

/// <summary>واریز کارت به کارت از دید مشتری و کارشناس پشتیبانی.</summary>
public class BankTransferDetailDto : BaseDto
{
    public Guid OrderId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerPhone { get; set; } = string.Empty;
    public decimal OrderPayableAmount { get; set; }
    public decimal Amount { get; set; }
    public BankTransferStatus Status { get; set; }
    public string PayerName { get; set; } = string.Empty;
    public string PayerCardNumber { get; set; } = string.Empty;
    public string? PayerBankName { get; set; }
    public string? TrackingNumber { get; set; }
    public DateTimeOffset? TransferDate { get; set; }
    public DateTimeOffset? SubmittedAt { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
    public string? RejectReason { get; set; }
    public string? AdminNote { get; set; }
    public string? CustomerNote { get; set; }
    public string? CardNumber { get; set; }
    public string? BankName { get; set; }
    public List<string> ReceiptFiles { get; set; } = [];

    public bool CanSubmit => Status is BankTransferStatus.None or BankTransferStatus.AwaitingCustomerInfo
        or BankTransferStatus.Rejected;

    public string StatusTitle => Status switch
    {
        BankTransferStatus.AwaitingCustomerInfo => "در انتظار ثبت اطلاعات واریز",
        BankTransferStatus.AwaitingReview => "در انتظار تأیید کارشناس",
        BankTransferStatus.Approved => "تأییدشده",
        BankTransferStatus.Rejected => "ردشده",
        BankTransferStatus.Expired => "منقضی‌شده",
        _ => "نامشخص",
    };
}

public class BankTransferFilterParams : BaseFilterParam
{
    public string? Search { get; set; }
    public BankTransferStatus? Status { get; set; }
}

public class BankTransferFilterResult : BaseFilter<BankTransferDetailDto, BankTransferFilterParams>;
