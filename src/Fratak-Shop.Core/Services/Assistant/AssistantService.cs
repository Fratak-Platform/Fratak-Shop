using Fratak_Shop.Core.DTOs.Products.Query;
using Fratak_Shop.Core.DTOs.Settings;
using Fratak_Shop.Core.DTOs.Shopping.Command;
using Fratak_Shop.Core.Services.Orders;
using Fratak_Shop.Core.Services.Products;
using Fratak_Shop.Core.Services.Searches;
using Fratak_Shop.Core.Services.Seo.Engine;
using Fratak_Shop.Core.Services.Settings;
using Fratak_Shop.Core.Services.Shopping;
using Fratak_Shop.Core.Services.Smart;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Assistant;
using Fratak_Shop.Data.Entities.Comments;
using Fratak_Shop.Data.Entities.Loyalty;
using Fratak_Shop.Data.Entities.Marketing;
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
    Func<bool, Task<Guid?>> Cart)
{
    /// <summary>دور ۲۴: آخرین عبارت جست‌وجوی همین گفت‌وگو (برای «داریم؟»، «ارزون‌ترش؟» بعد از یک جست‌وجو). حداکثر ۱۰۰ نویسه.</summary>
    public string? LastQuery { get; init; }

    /// <summary>دور ۲۴: مسیر صفحه‌ای که مشتری در آن است (مثلاً «/category/power-bank»)؛ برای راهنمای زنده و بازگشت بعد از ورود.</summary>
    public string? PagePath { get; init; }

    /// <summary>دور ۲۴: وضعیت جریان دکمه‌ای (مثلاً کالایاب «finder:power-bank:2»).</summary>
    public string? Flow { get; init; }

    /// <summary>دور ۲۴: سلیقهٔ گفت‌وگوی مهمان (از حافظهٔ مرورگر). کاربر واردشده از جدول AssistantProfiles خوانده می‌شود.</summary>
    public AssistantStyle? Style { get; init; }
}

/// <summary>
/// دور ۲۴: سلیقهٔ گفت‌وگو. CallMe = اسم دلخواه؛ Tone = لحن انتخابی (Auto = خودکار)؛ Register = طرز نوشتن مشاهده‌شده (‎-3 تا ‎+3).
/// مثال: ("علی"، Auto، 2) ← مشتری خواسته «علی» صدایش کنیم و خودمانی می‌نویسد.
/// </summary>
public sealed record AssistantStyle(string? CallMe, AssistantTone Tone, int Register);

/// <summary>دور ۲۴: پرسش‌های پیشنهادی یک صفحه + نام و معرفی دستیار (برای ویجت).</summary>
public sealed record AssistantSuggestion(string Name, string Intro, IReadOnlyList<AssistantAction> Chips, bool Greeting, bool HandoffEnabled);

/// <summary>کارت کالا در پاسخ دستیار. دور ۲۵: Note = برچسب کوتاه مشاور خرید روی کارت (مثلاً «بیشترین رضایت»).</summary>
public sealed record AssistantCard(Guid Id, string Name, string Url, string? ImageUrl, decimal Price, decimal FinalPrice, int DiscountPercent, bool InStock, string? Note = null);

