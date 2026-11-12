using System.ComponentModel.DataAnnotations;
using Fratak_Shop.Data.Utilities;

namespace Fratak_Shop.Data.Entities.Pages;

/// <summary>
/// Alert / notice block that can be shown at a chosen position of a chosen page
/// (for example an announcement above the home page or a note under the product
/// images). Both the page and the position are selectable enums.
/// </summary>
public class PageAlert : BaseEntity
{
    public SitePage Page { get; set; }

    public AlertPosition Position { get; set; }

    public AlertType Type { get; set; }

    [MaxLength(250)]
    public string? Title { get; set; }

    [Required]
    public string Content { get; set; } = null!;

    /// <summary>Optional call-to-action link (site-relative or https).</summary>
    [MaxLength(500)]
    public string? LinkUrl { get; set; }

    [MaxLength(100)]
    public string? LinkText { get; set; }

    /// <summary>The visitor can close the alert (remembered in the browser).</summary>
    public bool IsDismissible { get; set; }

    public AlertAudience Audience { get; set; } = AlertAudience.Everyone;

    /// <summary>Product page only: show just for this product.</summary>
    public Guid? ProductId { get; set; }

    /// <summary>Product / category pages: show just for this category (and its products).</summary>
    public Guid? CategoryId { get; set; }

    public int DisplayOrder { get; set; }

    public DateTimeOffset? StartAt { get; set; }

    public DateTimeOffset? EndAt { get; set; }

    public bool IsActive { get; set; } = true;
}

/// <summary>
/// Placement of the alert inside the page.
/// </summary>
public enum AlertPosition
{
    None = 0,
    Top = 1,              // بالای صفحه
    Bottom = 2,           // پایین صفحه
    Left = 3,             // چپ صفحه
    Right = 4,            // راست صفحه
    Center = 5,           // وسط صفحه
    AboveContent = 6,     // بالای محتوا
    BelowContent = 7,     // زیر محتوا
    BelowMainImage = 8,   // زیر تصویر اصلی (مثلاً زیر عکس محصول)
}

/// <summary>Who sees the alert.</summary>
public enum AlertAudience
{
    Everyone = 0,
    Guests = 1,     // فقط کاربران وارد نشده
    SignedIn = 2,   // فقط کاربران وارد شده
}

/// <summary>
/// Visual/semantic kind of the alert.
/// </summary>
public enum AlertType
{
    None = 0,
    Info = 1,
    Success = 2,
    Warning = 3,
    Error = 4,
}
