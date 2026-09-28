using Fratak_Shop.Data.Utilities;
using Fratak_Shop.Data.Utilities.SeoManagement;

namespace Fratak_Shop.Data.Entities.Pages;

/// <summary>
/// SEO record of a standalone page (home page, contact page, FAQ, ...).
/// Entity-backed pages such as products and blog posts carry their own
/// <see cref="SeoMetadata"/> instead; this table covers pages that have no
/// content entity of their own.
/// </summary>
public class PageSeo : BaseEntity
{
    public SitePage Page { get; set; }

    public SeoMetadata? Seo { get; set; }
}
