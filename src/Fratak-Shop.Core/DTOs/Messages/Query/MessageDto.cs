using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Entities.Messages;

namespace Fratak_Shop.Core.DTOs.Messages.Query;

/// <summary>
/// پیام دریافتی همراه متن کامل.
/// </summary>
public class MessageDto : BaseDto
{
    public Guid? UserId { get; set; }
    public string? UserName { get; set; }

    public MessageType Type { get; set; }
    public MessageStatus Status { get; set; }

    public string? FullName { get; set; }
    public string? PhoneNumber { get; set; }
    public string? Email { get; set; }
    public string? Subject { get; set; }
    public string Content { get; set; } = null!;

    public bool IsRead { get; set; }
    public DateTimeOffset? ReadAt { get; set; }
    public DateTimeOffset? RepliedAt { get; set; }
}
