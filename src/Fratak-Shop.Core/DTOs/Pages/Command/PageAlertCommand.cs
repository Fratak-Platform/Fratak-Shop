using Fratak_Shop.Data.Entities.Pages;

namespace Fratak_Shop.Core.DTOs.Pages.Command;

/// <summary>
/// دستور ایجاد هشدار صفحه (اعلان/نوتیفیکیشن قابل نمایش در موقعیت مشخص یک صفحه).
/// </summary>
public class CreatePageAlertCommand
{
    public SitePage Page { get; set; }
    public AlertPosition Position { get; set; }
    public AlertType Type { get; set; }
    public string? Title { get; set; }
    public string Content { get; set; } = null!;
    public string? LinkUrl { get; set; }
    public string? LinkText { get; set; }
    public bool IsDismissible { get; set; }
    public AlertAudience Audience { get; set; }
    public Guid? ProductId { get; set; }
    public Guid? CategoryId { get; set; }
    public int DisplayOrder { get; set; }
    public DateTimeOffset? StartAt { get; set; }
    public DateTimeOffset? EndAt { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>
/// دستور ویرایش هشدار صفحه.
/// </summary>
public class EditPageAlertCommand
{
    public Guid Id { get; set; }
    public SitePage Page { get; set; }
    public AlertPosition Position { get; set; }
    public AlertType Type { get; set; }
    public string? Title { get; set; }
    public string Content { get; set; } = null!;
    public string? LinkUrl { get; set; }
    public string? LinkText { get; set; }
    public bool IsDismissible { get; set; }
    public AlertAudience Audience { get; set; }
    public Guid? ProductId { get; set; }
    public Guid? CategoryId { get; set; }
    public int DisplayOrder { get; set; }
    public DateTimeOffset? StartAt { get; set; }
    public DateTimeOffset? EndAt { get; set; }
    public bool IsActive { get; set; } = true;
}
