using Fratak_Shop.Data.Utilities;

namespace Fratak_Shop.Data.Entities.Seo;

/// <summary>
/// Daily visit counter of an SEO entity (product, category, blog post, page).
/// Aggregated per day instead of one row per request, so it stays cheap while the
/// SEO centre can still show «پربازدیدترین محتوا» and «محتوای بدون بازدید».
/// </summary>
public class ContentVisit : BaseEntity
{
    public SeoEntityType EntityType { get; set; }

    public Guid EntityId { get; set; }

    /// <summary>
    /// Visit date (UTC, day precision).
    /// </summary>
    public DateOnly VisitDate { get; set; }

    public int Count { get; set; }
}
