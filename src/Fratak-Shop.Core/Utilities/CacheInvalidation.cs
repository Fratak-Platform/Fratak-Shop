using Fratak_Shop.Data.Utilities;
using Microsoft.Extensions.Caching.Memory;

namespace Fratak_Shop.Core.Utilities;

/// <summary>
/// Connects <see cref="DataChangeNotifier"/> to the memory caches of the core services: whenever a
/// kind of data is saved (storefront today, admin panel later) the matching cached copies are
/// dropped, so nothing stale is shown for the remaining cache lifetime.
/// </summary>
public static class CacheInvalidation
{
    public const string SitemapKey = "core:sitemap";
    public const string SearchSynonymsKey = "core:search-synonyms";
    public const string SearchVocabularyKey = "core:search-vocabulary";
    public const string PopularSearchesKey = "core:popular-searches";
    public const string SupportDepartmentsKey = "core:support-departments";
    public const string ReturnReasonsKey = "core:return-reasons";

    private static readonly string[] CatalogTypes = ["Product", "Category", "Brand", "ProductImage", "ProductFeature", "ProductVideo"];

    /// <summary>Entity type name → cache keys to drop.</summary>
    private static readonly Dictionary<string, string[]> KeysByType = new(StringComparer.Ordinal)
    {
        ["StoreSetting"] = [CacheKeys.StoreSettings],
        ["ShippingMethod"] = [CacheKeys.ShippingMethods, CacheKeys.ShippingRates],
        ["ShippingRate"] = [CacheKeys.ShippingRates],
        ["ContentPage"] = [CacheKeys.FooterPages, SitemapKey],
        ["SiteFeature"] = [CacheKeys.SiteFeatures],
        ["TrustBadge"] = [CacheKeys.TrustBadges],
        ["HomeSection"] = [CacheKeys.HomeSections],
        ["FaqItem"] = [CacheKeys.FaqGroups],
        ["FaqCategory"] = [CacheKeys.FaqGroups],
        ["Category"] = [CacheKeys.MenuCategories, CacheKeys.FilterAttributes, SitemapKey, SearchVocabularyKey],
        ["Brand"] = [SitemapKey, SearchVocabularyKey],
        ["Product"] = [SitemapKey, SearchVocabularyKey],
        ["BlogPost"] = [SitemapKey],
        ["BlogCategory"] = [SitemapKey],
        ["PageSeo"] = [SitemapKey],
        // دور ۲۱: صفحهٔ کمپین تازه/منتشرشده باید بلافاصله در نقشهٔ سایت بیاید (و صفحهٔ پیش‌نویس یا حذف‌شده بیرون برود).
        ["LandingPage"] = [SitemapKey],
        ["Menu"] = [CacheKeys.Menus],
        ["MenuItem"] = [CacheKeys.Menus],
        ["NotificationTemplate"] = [CacheKeys.NotificationTemplates],
        ["LoyaltyRule"] = [CacheKeys.LoyaltyProgram],
        ["LoyaltyTier"] = [CacheKeys.LoyaltyProgram],
        ["LoyaltyReward"] = [CacheKeys.LoyaltyProgram],
        ["ProductFilterAttribute"] = [CacheKeys.FilterAttributes],
        ["PageAlert"] = [CacheKeys.PageAlerts],
        ["AbandonedCartStep"] = [CacheKeys.AbandonedCartSteps],
        ["SearchSynonym"] = [SearchSynonymsKey, SearchVocabularyKey],
        ["SearchQueryLog"] = [PopularSearchesKey],
        ["SupportDepartment"] = [SupportDepartmentsKey],
        ["ReturnReason"] = [ReturnReasonsKey],
    };

    private static IMemoryCache? _cache;

    /// <summary>Called once at start-up with the application's memory cache.</summary>
    public static void Wire(IMemoryCache cache)
    {
        if (Interlocked.Exchange(ref _cache, cache) is null)
            DataChangeNotifier.Changed += OnChanged;
    }

    /// <summary>Changes whenever catalog data (products, categories, brands …) changes – for version-keyed caches.</summary>
    public static long CatalogVersion
    {
        get
        {
            long version = 0;
            foreach (var type in DataChangeNotifier.KnownTypes())
            {
                if (Array.IndexOf(CatalogTypes, type.Name) >= 0)
                    version += DataChangeNotifier.VersionOf(type);
            }
            return version;
        }
    }

    /// <summary>Cache keys affected by a change of these entity types (exposed for tests).</summary>
    public static IReadOnlyCollection<string> KeysFor(IEnumerable<Type> types)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var type in types)
        {
            if (KeysByType.TryGetValue(type.Name, out var mapped))
                keys.UnionWith(mapped);
        }
        return keys;
    }

    private static void OnChanged(IReadOnlyCollection<Type> types)
    {
        var cache = _cache;
        if (cache is null)
            return;

        foreach (var key in KeysFor(types))
            cache.Remove(key);
    }
}
