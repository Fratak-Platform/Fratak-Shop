using System.ComponentModel.DataAnnotations;
using Fratak_Shop.Data.Utilities;

namespace Fratak_Shop.Data.Entities.Seo;

/// <summary>
/// Aggregated log of 404 requests. It feeds the «خطاهای ۴۰۴» report of the SEO
/// centre, where the specialist turns a broken URL into a
/// <see cref="Fratak_Shop.Data.Entities.SeoRedirects.SeoRedirect"/> with one click.
/// Repeated hits of the same path increment <see cref="HitCount"/> instead of
/// creating new rows.
/// </summary>
public class NotFoundLog : BaseEntity
{
    /// <summary>
    /// Requested path, normalized and lower-cased (e.g. <c>/products/old-slug</c>).
    /// </summary>
    [Required]
    [MaxLength(500)]
    public string Path { get; set; } = null!;

    [MaxLength(1000)]
    public string? Referrer { get; set; }

    [MaxLength(300)]
    public string? UserAgent { get; set; }

    public int HitCount { get; set; }

    public DateTimeOffset LastHitAt { get; set; }

    /// <summary>
    /// Set when the specialist created a redirect for this path.
    /// </summary>
    public Guid? ResolvedRedirectId { get; set; }

    public bool IsResolved { get; set; }
}
