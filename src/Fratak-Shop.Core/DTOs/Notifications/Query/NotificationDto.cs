using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Entities.Notifications;

namespace Fratak_Shop.Core.DTOs.Notifications.Query;

public class NotificationDto : BaseDto
{
    public Guid? UserId { get; set; }

    public string Title { get; set; } = null!;

    public string? Message { get; set; }

    public NotificationType Type { get; set; }

    public NotificationChannel Channel { get; set; }

    public string? Url { get; set; }

    public bool IsRead { get; set; }

    public DateTimeOffset? ReadAt { get; set; }

    public bool IsBroadcast { get; set; }

    public string? UserFullName { get; set; }

    public string? UserPhoneNumber { get; set; }
}

public class NotificationFilterParams : BaseFilterParam
{
    public string? Search { get; set; }

    public NotificationType? Type { get; set; }

    public bool? IsRead { get; set; }

    public bool? IsBroadcast { get; set; }
}

public class NotificationFilterResult : BaseFilter<NotificationDto, NotificationFilterParams>;
