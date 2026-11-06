using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Entities.Pages;

namespace Fratak_Shop.Core.DTOs.Pages.Query;

/// <summary>
/// هشدار قابل نمایش در یک صفحه و موقعیت مشخص.
/// </summary>
public class PageAlertDto : BaseDto
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
    public bool IsActive { get; set; }
}

/// <summary>Who is looking at which page – used to pick the matching alerts.</summary>
public record PageAlertContext(SitePage Page, bool IsSignedIn, Guid? ProductId = null, Guid? CategoryId = null);
