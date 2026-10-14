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
    public int DisplayOrder { get; set; }
    public DateTimeOffset? StartAt { get; set; }
    public DateTimeOffset? EndAt { get; set; }
    public bool IsActive { get; set; }
}
