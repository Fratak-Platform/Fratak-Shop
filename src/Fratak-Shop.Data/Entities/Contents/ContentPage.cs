using System.ComponentModel.DataAnnotations;
using Fratak_Shop.Data.Entities.Pages;
using Fratak_Shop.Data.Utilities;
using Fratak_Shop.Data.Utilities.SeoManagement;

namespace Fratak_Shop.Data.Entities.Contents;

/// <summary>
/// Editable content page (about us, terms, privacy, warranty, return policy, ...).
/// The HTML body is sanitized on save. <see cref="PageKey"/> links a page to a
/// well-known slot of the site (e.g. the terms page linked from the register form).
/// </summary>
public class ContentPage : BaseEntity
{
    [Required]
    [MaxLength(200)]
    public string Slug { get; set; } = null!;

    [Required]
    [MaxLength(300)]
    public string Title { get; set; } = null!;

    [MaxLength(1000)]
    public string? Summary { get; set; }

    /// <summary>Sanitized HTML body.</summary>
    public string? Content { get; set; }

    [MaxLength(500)]
    public string? ImageUrl { get; set; }

    public SitePage? PageKey { get; set; }

    public bool IsActive { get; set; } = true;

    public bool ShowInFooter { get; set; } = true;

    public int DisplayOrder { get; set; }

    public SeoMetaData? Seo { get; set; }
}
