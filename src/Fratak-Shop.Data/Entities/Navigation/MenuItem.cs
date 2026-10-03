using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Fratak_Shop.Data.Utilities;

namespace Fratak_Shop.Data.Entities.Navigation;

/// <summary>
/// Editable site menu. Header, mobile header, footer columns and the profile
/// sidebar are all rows of this table, so the storefront navigation is fully
/// manageable from the admin panel without touching code.
/// Footer columns are modelled as a parent row (the column heading, without URL)
/// that owns its links as children.
/// </summary>
public class MenuItem : BaseEntity
{
    public MenuLocation Location { get; set; }

    public Guid? ParentId { get; set; }

    [Required]
    [MaxLength(200)]
    public string Title { get; set; } = null!;

    /// <summary>
    /// Relative URL (<c>/products</c>) or absolute URL (<c>https://...</c>).
    /// Empty for footer column headings.
    /// </summary>
    [MaxLength(500)]
    public string? Url { get; set; }

    /// <summary>
    /// Optional icon key rendered by the UI (see <c>MenuIconProvider</c> in the web layer).
    /// </summary>
    [MaxLength(100)]
    public string? Icon { get; set; }

    /// <summary>
    /// Small coloured label next to the title, e.g. «جدید» or «٪۲۰».
    /// </summary>
    [MaxLength(50)]
    public string? BadgeText { get; set; }

    [MaxLength(20)]
    public string? BadgeColor { get; set; }

    public bool OpenInNewTab { get; set; }

    /// <summary>
    /// When enabled the item is also rendered inside the mobile drawer.
    /// </summary>
    public bool ShowOnMobile { get; set; } = true;

    /// <summary>
    /// Highlights the item in the header (used for campaign links).
    /// </summary>
    public bool IsHighlighted { get; set; }

    public int DisplayOrder { get; set; }

    public bool IsActive { get; set; } = true;

    [ForeignKey(nameof(ParentId))]
    public MenuItem? Parent { get; set; }

    public List<MenuItem> Children { get; set; } = [];
}

public enum MenuLocation
{
    None = 0,
    Header = 1,           // منوی اصلی هدر
    Footer = 2,           // ستون‌های فوتر
    FooterBottom = 3,     // لینک‌های نوار پایانی فوتر
    ProfileSidebar = 4,   // منوی پنل کاربری
    QuickAccess = 5,      // دسترسی سریع (فوتر / صفحه اصلی)
}
