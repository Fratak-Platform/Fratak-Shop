using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using Fratak_Shop.Core.Services.Seo.Engine;
using Fratak_Shop.Core.Services.Suppliers;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Products;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;

namespace Fratak_Shop.Core.Services.Pricing;

/// <summary>
/// نتیجهٔ جست‌وجوی قیمت رقبا.
/// </summary>
/// <param name="Offers">قیمت فروشگاه‌ها (خام؛ پاک‌سازی و حذف پرت در <see cref="PriceAdvisor.Clean"/>).</param>
/// <param name="Problems">مشکل هر منبع به فارسی (مثلاً «ترب: کپچا»)؛ برای اینکه کارمند بداند چرا فهرست کوتاه است.</param>
/// <param name="MatchedTitle">عنوان کالایی که در ترب انتخاب شد (برای اطمینان از اینکه همان کالاست).</param>
/// <param name="Limited">دست‌کم یک منبع ما را محدود کرده یا در حال استراحت است (یعنی «پیدا نشد» قطعی نیست و بعداً باید دوباره امتحان کرد).</param>
/// <param name="CachedAt">اگر قیمت‌ها از کش ماندگار آمده‌اند، زمان جست‌وجوی واقعی؛ null یعنی همین الان گرفته شد.</param>
public sealed record CompetitorLookup(
    IReadOnlyList<CompetitorOffer> Offers, IReadOnlyList<string> Problems, string? MatchedTitle,
    bool Limited = false, DateTimeOffset? CachedAt = null);

/// <summary>
/// جست‌وجوی قیمت همان کالا در سایت‌های دیگر (رایگان، بدون کلید API و بدون سرویس پولی).
/// <para>کجا استفاده می‌شود: دکمهٔ «پیشنهاد قیمت فروش» در فرم محصول پنل مدیریت و قیمت‌گذاری بازار «درون‌ریزی از سایت» (MarketPricingService).</para>
/// </summary>
public interface ICompetitorPriceService
{
    /// <summary>
    /// قیمت فروشگاه‌ها برای یک عبارت. مثال: «هندزفری بلوتوثی هایلو T15» ← دیجی‌کالا ۳۰۰ هزار، فروشگاه X ۲۸۰ هزار، …
    /// <para>هیچ‌وقت استثنا نمی‌دهد؛ خطای هر منبع در <see cref="CompetitorLookup.Problems"/> می‌آید.</para>
    /// </summary>
    Task<CompetitorLookup> SearchAsync(string query, CancellationToken cancellationToken);
}

/// <summary>قیمت‌های ذخیره‌شدهٔ یک عبارت در کش ماندگار.</summary>
public sealed record StoredCompetitorPrices(IReadOnlyList<CompetitorOffer> Offers, string? MatchedTitle, DateTimeOffset FetchedAt);

/// <summary>
/// کش ماندگار قیمت رقبا (جدول CompetitorPriceCaches). خطای پایگاه داده هیچ‌وقت به بیرون نمی‌رسد (کش «بهترین تلاش» است).
/// </summary>
public interface ICompetitorPriceStore
{
    Task<StoredCompetitorPrices?> GetAsync(string key, CancellationToken cancellationToken);

    Task SaveAsync(string key, IReadOnlyList<CompetitorOffer> offers, string? matchedTitle, CancellationToken cancellationToken);
}

/// <summary>
/// پیاده‌سازی کش ماندگار با یک DbContext جدا (scope مستقل) تا ذخیرهٔ کش هرگز تغییرات ذخیره‌نشدهٔ صفحهٔ صدازننده را ذخیره یا پاک نکند.
/// </summary>
public sealed class CompetitorPriceStore(IServiceScopeFactory scopeFactory) : ICompetitorPriceStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<StoredCompetitorPrices?> GetAsync(string key, CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
            var row = await context.CompetitorPriceCaches.AsNoTracking()
                .Where(c => c.QueryKey == key)
                .Select(c => new { c.OffersJson, c.MatchedTitle, c.FetchedAt })
                .FirstOrDefaultAsync(cancellationToken);
            if (row is null)
                return null;
            var offers = JsonSerializer.Deserialize<List<CompetitorOffer>>(row.OffersJson, Json) ?? [];
            return new StoredCompetitorPrices(offers.Where(o => o.Price > 0 && !string.IsNullOrWhiteSpace(o.Shop)).ToList(), row.MatchedTitle, row.FetchedAt);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return null;
        }
    }

    public async Task SaveAsync(string key, IReadOnlyList<CompetitorOffer> offers, string? matchedTitle, CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
            var row = await context.CompetitorPriceCaches.AsTracking().FirstOrDefaultAsync(c => c.QueryKey == key, cancellationToken);
            if (row is null)
            {
                row = new CompetitorPriceCache { QueryKey = key };
                context.CompetitorPriceCaches.Add(row);
            }
            row.OffersJson = JsonSerializer.Serialize(offers.Take(60).ToList(), Json);
            row.OfferCount = offers.Count;
            row.MatchedTitle = matchedTitle is { Length: > 200 } ? matchedTitle[..200] : matchedTitle;
            row.FetchedAt = DateTimeOffset.UtcNow;
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // دو درخواست هم‌زمان برای یک عبارت (ایندکس یکتا) یا قطعی لحظه‌ای پایگاه داده: کش بعدی ذخیره می‌شود.
        }
    }
}

/// <summary>داوری دربارهٔ پاسخ یک منبع.</summary>
public enum SourceVerdict
{
    /// <summary>پاسخ سالم (JSON).</summary>
    Ok,

    /// <summary>منبع ما را محدود کرده (کپچا/۴۹۰/۴۰۳/۴۲۹/۵۰۳)؛ باید استراحت کرد.</summary>
    Blocked,

    /// <summary>خطای موقت (شبکه، ۵۰۰، ۴۰۴، پاسخ HTML نامعلوم).</summary>
    Failed,
}

