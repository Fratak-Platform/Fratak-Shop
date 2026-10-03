namespace Fratak_Shop.Data.Entities.Seo;

/// <summary>
/// Entities that carry SEO data and can therefore be audited, scored and
/// featured in the smart-SEO tools of the admin panel.
/// </summary>
public enum SeoEntityType
{
    None = 0,
    Product = 1,
    Category = 2,
    BlogPost = 3,
    BlogCategory = 4,
    Page = 5,
}
