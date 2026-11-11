using System.Text.Json;
using Fratak_Shop.Core.Services.Pricing;
using Fratak_Shop.Core.Services.Seo.Autopilot;
using Fratak_Shop.Core.Services.Seo.Engine;
using Fratak_Shop.Core.Services.Smart;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Entities.SeoEngine;
using Fratak_Shop.Data.Entities.Smart;
using Fratak_Shop.Web.Services;

namespace Fratak_Shop.Web.Infrastructure.Smart;

/// <summary>نتیجهٔ اجرای یک کار هوشمند.</summary>
public sealed record SmartOutcome(SmartTaskStatus Status, string Summary, string? ResultJson = null);

/// <summary>
/// جزئیات قابل بازبینی یک کار (در ستون ResultJson). کارشناس در صفحهٔ «محتوای هوشمند ← محصول» همین را می‌بیند و تأیید/رد می‌کند.
/// </summary>
public sealed class SmartTaskResult
{
    public List<SpecSuggestion> Specs { get; set; } = [];
    public List<FoundImage> PendingImages { get; set; } = [];
    public List<FoundImage> AddedImages { get; set; } = [];
    public int SpecsAdded { get; set; }
    public int Reviews { get; set; }
    public bool InsightApproved { get; set; }
    public int? QualityScore { get; set; }
    public List<string> Problems { get; set; } = [];
    public List<string> Steps { get; set; } = [];

    /// <summary>دور «رد همه و جست‌وجوی دوباره» عکس‌ها (۰ = بار اول). دکمهٔ «جست‌وجوی دوباره» دور بعدی را صف می‌کند.</summary>
    public int ImageRound { get; set; }

    /// <summary>پیش‌نمایش «توضیحات محصول» (دور بیستم)؛ null یعنی این کار متنی ننوشت.</summary>
    public ReviewPreview? Review { get; set; }

    /// <summary>دور ۲۲: پیشنهاد «نام بهینه / نام انگلیسی / نامک» (برای کالای منتشرشده فقط پیشنهاد؛ برای پیش‌نویس خودکار اعمال‌شده).</summary>
    public IdentitySuggestion? Identity { get; set; }
}

/// <summary>
/// دور ۲۲: پیشنهاد هویت کالا. Applied=true یعنی خودکار اعمال شد (کالای هرگز منتشرنشده)؛ وگرنه کارشناس در صفحهٔ «محتوای هوشمند ← محصول»
/// دکمهٔ «اعمال نام پیشنهادی» را می‌زند. BaseName = نام کالا هنگام ساخت پیشنهاد؛ اگر تا زمان تأیید نام عوض شده باشد اعمال نمی‌شود.
/// مثال: Name=«هدفون بی‌سیم شیائومی مدل Redmi Buds 4»، BaseName=«خرید هدفون بی سیم شیائومی Redmi Buds 4 اورجینال».
/// </summary>
public sealed class IdentitySuggestion
{
    public string? Name { get; set; }
    public string? EnglishName { get; set; }
    public string? Slug { get; set; }
    public string BaseName { get; set; } = "";
    public List<string> Changes { get; set; } = [];
    public bool Applied { get; set; }
}

/// <summary>
/// پیش‌نمایش «توضیحات محصول» حرفه‌ای در نتیجهٔ کار هوشمند. Applied=true یعنی خودکار روی صفحه نشسته (فقط برای اطلاع)؛
/// وگرنه کارشناس در صفحهٔ «محتوای هوشمند ← محصول» یکی از Mode ها را انتخاب می‌کند:
/// «replace» = جایگزین متن فعلی؛ «choose» = متن انسانی بلند وجود دارد ← جایگزین / افزودن زیر متن فعلی / رد.
/// </summary>
public sealed class ReviewPreview
{
    public string Html { get; set; } = "";
    public int Words { get; set; }
    public int CurrentWords { get; set; }
    public int ScoreBefore { get; set; }
    public int ScoreAfter { get; set; }
    public int Natural { get; set; }
    public bool Safe { get; set; }
    public bool Applied { get; set; }
    public string Mode { get; set; } = "replace";
    public List<string> Problems { get; set; } = [];

    /// <summary>هش متن صفحه هنگام ساخت پیش‌نمایش؛ اگر تا زمان تأیید متن صفحه عوض شده باشد، اعمال نمی‌شود (کار کسی پاک نشود).</summary>
    public string? BaseHash { get; set; }
}

/// <summary>تصمیم دستیار برای متن «توضیحات محصول» یک کالا (خالص؛ <see cref="SmartOrchestrator.DecideReview"/>).</summary>
public enum ReviewAction
{
    /// <summary>دست نزن (کارشناس برگردانده یا ویرایش کرده است).</summary>
    Skip,

    /// <summary>متن کم است (&lt; ۱۵۰ واژه): بررسی تازه زیر متن فعلی؛ اگر امن بود خودکار.</summary>
    Write,

    /// <summary>متن فعلی را خود دستیار نوشته: با داده‌های تازه بازنویسی (بخش انسانی بالای آن حفظ می‌شود).</summary>
    Regenerate,

    /// <summary>متن انسانی بلند: فقط پیشنهاد؛ کارشناس جایگزین/افزودن/رد را انتخاب می‌کند.</summary>
    Propose,
}