/// <summary>
/// پیاده‌سازی با سه منبع عمومی و رایگان:
/// <list type="number">
/// <item><b>ترب</b> (موتور مقایسهٔ قیمت): جست‌وجو در <c>api.torob.com/v4/base-product/search</c>، انتخاب نزدیک‌ترین کالا به نام ما،
/// سپس فهرست همهٔ فروشندگان آن کالا از صفحهٔ جزئیات (معمولاً ۵ تا ۳۰ فروشگاه). قیمت‌ها به تومان‌اند.</item>
/// <item><b>دیجی‌کالا</b>: جست‌وجو در <c>api.digikala.com/v1/search</c>؛ قیمت به ریال است و به تومان تبدیل می‌شود.</item>
/// <item><b>باسلام</b>: جست‌وجو در <c>search.basalam.com</c>؛ تا ۵ غرفهٔ مشابه (قیمت ریالی ← تومان). منبع تکمیلی است: «پیدا نشد» آن خطا حساب نمی‌شود.</item>
/// <item><b>ایمالز</b> (دور ۲۴): جست‌وجوی <c>emalls.ir/_Search.ashx</c>؛ تا ۳ فروشندهٔ مشابه (تومان). منبع تکمیلی و فقط برای پنل.</item>
/// <item><b>سایت‌های دیگر</b> (مرحلهٔ پانزدهم): جست‌وجوی «نام کالا + قیمت» در بینگ/داک‌داک‌گو (همان منبع رایگان موتور سئو،
/// <see cref="ISeoWebSource"/>)، باز کردن حداکثر ۸ صفحهٔ فروشگاهی (هر دامنه یکی) و خواندن قیمت از دادهٔ ساخت‌یافتهٔ خود صفحه
/// (JSON-LD محصول، متای product:price، itemprop=price). ترب، دیجی‌کالا و باسلام اینجا تکرار نمی‌شوند.</item>
/// </list>
/// <para><b>مقاوم در برابر محدودیت (مرحلهٔ سیزدهم)</b> — بدون دور زدن کپچا، فقط با رفتار درست:</para>
/// <list type="bullet">
/// <item>همهٔ درخواست‌ها از <see cref="IExternalSourceGate"/> می‌گذرند: برای هر منبع یک درخواست در لحظه، فاصلهٔ حداقل (ترب ۱٫۵ ثانیه، بقیه ۱ ثانیه)
/// و استراحت خودکار بعد از محدود شدن (۱۰ دقیقه تا ۲ ساعت). منبعِ در حال استراحت اصلاً درخواست نمی‌گیرد تا محدودیت طولانی‌تر نشود.</item>
/// <item>کش دو لایه: حافظه (۳۰ دقیقه) و پایگاه داده (۶ ساعت تازه). اگر منابع موقتاً محدود بودند، آخرین قیمت‌های معتبر تا ۳ روز با ذکر زمان برمی‌گردد.</item>
/// <item>یک منبع خراب بقیه را خراب نمی‌کند؛ منابع موازی اجرا می‌شوند و هر کدام سقف ۲۰ ثانیه دارد.</item>
/// </list>
/// <para>درخواست‌ها از همان کلاینت امن تأمین‌کننده (<see cref="ISupplierHttp"/>: محافظ SSRF، سقف حجم و زمان، User-Agent مرورگر) می‌روند.</para>
/// </summary>
public sealed class CompetitorPriceService(ISupplierHttp http, IMemoryCache cache, IExternalSourceGate gate, ICompetitorPriceStore? store = null,
    ISeoWebSource? web = null)
    : ICompetitorPriceService
{
    public const string Torob = "ترب";
    public const string DigikalaSource = "دیجی‌کالا";
    public const string Basalam = "باسلام";

    /// <summary>
    /// دور ۲۴ (پیشنهاد ۴۵): منبع «ایمالز» – موتور مقایسهٔ قیمت. مثل بقیهٔ منابع این سرویس <b>فقط در پنل مدیریت</b> دیده می‌شود
    /// (پیشنهاد قیمت فروش، قیمت‌گذاری بازار)؛ هیچ‌جای فروشگاه به مشتری نشان داده نمی‌شود.
    /// </summary>
    public const string Emalls = "ایمالز";

    /// <summary>نشانی جست‌وجوی ایمالز (POST فرم؛ پاسخ JSON با فهرست <c>lstsearchresualt</c>).</summary>
    public const string EmallsSearchUrl = "https://emalls.ir/_Search.ashx";

    /// <summary>نام منبع «فروشگاه‌های اینترنتی دیگر» (مرحلهٔ پانزدهم).</summary>
    public const string WebShops = "سایت‌های دیگر";

    /// <summary>حداکثر صفحه‌های فروشگاهی که برای هر جست‌وجو باز می‌شود (هر دامنه یک صفحه؛ مؤدبانه و سریع).</summary>
    public const int MaxWebPages = 8;

    /// <summary>بودجهٔ زمانی خواندن صفحه‌های فروشگاه‌ها؛ هر صفحه‌ای تا این زمان جواب داد حساب می‌شود و بقیه رها می‌شوند.</summary>
    private static readonly TimeSpan WebPagesBudget = TimeSpan.FromSeconds(14);

    /// <summary>سقف کل منبع «سایت‌های دیگر» (جست‌وجو + صفحه‌ها).</summary>
    private static readonly TimeSpan WebTimeout = TimeSpan.FromSeconds(24);

    /// <summary>بیشترین زمان انتظار برای هر منبع (شامل نوبت صف).</summary>
    private static readonly TimeSpan SourceTimeout = TimeSpan.FromSeconds(20);

    /// <summary>کش ماندگار تا این مدت «تازه» است و اصلاً جست‌وجوی واقعی انجام نمی‌شود.</summary>
    public static readonly TimeSpan FreshFor = TimeSpan.FromHours(6);

    /// <summary>اگر جست‌وجوی تازه ناموفق بود، قیمت‌های ذخیره‌شده تا این مدت (با ذکر زمان) قابل استفاده‌اند.</summary>
    public static readonly TimeSpan StaleFor = TimeSpan.FromDays(3);

    /// <summary>فاصلهٔ حداقل بین دو درخواست ترب (ترب به درخواست پشت‌سرهم کپچای ۴۹۰ می‌دهد).</summary>
    public static readonly TimeSpan TorobInterval = TimeSpan.FromSeconds(1.5);

    /// <summary>فاصلهٔ حداقل بین دو درخواست دیجی‌کالا و باسلام.</summary>
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromSeconds(1);

    public async Task<CompetitorLookup> SearchAsync(string query, CancellationToken cancellationToken)
    {
        query = (query ?? "").Trim();
        if (query.Length > 120)
            query = query[..120];
        if (query.Length < 2)
            return new CompetitorLookup([], ["عبارت جست‌وجو خیلی کوتاه است."], null);

        var normalized = PersianText.Normalize(query);
        var storeKey = normalized.Length > 200 ? normalized[..200] : normalized;
        var key = "competitor-prices:" + normalized;
        if (cache.TryGetValue(key, out CompetitorLookup? hit) && hit is not null)
            return hit;

        var stored = store is null ? null : await store.GetAsync(storeKey, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        if (stored is { Offers.Count: > 0 } && now - stored.FetchedAt < FreshFor)
        {
            var fresh = new CompetitorLookup(stored.Offers, [], stored.MatchedTitle, CachedAt: stored.FetchedAt);
            cache.Set(key, fresh, TimeSpan.FromMinutes(30));
            return fresh;
        }

        // قیمت‌هایی از سایت‌های دیگر که واحدشان (ریال/تومان) در صفحه نیامده بود؛ بعد از جواب بقیهٔ منابع تصمیم گرفته می‌شود.
        var unclear = new StrongBox<IReadOnlyList<WebPagePrice>>(Array.Empty<WebPagePrice>());
        var sources = new List<Task<CompetitorLookup>>
        {
            GuardAsync(Torob, token => TorobAsync(query, token), cancellationToken),
            GuardAsync(DigikalaSource, token => DigikalaAsync(query, token), cancellationToken),
            GuardAsync(Basalam, token => BasalamAsync(query, token), cancellationToken),
            GuardAsync(Emalls, token => EmallsAsync(query, token), cancellationToken),
        };
        // «سایت‌های دیگر» فقط وقتی منبع واقعی وب ثبت شده باشد (در تست‌ها و لایهٔ Core تنها، منبع آفلاین است).
        if (web is not null and not OfflineSeoWebSource)
            sources.Add(GuardAsync(WebShops, token => WebShopsAsync(query, unclear, token), cancellationToken, WebTimeout));
        var results = await Task.WhenAll(sources);

        var offers = results.SelectMany(r => r.Offers).ToList();
        var problems = results.SelectMany(r => r.Problems).ToList();
        if (unclear.Value is { Count: > 0 } pending)
            offers.AddRange(CompetitorParsers.ResolveCurrency(pending, offers));
        var limited = results.Any(r => r.Limited);
        var matched = results.Select(r => r.MatchedTitle).FirstOrDefault(t => t is not null);
        var lookup = new CompetitorLookup(offers, problems, matched, limited);

        if (offers.Count > 0)
        {
            // فقط نتیجهٔ موفق کش می‌شود؛ خطای موقت (مثلاً کپچای ترب) با کلیک بعدی دوباره امتحان می‌شود.
            cache.Set(key, lookup, TimeSpan.FromMinutes(30));
            if (store is not null)
                await store.SaveAsync(storeKey, offers, matched, cancellationToken);
            return lookup;
        }

        if (stored is { Offers.Count: > 0 } && now - stored.FetchedAt < StaleFor)
        {
            var hours = Math.Max(1, (int)Math.Round((now - stored.FetchedAt).TotalHours));
            problems.Add($"منابع الان قیمت تازه ندادند؛ آخرین قیمت‌های معتبر ({hours} ساعت پیش) نمایش داده شد.");
            return new CompetitorLookup(stored.Offers, problems, stored.MatchedTitle, limited, stored.FetchedAt);
        }
        return lookup;
    }

    /// <summary>
    /// داوری پاسخ: کدهای محدودیت (۴۹۰ کپچای ترب، ۴۰۳، ۴۲۹، ۵۰۳) یا صفحهٔ HTML چالش/کپچا به‌جای JSON ← محدودیت؛
    /// پاسخ ۲۰۰ با بدنه ← سالم؛ بقیه ← خطای موقت.
    /// <para>مثال: (490, "&lt;html&gt;captcha…") ← Blocked؛ (200, "{…}") ← Ok؛ (500, null) ← Failed.</para>
    /// </summary>
    public static SourceVerdict Classify(SupplierHttpResponse response)
    {
        if (response.Status is 490 or 403 or 429 or 503)
            return SourceVerdict.Blocked;
        if (response.Status is >= 200 and < 300 && response.Body is { } body)
        {
            var trimmed = body.TrimStart();
            if (trimmed.StartsWith('<'))
                return LooksLikeChallenge(trimmed) ? SourceVerdict.Blocked : SourceVerdict.Failed;
            return SourceVerdict.Ok;
        }
        return SourceVerdict.Failed;
    }

    /// <summary>صفحهٔ HTML کپچا/چالش ضدربات (arCAPTCHA، Cloudflare، صفحهٔ «anomaly» داک‌داک‌گو، …). موتور سئو هم از همین استفاده می‌کند.</summary>
    public static bool LooksLikeChallenge(string html)
    {
        var head = html.Length > 20_000 ? html[..20_000] : html;
        return head.Contains("captcha", StringComparison.OrdinalIgnoreCase)
               || head.Contains("cf-chl", StringComparison.OrdinalIgnoreCase)
               || head.Contains("challenge-platform", StringComparison.OrdinalIgnoreCase)
               || head.Contains("are you a robot", StringComparison.OrdinalIgnoreCase)
               || head.Contains("anomaly-modal", StringComparison.OrdinalIgnoreCase)
               || head.Contains("ربات نیستید", StringComparison.Ordinal);
    }

    /// <summary>اجرای یک منبع با سقف زمان و تبدیل هر خطا به پیام فارسی (یک منبع خراب، بقیه را خراب نمی‌کند).</summary>
    private async Task<CompetitorLookup> GuardAsync(string name, Func<CancellationToken, Task<CompetitorLookup>> run, CancellationToken cancellationToken,
        TimeSpan? limit = null)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(limit ?? SourceTimeout);
        try
        {
            return await run(timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new CompetitorLookup([], [$"{name}: پاسخ در زمان مجاز نرسید."], null);
        }
        catch (Exception ex) when (ex is JsonException or FormatException or InvalidOperationException or KeyNotFoundException)
        {
            gate.ReportFailure(name, "پاسخ قابل خواندن نبود");
            return new CompetitorLookup([], [$"{name}: پاسخ قابل خواندن نبود."], null);
        }
    }

    /// <summary>
    /// یک درخواست مؤدب: نوبت از دروازه، درخواست، داوری و گزارش به دروازه.
    /// خروجی: پاسخ سالم یا پیام مشکل (و اینکه مشکل از نوع «محدودیت» بود یا نه).
    /// </summary>
    private async Task<(SupplierHttpResponse? Response, string? Problem, bool Limited)> FetchAsync(string source, string url, TimeSpan interval, CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string>? form = null)
    {
        using var lease = await gate.TryEnterAsync(source, interval, cancellationToken);
        if (lease is null)
        {
            var minutes = gate.CoolingUntil(source) is { } until ? Math.Max(1, (int)Math.Ceiling((until - DateTimeOffset.UtcNow).TotalMinutes)) : 1;
            return (null, $"{source}: به‌خاطر محدودیت قبلی موقتاً درخواستی فرستاده نمی‌شود (حدود {minutes} دقیقهٔ دیگر دوباره امتحان می‌شود).", true);
        }

        var response = form is null
            ? await http.GetAsync(url, expectJson: true, cancellationToken)
            : await http.PostFormAsync(url, form, expectJson: true, cancellationToken);
        switch (Classify(response))
        {
            case SourceVerdict.Ok:
                gate.ReportSuccess(source);
                return (response, null, false);
            case SourceVerdict.Blocked:
                gate.ReportBlocked(source, null, $"کد {response.Status}");
                return (null, $"{source}: موقتاً درخواست را محدود کرده است (کپچا/محدودیت). تا رفع محدودیت، درخواستی به آن فرستاده نمی‌شود.", true);
            default:
                var reason = response.Status == 0 ? response.Error ?? "خطای اتصال" : "کد " + response.Status;
                gate.ReportFailure(source, reason);
                return (null, $"{source}: در دسترس نبود ({reason}).", false);
        }
    }

    private async Task<CompetitorLookup> TorobAsync(string query, CancellationToken cancellationToken)
    {
        var url = "https://api.torob.com/v4/base-product/search/?page=0&size=24&source=next_desktop&q=" + Uri.EscapeDataString(query);
        var (search, problem, limited) = await FetchAsync(Torob, url, TorobInterval, cancellationToken);
        if (search is null)
            return new CompetitorLookup([], [problem!], null, limited);

        var cards = CompetitorParsers.TorobSearch(search.Body!);
        var best = cards
            .Select(c => (Card: c, Score: CompetitorParsers.TitleScore(query, c.Title)))
            .Where(x => x.Score >= CompetitorParsers.MinScore)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Card.Price is > 0 ? 0 : 1)
            .Select(x => x.Card)
            .FirstOrDefault();
        if (best is null)
            return new CompetitorLookup([], ["ترب: کالای مشابهی پیدا نشد (نام یا مدل را دقیق‌تر بنویسید)."], null);

        var detailsLimited = false;
        if (best.DetailsUrl is { } details && CompetitorParsers.IsTorobDetailsUrl(details))
        {
            var (page, _, pageLimited) = await FetchAsync(Torob, details, TorobInterval, cancellationToken);
            detailsLimited = pageLimited;
            if (page is not null)
            {
                var offers = CompetitorParsers.TorobOffers(page.Body!, best.Title);
                if (offers.Count > 0)
                    return new CompetitorLookup(offers, [], best.Title);
            }
        }

        // جزئیات در دسترس نبود: دست‌کم ارزان‌ترین قیمت کارت جست‌وجو.
        return best.Price is > 0
            ? new CompetitorLookup([new CompetitorOffer("ارزان‌ترین فروشگاه ترب", best.Price.Value, Torob, best.WebUrl, best.Title)],
                ["ترب: فهرست فروشندگان در دسترس نبود؛ فقط ارزان‌ترین قیمت آمد."], best.Title, detailsLimited)
            : new CompetitorLookup([], ["ترب: این کالا فعلاً فروشندهٔ موجود ندارد."], best.Title);
    }

    private async Task<CompetitorLookup> DigikalaAsync(string query, CancellationToken cancellationToken)
    {
        var url = "https://api.digikala.com/v1/search/?page=1&q=" + Uri.EscapeDataString(query);
        var (response, problem, limited) = await FetchAsync(DigikalaSource, url, DefaultInterval, cancellationToken);
        if (response is null)
            return new CompetitorLookup([], [problem!], null, limited);

        var best = CompetitorParsers.Digikala(response.Body!)
            .Select(p => (Item: p, Score: CompetitorParsers.TitleScore(query, p.Title)))
            .Where(x => x.Score >= CompetitorParsers.MinScore)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Item.Price)
            .Select(x => x.Item)
            .FirstOrDefault();
        return best is null
            ? new CompetitorLookup([], ["دیجی‌کالا: کالای مشابه موجود پیدا نشد."], null)
            : new CompetitorLookup([best], [], null);
    }

    private async Task<CompetitorLookup> BasalamAsync(string query, CancellationToken cancellationToken)
    {
        var url = "https://search.basalam.com/ai-engine/api/v2.0/product/search?q=" + Uri.EscapeDataString(query);
        var (response, problem, limited) = await FetchAsync(Basalam, url, DefaultInterval, cancellationToken);
        if (response is null)
            return new CompetitorLookup([], [problem!], null, limited);

        // تا ۳ فروشندهٔ متفاوت با بیشترین شباهت؛ «پیدا نشد» برای این منبع تکمیلی پیام نمی‌دهد.
        var offers = CompetitorParsers.Basalam(response.Body!)
            .Select(p => (Item: p, Score: CompetitorParsers.TitleScore(query, p.Title)))
            .Where(x => x.Score >= CompetitorParsers.MinScore)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Item.Price)
            .Select(x => x.Item)
            .DistinctBy(o => o.Shop)
            .Take(5)
            .ToList();
        return new CompetitorLookup(offers, [], null);
    }

    /// <summary>
    /// دور ۲۴ (پیشنهاد ۴۵): جست‌وجوی ایمالز. منبع تکمیلی است: نبودن نتیجه یا در دسترس نبودن آن پیامی به فهرست مشکلات اضافه نمی‌کند
    /// (فقط محدودیت/کپچا گزارش می‌شود)، تا ۳ فروشندهٔ متفاوت با بیشترین شباهت عنوان. فاصلهٔ مؤدبانه مثل ترب (۱٫۵ ثانیه).
    /// </summary>
    private async Task<CompetitorLookup> EmallsAsync(string query, CancellationToken cancellationToken)
    {
        var form = new Dictionary<string, string>
        {
            ["entekhab"] = "listitemv2",
            ["findstr"] = query,
            ["pagenum"] = "1",
            ["minprice"] = "0",
            ["maxprice"] = "0",
            ["exist"] = "1",
            ["shop"] = "0",
            ["sort"] = "pa",
        };
        var (response, problem, limited) = await FetchAsync(Emalls, EmallsSearchUrl, TorobInterval, cancellationToken, form);
        if (response is null)
            return new CompetitorLookup([], limited ? new[] { problem! } : Array.Empty<string>(), null, limited);

        var offers = CompetitorParsers.Emalls(response.Body!)
            .Select(p => (Item: p, Score: CompetitorParsers.TitleScore(query, p.Title)))
            .Where(x => x.Score >= CompetitorParsers.MinScore)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Item.Price)
            .Select(x => x.Item)
            .DistinctBy(o => o.Shop)
            .Take(3)
            .ToList();
        return new CompetitorLookup(offers, [], null);
    }

    /// <summary>
    /// منبع «سایت‌های دیگر» (مرحلهٔ پانزدهم): همهٔ فروشگاه‌های اینترنتی که این کالا را می‌فروشند، نه فقط ترب/دیجی‌کالا/باسلام.
    /// <para>روند: ۱) جست‌وجوی «نام کالا قیمت» در بینگ (اگر نشد داک‌داک‌گو) از راه <see cref="ISeoWebSource"/> با همان دروازهٔ
    /// محدودیت موتور سئو؛ ۲) از نتایج، صفحه‌های فروشگاهی (نه شبکهٔ اجتماعی، ویکی، دیوار، و نه سه منبع اصلی) و از هر دامنه یکی،
    /// حداکثر ۸ صفحه؛ ۳) هم‌زمان باز کردن صفحه‌ها (هر دامنه با فاصلهٔ مؤدبانه، محافظ SSRF و سقف حجم ۱٫۵ مگابایت)؛
    /// ۴) خواندن قیمت از دادهٔ ساخت‌یافته (<see cref="CompetitorParsers.ShopPage"/>)؛ ۵) عنوان با نام کالای ما سنجیده می‌شود.</para>
    /// <para>هر صفحه‌ای تا ۱۴ ثانیه جواب نداد رها می‌شود تا کارمند منتظر نماند. مثال: «هدفون هایلو T15» ← تکنولایف ۲۸۹ هزار، فروشگاه X ۲۹۵ هزار.</para>
    /// </summary>
    private async Task<CompetitorLookup> WebShopsAsync(string query, StrongBox<IReadOnlyList<WebPagePrice>> unclear, CancellationToken cancellationToken)
    {
        var serp = await web!.SearchAsync(query + " قیمت", cancellationToken);
        if (!serp.Ok || serp.Value is null)
        {
            var error = serp.Error ?? "";
            var limited = error.Contains("استراحت", StringComparison.Ordinal) || error.Contains("محدودیت", StringComparison.Ordinal)
                          || error.Contains("کپچا", StringComparison.Ordinal);
            return new CompetitorLookup([], [$"{WebShops}: موتور جست‌وجو فعلاً نتیجه نداد ({(error.Length > 120 ? error[..120] : error)})."], null, limited);
        }

        var urls = serp.Value.Select(r => r.Url)
            .Where(CompetitorParsers.IsShopCandidate)
            .DistinctBy(u => PriceAdvisor.HostOf(u))
            .Take(MaxWebPages)
            .ToList();
        if (urls.Count == 0)
            return new CompetitorLookup([], [$"{WebShops}: در نتایج جست‌وجو فروشگاه دیگری پیدا نشد."], null);

        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var pages = urls.Select(url => ReadShopPageAsync(url, query, budget.Token)).ToList();
        await Task.WhenAny(Task.WhenAll(pages), Task.Delay(WebPagesBudget, cancellationToken));
        cancellationToken.ThrowIfCancellationRequested();
        budget.Cancel(); // صفحه‌های جانمانده رها می‌شوند (خطایشان داخل ReadShopPageAsync گرفته می‌شود)

        var found = pages.Where(t => t.IsCompletedSuccessfully && t.Result is not null).Select(t => t.Result!).ToList();
        var offers = found.Where(f => f.Currency is not null)
            .Select(f => new CompetitorOffer(f.Shop, f.Price, WebShops, f.Url, f.Title))
            .ToList();
        unclear.Value = found.Where(f => f.Currency is null).ToList();

        var problems = new List<string>();
        if (found.Count == 0)
            problems.Add($"{WebShops}: {urls.Count} صفحهٔ فروشگاهی بررسی شد ولی قیمت قابل اطمینانی (کالای مشابه و موجود) در آن‌ها نبود.");
        return new CompetitorLookup(offers, problems, null);
    }

    /// <summary>یک صفحهٔ فروشگاه را می‌خواند و قیمت را برمی‌گرداند؛ هر خطایی (شبکه، لغو، HTML خراب) ← null.</summary>
    private async Task<WebPagePrice?> ReadShopPageAsync(string url, string query, CancellationToken cancellationToken)
    {
        try
        {
            var page = await web!.FetchPageAsync(url, cancellationToken);
            return page.Ok && page.Value is not null ? CompetitorParsers.ShopPage(page.Value, url, query) : null;
        }
        catch (Exception exception) when (exception is OperationCanceledException or HttpRequestException or JsonException
                                              or RegexMatchTimeoutException or InvalidOperationException or ArgumentException)
        {
            return null;
        }
    }
}

