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

    /// <summary>How many times the redirect was used (shows the admin which ones still matter).</summary>
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

/// <summary>
/// URLs that returned 404 (aggregated, written in batches). Lets the SEO specialist find broken
/// links and old addresses that need a redirect.
/// </summary>
public class SeoNotFoundLog : BaseEntity
{
    [Required]
    [MaxLength(300)]
    public string Path { get; set; } = null!;

    /// <summary>Last referring page (internal or external).</summary>
    [MaxLength(500)]
    public string? LastReferrer { get; set; }

    public int HitCount { get; set; }

    public DateTimeOffset LastHitAt { get; set; }

    /// <summary>Admin: handled (redirect created / ignored) – hidden from the report.</summary>
    public bool IsResolved { get; set; }
}
