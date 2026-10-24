namespace Fratak_Shop.Core.Utilities;

/// <summary>
/// Memory-cache keys of data that is read on (almost) every storefront request
/// and changed rarely from the admin panel. Services that change the data remove
/// the matching key, so edits show up immediately.
/// </summary>
public static class CacheKeys
{
    public const string StoreSettings = "core:store-settings";
    public const string ShippingMethods = "core:shipping-methods";
    public const string FooterPages = "core:content-pages:footer";
    public const string SiteFeatures = "core:site-features";
    public const string TrustBadges = "core:trust-badges";
    public const string HomeSections = "core:home-sections";
    public const string FaqGroups = "core:faq-groups";
    public const string MenuCategories = "core:menu-categories";
    public const string Menus = "core:menus";
    public const string NotificationTemplates = "core:notification-templates";
    public const string LoyaltyProgram = "core:loyalty-program";
    public const string FilterAttributes = "core:filter-attributes";

    public static string SecurityStamp(Guid userId) => $"core:security-stamp:{userId:N}";

    public static readonly TimeSpan DefaultLifetime = TimeSpan.FromMinutes(10);
}
