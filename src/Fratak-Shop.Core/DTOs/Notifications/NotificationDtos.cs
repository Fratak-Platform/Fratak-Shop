using Fratak_Shop.Data.Entities.Notifications;

namespace Fratak_Shop.Core.DTOs.Notifications;

public class NotificationDto
{
    public Guid Id { get; set; }
    public NotificationCategory Category { get; set; }
    public string Title { get; set; } = null!;
    public string Body { get; set; } = null!;
    public string? LinkUrl { get; set; }
    public string? LinkText { get; set; }
    public bool IsPinned { get; set; }
    public bool IsBroadcast { get; set; }
    public bool IsRead { get; set; }

    /// <summary>دور ۲۹: مشتری می‌تواند این اعلان را حذف کند (پرچم فرستنده).</summary>
    public bool UserDeletable { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public class NotificationPage
{
    public List<NotificationDto> Items { get; set; } = [];
    public int TotalCount { get; set; }
    public int UnreadCount { get; set; }
    public int PageId { get; set; }
    public int PageCount { get; set; }
}

/// <summary>Personal notification produced from a <see cref="NotificationTemplate"/>.</summary>
public class NotificationRequest
{
    public Guid UserId { get; set; }
    public NotificationEvent Event { get; set; }
    public NotificationCategory Category { get; set; } = NotificationCategory.System;

    /// <summary>Placeholder values, e.g. ["OrderNumber"] = "FS-1001".</summary>
    public Dictionary<string, string?> Values { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public string? LinkUrl { get; set; }

    /// <summary>
    /// Own texts instead of the event template (e.g. the configurable abandoned-cart
    /// reminder steps). Placeholders are rendered the same way.
    /// </summary>
    public NotificationContent? Content { get; set; }

    /// <summary>
    /// دور ۲۹ (#۲۴): مشتری بتواند این اعلان را حذف کند؟ null ← پیش‌فرض: همه به‌جز اعلان‌های امنیتی
    /// (تغییر رمز، ورود از دستگاه تازه و…) که فقط مدیر حذفشان می‌کند. <see cref="DeletableByDefault"/>.
    /// </summary>
    public bool? UserDeletable { get; set; }

    /// <summary>دور ۲۹: پیش‌فرض «قابل حذف بودن» یک دستهٔ اعلان. مثال: Security ← false؛ Order ← true.</summary>
    public static bool DeletableByDefault(NotificationCategory category) => category != NotificationCategory.Security;
}

/// <summary>Texts of a notification that doesn't use a <see cref="NotificationTemplate"/>.</summary>
public class NotificationContent
{
    public string Title { get; set; } = null!;
    public string Body { get; set; } = null!;
    public string? LinkText { get; set; }

    /// <summary>Create the in-site notification.</summary>
    public bool InSite { get; set; } = true;

    /// <summary>SMS text; sent when not empty.</summary>
    public string? SmsText { get; set; }

    /// <summary>Also send a browser push notification to the customer's subscribed devices.</summary>
    public bool Push { get; set; } = true;

    /// <summary>
    /// مرحلهٔ دوازدهم: همین عنوان و متن به ایمیل «تأییدشدهٔ» مشتری هم برود (اگر سرویس ایمیل وصل باشد).
    /// </summary>
    public bool Email { get; set; }
}

/// <summary>Manual broadcast (admin panel): coupon campaigns, news, maintenance notices, ...</summary>
public class CreateBroadcastCommand
{
    public NotificationAudience Audience { get; set; } = NotificationAudience.AllCustomers;
    public Guid? LoyaltyTierId { get; set; }
    public NotificationCategory Category { get; set; } = NotificationCategory.Promotion;
    public string Title { get; set; } = null!;
    public string Body { get; set; } = null!;
    public string? LinkUrl { get; set; }
    public string? LinkText { get; set; }
    public bool IsPinned { get; set; }
    public DateTimeOffset? PublishAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }

    /// <summary>دور ۲۹ (#۲۴): هر مشتری بتواند این پیام همگانی را از فهرست خودش بردارد (برای بقیه می‌ماند).</summary>
    public bool UserDeletable { get; set; } = true;
}
