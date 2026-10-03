using System.ComponentModel.DataAnnotations;
using Fratak_Shop.Data.Entities.Pages;
using Fratak_Shop.Data.Utilities;

namespace Fratak_Shop.Data.Entities.Content;

/// <summary>
/// Editable body of a standalone page (درباره ما، قوانین، حریم خصوصی، گارانتی، تماس با ما).
/// The SEO record of the same page lives in
/// <see cref="PageSeo"/> keyed by <see cref="SitePage"/>, so content and SEO are
/// edited in one screen of the admin panel but stored independently.
/// </summary>
public class ContentPage : BaseEntity
{
    /// <summary>
    /// Which storefront page this content belongs to.
    /// </summary>
    public SitePage Page { get; set; }

    [Required]
    [MaxLength(250)]
    public string Title { get; set; } = null!;

    [MaxLength(1000)]
    public string? Summary { get; set; }

    /// <summary>
    /// HTML body of the page (sanitized on save).
    /// </summary>
    public string? Content { get; set; }

    public bool IsPublished { get; set; } = true;

    public int DisplayOrder { get; set; }
}