/// <summary>
/// مغز اجرای کارهای صف هوشمند (B1–B21): هر کار را بر اساس نوعش انجام می‌دهد و فقط چیزهای «مطمئن» را خودکار اعمال می‌کند.
/// <list type="bullet">
/// <item><b>عکس کالا</b>: ۱۰ تا ۱۵ نامزد از همهٔ منابع؛ پیش‌فرض همه منتظر تأیید کارشناس (رد همه ← جست‌وجوی دوباره بدون عکس‌های ردشده).</item>
/// <item><b>توضیحات محصول</b>: HTML حرفه‌ای سئو؛ متن کم ← خودکار (اگر امن)، متن انسانی بلند ← فقط پیشنهاد.</item>
/// <item><b>مشخصات</b>: ادغام دیجی‌کالا + صفحهٔ تأمین‌کننده (جدول، dl و JSON-LD) + تا ۶ فروشگاه نتایج جست‌وجو؛ ردیف تازه‌ای که دست‌کم ۲ منبع روی آن توافق دارند خودکار،
/// تغییر مقدار فعلی همیشه با تأیید کارشناس.</item>
/// <item><b>خلاصهٔ خرید</b>: تحلیل نظرهای سایت و دیجی‌کالا؛ با ۸ نظر یا بیشتر خودکار تأیید، کمتر ← پیش‌نویس. خلاصهٔ تأییدشدهٔ انسانی هرگز بازنویسی نمی‌شود.</item>
/// <item><b>تکمیل یک‌کلیکی</b>: عکس (اگر کم است) + مشخصات + خلاصه + سئوی خودکار.</item>
/// <item><b>دسته</b>: توضیح و پرسش‌های رایج از دادهٔ واقعی + بنر بدون متن؛ متن موجود هرگز جایگزین نمی‌شود.</item>
/// <item><b>مقاله</b>: نوشتن از تقویم محتوا، دروازهٔ کیفیت، انتشار فقط اگر قبول شد (وگرنه پیش‌نویس) و سئوی خودکار.</item>
/// </list>
/// <para>ثبت: Scoped؛ فقط <see cref="SmartWorker"/> آن را صدا می‌زند.</para>
/// </summary>
public sealed class SmartOrchestrator(
    ISmartCatalogService catalog,
    ISmartConfigService config,
    IProductInsightService insights,
    IBlogPlanService plan,
    ISeoAutopilotService seo,
    ISeoEngineService engine,
    SmartSources sources,
    SmartImageFinder images,
    ISmartCategoryService categorizer,
    ISmartTaskQueue queue,
    IMarketPricingService pricing,
    ILogger<SmartOrchestrator> logger)
{
    public const string Actor = "دستیار هوشمند";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>حداکثر فروشگاه نتایج جست‌وجو که مشخصاتشان خوانده می‌شود (هر فروشگاه یک «رأی» در ادغام مشخصات).</summary>
    public const int SpecShopsMax = 6;

    /// <summary>متن کمتر از این تعداد واژه «کم‌محتوا» حساب می‌شود و دستیار خودش توضیحات محصول را اضافه می‌کند.</summary>
    public const int ThinBodyWords = 150;

    public async Task<SmartOutcome> RunAsync(ClaimedSmartTask task, CancellationToken cancellationToken)
    {
        // دور ۲۶: کاری که مدیر خودش از صفحهٔ کالا خواسته (بار {"manual":1}) هرگز با «لازم نیست» رد نمی‌شود: تحقیق عمیق‌تر،
        // و اگر متن/مشخصهٔ انسانی هست، نسخهٔ تازه «پیشنهاد» می‌شود (بدون دست زدن به متن فعلی تا تأیید مدیر).
        var manual = PayloadInt(task.PayloadJson, "manual") == 1;
        return task.Kind switch
        {
            SmartTaskKind.ProductImages when task.TargetId is { } id => await ProductImagesAsync(id, manual: true, PayloadInt(task.PayloadJson, "round"), cancellationToken),
            SmartTaskKind.ProductReview when task.TargetId is { } id => await ProductReviewAsync(id, manual, cancellationToken),
            SmartTaskKind.ProductSpecs when task.TargetId is { } id => await ProductSpecsAsync(id, manual, cancellationToken),
            SmartTaskKind.ProductInsight when task.TargetId is { } id => await ProductInsightAsync(id, manual, cancellationToken),
            SmartTaskKind.ProductComplete when task.TargetId is { } id => await ProductCompleteAsync(id, manual, cancellationToken),
            SmartTaskKind.CategoryComplete when task.TargetId is { } id => await CategoryAsync(id, task.PayloadJson, cancellationToken),
            SmartTaskKind.BlogArticle when task.TargetId is { } id => await BlogArticleAsync(id, cancellationToken),
            SmartTaskKind.BlogImage when task.TargetId is { } id => await BlogImageAsync(id, task.PayloadJson, cancellationToken),
            _ => new SmartOutcome(SmartTaskStatus.Skipped, "نوع کار یا هدف نامعتبر است."),
        };
    }

    // ───────────────────────────── عکس کالا ─────────────────────────────

    /// <summary>
    /// عکس کالا: ۱۰ تا ۱۵ نامزد (تنظیم ImageCandidates) از همهٔ منابع. با «عکس‌ها نیاز به تأیید دارند» (پیش‌فرض) هیچ عکسی خودکار
    /// به کالا اضافه نمی‌شود و همه منتظر انتخاب کارشناس می‌مانند (مشتری نباید عکس کالای دیگری را ببیند)؛ در غیر این صورت فقط عکس‌های
    /// منابع «همان کالا» تا سقف AutoImagesMax خودکار. round = دور جست‌وجوی دوباره.
    /// </summary>
    private async Task<SmartOutcome> ProductImagesAsync(Guid productId, bool manual, int round, CancellationToken cancellationToken)
    {
        var subject = await catalog.GetProductAsync(productId, cancellationToken);
        if (subject is null)
            return new SmartOutcome(SmartTaskStatus.Skipped, "محصول پیدا نشد.");
        var options = await config.GetAsync(cancellationToken);
        var want = Math.Clamp(options.ImageCandidates, 6, 15);
        if (!manual && !options.ImagesNeedApproval)
            want = Math.Max(want, options.AutoImagesMax);
        var found = await images.FindForProductAsync(subject, want, Math.Clamp(round, 0, 20), cancellationToken);
        var result = new SmartTaskResult { ImageRound = Math.Clamp(round, 0, 20) };
        var auto = options.ImagesNeedApproval
            ? new List<FoundImage>()
            : found.Where(f => f.Trusted).Take(Math.Max(1, options.AutoImagesMax)).ToList();
        // دور ۲۸ (#۶۰ تا #۶۶ – «هیچ کالایی آماده نمی‌شد»): پیش‌نویس هرگز منتشرنشده‌ای که هیچ عکسی ندارد، بهترین عکسِ مطمئن
        // (منبع «همان کالا» یا مدل تأییدشده، و اطمینان بررسی سوژه ≥ ۰٫۷۲) را خودکار می‌گیرد تا به «آمادهٔ انتشار» برسد.
        // مشتری هنوز چیزی نمی‌بیند (کالا منتشر نشده) و مدیر پیش از انتشار عکس را می‌بیند؛ بقیهٔ نامزدها مثل قبل منتظر انتخاب می‌مانند.
        if (auto.Count == 0 && subject.NeverPublished && subject.ImageNames.Count == 0
            && PickDraftImage(found) is { } best)
            auto.Add(best);
        var pending = found.Except(auto).ToList();
        if (auto.Count > 0)
        {
            // دور ۲۳ (مورد ۲۷): نام انگلیسی خوانا از روی نامک کالا برای عکس‌هایی که مستقیم اضافه می‌شوند.
            var named = images.SeoRename(auto.Select(f => f.FileName).ToList(), subject.Slug);
            var alts = named.Select((name, i) => (FileName: name, Alt: i == 0 ? subject.Name : $"{subject.Name} – نمای {Core.Services.Seo.Engine.PersianText.Digits(i + 1)}")).ToList();
            var added = await catalog.AddProductImagesAsync(productId, alts, cancellationToken);
            result.AddedImages.AddRange(auto.Take(added).Select((f, i) => f with { FileName = named[i] }));
            if (added < auto.Count)
                images.DeleteProductFiles(named.Skip(added));
        }
        result.PendingImages.AddRange(pending);
        var summary = found.Count == 0
            ? "عکس مناسبی (≥ ۶۰۰ پیکسل، غیرتکراری) پیدا نشد."
            : $"{Core.Services.Seo.Engine.PersianText.Digits(result.AddedImages.Count)} عکس اضافه شد؛ {Core.Services.Seo.Engine.PersianText.Digits(pending.Count)} عکس منتظر انتخاب شماست.";
        var status = pending.Count > 0 ? SmartTaskStatus.NeedsReview : found.Count == 0 ? SmartTaskStatus.Skipped : SmartTaskStatus.Applied;
        return new SmartOutcome(status, summary, JsonSerializer.Serialize(result, Json));
    }

    /// <summary>
    /// دور ۲۸: عکس قابل افزودن خودکار به پیش‌نویس بی‌عکس (خالص؛ آزمون‌پذیر). شرط: منبع مطمئن (دیجی‌کالا/تأمین‌کننده/باسلام) یا
    /// «همان مدل» تأییدشده، و اطمینان بررسی سوژه دست‌کم <see cref="ImageSubjectCheck.AutoAttachConfidence"/>. بیشترین اطمینان اول.
    /// مثال: عکس دیجی‌کالا روی سفید با اطمینان ۰٫۸۵ ← همان؛ عکس بینگ ناشناس با مدل نامعلوم ← null.
    /// </summary>
    public static FoundImage? PickDraftImage(IEnumerable<FoundImage> found) =>
        found.Where(f => (f.Trusted || f.Model == ModelVerdict.Same) && f.Confidence >= ImageSubjectCheck.AutoAttachConfidence)
            .OrderByDescending(f => f.Confidence).ThenByDescending(f => f.Quality)
            .FirstOrDefault();

    // ───────────────────────────── مشخصات ─────────────────────────────

    /// <summary>
    /// مشخصات از دیجی‌کالا (همان کالا)، صفحهٔ تأمین‌کننده و فروشگاه‌های نتایج جست‌وجو؛ ادغام با رأی منابع.
    /// <para>دور ۲۶: درخواست دستی ← فروشگاه‌های بیشتر (۲ برابر) و هرگز «کامل بود» نمی‌گوید: نتیجهٔ تحقیق (چند منبع، چند ردیف) گزارش می‌شود.
    /// کالای هرگز منتشرنشده: ردیف تازه از منبع مطمئن (دیجی‌کالا یا تأمین‌کننده) با یک منبع هم خودکار اضافه می‌شود تا کالا بدون دخالت
    /// انسان به «آمادهٔ انتشار» برسد (مدیر پیش از انتشار همه را می‌بیند). تغییر مقدار فعلی همیشه فقط پیشنهاد است.</para>
    /// </summary>
    private async Task<SmartOutcome> ProductSpecsAsync(Guid productId, bool manual, CancellationToken cancellationToken)
    {
        var subject = await catalog.GetProductAsync(productId, cancellationToken);
        if (subject is null)
            return new SmartOutcome(SmartTaskStatus.Skipped, "محصول پیدا نشد.");
        var options = await config.GetAsync(cancellationToken);
        var observations = new List<SpecObservation>();
        var deep = manual || subject.NeverPublished;

        var hit = await sources.FindDigikalaAsync(subject, cancellationToken);
        if (hit is not null && await sources.DigikalaDetailsAsync(hit.Id, cancellationToken) is { } details)
            observations.AddRange(details.Specs.Select(s => new SpecObservation("digikala", s.Key, s.Value)));
        if (!string.IsNullOrWhiteSpace(subject.SupplierUrl))
        {
            var (specs, _) = await sources.PageAsync(subject.SupplierUrl, cancellationToken);
            observations.AddRange(specs.Select(s => new SpecObservation("supplier", s.Key, s.Value)));
        }
        var trusted = observations.ToList();
        var shops = 0;
        foreach (var url in await sources.SerpShopsAsync(subject.SearchQuery, deep ? SpecShopsMax * 2 : SpecShopsMax, cancellationToken))
        {
            var (specs, _) = await sources.PageAsync(url, cancellationToken);
            var host = new Uri(url).Host;
            if (specs.Count > 0)
                shops++;
            observations.AddRange(specs.Select(s => new SpecObservation(host, s.Key, s.Value)));
        }
        if (observations.Count == 0)
        {
            return new SmartOutcome(SmartTaskStatus.Skipped, manual
                ? "در دیجی‌کالا، صفحهٔ تأمین‌کننده و نتایج جست‌وجو مشخصاتی برای همین کالا پیدا نشد (یا منبع‌ها موقتاً در دسترس نبودند)؛ با نام انگلیسی/مدل دقیق‌تر دوباره امتحان کنید."
                : "هیچ منبعی مشخصات این کالا را نداشت (یا موقتاً در دسترس نبود).");
        }

        var filterKeys = await catalog.FilterKeysAsync(subject.CategoryId, cancellationToken);
        var merged = SpecMerge.Merge(observations)
            .Select(m => m with { Key = SpecMerge.AlignToFilterKey(m.Key, filterKeys) })
            .ToList();
        var suggestions = SpecMerge.Diff(merged, subject.Features.Select(f => (f.Key, f.Value)), Math.Max(2, options.AutoSpecMinSources))
            .Where(s => s.Diff != SpecDiffKind.Same)
            .ToList();
        var auto = suggestions.Where(s => s.AutoApplicable).ToList();
        if (subject.NeverPublished && trusted.Count > 0)
        {
            // ردیف تازه از منبع مطمئن (فقط افزودن؛ هرگز تغییر مقدار فعلی) برای کالای پیش‌نویس.
            var trustedMerged = SpecMerge.Merge(trusted).Select(m => m with { Key = SpecMerge.AlignToFilterKey(m.Key, filterKeys) }).ToList();
            var extra = SpecMerge.Diff(trustedMerged, subject.Features.Select(f => (f.Key, f.Value)), 1)
                .Where(s => s.AutoApplicable && auto.All(a => !string.Equals(a.Key, s.Key, StringComparison.Ordinal)))
                .ToList();
            auto.AddRange(extra);
            suggestions = suggestions.Where(s => extra.All(e => !string.Equals(e.Key, s.Key, StringComparison.Ordinal))).Concat(extra).ToList();
        }
        var result = new SmartTaskResult();
        if (auto.Count > 0)
        {
            var (added, _) = await catalog.ApplySpecsAsync(productId, auto.Select(s => (s.Key, s.Value)).ToList(), new HashSet<string>(), cancellationToken);
            result.SpecsAdded = added;
        }
        result.Specs = suggestions.Except(auto).ToList();
        var summary = $"{Core.Services.Seo.Engine.PersianText.Digits(result.SpecsAdded)} ردیف مشخصات (با توافق چند منبع) اضافه شد؛ {Core.Services.Seo.Engine.PersianText.Digits(result.Specs.Count)} پیشنهاد منتظر تأیید.";
        var status = result.Specs.Count > 0 ? SmartTaskStatus.NeedsReview : result.SpecsAdded > 0 ? SmartTaskStatus.Applied : SmartTaskStatus.Skipped;
        if (status == SmartTaskStatus.Skipped)
        {
            // دور ۲۶: به‌جای «لازم نبود» نتیجهٔ واقعی تحقیق گزارش می‌شود (کار انجام شد؛ همهٔ مقدارها با منابع یکی بود).
            var sourceCount = observations.Select(o => o.Source).Distinct(StringComparer.Ordinal).Count();
            summary = $"تحقیق انجام شد: {PersianText.Digits(merged.Count)} مشخصه از {PersianText.Digits(sourceCount)} منبع بررسی شد و همه با مشخصات فعلی کالا یکی بود (مشخصات تأیید شد).";
            status = manual ? SmartTaskStatus.Applied : SmartTaskStatus.Skipped;
        }
        return new SmartOutcome(status, summary, JsonSerializer.Serialize(result, Json));
    }

    // ───────────────────────────── خلاصهٔ خرید ─────────────────────────────

    private async Task<SmartOutcome> ProductInsightAsync(Guid productId, bool manual, CancellationToken cancellationToken)
    {
        var subject = await catalog.GetProductAsync(productId, cancellationToken);
        if (subject is null)
            return new SmartOutcome(SmartTaskStatus.Skipped, "محصول پیدا نشد.");
        var options = await config.GetAsync(cancellationToken);
        var reviews = await catalog.GetOwnReviewsAsync(productId, cancellationToken);
        var hit = await sources.FindDigikalaAsync(subject, cancellationToken);
        if (hit is not null)
            reviews.AddRange(await sources.DigikalaReviewsAsync(hit.Id, cancellationToken));
        if (reviews.Count < 3)
        {
            // نظر ساختگی هرگز نوشته نمی‌شود؛ پیام روشن می‌گوید چه چیزی کم است.
            return new SmartOutcome(SmartTaskStatus.Skipped, manual
                ? $"برای خلاصهٔ خرید دست‌کم ۳ نظر واقعی لازم است؛ این کالا {PersianText.Digits(reviews.Count)} نظر دارد (سایت ما + دیجی‌کالا). با رسیدن نظرها خودکار ساخته می‌شود."
                : "نظر کافی (دست‌کم ۳) برای جمع‌بندی پیدا نشد.");
        }

        var draft = ReviewSynthesis.Build(new InsightSubject(subject.Id, subject.Name, subject.BrandName, subject.CategoryName, subject.Features), reviews);
        if (!draft.HasContent)
            return new SmartOutcome(SmartTaskStatus.Skipped, "از نظرها نکتهٔ پرتکرار و قابل اتکایی درنیامد.");

        var approve = options.AutoApproveInsightMinReviews > 0 && draft.ReviewCount >= options.AutoApproveInsightMinReviews;
        var saved = await insights.SaveDraftAsync(productId, draft, approve, Actor, cancellationToken);
        if (!saved)
            return new SmartOutcome(SmartTaskStatus.Skipped, "خلاصهٔ تأییدشده توسط کارشناس وجود دارد؛ دست نخورد.");
        if (approve && string.IsNullOrWhiteSpace(subject.ShortDescription) && !string.IsNullOrWhiteSpace(draft.ShortDescription))
            await catalog.FillShortDescriptionAsync(productId, draft.ShortDescription, cancellationToken);
        var result = new SmartTaskResult { Reviews = draft.ReviewCount, InsightApproved = approve };
        return new SmartOutcome(approve ? SmartTaskStatus.Applied : SmartTaskStatus.NeedsReview,
            approve
                ? $"خلاصهٔ خرید از {Core.Services.Seo.Engine.PersianText.Digits(draft.ReviewCount)} نظر ساخته و منتشر شد."
                : $"پیش‌نویس خلاصهٔ خرید از {Core.Services.Seo.Engine.PersianText.Digits(draft.ReviewCount)} نظر آماده است؛ بعد از تأیید شما نمایش داده می‌شود.",
            JsonSerializer.Serialize(result, Json));
    }

    // ───────────────────────────── تکمیل یک‌کلیکی ─────────────────────────────

    private async Task<SmartOutcome> ProductCompleteAsync(Guid productId, bool manual, CancellationToken cancellationToken)
    {
        var subject = await catalog.GetProductAsync(productId, cancellationToken);
        if (subject is null)
            return new SmartOutcome(SmartTaskStatus.Skipped, "محصول پیدا نشد.");
        var options = await config.GetAsync(cancellationToken);
        var merged = new SmartTaskResult();
        var needsReview = false;

        async Task Step(string name, Func<Task<SmartOutcome>> run)
        {
            try
            {
                var outcome = await run();
                merged.Steps.Add($"{name}: {outcome.Summary}");
                needsReview |= outcome.Status == SmartTaskStatus.NeedsReview;
                if (outcome.ResultJson is { } json && JsonSerializer.Deserialize<SmartTaskResult>(json, Json) is { } part)
                {
                    merged.Specs.AddRange(part.Specs);
                    merged.PendingImages.AddRange(part.PendingImages);
                    merged.AddedImages.AddRange(part.AddedImages);
                    merged.SpecsAdded += part.SpecsAdded;
                    merged.Reviews = Math.Max(merged.Reviews, part.Reviews);
                    merged.InsightApproved |= part.InsightApproved;
                    merged.Review ??= part.Review;
                    merged.Identity ??= part.Identity;
                    merged.ImageRound = Math.Max(merged.ImageRound, part.ImageRound);
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning(exception, "Smart complete step {Step} failed for {Product}", name, productId);
                merged.Steps.Add($"{name}: خطا – این مرحله رد شد و بقیه ادامه یافت.");
            }
        }

        // دور ۲۲ – ترتیب «تکمیل هوشمند»: اول دسته (همه‌چیز بعدی از نوع/دستهٔ کالا کمک می‌گیرد)، بعد نام/نام انگلیسی/نامک، عکس، مشخصات،
        // خلاصهٔ خرید، توضیح کوتاه (از مشخصات تازه)، توضیحات کامل، قیمت هوشمند و در آخر سئو (فقط کالای منتشرشده).
        if (subject.CategoryIsSystem || CategoryClassifier.IsCatchAll(subject.CategoryName))
            await Step("دسته", () => CategorizeOneAsync(productId, cancellationToken));
        await Step("نام و نامک", () => IdentityAsync(productId, cancellationToken));
        if (subject.ImageNames.Count < Math.Max(1, options.AutoImagesWhenFewerThan))
            await Step("عکس", () => ProductImagesAsync(productId, manual: false, round: 0, cancellationToken));
        await Step("مشخصات", () => ProductSpecsAsync(productId, manual, cancellationToken));
        await Step("خلاصهٔ خرید", () => ProductInsightAsync(productId, manual, cancellationToken));
        await Step("توضیح کوتاه", () => ShortDescriptionAsync(productId, cancellationToken));
        // توضیحات محصول بعد از مشخصات و خلاصهٔ خرید (از داده‌های تازهٔ آن‌ها استفاده می‌کند) و پیش از سئوی خودکار
        // (تا خلبان عنوان/متا را با متن نهایی بسنجد).
        // دور ۲۶: درخواست دستی و کالای پیش‌نویس (مسیر «آمادهٔ انتشار») همیشه توضیحات می‌گیرند، حتی اگر نوشتن خودکار خاموش باشد.
        if (options.AutoReview || manual || subject.NeverPublished)
            await Step("توضیحات محصول", () => ProductReviewAsync(productId, manual, cancellationToken));
        // دور ۲۸: اگر توضیح کوتاه هنوز خالی است (مشخصات کم بود)، از جملهٔ اول توضیحات تازه ساخته می‌شود.
        if (subject.NeverPublished)
            await Step("توضیح کوتاه (از توضیحات)", () => ShortFromBodyAsync(productId, cancellationToken));
        await Step("قیمت", () => PriceAsync(productId, cancellationToken));
        // خواستهٔ کاربر: فیلدهای سئو بعد از انتشار پر شوند. کالای پیش‌نویس ← سئو منتظر می‌ماند؛ لحظهٔ انتشار، ذخیرهٔ کالا
        // خلبان سئو را خبر می‌کند (SeoChangeFeed) و ورود هوشمند هم کالای تازه‌منتشرشدهٔ بی‌متا را جا نمی‌اندازد.
        if (subject.IsActive)
        {
            try
            {
                await seo.ProcessAsync(SeoEntityKind.Product, productId, cancellationToken);
                merged.Steps.Add("سئو: عنوان، توضیح متا و متن جایگزین بررسی و تکمیل شد.");
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning(exception, "Autopilot SEO failed for {Product}", productId);
            }
        }
        else
        {
            merged.Steps.Add("سئو: کالا هنوز منتشر نشده؛ فیلدهای سئو بعد از انتشار خودکار پر می‌شوند.");
        }
        return new SmartOutcome(needsReview ? SmartTaskStatus.NeedsReview : SmartTaskStatus.Applied, string.Join(" | ", merged.Steps), JsonSerializer.Serialize(merged, Json));
    }

    // ───────────────────────────── دور ۲۲: دسته، هویت، توضیح کوتاه، قیمت ─────────────────────────────

    /// <summary>
    /// کالای «دسته‌بندی نشده/سایر» همین حالا دسته‌بندی می‌شود (همان قواعد بازبینی خودکار؛ دستهٔ تازه فقط اگر واقعاً لازم باشد).
    /// دستهٔ تازه ← «تکمیل دسته» (متن و ۴ تصویر پیشنهادی) در صف. پیدا نشد ← کالا بی‌دسته می‌ماند و قابل انتشار نیست.
    /// </summary>
    private async Task<SmartOutcome> CategorizeOneAsync(Guid productId, CancellationToken cancellationToken)
    {
        var report = await categorizer.RunAsync([productId], 1, cancellationToken);
        foreach (var category in report.Created)
        {
            await queue.EnqueueAsync(SmartTaskKind.CategoryComplete, SmartTarget.Category, category.Id, category.Name, SmartPriority.Batch,
                null, null, Actor, cancellationToken);
        }
        if (report.Moved == 0)
            return new SmartOutcome(SmartTaskStatus.NeedsReview, "دستهٔ مطمئنی پیدا نشد؛ یک دسته انتخاب کنید (کالای بی‌دسته منتشر نمی‌شود).");
        var subject = await catalog.GetProductAsync(productId, cancellationToken);
        return new SmartOutcome(SmartTaskStatus.Applied, $"کالا به دستهٔ «{subject?.CategoryName}» رفت"
            + (report.Created.Count > 0 ? " (دستهٔ تازه)." : report.General > 0 ? " (دستهٔ کلی؛ با کالاهای مشابه بیشتر، دستهٔ اختصاصی ساخته می‌شود)." : "."));
    }

    /// <summary>
    /// نام بهینه، نام انگلیسی و نامک (<see cref="ProductNaming"/>):
    /// <list type="bullet">
    /// <item>کالای هرگز منتشرنشده ← خودکار اعمال (آدرسش هنوز جایی ثبت نشده).</item>
    /// <item>کالای منتشرشده ← نام انگلیسی خالی خودکار پر می‌شود؛ تغییر نام فقط «پیشنهاد» برای تأیید کارشناس؛ نامک هرگز (آدرس گوگل عوض نمی‌شود).</item>
    /// </list>
    /// نام انگلیسی فقط از عنوان انگلیسی واقعی همین کالا در دیجی‌کالا یا واژه‌های لاتین خود نام ساخته می‌شود (هرگز ترجمهٔ حدسی).
    /// </summary>
    private async Task<SmartOutcome> IdentityAsync(Guid productId, CancellationToken cancellationToken)
    {
        var subject = await catalog.GetProductAsync(productId, cancellationToken);
        if (subject is null)
            return new SmartOutcome(SmartTaskStatus.Skipped, "محصول پیدا نشد.");
        var type = ProductTypes.Detect(subject.Name) ?? (subject.CategoryIsSystem ? null : ProductTypes.DetectHead(subject.CategoryName));
        var refined = ProductNaming.Refine(subject.Name, type, subject.BrandEnglishName);
        string? english = null;
        // دور ۲۸: نام انگلیسی خالی یا «نام انگلیسی» بدون حرف لاتین (مثلاً فارسی واردشده از منبع) دوباره ساخته می‌شود.
        if (!ProductNaming.IsUsableEnglish(subject.EnglishName))
        {
            var hit = await sources.FindDigikalaAsync(subject, cancellationToken);
            var known = hit?.EnglishTitle;
            // دور ۲۶: دیجی‌کالا نام انگلیسی نداد ← نام انگلیسی همین کالا در ترب (فقط با شباهت نام ≥ ۷۵٪؛ هرگز ترجمهٔ حدسی).
            if (string.IsNullOrWhiteSpace(known))
                known = await sources.TorobEnglishNameAsync(subject.Name, cancellationToken);
            english = ProductNaming.EnglishName(refined.Name, subject.BrandEnglishName, type, known)
                      // دور ۲۸: هیچ منبعی نام انگلیسی نداد ← از واژه‌نامهٔ داخلی (برند انگلیسی + نوع کالا + واژه‌های شناخته‌شدهٔ نام).
                      ?? ProductNaming.EnglishFromDictionary(refined.Name, subject.BrandEnglishName, type);
        }
        var slug = subject.NeverPublished ? ProductNaming.Slug(refined.Name, english ?? subject.EnglishName) : null;
        if (slug == subject.Slug)
            slug = null;
        if (!refined.Changed && english is null && slug is null)
            return new SmartOutcome(SmartTaskStatus.Skipped, "نام، نام انگلیسی و نامک مناسب بود.");

        var suggestion = new IdentitySuggestion { BaseName = subject.Name, Changes = refined.Changes.ToList() };
        if (subject.NeverPublished)
        {
            var applied = await catalog.ApplyIdentityAsync(productId, refined.Changed ? refined.Name : null, english, slug, subject.Name,
                cancellationToken: cancellationToken);
            suggestion.Applied = true;
            suggestion.Name = applied.Name ? refined.Name : null;
            suggestion.EnglishName = applied.EnglishName ? english : null;
            suggestion.Slug = applied.Slug;
            if (!applied.Any)
                return new SmartOutcome(SmartTaskStatus.Skipped, "تغییری لازم نشد (نام در این فاصله عوض شده بود یا نامک تکراری بود).");
            var parts = new List<string>();
            if (applied.Name)
                parts.Add($"نام ← «{refined.Name}»");
            if (applied.EnglishName)
                parts.Add($"نام انگلیسی ← «{english}»");
            if (applied.Slug is not null)
                parts.Add($"نامک ← {applied.Slug}");
            return new SmartOutcome(SmartTaskStatus.Applied, string.Join("؛ ", parts) + ".", JsonSerializer.Serialize(new SmartTaskResult { Identity = suggestion }, Json));
        }

        // منتشرشده: نام انگلیسی خالی بی‌خطر است (آدرس و عنوان صفحه عوض نمی‌شود)؛ تغییر نام فقط با تأیید.
        var englishApplied = english is not null
                             && (await catalog.ApplyIdentityAsync(productId, null, english, null, subject.Name, cancellationToken: cancellationToken)).EnglishName;
        if (!refined.Changed)
        {
            return englishApplied
                ? new SmartOutcome(SmartTaskStatus.Applied, $"نام انگلیسی ← «{english}».")
                : new SmartOutcome(SmartTaskStatus.Skipped, "نام مناسب بود.");
        }
        suggestion.Name = refined.Name;
        suggestion.EnglishName = englishApplied ? english : null;
        return new SmartOutcome(SmartTaskStatus.NeedsReview,
            $"نام پیشنهادی: «{refined.Name}» ({string.Join("، ", refined.Changes)}); کالا منتشر شده، پس بعد از تأیید شما اعمال می‌شود."
            + (englishApplied ? $" نام انگلیسی «{english}» ثبت شد." : ""),
            JsonSerializer.Serialize(new SmartTaskResult { Identity = suggestion }, Json));
    }

    /// <summary>
    /// توضیح کوتاه (خواستهٔ کاربر: هر کالا باید داشته باشد) فقط از واقعیت‌های ثبت‌شده (<see cref="ShortDescriptionWriter"/>).
    /// توضیح موجود هرگز عوض نمی‌شود؛ واقعیت کافی نبود ← خالی می‌ماند (متن پوچ بدتر از نبودن است).
    /// </summary>
    private async Task<SmartOutcome> ShortDescriptionAsync(Guid productId, CancellationToken cancellationToken)
    {
        var subject = await catalog.GetProductAsync(productId, cancellationToken);
        if (subject is null)
            return new SmartOutcome(SmartTaskStatus.Skipped, "محصول پیدا نشد.");
        if (!string.IsNullOrWhiteSpace(subject.ShortDescription))
            return new SmartOutcome(SmartTaskStatus.Skipped, "توضیح کوتاه دارد.");
        var text = ShortDescriptionWriter.Write(subject.Name, subject.BrandName, subject.CategoryIsSystem ? null : subject.CategoryName, subject.Features,
            subject.WarrantyTitle);
        if (text is null || !await catalog.FillShortDescriptionAsync(productId, text, cancellationToken))
            return new SmartOutcome(SmartTaskStatus.Skipped, "مشخصات کافی برای نوشتن توضیح کوتاه واقعی نبود؛ بعد از تکمیل مشخصات دوباره امتحان می‌شود.");
        return new SmartOutcome(SmartTaskStatus.Applied, $"«{text}»");
    }

    /// <summary>
    /// دور ۲۸: توضیح کوتاه از دو جملهٔ اول توضیحات کامل (وقتی مشخصات برای <see cref="ShortDescriptionWriter"/> کم بود).
    /// متن از همان توضیحاتی است که روی صفحه هست، پس چیز ساختگی اضافه نمی‌شود. توضیح موجود هرگز عوض نمی‌شود.
    /// </summary>
    private async Task<SmartOutcome> ShortFromBodyAsync(Guid productId, CancellationToken cancellationToken)
    {
        var subject = await catalog.GetProductAsync(productId, cancellationToken);
        if (subject is null || !string.IsNullOrWhiteSpace(subject.ShortDescription))
            return new SmartOutcome(SmartTaskStatus.Skipped, "توضیح کوتاه دارد.");
        var state = await catalog.GetBodyStateAsync(productId, cancellationToken);
        var text = ShortFromBody(state?.Html);
        if (text is null || !await catalog.FillShortDescriptionAsync(productId, text, cancellationToken))
            return new SmartOutcome(SmartTaskStatus.Skipped, "توضیحات کامل هنوز نوشته نشده؛ دور بعد دوباره امتحان می‌شود.");
        return new SmartOutcome(SmartTaskStatus.Applied, $"«{text}»");
    }

    /// <summary>
    /// دور ۲۸ (خالص؛ آزمون‌پذیر): یک یا دو جملهٔ اول متن ساده‌شدهٔ HTML تا ۲۵۰ نویسه. کمتر از ۲۰ نویسه ← null.
    /// مثال: «&lt;h2&gt;معرفی&lt;/h2&gt;&lt;p&gt;این کتری برقی ۱٫۷ لیتری است. بدنهٔ استیل دارد. …&lt;/p&gt;» ← «این کتری برقی ۱٫۷ لیتری است. بدنهٔ استیل دارد.»
    /// </summary>
    public static string? ShortFromBody(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return null;
        // تیترها (h1 تا h4) جمله نیستند؛ پیش از ساده‌سازی حذف می‌شوند.
        var withoutHeadings = System.Text.RegularExpressions.Regex.Replace(html, @"<h[1-4][^>]*>.*?</h[1-4]>", " ",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Singleline, TimeSpan.FromMilliseconds(200));
        var plain = PersianText.HtmlToText(withoutHeadings);
        plain = System.Text.RegularExpressions.Regex.Replace(plain, @"\s+", " ").Trim();
        if (plain.Length < 20)
            return null;
        var sentences = System.Text.RegularExpressions.Regex.Split(plain, @"(?<=[.!؟?])\s+");
        var text = "";
        foreach (var sentence in sentences)
        {
            var next = (text + " " + sentence).Trim();
            if (next.Length > 250)
                break;
            text = next;
            if (text.Length >= 80)
                break;
        }
        if (text.Length < 20)
        {
            var cut = plain.Length > 250 ? plain.LastIndexOf(' ', 249) : plain.Length;
            text = plain.Length > 250 ? plain[..(cut >= 20 ? cut : 249)].TrimEnd('،', ',') + "…" : plain;
        }
        return text.Length >= 20 ? text : null;
    }

    /// <summary>
    /// قیمت هوشمند: فقط برای کالای هرگز منتشرنشده که قیمت خرید دارد و قبلاً قیمت‌گذاری بازار نشده (کالای منتشرشده قیمتش را مدیر
    /// تعیین کرده و «مقایسهٔ قیمت رقبا» جداگانه پیشنهاد می‌دهد). همان صف قیمت‌گذاری درون‌ریزی: قیمت بازار با محافظ سود.
    /// </summary>
    private async Task<SmartOutcome> PriceAsync(Guid productId, CancellationToken cancellationToken)
    {
        var subject = await catalog.GetProductAsync(productId, cancellationToken);
        if (subject is null)
            return new SmartOutcome(SmartTaskStatus.Skipped, "محصول پیدا نشد.");
        if (!subject.NeverPublished)
            return new SmartOutcome(SmartTaskStatus.Skipped, "کالا منتشر شده؛ قیمت فقط با «مقایسهٔ قیمت رقبا» پیشنهاد می‌شود.");
        if (subject.PurchasePrice is not > 0m)
            return new SmartOutcome(SmartTaskStatus.Skipped, "قیمت خرید ثبت نشده؛ قیمت‌گذاری هوشمند بدون آن انجام نمی‌شود (محافظ سود).");
        if (await catalog.HasMarketPriceCheckAsync(productId, cancellationToken))
            return new SmartOutcome(SmartTaskStatus.Skipped, "قیمت بازار قبلاً بررسی شده (یا در صف است).");
        await pricing.EnqueueAsync(productId, subject.SearchQuery, subject.Price, subject.PurchasePrice, cancellationToken);
        return new SmartOutcome(SmartTaskStatus.Applied, "بررسی قیمت بازار (ترب و دیجی‌کالا) در صف رفت؛ قیمت با حفظ سود تنظیم می‌شود.");
    }

    // ───────────────────────────── توضیحات محصول ─────────────────────────────

    /// <summary>
    /// تصمیم دربارهٔ متن فعلی صفحه (خالص؛ آزمون‌پذیر). ترتیب مهم است:
    /// <list type="number">
    /// <item>آخرین توضیحات محصول هوشمند را کارشناس «برگردانده» ← Skip (تصمیم انسان محترم است).</item>
    /// <item>متن فعلی دقیقاً همان نوشتهٔ دستیار است ← Regenerate.</item>
    /// <item>نشانه هست ولی متن عوض شده (کارشناس ویرایش کرده) ← Skip.</item>
    /// <item>کمتر از ۱۵۰ واژه ← Write (متن فعلی بالای بررسی می‌ماند).</item>
    /// <item>متن انسانی بلند ← Propose (فقط پیشنهاد).</item>
    /// </list>
    /// </summary>
    /// <param name="state">وضعیت متن فعلی صفحه.</param>
    /// <param name="manual">دور ۲۶: مدیر خودش خواسته ← هرگز Skip نیست؛ متن برگردانده/ویرایش‌شده ← Propose (نسخهٔ تازه فقط پیشنهاد می‌شود).</param>
    public static (ReviewAction Action, string Reason) DecideReview(ProductBodyState state, bool manual = false)
    {
        if (state.RolledBack)
        {
            return manual
                ? (ReviewAction.Propose, "به درخواست شما نسخهٔ تازه نوشته شد؛ چون قبلاً متن هوشمند را برگردانده بودید، فقط پیشنهاد است و متن فعلی دست نمی‌خورد.")
                : (ReviewAction.Skip, "کارشناس آخرین «توضیحات محصول» هوشمند را برگردانده است؛ دستیار دوباره چیزی نمی‌نویسد.");
        }
        if (state.WrittenBySmart)
            return (ReviewAction.Regenerate, "متن فعلی را دستیار نوشته؛ با داده‌های تازه به‌روز می‌شود.");
        if (state.HasMarker)
        {
            return manual
                ? (ReviewAction.Propose, "به درخواست شما نسخهٔ تازه نوشته شد؛ متن ویرایش‌شدهٔ کارشناس تا تأیید شما دست نمی‌خورد.")
                : (ReviewAction.Skip, "متن «توضیحات محصول» را کارشناس ویرایش کرده است؛ دستیار به آن دست نمی‌زند.");
        }
        if (state.Words < ThinBodyWords)
            return (ReviewAction.Write, "توضیحات کالا کم است؛ توضیحات محصول حرفه‌ای اضافه می‌شود.");
        return (ReviewAction.Propose, "کالا توضیحات انسانی بلند دارد؛ توضیحات محصول فقط پیشنهاد می‌شود.");
    }

    /// <summary>
    /// نوشتن «توضیحات محصول» HTML حرفه‌ای (تیتر، جدول مشخصات، فهرست مزایا/معایب، کلیدواژهٔ پررنگ …) با موتور سئوی داخلی.
    /// اعمال خودکار فقط وقتی: تصمیم Write/Regenerate باشد و پیش‌نویس از همهٔ دروازه‌های ضد جریمه بگذرد (Safe). وگرنه پیش‌نمایش برای کارشناس.
    /// خلاصهٔ خرید تأییدشده (مزایا/معایب از نظر خریداران) هم در متن می‌آید.
    /// </summary>
    private async Task<SmartOutcome> ProductReviewAsync(Guid productId, bool manual, CancellationToken cancellationToken)
    {
        var state = await catalog.GetBodyStateAsync(productId, cancellationToken);
        if (state is null)
            return new SmartOutcome(SmartTaskStatus.Skipped, "محصول پیدا نشد.");
        var (action, reason) = DecideReview(state, manual);
        if (action == ReviewAction.Skip)
            return new SmartOutcome(SmartTaskStatus.Skipped, reason);

        var keep = action switch
        {
            ReviewAction.Write => string.IsNullOrWhiteSpace(state.Html) ? null : state.Html,
            ReviewAction.Regenerate => ContentWriter.HumanPart(state.Html),
            _ => null,
        };
        var notes = await NotesAsync(productId, cancellationToken);
        var written = await engine.WriteProductReviewAsync(productId, keep, notes, cancellationToken);
        if ((!written.IsSuccess() || written.Data is null) && manual)
        {
            // دور ۲۶: مشخصات برای نوشتن کم بود ← اول تحقیق مشخصات (عمیق) و یک بار دیگر نوشتن.
            await ProductSpecsAsync(productId, manual: true, cancellationToken);
            written = await engine.WriteProductReviewAsync(productId, keep, notes, cancellationToken);
        }
        if (!written.IsSuccess() || written.Data is not { } draft)
            return new SmartOutcome(SmartTaskStatus.Skipped, written.Message ?? "نوشتن توضیحات محصول ممکن نشد.");
        var preview = new ReviewPreview
        {
            Html = draft.Html, Words = draft.Words, CurrentWords = state.Words, ScoreBefore = draft.ScoreBefore, ScoreAfter = draft.ScoreAfter,
            Natural = draft.NaturalScore, Safe = draft.Safe, Problems = draft.Problems.ToList(),
            Mode = action == ReviewAction.Propose ? "choose" : "replace",
            BaseHash = PersianText.Hash(state.Html),
        };
        var result = new SmartTaskResult { Review = preview };

        // دور ۲۸: کالای هرگز منتشرنشده (مسیر «آمادهٔ انتشار») متن را حتی اگر از همهٔ دروازه‌های ضد جریمه نگذشته باشد می‌گیرد؛
        // مشتری و گوگل هنوز صفحه را نمی‌بینند و مدیر پیش از انتشار بازبینی می‌کند. مشکل‌ها در نتیجهٔ کار ثبت می‌شوند.
        var neverPublished = (await catalog.GetProductAsync(productId, cancellationToken))?.NeverPublished == true;
        if (action != ReviewAction.Propose && (draft.Safe || neverPublished))
        {
            var applied = await seo.ApplyBodyAsync(productId, draft.Html,
                $"توضیحات محصول هوشمند ({PersianText.Digits(draft.Words)} واژه)", Actor, cancellationToken);
            if (applied.IsSuccess())
            {
                if (!applied.Data)
                {
                    // متن تازه با داده‌های فعلی دقیقاً همان متن روی صفحه شد (کار انجام شده؛ چیزی برای جایگزینی نبود).
                    return new SmartOutcome(manual ? SmartTaskStatus.Applied : SmartTaskStatus.Skipped,
                        $"توضیحات محصول دوباره با تازه‌ترین داده‌ها نوشته شد ({PersianText.Digits(draft.Words)} واژه) و با متن روی صفحه یکی بود.");
                }
                preview.Applied = true;
                preview.Html = "";
                return new SmartOutcome(SmartTaskStatus.Applied,
                    $"توضیحات محصول حرفه‌ای ({PersianText.Digits(draft.Words)} واژه، امتیاز سئو {PersianText.Digits(draft.ScoreBefore)} ← {PersianText.Digits(draft.ScoreAfter)}) روی صفحه نشست؛ از «تاریخچهٔ سئو» برگشت‌پذیر است.",
                    JsonSerializer.Serialize(result, Json));
            }
            preview.Problems.Add(applied.Message ?? "ذخیره ممکن نشد.");
        }
        var why = action == ReviewAction.Propose ? reason : "پیش‌نویس از همهٔ بررسی‌های ضد جریمه نگذشت؛ بعد از دیدن شما اعمال می‌شود.";
        return new SmartOutcome(SmartTaskStatus.NeedsReview, $"پیش‌نویس توضیحات محصول ({PersianText.Digits(draft.Words)} واژه) آماده است. {why}",
            JsonSerializer.Serialize(result, Json));
    }

    /// <summary>
    /// داده‌های کمکی «توضیحات محصول» (دور ۲۱): تحقیق در سایت‌های دیگر (<see cref="SmartSources.ResearchDocsAsync"/> ←
    /// <see cref="ProductResearch.Analyze"/>) + نکته‌های خلاصهٔ خرید «تأییدشده» (پیش‌نویس تأییدنشده هرگز در متن عمومی نمی‌آید).
    /// <para>هیچ جمله‌ای از سایت دیگر کپی نمی‌شود؛ فقط ترتیب موضوع‌ها، اقلام جعبه، کاربردها و ویژگی‌های مورد توافق.</para>
    /// </summary>
    private async Task<ReviewNotes?> NotesAsync(Guid productId, CancellationToken cancellationToken)
    {
        ReviewNotes? buyers = null;
        var insight = await insights.GetApprovedAsync(productId, cancellationToken);
        if (insight is not null && (insight.Pros.Count > 0 || insight.Cons.Count > 0 || insight.ForWhom.Count > 0 || !string.IsNullOrWhiteSpace(insight.Verdict)))
        {
            buyers = new ReviewNotes
            {
                Pros = insight.Pros.Select(p => p.Text).ToList(),
                Cons = insight.Cons.Select(p => p.Text).ToList(),
                ForWhom = insight.ForWhom,
                Verdict = insight.Verdict,
                ReviewCount = insight.ReviewCount,
            };
        }

        var digest = ResearchDigest.Empty;
        if (await catalog.GetProductAsync(productId, cancellationToken) is { } subject)
            digest = ProductResearch.Analyze(await sources.ResearchDocsAsync(subject, SpecShopsMax, cancellationToken));
        return digest.Sources == 0 ? buyers : ProductResearch.ToNotes(digest, buyers);
    }

    /// <summary>خواندن یک عدد صحیح از JSON بار کار. مثال: ({"round":2}, "round") ← ۲؛ JSON خراب یا نبود ← ۰.</summary>
    public static int PayloadInt(string? payloadJson, string name)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
            return 0;
        try
        {
            using var doc = JsonDocument.Parse(payloadJson);
            return doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty(name, out var value)
                   && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) ? number : 0;
        }
        catch (JsonException)
        {
            return 0;
        }
    }

    // ───────────────────────────── دسته ─────────────────────────────

    /// <summary>
    /// تکمیل دسته: توضیح و پرسش‌های رایج از دادهٔ واقعی (متن موجود هرگز جایگزین نمی‌شود) + دور ۲۲: «۴ تصویر پیشنهادی».
    /// <list type="bullet">
    /// <item>بار اول (فهرست پیشنهاد هنوز ساخته نشده): ۴ بنر از عکس کالاهای دسته؛ اگر دسته تصویر ندارد اولی خودکار تصویر دسته می‌شود.</item>
    /// <item>ادمین همه را حذف کرده (فهرست خالی) ← دیگر خودکار ساخته نمی‌شود (تصمیم انسان محترم است).</item>
    /// <item>بار «پیشنهاد دوباره» (payload {"images":1}): فقط تصویرها؛ تصویر انتخاب‌شده می‌ماند و بقیه با گزینه‌های تازه عوض می‌شوند
    /// (فایل‌های قبلی انتخاب‌نشده پاک می‌شوند). گزینهٔ تازه‌ای نبود ← هیچ چیز عوض نمی‌شود.</item>
    /// </list>
    /// </summary>
    private async Task<SmartOutcome> CategoryAsync(Guid categoryId, string? payloadJson, CancellationToken cancellationToken)
    {
        var subject = await catalog.GetCategoryAsync(categoryId, cancellationToken);
        if (subject is null)
            return new SmartOutcome(SmartTaskStatus.Skipped, "دسته پیدا نشد.");
        var imagesOnly = PayloadInt(payloadJson, "images") == 1;
        var text = !imagesOnly && (string.IsNullOrWhiteSpace(subject.Description) || string.IsNullOrWhiteSpace(subject.FaqJson)) ? CategoryWriter.Write(subject) : null;
        var added = await CategoryImagesAsync(subject, imagesOnly, cancellationToken);
        if (text is null && added == 0)
        {
            return new SmartOutcome(SmartTaskStatus.Skipped, imagesOnly
                ? "عکس فضاسازی مناسبی در اینترنت پیدا نشد (عکس‌های بی‌روح، لوگو، آیکن و عکس کالاهای خود دسته کنار گذاشته می‌شوند)؛ بعداً دوباره امتحان می‌شود."
                : subject.ProductCount < 3 ? "دسته کمتر از ۳ کالای موجود دارد؛ متن نوشته نشد و عکس فضاسازی هم از قبل بود (یا پیدا نشد)." : "توضیح و تصویرهای دسته از قبل کامل بود.");
        }
        var changed = text is null ? 0 : await catalog.ApplyCategoryAsync(categoryId, text.Html, null, text.FaqJson, overwrite: false, cancellationToken);
        if (changed > 0 || added > 0)
        {
            try
            {
                await seo.ProcessAsync(SeoEntityKind.Category, categoryId, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning(exception, "Autopilot SEO failed for category {Category}", categoryId);
            }
        }
        var parts = new List<string>();
        if (text is not null && changed > 0)
            parts.Add($"توضیح {PersianText.Digits(text.Words)} کلمه‌ای و پرسش‌های رایج ذخیره شد");
        if (added > 0)
            parts.Add($"{PersianText.Digits(added)} عکس فضاسازی از اینترنت پیدا و ذخیره شد (یکی فعال است؛ از «تکمیل هوشمند دسته‌ها» عوض یا حذف کنید)");
        return new SmartOutcome(parts.Count > 0 ? SmartTaskStatus.Applied : SmartTaskStatus.Skipped, parts.Count > 0 ? string.Join("؛ ", parts) + "." : "تغییری لازم نشد.");
    }

    /// <summary>ساخت/تازه کردن تصویرهای پیشنهادی یک دسته (جزئیات در <see cref="CategoryAsync"/>). خروجی: تعداد تصویر تازه.</summary>
    private async Task<int> CategoryImagesAsync(SmartCategorySubject subject, bool resuggest, CancellationToken cancellationToken)
    {
        var state = await catalog.GetCategoryImagesAsync(subject.Id, cancellationToken);
        if (state is null)
            return 0;
        // دور ۲۶: فهرستی که هنوز عکس فضاسازی ندارد (فقط بنرهای قدیمیِ ساخته‌شده از عکس کالا) هم دوباره پیشنهاد می‌گیرد.
        // دور ۲۸: فقط عکس «سوژهٔ تأییدشده» (-photo-w-) کافی است؛ عکس فضاسازی قدیمی یک بار بازبینی و جایگزین می‌شود.
        if (!resuggest && state.Options is not null && state.Options.Any(CategorySubjectImages.IsSubject))
            return 0;
        var previous = state.Options ?? [];
        var keep = new List<string>();
        if (resuggest && state.ImageName is { } chosen && previous.Contains(chosen, StringComparer.Ordinal) && CategorySubjectImages.IsSubject(chosen))
            keep.Add(chosen);
        var photoSubject = new CategoryPhotoSubject(subject.Id, subject.Name, subject.Slug, subject.ParentName, subject.ParentSlug);
        var fresh = await images.CategoryPhotosAsync(photoSubject, subject.ProductImageNames, CategoryImages.Max - keep.Count, previous, cancellationToken);
        if (fresh.Count == 0)
        {
            // بار اول و هیچ بنری ساخته نشد ← فهرست را دست نمی‌زنیم تا دفعهٔ بعد (با عکس‌های بیشتر) دوباره امتحان شود.
            return 0;
        }
        var all = keep.Concat(fresh).ToList();
        if (!await catalog.SaveCategoryImageOptionsAsync(subject.Id, all, cancellationToken))
        {
            images.DeleteCategoryFiles(fresh);
            return 0;
        }
        // تصویر فعلی فقط اگر بنر خودکار قدیمی بوده و حالا عکس فضاسازی جایش نشسته پاک می‌شود (SaveCategoryImageOptionsAsync).
        var current = (await catalog.GetCategoryImagesAsync(subject.Id, cancellationToken))?.ImageName;
        var stale = previous.Where(o => !all.Contains(o, StringComparer.Ordinal) && !string.Equals(o, current, StringComparison.Ordinal)).ToList();
        if (stale.Count > 0)
            images.DeleteCategoryFiles(stale);
        return fresh.Count;
    }

    // ───────────────────────────── مقاله ─────────────────────────────

    private async Task<SmartOutcome> BlogArticleAsync(Guid planId, CancellationToken cancellationToken)
    {
        var row = await plan.GetAsync(planId, cancellationToken);
        if (row is null || row.Status is BlogPlanStatus.Published or BlogPlanStatus.Skipped)
            return new SmartOutcome(SmartTaskStatus.Skipped, "ردیف تقویم پیدا نشد یا قبلاً انجام شده است.");
        await plan.MarkAsync(planId, BlogPlanStatus.Writing, cancellationToken: cancellationToken);
        try
        {
            var options = await config.GetAsync(cancellationToken);
            var products = await catalog.GetBlogProductsAsync(row.CategoryId, BlogPlanService.ParseIds(row.ProductIds), 8, cancellationToken);
            // دور ۲۶: مقالهٔ دسته/کمپین/خبر/نگهداری با ۱ کالای موجود هم نوشته می‌شود؛ راهنمای خرید و فهرست ۲ و مقایسه ۲.
            var minimum = row.Type switch
            {
                BlogArticleType.Comparison or BlogArticleType.BuyingGuide or BlogArticleType.BestPicks => 2,
                _ => 1,
            };
            if (products.Count(p => p.InStock) < minimum)
            {
                await plan.MarkAsync(planId, BlogPlanStatus.Skipped, note: "کالای موجود کافی برای این مقاله نبود.", cancellationToken: cancellationToken);
                return new SmartOutcome(SmartTaskStatus.Skipped, "کالای موجود کافی برای این مقاله نبود.");
            }
            var category = row.CategoryId is { } cid ? await catalog.GetCategoryAsync(cid, cancellationToken) : null;
            // دور ۲۶ – تحقیق پیش از نوشتن: جست‌وجوهای واقعی مرتبط مردم (پیشنهاد جست‌وجوی موتورها، کش ۷ روزه) برای بخش
            // «دیگران چه جست‌وجو می‌کنند» و آدرس صفحهٔ کمپین برای مقالهٔ کمپین.
            var related = await sources.RelatedSearchesAsync(row.Keyword ?? category?.Name ?? row.Topic, cancellationToken);
            var campaignUrl = row.Type == BlogArticleType.Campaign ? await catalog.CampaignUrlAsync(row.Topic, cancellationToken) : null;
            var article = BlogWriter.Write(new BlogBrief(row.Type, row.Topic, row.Keyword, category?.Name, category?.Slug, products, DateTimeOffset.UtcNow,
                CustomTitle: row.Origin == "manual" ? row.Topic : null, Related: related, LinkUrl: campaignUrl));
            var (titles, hashes) = await catalog.ExistingPostsAsync(cancellationToken);
            var report = BlogWriter.Check(article, titles, products.Select(p => p.Name), hashes, Math.Clamp(options.BlogQualityMin, 50, 100));
            var publish = report.Passed && options.BlogAutoPublish;

            string? image = null;
            var first = await catalog.GetProductAsync(products.First(p => p.InStock).Id, cancellationToken);
            if (first?.ImageNames.FirstOrDefault() is { } productImage)
                image = await images.BannerFromProductImageAsync(productImage, UploadArea.Blogs, cancellationToken);

            var postId = await catalog.CreatePostAsync(article, image, image is null ? null : article.Title, publish, null, cancellationToken);
            try
            {
                await seo.ProcessAsync(SeoEntityKind.BlogPost, postId, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning(exception, "Autopilot SEO failed for post {Post}", postId);
            }
            var note = report.Passed ? null : string.Join("؛ ", report.Problems.Take(4));
            await plan.MarkAsync(planId, publish ? BlogPlanStatus.Published : BlogPlanStatus.Drafted, postId, report.Score, note, cancellationToken);
            var result = new SmartTaskResult { QualityScore = report.Score, Problems = report.Problems.ToList() };
            return new SmartOutcome(publish ? SmartTaskStatus.Applied : SmartTaskStatus.NeedsReview,
                publish
                    ? $"مقالهٔ «{article.Title}» با امتیاز کیفیت {Core.Services.Seo.Engine.PersianText.Digits(report.Score)} منتشر شد."
                    : $"مقالهٔ «{article.Title}» به‌صورت پیش‌نویس ذخیره شد (امتیاز {Core.Services.Seo.Engine.PersianText.Digits(report.Score)})؛ بعد از بازبینی منتشر کنید.",
                JsonSerializer.Serialize(result, Json));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Writing planned article {Plan} failed", planId);
            await plan.MarkAsync(planId, BlogPlanStatus.Failed, note: "خطا در نوشتن؛ دوباره تلاش می‌شود.", cancellationToken: cancellationToken);
            throw;
        }
    }

    private async Task<SmartOutcome> BlogImageAsync(Guid postId, string? payloadJson, CancellationToken cancellationToken)
    {
        var post = await catalog.GetPostAsync(postId, cancellationToken);
        if (post is null)
            return new SmartOutcome(SmartTaskStatus.Skipped, "مقاله پیدا نشد.");
        if (!string.IsNullOrWhiteSpace(post.Value.ImageName))
            return new SmartOutcome(SmartTaskStatus.Skipped, "مقاله تصویر دارد.");
        Guid? productId = null;
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(payloadJson) ? "{}" : payloadJson);
            if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("productId", out var p) && p.ValueKind == JsonValueKind.String && p.TryGetGuid(out var g))
                productId = g;
        }
        catch (JsonException)
        {
            // بدون محصول مرجع.
        }
        if (productId is null || await catalog.GetProductAsync(productId.Value, cancellationToken) is not { } product || product.ImageNames.Count == 0)
            return new SmartOutcome(SmartTaskStatus.Skipped, "کالای مرجعی با تصویر برای ساخت بنر مقاله مشخص نشده است.");
        var image = await images.BannerFromProductImageAsync(product.ImageNames[0], UploadArea.Blogs, cancellationToken);
        if (image is null)
            return new SmartOutcome(SmartTaskStatus.Failed, "ساخت بنر ممکن نشد.");
        await catalog.SetPostImageAsync(postId, image, post.Value.Title, cancellationToken);
        return new SmartOutcome(SmartTaskStatus.Applied, "بنر بدون متن برای مقاله ساخته و ثبت شد.");
    }
}
