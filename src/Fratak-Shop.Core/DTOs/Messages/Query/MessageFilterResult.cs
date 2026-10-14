using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Entities.Messages;

namespace Fratak_Shop.Core.DTOs.Messages.Query;

/// <summary>
/// سطر لیست پیام‌ها (بدون متن کامل پیام تا لیست سبک بماند).
/// </summary>
public class MessageFilterData : BaseDto
{
    public Guid? UserId { get; set; }
    public string? UserName { get; set; }

    public MessageType Type { get; set; }
    public MessageStatus Status { get; set; }

    public string? FullName { get; set; }
    public string? PhoneNumber { get; set; }
    public string? Email { get; set; }
    public string? Subject { get; set; }

    public bool IsRead { get; set; }
    public DateTimeOffset? ReadAt { get; set; }
    public DateTimeOffset? RepliedAt { get; set; }
}

/// <summary>
/// پارامترهای فیلتر و صفحه‌بندی لیست پیام‌ها.
/// </summary>
public class MessageFilterParams : BaseFilterParam
{
    /// <summary>جستجو در نام، شماره تماس، ایمیل و موضوع پیام.</summary>
    public string? Search { get; set; }

    public MessageType? MessageType { get; set; }

    public MessageStatus? Status { get; set; }

    public bool? IsRead { get; set; }

    public Guid? UserId { get; set; }
}

public class MessageFilterResult : BaseFilter<MessageFilterData, MessageFilterParams>;
