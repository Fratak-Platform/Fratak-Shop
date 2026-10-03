using System.ComponentModel.DataAnnotations;
using Fratak_Shop.Data.Utilities;

namespace Fratak_Shop.Data.Entities.SeoRedirects;

/// <summary>
/// URL redirect managed by the SEO specialist (for example after a product slug
/// change) so old links keep their ranking instead of returning 404.
/// </summary>
public class SeoRedirect : BaseEntity
{
    /// <summary>
    /// Old path requested by the visitor, e.g. /products/old-slug.
    /// </summary>
    [Required]
    [MaxLength(500)]
    public string FromPath { get; set; } = null!;

    /// <summary>
    /// Destination URL or path the visitor is redirected to.
    /// </summary>
    [Required]
    [MaxLength(2000)]
    public string ToUrl { get; set; } = null!;

    public RedirectType Type { get; set; } = RedirectType.MovedPermanently;

    [MaxLength(500)]
    public string? Notes { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Number of times the redirect was served, and the last time it happened.
    /// </summary>
    public int HitCount { get; set; }

    public DateTimeOffset? LastHitAt { get; set; }
}

public enum RedirectType
{
    MovedPermanently = 301,
    Found = 302,
    TemporaryRedirect = 307,
    PermanentRedirect = 308,
}
