using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Fratak_Shop.Data.Entities.Orders;
using Fratak_Shop.Data.Entities.Users;
using Fratak_Shop.Data.Utilities;

namespace Fratak_Shop.Data.Entities.Payments;

/// <summary>
/// Internal «کارت به کارت» payment: the customer transfers the amount to one of
/// the published <see cref="PaymentCard"/>s, registers the transfer details and
/// uploads the receipt file(s). An admin then approves or rejects it; approving
/// marks the related order as paid and notifies the customer.
/// </summary>
public class BankTransferPayment : BaseEntity
{
    public Guid OrderId { get; set; }

    /// <summary>
    /// Card the customer transferred to (nullable in case the card is removed later).
    /// </summary>
    public Guid? PaymentCardId { get; set; }

    public decimal Amount { get; set; }

    [Required]
    [MaxLength(200)]
    public string PayerName { get; set; } = null!;

    /// <summary>
    /// Card number of the customer (16 digits, digits only).
    /// </summary>
    [Required]
    [MaxLength(20)]
    public string PayerCardNumber { get; set; } = null!;

    [MaxLength(100)]
    public string? PayerBankName { get; set; }

    /// <summary>
    /// Date the transfer was made, as declared by the customer.
    /// </summary>
    public DateTimeOffset? TransferDate { get; set; }

    /// <summary>
    /// Bank tracking number (شماره پیگیری) declared by the customer.
    /// </summary>
    [MaxLength(100)]
    public string? TrackingNumber { get; set; }

    [MaxLength(1000)]
    public string? CustomerNote { get; set; }

    public BankTransferStatus Status { get; set; }

    public Guid? ReviewedByUserId { get; set; }

    public DateTimeOffset? ReviewedAt { get; set; }

    [MaxLength(500)]
    public string? RejectReason { get; set; }

    [MaxLength(1000)]
    public string? AdminNote { get; set; }

    /// <summary>
    /// Moment the customer submitted the transfer information (used for statistics).
    /// </summary>
    public DateTimeOffset? SubmittedAt { get; set; }

    [ForeignKey(nameof(OrderId))]
    public Order Order { get; set; } = null!;

    [ForeignKey(nameof(PaymentCardId))]
    public PaymentCard? PaymentCard { get; set; }

    [ForeignKey(nameof(ReviewedByUserId))]
    public User? ReviewedByUser { get; set; }

    public List<BankTransferReceipt> Receipts { get; set; } = [];
}

/// <summary>
/// Uploaded receipt / transfer slip of a bank transfer payment
/// (شماره کارت، فیش یا اسکرین‌شات کارت به کارت).
/// </summary>
public class BankTransferReceipt : BaseEntity
{
    public Guid BankTransferPaymentId { get; set; }

    /// <summary>
    /// Stored file name inside <c>wwwroot/uploads/bank-transfers</c>.
    /// </summary>
    [Required]
    [MaxLength(250)]
    public string FileName { get; set; } = null!;

    /// <summary>
    /// Original file name of the uploaded receipt.
    /// </summary>
    [MaxLength(250)]
    public string? OriginalName { get; set; }

    [MaxLength(100)]
    public string? ContentType { get; set; }

    public long SizeBytes { get; set; }

    [ForeignKey(nameof(BankTransferPaymentId))]
    public BankTransferPayment BankTransferPayment { get; set; } = null!;
}

public enum BankTransferStatus
{
    None = 0,
    /// <summary>در انتظار ثبت اطلاعات واریز توسط مشتری</summary>
    AwaitingCustomerInfo = 1,
    /// <summary>در انتظار بررسی توسط ادمین</summary>
    AwaitingReview = 2,
    /// <summary>تأیید شده و سفارش پرداخت‌شده</summary>
    Approved = 3,
    /// <summary>رد شده (فیش نامعتبر یا عدم تطابق مبلغ)</summary>
    Rejected = 4,
    /// <summary>منقضی شده و سفارش لغو شده است</summary>
    Expired = 5,
}
