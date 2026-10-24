namespace Fratak_Shop.Web.Infrastructure;

/// <summary>
/// Deployment-level switches (section <c>Shop</c>). Everything a store manager may want to
/// change – store name, contact details, shipping, payment, wallet, SEO – lives in the
/// database (<c>StoreSetting</c> and friends) so it can be edited from the admin panel.
/// </summary>
public class ShopOptions
{
    public const string SectionName = "Shop";

    /// <summary>
    /// Seeds demo catalog data (categories, brands, products, banners, posts, ...) on an empty database.
    /// </summary>
    public bool SeedDemoData { get; set; }
}
