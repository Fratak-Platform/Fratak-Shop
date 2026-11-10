using Fratak_Shop.Core.DTOs.Products.Query;
using Fratak_Shop.Core.DTOs.Settings;
using Fratak_Shop.Core.DTOs.Shopping.Command;
using Fratak_Shop.Core.Services.Products;
using Fratak_Shop.Core.Services.Searches;
using Fratak_Shop.Core.Services.Seo.Engine;
using Fratak_Shop.Core.Services.Settings;
using Fratak_Shop.Core.Services.Shopping;
using Fratak_Shop.Core.Services.Smart;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Assistant;
using Fratak_Shop.Data.Entities.Orders;
using Fratak_Shop.Data.Entities.Smart;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Fratak_Shop.Core.Services.Assistant;

// ═══════════════════════════════════════════════════════════════════════════
//  دور ۲۳ – «دستیار فروشگاه»: پاسخ از دادهٔ خود فروشگاه (کالا، دسته، مقاله، پرسش‌های متداول، سبد و سفارش‌های خود کاربر).
//  بدون هوش مصنوعی و بدون سرویس پولی. فقط وقتی جواب پرسش دربارهٔ یک کالا در دادهٔ ما نیست، «جست‌وجوی اینترنت» پیشنهاد می‌شود
//  که در پس‌زمینه انجام می‌شود (AssistantLookup در Web/Infrastructure/Assistant/AssistantEndpoints.cs) و سایت را کند نمی‌کند.
//  محل استفاده: POST /api/assistant/message (Web/Infrastructure/Assistant/AssistantEndpoints.cs) و پنل /admin/smart/assistant.
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// یک پیام مشتری.
/// </summary>
/// <param name="Text">متن پیام (حداکثر ۳۰۰ نویسه).</param>
/// <param name="SessionKey">شناسهٔ تصادفی گفت‌وگو از مرورگر (۸ تا ۳۲ حرف/رقم انگلیسی).</param>
/// <param name="UserId">کاربر واردشده یا null.</param>
/// <param name="CurrentProductId">کالای صفحه‌ای که مشتری در آن است (یا null).</param>
/// <param name="LastProductIds">کالاهایی که دستیار همین الان نشان داده (برای «اولی رو اضافه کن»؛ حداکثر ۶).</param>
/// <param name="ActionProductId">کالای دکمهٔ «افزودن به سبد» روی کارت (یا null).</param>
/// <param name="Cart">گرفتن شناسهٔ سبد فعلی؛ ورودی true = اگر نیست بساز. (سبد مهمان با کوکی، در لایهٔ وب.)</param>
public sealed record AssistantRequest(
    string Text,
    string SessionKey,
    Guid? UserId,
    Guid? CurrentProductId,
    IReadOnlyList<Guid> LastProductIds,
    Guid? ActionProductId,
    Func<bool, Task<Guid?>> Cart);

/// <summary>کارت کالا در پاسخ دستیار.</summary>
public sealed record AssistantCard(Guid Id, string Name, string Url, string? ImageUrl, decimal Price, decimal FinalPrice, int DiscountPercent, bool InStock);

/// <summary>دکمهٔ پاسخ: یا لینک (Url) یا پیامی که با زدنش فرستاده می‌شود (Say).</summary>
public sealed record AssistantAction(string Label, string? Url = null, string? Say = null);

/// <summary>نیاز به جست‌وجوی اینترنت (پس‌زمینه) برای پرسش دربارهٔ یک کالا.</summary>
public sealed record AssistantLookupNeed(Guid ProductId, string ProductName, string TopicTitle, IReadOnlyList<string> TopicWords);

/// <summary>
/// پاسخ دستیار. Text متن ساده است (HTML نیست؛ مرورگر با textContent نشان می‌دهد).
/// </summary>
public sealed record AssistantReply(
    string Text,
    IReadOnlyList<AssistantCard> Cards,
    IReadOnlyList<AssistantAction> Actions,
    AssistantIntent Intent,
    AssistantLookupNeed? Lookup = null,
    bool CartChanged = false);

/// <summary>شمارش یک گروه در پنل. مثال: («باتری»، ۱۲).</summary>
public sealed record AssistantCount(string Key, int Count);

/// <summary>کالای درخواستی که نداریم. مثال: («کنسول ps5»، ۷ بار، آخرین بار دیروز).</summary>
public sealed record AssistantDemand(string Query, int Count, DateTimeOffset Last);

/// <summary>کمبود اطلاعات یک کالا: پرسشی دربارهٔ این موضوع که جواب نداشت.</summary>
public sealed record AssistantGap(Guid ProductId, string ProductName, string? Topic, int Count, DateTimeOffset Last);

/// <summary>یک پیام اخیر (متن پوشانده‌شده).</summary>
public sealed record AssistantRecent(string Text, AssistantIntent Intent, DateTimeOffset At);

/// <summary>گزارش پنل «دستیار فروشگاه».</summary>
public sealed record AssistantInsights(
    int Days,
    int Total,
    int Sessions,
    int Answered,
    IReadOnlyList<AssistantCount> Intents,
    IReadOnlyList<AssistantCount> Topics,
    IReadOnlyList<AssistantCount> Questions,
    IReadOnlyList<AssistantDemand> Demands,
    IReadOnlyList<AssistantGap> Gaps,
    IReadOnlyList<AssistantRecent> Unknown);

/// <summary>
/// دستیار فروشگاه. مثال: ReplyAsync(«پاوربانک ۲۰ هزار دارید؟») ← کارت پاوربانک‌های موجود؛ ReplyAsync(«سفارشم کجاست») ← وضعیت آخرین سفارش‌ها.
/// </summary>
public interface IAssistantService
{
    /// <summary>پاسخ به یک پیام و ثبت آن (متن پوشانده‌شده) برای گزارش پنل.</summary>
    Task<AssistantReply> ReplyAsync(AssistantRequest request, CancellationToken cancellationToken = default);

    /// <summary>گزارش <paramref name="days"/> روز اخیر برای پنل.</summary>
    Task<AssistantInsights> InsightsAsync(int days, CancellationToken cancellationToken = default);

    /// <summary>
    /// خوراک «تکمیل هوشمند»: کالاهایی که در ۳۰ روز اخیر دست‌کم ۲ پرسش بی‌جواب داشته‌اند در صف «دریافت مشخصات» می‌روند (حداکثر <paramref name="max"/>).
    /// خروجی: تعداد کار افزوده‌شده. اجرای خودکار روزانه + دکمهٔ پنل.
    /// </summary>
    Task<int> FeedGapsAsync(int max, CancellationToken cancellationToken = default);