/// <summary>
/// قیمت خوانده‌شده از صفحهٔ یک فروشگاه اینترنتی (مرحلهٔ پانزدهم).
/// </summary>
/// <param name="Shop">نام فروشگاه (og:site_name) یا دامنه. مثال: «تکنولایف» یا «example-shop.ir».</param>
/// <param name="Url">آدرس صفحه.</param>
/// <param name="Title">نام کالا در آن صفحه.</param>
/// <param name="Price">قیمت؛ اگر <paramref name="Currency"/> معلوم باشد به تومان، وگرنه عدد خام صفحه.</param>
/// <param name="Currency">«IRT» (تومان) یا «IRR» (ریال، قبلاً به تومان تبدیل شده)؛ null یعنی صفحه واحد را نگفته بود.</param>
/// <param name="Score">شباهت عنوان با نام کالای ما (۰ تا ۱).</param>
public sealed record WebPagePrice(string Shop, string Url, string? Title, decimal Price, string? Currency, double Score);

/// <summary>
/// نتیجهٔ سنجش شباهت عنوان (مرحلهٔ پانزدهم).
/// </summary>
/// <param name="Score">۰ تا ۱؛ از <see cref="CompetitorParsers.MinScore"/> کمتر یعنی کالای دیگر.</param>
/// <param name="MissingCode">کد مدل/عدد نام ما (مثل T15 یا 256gb) در عنوان نیست.</param>
/// <param name="Accessory">عنوان واژهٔ لوازم جانبی (قاب، کاور، محافظ …) دارد که در نام ما نیست.</param>
public sealed record TitleMatch(double Score, bool MissingCode, bool Accessory);

