using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Fratak_Shop.Data.Utilities;

namespace Fratak_Shop.Data.Entities.Contents;

/// <summary>
/// A navigation menu of the site (header bar, mega-menu shortcuts, footer columns,
/// mobile menu). Menus and their items are managed from the admin panel; nothing in
/// the header or footer navigation is hard coded.
/// </summary>
public class Menu : BaseEntity
{
    /// <summary>Stable key used by the views, e.g. "header-main", "footer-quick-links".</summary>
    [Required]
    [MaxLength(100)]
    public string Key { get; set; } = null!;

    /// <summary>Heading (footer column title).</summary>
    [Required]
    [MaxLength(200)]
    public string Title { get; set; } = null!;

    public MenuLocation Location { get; set; }

    /// <summary>
    /// <see cref="MenuSource.ContentPages"/> fills the menu automatically with the active
    /// content pages marked "show in footer" (manual items are appended).
    /// </summary>
    public MenuSource Source { get; set; } = MenuSource.Manual;

    public bool IsActive { get; set; } = true;

    public int DisplayOrder { get; set; }

    public List<MenuItem> Items { get; set; } = [];
}

/// <summary>A link of a menu. Items can be nested one level (dropdown) and scheduled.</summary>
public class MenuItem : BaseEntity
{
    public Guid MenuId { get; set; }

    public Guid? ParentId { get; set; }

    [Required]
    [MaxLength(200)]
    public string Title { get; set; } = null!;

    /// <summary>Site-relative path ("/products?sort=BestSelling") or an http(s) URL.</summary>
    [MaxLength(500)]
    public string? Url { get; set; }

    public MenuItemType Type { get; set; } = MenuItemType.Link;

    /// <summary>Icon key of the template icon set (optional).</summary>
    [MaxLength(50)]
    public string? Icon { get; set; }

    /// <summary>Small label next to the title, e.g. "جدید" or "٪".</summary>
    [MaxLength(30)]
    public string? BadgeText { get; set; }

    public bool OpenInNewTab { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>Optional schedule: the link is only shown inside this window.</summary>
    public DateTimeOffset? StartsAt { get; set; }

    public DateTimeOffset? EndsAt { get; set; }

    public int DisplayOrder { get; set; }

    [ForeignKey(nameof(MenuId))]
    public Menu Menu { get; set; } = null!;

    [ForeignKey(nameof(ParentId))]
    public MenuItem? Parent { get; set; }

    public List<MenuItem> Children { get; set; } = [];
}

public enum MenuLocation
{
    None = 0,
    HeaderMain = 1,       // نوار منوی هدر (دسکتاپ و موبایل)
    HeaderShortcuts = 2,  // میانبرهای کنار مگامنو
    Footer = 3,           // ستون‌های فوتر
}

public enum MenuSource
{
    Manual = 1,
    ContentPages = 2,
}

public enum MenuItemType
{
    Link = 1,
    /// <summary>"فروشگاه" item: opens the category mega menu (Url = link of the item itself).</summary>
    CategoriesMegaMenu = 2,
}