    /// <summary>حذف پیام‌های قدیمی‌تر از <paramref name="days"/> روز (پیش‌فرض نگه‌داری: ۱۸۰ روز).</summary>
    Task<int> PruneAsync(int days, CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public sealed class AssistantService(
    FratakShopDbContext context,
    IProductService products,
    ISearchInsightsService insights,
    ICartService carts,
    IStoreSettingService settings,
    IMemoryCache cache,
    ILogger<AssistantService> logger,
    ISmartTaskQueue? queue = null,
    ISmartConfigService? smart = null) : IAssistantService
{
    /// <summary>نام انجام‌دهندهٔ کارهایی که دستیار در صف هوشمند می‌گذارد.</summary>
    public const string Actor = "دستیار فروشگاه";

    /// <summary>جملهٔ ثابت کالای ناموجود (درخواست مشتری در «کالاهای درخواستی» ثبت می‌شود).</summary>
    public const string NotCarried = "متأسفانه موجود نداریم، درخواست شما ثبت شد.";

    /// <summary>از این تعداد پرسش بی‌جواب دربارهٔ یک کالا، «دریافت مشخصات» خودکار در صف می‌رود.</summary>
    public const int GapThreshold = 2;

    private const int MaxCards = 4;
    private static readonly string[] BlogCues = ["آموزش", "راهنما", "راهنمای", "مقاله", "چطور", "چگونه", "روش", "نکات", "ترفند"];

    private sealed record CachedCategory(string Name, string Slug);

    private sealed record CachedFaq(string Question, string Answer);

    /// <summary>نتیجهٔ یک شاخهٔ پاسخ + اطلاعات ثبت.</summary>
    private sealed record Outcome(AssistantReply Reply, Guid? ProductId = null, bool Answered = true, string? Topic = null, string? Query = null);

    /// <inheritdoc />
    public async Task<AssistantReply> ReplyAsync(AssistantRequest request, CancellationToken cancellationToken = default)
    {
        // موبایل/کارت/ایمیل از همان ابتدا پوشانده می‌شود تا نه در جست‌وجو، نه در «کالاهای درخواستی» و نه در گزارش بماند.
        var text = AssistantNlu.Mask(AssistantNlu.Cut(request.Text));
        var parse = AssistantNlu.Parse(text);
        var store = await settings.GetAsync(cancellationToken);
        var name = request.UserId is { } userId ? await LastNameAsync(userId, cancellationToken) : null;
        var last = request.LastProductIds.Distinct().Take(6).ToList();

        Outcome outcome;
        try
        {
            outcome = parse.Intent switch
            {
                AssistantIntent.Greeting => Greeting(name, store.SiteName),
                AssistantIntent.Thanks => Simple(AssistantIntent.Thanks, $"خواهش می‌کنم{Comma(name)}! اگر سؤال دیگری بود در خدمتم."),
                AssistantIntent.Help => Help(name),
                AssistantIntent.Human => Human(name, store.SupportPhone, store.WorkingHours, request.UserId is not null),
                AssistantIntent.OffTopic => Simple(AssistantIntent.OffTopic,
                    $"ببخشید{Comma(name)}، من فقط دربارهٔ کالاها، خرید و سفارش‌های {store.SiteName} می‌توانم کمک کنم. مثلاً بپرسید «قیمت هدفون بلوتوثی» یا «سفارشم کجاست؟»",
                    Suggestions()),
                AssistantIntent.Track => await TrackAsync(request.UserId, parse.OrderNumber, name, cancellationToken),
                AssistantIntent.CartShow => await CartShowAsync(request, name),
                AssistantIntent.CartAdd => await CartAddAsync(request, parse, last, name, cancellationToken),
                AssistantIntent.CartRemove => await CartRemoveAsync(request, parse, name),
                AssistantIntent.Checkout => await CheckoutAsync(request, name, store.FreeShippingThreshold, cancellationToken),
                AssistantIntent.Address => await AddressAsync(request.UserId, name, cancellationToken),
                AssistantIntent.Compare => await CompareAsync(request, text, parse, last, cancellationToken),
                AssistantIntent.ProductQuestion => await QuestionAsync(request, parse, last, name, store, cancellationToken),
                _ => await SearchAsync(text, parse, request.SessionKey, name, cancellationToken),
            };
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Assistant reply failed for intent {Intent}", parse.Intent);
            outcome = new Outcome(new AssistantReply("ببخشید، الان نتوانستم این کار را انجام دهم. لطفاً چند لحظهٔ دیگر دوباره امتحان کنید.", [], [], parse.Intent), Answered: false);
        }

        await LogAsync(request, text, outcome, cancellationToken);
        return outcome.Reply;
    }

    // ── پاسخ‌های ساده ───────────────────────────────────────────

    /// <summary>«، فلانی گرامی» یا خالی. مثال: «ناظری» ← «، ناظری گرامی».</summary>
    private static string Comma(string? name) => name is null ? "" : $"، {name} گرامی";

    private static Outcome Simple(AssistantIntent intent, string text, IReadOnlyList<AssistantAction>? actions = null) =>
        new(new AssistantReply(text, [], actions ?? [], intent));

    private static List<AssistantAction> Suggestions() =>
    [
        new("پیشنهاد کالا", Say: "کالاهای پرفروش"),
        new("سبد خرید من", Say: "سبد خرید من"),
        new("پیگیری سفارش", Say: "سفارشم کجاست"),
    ];

    private static Outcome Greeting(string? name, string siteName) => Simple(AssistantIntent.Greeting,
        $"سلام{(name is null ? "" : $" {name} گرامی")}! من دستیار فروشگاه {siteName} هستم. نام کالایی را که می‌خواهید بنویسید، دربارهٔ قیمت و موجودی و ارسال بپرسید، یا سفارشتان را پیگیری کنید.",
        Suggestions());

    private static Outcome Help(string? name) => Simple(AssistantIntent.Help,
        $"با کمال میل{Comma(name)}. این کارها از من برمی‌آید:\n" +
        "• پیدا کردن کالا و نشان دادن قیمت و موجودی (مثلاً «هدفون بلوتوثی زیر ۲ میلیون»)\n" +
        "• جواب دربارهٔ مشخصات، گارانتی، ارسال و مرجوعی\n" +
        "• افزودن به سبد، دیدن سبد و راهنمای تسویه حساب\n" +
        "• پیگیری سفارش‌های شما",
        Suggestions());

    private static Outcome Human(string? name, string? phone, string? hours, bool signedIn)
    {
        var text = $"حتماً{Comma(name)}. ";
        text += string.IsNullOrWhiteSpace(phone) ? "همکاران پشتیبانی از طریق تیکت در خدمت شما هستند." : $"شمارهٔ پشتیبانی: {phone}.";
        if (!string.IsNullOrWhiteSpace(hours))
            text += $" ساعت پاسخ‌گویی: {hours}.";
        var actions = signedIn
            ? new List<AssistantAction> { new("ثبت تیکت پشتیبانی", "/profile/tickets/new"), new("تماس با ما", "/contact-us") }
            : new List<AssistantAction> { new("تماس با ما", "/contact-us"), new("پرسش‌های متداول", "/faq") };
        return Simple(AssistantIntent.Human, text, actions);
    }

    // ── جست‌وجو و کالای درخواستی ─────────────────────────────────

    private async Task<Outcome> SearchAsync(string text, AssistantParse parse, string sessionKey, string? name, CancellationToken cancellationToken)
    {
        var query = parse.Query;
        if (parse.Normalized.Contains("پرفروش", StringComparison.Ordinal) && query.Replace("کالاهای", "").Replace("پرفروش", "").Trim().Length == 0)
        {
            var best = await FindAsync(null, ProductSortBy.BestSelling, cancellationToken);
            return Cards(AssistantIntent.Search, "پرفروش‌ترین کالاهای موجود:", best, [], null);
        }

        // پرسش‌های متداول فروشگاه (ارسال، مرجوعی، …) وقتی پیام شبیه یکی از آن‌هاست.
        var faq = await FaqAsync(text, cancellationToken);
        if (faq is { Score: >= 0.6 })
            return Simple(AssistantIntent.Help, faq.Value.Answer, [new("همهٔ پرسش‌های متداول", "/faq")]);

        if (query.Length < 2)
            return Simple(AssistantIntent.Help, $"متوجه نشدم{Comma(name)}. نام کالا یا سؤالتان را کمی کامل‌تر بنویسید؛ مثلاً «پاوربانک ۲۰ هزار».", Suggestions());

        var found = await FindAsync(query, ProductSortBy.BestSelling, cancellationToken);
        var category = await CategoryAsync(query, cancellationToken);
        var actions = new List<AssistantAction>();
        if (category is not null)
            actions.Add(new($"همهٔ «{category.Value.Name}»", $"/category/{category.Value.Slug}"));
        if (found.Count > 0)
        {
            var intro = found.All(p => !p.IsInStock)
                ? "این‌ها را پیدا کردم ولی فعلاً موجود نیستند؛ در صفحهٔ هر کالا «خبرم کن» را بزنید تا موجود شد خبرتان کنیم:"
                : "این‌ها را پیدا کردم:";
            actions.Add(new("نتایج بیشتر", "/products?Query=" + Uri.EscapeDataString(query)));
            return Cards(AssistantIntent.Search, intro, found, actions, query);
        }

        if (await insights.DidYouMeanAsync(query) is { } corrected && corrected != query)
        {
            var retry = await FindAsync(corrected, ProductSortBy.BestSelling, cancellationToken);
            if (retry.Count > 0)
                return Cards(AssistantIntent.Search, $"منظورتان «{corrected}» بود؟ این‌ها را پیدا کردم:", retry, actions, corrected);
        }

        if (category is not null)
            return new Outcome(new AssistantReply($"در دستهٔ «{category.Value.Name}» کالا داریم؛ از این لینک ببینید:", [], actions, AssistantIntent.Search), Query: query);

        if (BlogCues.Any(c => parse.Normalized.Contains(c, StringComparison.Ordinal)))
        {
            var posts = await PostsAsync(query, cancellationToken);
            if (posts.Count > 0)
                return new Outcome(new AssistantReply("این مقاله‌ها به سؤالتان کمک می‌کند:", [], posts, AssistantIntent.Search), Query: query);
        }

        if (faq is { Score: >= 0.4 })
            return Simple(AssistantIntent.Help, faq.Value.Answer, [new("همهٔ پرسش‌های متداول", "/faq")]);

        if (parse.ShopCue || AssistantNlu.Words(query).Count is >= 1 and <= 4)
        {
            // کالای درخواستی که نداریم: هم در گزارش جست‌وجوهای بی‌نتیجه (خلاصهٔ روزانه) و هم در پنل دستیار.
            await insights.LogAsync(query, 0, "assistant:" + sessionKey);
            return new Outcome(new AssistantReply($"{NotCarried} {(name is null ? "" : name + " گرامی، ")}به‌محض تأمین، این کالا به فروشگاه اضافه می‌شود.", [],
                [new("کالاهای پرفروش", Say: "کالاهای پرفروش")], AssistantIntent.Demand), Answered: true, Query: query);
        }

        return new Outcome(new AssistantReply($"متوجه نشدم{Comma(name)}. می‌توانم در پیدا کردن کالا، قیمت و موجودی، سبد خرید و پیگیری سفارش کمک کنم.", [], Suggestions(), AssistantIntent.Unknown),
            Answered: false, Query: query);
    }

    private static Outcome Cards(AssistantIntent intent, string text, List<ProductFilterData> found, List<AssistantAction> actions, string? query) =>
        new(new AssistantReply(text, found.Take(MaxCards).Select(ToCard).ToList(), actions, intent), found.Count == 1 ? found[0].Id : null, true, null, query);

    /// <summary>کارت کالا. مثال: تصویر ← «/uploads/products/x.webp»، لینک ← «/product/{slug}».</summary>
    public static AssistantCard ToCard(ProductFilterData p) => new(p.Id, p.Name, $"/product/{p.Slug}",
        string.IsNullOrWhiteSpace(p.PrimaryImageName) ? null : $"/uploads/products/{p.PrimaryImageName}",
        p.Price, p.FinalPrice, p.DiscountPercent, p.IsInStock);

    /// <summary>جست‌وجوی کالای فعال با هم‌معنی‌ها (همان موتور جست‌وجوی سایت). موجودها اول.</summary>
    private async Task<List<ProductFilterData>> FindAsync(string? query, ProductSortBy sort, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var filter = new ProductFilterParams { Search = query, OnlyActive = true, SortBy = sort, PageId = 1, Take = 8 };
        if (!string.IsNullOrWhiteSpace(query))
            filter.SearchTermGroups = await insights.ExpandAsync(query);
        else
            filter.OnlyAvailable = true;
        var result = await products.GetProductByFilterAsync(filter);
        return result.Data.OrderByDescending(p => p.IsInStock).ToList();
    }

    private async Task<(string Name, string Slug)?> CategoryAsync(string query, CancellationToken cancellationToken)
    {
        var normalized = PersianText.Normalize(query);
        if (normalized.Length < 2)
            return null;
        var categories = await cache.GetOrCreateAsync("assistant:categories", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10);
            entry.Size = 1;
            return await context.Categories.AsNoTracking()
                .Where(c => c.IsActive && !c.IsSystem && c.Products.Any(p => p.IsActive))
                .Select(c => new CachedCategory(c.Name, c.Slug))
                .ToListAsync(cancellationToken);
        }) ?? new List<CachedCategory>();
        var match = categories
            .Select(c => new { c.Name, c.Slug, Key = PersianText.Normalize(c.Name) })
            .Where(c => c.Key == normalized || c.Key.Contains(normalized, StringComparison.Ordinal) && normalized.Length >= 3)
            .OrderBy(c => c.Key == normalized ? 0 : 1).ThenBy(c => c.Key.Length)
            .FirstOrDefault();
        return match is null ? null : (match.Name, match.Slug);
    }

