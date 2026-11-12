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
        // داده‌های فقط پنل مدیریت: پیش‌نویس خودکار هر چند ثانیه ذخیره می‌شود و نباید کش صفحات عمومی را خالی کند.
        "EditDraft", "AuditLog", "ErrorLog", "SecurityLog", "StaffNotification", "AdminUserPreference", "AdminHelpArticle",
        "AdminRole", "AdminRolePermission", "UserAdminRole",
        // دور ۲۱: طرح‌های استودیو و گزارش رشد خودکار فقط در پنل‌اند (انتشار بنر/کاور خودش Banner/BlogPost را عوض می‌کند و کش خالی می‌شود).
        "DesignDoc", "GrowthAction",
        "OrderMessage", "CustomerTag", "CustomerNote", "OrderNote", "PurchaseInvoice",
        // گزارش بررسی تأمین‌کننده (تغییر واقعی موجودی روی خود Product ثبت می‌شود و کش را خالی می‌کند).
        "SupplierProductState", "SupplierSyncRun", "SupplierChange", "Supplier",
        "StockMovement", "PriceChangeBatch", "PriceChangeItem", "ShipmentTracking", "ShipmentEvent", "VisitorDay", "AnalyticsCounter", "Expense", "SalesGoal", "ScheduledReport", "ReportDeliveryConfig",
        "SeoScore", "SeoSuggestion", "SeoResearchCache", "SeoEngineRun",
        // مرحلهٔ شانزدهم: جدول دانش سئو و بازخورد «پیشنهاد هوشمند» (یادگیری پیوسته؛ هرگز مستقیم در صفحات عمومی نیستند).
        "SeoKnowledgeEntry", "SmartTextFeedback",
        "SeoLinkIssue", "SearchConsoleQueryStat", "SearchConsoleDailyStat", "SeoToolsConfig",
        // فاز ۹ (فنی): پشتیبان، فایروال، کارها و هشدارها هیچ‌وقت در صفحات عمومی نیستند (شمارندهٔ فایروال هر چند دقیقه ذخیره می‌شود).
        "BackupRecord", "IpRule", "BotRule", "BackgroundJobSetting", "AlertRule", "AlertEvent",
        // دور هشتم: تنظیمات سرویس پیامک.
        "SmsProviderConfig",
        // دور نوزدهم: صف و کش تحقیق هوشمند، تقویم محتوا، یادآوری‌های مشتری و تنظیمات هوشمند فقط در پنل‌اند.
        // ProductInsight عمداً این‌جا نیست: جمع‌بندی تأییدشده در صفحهٔ محصول نمایش داده می‌شود و باید کش را تازه کند.
        // دور ۲۹: SmartContentConfig از این فهرست بیرون آمد – سقف/چرخش بنرها و حالت سوگواری در صفحهٔ اصلی دیده می‌شوند،
        // پس ذخیرهٔ تنظیمات هوشمند (فقط دستی از پنل، نادر) باید کش صفحه‌ها را تازه کند.
        "SmartTask", "SmartResearchCache", "BlogPlanItem", "CustomerReminder",
    };

    // ── آمار کش صفحات از زمان روشن شدن سایت (کارت «کش» در /admin/system) ──
    private static long _hits;
    private static long _misses;
    private static long _evictions;

    /// <summary>صفحه از کش داده شد.</summary>
    public static long Hits => Interlocked.Read(ref _hits);

    /// <summary>صفحهٔ قابل کش که ساخته شد (در کش نبود یا منقضی شده بود).</summary>
    public static long Misses => Interlocked.Read(ref _misses);

    /// <summary>دفعات خالی شدن کش به‌خاطر تغییر داده.</summary>
    public static long Evictions => Interlocked.Read(ref _evictions);

    public static DateTimeOffset? LastEvictedAt { get; private set; }

    public static void CountEviction()
    {
        Interlocked.Increment(ref _evictions);
        LastEvictedAt = DateTimeOffset.UtcNow;
    }

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

            CountEviction();
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

    /// <summary>
    /// کوکی‌های «شخصی‌سازی سبک» مهمان: بازدیدهای اخیر (fratak_rv)، معرف (fratak_ref) و آمار (fr_vid، fr_src).
    /// صفحه را برای این مهمان کمی شخصی می‌کنند (مثلاً «پیشنهاد برای شما»)، ولی حساب کاربری، سبد یا اطلاعات خصوصی نیستند.
    /// </summary>
    public static bool IsPersonalizationCookie(string name) =>
        name is "fratak_rv" or "fratak_ref" or "fr_vid" or "fr_src";

    /// <summary>
    /// دور نوزدهم (A13/A19): آیا درخواست اطلاعات خصوصی دارد (ورود، سبد، مقایسه، فرم امنیتی)؟ مانند <see cref="HasSessionState"/>
    /// ولی کوکی‌های شخصی‌سازی سبک را نادیده می‌گیرد. کاربرد: صف ورودی و «آخرین نسخهٔ سالم» – مهمانی که فقط چند محصول دیده
    /// (کوکی fratak_rv) هنگام شلوغی هم در صف می‌ماند و در بدترین حالت نسخهٔ عمومی صفحه را می‌بیند، نه ۵۰۳.
    /// مثال: «fratak_rv=a1» → false؛ «fratak_at=…» یا «.Fratak.Cart=…» → true.
    /// </summary>
    public static bool HasPrivateState(HttpRequest request)
    {
        if (request.Headers.ContainsKey("Authorization"))
            return true;
        foreach (var cookie in request.Cookies)
        {
            var name = cookie.Key;
            if (IsPersonalizationCookie(name))
                continue;
            if (name.Contains("fratak", StringComparison.OrdinalIgnoreCase) || name.StartsWith(".AspNetCore", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
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
        Interlocked.Increment(ref _hits);
        return ValueTask.CompletedTask;
    }

    ValueTask IOutputCachePolicy.ServeResponseAsync(OutputCacheContext context, CancellationToken cancellation)
    {
        var response = context.HttpContext.Response;
        if (IsCacheable(context.HttpContext))
            Interlocked.Increment(ref _misses);

        // Never store personal or unsuccessful responses.
        if (response.StatusCode != StatusCodes.Status200OK || response.Headers.ContainsKey("Set-Cookie"))
        {
            context.AllowCacheStorage = false;
        }

        return ValueTask.CompletedTask;
    }
}
