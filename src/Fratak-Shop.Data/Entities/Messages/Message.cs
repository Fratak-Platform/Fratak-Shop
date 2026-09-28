using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Fratak_Shop.Data.Entities.Users;
using Fratak_Shop.Data.Utilities;

namespace Fratak_Shop.Data.Entities.Messages;

public class Message : BaseEntity
{
    public Guid? UserId { get; set; }

    public MessageType Type { get; set; }

    [MaxLength(250)]
    public string? FullName { get; set; }

    [MaxLength(20)]
    public string? PhoneNumber { get; set; }

    [MaxLength(255)]
    public string? Email { get; set; }

    [MaxLength(250)]
    public string? Subject { get; set; }

    [Required]
    public string Content { get; set; } = null!;

    public MessageStatus Status { get; set; }

    public bool IsRead { get; set; }

    public DateTimeOffset? ReadAt { get; set; }

    public DateTimeOffset? RepliedAt { get; set; }

    [ForeignKey(nameof(UserId))]
    public User? User { get; set; }
}

public enum MessageType
{
    None = 0,
    Contact = 1,
    Support = 2,
    Suggestion = 3,
    Complaint = 4,
}

public enum MessageStatus
{
    None = 0,
    New = 1,
    InProgress = 2,
    Answered = 3,
    Closed = 4,
}