    private async Task<List<AssistantAction>> PostsAsync(string query, CancellationToken cancellationToken)
    {
        var words = AssistantNlu.Words(query).Where(w => w.Length >= 3).Take(4).ToList();
        if (words.Count == 0)
            return [];
        var now = DateTimeOffset.UtcNow;
        var posts = await context.BlogPosts.AsNoTracking()
            .Where(p => p.IsPublished && (p.PublishedAt == null || p.PublishedAt <= now))
            .OrderByDescending(p => p.ViewCount)
            .Select(p => new { p.Title, p.Slug })
            .Take(300)
            .ToListAsync(cancellationToken);
        return posts
            .Select(p => new { p.Title, p.Slug, Score = words.Count(w => PersianText.Normalize(p.Title).Contains(w, StringComparison.Ordinal)) })
            .Where(p => p.Score > 0)
            .OrderByDescending(p => p.Score)
            .Take(3)
            .Select(p => new AssistantAction(p.Title.Length > 60 ? p.Title[..60] + "…" : p.Title, $"/blog/{p.Slug}"))
            .ToList();
    }

    private async Task<(string Answer, double Score)?> FaqAsync(string text, CancellationToken cancellationToken)
    {
        var faqs = await cache.GetOrCreateAsync("assistant:faq", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10);
            entry.Size = 1;
            return await context.FaqItems.AsNoTracking()
                .Where(f => f.IsActive)
                .OrderBy(f => f.DisplayOrder)
                .Select(f => new CachedFaq(f.Question, f.Answer))
                .Take(300)
                .ToListAsync(cancellationToken);
        }) ?? new List<CachedFaq>();
        var best = faqs
            .Select(f => new { f.Answer, Score = AssistantNlu.Similarity(text, f.Question) })
            .OrderByDescending(f => f.Score)
            .FirstOrDefault();
        if (best is null || best.Score <= 0)
            return null;
        var answer = PersianText.HtmlToText(best.Answer).Trim();
        return (answer.Length > 500 ? answer[..500].TrimEnd() + "…" : answer, best.Score);
    }

    // ── پرسش دربارهٔ کالا ────────────────────────────────────────

    /// <summary>
    /// پرسش دربارهٔ کالا یا فروشگاه. موضوع فروشگاهی (ارسال/مرجوعی/پرداخت/تماس) از تنظیمات و پرسش‌های متداول؛ بقیه از دادهٔ همان کالا.
    /// نبود جواب در دادهٔ ما ← Answered = false (خوراک تکمیل هوشمند) و برای مشخصات فنی، جست‌وجوی اینترنت در پس‌زمینه پیشنهاد می‌شود.
    /// </summary>
    private async Task<Outcome> QuestionAsync(AssistantRequest request, AssistantParse parse, List<Guid> last, string? name, StoreSettingDto store, CancellationToken cancellationToken)
    {
        var topic = AssistantNlu.Topic(parse.Topic)!;
        if (topic.StoreLevel)
            return await StoreAnswerAsync(topic, request.Text, store, name, cancellationToken);

        var (product, options) = await ResolveAsync(request, parse, last, cancellationToken);
        if (product is null)
        {
            if (options.Count > 1)
            {
                var intro = topic.Key is "price" or "stock"
                    ? "این‌ها را پیدا کردم؛ قیمت و موجودی روی هر کارت هست:"
                    : "دربارهٔ کدام کالا می‌پرسید؟ یکی را باز کنید یا بنویسید «اولی»، «دومی»…";
                return Cards(AssistantIntent.Search, intro, options, [], parse.Query) with { Topic = topic.Title };
            }
            if (parse.Query.Length >= 2)
                return await SearchAsync(request.Text, parse, request.SessionKey, name, cancellationToken);
            return new Outcome(new AssistantReply($"دربارهٔ کدام کالا می‌پرسید{Comma(name)}؟ نام کالا را بنویسید یا از صفحهٔ همان کالا بپرسید.", [], [], AssistantIntent.ProductQuestion), Topic: topic.Title);
        }

        var card = ToCard(product);
        AssistantReply Reply(string text, AssistantLookupNeed? lookup = null) =>
            new(text, [card], [], AssistantIntent.ProductQuestion, lookup);
        Outcome Done(string text) => new(Reply(text), product.Id, true, topic.Title, parse.Query);
        Outcome Gap(string text, AssistantLookupNeed? lookup = null) => new(Reply(text, lookup), product.Id, false, topic.Title, parse.Query);

        switch (topic.Key)
        {
            case "price":
                return Done(PriceText(product) + " " + StockText(product));
            case "stock":
                return Done(StockText(product));
        }

        var facts = await context.Products.AsNoTracking()
            .Where(p => p.Id == product.Id)
            .Select(p => new
            {
                p.WarrantyTitle,
                p.WarrantyMonths,
                Features = p.Features.OrderBy(f => f.DisplayOrder).Select(f => new { f.Key, f.Value }).Take(60).ToList(),
            })
            .FirstOrDefaultAsync(cancellationToken);
        var features = facts?.Features.Select(f => (Key: f.Key.Trim(), Value: f.Value.Trim())).Where(f => f.Key.Length > 0 && f.Value.Length > 0).ToList() ?? [];

        if (topic.Key == "warranty" && !string.IsNullOrWhiteSpace(facts?.WarrantyTitle))
        {
            var months = facts.WarrantyMonths is > 0 ? $" ({PersianText.Digits(facts.WarrantyMonths.Value)} ماه)" : "";
            return Done($"گارانتی «{product.Name}»: {facts.WarrantyTitle.Trim()}{months}.");
        }

        if (topic.Key == "specs")
        {
            if (features.Count == 0)
                return Gap($"مشخصات فنی «{product.Name}» هنوز کامل ثبت نشده است؛ پرسش شما ثبت شد تا به‌زودی کامل شود. توضیحات صفحهٔ کالا را هم ببینید.");
            return Done($"مشخصات «{product.Name}»:\n" + string.Join("\n", features.Take(8).Select(f => $"• {f.Key}: {f.Value}")));
        }

        var keys = topic.FeatureKeys.Select(PersianText.Normalize).ToList();
        var queryWords = AssistantNlu.Words(parse.Query).Where(w => w.Length >= 3).ToList();
        var matched = features
            .Where(f =>
            {
                var key = PersianText.Normalize(f.Key);
                return keys.Any(k => key.Contains(k, StringComparison.Ordinal)) || queryWords.Any(w => key.Contains(w, StringComparison.Ordinal));
            })
            .Take(4)
            .ToList();
        if (matched.Count > 0)
            return Done($"«{product.Name}» – " + string.Join("؛ ", matched.Select(f => $"{f.Key}: {f.Value}")));

        // گارانتی و اصالت از اینترنت پرسیده نمی‌شود (به فروشنده بستگی دارد)؛ مشخصات فنی را در پس‌زمینه جست‌وجو می‌کنیم.
        if (topic.Key is "warranty" or "original")
            return Gap($"{topic.Title} «{product.Name}» در اطلاعات ما ثبت نشده است؛ پرسش شما برای همکاران ثبت شد. برای اطمینان می‌توانید با پشتیبانی تماس بگیرید.");
        return Gap($"«{topic.Title}» در مشخصات ثبت‌شدهٔ «{product.Name}» نیست. لطفاً چند لحظه صبر کنید تا در منابع اینترنتی جست‌وجو کنم…",
            new AssistantLookupNeed(product.Id, product.Name, topic.Title, topic.Words.Concat(topic.FeatureKeys).Distinct().ToList()));
    }

    /// <summary>قیمت با تخفیف واقعی. مثال: «قیمت «هدفون X»: ۱٬۲۰۰٬۰۰۰ تومان (۲۰٪ تخفیف؛ قیمت قبل ۱٬۵۰۰٬۰۰۰ تومان).»</summary>
    public static string PriceText(ProductFilterData p) => p.HasActiveDiscount && p.DiscountPercent > 0
        ? $"قیمت «{p.Name}»: {PersianText.Money(p.FinalPrice)} تومان ({PersianText.Digits(p.DiscountPercent)}٪ تخفیف؛ قیمت قبل {PersianText.Money(p.Price)} تومان)."
        : $"قیمت «{p.Name}»: {PersianText.Money(p.FinalPrice)} تومان.";

    /// <summary>موجودی واقعی. ۵ عدد یا کمتر ← تعداد گفته می‌شود. ناموجود ← راهنمای «خبرم کن».</summary>
    public static string StockText(ProductFilterData p) => p.StockQuantity switch
    {
        <= 0 => $"«{p.Name}» فعلاً ناموجود است؛ در صفحهٔ کالا «خبرم کن» را بزنید تا موجود شد خبرتان کنیم.",
        <= 5 => $"«{p.Name}» موجود است (فقط {PersianText.Digits(p.StockQuantity)} عدد مانده).",
        _ => $"«{p.Name}» موجود است.",
    };

    /// <summary>جواب موضوع‌های فروشگاهی از پرسش‌های متداول (اگر شبیه بود) یا تنظیمات فروشگاه.</summary>
    private async Task<Outcome> StoreAnswerAsync(AssistantTopic topic, string text, StoreSettingDto store, string? name, CancellationToken cancellationToken)
    {
        var faq = await FaqAsync(text, cancellationToken);
        if (faq is { Score: >= 0.5 })
            return new Outcome(new AssistantReply(faq.Value.Answer, [], [new("همهٔ پرسش‌های متداول", "/faq")], AssistantIntent.ProductQuestion), Topic: topic.Title);

        string answer;
        List<AssistantAction> actions = [];
        switch (topic.Key)
        {
            case "shipping":
                answer = $"ارسال سفارش‌ها معمولاً {PersianText.Digits(store.SlaDeliveryMinDays)} تا {PersianText.Digits(store.SlaDeliveryMaxDays)} روز کاری طول می‌کشد.";
                if (store.FreeShippingThreshold > 0)
                    answer += $" خرید بالای {PersianText.Money(store.FreeShippingThreshold)} تومان ارسال رایگان دارد.";
                answer += " روش و هزینهٔ دقیق ارسال در صفحهٔ تسویه حساب بر اساس آدرس شما نشان داده می‌شود.";
                break;
            case "return":
                answer = store.IsReturnEnabled
                    ? $"تا {PersianText.Digits(store.ReturnWindowDays)} روز پس از تحویل می‌توانید از «سفارش‌های من» درخواست مرجوعی ثبت کنید."
                    : "ثبت آنلاین مرجوعی فعلاً فعال نیست؛ لطفاً با پشتیبانی تماس بگیرید.";
                if (store.IsReturnEnabled)
                    actions.Add(new("مرجوعی‌های من", "/profile/returns"));
                break;
            case "payment":
                answer = "پرداخت آنلاین با کارت‌های بانکی عضو شتاب امکان‌پذیر است";
                answer += store.IsCashOnDeliveryEnabled
                    ? store.CashOnDeliveryMaxAmount > 0 ? $" و پرداخت در محل هم برای سفارش تا {PersianText.Money(store.CashOnDeliveryMaxAmount)} تومان فعال است." : " و پرداخت در محل هم فعال است."
                    : ".";
                break;
            default:
                answer = string.IsNullOrWhiteSpace(store.SupportPhone) ? "از طریق صفحهٔ «تماس با ما» در خدمتتان هستیم." : $"شمارهٔ تماس: {store.SupportPhone}.";
                if (!string.IsNullOrWhiteSpace(store.WorkingHours))
                    answer += $" ساعت کاری: {store.WorkingHours}.";
                if (!string.IsNullOrWhiteSpace(store.SupportEmail))
                    answer += $" ایمیل: {store.SupportEmail}.";
                actions.Add(new("تماس با ما", "/contact-us"));
                break;
        }
        return new Outcome(new AssistantReply(answer, [], actions, AssistantIntent.ProductQuestion), Topic: topic.Title);
    }

    /// <summary>
    /// کالای مورد نظر پیام. ترتیب: دکمهٔ کارت ← «اولی/دومی» ← نام کامل کالا در پیام ← کالای همین صفحه ← اولین کارت قبلی.
    /// چند نتیجهٔ هم‌وزن ← Product = null و Options = نتیجه‌ها (از مشتری می‌پرسیم کدام).
    /// </summary>
    private async Task<(ProductFilterData? Product, List<ProductFilterData> Options)> ResolveAsync(AssistantRequest request, AssistantParse parse, List<Guid> last, CancellationToken cancellationToken)
    {
        async Task<ProductFilterData?> ById(Guid id) => (await products.GetCardsByIdsAsync([id])).FirstOrDefault();

        if (request.ActionProductId is { } action && await ById(action) is { } chosen)
            return (chosen, new List<ProductFilterData> { chosen });
        if (parse.Ordinal is { } ordinal && ordinal <= last.Count && await ById(last[ordinal - 1]) is { } picked)
            return (picked, new List<ProductFilterData> { picked });

        var pointsHere = parse.Normalized.Split(' ').Any(t => t is "این" or "اینو" or "همین" or "همینو" or "اینه" or "اون" or "اونو");
        var queryWords = AssistantNlu.Words(parse.Query);
        Guid? current = request.CurrentProductId ?? (last.Count > 0 && (pointsHere || queryWords.Count == 0) ? last[0] : (Guid?)null);
        if (queryWords.Count > 0 && !(pointsHere && current is not null))
        {
            var found = await FindAsync(parse.Query, ProductSortBy.BestSelling, cancellationToken);
            var normalizedQuery = PersianText.Normalize(parse.Query);
            var exact = found.Where(p => PersianText.Normalize(p.Name).Contains(normalizedQuery, StringComparison.Ordinal)).ToList();
            // در صفحهٔ یک کالا فقط وقتی سراغ کالای دیگر می‌رویم که نام آن کامل (دست‌کم ۲ واژه) در پیام باشد؛ «ساعت شارژ» نباید ساعت هوشمند بیاورد.
            if (current is null || queryWords.Count >= 2 && exact.Count >= 1)
            {
                if (exact.Count == 1)
                    return (exact[0], found);
                if (found.Count == 1)
                    return (found[0], found);
                if (found.Count > 1 && current is null)
                    return (null, found);
            }
        }
        if (current is { } id && await ById(id) is { } here)
            return (here, new List<ProductFilterData> { here });
        return (null, new List<ProductFilterData>());
    }

    // ── سبد خرید و تسویه ─────────────────────────────────────────

    private async Task<Outcome> CartShowAsync(AssistantRequest request, string? name)
    {
        var cartId = await request.Cart(false);
        var cart = cartId is { } id ? await carts.GetCartByIdAsync(id) : null;
        if (cart is null || cart.Items.Count == 0)
            return Simple(AssistantIntent.CartShow, $"سبد خرید شما خالی است{Comma(name)}. نام کالایی را که می‌خواهید بنویسید تا پیدا کنم.", Suggestions());
        var lines = cart.Items.Take(8).Select(i =>
            $"• {i.ProductName ?? "کالای حذف‌شده"} × {PersianText.Digits(i.Quantity)} — {PersianText.Money(i.TotalAmount)} تومان{(i.HasEnoughStock ? "" : " (موجودی کافی نیست)")}");
        var more = cart.Items.Count > 8 ? $"\n… و {PersianText.Digits(cart.Items.Count - 8)} کالای دیگر" : "";
        var text = $"سبد خرید شما{Comma(name)}:\n{string.Join("\n", lines)}{more}\nجمع (بدون هزینهٔ ارسال): {PersianText.Money(cart.PayableAmount)} تومان";
        return Simple(AssistantIntent.CartShow, text, [new("مشاهدهٔ سبد", "/cart"), new("تسویه حساب", Say: "تسویه حساب")]);
    }

    private async Task<Outcome> CartAddAsync(AssistantRequest request, AssistantParse parse, List<Guid> last, string? name, CancellationToken cancellationToken)
    {
        var (product, options) = await ResolveAsync(request, parse, last, cancellationToken);
        if (product is null)
        {
            if (options.Count > 1)
                return Cards(AssistantIntent.CartAdd, "کدام را اضافه کنم؟ روی «افزودن» کارت مورد نظر بزنید.", options, [], parse.Query);
            if (parse.Query.Length >= 2)
                return await SearchAsync(request.Text, parse, request.SessionKey, name, cancellationToken);
            return Simple(AssistantIntent.CartAdd, "کدام کالا را اضافه کنم؟ نام کالا را بنویسید یا از صفحهٔ همان کالا بپرسید.");
        }
        if (!product.IsInStock)
            return new Outcome(new AssistantReply(StockText(product), [ToCard(product)], [], AssistantIntent.CartAdd), product.Id);

        var cartId = await request.Cart(true);
        if (cartId is null)
            return new Outcome(new AssistantReply("سبد خرید در دسترس نبود؛ لطفاً صفحه را دوباره باز کنید.", [], [], AssistantIntent.CartAdd), product.Id, false);
        var result = await carts.AddItemAsync(new AddCartItemCommand { CartId = cartId.Value, ProductId = product.Id, Quantity = parse.Quantity });
        if (!result.IsSuccess())
            return new Outcome(new AssistantReply(result.Message ?? "افزودن به سبد ممکن نشد.", [ToCard(product)], [], AssistantIntent.CartAdd), product.Id);
        var cart = await carts.GetCartByIdAsync(cartId.Value);
        var total = cart is null ? "" : $" جمع سبد: {PersianText.Money(cart.PayableAmount)} تومان.";
        return new Outcome(new AssistantReply($"«{product.Name}» ({PersianText.Digits(parse.Quantity)} عدد) به سبد خرید اضافه شد{Comma(name)}.{total}", [],
            [new("مشاهدهٔ سبد", "/cart"), new("تسویه حساب", Say: "تسویه حساب")], AssistantIntent.CartAdd, CartChanged: true), product.Id);
    }

    private async Task<Outcome> CartRemoveAsync(AssistantRequest request, AssistantParse parse, string? name)
    {
        var cartId = await request.Cart(false);
        var cart = cartId is { } id ? await carts.GetCartByIdAsync(id) : null;
        if (cart is null || cart.Items.Count == 0)
            return Simple(AssistantIntent.CartRemove, "سبد خرید شما خالی است.");

        var words = AssistantNlu.Words(parse.Query).Where(w => w.Length >= 2).ToList();
        var item = cart.Items.FirstOrDefault(i => i.ProductId == request.ActionProductId)
                   ?? (parse.Ordinal is { } n && n <= cart.Items.Count ? cart.Items[n - 1] : null)
                   ?? (words.Count > 0
                       ? cart.Items
                           .Select(i => new { Item = i, Score = words.Count(w => PersianText.Normalize(i.ProductName).Contains(w, StringComparison.Ordinal)) })
                           .Where(x => x.Score > 0).OrderByDescending(x => x.Score).Select(x => x.Item).FirstOrDefault()
                       : null)
                   ?? cart.Items.FirstOrDefault(i => i.ProductId == request.CurrentProductId)
                   ?? (cart.Items.Count == 1 ? cart.Items[0] : null);
        if (item is null)
            return Simple(AssistantIntent.CartRemove, "کدام کالا را حذف کنم؟ نامش را بنویسید یا بگویید «اولی»، «دومی»… (به ترتیب سبد).",
                [new("سبد خرید من", Say: "سبد خرید من")]);

        var result = await carts.RemoveItemAsync(new RemoveCartItemCommand { CartId = cart.Id, ProductId = item.ProductId });
        if (!result.IsSuccess())
            return Simple(AssistantIntent.CartRemove, result.Message ?? "حذف از سبد ممکن نشد.");
        return new Outcome(new AssistantReply($"«{item.ProductName}» از سبد خرید حذف شد{Comma(name)}.", [], [new("سبد خرید من", Say: "سبد خرید من")],
            AssistantIntent.CartRemove, CartChanged: true), item.ProductId);
    }

    /// <summary>
    /// راهنمای تسویه: خلاصهٔ سبد + انتخاب آدرس ذخیره‌شده (پیش‌فرض اول) ← لینک صفحهٔ پرداخت با همان آدرس. پرداخت را خود مشتری می‌زند.
    /// </summary>
    private async Task<Outcome> CheckoutAsync(AssistantRequest request, string? name, decimal freeShippingThreshold, CancellationToken cancellationToken)
    {
        if (request.UserId is not { } userId)
            return Simple(AssistantIntent.Checkout, "برای ثبت سفارش لطفاً وارد حساب شوید (با کد پیامکی هم می‌شود). سبد خرید شما حفظ می‌شود.",
                [new("ورود و ادامهٔ خرید", "/login?returnUrl=%2Fcheckout"), new("مشاهدهٔ سبد", "/cart")]);

        var cartId = await request.Cart(false);
        var cart = cartId is { } id ? await carts.GetCartByIdAsync(id) : null;
        if (cart is null || cart.Items.Count == 0)
            return Simple(AssistantIntent.Checkout, $"سبد خرید شما خالی است{Comma(name)}. اول کالایی اضافه کنید.", Suggestions());

        var text = $"خلاصهٔ سفارش{Comma(name)}: {PersianText.Digits(cart.TotalQuantity)} کالا، جمع {PersianText.Money(cart.PayableAmount)} تومان (هزینهٔ ارسال در مرحلهٔ بعد بر اساس آدرس حساب می‌شود).";
        var short_ = cart.Items.Where(i => !i.HasEnoughStock).Select(i => i.ProductName ?? "کالای حذف‌شده").ToList();
        if (short_.Count > 0)
            text += $"\nموجودی این کالاها کافی نیست و باید از سبد اصلاح شوند: {PersianText.JoinList(short_)}.";
        if (freeShippingThreshold > 0 && cart.PayableAmount < freeShippingThreshold)
            text += $"\nبا {PersianText.Money(freeShippingThreshold - cart.PayableAmount)} تومان خرید بیشتر، ارسال رایگان می‌شود.";

        var addresses = await context.UserAddresses.AsNoTracking()
            .Where(a => a.UserId == userId)
            .OrderByDescending(a => a.IsDefault).ThenByDescending(a => a.CreatedAt)
            .Select(a => new { a.Id, a.Title, City = a.City.Name })
            .Take(4)
            .ToListAsync(cancellationToken);
        if (addresses.Count == 0)
            return Simple(AssistantIntent.Checkout, text + "\nهنوز آدرسی ثبت نکرده‌اید؛ در صفحهٔ تسویه حساب آدرس را وارد کنید و پرداخت را بزنید.",
                [new("ادامه به تسویه حساب", "/checkout"), new("ویرایش سبد", "/cart")]);

        var main = addresses[0];
        text += $"\nارسال به آدرس «{main.Title}» ({main.City})؟ با «تأیید و ادامه» به صفحهٔ پرداخت می‌روید؛ روش ارسال را انتخاب و پرداخت را خودتان انجام می‌دهید.";
        var actions = new List<AssistantAction> { new("تأیید و ادامه", $"/checkout?addressId={main.Id}") };
        actions.AddRange(addresses.Skip(1).Select(a => new AssistantAction($"ارسال به «{a.Title}»", $"/checkout?addressId={a.Id}")));
        actions.Add(new("ویرایش سبد", "/cart"));
        return Simple(AssistantIntent.Checkout, text, actions);
    }

    private async Task<Outcome> AddressAsync(Guid? userId, string? name, CancellationToken cancellationToken)
    {
        if (userId is null)
            return Simple(AssistantIntent.Address, "برای دیدن آدرس‌های ذخیره‌شده لطفاً وارد حساب شوید.", [new("ورود", "/login?returnUrl=%2Fprofile%2Faddresses")]);
        var addresses = await context.UserAddresses.AsNoTracking()
            .Where(a => a.UserId == userId)
            .OrderByDescending(a => a.IsDefault).ThenByDescending(a => a.CreatedAt)
            .Select(a => new { a.Title, City = a.City.Name, a.IsDefault })
            .Take(6)
            .ToListAsync(cancellationToken);
        if (addresses.Count == 0)
            return Simple(AssistantIntent.Address, $"هنوز آدرسی ثبت نکرده‌اید{Comma(name)}.", [new("افزودن آدرس", "/profile/addresses")]);
        var lines = addresses.Select(a => $"• {a.Title} ({a.City}){(a.IsDefault ? " – پیش‌فرض" : "")}");
        return Simple(AssistantIntent.Address, $"آدرس‌های شما{Comma(name)}:\n{string.Join("\n", lines)}", [new("مدیریت آدرس‌ها", "/profile/addresses"), new("تسویه حساب", Say: "تسویه حساب")]);
    }

    // ── پیگیری سفارش ─────────────────────────────────────────────

    /// <summary>متن وضعیت سفارش برای مشتری. مثال: (Pending، Pending) ← «در انتظار پرداخت».</summary>
    public static string StatusText(OrderStatus status, PaymentStatus payment) => status switch
    {
        OrderStatus.Pending when payment is PaymentStatus.Pending or PaymentStatus.Failed => "در انتظار پرداخت",
        OrderStatus.Pending => "ثبت شده",
        OrderStatus.Confirmed => "تأیید شده",
        OrderStatus.Processing => "در حال آماده‌سازی",
        OrderStatus.Shipped => "ارسال شده",
        OrderStatus.Delivered => "تحویل شده",
        OrderStatus.Cancelled => "لغو شده",
        OrderStatus.Returned => "مرجوع شده",
        _ => "در حال بررسی",
    };

    private async Task<Outcome> TrackAsync(Guid? userId, string? orderNumber, string? name, CancellationToken cancellationToken)
    {
        if (userId is null)
            return Simple(AssistantIntent.Track, "برای دیدن سفارش‌هایتان لطفاً وارد حساب شوید؛ یا با شمارهٔ سفارش و موبایل از صفحهٔ پیگیری استفاده کنید.",
                [new("ورود به حساب", "/login?returnUrl=%2Fprofile%2Forders"), new("پیگیری با شمارهٔ سفارش", "/track-order")]);

        var query = context.Orders.AsNoTracking().Where(o => o.UserId == userId);
        if (orderNumber is not null)
            query = query.Where(o => o.OrderNumber == orderNumber);
        var orders = await query
            .OrderByDescending(o => o.CreatedAt)
            .Take(3)
            .Select(o => new
            {
                o.OrderNumber,
                o.Status,
                o.PaymentStatus,
                o.TrackingCode,
                o.PayableAmount,
                Post = context.ShipmentTrackings.Where(t => t.OrderId == o.Id).OrderByDescending(t => t.LastEventAt).Select(t => t.LastStatus).FirstOrDefault(),
            })
            .ToListAsync(cancellationToken);
        if (orders.Count == 0)
            return Simple(AssistantIntent.Track, orderNumber is null
                ? $"هنوز سفارشی در حساب شما ثبت نشده است{Comma(name)}."
                : "سفارشی با این شماره در حساب شما پیدا نشد. شماره را دوباره بررسی کنید.", [new("سفارش‌های من", "/profile/orders")]);

        var lines = orders.Select(o =>
            $"• سفارش {o.OrderNumber} ({PersianText.Money(o.PayableAmount)} تومان): {StatusText(o.Status, o.PaymentStatus)}" +
            (string.IsNullOrWhiteSpace(o.TrackingCode) ? "" : $" – کد رهگیری: {o.TrackingCode}") +
            (string.IsNullOrWhiteSpace(o.Post) ? "" : $" – آخرین وضعیت پست: {o.Post}"));
        var actions = orders.Select(o => new AssistantAction($"جزئیات {o.OrderNumber}", $"/profile/orders/{Uri.EscapeDataString(o.OrderNumber)}")).ToList();
        return Simple(AssistantIntent.Track, $"وضعیت سفارش‌های شما{Comma(name)}:\n{string.Join("\n", lines)}", actions);
    }

    // ── مقایسه ───────────────────────────────────────────────────

    private async Task<Outcome> CompareAsync(AssistantRequest request, string text, AssistantParse parse, List<Guid> last, CancellationToken cancellationToken)
    {
        var picked = new List<ProductFilterData>();
        if (AssistantNlu.CompareParts(text) is { } parts)
        {
            foreach (var part in new[] { parts.First, parts.Second })
                if ((await FindAsync(part, ProductSortBy.BestSelling, cancellationToken)).FirstOrDefault() is { } hit && picked.All(p => p.Id != hit.Id))
                    picked.Add(hit);
        }
        if (picked.Count < 2)
        {
            var ids = new[] { request.ActionProductId, request.CurrentProductId }.Where(i => i is not null).Select(i => i!.Value).Concat(last).Distinct().Take(2).ToList();
            if (ids.Count == 2)
                picked = await products.GetCardsByIdsAsync(ids);
        }
        if (picked.Count < 2)
            return new Outcome(new AssistantReply("دو کالا را نام ببرید؛ مثلاً «مقایسهٔ ایرپاد ۳ با گلکسی بادز ۲».", [], [], AssistantIntent.Compare), Answered: true, Query: parse.Query);

        var a = picked[0];
        var b = picked[1];
        var featureRows = await context.ProductFeatures.AsNoTracking()
            .Where(f => f.ProductId == a.Id || f.ProductId == b.Id)
            .OrderBy(f => f.DisplayOrder)
            .Select(f => new { f.ProductId, f.Key, f.Value })
            .Take(200)
            .ToListAsync(cancellationToken);
        var first = featureRows.Where(f => f.ProductId == a.Id).GroupBy(f => PersianText.Normalize(f.Key)).ToDictionary(g => g.Key, g => g.First());
        var second = featureRows.Where(f => f.ProductId == b.Id).GroupBy(f => PersianText.Normalize(f.Key)).ToDictionary(g => g.Key, g => g.First());
        var shared = first.Keys.Where(second.ContainsKey).Take(6)
            .Select(k => $"• {first[k].Key}: {first[k].Value} | {second[k].Value}");
        var lines = new List<string>
        {
            $"مقایسهٔ «{a.Name}» | «{b.Name}»:",
            $"• قیمت: {PersianText.Money(a.FinalPrice)} | {PersianText.Money(b.FinalPrice)} تومان",
            $"• موجودی: {(a.IsInStock ? "موجود" : "ناموجود")} | {(b.IsInStock ? "موجود" : "ناموجود")}",
        };
        lines.AddRange(shared);
        return new Outcome(new AssistantReply(string.Join("\n", lines), [ToCard(a), ToCard(b)], [], AssistantIntent.Compare), Query: parse.Query);
    }

    // ── ثبت، گزارش و خوراک تکمیل هوشمند ────────────────────────

    private async Task<string?> LastNameAsync(Guid userId, CancellationToken cancellationToken)
    {
        var name = await context.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => u.LastName ?? u.FirstName)
            .FirstOrDefaultAsync(cancellationToken);
        name = name?.Trim();
        return string.IsNullOrEmpty(name) ? null : name.Length > 40 ? name[..40] : name;
    }

    private static string? Cut(string? value, int max)
    {
        value = value?.Trim();
        return string.IsNullOrEmpty(value) ? null : value.Length > max ? value[..max] : value;
    }

    private async Task LogAsync(AssistantRequest request, string text, Outcome outcome, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;
        var productId = outcome.ProductId ?? outcome.Reply.Lookup?.ProductId;
        try
        {
            context.AssistantMessages.Add(new AssistantMessage
            {
                SessionKey = Cut(request.SessionKey, 32) ?? "unknown",
                UserId = request.UserId,
                Text = Cut(AssistantNlu.Mask(text), 500)!,
                Intent = outcome.Reply.Intent,
                ProductId = productId,
                Answered = outcome.Answered,
                Topic = Cut(outcome.Topic, 60),
                Query = Cut(outcome.Query, 200),
            });
            await context.SaveChangesAsync(cancellationToken);
            if (!outcome.Answered && productId is { } id && outcome.Reply.Intent == AssistantIntent.ProductQuestion)
                await MaybeFeedAsync(id, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // ثبت گزارش هرگز نباید پاسخ مشتری را خراب کند.
            context.ChangeTracker.Clear();
            logger.LogWarning(exception, "Assistant message log failed");
        }
    }

    /// <summary>دومین پرسش بی‌جواب ۳۰ روز اخیر دربارهٔ یک کالا ← «دریافت مشخصات» در صف هوشمند (فقط یک بار؛ صف هم تکراری نمی‌پذیرد).</summary>
    private async Task MaybeFeedAsync(Guid productId, CancellationToken cancellationToken)
    {
        if (queue is null || smart is null || !(await smart.GetAsync(cancellationToken)).Enabled)
            return;
        var since = DateTimeOffset.UtcNow.AddDays(-30);
        var count = await context.AssistantMessages.CountAsync(m => m.ProductId == productId && !m.Answered && m.Intent == AssistantIntent.ProductQuestion && m.CreatedAt >= since, cancellationToken);
        if (count != GapThreshold)
            return;
        var title = await context.Products.Where(p => p.Id == productId).Select(p => p.Name).FirstOrDefaultAsync(cancellationToken);
        if (title is not null)
            await queue.EnqueueAsync(SmartTaskKind.ProductSpecs, SmartTarget.Product, productId, title, SmartPriority.Batch, null, null, Actor, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<int> FeedGapsAsync(int max, CancellationToken cancellationToken = default)
    {
        if (queue is null || max <= 0 || smart is not null && !(await smart.GetAsync(cancellationToken)).Enabled)
            return 0;
        var since = DateTimeOffset.UtcNow.AddDays(-30);
        var gaps = await context.AssistantMessages.AsNoTracking()
            .Where(m => m.ProductId != null && !m.Answered && m.Intent == AssistantIntent.ProductQuestion && m.CreatedAt >= since)
            .GroupBy(m => m.ProductId!.Value)
            .Select(g => new { ProductId = g.Key, Count = g.Count() })
            .Where(g => g.Count >= GapThreshold)
            .OrderByDescending(g => g.Count)
            .Take(Math.Clamp(max, 1, 50))
            .ToListAsync(cancellationToken);
        if (gaps.Count == 0)
            return 0;
        var ids = gaps.Select(g => g.ProductId).ToList();
        var names = await context.Products.AsNoTracking().Where(p => ids.Contains(p.Id) && p.IsActive).Select(p => new { p.Id, p.Name }).ToListAsync(cancellationToken);
        foreach (var product in names)
            await queue.EnqueueAsync(SmartTaskKind.ProductSpecs, SmartTarget.Product, product.Id, product.Name, SmartPriority.Batch, null, null, Actor, cancellationToken);
        return names.Count;
    }

    /// <inheritdoc />
    public async Task<int> PruneAsync(int days, CancellationToken cancellationToken = default)
    {
        var cutoff = DateTimeOffset.UtcNow.AddDays(-Math.Max(30, days));
        return await context.AssistantMessages.IgnoreQueryFilters().Where(m => m.CreatedAt < cutoff).ExecuteDeleteAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<AssistantInsights> InsightsAsync(int days, CancellationToken cancellationToken = default)
    {
        days = Math.Clamp(days, 1, 180);
        var since = DateTimeOffset.UtcNow.AddDays(-days);
        var messages = context.AssistantMessages.AsNoTracking().Where(m => m.CreatedAt >= since);

        var total = await messages.CountAsync(cancellationToken);
        var sessions = await messages.Select(m => m.SessionKey).Distinct().CountAsync(cancellationToken);
        var answered = await messages.CountAsync(m => m.Answered, cancellationToken);
        var intents = await messages.GroupBy(m => m.Intent).Select(g => new { g.Key, Count = g.Count() }).OrderByDescending(g => g.Count).ToListAsync(cancellationToken);
        var topics = await messages.Where(m => m.Topic != null).GroupBy(m => m.Topic!).Select(g => new { g.Key, Count = g.Count() })
            .OrderByDescending(g => g.Count).Take(15).ToListAsync(cancellationToken);
        var questions = await messages
            .Where(m => m.Intent != AssistantIntent.Greeting && m.Intent != AssistantIntent.Thanks && m.Intent != AssistantIntent.CartShow && m.Intent != AssistantIntent.CartAdd)
            .GroupBy(m => m.Text).Select(g => new { g.Key, Count = g.Count() })
            .Where(g => g.Count >= 2).OrderByDescending(g => g.Count).Take(15).ToListAsync(cancellationToken);
        var demands = await messages.Where(m => m.Intent == AssistantIntent.Demand && m.Query != null)
            .GroupBy(m => m.Query!).Select(g => new { g.Key, Count = g.Count(), Last = g.Max(m => m.CreatedAt) })
            .OrderByDescending(g => g.Count).ThenByDescending(g => g.Last).Take(30).ToListAsync(cancellationToken);
        var gaps = await messages.Where(m => !m.Answered && m.ProductId != null && m.Intent == AssistantIntent.ProductQuestion)
            .GroupBy(m => new { ProductId = m.ProductId!.Value, m.Topic }).Select(g => new { g.Key.ProductId, g.Key.Topic, Count = g.Count(), Last = g.Max(m => m.CreatedAt) })
            .OrderByDescending(g => g.Count).ThenByDescending(g => g.Last).Take(30).ToListAsync(cancellationToken);
        var gapIds = gaps.Select(g => g.ProductId).Distinct().ToList();
        var names = await context.Products.AsNoTracking().Where(p => gapIds.Contains(p.Id)).Select(p => new { p.Id, p.Name }).ToDictionaryAsync(p => p.Id, p => p.Name, cancellationToken);
        var unknown = await messages.Where(m => !m.Answered && m.ProductId == null)
            .OrderByDescending(m => m.CreatedAt).Take(20).Select(m => new { m.Text, m.Intent, m.CreatedAt }).ToListAsync(cancellationToken);

        return new AssistantInsights(days, total, sessions, answered,
            intents.Select(i => new AssistantCount(i.Key.ToString(), i.Count)).ToList(),
            topics.Select(t => new AssistantCount(t.Key, t.Count)).ToList(),
            questions.Select(q => new AssistantCount(q.Key, q.Count)).ToList(),
            demands.Select(d => new AssistantDemand(d.Key, d.Count, d.Last)).ToList(),
            gaps.Where(g => names.ContainsKey(g.ProductId)).Select(g => new AssistantGap(g.ProductId, names[g.ProductId], g.Topic, g.Count, g.Last)).ToList(),
            unknown.Select(u => new AssistantRecent(u.Text, u.Intent, u.CreatedAt)).ToList());
    }
}
