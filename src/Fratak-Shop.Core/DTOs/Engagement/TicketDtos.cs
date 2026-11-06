using Fratak_Shop.Data.Entities.Support;

namespace Fratak_Shop.Core.DTOs.Engagement;

public sealed record SupportDepartmentDto(Guid Id, string Title, string? Description);

public sealed record TicketAttachmentInput(string FileName, string ContentType, int SizeBytes);

public sealed class CreateTicketCommand
{
    public Guid UserId { get; set; }
    public Guid DepartmentId { get; set; }
    public string? OrderNumber { get; set; }
    public string Subject { get; set; } = null!;
    public string Content { get; set; } = null!;
    public TicketPriority Priority { get; set; } = TicketPriority.Normal;
    public List<TicketAttachmentInput> Attachments { get; set; } = [];
}

public sealed class TicketSummaryDto
{
    public Guid Id { get; init; }
    public string TicketNumber { get; init; } = null!;
    public string Subject { get; init; } = null!;
    public string DepartmentTitle { get; init; } = null!;
    public TicketStatus Status { get; init; }
    public TicketPriority Priority { get; init; }
    public bool HasUnreadReply { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset LastActivityAt { get; init; }
    public string? OrderNumber { get; init; }
}

public sealed class TicketDetailDto
{
    public TicketSummaryDto Ticket { get; init; } = null!;
    public IReadOnlyList<TicketMessageDto> Messages { get; init; } = [];
    public bool CanReply => Ticket.Status != TicketStatus.Closed || Ticket.LastActivityAt > DateTimeOffset.UtcNow.AddDays(-30);
    public bool CanClose => Ticket.Status != TicketStatus.Closed;
}

public sealed record TicketMessageDto(Guid Id, bool IsStaff, string AuthorName, string Content, DateTimeOffset CreatedAt, IReadOnlyList<TicketAttachmentDto> Attachments);

public sealed record TicketAttachmentDto(string FileName, string ContentType, int SizeBytes);
