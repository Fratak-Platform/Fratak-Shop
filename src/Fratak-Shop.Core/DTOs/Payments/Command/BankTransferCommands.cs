namespace Fratak_Shop.Core.DTOs.Payments.Command;

/// <summary>
/// اطلاعاتی که مشتری پس از واریز کارت به کارت ثبت می‌کند. فایل‌های فیش پیش از
/// فراخوانی این فرمان در لایه وب ذخیره شده و نام آن‌ها اینجا ارسال می‌شود.
/// </summary>
public class SubmitBankTransferCommand
{
    public Guid OrderId { get; set; }
    public Guid? PaymentCardId { get; set; }
    public string PayerName { get; set; } = null!;
    public string PayerCardNumber { get; set; } = null!;
    public string? PayerBankName { get; set; }
    public string? TrackingNumber { get; set; }
    public DateTimeOffset? TransferDate { get; set; }
    public string? CustomerNote { get; set; }

    public List<StoredReceipt> Receipts { get; set; } = [];
}

/// <summary>نام فایل ذخیره‌شده فیش که در دیتابیس ثبت می‌شود.</summary>
public class StoredReceipt
{
    public string FileName { get; set; } = null!;
    public string? OriginalName { get; set; }
    public string? ContentType { get; set; }
    public long SizeBytes { get; set; }
}

/// <summary>تأیید یا رد واریز کارت به کارت توسط کارشناس.</summary>
public class ReviewBankTransferCommand
{
    public Guid PaymentId { get; set; }
    public bool Approve { get; set; }
    public string? RejectReason { get; set; }
    public string? AdminNote { get; set; }
    public Guid? ReviewerUserId { get; set; }
}
