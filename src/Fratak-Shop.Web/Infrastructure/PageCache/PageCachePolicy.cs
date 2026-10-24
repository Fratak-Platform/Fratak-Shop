using Fratak_Shop.Core.Services.Settings;
using Fratak_Shop.Data.Utilities;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Extensions.Primitives;

namespace Fratak_Shop.Web.Infrastructure.PageCache;

/// <summary>
/// Output cache of public pages (home, product, listing, blog, content pages …).
/// Only anonymous GET/HEAD requests without any shop / framework cookie are served from or stored in
/// the cache, so nothing personal (cart, login, flash messages, CSRF token) can ever be cached or shown
/// to someone else. Forms on cacheable pages get their antiforgery token lazily
/// (<see cref="DeferredAntiforgeryHtmlGenerator"/>). Entries live <c>PageCacheSeconds</c> and are
/// evicted at once when shop data changes (<see cref="WireEviction"/>).
/// </summary>
public sealed class PageCachePolicy : IOutputCachePolicy
{
    public const string Name = "Page";
    public const string Tag = "pages";
    private const string EligibleKey = "fratak.page-cache";

    /// <summary>Data that never appears on public pages – changes don't evict the cache.</summary>
    private static readonly HashSet<string> IgnoredTypes = new(StringComparer.Ordinal)
    {
        "Cart", "CartItem", "SavedCartItem", "RefreshToken", "SearchQueryLog", "Notification", "UserKnownDevice",
        "PaymentTransaction", "WebPushSubscription", "SeoNotFoundLog", "ProductPriceHistory", "ProductAlert",
        "Wallet", "WalletTransaction", "WithdrawalRequest", "LoyaltyTransaction", "AbandonedCart", "AbandonedCartReminder",
        "UserAddress", "Favorite", "User", "Message", "SupportTicket", "SupportTicketMessage", "SupportTicketAttachment",
        "ReturnRequest", "ReturnRequestItem", "ReturnRequestImage", "OrderFeedback", "GiftCard", "GiftCardRedemption",
        "Order", "OrderItem", "OrderStatusHistory", "Invoice", "CouponUsage",
    };

    /// <summary>True while rendering a response that may be stored in the page cache.</summary>
    public static bool IsCacheable(HttpContext http) => http.Items.ContainsKey(EligibleKey);

    public static bool AffectsPages(IReadOnlyCollection<Type> types) => types.Any(t => !IgnoredTypes.Contains(t.Name));

    /// <summary>Evicts all cached pages whenever page-relevant data is saved.</summary>
    public static void WireEviction(IOutputCacheStore store, ILogger logger)
    {
        DataChangeNotifier.Changed += types =>
        {
            if (!AffectsPages(types))
                return;

            _ = EvictAsync();

            async Task EvictAsync()
            {
                try
                {
                    await store.EvictByTagAsync(Tag, CancellationToken.None);
                }
                catch (Exception exception)
                {
                    logger.LogWarning(exception, "Page cache eviction failed");
                }
            }
        };
    }

    public static bool HasSessionState(HttpRequest request)
    {
        if (request.Headers.ContainsKey("Authorization"))
            return true;

        foreach (var cookie in request.Cookies)
        {
            var name = cookie.Key;
            if (name.Contains("fratak", StringComparison.OrdinalIgnoreCase) || name.StartsWith(".AspNetCore", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    async ValueTask IOutputCachePolicy.CacheRequestAsync(OutputCacheContext context, CancellationToken cancellation)
    {
        var http = context.HttpContext;
        var request = http.Request;

        var eligible = (HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method))
                       && !request.Query.ContainsKey("handler")
                       && !request.Query.ContainsKey("q") // searches are logged for "popular searches"
                       && !request.Query.ContainsKey("ref") // invite links set a cookie
                       && !HasSessionState(request);

        if (eligible)
        {
            var settings = await http.RequestServices.GetRequiredService<IStoreSettingService>().GetAsync(cancellation);
            eligible = settings.IsPageCacheEnabled && settings.PageCacheSeconds > 0;
            if (eligible)
                context.ResponseExpirationTimeSpan = TimeSpan.FromSeconds(Math.Clamp(settings.PageCacheSeconds, 5, 3600));
        }

        context.EnableOutputCaching = eligible;
        context.AllowCacheLookup = eligible;
        context.AllowCacheStorage = eligible;
        context.AllowLocking = true;
        if (!eligible)
            return;

        http.Items[EligibleKey] = true;
        context.CacheVaryByRules.QueryKeys = new StringValues("*");
        context.CacheVaryByRules.VaryByHost = true;
        context.Tags.Add(Tag);
    }

    ValueTask IOutputCachePolicy.ServeFromCacheAsync(OutputCacheContext context, CancellationToken cancellation)
    {
        context.HttpContext.Response.Headers["X-Page-Cache"] = "HIT";
        return ValueTask.CompletedTask;
    }

    ValueTask IOutputCachePolicy.ServeResponseAsync(OutputCacheContext context, CancellationToken cancellation)
    {
        var response = context.HttpContext.Response;

        // Never store personal or unsuccessful responses.
        if (response.StatusCode != StatusCodes.Status200OK || response.Headers.ContainsKey("Set-Cookie"))
        {
            context.AllowCacheStorage = false;
        }

        return ValueTask.CompletedTask;
    }
}
