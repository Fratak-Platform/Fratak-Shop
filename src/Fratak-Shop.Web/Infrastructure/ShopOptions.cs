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

    /// <summary>
    /// Mobile number of the store owner (full access to the admin panel). Applied at startup when the
    /// account exists; owners can then manage staff and roles from the panel itself.
    /// </summary>
    public string? OwnerPhoneNumber { get; set; }
}
