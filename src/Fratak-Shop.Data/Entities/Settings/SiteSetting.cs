using System.ComponentModel.DataAnnotations;
using Fratak_Shop.Data.Utilities;

namespace Fratak_Shop.Data.Entities.Settings;

/// <summary>
/// Key/value configuration of the whole shop, managed from the admin panel
/// (identity, header, footer, payment, shipping, SEO, social networks).
/// A single table is used on purpose: adding a new configurable option never
/// requires a schema change, only a seed row.
/// </summary>
public class SiteSetting : BaseEntity
{
    /// <summary>
    /// Unique machine key, e.g. <c>General.SiteName</c> or <c>Footer.Copyright</c>.
    /// </summary>
    [Required]
    [MaxLength(150)]
    public string Key { get; set; } = null!;

    /// <summary>
    /// Stored value. Complex values (lists, objects) are stored as JSON and are
    /// typed by <see cref="ValueType"/>.
    /// </summary>
    public string? Value { get; set; }

    /// <summary>
    /// Logical group used by the admin settings screen
    /// (General, Header, Footer, Payment, Shop, Seo, Social).
    /// </summary>
    [MaxLength(100)]
    public string? Group { get; set; }

    /// <summary>
    /// Persian label and helper text shown in the admin panel.
    /// </summary>
    [MaxLength(250)]
    public string? Title { get; set; }

    [MaxLength(500)]
    public string? Description { get; set; }

    public SettingValueType ValueType { get; set; }

    /// <summary>
    /// JSON array of allowed options when <see cref="ValueType"/> is
    /// <see cref="SettingValueType.Select"/>.
    /// </summary>
    public string? Options { get; set; }

    /// <summary>
    /// Marks the setting as safe to expose to the storefront
    /// (public settings are cached and handed to the views).
    /// </summary>
    public bool IsPublic { get; set; }

    public int DisplayOrder { get; set; }
}

public enum SettingValueType
{
    None = 0,
    Text = 1,
    Textarea = 2,
    Html = 3,
    Number = 4,
    Bool = 5,
    Image = 6,
    Color = 7,
    Select = 8,
    Json = 9,
}