/// <summary>یک کارت نتیجهٔ جست‌وجوی ترب.</summary>
/// <param name="Title">نام کالا (name1).</param>
/// <param name="Price">ارزان‌ترین قیمت (تومان)؛ null یعنی ناموجود.</param>
/// <param name="DetailsUrl">آدرس API جزئیات (more_info_url).</param>
/// <param name="Shop">متن فروشگاه کارت (shop_text)، مثلاً «در ۱۲ فروشگاه».</param>
/// <param name="WebUrl">صفحهٔ کالا در torob.com.</param>
public sealed record TorobCard(string Title, decimal? Price, string? DetailsUrl, string? Shop, string? WebUrl);

/// <summary>
/// خواندن پاسخ‌های JSON ترب و دیجی‌کالا و سنجش شباهت عنوان (خالص و تست‌پذیر؛ نمونه‌ها در تست‌ها).
/// <para>ساختارها طبق API عمومی که خود سایت‌ها استفاده می‌کنند:
/// ترب: <c>results[].{name1, price, more_info_url, web_client_absolute_url, shop_text}</c> و
/// جزئیات <c>products_info.result[].{shop_name, shop_id, price, price_text, availability, is_price_unreliable, page_url}</c>؛
/// دیجی‌کالا: <c>data.products[].{id, title_fa, url.uri, default_variant.price.selling_price (ریال)}</c>.</para>
/// </summary>
public static class CompetitorParsers
{
    /// <summary>کمترین شباهت عنوان برای پذیرفتن یک نتیجه (۰ تا ۱).</summary>
    public const double MinScore = 0.6;

