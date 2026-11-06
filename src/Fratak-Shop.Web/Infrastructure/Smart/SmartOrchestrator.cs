using System.Text.Json;
using Fratak_Shop.Core.Services.Seo.Autopilot;
using Fratak_Shop.Core.Services.Smart;
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
}

/// <summary>
/// مغز اجرای کارهای صف هوشمند (B1–B21): هر کار را بر اساس نوعش انجام می‌دهد و فقط چیزهای «مطمئن» را خودکار اعمال می‌کند.
/// <list type="bullet">
/// <item><b>عکس کالا</b>: عکس‌های منابع مطمئن تا سقف تنظیمات خودکار؛ عکس‌های جست‌وجوی عمومی فقط پیشنهاد.</item>
/// <item><b>مشخصات</b>: ادغام دیجی‌کالا + صفحهٔ تأمین‌کننده + دو فروشگاه برتر؛ ردیف تازه‌ای که دست‌کم ۲ منبع روی آن توافق دارند خودکار،
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
    SmartSources sources,
    SmartImageFinder images,
    ILogger<SmartOrchestrator> logger)
{
    public const string Actor = "دستیار هوشمند";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<SmartOutcome> RunAsync(ClaimedSmartTask task, CancellationToken cancellationToken)
    {
        return task.Kind switch
        {
            SmartTaskKind.ProductImages when task.TargetId is { } id => await ProductImagesAsync(id, manual: true, cancellationToken),
            SmartTaskKind.ProductSpecs when task.TargetId is { } id => await ProductSpecsAsync(id, cancellationToken),
            SmartTaskKind.ProductInsight when task.TargetId is { } id => await ProductInsightAsync(id, cancellationToken),
            SmartTaskKind.ProductComplete when task.TargetId is { } id => await ProductCompleteAsync(id, cancellationToken),
            SmartTaskKind.CategoryComplete when task.TargetId is { } id => await CategoryAsync(id, cancellationToken),
            SmartTaskKind.BlogArticle when task.TargetId is { } id => await BlogArticleAsync(id, cancellationToken),
            SmartTaskKind.BlogImage when task.TargetId is { } id => await BlogImageAsync(id, task.PayloadJson, cancellationToken),
            _ => new SmartOutcome(SmartTaskStatus.Skipped, "نوع کار یا هدف نامعتبر است."),
        };
    }

    // ───────────────────────────── عکس کالا ─────────────────────────────

    private async Task<SmartOutcome> ProductImagesAsync(Guid productId, bool manual, CancellationToken cancellationToken)
    {
        var subject = await catalog.GetProductAsync(productId, cancellationToken);
        if (subject is null)
            return new SmartOutcome(SmartTaskStatus.Skipped, "محصول پیدا نشد.");
        var options = await config.GetAsync(cancellationToken);
        var want = manual ? 6 : Math.Max(1, options.AutoImagesMax);
        var found = await images.FindForProductAsync(subject, want, cancellationToken);
        var result = new SmartTaskResult();
        var auto = found.Where(f => f.Trusted).Take(Math.Max(1, options.AutoImagesMax)).ToList();
        var pending = found.Except(auto).ToList();
        if (auto.Count > 0)
        {
            var alts = auto.Select((f, i) => (f.FileName, Alt: i == 0 ? subject.Name : $"{subject.Name} – نمای {Core.Services.Seo.Engine.PersianText.Digits(i + 1)}")).ToList();
            var added = await catalog.AddProductImagesAsync(productId, alts, cancellationToken);
            result.AddedImages.AddRange(auto.Take(added));
            if (added < auto.Count)
                images.DeleteProductFiles(auto.Skip(added).Select(f => f.FileName));
        }
        result.PendingImages.AddRange(pending);
        var summary = found.Count == 0
            ? "عکس مناسبی (≥ ۶۰۰ پیکسل، غیرتکراری) پیدا نشد."
            : $"{Core.Services.Seo.Engine.PersianText.Digits(result.AddedImages.Count)} عکس اضافه شد؛ {Core.Services.Seo.Engine.PersianText.Digits(pending.Count)} عکس منتظر انتخاب شماست.";
        var status = pending.Count > 0 ? SmartTaskStatus.NeedsReview : found.Count == 0 ? SmartTaskStatus.Skipped : SmartTaskStatus.Applied;
        return new SmartOutcome(status, summary, JsonSerializer.Serialize(result, Json));
    }

    // ───────────────────────────── مشخصات ─────────────────────────────

    private async Task<SmartOutcome> ProductSpecsAsync(Guid productId, CancellationToken cancellationToken)
    {
        var subject = await catalog.GetProductAsync(productId, cancellationToken);
        if (subject is null)
            return new SmartOutcome(SmartTaskStatus.Skipped, "محصول پیدا نشد.");
        var options = await config.GetAsync(cancellationToken);
        var observations = new List<SpecObservation>();

        var hit = await sources.FindDigikalaAsync(subject, cancellationToken);
        if (hit is not null && await sources.DigikalaDetailsAsync(hit.Id, cancellationToken) is { } details)
            observations.AddRange(details.Specs.Select(s => new SpecObservation("digikala", s.Key, s.Value)));
        if (!string.IsNullOrWhiteSpace(subject.SupplierUrl))
        {
            var (specs, _) = await sources.PageAsync(subject.SupplierUrl, cancellationToken);
            observations.AddRange(specs.Select(s => new SpecObservation("supplier", s.Key, s.Value)));
        }
        foreach (var url in await sources.SerpShopsAsync(subject.SearchQuery, 2, cancellationToken))
        {
            var (specs, _) = await sources.PageAsync(url, cancellationToken);
            var host = new Uri(url).Host;
            observations.AddRange(specs.Select(s => new SpecObservation(host, s.Key, s.Value)));
        }
        if (observations.Count == 0)
            return new SmartOutcome(SmartTaskStatus.Skipped, "هیچ منبعی مشخصات این کالا را نداشت (یا موقتاً در دسترس نبود).");

        var filterKeys = await catalog.FilterKeysAsync(subject.CategoryId, cancellationToken);
        var merged = SpecMerge.Merge(observations)
            .Select(m => m with { Key = SpecMerge.AlignToFilterKey(m.Key, filterKeys) })
            .ToList();
        var suggestions = SpecMerge.Diff(merged, subject.Features.Select(f => (f.Key, f.Value)), Math.Max(2, options.AutoSpecMinSources))
            .Where(s => s.Diff != SpecDiffKind.Same)
            .ToList();
        var auto = suggestions.Where(s => s.AutoApplicable).ToList();
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
            summary = "مشخصات فعلی کامل بود؛ چیز تازه‌ای پیدا نشد.";
        return new SmartOutcome(status, summary, JsonSerializer.Serialize(result, Json));
    }

    // ───────────────────────────── خلاصهٔ خرید ─────────────────────────────

    private async Task<SmartOutcome> ProductInsightAsync(Guid productId, CancellationToken cancellationToken)
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
            return new SmartOutcome(SmartTaskStatus.Skipped, "نظر کافی (دست‌کم ۳) برای جمع‌بندی پیدا نشد.");

        var draft = ReviewSynthesis.Build(new InsightSubject(subject.Id, subject.Name, subject.BrandName, subject.CategoryName, subject.Features), reviews);
        if (!draft.HasContent)
            return new SmartOutcome(SmartTaskStatus.Skipped, "از نظرها نکتهٔ پرتکرار و قابل اتکایی درنیامد.");
        if (!string.IsNullOrWhiteSpace(draft.Summary))
            draft.Summary = await sources.PolishAsync(draft.Summary, cancellationToken);

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

    private async Task<SmartOutcome> ProductCompleteAsync(Guid productId, CancellationToken cancellationToken)
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
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning(exception, "Smart complete step {Step} failed for {Product}", name, productId);
                merged.Steps.Add($"{name}: خطا – این مرحله رد شد و بقیه ادامه یافت.");
            }
        }

        if (subject.ImageNames.Count < Math.Max(1, options.AutoImagesWhenFewerThan))
            await Step("عکس", () => ProductImagesAsync(productId, manual: false, cancellationToken));
        await Step("مشخصات", () => ProductSpecsAsync(productId, cancellationToken));
        await Step("خلاصهٔ خرید", () => ProductInsightAsync(productId, cancellationToken));
        try
        {
            await seo.ProcessAsync(SeoEntityKind.Product, productId, cancellationToken);
            merged.Steps.Add("سئو: عنوان، توضیح متا و متن جایگزین بررسی و تکمیل شد.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Autopilot SEO failed for {Product}", productId);
        }
        return new SmartOutcome(needsReview ? SmartTaskStatus.NeedsReview : SmartTaskStatus.Applied, string.Join(" | ", merged.Steps), JsonSerializer.Serialize(merged, Json));
    }

    // ───────────────────────────── دسته ─────────────────────────────

    private async Task<SmartOutcome> CategoryAsync(Guid categoryId, CancellationToken cancellationToken)
    {
        var subject = await catalog.GetCategoryAsync(categoryId, cancellationToken);
        if (subject is null)
            return new SmartOutcome(SmartTaskStatus.Skipped, "دسته پیدا نشد.");
        var text = string.IsNullOrWhiteSpace(subject.Description) || string.IsNullOrWhiteSpace(subject.FaqJson) ? CategoryWriter.Write(subject) : null;
        string? image = null;
        if (string.IsNullOrWhiteSpace(subject.ImageName) && subject.ProductImageNames.Count > 0)
            image = await images.BannerFromProductImageAsync(subject.ProductImageNames[0], UploadArea.Categories, cancellationToken);
        if (text is null && image is null)
            return new SmartOutcome(SmartTaskStatus.Skipped, subject.ProductCount < 3 ? "دسته کمتر از ۳ کالای موجود دارد؛ متن نوشته نشد." : "توضیح و تصویر دسته از قبل کامل بود.");
        var changed = await catalog.ApplyCategoryAsync(categoryId, text?.Html, image, text?.FaqJson, overwrite: false, cancellationToken);
        try
        {
            await seo.ProcessAsync(SeoEntityKind.Category, categoryId, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Autopilot SEO failed for category {Category}", categoryId);
        }
        return new SmartOutcome(changed > 0 ? SmartTaskStatus.Applied : SmartTaskStatus.Skipped,
            $"{(text is null ? "" : $"توضیح {Core.Services.Seo.Engine.PersianText.Digits(text.Words)} کلمه‌ای و پرسش‌های رایج")}{(text is not null && image is not null ? " + " : "")}{(image is null ? "" : "بنر بدون متن")} ذخیره شد.");
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
            var minimum = row.Type == BlogArticleType.Comparison ? 2 : row.Type == BlogArticleType.Review ? 1 : 3;
            if (products.Count(p => p.InStock) < minimum)
            {
                await plan.MarkAsync(planId, BlogPlanStatus.Skipped, note: "کالای موجود کافی برای این مقاله نبود.", cancellationToken: cancellationToken);
                return new SmartOutcome(SmartTaskStatus.Skipped, "کالای موجود کافی برای این مقاله نبود.");
            }
            var category = row.CategoryId is { } cid ? await catalog.GetCategoryAsync(cid, cancellationToken) : null;
            var article = BlogWriter.Write(new BlogBrief(row.Type, row.Topic, row.Keyword, category?.Name, category?.Slug, products, DateTimeOffset.UtcNow));
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
