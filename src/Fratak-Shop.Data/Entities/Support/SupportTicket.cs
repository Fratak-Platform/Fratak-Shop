using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Fratak_Shop.Data.Entities.Orders;
using Fratak_Shop.Data.Entities.Users;
using Fratak_Shop.Data.Utilities;

namespace Fratak_Shop.Data.Entities.Support;

/// <summary>Support department a ticket is sent to (sales, technical, after-sales, ...).</summary>
public class SupportDepartment : BaseEntity
{
    [Required]
    [MaxLength(100)]
    public string Title { get; set; } = null!;

    [MaxLength(300)]
    public string? Description { get; set; }

    public int DisplayOrder { get; set; }

    public bool IsActive { get; set; } = true;
}

/// <summary>Customer support ticket (conversation with the shop), optionally about an order.</summary>
public class SupportTicket : BaseEntity
{
    /// <summary>Public number (TK-XXXXXXX).</summary>
    [Required]
    [MaxLength(20)]
    public string TicketNumber { get; set; } = null!;

    public Guid UserId { get; set; }

    public Guid DepartmentId { get; set; }

    public Guid? OrderId { get; set; }

    [Required]
    [MaxLength(200)]
    public string Subject { get; set; } = null!;

    public TicketStatus Status { get; set; } = TicketStatus.Open;

    public TicketPriority Priority { get; set; } = TicketPriority.Normal;

    /// <summary>Staff member responsible for the ticket (admin panel).</summary>
    public Guid? AssignedToUserId { get; set; }

    public DateTimeOffset LastActivityAt { get; set; }

    public DateTimeOffset? ClosedAt { get; set; }

    /// <summary>The customer hasn't seen the last staff reply yet.</summary>
    public bool HasUnreadReply { get; set; }

    [ForeignKey(nameof(UserId))]
    public User User { get; set; } = null!;

    [ForeignKey(nameof(DepartmentId))]
    public SupportDepartment Department { get; set; } = null!;

    [ForeignKey(nameof(OrderId))]
    public Order? Order { get; set; }

    public List<SupportTicketMessage> Messages { get; set; } = [];
}

public class SupportTicketMessage : BaseEntity
{
    public Guid TicketId { get; set; }

    /// <summary>Author (customer or staff member).</summary>
    public Guid? UserId { get; set; }

    public bool IsStaff { get; set; }

    [Required]
    [MaxLength(100)]
    public string AuthorName { get; set; } = null!;

    [Required]
    [MaxLength(4000)]
    public string Content { get; set; } = null!;

    [ForeignKey(nameof(TicketId))]
    public SupportTicket Ticket { get; set; } = null!;

    [ForeignKey(nameof(UserId))]
    public User? User { get; set; }

    public List<SupportTicketAttachment> Attachments { get; set; } = [];
}

public class SupportTicketAttachment : BaseEntity
{
    public Guid MessageId { get; set; }

    /// <summary>Random file name under the private ticket folder (served only to the owner).</summary>
    [Required]
    [MaxLength(100)]
    public string FileName { get; set; } = null!;

    [Required]
    [MaxLength(50)]
    public string ContentType { get; set; } = null!;

    public int SizeBytes { get; set; }

    [ForeignKey(nameof(MessageId))]
    public SupportTicketMessage Message { get; set; } = null!;
}

public enum TicketStatus
{
    None = 0,
    Open = 1,              // باز – در انتظار پاسخ پشتیبانی
    Answered = 2,          // پاسخ داده شد
    CustomerReplied = 3,   // پاسخ مشتری – در انتظار پشتیبانی
    InProgress = 4,        // در حال بررسی
    Closed = 5,            // بسته
}

public enum TicketPriority
{
    Low = 0,
    Normal = 1,
    High = 2,
}