/// <summary>دکمهٔ پاسخ: یا لینک (Url) یا پیامی که با زدنش فرستاده می‌شود (Say).</summary>
/// <remarks>دور ۲۴: Flow = وضعیت جریان دکمه‌ای (مثلاً کالایاب) که با زدن دکمه همراه پیام برمی‌گردد.</remarks>
public sealed record AssistantAction(string Label, string? Url = null, string? Say = null, string? Flow = null);

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
    bool CartChanged = false)
{
    /// <summary>دور ۲۴: قدم‌های راهنمای زنده (رفتن به صفحه، برجسته کردن دکمه، …). <see cref="AssistantGuide"/>.</summary>
    public IReadOnlyList<AssistantStep> Steps { get; init; } = [];

    /// <summary>دور ۲۴: عبارت جست‌وجویی که مرورگر برای پیام بعدی نگه می‌دارد (حافظهٔ گفت‌وگو).</summary>
    public string? Query { get; init; }

    /// <summary>دور ۲۴: سلیقهٔ به‌روزشدهٔ گفت‌وگو (مرورگر برای مهمان ذخیره می‌کند).</summary>
    public AssistantStyle? Style { get; init; }

    /// <summary>دور ۲۴: گفت‌وگو با کارشناس باز است (ویجت شروع به گرفتن پیام‌های کارشناس می‌کند).</summary>
    public bool Handoff { get; init; }

    /// <summary>دور ۲۵: شناسهٔ پیام ثبت‌شده (برای دکمه‌های 👍/👎)؛ ثبت نشد ← null.</summary>
    public Guid? MessageId { get; init; }
}

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

    /// <summary>
    /// دور ۲۴: پرسش‌های پیشنهادی صفحه‌ای که مشتری در آن است + نام و معرفی دستیار (با لحن همان مشتری).
    /// مثال: صفحهٔ کالا ← «معرفی کوتاه این کالا»، «مشخصات کلیدی»، «کی به دستم می‌رسد؟»…
    /// </summary>
    Task<AssistantSuggestion> SuggestAsync(string? pagePath, Guid? productId, Guid? userId, AssistantStyle? style, CancellationToken cancellationToken = default);

    /// <summary>دور ۲۴: آموزش دوبارهٔ مدل نیت پیام از پیام‌های واقعی ۹۰ روز اخیر (نگه‌داری شبانه). خروجی: تعداد نمونهٔ واقعی.</summary>
    Task<int> RetrainIntentModelAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// دور ۲۵: نظر مشتری دربارهٔ یک پاسخ (👍 = true، 👎 = false). فقط پیام همان گفت‌وگو (SessionKey) و تا ۲۴ ساعت. خروجی: ثبت شد؟
    /// پاسخ‌های 👎 در پنل «آموزش دستیار» نشان داده می‌شوند تا کارکنان جواب درست را یاد بدهند.
    /// </summary>
    Task<bool> FeedbackAsync(Guid messageId, string sessionKey, bool helpful, CancellationToken cancellationToken = default);
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
    ISmartConfigService? smart = null,
    IAssistantKnowledge? knowledge = null,
    IAssistantHandoffService? handoffs = null,
    IOrderService? orderActions = null,
    IReturnService? returns = null,
    IAssistantTeachingService? teachings = null,
    IAssistantFactService? facts = null) : IAssistantService
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
        // دور ۲۵: فهم در یک جا – اصلاح غلط تایپی/فینگلیش/محاوره، خواسته‌های خرید (بودجه، مرتب‌سازی…) و نیت.
        var understood = AssistantUnderstanding.Understand(text, await VocabularyAsync(cancellationToken));
        var parse = understood.Parse;
        var said = understood.Prepared.Text;
        var store = await settings.GetAsync(cancellationToken);
        var options = smart is null ? new SmartOptions().Normalize() : await smart.GetAsync(cancellationToken);
        var (voice, style) = await VoiceAsync(request, text, cancellationToken);
        var last = request.LastProductIds.Distinct().Take(6).ToList();

        // دور ۲۴: گفت‌وگوی باز با کارشناس ← پیام به کارشناس می‌رود (دستیار جواب خودکار نمی‌دهد مگر مشتری بخواهد برگردد).
        if (handoffs is not null && await handoffs.IsOpenAsync(request.SessionKey, cancellationToken))
        {
            var sent = await handoffs.PostCustomerAsync(request.SessionKey, text, cancellationToken);
            var handoffReply = new AssistantReply(sent
                    ? "پیامتان برای کارشناس فرستاده شد؛ جواب همین‌جا نشان داده می‌شود."
                    : "پیام‌های زیادی فرستاده‌اید؛ لطفاً کمی صبر کنید تا کارشناس جواب دهد.",
                [], [], AssistantIntent.Handoff) { Handoff = true, Style = style };
            await LogAsync(request, text, new Outcome(handoffReply), cancellationToken);
            return handoffReply;
        }

        // دور ۲۵: جوابی که کارکنان برای همین پرسش یاد داده‌اند (پیش از قانون‌ها؛ دکمه‌های کارت و جریان کالایاب دست نمی‌خورند).
        AssistantTaught? taught = null;
        if (teachings is not null && request.Flow is null && request.ActionProductId is null)
        {
            try
            {
                taught = await teachings.MatchAsync(said, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning(exception, "Assistant teaching match failed");
            }
        }

        // دور ۲۴: مدل یادگیرنده فقط وقتی قانون‌ها پیام را «جست‌وجو» دیده‌اند نظر دوم می‌دهد (مثلاً «میشه با یه نفر حرف بزنم» ← پشتیبان).
        var intent = understood.Route;
        var finder = AssistantFinder.Parse(request.Flow);
        var learnedRoute = false;
        if (finder is not null)
            intent = AssistantIntent.Finder;
        else if (taught?.Intent is { } taughtIntent)
            intent = taughtIntent;
        else if (intent == AssistantIntent.Search && !Similar(parse) && !understood.Slots.Strong && AssistantIntentModel.Suggest(said) is { } learned)
        {
            intent = learned;
            learnedRoute = true;
        }
        // «مشابه این» / «مشابه این موجود» روی صفحهٔ کالا یا بعد از نمایش کارت.
        var similarBase = intent is AssistantIntent.Search or AssistantIntent.ProductQuestion && Similar(parse) ? request.CurrentProductId ?? last.FirstOrDefault() : (Guid?)null;
        if (similarBase == Guid.Empty)
            similarBase = null;

        // دور ۲۸: «مغز» دستیار – راهنمای خرید («برای خرید پاوربانک به چی دقت کنم»)، واژه‌نامه («ANC یعنی چی») و گپ کوتاه انسانی
        // («خسته نباشی»، «ربات هستی؟»). فقط وقتی قانون‌ها پیام را جست‌وجو/نامفهوم/بی‌ربط دیده‌اند (یا «راهنما» برای راهنمای خرید)؛
        // کارهای حساس (لغو، مرجوعی، پرداخت) و جریان‌های دکمه‌ای هرگز از این مسیر نمی‌گذرند.
        BrainHit? brain = null;
        if (facts is not null && taught is null && finder is null && similarBase is null && request.ActionProductId is null
            && (understood.Route is AssistantIntent.Search or AssistantIntent.Unknown or AssistantIntent.OffTopic or AssistantIntent.Help
                || understood.Route == AssistantIntent.ProductQuestion && request.CurrentProductId is null && last.Count == 0))
        {
            try
            {
                // تصمیم با مسیر قانون‌ها (پیش از نظر مدل یادگیرنده): دانستنیِ دقیق بر حدس آماری مقدم است.
                brain = AssistantBrain.Match(text, await facts.FactsAsync(cancellationToken));
                if (brain is not null && understood.Route == AssistantIntent.Help && brain.Fact.Kind != AssistantFactKind.Guide)
                    brain = null;
                // «ANC یعنی چی» بدون کالای در گفت‌وگو: پرسش معنی است نه پرسش دربارهٔ یک کالا.
                if (brain is not null && understood.Route == AssistantIntent.ProductQuestion && brain.Fact.Kind != AssistantFactKind.Glossary)
                    brain = null;
                // «باشه» وقتی کالایی در گفت‌وگو هست یعنی «دربارهٔ همین بگو» (مسیر قبلی)، نه گپ کوتاه.
                if (brain is { Fact.Kind: AssistantFactKind.SmallTalk } && (request.CurrentProductId is not null || last.Count > 0)
                    && brain.Fact.Term == "باشه")
                    brain = null;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning(exception, "Assistant knowledge match failed");
            }
        }

        // گپ کوتاه در برابر مدل یادگیرنده کوتاه می‌آید («ربات نمیخوام» یعنی «کارشناس»، نه «نه ممنون»)؛ راهنما و واژه‌نامه دقیق‌ترند و مقدم.
        if (brain is { Fact.Kind: AssistantFactKind.SmallTalk } && learnedRoute)
            brain = null;
        if (brain is not null)
        {
            intent = understood.Route;
            learnedRoute = false;
        }

        Outcome outcome;
        try
        {
            outcome = intent switch
            {
                _ when taught is { Intent: null } => Taught(taught),
                _ when similarBase is { } baseId => await SimilarAsync(baseId, voice, cancellationToken),
                _ when brain is not null => await BrainAsync(brain, intent, voice, cancellationToken),
                AssistantIntent.Greeting => Greeting(voice, store.SiteName, options.AssistantName),
                AssistantIntent.Thanks => Simple(AssistantIntent.Thanks, voice.Thanks()),
                AssistantIntent.Help => Help(voice, store.SiteName, options.AssistantName),
                AssistantIntent.Preference => await PreferenceAsync(request, voice, style, cancellationToken),
                AssistantIntent.Human => await HumanAsync(request, voice, store, options.AssistantHandoff, cancellationToken),
                AssistantIntent.OffTopic => Simple(AssistantIntent.OffTopic,
                    $"ببخشید{voice.Comma}، من فقط دربارهٔ کالاها، خرید و سفارش‌های {store.SiteName} می‌توانم کمک کنم. مثلاً بپرسید «قیمت هدفون بلوتوثی» یا «سفارشم کجاست؟»",
                    Suggestions()),
                AssistantIntent.Track => await TrackAsync(request.UserId, parse.OrderNumber, voice, cancellationToken),
                AssistantIntent.CartShow => await CartShowAsync(request, voice),
                AssistantIntent.CartAdd => await CartAddAsync(request, parse, last, voice, cancellationToken),
                AssistantIntent.CartRemove => await CartRemoveAsync(request, parse, voice),
                AssistantIntent.Checkout => await CheckoutAsync(request, voice, store.FreeShippingThreshold, cancellationToken),
                AssistantIntent.Address => await AddressAsync(request.UserId, voice, cancellationToken),
                AssistantIntent.Compare => await CompareAsync(request, said, parse, last, cancellationToken),
                AssistantIntent.Navigate => await NavigateAsync(request, parse, last, voice, store.FreeShippingThreshold, cancellationToken),
                AssistantIntent.Login => Login(request, text, voice),
                AssistantIntent.Offers => await OffersAsync(request, parse, voice, store.FreeShippingThreshold, cancellationToken),
                AssistantIntent.Finder => await FinderAsync(finder ?? new FinderState(null, null, null), parse, voice, cancellationToken),
                AssistantIntent.ProductQuestion => await QuestionAsync(request, parse, last, voice, store, cancellationToken),
                // دور ۲۵: مشاور خرید و کارهای بعد از خرید.
                AssistantIntent.Advisor => await AdvisorAsync(understood, request, voice, cancellationToken),
                AssistantIntent.OrderCancel => await CancelOrderAsync(request, parse, voice, store, cancellationToken),
                AssistantIntent.OrderReturn => await ReturnOrderAsync(request, parse, voice, store, cancellationToken),
                AssistantIntent.Invoice => await InvoiceAsync(request, parse, voice, cancellationToken),
                _ => await SearchAsync(said, parse, request, voice, cancellationToken),
            };
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Assistant reply failed for intent {Intent}", intent);
            outcome = new Outcome(new AssistantReply($"{voice.Sorry}، الان نتوانستم این کار را انجام دهم. لطفاً چند لحظهٔ دیگر دوباره امتحان کنید.", [], [], intent), Answered: false);
        }

        if (learnedRoute)
            outcome = outcome with { Topic = LearnedTopic };
        else if (taught is not null && outcome.Topic is null)
            outcome = outcome with { Topic = TaughtTopic };
        var messageId = await LogAsync(request, text, outcome, cancellationToken);
        var reply = outcome.Reply;
        return reply with
        {
            Steps = AssistantGuide.Clean(reply.Steps),
            Style = reply.Style ?? style,
            // حافظهٔ گفت‌وگو: آخرین عبارت جست‌وجو (برای «داریم؟» یا «ارزون‌ترش» در پیام بعد).
            Query = reply.Query ?? (outcome.Query is { Length: >= 2 } q && reply.Intent is AssistantIntent.Search or AssistantIntent.Demand or AssistantIntent.ProductQuestion or AssistantIntent.Advisor ? q : request.LastQuery),
            MessageId = messageId,
        };
    }

    // ── شخصیت و لحن (دور ۲۴) ─────────────────────────────────────

    /// <summary>
    /// صدای دستیار برای این پیام. کاربر واردشده: نام، تاریخ تولد و سلیقهٔ ذخیره‌شده از پایگاه داده (طرز نوشتن هم به‌روز می‌شود)؛
    /// مهمان: سلیقه‌ای که مرورگر فرستاده. خروجی دوم = سلیقهٔ به‌روز برای ذخیره در مرورگر.
    /// </summary>
    private async Task<(AssistantVoice Voice, AssistantStyle Style)> VoiceAsync(AssistantRequest request, string text, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(IranClock.ToLocal(DateTimeOffset.UtcNow).DateTime);
        var guest = request.Style;
        var register = AssistantPersona.UpdateRegister(Math.Clamp(guest?.Register ?? 0, -AssistantPersona.MaxRegister, AssistantPersona.MaxRegister), text);
        var callMe = AssistantPersona.CleanName(guest?.CallMe);
        var tone = guest?.Tone is { } t && Enum.IsDefined(t) ? t : AssistantTone.Auto;
        if (request.UserId is not { } userId)
        {
            var guestStyle = new AssistantStyle(callMe, tone, register);
            return (AssistantPersona.Build(callMe, null, null, AssistantPersona.ToneFor(AssistantGeneration.Unknown, register, tone)), guestStyle);
        }

        var user = await context.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.FirstName, u.LastName, u.BirthDate })
            .FirstOrDefaultAsync(cancellationToken);
        var profile = await context.AssistantProfiles.AsNoTracking()
            .Where(p => p.UserId == userId)
            .Select(p => new { p.CallMe, p.Tone, p.Register })
            .FirstOrDefaultAsync(cancellationToken);
        var savedRegister = AssistantPersona.UpdateRegister(profile?.Register ?? 0, text);
        if (savedRegister != (profile?.Register ?? 0))
            await SaveRegisterAsync(userId, savedRegister, profile is not null, cancellationToken);
        var generation = AssistantPersona.Generation(user?.BirthDate, today);
        var finalTone = AssistantPersona.ToneFor(generation, savedRegister, profile?.Tone ?? AssistantTone.Auto);
        var voice = AssistantPersona.Build(user?.FirstName, user?.LastName, profile?.CallMe, finalTone);
        return (voice, new AssistantStyle(profile?.CallMe, profile?.Tone ?? AssistantTone.Auto, savedRegister));
    }

    /// <summary>ذخیرهٔ طرز نوشتن کاربر واردشده (ردیف تازه فقط وقتی طرز نوشتن از حالت عادی فاصله گرفت). خطای هم‌زمانی بی‌اثر است.</summary>
    private async Task SaveRegisterAsync(Guid userId, int register, bool exists, CancellationToken cancellationToken)
    {
        try
        {
            if (exists)
                await context.AssistantProfiles.Where(p => p.UserId == userId).ExecuteUpdateAsync(s => s.SetProperty(p => p.Register, register), cancellationToken);
            else
            {
                context.AssistantProfiles.Add(new AssistantProfile { UserId = userId, Register = register });
                await context.SaveChangesAsync(cancellationToken);
                context.ChangeTracker.Clear();
            }
        }
        catch (DbUpdateException exception)
        {
            // دو پیام هم‌زمان هر دو ردیف تازه ساختند؛ یکی کافی است.
            context.ChangeTracker.Clear();
            logger.LogDebug(exception, "Assistant profile race for {User}", userId);
        }
    }

    /// <summary>«منو علی صدا کن» / «رسمی صحبت کن»: ذخیره (کاربر واردشده در جدول؛ مهمان در مرورگر) و تأیید با لحن تازه.</summary>
    private async Task<Outcome> PreferenceAsync(AssistantRequest request, AssistantVoice voice, AssistantStyle style, CancellationToken cancellationToken)
    {
        var change = AssistantPersona.ParsePreference(request.Text);
        if (change is null)
            return Simple(AssistantIntent.Preference, "متوجه نشدم چطور صدایتان کنم؛ مثلاً بنویسید «منو علی صدا کن» یا «رسمی صحبت کن».");
        var callMe = change.ForgetName ? null : change.CallMe ?? style.CallMe;
        var tone = change.Tone ?? style.Tone;
        var updated = new AssistantStyle(callMe, tone, style.Register);
        if (request.UserId is { } userId)
        {
            var row = await context.AssistantProfiles.AsTracking().FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);
            if (row is null)
                context.AssistantProfiles.Add(new AssistantProfile { UserId = userId, CallMe = callMe, Tone = tone, Register = style.Register });
            else
            {
                row.CallMe = callMe;
                row.Tone = tone;
            }
            await context.SaveChangesAsync(cancellationToken);
            context.ChangeTracker.Clear();
        }
        var (fresh, _) = await VoiceAsync(request with { Style = updated, Text = "" }, "", cancellationToken);
        var text = change switch
        {
            { ForgetName: true } => "باشه، از این به بعد با نام حسابتان صدایتان می‌کنم.",
            { CallMe: { } name } => fresh.IsCasual ? $"حله {name}! از این به بعد همین‌طوری صدات می‌کنم 🙂" : $"چشم{fresh.Comma}؛ از این به بعد «{name}» صدایتان می‌کنم.",
            { Tone: AssistantTone.Formal } => $"چشم{fresh.Comma}، از این به بعد رسمی خدمتتان عرض می‌کنم.",
            { Tone: AssistantTone.Casual } => $"اوکی{(fresh.First is null ? "" : " " + fresh.First)}! از این به بعد خودمونی‌تر حرف می‌زنیم 🙂",
            _ => "باشه، لحنم را خودکار با شما هماهنگ می‌کنم.",
        };
        return new Outcome(new AssistantReply(text, [], Suggestions(), AssistantIntent.Preference) { Style = updated });
    }

    // ── پاسخ‌های ساده ───────────────────────────────────────────

    private static Outcome Simple(AssistantIntent intent, string text, IReadOnlyList<AssistantAction>? actions = null) =>
        new(new AssistantReply(text, [], actions ?? [], intent));

    private static List<AssistantAction> Suggestions() =>
    [
        new("کمکم کن انتخاب کنم", Say: "کمکم کن انتخاب کنم", Flow: AssistantFinder.Build(null)),
        new("تخفیف‌ها", Say: "تخفیف دارید؟"),
        new("سبد خرید من", Say: "سبد خرید من"),
        new("پیگیری سفارش", Say: "سفارشم کجاست"),
    ];

    private static Outcome Greeting(AssistantVoice voice, string siteName, string botName) => Simple(AssistantIntent.Greeting,
        voice.Hello(botName, siteName, IranClock.ToLocal(DateTimeOffset.UtcNow).Hour) + " " + (voice.IsCasual
            ? "اسم کالا رو بنویس یا از قیمت، موجودی، ارسال و سفارشت بپرس؛ کارای خرید رو هم می‌تونم برات انجام بدم."
            : "نام کالا را بنویسید یا دربارهٔ قیمت، موجودی، ارسال و سفارشتان بپرسید؛ کارهای خرید را هم می‌توانم برایتان انجام دهم."),
        Suggestions());

    private static Outcome Help(AssistantVoice voice, string siteName, string botName) => Simple(AssistantIntent.Help,
        $"من {botName} هستم، دستیار هوشمند {siteName}{voice.Comma}. این کارها از من برمی‌آید:\n" +
        "• پیدا کردن کالا، قیمت و موجودی (مثلاً «هدفون بلوتوثی زیر ۲ میلیون»)\n" +
        "• معرفی و مشخصات کالا، گارانتی، زمان ارسال و مرجوعی\n" +
        "• کمک در انتخاب و مقایسهٔ کالاها\n" +
        "• افزودن به سبد و بردن شما تا صفحهٔ پرداخت (پرداخت را خودتان می‌زنید)\n" +
        "• تخفیف‌ها، کد تخفیف و امتیاز باشگاه\n" +
        "• پیگیری سفارش و کمک برای ورود به حساب\n" +
        "• وصل کردن شما به کارشناس\n" +
        "اگر خواستید بگویید چطور صدایتان کنم؛ مثلاً «منو علی صدا کن» یا «رسمی صحبت کن».",
        Suggestions());

    /// <summary>درخواست کارشناس: اگر «گفت‌وگو با کارشناس» روشن است گفت‌وگو باز و به کارشناسان اعلان می‌شود؛ وگرنه شماره و تیکت.</summary>
    private async Task<Outcome> HumanAsync(AssistantRequest request, AssistantVoice voice, StoreSettingDto store, bool handoffEnabled, CancellationToken cancellationToken)
    {
        var contact = string.IsNullOrWhiteSpace(store.SupportPhone) ? "" : $" شمارهٔ پشتیبانی: {store.SupportPhone}.";
        if (!string.IsNullOrWhiteSpace(store.WorkingHours))
            contact += $" ساعت پاسخ‌گویی: {store.WorkingHours}.";
        if (handoffEnabled && handoffs is not null)
        {
            var (_, created) = await handoffs.OpenAsync(request.SessionKey, request.UserId, request.PagePath, cancellationToken);
            var text = (created ? $"حتماً{voice.Comma}. درخواستتان را برای کارشناس فرستادم؛ " : "گفت‌وگوی شما با کارشناس باز است؛ ")
                       + "همین‌جا بنویسید و جواب کارشناس در همین پنجره نشان داده می‌شود." + contact;
            return new Outcome(new AssistantReply(text, [], [], AssistantIntent.Human) { Handoff = true });
        }
        var fallback = $"حتماً{voice.Comma}." + (contact.Length > 0 ? contact : " همکاران پشتیبانی از طریق تیکت در خدمت شما هستند.");
        var actions = request.UserId is not null
            ? new List<AssistantAction> { new("ثبت تیکت پشتیبانی", "/profile/tickets/new"), new("تماس با ما", "/contact-us") }
            : new List<AssistantAction> { new("تماس با ما", "/contact-us"), new("پرسش‌های متداول", "/faq") };
        return Simple(AssistantIntent.Human, fallback, actions);
    }

    // ── دور ۲۴: کارهای تازهٔ دستیار ─────────────────────────────────

    /// <summary>«مشابه این»، «شبیه این»، «جایگزین این». مثال: «مشابه این موجود» ← true.</summary>
    public static bool Similar(AssistantParse parse) =>
        SimilarWords.Any(w => parse.Normalized.Contains(w, StringComparison.Ordinal));

    private static readonly string[] SimilarWords = ["مشابه", "شبیه این", "شبیه اینو", "جایگزین", "مدل های دیگه", "مدلهای دیگه"];

    /// <summary>کالاهای مشابه موجود: همان دسته، بدون خود کالا، پرفروش‌ترها اول.</summary>
    private async Task<List<ProductFilterData>> SimilarProductsAsync(ProductFilterData baseProduct, int take, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = await products.GetProductByFilterAsync(new ProductFilterParams
        {
            CategoryId = baseProduct.CategoryId,
            ExcludeProductId = baseProduct.Id,
            OnlyActive = true,
            OnlyAvailable = true,
            SortBy = ProductSortBy.BestSelling,
            PageId = 1,
            Take = take,
        });
        return result.Data.ToList();
    }

    /// <summary>«مشابه این» روی صفحهٔ کالا یا بعد از نمایش یک کارت.</summary>
    private async Task<Outcome> SimilarAsync(Guid baseId, AssistantVoice voice, CancellationToken cancellationToken)
    {
        var baseProduct = (await products.GetCardsByIdsAsync([baseId])).FirstOrDefault();
        if (baseProduct is null)
            return Simple(AssistantIntent.Search, $"این کالا دیگر در فروشگاه نیست{voice.Comma}؛ نام کالای مورد نظر را بنویسید تا پیدا کنم.");
        var similar = await SimilarProductsAsync(baseProduct, MaxCards, cancellationToken);
        var actions = new List<AssistantAction>();
        if (!string.IsNullOrEmpty(baseProduct.CategorySlug))
            actions.Add(new($"همهٔ «{baseProduct.CategoryName}»", $"/category/{baseProduct.CategorySlug}?Available=true&Sort=BestSelling"));
        if (similar.Count == 0)
            return new Outcome(new AssistantReply($"فعلاً کالای مشابه موجودی برای «{baseProduct.Name}» نداریم{voice.Comma}.", [], actions, AssistantIntent.Search), baseProduct.Id);
        actions.Insert(0, new("مقایسه با بهترین مشابه", Say: "مقایسه این با مشابه"));
        return new Outcome(new AssistantReply($"کالاهای مشابه و موجود «{baseProduct.Name}»:", similar.Select(ToCard).ToList(), actions, AssistantIntent.Search), baseProduct.Id);
    }

    /// <summary>
    /// «برو به سبد»، «صفحه‌اش رو باز کن»، «ببرم به سفارش‌هام»: مسیر شناخته‌شده یا صفحهٔ کالای مورد گفت‌وگو با راهنمای زنده.
    /// </summary>
    private async Task<Outcome> NavigateAsync(AssistantRequest request, AssistantParse parse, List<Guid> last, AssistantVoice voice, decimal freeShippingThreshold, CancellationToken cancellationToken)
    {
        var n = parse.Normalized;
        bool Has(params string[] words) => words.Any(w => n.Contains(w, StringComparison.Ordinal));
        Outcome Go(string url, string text) => new(new AssistantReply(text, [], [new("باز کردن صفحه", url)], AssistantIntent.Navigate) { Steps = [AssistantGuide.Go(url)] });
        Outcome Private(string url, string text) => request.UserId is null
            ? new(new AssistantReply("برای این صفحه اول وارد حساب شوید؛ شما را به صفحهٔ ورود می‌برم.", [], [], AssistantIntent.Navigate) { Steps = AssistantGuide.Login(url, null, withCode: true) })
            : Go(url, text);

        if (Has("تسویه", "پرداخت"))
            return await CheckoutAsync(request, voice, freeShippingThreshold, cancellationToken);
        if (Has("سبد"))
            return Go("/cart", $"سبد خریدتان را باز می‌کنم{voice.Comma}.");
        if (Has("ورود", "لاگین"))
            return Login(request, request.Text, voice);
        if (Has("سفارش"))
            return Private("/profile/orders", "سفارش‌هایتان را باز می‌کنم.");
        if (Has("علاقه"))
            return Private("/profile/favorites", "فهرست علاقه‌مندی‌هایتان را باز می‌کنم.");
        if (Has("کیف پول"))
            return Private("/profile/wallet", "کیف پولتان را باز می‌کنم.");
        if (Has("باشگاه", "امتیاز"))
            return Private("/profile/club", "باشگاه مشتریان را باز می‌کنم.");
        if (Has("تیکت"))
            return Private("/profile/tickets", "تیکت‌هایتان را باز می‌کنم.");
        if (Has("پروفایل", "حساب کاربری", "حسابم"))
            return Private("/profile", "حساب کاربری‌تان را باز می‌کنم.");
        if (Has("تماس"))
            return Go("/contact-us", "صفحهٔ تماس با ما را باز می‌کنم.");
        if (Has("سوالات", "سؤالات", "پرسش"))
            return Go("/faq", "پرسش‌های متداول را باز می‌کنم.");
        if (Has("وبلاگ", "مقاله"))
            return Go("/blog", "وبلاگ را باز می‌کنم.");
        if (Has("مقایسه"))
            return Go("/compare", "صفحهٔ مقایسه را باز می‌کنم.");
        if (Has("صفحه اصلی", "خانه"))
            return Go("/", "به صفحهٔ اصلی می‌رویم.");

        // کالای مورد گفت‌وگو («بازش کن») یا کالای نام‌برده («صفحه هدفون سونی رو باز کن»).
        var (product, options) = await ResolveAsync(request, parse, last, cancellationToken);
        product ??= options.Count == 1 ? options[0] : null;
        if (product is not null)
        {
            var url = $"/product/{product.Slug}";
            return new Outcome(new AssistantReply($"صفحهٔ «{product.Name}» را باز می‌کنم.", [ToCard(product)], [], AssistantIntent.Navigate) { Steps = [AssistantGuide.Go(url)] }, product.Id);
        }
        if (options.Count > 1)
            return Cards(AssistantIntent.Navigate, "کدام را باز کنم؟ روی کارت مورد نظر بزنید.", options, [], parse.Query);
        if (parse.Query.Length >= 2 && await CategoryAsync(parse.Query, cancellationToken) is { } category)
            return Go($"/category/{category.Slug}", $"دستهٔ «{category.Name}» را باز می‌کنم.");
        return Simple(AssistantIntent.Navigate, $"کدام صفحه را باز کنم{voice.Comma}؟ مثلاً «برو به سبد»، «سفارش‌هام» یا نام یک کالا.", Suggestions());
    }

    /// <summary>
    /// کمک برای ورود: به صفحهٔ ورود (با بازگشت به همین صفحه) می‌برد؛ اگر موبایل را در پیام نوشته باشد همان را پر می‌کند.
    /// رمز هرگز در چت گرفته، ذخیره یا پر نمی‌شود و موبایل در گزارش پوشانده است.
    /// </summary>
    private static Outcome Login(AssistantRequest request, string text, AssistantVoice voice)
    {
        if (request.UserId is not null)
            return Simple(AssistantIntent.Login, $"شما وارد حسابتان هستید{voice.Comma}.", [new("حساب کاربری", "/profile"), new("تغییر رمز", "/profile/password")]);
        var phone = AssistantNlu.Phone(request.Text);
        var n = PersianText.Normalize(text);
        var withPassword = (n.Contains("رمز", StringComparison.Ordinal) || n.Contains("پسورد", StringComparison.Ordinal)) && !n.Contains("فراموش", StringComparison.Ordinal);
        var intro = n.Contains("فراموش", StringComparison.Ordinal)
            ? "نگران نباشید؛ با کد پیامکی وارد می‌شوید و بعد از ورود از «حساب کاربری ← تغییر رمز» رمز تازه بگذارید."
            : withPassword
                ? "شما را به ورود با رمز می‌برم؛ رمزتان را خودتان در کادر بنویسید (من هیچ‌وقت رمز نمی‌پرسم)."
                : "شما را به ورود با کد پیامکی می‌برم؛ اگر حساب نداشته باشید همین‌جا ساخته می‌شود.";
        if (phone is not null)
            intro += " شمارهٔ موبایل را هم برایتان در کادر نوشتم.";
        return new Outcome(new AssistantReply(intro, [], [new("صفحهٔ ورود", "/login")], AssistantIntent.Login)
        {
            Steps = AssistantGuide.Login(request.PagePath, phone, withCode: !withPassword),
        });
    }

    /// <summary>
    /// تخفیف‌ها: کالاهای تخفیف‌دار موجود (با عبارت جست‌وجو اگر داد)، کدهای تخفیف عمومی فعال، ارسال رایگان و امتیاز باشگاه.
    /// همه از دادهٔ واقعی؛ کد شخصی یا منقضی هرگز نشان داده نمی‌شود.
    /// </summary>
    private async Task<Outcome> OffersAsync(AssistantRequest request, AssistantParse parse, AssistantVoice voice, decimal freeShippingThreshold, CancellationToken cancellationToken)
    {
        var query = parse.Query.Length >= 2 ? parse.Query : null;
        var filter = new ProductFilterParams { Search = query, OnlyActive = true, OnlyAvailable = true, OnlyDiscounted = true, SortBy = ProductSortBy.MostDiscounted, PageId = 1, Take = MaxCards };
        if (query is not null)
            filter.SearchTermGroups = await insights.ExpandAsync(query);
        var deals = (await products.GetProductByFilterAsync(filter)).Data.ToList();

        var now = DateTimeOffset.UtcNow;
        var coupons = await context.Coupons.AsNoTracking()
            .Where(c => c.IsActive && c.IsPublic && c.UserId == null
                        && (c.StartsAt == null || c.StartsAt <= now) && (c.EndsAt == null || c.EndsAt > now)
                        && (c.UsageLimit == null || c.UsedCount < c.UsageLimit))
            .OrderByDescending(c => c.CreatedAt)
            .Select(c => new { c.Code, c.Type, c.Value, c.MaxDiscountAmount, c.MinOrderAmount, c.FirstOrderOnly, c.EndsAt })
            .Take(3)
            .ToListAsync(cancellationToken);

        var lines = new List<string>();
        if (deals.Count > 0)
            lines.Add(query is null ? "پرتخفیف‌ترین کالاهای موجود:" : $"تخفیف‌های «{query}»:");
        else if (query is not null)
            lines.Add($"الان برای «{query}» تخفیف فعالی نداریم.");
        foreach (var c in coupons)
        {
            var amount = c.Type switch
            {
                CouponType.Percentage => $"{PersianText.Digits((long)c.Value)}٪ تخفیف" + (c.MaxDiscountAmount is > 0 ? $" تا سقف {PersianText.Money(c.MaxDiscountAmount.Value)} تومان" : ""),
                CouponType.FixedAmount => $"{PersianText.Money(c.Value)} تومان تخفیف",
                CouponType.FreeShipping => "ارسال رایگان",
                _ => "تخفیف",
            };
            var rules = new List<string>();
            if (c.MinOrderAmount > 0)
                rules.Add($"برای خرید بالای {PersianText.Money(c.MinOrderAmount)} تومان");
            if (c.FirstOrderOnly)
                rules.Add("فقط اولین خرید");
            if (c.EndsAt is { } end)
                rules.Add($"تا {SeoDate(end)}");
            lines.Add($"🎟 کد «{c.Code}»: {amount}{(rules.Count > 0 ? " (" + string.Join("، ", rules) + ")" : "")}");
        }
        if (freeShippingThreshold > 0)
        {
            var gap = "";
            if (await request.Cart(false) is { } cartId && await carts.GetCartByIdAsync(cartId) is { Items.Count: > 0 } cart)
                gap = cart.PayableAmount >= freeShippingThreshold ? " — سبد شما همین الان ارسال رایگان است ✓" : $" — {PersianText.Money(freeShippingThreshold - cart.PayableAmount)} تومان دیگر تا ارسال رایگان";
            lines.Add($"🚚 ارسال رایگان برای خرید بالای {PersianText.Money(freeShippingThreshold)} تومان{gap}");
        }
        var actions = new List<AssistantAction> { new("همهٔ کالاهای تخفیف‌دار", "/search?Discounted=true&Available=true&Sort=MostDiscounted") };
        if (request.UserId is { } userId)
        {
            var points = await context.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.LoyaltyPoints).FirstOrDefaultAsync(cancellationToken);
            var rule = await context.LoyaltyRules.AsNoTracking()
                .Where(r => r.IsActive && r.Action == LoyaltyAction.Purchase && r.AmountStep > 0 && r.PointsPerStep > 0)
                .Select(r => new { r.AmountStep, r.PointsPerStep })
                .FirstOrDefaultAsync(cancellationToken);
            lines.Add($"⭐ امتیاز باشگاه شما: {PersianText.Digits(points)}" + (rule is null ? "" : $" (هر {PersianText.Money(rule.AmountStep)} تومان خرید = {PersianText.Digits(rule.PointsPerStep)} امتیاز)"));
            actions.Add(new("باشگاه مشتریان", "/profile/club"));
        }
        else
            actions.Add(new("باشگاه مشتریان", "/club"));
        if (lines.Count == 0)
            lines.Add($"الان تخفیف فعالی نداریم{voice.Comma}؛ به‌محض شروع جشنواره در صفحهٔ اصلی و همین‌جا اعلام می‌شود.");
        return new Outcome(new AssistantReply(string.Join("\n", lines), deals.Select(ToCard).ToList(), actions, AssistantIntent.Offers), Query: query);
    }

    /// <summary>تاریخ کوتاه شمسی برای متن. مثال: «۱۴۰۵/۰۸/۰۱».</summary>
    private static string SeoDate(DateTimeOffset value)
    {
        var local = IranClock.ToLocal(value).DateTime;
        var calendar = new System.Globalization.PersianCalendar();
        return PersianText.Digits(calendar.GetYear(local)) + "/" + PersianText.Digits(calendar.GetMonth(local)).PadLeft(2, '۰') + "/" + PersianText.Digits(calendar.GetDayOfMonth(local)).PadLeft(2, '۰');
    }

    private sealed record FinderCategory(Guid Id, string Name, string Slug, int Count);

    /// <summary>
    /// کالایاب مرحله‌ای: دسته ← بودجه (از قیمت واقعی کالاهای موجود) ← اولویت ← ۴ پیشنهاد + «دیدن همه».
    /// وضعیت فقط در دکمه‌هاست (Flow)؛ هر مرحله از دادهٔ زنده و حافظهٔ موقت ۱۰ دقیقه‌ای ساخته می‌شود.
    /// </summary>
    private async Task<Outcome> FinderAsync(FinderState state, AssistantParse parse, AssistantVoice voice, CancellationToken cancellationToken)
    {
        var top = await cache.GetOrCreateAsync("assistant:finder:categories", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10);
            entry.Size = 1;
            var counts = await context.Products.AsNoTracking()
                .Where(p => p.IsActive && p.StockQuantity > 0 && p.Category.IsActive && !p.Category.IsSystem)
                .GroupBy(p => new { p.CategoryId, p.Category.Name, p.Category.Slug })
                .Select(g => new { g.Key.CategoryId, g.Key.Name, g.Key.Slug, Count = g.Count() })
                .OrderByDescending(g => g.Count)
                .Take(60)
                .ToListAsync(cancellationToken);
            return counts.Select(c => new FinderCategory(c.CategoryId, c.Name, c.Slug, c.Count)).ToList();
        }) ?? new List<FinderCategory>();

        var slug = state.CategorySlug;
        if (slug is null && parse.Query.Length >= 2 && await CategoryAsync(parse.Query, cancellationToken) is { } named)
            slug = named.Slug;
        var category = slug is null ? null : top.FirstOrDefault(c => c.Slug == slug);
        if (category is null && slug is not null)
        {
            // دستهٔ والد (کالاهایش در زیردسته‌ها) یا دسته‌ای خارج از ۶۰ دستهٔ پرکالا.
            var row = await context.Categories.AsNoTracking().Where(c => c.Slug == slug && c.IsActive).Select(c => new { c.Id, c.Name, c.Slug }).FirstOrDefaultAsync(cancellationToken);
            category = row is null ? null : new FinderCategory(row.Id, row.Name, row.Slug, 0);
        }
        if (category is null)
        {
            if (top.Count == 0)
                return Simple(AssistantIntent.Finder, "الان کالای موجودی برای پیشنهاد نداریم؛ لطفاً کمی بعد دوباره سر بزنید.");
            var chips = top.Take(6).Select(c => new AssistantAction(c.Name, Say: c.Name, Flow: AssistantFinder.Build(c.Slug))).ToList();
            return new Outcome(new AssistantReply(
                (voice.IsCasual ? "بزن بریم! " : "با کمال میل. ") + "دنبال چه نوع کالایی هستید؟ یکی را انتخاب کنید یا نامش را بنویسید (مثلاً «کمکم کن هدفون انتخاب کنم»).",
                [], chips, AssistantIntent.Finder));
        }

        var bands = await cache.GetOrCreateAsync("assistant:finder:bands:" + category.Slug, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10);
            entry.Size = 1;
            var now = DateTimeOffset.UtcNow;
            var prices = await context.Products.AsNoTracking()
                .Where(p => p.IsActive && p.StockQuantity > 0 && (p.CategoryId == category.Id || p.Category.ParentId == category.Id))
                .Select(p => p.DiscountPrice != null && p.DiscountPrice > 0 && p.DiscountPrice < p.Price
                             && (p.DiscountStartAt == null || p.DiscountStartAt <= now) && (p.DiscountEndAt == null || p.DiscountEndAt > now)
                    ? p.DiscountPrice.Value : p.Price)
                .Take(1000)
                .ToListAsync(cancellationToken);
            return AssistantFinder.Bands(prices).ToList();
        }) ?? new List<PriceBand>();

        if (state.Band is null && bands.Count > 0)
        {
            var chips = bands.Select((b, i) => new AssistantAction(b.Label, Say: b.Label, Flow: AssistantFinder.Build(category.Slug, i + 1))).ToList();
            chips.Add(new("فرقی نمی‌کند", Say: "فرقی نمی‌کند", Flow: AssistantFinder.Build(category.Slug, 0)));
            return new Outcome(new AssistantReply($"بودجه‌تان برای «{category.Name}» حدوداً چقدر است؟", [], chips, AssistantIntent.Finder));
        }
        var bandIndex = state.Band ?? 0;
        var band = bandIndex >= 1 && bandIndex <= bands.Count ? bands[bandIndex - 1] : null;

        if (state.Priority is null || !AssistantFinder.Priorities.ContainsKey(state.Priority))
        {
            var chips = AssistantFinder.Priorities
                .Select(p => new AssistantAction(p.Value.Label, Say: p.Value.Label, Flow: AssistantFinder.Build(category.Slug, bandIndex, p.Key)))
                .ToList();
            return new Outcome(new AssistantReply("برایتان کدام مهم‌تر است؟", [], chips, AssistantIntent.Finder));
        }

        var sort = AssistantFinder.Priorities[state.Priority].Sort;
        var filter = new ProductFilterParams
        {
            CategorySlug = category.Slug,
            OnlyActive = true,
            OnlyAvailable = true,
            OnlyDiscounted = state.Priority == "deal" ? true : null,
            MinPrice = band is { Min: > 0 } ? band.Min : null,
            MaxPrice = band?.Max,
            SortBy = sort,
            PageId = 1,
            Take = MaxCards,
        };
        var found = (await products.GetProductByFilterAsync(filter)).Data.ToList();
        var relaxed = false;
        if (found.Count == 0 && (filter.OnlyDiscounted is true || band is not null))
        {
            filter.OnlyDiscounted = null;
            filter.MinPrice = null;
            filter.MaxPrice = null;
            found = (await products.GetProductByFilterAsync(filter)).Data.ToList();
            relaxed = true;
        }
        var query = new List<string> { "Available=true", "Sort=" + sort };
        if (!relaxed && filter.MinPrice is { } min)
            query.Add("MinPrice=" + min.ToString("0", System.Globalization.CultureInfo.InvariantCulture));
        if (!relaxed && filter.MaxPrice is { } max)
            query.Add("MaxPrice=" + max.ToString("0", System.Globalization.CultureInfo.InvariantCulture));
        if (!relaxed && filter.OnlyDiscounted is true)
            query.Add("Discounted=true");
        var actions = new List<AssistantAction>
        {
            new("دیدن همه", $"/category/{category.Slug}?{string.Join("&", query)}"),
            new("از اول", Say: "از اول", Flow: AssistantFinder.Build(null)),
        };
        if (found.Count == 0)
            return new Outcome(new AssistantReply($"فعلاً در «{category.Name}» کالای موجودی نداریم{voice.Comma}.", [], actions, AssistantIntent.Finder));
        var intro = relaxed
            ? $"با این بودجه/شرط کالای موجودی نبود؛ نزدیک‌ترین گزینه‌های «{category.Name}»:"
            : $"پیشنهاد من در «{category.Name}»{(band is null ? "" : " (" + band.Label + ")")} – {AssistantFinder.Priorities[state.Priority].Label}:";
        return new Outcome(new AssistantReply(intro, found.Select(ToCard).ToList(), actions, AssistantIntent.Finder));
    }

    /// <inheritdoc />
    public async Task<AssistantSuggestion> SuggestAsync(string? pagePath, Guid? productId, Guid? userId, AssistantStyle? style, CancellationToken cancellationToken = default)
    {
        var options = smart is null ? new SmartOptions().Normalize() : await smart.GetAsync(cancellationToken);
        var store = await settings.GetAsync(cancellationToken);
        var request = new AssistantRequest("", "suggest", userId, productId, [], null, _ => Task.FromResult<Guid?>(null)) { Style = style };
        var (voice, _) = await VoiceAsync(request, "", cancellationToken);
        var kind = AssistantPages.Kind(pagePath);
        string? productName = null;
        var inStock = true;
        if (kind == AssistantPageKind.Product && productId is { } id)
        {
            var card = (await products.GetCardsByIdsAsync([id])).FirstOrDefault();
            productName = card?.Name;
            inStock = card?.IsInStock ?? true;
        }
        var chips = AssistantPages.Chips(kind, productName, inStock, 0, AssistantPages.CategorySlug(pagePath));
        var intro = voice.Hello(options.AssistantName, store.SiteName, IranClock.ToLocal(DateTimeOffset.UtcNow).Hour) + " " + voice.HowCanIHelp;
        if (productName is not null)
            intro += $"\nدربارهٔ «{productName}» هر سؤالی دارید بپرسید.";
        return new AssistantSuggestion(options.AssistantName, intro, chips, options.AssistantGreeting, options.AssistantHandoff && handoffs is not null);
    }

    /// <summary>برچسب پیام‌هایی که مسیرشان را مدل یادگیرنده (نه قانون‌ها) تعیین کرده؛ در آموزش بعدی کنار گذاشته می‌شوند تا خطا خودش را تقویت نکند.</summary>
    public const string LearnedTopic = "مدل یادگیرنده";

    /// <inheritdoc />
    public async Task<int> RetrainIntentModelAsync(CancellationToken cancellationToken = default)
    {
        // فقط پیام‌هایی که قانون‌ها مسیرشان را مشخص کرده‌اند و جواب گرفته‌اند (برچسب مطمئن)؛ سقف ۲۰٬۰۰۰ برای حافظه.
        var since = DateTimeOffset.UtcNow.AddDays(-90);
        var routable = AssistantIntentModel.Routable.ToList();
        var rows = await context.AssistantMessages.AsNoTracking()
            .Where(m => m.CreatedAt >= since && m.Answered && routable.Contains(m.Intent) && m.Topic != LearnedTopic)
            .OrderByDescending(m => m.CreatedAt)
            .Select(m => new { m.Text, m.Intent })
            .Take(20_000)
            .ToListAsync(cancellationToken);
        var examples = rows.Select(r => (r.Text, r.Intent)).ToList();
        // دور ۲۵: آموزش‌های کارکنان که «کار» دارند (مثلاً «بسته‌ام نیومده» ← پیگیری) هم نمونهٔ مطمئن هستند.
        if (teachings is not null)
            examples.AddRange(await teachings.ExamplesAsync(cancellationToken));
        var count = AssistantIntentModel.Retrain(examples);
        logger.LogInformation("Assistant intent model retrained with {Count} real messages", count);
        return count;
    }

    // ── جست‌وجو و کالای درخواستی ─────────────────────────────────

    private async Task<Outcome> SearchAsync(string text, AssistantParse parse, AssistantRequest request, AssistantVoice voice, CancellationToken cancellationToken)
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
        {
            // دور ۲۴: «باشه»، «در موردش بگو»… وقتی کالایی در گفت‌وگو هست، دربارهٔ همان کالا پیشنهاد می‌دهیم (نه «کالای درخواستی»).
            if (await ContextProductAsync(request, cancellationToken) is { } here)
                return Clarify(here, voice);
            return Simple(AssistantIntent.Help, $"متوجه نشدم{voice.Comma}. نام کالا یا سؤالتان را کمی کامل‌تر بنویسید؛ مثلاً «پاوربانک ۲۰ هزار».", Suggestions());
        }

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
            actions.Add(new("نتایج بیشتر", "/search?q=" + Uri.EscapeDataString(query)));
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

        // دور ۲۴: دانش عمومی سایت (برند، دسته، مقاله، صفحه‌های قوانین و راهنما).
        if (knowledge is not null && (await knowledge.SearchAsync(text, 3, 0.5, cancellationToken)) is { Count: > 0 } hits)
        {
            var top = hits[0];
            var answer = top.Entry.Kind switch
            {
                KnowledgeKind.Brand => $"کالاهای برند «{top.Entry.Title}» را در این صفحه ببینید.",
                KnowledgeKind.Category => $"دستهٔ «{top.Entry.Title}» را ببینید.",
                _ => top.Snippet.Length > 0 ? $"«{top.Entry.Title}»: {top.Snippet}" : $"این صفحه به سؤالتان کمک می‌کند: «{top.Entry.Title}».",
            };
            var links = hits.Select(h => new AssistantAction(h.Entry.Title.Length > 50 ? h.Entry.Title[..50] + "…" : h.Entry.Title, h.Entry.Url)).ToList();
            return new Outcome(new AssistantReply(answer, [], links, AssistantIntent.Help), Answered: true, Topic: "دانش سایت", Query: query);
        }

        // دور ۲۴: پیام کوتاه بدون نشانهٔ خرید وقتی کالایی در گفت‌وگو هست ← احتمالاً دربارهٔ همان کالاست؛ می‌پرسیم (نه «موجود نداریم»).
        if (!parse.ShopCue && AssistantNlu.Words(query).Count <= 2 && await ContextProductAsync(request, cancellationToken) is { } current)
            return Clarify(current, voice) with { Query = query };

        // دور ۲۸: هم‌معنی‌های جدول دانش («لبتاپ» ← «لپ تاپ»، «هنذفری» ← «هدفون») پیش از «موجود نداریم».
        if (facts is not null && AssistantBrain.Canonical(query, await facts.FactsAsync(cancellationToken)) is { } canonical)
        {
            var again = await FindAsync(canonical, ProductSortBy.BestSelling, cancellationToken);
            if (again.Count > 0)
                return Cards(AssistantIntent.Search, $"منظورتان «{canonical}» بود؟ این‌ها را پیدا کردم:", again, actions, canonical);
        }

        if (parse.ShopCue || AssistantNlu.Words(query).Count is >= 1 and <= 4)
        {
            // کالای درخواستی که نداریم: هم در گزارش جست‌وجوهای بی‌نتیجه (خلاصهٔ روزانه) و هم در پنل دستیار.
            await insights.LogAsync(query, 0, "assistant:" + request.SessionKey);
            var who = voice.Address is null ? "" : voice.Address + "، ";
            return new Outcome(new AssistantReply($"{NotCarried} {who}به‌محض تأمین، این کالا به فروشگاه اضافه می‌شود.", [],
                [new("کالاهای پرفروش", Say: "کالاهای پرفروش"), new("کمکم کن انتخاب کنم", Say: "کمکم کن انتخاب کنم", Flow: AssistantFinder.Build(null))], AssistantIntent.Demand), Answered: true, Query: query);
        }

        return new Outcome(new AssistantReply($"متوجه نشدم{voice.Comma}. می‌توانم در پیدا کردن کالا، قیمت و موجودی، سبد خرید و پیگیری سفارش کمک کنم.", [], Suggestions(), AssistantIntent.Unknown),
            Answered: false, Query: query);
    }

    /// <summary>
    /// دور ۲۸: جواب از جدول دانش. راهنمای خرید ← نکته‌های شماره‌دار + «نمایش کالاها» و «کمکم کن انتخاب کنم»؛ واژه‌نامه ← توضیح کوتاه؛
    /// گپ کوتاه ← یکی از چند جواب (چرخشی، تا تکراری و رباتی نباشد). موضوع پیام «دانش:شناسه» است تا 👍/👎 مشتری در یادگیری شبانه بنشیند.
    /// </summary>
    private async Task<Outcome> BrainAsync(BrainHit hit, AssistantIntent intent, AssistantVoice voice, CancellationToken cancellationToken)
    {
        var fact = hit.Fact;
        await facts!.RecordShownAsync(fact.Id, cancellationToken);
        var topic = AssistantBrain.TopicPrefix + (fact.Id?.ToString() ?? "بذر");
        var links = new List<AssistantAction>();
        if (fact.Url is { Length: > 1 } url && url.StartsWith('/') && !url.StartsWith("//", StringComparison.Ordinal))
            links.Add(new("بیشتر بخوانید", url));
        string reply;
        switch (fact.Kind)
        {
            case AssistantFactKind.Guide:
            {
                var tips = AssistantBrain.Tips(fact.Answer);
                var lines = tips.Select((t, i) => $"{DesignTipNumber(i + 1)}. {t}");
                reply = $"چند نکتهٔ کاربردی برای خرید {fact.Term}{voice.Comma}:\n" + string.Join("\n", lines);
                links.Add(new($"نمایش {fact.Term}", Say: fact.Term));
                links.Add(new("کمکم کن انتخاب کنم", Say: "کمکم کن انتخاب کنم", Flow: AssistantFinder.Build(null)));
                break;
            }
            case AssistantFactKind.SmallTalk:
                reply = AssistantBrain.Pick(fact.Answer, Environment.TickCount / 1000);
                if (links.Count == 0)
                    links.AddRange(Suggestions().Take(3));
                break;
            case AssistantFactKind.Glossary:
                reply = $"«{fact.Term}»: {fact.Answer}";
                break;
            default:
                reply = fact.Answer;
                break;
        }
        // پیام بی‌ربط (مثل «جوک بگو») همان «بی‌ربط» گزارش می‌شود؛ بقیه «راهنما».
        var shown = intent == AssistantIntent.OffTopic ? AssistantIntent.OffTopic : AssistantIntent.Help;
        return new Outcome(new AssistantReply(reply, [], links, shown), Answered: true, Topic: topic);
    }

    /// <summary>شمارهٔ فارسی نکته. مثال: ۳ ← «۳».</summary>
    private static string DesignTipNumber(int number) =>
        new(number.ToString(System.Globalization.CultureInfo.InvariantCulture).Select(c => (char)('۰' + (c - '0'))).ToArray());

    /// <summary>کالای در حال گفت‌وگو: کالای همین صفحه یا اولین کارتی که دستیار نشان داده (یا null).</summary>
    private async Task<ProductFilterData?> ContextProductAsync(AssistantRequest request, CancellationToken cancellationToken)
    {
        var value = request.CurrentProductId ?? request.LastProductIds.FirstOrDefault();
        if (value == Guid.Empty)
            return null;
        cancellationToken.ThrowIfCancellationRequested();
        return (await products.GetCardsByIdsAsync([value])).FirstOrDefault();
    }

    /// <summary>«منظورتان دربارهٔ «X» بود؟» + دکمه‌های معرفی، مشخصات، قیمت و افزودن.</summary>
    private static Outcome Clarify(ProductFilterData product, AssistantVoice voice) =>
        new(new AssistantReply($"دربارهٔ «{product.Name}» می‌پرسید{voice.Comma}؟ یکی از این‌ها را انتخاب کنید یا سؤالتان را کامل‌تر بنویسید:", [ToCard(product)],
            [
                new("معرفی کوتاه", Say: "در مورد این کالا توضیح بده"),
                new("مشخصات", Say: "مشخصات این چیه؟"),
                new("قیمت و موجودی", Say: "قیمت این چنده؟"),
                new("کالای مشابه", Say: "مشابه این"),
            ], AssistantIntent.ProductQuestion), product.Id, true, "روشن‌سازی");

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
    private async Task<Outcome> QuestionAsync(AssistantRequest request, AssistantParse parse, List<Guid> last, AssistantVoice voice, StoreSettingDto store, CancellationToken cancellationToken)
    {
        var topic = AssistantNlu.Topic(parse.Topic)!;
        if (topic.StoreLevel)
            return await StoreAnswerAsync(topic, request.Text, store, voice, cancellationToken);

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
                return await SearchAsync(request.Text, parse, request, voice, cancellationToken);
            // دور ۲۴: «داریم؟» / «قیمتش؟» بعد از یک جست‌وجو ← همان جست‌وجوی قبلی دوباره (حافظهٔ گفت‌وگو).
            if (request.LastQuery is { Length: >= 2 } lastQuery && topic.Key is "price" or "stock")
            {
                var again = await FindAsync(lastQuery, ProductSortBy.BestSelling, cancellationToken);
                if (again.Count > 0)
                    return Cards(AssistantIntent.Search, $"برای «{lastQuery}» این‌ها را داریم؛ قیمت و موجودی روی هر کارت هست:", again, [new("نتایج بیشتر", "/search?q=" + Uri.EscapeDataString(lastQuery))], lastQuery) with { Topic = topic.Title };
                return await SearchAsync(lastQuery, parse with { Query = lastQuery, Intent = AssistantIntent.Search }, request, voice, cancellationToken);
            }
            return new Outcome(new AssistantReply($"دربارهٔ کدام کالا می‌پرسید{voice.Comma}؟ نام کالا را بنویسید یا از صفحهٔ همان کالا بپرسید.", [], [], AssistantIntent.ProductQuestion), Topic: topic.Title);
        }

        var card = ToCard(product);
        // دور ۲۵: وقتی جواب در دادهٔ ما نیست، مشتری می‌تواند همین سؤال را در بخش «پرسش و پاسخ» کالا از فروشنده بپرسد.
        var askSeller = new AssistantAction("پرسیدن از فروشنده", $"/product/{product.Slug}#quest");
        AssistantReply Reply(string text, AssistantLookupNeed? lookup = null, bool ask = false) =>
            new(text, [card], ask ? new List<AssistantAction> { askSeller } : new List<AssistantAction>(), AssistantIntent.ProductQuestion, lookup);
        Outcome Done(string text) => new(Reply(text), product.Id, true, topic.Title, parse.Query);
        Outcome Gap(string text, AssistantLookupNeed? lookup = null) => new(Reply(text, lookup, ask: true), product.Id, false, topic.Title, parse.Query);

        switch (topic.Key)
        {
            case "price":
                return Done(PriceText(product) + " " + StockText(product));
            case "stock":
                return Done(StockText(product));
        }

        // دور ۲۵: «کیفیتش چطوره؟»، «نظر خریدارا چیه؟» ← خلاصهٔ واقعی نظر خریداران (نه جست‌وجوی اینترنت).
        if (topic.Key == "reviews")
        {
            var summary = AssistantReviews.Summarize(await ReviewsOfAsync(product.Id, cancellationToken));
            if (AssistantReviews.Text(summary, product.Name, summary.Aspects.Any(a => a.Key == "quality") && parse.Normalized.Contains("کیفیت", StringComparison.Ordinal) ? "quality" : null) is { } reviewText)
                return Done(reviewText) with { Reply = Reply(reviewText) with { Actions = [new("همهٔ نظرها", $"/product/{product.Slug}#comments")] } };
            if (await AnsweredQuestionAsync(product.Id, request.Text, cancellationToken) is { } earlier)
                return Done($"این سؤال قبلاً پرسیده شده: «{earlier.Question}»\nپاسخ فروشنده: {earlier.Answer}");
            return new Outcome(Reply($"هنوز نظری برای «{product.Name}» ثبت نشده است{voice.Comma}. {(product.SalesCount > 0 ? "این کالا فروش داشته ولی خریداران هنوز نظر نداده‌اند. " : "")}می‌توانید سؤالتان را از فروشنده بپرسید.", ask: true),
                product.Id, true, topic.Title, parse.Query);
        }

        var facts = await context.Products.AsNoTracking()
            .Where(p => p.Id == product.Id)
            .Select(p => new
            {
                p.WarrantyTitle,
                p.WarrantyMonths,
                p.ShortDescription,
                p.Description,
                Features = p.Features.OrderBy(f => f.DisplayOrder).Select(f => new { f.Key, f.Value }).Take(60).ToList(),
            })
            .FirstOrDefaultAsync(cancellationToken);
        var features = facts?.Features.Select(f => (Key: f.Key.Trim(), Value: f.Value.Trim())).Where(f => f.Key.Length > 0 && f.Value.Length > 0).ToList() ?? [];

        // دور ۲۴: معرفی کوتاه کالا (قیمت، موجودی، چند ویژگی کلیدی، خلاصهٔ توضیحات، گارانتی).
        if (topic.Key == "about")
            return Done(AboutText(product, features, facts?.ShortDescription, facts?.Description, facts?.WarrantyTitle));

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

        // دور ۲۵: جواب از «پرسش و پاسخ» تأییدشدهٔ همین کالا (فروشنده قبلاً به سؤال مشابه جواب داده).
        if (await AnsweredQuestionAsync(product.Id, request.Text, cancellationToken) is { } answered)
            return Done($"این سؤال قبلاً پرسیده شده: «{answered.Question}»\nپاسخ فروشنده: {answered.Answer}");

        // دور ۲۵: صدا/نمایشگر/دوربین/باتری… که در مشخصات نیست ← اگر خریداران دربارهٔ همین جنبه نظر داده‌اند، خلاصهٔ همان.
        if (AssistantReviews.Aspects.Any(a => a.Key == topic.Key))
        {
            var summary = AssistantReviews.Summarize(await ReviewsOfAsync(product.Id, cancellationToken));
            if (summary.Aspects.Any(a => a.Key == topic.Key) && AssistantReviews.Text(summary, product.Name, topic.Key) is { } aspectText)
                return Done($"«{topic.Title}» در مشخصات ثبت نشده، ولی خریداران نوشته‌اند:\n" + aspectText);
        }

        // گارانتی و اصالت از اینترنت پرسیده نمی‌شود (به فروشنده بستگی دارد)؛ مشخصات فنی را در پس‌زمینه جست‌وجو می‌کنیم.
        if (topic.Key is "warranty" or "original")
            return Gap($"{topic.Title} «{product.Name}» در اطلاعات ما ثبت نشده است؛ پرسش شما برای همکاران ثبت شد. برای اطمینان می‌توانید با پشتیبانی تماس بگیرید.");
        return Gap($"«{topic.Title}» در مشخصات ثبت‌شدهٔ «{product.Name}» نیست. لطفاً چند لحظه صبر کنید تا در منابع اینترنتی جست‌وجو کنم…",
            new AssistantLookupNeed(product.Id, product.Name, topic.Title, topic.Words.Concat(topic.FeatureKeys).Distinct().ToList()));
    }

    /// <summary>
    /// معرفی کوتاه یک کالا از دادهٔ واقعی. مثال:
    /// «هدفون X از برند سونی (هدفون بی‌سیم)\nقیمت…\nموجود است.\n• اتصال: بلوتوث ۵٫۳\n• …\nخلاصه: …\nگارانتی: …»
    /// </summary>
    public static string AboutText(ProductFilterData product, IReadOnlyList<(string Key, string Value)> features, string? shortDescription, string? description, string? warranty)
    {
        var head = $"«{product.Name}»" + (string.IsNullOrWhiteSpace(product.BrandName) ? "" : $" از برند {product.BrandName}")
                   + (string.IsNullOrWhiteSpace(product.CategoryName) ? "" : $" ({product.CategoryName})");
        var lines = new List<string> { head, PriceText(product), StockText(product) };
        if (product.ReviewCount > 0)
            lines.Add($"امتیاز خریداران: {PersianText.Decimal1(product.AverageRating)} از ۵ ({PersianText.Digits(product.ReviewCount)} نظر)");
        lines.AddRange(features.Take(4).Select(f => $"• {f.Key}: {f.Value}"));
        var summary = KnowledgeIndex.Plain(string.IsNullOrWhiteSpace(shortDescription) ? description : shortDescription);
        if (summary.Length > 0)
            lines.Add("خلاصه: " + PersianText.Truncate(summary, 260));
        if (!string.IsNullOrWhiteSpace(warranty))
            lines.Add("گارانتی: " + warranty.Trim());
        return string.Join("\n", lines);
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
    private async Task<Outcome> StoreAnswerAsync(AssistantTopic topic, string text, StoreSettingDto store, AssistantVoice voice, CancellationToken cancellationToken)
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

    private async Task<Outcome> CartShowAsync(AssistantRequest request, AssistantVoice voice)
    {
        var cartId = await request.Cart(false);
        var cart = cartId is { } id ? await carts.GetCartByIdAsync(id) : null;
        if (cart is null || cart.Items.Count == 0)
            return Simple(AssistantIntent.CartShow, $"سبد خرید شما خالی است{voice.Comma}. نام کالایی را که می‌خواهید بنویسید تا پیدا کنم.", Suggestions());
        var lines = cart.Items.Take(8).Select(i =>
            $"• {i.ProductName ?? "کالای حذف‌شده"} × {PersianText.Digits(i.Quantity)} — {PersianText.Money(i.TotalAmount)} تومان{(i.HasEnoughStock ? "" : " (موجودی کافی نیست)")}");
        var more = cart.Items.Count > 8 ? $"\n… و {PersianText.Digits(cart.Items.Count - 8)} کالای دیگر" : "";
        var text = $"سبد خرید شما{voice.Comma}:\n{string.Join("\n", lines)}{more}\nجمع (بدون هزینهٔ ارسال): {PersianText.Money(cart.PayableAmount)} تومان";
        return Simple(AssistantIntent.CartShow, text, [new("مشاهدهٔ سبد", "/cart"), new("تسویه حساب", Say: "تسویه حساب")]);
    }

    private async Task<Outcome> CartAddAsync(AssistantRequest request, AssistantParse parse, List<Guid> last, AssistantVoice voice, CancellationToken cancellationToken)
    {
        var (product, options) = await ResolveAsync(request, parse, last, cancellationToken);
        if (product is null)
        {
            if (options.Count > 1)
                return Cards(AssistantIntent.CartAdd, "کدام را اضافه کنم؟ روی «افزودن» کارت مورد نظر بزنید.", options, [], parse.Query);
            if (parse.Query.Length >= 2)
                return await SearchAsync(request.Text, parse, request, voice, cancellationToken);
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
        return new Outcome(new AssistantReply($"«{product.Name}» ({PersianText.Digits(parse.Quantity)} عدد) به سبد خرید اضافه شد{voice.Comma}.{total}", [],
            [new("مشاهدهٔ سبد", "/cart"), new("تسویه حساب", Say: "تسویه حساب")], AssistantIntent.CartAdd, CartChanged: true)
        {
            // دور ۲۴: راهنمای زنده ← دکمهٔ افزودن روشن می‌شود و مشتری را تا سبد و دکمهٔ ادامهٔ خرید همراهی می‌کنیم.
            Steps = AssistantGuide.AddedToCart($"/product/{product.Slug}", request.CurrentProductId == product.Id),
        }, product.Id);
    }

    private async Task<Outcome> CartRemoveAsync(AssistantRequest request, AssistantParse parse, AssistantVoice voice)
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
        return new Outcome(new AssistantReply($"«{item.ProductName}» از سبد خرید حذف شد{voice.Comma}.", [], [new("سبد خرید من", Say: "سبد خرید من")],
            AssistantIntent.CartRemove, CartChanged: true), item.ProductId);
    }

    /// <summary>
    /// راهنمای تسویه: خلاصهٔ سبد + انتخاب آدرس ذخیره‌شده (پیش‌فرض اول) ← لینک صفحهٔ پرداخت با همان آدرس. پرداخت را خود مشتری می‌زند.
    /// </summary>
    private async Task<Outcome> CheckoutAsync(AssistantRequest request, AssistantVoice voice, decimal freeShippingThreshold, CancellationToken cancellationToken)
    {
        if (request.UserId is not { } userId)
            return new Outcome(new AssistantReply("برای ثبت سفارش اول وارد حساب شوید؛ شما را به صفحهٔ ورود با کد پیامکی می‌برم و بعد از ورود به تسویه حساب برمی‌گردید. سبد خرید شما حفظ می‌شود.", [],
                [new("ورود و ادامهٔ خرید", "/login?returnUrl=%2Fcheckout"), new("مشاهدهٔ سبد", "/cart")], AssistantIntent.Checkout)
            {
                Steps = AssistantGuide.Login("/checkout", null, withCode: true),
            });

        var cartId = await request.Cart(false);
        var cart = cartId is { } id ? await carts.GetCartByIdAsync(id) : null;
        if (cart is null || cart.Items.Count == 0)
            return Simple(AssistantIntent.Checkout, $"سبد خرید شما خالی است{voice.Comma}. اول کالایی اضافه کنید.", Suggestions());

        var text = $"خلاصهٔ سفارش{voice.Comma}: {PersianText.Digits(cart.TotalQuantity)} کالا، جمع {PersianText.Money(cart.PayableAmount)} تومان (هزینهٔ ارسال در مرحلهٔ بعد بر اساس آدرس حساب می‌شود).";
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
        // موجودی ناکافی ← اول سبد اصلاح شود (به صفحهٔ سبد می‌بریم، نه پرداخت).
        if (short_.Count > 0)
            return new Outcome(new AssistantReply(text, [], [new("ویرایش سبد", "/cart")], AssistantIntent.Checkout) { Steps = [AssistantGuide.Go("/cart"), AssistantGuide.Point("cart-summary", "این کالاها را اصلاح کنید")] });
        if (addresses.Count == 0)
            return new Outcome(new AssistantReply(text + "\nهنوز آدرسی ثبت نکرده‌اید؛ در صفحهٔ تسویه حساب آدرس را وارد کنید و پرداخت را خودتان بزنید.", [],
                [new("ادامه به تسویه حساب", "/checkout"), new("ویرایش سبد", "/cart")], AssistantIntent.Checkout) { Steps = [AssistantGuide.Go("/checkout"), AssistantGuide.Point("address", "آدرس تحویل را اینجا وارد کنید")] });

        // دور ۲۴: آدرس پیش‌فرض انتخاب و مشتری تا دکمهٔ پرداخت همراهی می‌شود؛ پرداخت را همیشه خودش می‌زند.
        var main = addresses[0];
        text += $"\nشما را با آدرس «{main.Title}» ({main.City}) به صفحهٔ پرداخت می‌برم؛ روش ارسال را ببینید و پرداخت را خودتان بزنید.";
        var actions = addresses.Skip(1).Select(a => new AssistantAction($"ارسال به «{a.Title}»", $"/checkout?addressId={a.Id}")).ToList();
        actions.Add(new("ویرایش سبد", "/cart"));
        return new Outcome(new AssistantReply(text, [], actions, AssistantIntent.Checkout) { Steps = AssistantGuide.Checkout(main.Id) });
    }

    private async Task<Outcome> AddressAsync(Guid? userId, AssistantVoice voice, CancellationToken cancellationToken)
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
            return Simple(AssistantIntent.Address, $"هنوز آدرسی ثبت نکرده‌اید{voice.Comma}.", [new("افزودن آدرس", "/profile/addresses")]);
        var lines = addresses.Select(a => $"• {a.Title} ({a.City}){(a.IsDefault ? " – پیش‌فرض" : "")}");
        return Simple(AssistantIntent.Address, $"آدرس‌های شما{voice.Comma}:\n{string.Join("\n", lines)}", [new("مدیریت آدرس‌ها", "/profile/addresses"), new("تسویه حساب", Say: "تسویه حساب")]);
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

    private async Task<Outcome> TrackAsync(Guid? userId, string? orderNumber, AssistantVoice voice, CancellationToken cancellationToken)
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
                ? $"هنوز سفارشی در حساب شما ثبت نشده است{voice.Comma}."
                : "سفارشی با این شماره در حساب شما پیدا نشد. شماره را دوباره بررسی کنید.", [new("سفارش‌های من", "/profile/orders")]);

        var lines = orders.Select(o =>
            $"• سفارش {o.OrderNumber} ({PersianText.Money(o.PayableAmount)} تومان): {StatusText(o.Status, o.PaymentStatus)}" +
            (string.IsNullOrWhiteSpace(o.TrackingCode) ? "" : $" – کد رهگیری: {o.TrackingCode}") +
            (string.IsNullOrWhiteSpace(o.Post) ? "" : $" – آخرین وضعیت پست: {o.Post}"));
        var actions = orders.Select(o => new AssistantAction($"جزئیات {o.OrderNumber}", $"/profile/orders/{Uri.EscapeDataString(o.OrderNumber)}")).ToList();
        return Simple(AssistantIntent.Track, $"وضعیت سفارش‌های شما{voice.Comma}:\n{string.Join("\n", lines)}", actions);
    }

    // ── مقایسه ───────────────────────────────────────────────────

    private async Task<Outcome> CompareAsync(AssistantRequest request, string text, AssistantParse parse, List<Guid> last, CancellationToken cancellationToken)
    {
        var picked = new List<ProductFilterData>();
        if (!Similar(parse) && AssistantNlu.CompareParts(text) is { } parts)
        {
            foreach (var part in new[] { parts.First, parts.Second })
                if ((await FindAsync(part, ProductSortBy.BestSelling, cancellationToken)).FirstOrDefault() is { } hit && picked.All(p => p.Id != hit.Id))
                    picked.Add(hit);
        }
        // دور ۲۴: «مقایسه این با مشابه» ← کالای همین صفحه (یا آخرین کارت) با پرفروش‌ترین کالای مشابه موجود.
        if (picked.Count < 2 && Similar(parse) && (request.ActionProductId ?? request.CurrentProductId ?? last.FirstOrDefault()) is { } baseId && baseId != Guid.Empty
            && (await products.GetCardsByIdsAsync([baseId])).FirstOrDefault() is { } baseProduct)
        {
            var rival = (await SimilarProductsAsync(baseProduct, 1, cancellationToken)).FirstOrDefault();
            if (rival is null)
                return new Outcome(new AssistantReply($"فعلاً کالای مشابه موجودی برای مقایسه با «{baseProduct.Name}» نداریم.", [ToCard(baseProduct)], [], AssistantIntent.Compare), baseProduct.Id);
            picked = [baseProduct, rival];
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

    // ── دور ۲۵: مشاور خرید (لایهٔ ۳) ─────────────────────────────

    /// <summary>
    /// واژه‌های آشنای فروشگاه (نام کالاها، دسته‌ها و برندها) برای اصلاح غلط تایپی. هر ۱۰ دقیقه از نو؛ خطا ← واژه‌نامهٔ پایه.
    /// </summary>
    private async Task<AssistantVocabulary> VocabularyAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await cache.GetOrCreateAsync(VocabularyKey, async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10);
                entry.Size = 1;
                var names = await context.Products.AsNoTracking().Where(p => p.IsActive).OrderByDescending(p => p.SalesCount).Select(p => p.Name).Take(5000).ToListAsync(cancellationToken);
                var categories = await context.Categories.AsNoTracking().Where(c => c.IsActive).Select(c => c.Name).Take(2000).ToListAsync(cancellationToken);
                var brands = await context.Brands.AsNoTracking().Select(b => b.Name).Take(2000).ToListAsync(cancellationToken);
                return new AssistantVocabulary(names.Concat(categories).Concat(brands));
            }) ?? AssistantVocabulary.Base;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Assistant vocabulary failed");
            return AssistantVocabulary.Base;
        }
    }

    /// <summary>کلید حافظهٔ موقت واژه‌های فروشگاه.</summary>
    public const string VocabularyKey = CacheInvalidation.AssistantVocabularyKey;

    /// <summary>
    /// مشاور خرید: «بهترین هدفون بلوتوثی زیر ۲ میلیون برای ورزش».
    /// ۱. تا ۲۴ نامزد با همان موتور جست‌وجو و فیلتر بودجه/تخفیف/مرتب‌سازی (موجودها اول).
    /// ۲. نبود نتیجه ← «منظورتان … بود؟» ← بدون بودجه (و صادقانه گفته می‌شود) ← دسته ← همان جست‌وجوی معمولی.
    /// ۳. امتیاز (موجودی، رضایت، فروش، تخفیف، کاربرد) و ۳ پیشنهاد با دلیل، نکتهٔ منفی و برچسب روی کارت.
    /// ۴. دکمه‌های بازهٔ قیمت، «ارزان‌ترین»، «مقایسهٔ دو گزینهٔ اول» و «دیدن همه».
    /// </summary>
    private async Task<Outcome> AdvisorAsync(AssistantUnderstood understood, AssistantRequest request, AssistantVoice voice, CancellationToken cancellationToken)
    {
        var slots = understood.Slots;
        var query = understood.ProductQuery.Trim();
        // «کالاهای پرفروش»، «محصولات ارزون» ← نام کالا ندارد؛ فهرست مرتب‌شدهٔ کل فروشگاه.
        if (query is "کالا" or "کالاها" or "کالاهای" or "محصول" or "محصولات" or "جنس" or "اجناس" or "چیزی" or "یه چیزی")
            query = "";
        var purpose = AssistantSlotParser.Purpose(slots.Purpose);
        var sort = slots.Sort ?? ProductSortBy.BestSelling;
        var groups = query.Length >= 2 ? await insights.ExpandAsync(query) : null;

        async Task<List<ProductFilterData>> Load(string? q, IReadOnlyList<IReadOnlyList<string>>? g, decimal? min, decimal? max, bool discounted)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var filter = new ProductFilterParams
            {
                Search = string.IsNullOrWhiteSpace(q) ? null : q,
                SearchTermGroups = string.IsNullOrWhiteSpace(q) ? null : g,
                OnlyActive = true,
                OnlyAvailable = string.IsNullOrWhiteSpace(q) ? true : null,
                OnlyDiscounted = discounted ? true : null,
                MinPrice = min,
                MaxPrice = max,
                SortBy = sort,
                PageId = 1,
                Take = 24,
            };
            return (await products.GetProductByFilterAsync(filter)).Data.ToList();
        }

        var used = query;
        var found = await Load(query, groups, slots.MinPrice, slots.MaxPrice, slots.Discounted);
        string? note = null;
        if (found.Count == 0 && query.Length >= 2 && await insights.DidYouMeanAsync(query) is { } corrected && corrected != query)
        {
            var retry = await Load(corrected, await insights.ExpandAsync(corrected), slots.MinPrice, slots.MaxPrice, slots.Discounted);
            if (retry.Count > 0)
            {
                found = retry;
                used = corrected;
                note = $"منظورتان «{corrected}» بود؟ ";
            }
        }
        var relaxed = false;
        if (found.Count == 0 && (slots.HasBudget || slots.Discounted) && used.Length >= 2)
        {
            found = await Load(used, used == query ? groups : await insights.ExpandAsync(used), null, null, false);
            relaxed = found.Count > 0;
        }
        if (found.Count == 0)
        {
            // هیچ کالایی با این نام نیست ← همان مسیر جست‌وجوی معمولی (دسته، مقاله، «کالای درخواستی»…).
            var plain = understood.Parse with { Query = query, Intent = AssistantIntent.Search };
            return await SearchAsync(understood.Prepared.Text, plain, request, voice, cancellationToken);
        }

        var picks = AssistantAdvisor.Pick(found, purpose, keepOrder: slots.Sort is not null, relaxed ? slots.MaxPrice : null);
        var budget = AssistantSlotParser.BudgetLabel(slots.MinPrice, slots.MaxPrice);
        var what = used.Length >= 2 ? $"«{used}»" : "کالاهای فروشگاه";
        var criteria = new List<string>();
        if (budget.Length > 0 && !relaxed)
            criteria.Add(slots.Approximate && slots.MinPrice is { } a && slots.MaxPrice is { } b ? "حدود " + AssistantFinder.Short((a + b) / 2) : budget);
        if (slots.Discounted && !relaxed)
            criteria.Add("تخفیف‌دار");
        if (purpose is not null)
            criteria.Add("برای " + purpose.Title);
        var sortTitle = slots.Sort switch
        {
            ProductSortBy.Cheapest => "ارزان‌ترین",
            ProductSortBy.MostExpensive => "گران‌ترین",
            ProductSortBy.BestSelling => "پرفروش‌ترین",
            ProductSortBy.Newest => "جدیدترین",
            ProductSortBy.MostDiscounted => "پرتخفیف‌ترین",
            _ => null,
        };
        var head = sortTitle is not null
            ? $"{sortTitle} {what}{(criteria.Count > 0 ? " (" + string.Join("، ", criteria) + ")" : "")}:"
            : AssistantVariety.Pick(understood.Prepared.Text,
                $"با توجه به خواستهٔ شما{(criteria.Count > 0 ? " (" + string.Join("، ", criteria) + ")" : "")}، این‌ها را برای {what} پیشنهاد می‌کنم{voice.Comma}:",
                $"بهترین گزینه‌های {what}{(criteria.Count > 0 ? " " + string.Join("، ", criteria) : "")} از نظر رضایت خریداران، فروش و قیمت:",
                $"{what}{(criteria.Count > 0 ? " " + string.Join("، ", criteria) : "")} – پیشنهاد من به ترتیب امتیاز:");
        var lines = new List<string>();
        if (relaxed)
        {
            var condition = slots.HasBudget ? "این بودجه" : "این شرط";
            lines.Add(used.Length >= 2
                ? $"با {condition} «{used}» پیدا نکردم{voice.Comma}؛ نزدیک‌ترین گزینه‌ها (بدون شرط بودجه/تخفیف):"
                : $"با {condition} کالایی پیدا نکردم{voice.Comma}؛ نزدیک‌ترین گزینه‌ها:");
        }
        lines.Add((note ?? "") + head);
        lines.AddRange(picks.Select((p, i) => AssistantAdvisor.Line(i + 1, p)));
        if (picks.All(p => !p.Product.IsInStock))
            lines.Add("هیچ‌کدام فعلاً موجود نیست؛ در صفحهٔ کالا «خبرم کن» را بزنید تا موجود شد خبرتان کنیم.");

        var actions = new List<AssistantAction>();
        var target = used.Length >= 2 ? used : null;
        if (!slots.HasBudget || relaxed)
            foreach (var (min, max) in AssistantAdvisor.Bands(found.Select(p => p.FinalPrice)))
            {
                var label = AssistantSlotParser.BudgetLabel(min, max);
                actions.Add(new(label, Say: $"{target ?? "کالا"} {label}"));
            }
        if (slots.Sort != ProductSortBy.Cheapest && target is not null)
            actions.Add(new("ارزان‌ترین‌ها", Say: $"ارزانترین {target}{(slots.MaxPrice is { } cap && !relaxed ? " زیر " + AssistantFinder.Short(cap) : "")}"));
        if (picks.Count >= 2)
            actions.Add(new("مقایسهٔ دو گزینهٔ اول", Say: "مقایسه اولی و دومی"));
        var link = new List<string>();
        if (target is not null)
            link.Add("q=" + Uri.EscapeDataString(target));
        if (!relaxed && slots.MinPrice is { } low)
            link.Add("MinPrice=" + low.ToString("0", System.Globalization.CultureInfo.InvariantCulture));
        if (!relaxed && slots.MaxPrice is { } high)
            link.Add("MaxPrice=" + high.ToString("0", System.Globalization.CultureInfo.InvariantCulture));
        if (!relaxed && slots.Discounted)
            link.Add("Discounted=true");
        link.Add("Sort=" + (int)sort);
        actions.Add(new("دیدن همه", "/search?" + string.Join("&", link)));

        var cards = picks.Select(p => ToCard(p.Product) with { Note = p.Badge }).ToList();
        var reply = new AssistantReply(string.Join("\n", lines), cards, actions.Take(6).ToList(), AssistantIntent.Advisor);
        return new Outcome(reply, picks.Count == 1 ? picks[0].Product.Id : null, true, purpose?.Title ?? (slots.HasBudget ? "بودجه" : sortTitle), target);
    }

    // ── دور ۲۵: نظر خریداران و پرسش و پاسخ (لایهٔ ۴) ──────────────

    /// <summary>دیدگاه‌های تأییدشدهٔ یک کالا (حداکثر ۶۰ تای اخیر) برای خلاصه.</summary>
    private async Task<IReadOnlyList<ReviewInput>> ReviewsOfAsync(Guid productId, CancellationToken cancellationToken) =>
        (await context.Comments.AsNoTracking()
            .Where(c => c.ProductId == productId && c.Type == CommentType.Review && c.Status == CommentStatus.Approved && c.ParentId == null)
            .OrderByDescending(c => c.CreatedAt)
            .Select(c => new { c.Content, c.Rating, c.IsRecommended })
            .Take(60)
            .ToListAsync(cancellationToken))
        .Select(c => new ReviewInput(c.Content, c.Rating, c.IsRecommended))
        .ToList();

    /// <summary>
    /// جواب از «پرسش و پاسخ» تأییدشدهٔ همان کالا: پرسشی که شباهتش با پیام ≥ ۰٫۵ باشد و پاسخ تأییدشده داشته باشد.
    /// مثال: پیام «با آیفون سازگاره؟» و پرسش قبلی «روی آیفون ۱۳ کار می‌کند؟» با پاسخ فروشنده ← همان پاسخ.
    /// </summary>
    private async Task<(string Question, string Answer)?> AnsweredQuestionAsync(Guid productId, string text, CancellationToken cancellationToken)
    {
        var rows = await context.Comments.AsNoTracking()
            .Where(c => c.ProductId == productId && c.Type == CommentType.Question && c.Status == CommentStatus.Approved && c.ParentId == null)
            .OrderByDescending(c => c.CreatedAt)
            .Select(c => new
            {
                c.Content,
                Answer = c.Replies.Where(r => r.Status == CommentStatus.Approved).OrderBy(r => r.CreatedAt).Select(r => r.Content).FirstOrDefault(),
            })
            .Take(50)
            .ToListAsync(cancellationToken);
        var best = rows
            .Where(r => !string.IsNullOrWhiteSpace(r.Answer))
            .Select(r => new { r.Content, r.Answer, Score = AssistantNlu.Similarity(text, r.Content) })
            .Where(r => r.Score >= 0.5)
            .OrderByDescending(r => r.Score)
            .FirstOrDefault();
        if (best is null)
            return null;
        return (PersianText.Truncate(PersianText.HtmlToText(best.Content).Trim(), 160), PersianText.Truncate(PersianText.HtmlToText(best.Answer!).Trim(), 400));
    }

    // ── دور ۲۵: کارهای بعد از خرید (لایهٔ ۶) ─────────────────────

    /// <summary>پاسخ «اول وارد شوید» با راهنمای زنده تا صفحهٔ ورود و بازگشت به <paramref name="back"/>.</summary>
    private static Outcome LoginFirst(AssistantIntent intent, string back, string text) =>
        new(new AssistantReply(text, [], [new("ورود به حساب", "/login?returnUrl=" + Uri.EscapeDataString(back))], intent)
        {
            Steps = AssistantGuide.Login(back, null, withCode: true),
        });

    private static string OrderUrl(string orderNumber) => "/profile/orders/" + orderNumber.ToLowerInvariant();

    /// <summary>
    /// لغو سفارش با تأیید خود مشتری. مراحل: فهرست سفارش‌های قابل لغو (پیش از آماده‌سازی) ← «مطمئنید؟» با دکمهٔ «بله، لغو شود»
    /// ← لغو واقعی با همان سرویس سایت (مبلغ پرداختی به کیف پول برمی‌گردد). سفارش در حال آماده‌سازی/ارسال‌شده ← توضیح و راه درست (پشتیبانی/مرجوعی).
    /// </summary>
    private async Task<Outcome> CancelOrderAsync(AssistantRequest request, AssistantParse parse, AssistantVoice voice, StoreSettingDto store, CancellationToken cancellationToken)
    {
        if (request.UserId is not { } userId)
            return LoginFirst(AssistantIntent.OrderCancel, "/profile/orders", "برای لغو سفارش اول وارد حساب شوید؛ بعد از ورود دوباره بنویسید «لغو سفارش» تا سفارش‌های قابل لغو را نشانتان بدهم.");
        if (!store.AllowCustomerCancellation)
            return Simple(AssistantIntent.OrderCancel, "لغو سفارش از طریق سایت فعلاً غیرفعال است؛ لطفاً با پشتیبانی تماس بگیرید.", [new("گفت‌وگو با کارشناس", Say: "میخوام با کارشناس صحبت کنم")]);

        var number = parse.OrderNumber;
        var orders = await context.Orders.AsNoTracking()
            .Where(o => o.UserId == userId && (number == null || o.OrderNumber == number))
            .OrderByDescending(o => o.CreatedAt)
            .Take(number is null ? 10 : 1)
            .Select(o => new { o.Id, o.OrderNumber, o.Status, o.PaymentStatus, o.PayableAmount })
            .ToListAsync(cancellationToken);
        if (number is not null && orders.Count == 0)
            return Simple(AssistantIntent.OrderCancel, $"سفارشی با شمارهٔ {number} در حساب شما پیدا نشد{voice.Comma}.", [new("سفارش‌های من", "/profile/orders")]);
        if (orders.Count == 0)
            return Simple(AssistantIntent.OrderCancel, $"هنوز سفارشی ثبت نکرده‌اید{voice.Comma}.", Suggestions());

        var chosen = number is not null ? orders[0] : null;
        var cancellable = orders.Where(o => OrderRules.IsCancellableByCustomer(o.Status)).Take(4).ToList();
        if (chosen is null && cancellable.Count == 1)
            chosen = cancellable[0];
        if (chosen is null)
        {
            if (cancellable.Count == 0)
                return Simple(AssistantIntent.OrderCancel,
                    "سفارش قابل لغوی ندارید؛ فقط سفارش‌هایی که آماده‌سازی‌شان شروع نشده از سایت لغو می‌شوند. سفارش ارسال‌شده را بعد از تحویل می‌توانید مرجوع کنید.",
                    [new("پیگیری سفارش", Say: "سفارشم کجاست"), new("گفت‌وگو با کارشناس", Say: "میخوام با کارشناس صحبت کنم")]);
            var list = cancellable.Select(o => $"• {o.OrderNumber} – {PersianText.Money(o.PayableAmount)} تومان ({StatusText(o.Status, o.PaymentStatus)})");
            return Simple(AssistantIntent.OrderCancel, $"این سفارش‌ها هنوز قابل لغو هستند{voice.Comma}؛ کدام را لغو کنم؟\n" + string.Join("\n", list),
                cancellable.Select(o => new AssistantAction("لغو " + o.OrderNumber, Say: "لغو سفارش " + o.OrderNumber)).ToList());
        }

        var orderUrl = OrderUrl(chosen.OrderNumber);
        if (!OrderRules.IsCancellableByCustomer(chosen.Status))
        {
            var why = chosen.Status switch
            {
                OrderStatus.Processing => "در حال آماده‌سازی است و از این مرحله فقط پشتیبانی می‌تواند لغوش کند.",
                OrderStatus.Shipped => "ارسال شده است؛ بعد از تحویل اگر نخواستید می‌توانید درخواست مرجوعی ثبت کنید.",
                OrderStatus.Delivered => "تحویل شده است؛ برای پس دادن، درخواست مرجوعی ثبت کنید.",
                OrderStatus.Cancelled => "قبلاً لغو شده است.",
                _ => "دیگر قابل لغو نیست.",
            };
            var next = chosen.Status is OrderStatus.Shipped or OrderStatus.Delivered
                ? new AssistantAction("درخواست مرجوعی", Say: "مرجوع سفارش " + chosen.OrderNumber)
                : new AssistantAction("گفت‌وگو با کارشناس", Say: "میخوام با کارشناس صحبت کنم");
            return Simple(AssistantIntent.OrderCancel, $"سفارش {chosen.OrderNumber} {why}", [next, new("جزئیات سفارش", orderUrl)]);
        }

        var paid = chosen.PaymentStatus == PaymentStatus.Paid;
        var confirmed = parse.Normalized.Split(' ').Any(t => t is "تایید" or "تاييد" or "تائید");
        if (confirmed && number is not null)
        {
            if (orderActions is null)
                return Simple(AssistantIntent.OrderCancel, "لغو از اینجا در دسترس نیست؛ از صفحهٔ سفارش «لغو سفارش» را بزنید.", [new("جزئیات سفارش", orderUrl)]);
            var result = await orderActions.CancelOrderByCustomerAsync(chosen.Id, userId, "لغو به درخواست مشتری از طریق دستیار فروشگاه");
            if (!result.IsSuccess())
                return Simple(AssistantIntent.OrderCancel, result.Message ?? "لغو سفارش ممکن نشد.", [new("جزئیات سفارش", orderUrl), new("گفت‌وگو با کارشناس", Say: "میخوام با کارشناس صحبت کنم")]);
            return Simple(AssistantIntent.OrderCancel,
                $"سفارش {chosen.OrderNumber} لغو شد{voice.Comma}." + (paid ? $" مبلغ {PersianText.Money(chosen.PayableAmount)} تومان به کیف پول شما برگشت." : ""),
                [new("سفارش‌های من", "/profile/orders"), new("ادامهٔ خرید", Say: "کالاهای پرفروش")]);
        }

        // مرحلهٔ تأیید: لغو هرگز بدون «بله» خود مشتری انجام نمی‌شود.
        var refund = paid ? $" مبلغ پرداختی ({PersianText.Money(chosen.PayableAmount)} تومان) به کیف پول شما برمی‌گردد." : "";
        return new Outcome(new AssistantReply($"مطمئنید سفارش {chosen.OrderNumber} لغو شود{voice.Comma}؟{refund}", [],
            [new("بله، لغو شود", Say: "تایید لغو سفارش " + chosen.OrderNumber), new("نه، منصرف شدم", Say: "ممنون"), new("جزئیات سفارش", orderUrl)], AssistantIntent.OrderCancel)
        {
            Steps = request.PagePath == orderUrl
                ? new List<AssistantStep> { AssistantGuide.Point("order-cancel", "از اینجا هم می‌توانید لغو کنید") }
                : new List<AssistantStep>(),
        });
    }

    /// <summary>
    /// مرجوعی: سفارش‌های تحویل‌شده‌ای که هنوز در مهلت مرجوعی‌اند (با همان قانون‌های سایت) ← فرم «درخواست مرجوعی» همان سفارش.
    /// ثبت نهایی (انتخاب کالا و دلیل) را خود مشتری در فرم انجام می‌دهد.
    /// </summary>
    private async Task<Outcome> ReturnOrderAsync(AssistantRequest request, AssistantParse parse, AssistantVoice voice, StoreSettingDto store, CancellationToken cancellationToken)
    {
        if (request.UserId is not { } userId)
            return LoginFirst(AssistantIntent.OrderReturn, "/profile/orders", "برای ثبت مرجوعی اول وارد حساب شوید؛ بعد از ورود سفارش‌های قابل مرجوعی را نشانتان می‌دهم.");
        if (!store.IsReturnEnabled)
            return Simple(AssistantIntent.OrderReturn, "ثبت آنلاین مرجوعی فعلاً فعال نیست؛ لطفاً با پشتیبانی تماس بگیرید.", [new("گفت‌وگو با کارشناس", Say: "میخوام با کارشناس صحبت کنم")]);
        if (returns is null)
            return Simple(AssistantIntent.OrderReturn, "درخواست مرجوعی را از صفحهٔ سفارش ثبت کنید.", [new("سفارش‌های من", "/profile/orders")]);

        List<string> numbers;
        if (parse.OrderNumber is { } given)
            numbers = [given];
        else
            numbers = await context.Orders.AsNoTracking()
                .Where(o => o.UserId == userId && o.Status == OrderStatus.Delivered)
                .OrderByDescending(o => o.CreatedAt)
                .Select(o => o.OrderNumber)
                .Take(3)
                .ToListAsync(cancellationToken);
        if (numbers.Count == 0)
            return Simple(AssistantIntent.OrderReturn, $"سفارش تحویل‌شده‌ای در حساب شما نیست{voice.Comma}؛ مرجوعی بعد از تحویل سفارش ممکن است." +
                (store.ReturnWindowDays > 0 ? $" مهلت آن {PersianText.Digits(store.ReturnWindowDays)} روز پس از تحویل است." : ""), [new("پیگیری سفارش", Say: "سفارشم کجاست")]);

        var eligible = new List<(string Number, DateTimeOffset? Deadline)>();
        string? reason = null;
        foreach (var number in numbers)
        {
            var check = await returns.GetEligibilityAsync(userId, number);
            if (check.IsEligible)
                eligible.Add((check.OrderNumber ?? number, check.Deadline));
            else
                reason ??= check.Reason;
        }
        if (eligible.Count == 0)
            return Simple(AssistantIntent.OrderReturn, (reason ?? "این سفارش قابل مرجوعی نیست.") + " اگر سؤالی دارید با کارشناس صحبت کنید.",
                [new("مرجوعی‌های من", "/profile/returns"), new("گفت‌وگو با کارشناس", Say: "میخوام با کارشناس صحبت کنم")]);
        if (eligible.Count == 1)
        {
            var (number, deadline) = eligible[0];
            var until = deadline is { } d ? $" (تا {SeoDate(d)})" : "";
            return new Outcome(new AssistantReply($"سفارش {number} قابل مرجوعی است{until}{voice.Comma}. در فرم، کالا و دلیل مرجوعی را انتخاب و ثبت کنید؛ مبلغ پس از بررسی به کیف پولتان برمی‌گردد.", [],
                [new("ثبت درخواست مرجوعی", "/profile/returns/new/" + number.ToLowerInvariant()), new("شرایط مرجوعی", Say: "شرایط مرجوعی چیه")], AssistantIntent.OrderReturn)
            {
                Steps = [AssistantGuide.Go(OrderUrl(number)), AssistantGuide.Point("order-return", "این دکمه فرم مرجوعی را باز می‌کند")],
            });
        }
        return Simple(AssistantIntent.OrderReturn, "این سفارش‌ها قابل مرجوعی هستند؛ کدام؟\n" + string.Join("\n", eligible.Select(e => "• " + e.Number + (e.Deadline is { } d ? $" (تا {SeoDate(d)})" : ""))),
            eligible.Select(e => new AssistantAction("مرجوعی " + e.Number, "/profile/returns/new/" + e.Number.ToLowerInvariant())).ToList());
    }

    /// <summary>فاکتور: صفحهٔ فاکتور (پیش‌فاکتور یا فاکتور رسمی) با دکمهٔ PDF؛ چند سفارش ← انتخاب.</summary>
    private async Task<Outcome> InvoiceAsync(AssistantRequest request, AssistantParse parse, AssistantVoice voice, CancellationToken cancellationToken)
    {
        if (request.UserId is not { } userId)
            return LoginFirst(AssistantIntent.Invoice, "/profile/orders", "برای دیدن فاکتور اول وارد حساب شوید.");
        var number = parse.OrderNumber;
        var orders = await context.Orders.AsNoTracking()
            .Where(o => o.UserId == userId && (number == null || o.OrderNumber == number) && (number != null || o.Status != OrderStatus.Cancelled))
            .OrderByDescending(o => o.CreatedAt)
            .Take(3)
            .Select(o => new { o.OrderNumber, o.InvoiceNumber, o.CreatedAt })
            .ToListAsync(cancellationToken);
        if (orders.Count == 0)
            return Simple(AssistantIntent.Invoice, number is null ? $"هنوز سفارشی ندارید{voice.Comma}." : $"سفارشی با شمارهٔ {number} در حساب شما پیدا نشد.", [new("سفارش‌های من", "/profile/orders")]);
        if (orders.Count == 1)
        {
            var order = orders[0];
            var kind = string.IsNullOrEmpty(order.InvoiceNumber) ? "پیش‌فاکتور" : "فاکتور";
            return new Outcome(new AssistantReply($"{kind} سفارش {order.OrderNumber} آماده است{voice.Comma}؛ در صفحهٔ فاکتور دکمهٔ دریافت PDF و چاپ هست.", [],
                [new($"مشاهدهٔ {kind} و PDF", OrderUrl(order.OrderNumber) + "/invoice")], AssistantIntent.Invoice)
            {
                Steps = [AssistantGuide.Go(OrderUrl(order.OrderNumber)), AssistantGuide.Point("order-invoice", "فاکتور و PDF از اینجا")],
            });
        }
        return Simple(AssistantIntent.Invoice, "فاکتور کدام سفارش را می‌خواهید؟",
            orders.Select(o => new AssistantAction($"فاکتور {o.OrderNumber} ({SeoDate(o.CreatedAt)})", OrderUrl(o.OrderNumber) + "/invoice")).ToList());
    }

    // ── دور ۲۵: نظر مشتری دربارهٔ پاسخ و آموزش‌ها (لایهٔ ۷) ────────

    /// <summary>پاسخ آموزش‌داده‌شده توسط کارکنان (متن + لینک اختیاری).</summary>
    private static Outcome Taught(AssistantTaught taught)
    {
        var actions = new List<AssistantAction>();
        if (AssistantGuide.IsLocalUrl(taught.Url))
            actions.Add(new("مشاهده", taught.Url));
        return new Outcome(new AssistantReply(taught.Answer ?? "", [], actions, AssistantIntent.Help), Topic: TaughtTopic);
    }

    /// <summary>برچسب پیام‌هایی که با «آموزش کارکنان» جواب گرفته‌اند (در گزارش پنل).</summary>
    public const string TaughtTopic = "آموزش کارکنان";

    /// <inheritdoc />
    public async Task<bool> FeedbackAsync(Guid messageId, string sessionKey, bool helpful, CancellationToken cancellationToken = default)
    {
        if (messageId == Guid.Empty || string.IsNullOrWhiteSpace(sessionKey))
            return false;
        // فقط پیام همین گفت‌وگو و فقط تا ۲۴ ساعت (کسی نمی‌تواند به پیام دیگران رأی بدهد).
        var since = DateTimeOffset.UtcNow.AddDays(-1);
        var count = await context.AssistantMessages
            .Where(m => m.Id == messageId && m.SessionKey == sessionKey && m.CreatedAt >= since)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.Helpful, helpful), cancellationToken);
        return count > 0;
    }

    // ── ثبت، گزارش و خوراک تکمیل هوشمند ────────────────────────

    private static string? Cut(string? value, int max)
    {
        value = value?.Trim();
        return string.IsNullOrEmpty(value) ? null : value.Length > max ? value[..max] : value;
    }

    /// <summary>ثبت پیام (متن پوشانده‌شده) برای گزارش پنل. خروجی (دور ۲۵): شناسهٔ ردیف برای 👍/👎؛ خطا ← null.</summary>
    private async Task<Guid?> LogAsync(AssistantRequest request, string text, Outcome outcome, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        var productId = outcome.ProductId ?? outcome.Reply.Lookup?.ProductId;
        var id = Guid.NewGuid();
        try
        {
            context.AssistantMessages.Add(new AssistantMessage
            {
                Id = id,
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
            context.ChangeTracker.Clear();
            if (!outcome.Answered && productId is { } gapProduct && outcome.Reply.Intent == AssistantIntent.ProductQuestion)
                await MaybeFeedAsync(gapProduct, cancellationToken);
            return id;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // ثبت گزارش هرگز نباید پاسخ مشتری را خراب کند.
            context.ChangeTracker.Clear();
            logger.LogWarning(exception, "Assistant message log failed");
            return null;
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