    // واژه‌هایی که در شباهت نام کالا معنایی ندارند.
    private static readonly HashSet<string> StopWords = ["و", "با", "از", "در", "به", "برای", "مدل", "رنگ", "اصلی", "اورجینال", "جدید", "the", "with", "for", "and", "model"];

    // نشانهٔ لوازم جانبی: «قاب هدفون T15» همهٔ واژه‌های «هدفون T15» را دارد ولی کالای دیگری است و قیمتش خیلی کمتر.
    private static readonly HashSet<string> AccessoryWords = ["قاب", "کاور", "محافظ", "گلس", "بند", "کیف", "لوازم", "جانبی", "استند", "case", "cover", "strap", "protector"];

    /// <summary>کارت‌های نتیجهٔ جست‌وجوی ترب.</summary>
    public static List<TorobCard> TorobSearch(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var list = new List<TorobCard>();
        if (doc.RootElement.ValueKind != JsonValueKind.Object
            || !doc.RootElement.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
            return list;
        foreach (var item in results.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
                continue;
            var title = Text(item, "name1") ?? Text(item, "name2");
            if (string.IsNullOrWhiteSpace(title))
                continue;
            var web = Text(item, "web_client_absolute_url");
            list.Add(new TorobCard(
                Clip(title, 200)!,
                Money(item, "price") ?? FromText(Text(item, "price_text")),
                Text(item, "more_info_url"),
                Clip(Text(item, "shop_text"), 80),
                web is null ? null : web.StartsWith('/') ? "https://torob.com" + web : web));
        }
        return list;
    }

    /// <summary>
    /// فروشندگان یک کالای ترب. فروشندهٔ ناموجود و قیمتی که خود ترب «غیرقابل اعتماد» علامت زده کنار می‌روند.
    /// </summary>
    public static List<CompetitorOffer> TorobOffers(string json, string? title)
    {
        using var doc = JsonDocument.Parse(json);
        var offers = new List<CompetitorOffer>();
        if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty("products_info", out var info) || info.ValueKind != JsonValueKind.Object
            || !info.TryGetProperty("result", out var result) || result.ValueKind != JsonValueKind.Array)
            return offers;
        foreach (var row in result.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Object)
                continue;
            var shop = Clip(Text(row, "shop_name"), 80);
            if (string.IsNullOrWhiteSpace(shop))
                continue;
            if (row.TryGetProperty("availability", out var available) && available.ValueKind == JsonValueKind.False)
                continue;
            if (row.TryGetProperty("is_price_unreliable", out var unreliable) && unreliable.ValueKind == JsonValueKind.True)
                continue;
            var price = Money(row, "price") ?? FromText(Text(row, "price_text") ?? Text(row, "price_string"));
            if (price is not > 0)
                continue;
            var shopId = Text(row, "shop_id");
            var url = !string.IsNullOrEmpty(shopId) && shopId.All(char.IsDigit) ? $"https://torob.com/shop/{shopId}/" : null;
            offers.Add(new CompetitorOffer(shop, price.Value, "ترب", url, Clip(title, 200)));
        }
        return offers;
    }

    /// <summary>کالاهای موجود جست‌وجوی دیجی‌کالا (قیمت از ریال به تومان).</summary>
    public static List<CompetitorOffer> Digikala(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var list = new List<CompetitorOffer>();
        if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object
            || !data.TryGetProperty("products", out var products) || products.ValueKind != JsonValueKind.Array)
            return list;
        foreach (var p in products.EnumerateArray())
        {
            if (p.ValueKind != JsonValueKind.Object)
                continue;
            var title = Text(p, "title_fa") ?? Text(p, "title_en");
            if (string.IsNullOrWhiteSpace(title))
                continue;
            if (!p.TryGetProperty("default_variant", out var variant) || variant.ValueKind != JsonValueKind.Object)
                continue; // ناموجود: دیجی‌کالا تنوع پیش‌فرض نمی‌فرستد
            if (variant.TryGetProperty("price", out var price) && price.ValueKind == JsonValueKind.Object
                && Money(price, "selling_price") is > 0 and var rial)
            {
                var uri = p.TryGetProperty("url", out var u) && u.ValueKind == JsonValueKind.Object ? Text(u, "uri") : null;
                var id = p.TryGetProperty("id", out var idElement) && idElement.ValueKind == JsonValueKind.Number ? idElement.GetRawText() : null;
                var link = uri is not null && uri.StartsWith('/') ? "https://www.digikala.com" + uri
                    : id is not null ? $"https://www.digikala.com/product/dkp-{id}/" : null;
                list.Add(new CompetitorOffer("دیجی‌کالا", Math.Round(rial / 10m), "دیجی‌کالا", link, Clip(title, 200)));
            }
        }
        return list;
    }

    /// <summary>
    /// محصولات جست‌وجوی باسلام: <c>products[].{id, name, price (ریال), vendor.name}</c> ← پیشنهاد با نام «باسلام – غرفه» و قیمت تومانی.
    /// <para>مثال: {"products":[{"id":12,"name":"هدفون هایلو T15","price":2950000,"vendor":{"name":"غرفه صوتی"}}]} ←
    /// («باسلام – غرفه صوتی»، ۲۹۵٬۰۰۰ تومان، https://basalam.com/p/12).</para>
    /// </summary>
    public static List<CompetitorOffer> Basalam(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var list = new List<CompetitorOffer>();
        if (doc.RootElement.ValueKind != JsonValueKind.Object
            || !doc.RootElement.TryGetProperty("products", out var products) || products.ValueKind != JsonValueKind.Array)
            return list;
        foreach (var p in products.EnumerateArray())
        {
            if (p.ValueKind != JsonValueKind.Object)
                continue;
            var title = Text(p, "name") ?? Text(p, "title");
            if (string.IsNullOrWhiteSpace(title) || Money(p, "price") is not { } rial)
                continue;
            var vendor = p.TryGetProperty("vendor", out var v) && v.ValueKind == JsonValueKind.Object ? Clip(Text(v, "name") ?? Text(v, "title"), 60) : null;
            var id = Text(p, "id");
            var link = !string.IsNullOrEmpty(id) && id.All(char.IsAsciiDigit) ? $"https://basalam.com/p/{id}" : null;
            list.Add(new CompetitorOffer(string.IsNullOrWhiteSpace(vendor) ? "باسلام" : "باسلام – " + vendor, Math.Round(rial / 10m), "باسلام", link, Clip(title, 200)));
        }
        return list;
    }

    /// <summary>
    /// دور ۲۴ (پیشنهاد ۴۵): نتیجهٔ جست‌وجوی ایمالز (قیمت‌ها <b>تومان</b> است). ساختار پاسخ رسمی منتشر نشده، پس خواندن «انعطاف‌پذیر» است:
    /// <list type="bullet">
    /// <item>JSON: آرایهٔ <c>lstsearchresualt</c> (یا هر آرایهٔ اولِ ریشه) با نام‌های رایج فیلد (title/name، price، shopname/shop، id، url).</item>
    /// <item>HTML (اگر به‌جای JSON صفحهٔ فهرست برگشت): پیوندهای کالا (<c>…~id~123</c>) با عنوان (alt تصویر یا خط اول) و آخرین عدد متن به‌عنوان قیمت.</item>
    /// </list>
    /// قیمت صفر/نامعتبر یا عنوان خالی کنار گذاشته می‌شود؛ هیچ‌وقت استثنا نمی‌دهد.
    /// <para>مثال: {"lstsearchresualt":[{"title":"هدفون هایلو T15","price":"۲,۹۵۰,۰۰۰","shopname":"صوتی‌شاپ","id":"123"}]} ←
    /// («ایمالز – صوتی‌شاپ»، ۲٬۹۵۰٬۰۰۰ تومان، https://emalls.ir/~id~123).</para>
    /// </summary>
    public static List<CompetitorOffer> Emalls(string body)
    {
        var list = new List<CompetitorOffer>();
        if (string.IsNullOrWhiteSpace(body))
            return list;
        var trimmed = body.TrimStart();
        if (trimmed.StartsWith('<'))
            return EmallsHtml(trimmed);
        try
        {
            using var doc = JsonDocument.Parse(trimmed);
            var root = doc.RootElement;
            JsonElement items = default;
            if (root.ValueKind == JsonValueKind.Array)
                items = root;
            else if (root.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in root.EnumerateObject())
                {
                    if (property.Value.ValueKind == JsonValueKind.Array
                        && (property.Name.Equals("lstsearchresualt", StringComparison.OrdinalIgnoreCase) || items.ValueKind != JsonValueKind.Array))
                        items = property.Value;
                }
            }
            if (items.ValueKind != JsonValueKind.Array)
                return list;
            foreach (var item in items.EnumerateArray().Take(60))
            {
                if (item.ValueKind != JsonValueKind.Object)
                    continue;
                var title = Pick(item, "title", "name", "titr", "onvan", "productname");
                var price = ParsePrice(Pick(item, "price", "minprice", "lowprice", "pricenum"));
                if (string.IsNullOrWhiteSpace(title) || price is not > 0)
                    continue;
                var shop = Clip(Pick(item, "shopname", "shoptitle", "shop", "storename"), 60);
                var id = Pick(item, "id", "productid", "pid");
                var url = Pick(item, "url", "link", "href");
                var link = EmallsLink(url, id);
                list.Add(new CompetitorOffer(string.IsNullOrWhiteSpace(shop) || shop.All(char.IsAsciiDigit) ? "ایمالز" : "ایمالز – " + shop,
                    price.Value, CompetitorPriceService.Emalls, link, Clip(title, 200)));
            }
        }
        catch (JsonException)
        {
            return list;
        }
        return list;
    }

    /// <summary>خواندن صفحهٔ HTML فهرست ایمالز (پشتیبان وقتی JSON نیامد).</summary>
    private static List<CompetitorOffer> EmallsHtml(string html)
    {
        var list = new List<CompetitorOffer>();
        var document = new AngleSharp.Html.Parser.HtmlParser().ParseDocument(html.Length > 1_500_000 ? html[..1_500_000] : html);
        foreach (var anchor in document.QuerySelectorAll("a[href*='~id~']").Take(60))
        {
            var text = anchor.TextContent ?? "";
            var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var title = anchor.QuerySelector("img[alt]")?.GetAttribute("alt") ?? lines.FirstOrDefault();
            var priceText = Regex.Matches(text, "[0-9۰-۹][0-9۰-۹,٬]{3,}").LastOrDefault()?.Value;
            var price = ParsePrice(priceText);
            if (string.IsNullOrWhiteSpace(title) || price is not > 0)
                continue;
            list.Add(new CompetitorOffer("ایمالز", price.Value, CompetitorPriceService.Emalls, EmallsLink(anchor.GetAttribute("href"), null), Clip(title, 200)));
        }
        return list;
    }

    /// <summary>پیوند امن کالا در ایمالز: نشانی نسبی ← کامل؛ فقط دامنهٔ emalls.ir؛ وگرنه از شناسهٔ عددی ساخته می‌شود.</summary>
    public static string? EmallsLink(string? url, string? id)
    {
        if (!string.IsNullOrWhiteSpace(url))
        {
            var value = url.Trim();
            if (value.StartsWith('/'))
                value = "https://emalls.ir" + value;
            if (Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps
                && (uri.Host == "emalls.ir" || uri.Host.EndsWith(".emalls.ir", StringComparison.OrdinalIgnoreCase)))
                return uri.AbsoluteUri;
        }
        return !string.IsNullOrEmpty(id) && id.Length <= 12 && id.All(char.IsAsciiDigit) ? $"https://emalls.ir/~id~{id}" : null;
    }

    /// <summary>اولین فیلد موجود از میان نام‌ها (بدون حساسیت به حروف بزرگ/کوچک).</summary>
    private static string? Pick(JsonElement element, params string[] names)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (!names.Any(n => property.Name.Equals(n, StringComparison.OrdinalIgnoreCase)))
                continue;
            var value = property.Value.ValueKind switch
            {
                JsonValueKind.String => property.Value.GetString(),
                JsonValueKind.Number => property.Value.GetRawText(),
                _ => null,
            };
            if (!string.IsNullOrWhiteSpace(value))
                return value.Trim();
        }
        return null;
    }

    /// <summary>
    /// شباهت عنوان یک نتیجه با نام کالای ما (۰ تا ۱): چه سهمی از واژه‌های نام ما در عنوان آمده.
    /// اگر کد مدل/عدد نام ما (مثل T15 یا 256gb) در عنوان نباشد، امتیاز نصف می‌شود (مدل دیگر است)؛
    /// اگر عنوان واژهٔ لوازم جانبی (قاب، کاور، محافظ …) داشته باشد که در نام ما نیست هم نصف می‌شود.
    /// «256gb» با «256 گیگابایت» یکی حساب می‌شود.
    /// <para>مثال: («هدفون هایلو T15»، «هدفون بی‌سیم هایلو مدل T15») ← ۱؛ («هدفون هایلو T15»، «هدفون هایلو GT1») ← ۰٫۳۳؛
    /// («هدفون هایلو T15»، «قاب هدفون هایلو T15») ← ۰٫۵.</para>
    /// </summary>
    public static double TitleScore(string query, string? title) => TitleCheck(query, title).Score;

    /// <summary>
    /// همان <see cref="TitleScore"/> به‌همراه دلیل (مرحلهٔ پانزدهم، برای نمایش «چرا این قیمت حساب نشد»).
    /// <para>مثال: («هدفون هایلو T15»، «هدفون هایلو GT1») ← (۰٫۳۳، MissingCode: true، Accessory: false)؛
    /// («هدفون هایلو T15»، «قاب هدفون هایلو T15») ← (۰٫۵، false، true).</para>
    /// </summary>
    public static TitleMatch TitleCheck(string query, string? title)
    {
        var wanted = Words(query ?? "");
        if (wanted.Count == 0 || string.IsNullOrWhiteSpace(title))
            return new TitleMatch(0, false, false);
        var have = Words(title);
        var matched = 0;
        var missingCode = false;
        foreach (var word in wanted)
        {
            if (Found(word, have))
                matched++;
            else if (word.Any(char.IsDigit))
                missingCode = true;
        }
        var score = (double)matched / wanted.Count;
        if (missingCode)
            score /= 2;
        var accessory = have.Any(h => AccessoryWords.Contains(h) && !wanted.Contains(h));
        if (accessory)
            score /= 2;
        return new TitleMatch(score, missingCode, accessory);
    }

    /// <summary>
    /// آیا واژهٔ نام ما در عنوان هست؟ برابر، پیشوند (برای واژه‌های ۴ حرفی به بالا: «هایلو» در «هایلوی»)،
    /// یا عدد با واحد: «256gb» ↔ «256»، و «256» ↔ «256gb». «t15» با «t150» برابر نیست.
    /// </summary>
    private static bool Found(string word, HashSet<string> have)
    {
        if (have.Contains(word))
            return true;
        if (word.Length >= 4 && have.Any(h => h.StartsWith(word, StringComparison.Ordinal)))
            return true;
        var digits = new string(word.TakeWhile(char.IsAsciiDigit).ToArray());
        if (digits.Length == 0)
            return false;
        var unit = word[digits.Length..];
        if (unit.Length > 0 && unit.All(char.IsLetter) && have.Contains(digits))
            return true;
        return unit.Length == 0 && have.Any(h => h.Length > word.Length && h.StartsWith(word, StringComparison.Ordinal) && h[word.Length..].All(char.IsLetter));
    }

    // ── مرحلهٔ پانزدهم: فروشگاه‌های اینترنتی دیگر ──

    /// <summary>دامنه‌هایی که صفحهٔ فروش کالا نیستند یا منبع جداگانه دارند (ترب/دیجی‌کالا/باسلام) — زیردامنه‌ها هم.</summary>
    private static readonly string[] NotShopHosts =
    [
        "torob.com", "digikala.com", "basalam.com", "emalls.ir", "aparat.com", "youtube.com", "instagram.com", "facebook.com", "twitter.com", "x.com",
        "t.me", "telegram.me", "wikipedia.org", "wikiwand.com", "linkedin.com", "pinterest.com", "google.com", "bing.com", "duckduckgo.com",
        "divar.ir", "sheypoor.com", "virgool.io", "blogfa.com", "blogsky.com", "namnak.com", "reddit.com", "quora.com", "github.com",
    ];

    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(250);
    private static readonly Regex JsonLdRegex = new(@"<script[^>]*type\s*=\s*[""']application/ld\+json[""'][^>]*>(.*?)</script>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant, RegexTimeout);
    private static readonly Regex TagRegex = new(@"<(?:meta|span|div|p|ins|bdi|data|link)\b[^>]{0,600}>",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeout);
    private static readonly Regex AttributeRegex = new(@"([a-zA-Z:_-]+)\s*=\s*(?:""([^""]*)""|'([^']*)')",
        RegexOptions.CultureInvariant, RegexTimeout);
    private static readonly Regex TitleRegex = new(@"<title[^>]*>(.*?)</title>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant, RegexTimeout);

    /// <summary>
    /// آیا این نتیجهٔ جست‌وجو ارزش باز کردن برای قیمت دارد؟ فقط http/https، نه شبکهٔ اجتماعی/ویکی/آگهی و نه ترب/دیجی‌کالا/باسلام
    /// (آن‌ها منبع جداگانه دارند) و نه صفحهٔ اول سایت.
    /// <para>مثال: «https://shop.example.ir/product/haylou-t15» ← true؛ «https://torob.com/p/…» ← false؛ «https://www.aparat.com/v/1» ← false.</para>
    /// </summary>
    public static bool IsShopCandidate(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
            || !uri.IsDefaultPort || uri.HostNameType != UriHostNameType.Dns || !uri.Host.Contains('.'))
            return false;
        var host = uri.Host.ToLowerInvariant();
        if (NotShopHosts.Any(h => host == h || host.EndsWith("." + h, StringComparison.Ordinal)))
            return false;
        return uri.AbsolutePath.Trim('/').Length > 0;
    }

    /// <summary>
    /// قیمت کالا از HTML صفحهٔ یک فروشگاه (بدون حدس از متن آزاد؛ فقط دادهٔ ساخت‌یافته‌ای که خود فروشگاه برای گوگل گذاشته):
    /// <list type="number">
    /// <item>JSON-LD از نوع Product با offers (Offer یا AggregateOffer؛ price یا lowPrice و priceCurrency)؛ ناموجود (OutOfStock) کنار می‌رود.</item>
    /// <item>اگر نبود: متای product:price:amount / og:price:amount یا itemprop="price" (با واحد product:price:currency یا itemprop="priceCurrency").</item>
    /// </list>
    /// <para>واحد: IRR/ریال ← تقسیم بر ۱۰؛ IRT/TOMAN/تومان ← همان؛ واحد خارجی ← کنار؛ بدون واحد ← Currency = null (بعداً با قیمت بقیه تصمیم گرفته می‌شود).</para>
    /// <para>اگر صفحه چند محصول داشت، شبیه‌ترین عنوان به نام کالای ما انتخاب می‌شود. خروجی null یعنی قیمت قابل اطمینانی نبود.</para>
    /// <para>مثال: JSON-LD {"@type":"Product","name":"هدفون هایلو T15","offers":{"price":"2890000","priceCurrency":"IRR"}} ← ۲۸۹٬۰۰۰ تومان.</para>
    /// </summary>
    public static WebPagePrice? ShopPage(string html, string url, string query)
    {
        var host = PriceAdvisor.HostOf(url);
        if (host is null || string.IsNullOrEmpty(html))
            return null;
        if (html.Length > 1_500_000)
            html = html[..1_500_000];

        var candidates = new List<(string? Title, decimal Price, string? Currency)>();
        try
        {
            foreach (Match match in JsonLdRegex.Matches(html))
            {
                var json = match.Groups[1].Value.Trim();
                if (json.Length is 0 or > 400_000)
                    continue;
                try
                {
                    using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip, MaxDepth = 64 });
                    CollectProducts(doc.RootElement, candidates, 0);
                }
                catch (JsonException)
                {
                    // JSON-LD خراب در بعضی قالب‌ها رایج است؛ سراغ بلوک بعدی یا متاها می‌رویم.
                }
                if (candidates.Count >= 20)
                    break;
            }

            var tags = TagRegex.Matches(html).Select(m => Attributes(m.Value)).ToList();
            var pageTitle = Meta(tags, "og:title") ?? Clean(TitleRegex.Match(html) is { Success: true } t ? t.Groups[1].Value : null);
            if (candidates.Count == 0)
            {
                var availability = Meta(tags, "product:availability") ?? Meta(tags, "og:availability") ?? ItemProp(tags, "availability");
                var raw = Meta(tags, "product:price:amount") ?? Meta(tags, "og:price:amount") ?? ItemProp(tags, "price");
                var currency = Meta(tags, "product:price:currency") ?? Meta(tags, "og:price:currency") ?? ItemProp(tags, "priceCurrency");
                if (!IsUnavailable(availability) && ParsePrice(raw) is { } price)
                    candidates.Add((pageTitle, price, currency));
            }

            var shop = Clip(Clean(Meta(tags, "og:site_name")), 60);
            var best = candidates
                .Select(c => (c.Title, c.Price, Currency: NormalizeCurrency(c.Currency), Name: c.Title ?? pageTitle))
                .Where(c => c.Currency != "foreign")
                .Select(c => (c.Name, Price: c.Currency == "IRR" ? Math.Round(c.Price / 10m) : c.Price, c.Currency, Score: TitleScore(query, c.Name)))
                .Where(c => c.Price >= 1_000)
                .OrderByDescending(c => c.Score).ThenBy(c => c.Price)
                .FirstOrDefault();
            if (best.Price <= 0)
                return null;
            return new WebPagePrice(string.IsNullOrWhiteSpace(shop) ? host : shop!, url, Clip(best.Name, 200), best.Price, best.Currency, best.Score);
        }
        catch (RegexMatchTimeoutException)
        {
            return null; // HTML بیمارگونه؛ وقت سرور هدر نمی‌رود
        }
    }

    /// <summary>
    /// قیمت‌های بدون واحد سایت‌های دیگر: با میانهٔ قیمت بقیهٔ منابع (ترب/دیجی‌کالا/…) تصمیم می‌گیریم که عدد تومان است یا ریال.
    /// نزدیک‌ترین تعبیر به میانه انتخاب می‌شود و فقط اگر بین ۴۰٪ تا ۲۵۰٪ میانه باشد پذیرفته می‌شود؛ بدون مرجع ← کنار.
    /// <para>مثال: میانهٔ بقیه ۲۸۰ هزار، صفحه «2900000» بدون واحد ← ۲۹۰ هزار تومان (ریال بوده)؛ صفحه «285000» ← ۲۸۵ هزار.</para>
    /// </summary>
    public static List<CompetitorOffer> ResolveCurrency(IReadOnlyList<WebPagePrice> unclear, IReadOnlyList<CompetitorOffer> known)
    {
        var list = new List<CompetitorOffer>();
        var prices = known.Where(o => o.Price > 0).Select(o => o.Price).ToList();
        if (prices.Count == 0)
            return list;
        var reference = PriceAdvisor.Median(prices);
        foreach (var item in unclear)
        {
            var asToman = item.Price;
            var asRial = Math.Round(item.Price / 10m);
            var pick = Math.Abs(Math.Log((double)(asToman / reference))) <= Math.Abs(Math.Log((double)Math.Max(1, asRial) / (double)reference)) ? asToman : asRial;
            if (pick >= reference * PriceAdvisor.LowOutlier && pick <= reference * PriceAdvisor.HighOutlier)
                list.Add(new CompetitorOffer(item.Shop, pick, CompetitorPriceService.WebShops, item.Url, item.Title));
        }
        return list;
    }

    /// <summary>
    /// عدد قیمت از متن ساخت‌یافته: رقم فارسی/عربی، جداکنندهٔ هزارگان و اعشار. مثال: «2,890,000» ← ۲۸۹۰۰۰۰؛ «۲۸۹۰۰۰۰.00» ← ۲۸۹۰۰۰۰؛ «رایگان» ← null.
    /// </summary>
    public static decimal? ParsePrice(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 40)
            return null;
        var chars = text.Trim().Select(c => c switch
        {
            >= '۰' and <= '۹' => (char)('0' + (c - '۰')),
            >= '٠' and <= '٩' => (char)('0' + (c - '٠')),
            '٫' => '.',
            _ => c,
        }).Where(c => char.IsAsciiDigit(c) || c == '.').ToArray();
        var clean = new string(chars);
        // «2.890.000» (نقطه به‌جای جداکنندهٔ هزارگان) ← بیش از یک نقطه یعنی جداکننده است.
        if (clean.Count(c => c == '.') > 1)
            clean = clean.Replace(".", "", StringComparison.Ordinal);
        return clean.Length is > 0 and <= 18 && decimal.TryParse(clean, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value) && value > 0
            ? Math.Floor(value)
            : null;
    }

    /// <summary>
    /// واحد پول: «IRR»/«ریال»/«RIAL» ← IRR؛ «IRT»/«TOMAN»/«TMN»/«تومان» ← IRT؛ خالی ← null؛ هر واحد دیگر (USD، EUR …) ← «foreign».
    /// </summary>
    public static string? NormalizeCurrency(string? currency)
    {
        if (string.IsNullOrWhiteSpace(currency))
            return null;
        var c = currency.Trim().ToUpperInvariant();
        return c switch
        {
            "IRR" or "RIAL" or "ریال" => "IRR",
            "IRT" or "TOMAN" or "TMN" or "تومان" => "IRT",
            _ => "foreign",
        };
    }

    /// <summary>JSON-LD را می‌گردد و هر Product با قیمت موجود را جمع می‌کند (@graph، آرایه‌ها و محصول تو در تو؛ حداکثر عمق ۸).</summary>
    private static void CollectProducts(JsonElement element, List<(string? Title, decimal Price, string? Currency)> found, int depth)
    {
        if (depth > 8 || found.Count >= 20)
            return;
        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
                CollectProducts(item, found, depth + 1);
            return;
        }
        if (element.ValueKind != JsonValueKind.Object)
            return;

        if (HasType(element, "Product") || HasType(element, "ProductGroup"))
        {
            var name = Clean(Text(element, "name"));
            if (element.TryGetProperty("offers", out var offers))
                CollectOffers(offers, name, found, 0);
            return;
        }
        foreach (var property in element.EnumerateObject())
        {
            if (property.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                CollectProducts(property.Value, found, depth + 1);
        }
    }

    /// <summary>offers یک محصول: Offer تکی، آرایه، یا AggregateOffer (lowPrice یا offers داخلی).</summary>
    private static void CollectOffers(JsonElement offers, string? name, List<(string? Title, decimal Price, string? Currency)> found, int depth)
    {
        if (depth > 3)
            return;
        if (offers.ValueKind == JsonValueKind.Array)
        {
            foreach (var offer in offers.EnumerateArray())
                CollectOffers(offer, name, found, depth + 1);
            return;
        }
        if (offers.ValueKind != JsonValueKind.Object || IsUnavailable(Text(offers, "availability")))
            return;

        var price = ParsePrice(Text(offers, "price")) ?? ParsePrice(Text(offers, "lowPrice"));
        var currency = Text(offers, "priceCurrency");
        if (price is null && offers.TryGetProperty("priceSpecification", out var spec))
        {
            var first = spec.ValueKind == JsonValueKind.Array ? spec.EnumerateArray().FirstOrDefault() : spec;
            if (first.ValueKind == JsonValueKind.Object)
            {
                price = ParsePrice(Text(first, "price"));
                currency ??= Text(first, "priceCurrency");
            }
        }
        if (price is > 0)
            found.Add((name, price.Value, currency));
        else if (offers.TryGetProperty("offers", out var inner))
            CollectOffers(inner, name, found, depth + 1);
    }

    private static bool HasType(JsonElement element, string type)
    {
        if (!element.TryGetProperty("@type", out var value))
            return false;
        if (value.ValueKind == JsonValueKind.String)
            return string.Equals(value.GetString(), type, StringComparison.OrdinalIgnoreCase);
        return value.ValueKind == JsonValueKind.Array
               && value.EnumerateArray().Any(v => v.ValueKind == JsonValueKind.String && string.Equals(v.GetString(), type, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>«https://schema.org/OutOfStock»، «SoldOut»، «Discontinued»، «out of stock» ← ناموجود.</summary>
    private static bool IsUnavailable(string? availability)
        => !string.IsNullOrEmpty(availability)
           && (availability.Contains("OutOfStock", StringComparison.OrdinalIgnoreCase) || availability.Contains("SoldOut", StringComparison.OrdinalIgnoreCase)
               || availability.Contains("Discontinued", StringComparison.OrdinalIgnoreCase) || availability.Contains("out of stock", StringComparison.OrdinalIgnoreCase)
               || availability.Contains("oos", StringComparison.OrdinalIgnoreCase) && availability.Length <= 4);

    private static Dictionary<string, string> Attributes(string tag)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match a in AttributeRegex.Matches(tag))
            map.TryAdd(a.Groups[1].Value, a.Groups[2].Success ? a.Groups[2].Value : a.Groups[3].Value);
        return map;
    }

    /// <summary>محتوای متا با property یا name برابر کلید. مثال: Meta(tags, "og:site_name") ← «تکنولایف».</summary>
    private static string? Meta(List<Dictionary<string, string>> tags, string key)
        => tags.FirstOrDefault(t => (t.TryGetValue("property", out var p) && p.Equals(key, StringComparison.OrdinalIgnoreCase))
                                    || (t.TryGetValue("name", out var n) && n.Equals(key, StringComparison.OrdinalIgnoreCase)))
            is { } tag && tag.TryGetValue("content", out var content) && !string.IsNullOrWhiteSpace(content) ? System.Net.WebUtility.HtmlDecode(content).Trim() : null;

    /// <summary>مقدار itemprop (از content یا value). مثال: &lt;meta itemprop="price" content="2890000"&gt; ← «2890000».</summary>
    private static string? ItemProp(List<Dictionary<string, string>> tags, string key)
    {
        foreach (var tag in tags)
        {
            if (!tag.TryGetValue("itemprop", out var prop) || !prop.Equals(key, StringComparison.OrdinalIgnoreCase))
                continue;
            if (tag.TryGetValue("content", out var content) && !string.IsNullOrWhiteSpace(content))
                return System.Net.WebUtility.HtmlDecode(content).Trim();
            if (tag.TryGetValue("value", out var value) && !string.IsNullOrWhiteSpace(value))
                return value.Trim();
            if (tag.TryGetValue("href", out var href) && !string.IsNullOrWhiteSpace(href))
                return href.Trim();
        }
        return null;
    }

    /// <summary>متن HTML به متن ساده (رمزگشایی موجودیت‌ها و حذف فاصله‌های اضافه).</summary>
    private static string? Clean(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        var decoded = System.Net.WebUtility.HtmlDecode(text);
        return string.Join(' ', decoded.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>فقط آدرس جزئیات خود ترب دنبال می‌شود (آدرس داخل پاسخ نباید ما را به جای دیگری بفرستد).</summary>
    public static bool IsTorobDetailsUrl(string url)
        => Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps
           && uri.Host == "api.torob.com" && uri.AbsolutePath.StartsWith("/v4/base-product/details", StringComparison.Ordinal);

    /// <summary>
    /// عدد قیمت از متن فارسی. مثال: «۲۸۰٬۰۰۰ تومان» ← ۲۸۰۰۰۰. متن بدون رقم ← null.
    /// </summary>
    public static decimal? FromText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        var normalized = PersianText.Normalize(text);
        var digits = new string(normalized.SkipWhile(c => !char.IsAsciiDigit(c))
            .TakeWhile(c => char.IsAsciiDigit(c) || c is ',' or '٬' or '٫' or '.' or ' ')
            .Where(char.IsAsciiDigit).ToArray());
        return digits.Length is > 0 and <= 15 ? decimal.Parse(digits, CultureInfo.InvariantCulture) : null;
    }

    private static HashSet<string> Words(string text)
        => PersianText.Tokenize(text).Where(w => !StopWords.Contains(w) && (w.Length > 1 || w.Any(char.IsDigit))).ToHashSet();

    private static string? Text(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) ? value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null,
        } : null;

    private static decimal? Money(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
            return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number))
            return number > 0 ? number : null;
        if (value.ValueKind == JsonValueKind.String && decimal.TryParse(value.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
            return parsed > 0 ? parsed : null;
        return null;
    }

    private static string? Clip(string? text, int max)
        => text is null ? null : text.Length <= max ? text.Trim() : text[..max].Trim();
}
