using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Fratak_Shop.Core.Services.Seo.Engine;
using Fratak_Shop.Core.Services.Seo.Tools;
using Fratak_Shop.Core.Services.Settings;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.SeoEngine;
using Fratak_Shop.Data.Utilities;
using Fratak_Shop.Data.Utilities.SeoManagement;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Fratak_Shop.Core.Services.Seo.Autopilot;

/// <summary>نتیجهٔ پردازش خودکار یک صفحه.</summary>
/// <param name="Changed">چیزی عوض شد؟</param>
/// <param name="Path">آدرس عمومی صفحه (برای IndexNow و پاک کردن کش).</param>
/// <param name="Summary">خلاصهٔ فارسی کارها.</param>
public sealed record AutopilotPageResult(bool Changed, string? Path, IReadOnlyList<string> Summary)
{
    public static readonly AutopilotPageResult Nothing = new(false, null, []);
}

/// <summary>خلاصهٔ کار شبانه.</summary>
public sealed record AutopilotNightResult(int Processed, int Filled, int Rewritten, int TestsJudged, int Reverted, string Message);

/// <summary>
/// خلبان خودکار سئو (موارد ۱ تا ۱۸ و ۱۵۰ تا ۱۶۴ پیشنهاد سئو): سایت بدون کارشناس سئو، خودش سئو می‌شود.
/// </summary>
public interface ISeoAutopilotService
{
    /// <summary>ردیف تنظیمات (اگر نبود ساخته می‌شود؛ کلید IndexNow هم همان‌جا ساخته می‌شود).</summary>
    Task<SeoAutopilotConfig> GetConfigAsync(CancellationToken cancellationToken = default);

    /// <summary>تنظیمات فعلی (خراب/خالی ← پیش‌فرض‌ها).</summary>
    Task<SeoAutopilotOptions> GetOptionsAsync(CancellationToken cancellationToken = default);

    /// <summary>ذخیرهٔ تنظیمات.</summary>
    Task SaveOptionsAsync(SeoAutopilotOptions options, CancellationToken cancellationToken = default);

    /// <summary>
    /// پردازش یک صفحه بعد از ذخیره: پر کردن فیلدهای خالی (با موتور سئو)، فیلدهای مشتق، مرتب‌سازی HTML و Alt تصویرها.
    /// </summary>
    Task<AutopilotPageResult> ProcessAsync(SeoEntityKind kind, Guid id, CancellationToken cancellationToken = default);

    /// <summary>کار شبانه: صفحه‌های پردازش‌نشده، تأیید خودکار پیشنهادهای امن، بازنویسی داده‌محور، داوری آزمایش‌های عنوان.</summary>
    Task<AutopilotNightResult> RunNightlyAsync(CancellationToken cancellationToken = default);

    /// <summary>برگرداندن یک تغییر از تاریخچه.</summary>
    Task<OperationResult> RollbackAsync(Guid revisionId, string? byName, CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="ISeoAutopilotService"/>
public sealed class SeoAutopilotService(
    FratakShopDbContext context,
    ISeoEngineService engine,
    IStoreSettingService settings,
    ILogger<SeoAutopilotService> logger,
    ISeoHost? host = null) : ISeoAutopilotService
{
    /// <summary>نامی که در تاریخچه و «تأییدکننده» ثبت می‌شود.</summary>
    public const string ActorName = "سئوی خودکار";

    /// <summary>بیشترین صفحهٔ پردازش‌نشده در هر شب (بقیه شب‌های بعد؛ بار سرور کنترل می‌شود).</summary>
    public const int NightlyBacklog = 250;

    /// <summary>مدت آزمایش عنوان پیش از داوری (روز).</summary>
    public const int TitleTestDays = 28;

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    // ── تنظیمات ─────────────────────────────────────────────────────────

    public async Task<SeoAutopilotConfig> GetConfigAsync(CancellationToken cancellationToken = default)
    {
        var config = await context.SeoAutopilotConfigs.AsTracking().OrderBy(c => c.CreatedAt).FirstOrDefaultAsync(cancellationToken);
        if (config is not null)
        {
            if (string.IsNullOrWhiteSpace(config.IndexNowKey))
            {
                config.IndexNowKey = NewIndexNowKey();
                await context.SaveChangesAsync(cancellationToken);
            }

            return config;
        }

        config = new SeoAutopilotConfig { OptionsJson = new SeoAutopilotOptions().ToJson(), IndexNowKey = NewIndexNowKey() };
        context.SeoAutopilotConfigs.Add(config);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // سرور/درخواست دیگری هم‌زمان ساخت؛ همان را بخوان.
            context.Entry(config).State = EntityState.Detached;
            config = await context.SeoAutopilotConfigs.AsTracking().OrderBy(c => c.CreatedAt).FirstAsync(cancellationToken);
        }

        return config;
    }

    public async Task<SeoAutopilotOptions> GetOptionsAsync(CancellationToken cancellationToken = default) =>
        SeoAutopilotOptions.Parse((await GetConfigAsync(cancellationToken)).OptionsJson);

    public async Task SaveOptionsAsync(SeoAutopilotOptions options, CancellationToken cancellationToken = default)
    {
        var config = await GetConfigAsync(cancellationToken);
        config.OptionsJson = options.ToJson();
        await context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>کلید IndexNow: ۳۲ نویسهٔ هگز تصادفی (استاندارد IndexNow: ۸ تا ۱۲۸ نویسهٔ a-z، 0-9 و خط تیره).</summary>
    public static string NewIndexNowKey() => Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();

    // ── پردازش یک صفحه ──────────────────────────────────────────────────

    public async Task<AutopilotPageResult> ProcessAsync(SeoEntityKind kind, Guid id, CancellationToken cancellationToken = default)
    {
        var options = await GetOptionsAsync(cancellationToken);
        if (!options.Enabled)
            return AutopilotPageResult.Nothing;

        // تغییرهای خود خلبان دوباره او را صدا نزنند (حلقه نشود).
        using var _ = SeoChangeFeed.Suppress();
        var summary = new List<string>();

        // ۱) فیلدهای نوشتنی خالی ← موتور سئو (با تحقیق، تنوع و نگهبان اسپم) و تأیید خودکار امن
        if (options.Can(options.FillFields))
        {
            var page = await SeoPages.LoadAsync(context, kind, id, cancellationToken);
            if (page is null || !page.IsLive)
                return AutopilotPageResult.Nothing;
            var state = FieldStateOf(page);
            if (state.TitleEmpty || state.MetaEmpty || state.FocusEmpty || state.FaqEmpty || state.AllAltsEmpty || state.BodyThin)
            {
                var applied = await GenerateAndApproveAsync(kind, id, state, rewrite: false, cancellationToken);
                if (applied != SeoFields.None)
                    summary.Add("موتور سئو پر کرد: " + FieldNames(applied));
            }

            context.ChangeTracker.Clear();
        }

        // ۲) فیلدهای مشتق، HTML و تصویرها
        var current = await SeoPages.LoadAsync(context, kind, id, cancellationToken);
        if (current is null || !current.IsLive)
            return new AutopilotPageResult(summary.Count > 0, current?.Path, summary);

        var stateRow = await StateAsync(kind, id, cancellationToken);
        var stamps = ReadStamps(stateRow.StampsJson);
        var revisions = new List<SeoRevision>();
        var store = await settings.GetAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;

        if (options.Can(options.FillFields))
        {
            var keywords = await KeywordsAsync(current, cancellationToken);
            var facts = new AutoFillFacts
            {
                Kind = kind,
                Name = current.Name,
                CategoryName = current.CategoryName,
                BrandName = current.BrandName,
                SiteName = store.SiteName,
                AuthorName = current.AuthorName ?? options.DefaultAuthor,
                XHandle = string.IsNullOrWhiteSpace(store.XSiteHandle) ? XHandle(store.TwitterUrl) : XHandle(store.XSiteHandle),
                CreatedAt = current.Entity.CreatedAt,
                UpdatedAt = current.Entity.UpdatedAt,
                PublishedAt = current.PublishedAt,
                Keywords = keywords,
                InStock = current.InStock,
                Popularity = current.Popularity,
                IsLive = current.IsLive,
            };
            var seo = current.EnsureSeo();
            var snapshot = SeoFieldMap.Read(seo);
            var changed = SeoAutoFill.Apply(seo, facts, stamps);
            if (changed.Count > 0)
            {
                var after = SeoFieldMap.Read(seo);
                revisions.Add(Revision(current, SeoRevisionSource.AutoFill, "فیلدهای سئو خودکار پر شد: " + string.Join("، ", changed.Keys.Take(12)),
                    changed.Keys.ToDictionary(k => k, k => snapshot.GetValueOrDefault(k)), changed.Keys.ToDictionary(k => k, k => after.GetValueOrDefault(k))));
                summary.Add($"{PersianText.Digits(changed.Count)} فیلد مشتق (اوپن‌گراف، اسکیما، نقشهٔ سایت ...) پر شد");
            }

            stateRow.LastFilledAt = now;
        }

        if (options.Can(options.FixHtml) && current.Body is { Length: > 0 } body)
        {
            var hash = PersianText.Hash(body);
            if (hash != stateRow.LastHtmlHash)
            {
                var fixedHtml = HtmlSeoFixer.Fix(body, new HtmlFixContext(current.Name, host?.Host));
                if (fixedHtml.Changed && fixedHtml.Html.Length <= current.BodyMaxLength)
                {
                    current.SetBody(fixedHtml.Html);
                    revisions.Add(Revision(current, SeoRevisionSource.HtmlFix, "مرتب‌سازی HTML: " + string.Join("؛ ", fixedHtml.Changes),
                        new Dictionary<string, string?> { [SeoFieldMap.BodyField] = body },
                        new Dictionary<string, string?> { [SeoFieldMap.BodyField] = fixedHtml.Html }));
                    summary.Add("HTML متن مرتب شد (" + PersianText.Digits(fixedHtml.Changes.Count) + " نوع اصلاح)");
                    hash = PersianText.Hash(fixedHtml.Html);
                }

                stateRow.LastHtmlHash = hash;
                stateRow.LastHtmlFixAt = now;
            }
        }

        if (options.Can(options.Images))
        {
            var alts = current.FillMissingAlts();
            if (alts.Count > 0)
            {
                revisions.Add(Revision(current, SeoRevisionSource.Image, $"Alt برای {PersianText.Digits(alts.Count)} تصویر نوشته شد",
                    alts.ToDictionary(a => a.Key, _ => (string?)null), alts.ToDictionary(a => a.Key, a => (string?)a.Value)));
                summary.Add($"Alt {PersianText.Digits(alts.Count)} تصویر نوشته شد");
            }
        }

        stateRow.StampsJson = JsonSerializer.Serialize(stamps, Json);
        context.SeoRevisions.AddRange(revisions);

        if (DbLengthGuard.FindOverflow(context) is { } overflow)
        {
            logger.LogWarning("SEO autopilot skipped {Kind} {Id}: {Overflow}", kind, id, overflow);
            context.ChangeTracker.Clear();
            return new AutopilotPageResult(summary.Count > 0, current.Path, summary);
        }

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            logger.LogWarning(ex, "SEO autopilot could not save {Kind} {Id}", kind, id);
            context.ChangeTracker.Clear();
            return new AutopilotPageResult(false, current.Path, []);
        }

        return new AutopilotPageResult(summary.Count > 0, current.Path, summary);
    }

    /// <summary>ساخت پیشنهاد با موتور و تأیید بخش‌های مجاز. خروجی: بخش‌هایی که واقعاً اعمال شد.</summary>
    private async Task<SeoFields> GenerateAndApproveAsync(SeoEntityKind kind, Guid id, FieldState state, bool rewrite, CancellationToken cancellationToken)
    {
        var generated = await engine.GenerateAsync(kind, id, cancellationToken);
        if (!generated.IsSuccess())
            return SeoFields.None;
        var suggestion = await context.SeoSuggestions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == generated.Data, cancellationToken);
        if (suggestion is null)
            return SeoFields.None;
        var fields = AutoApprovePolicy.Fill(suggestion.Fields, state) | AutoApprovePolicy.Rewrite(suggestion.Fields, state, rewrite);
        if (fields == SeoFields.None)
            return SeoFields.None;
        var result = await engine.ApproveAsync(suggestion.Id, fields, null, ActorName, cancellationToken);
        return result.IsSuccess() ? fields : SeoFields.None;
    }

    private static FieldState FieldStateOf(SeoPage page)
    {
        var seo = page.Entity is null ? null : page.Seo;
        var words = PersianText.AnalyzeHtml(page.Body).Words;
        return new FieldState(
            TitleEmpty: string.IsNullOrWhiteSpace(seo?.Title),
            MetaEmpty: string.IsNullOrWhiteSpace(seo?.MetaDescription),
            FocusEmpty: string.IsNullOrWhiteSpace(seo?.FocusKeyword),
            FaqEmpty: page.SupportsFaq && string.IsNullOrWhiteSpace(seo?.FaqJson),
            AllAltsEmpty: page.ImageCount > 0 && page.ImagesWithAlt == 0,
            BodyThin: page.HasBody && words < SeoLimits.ThinContentWords);
    }

    /// <summary>
    /// کلمات کلیدی واقعی صفحه: جست‌وجوهای سرچ کنسول همین آدرس + جست‌وجوهای داخلی مرتبط + کلمهٔ کانونی و مشخصات.
    /// </summary>
    private async Task<List<string>> KeywordsAsync(SeoPage page, CancellationToken cancellationToken)
    {
        var path = SeoPaths.Normalize(page.Path);
        var gsc = await context.SearchConsoleQueryStats.AsNoTracking()
            .Where(s => s.Path == path && s.Query != "")
            .OrderByDescending(s => s.Impressions).Take(30)
            .Select(s => new { s.Query, s.Impressions })
            .ToListAsync(cancellationToken);

        var shortName = KeywordPlanner.ShortName(page.Name);
        var probe = PersianText.Normalize(KeywordPlanner.TypeWord(page.Name) ?? shortName);
        var internalSearch = probe.Length < 2
            ? []
            : await context.SearchQueryLogs.AsNoTracking()
                .Where(q => !q.IsHidden && q.LastResultCount > 0 && q.NormalizedQuery.Contains(probe))
                .OrderByDescending(q => q.SearchCount).Take(30)
                .Select(q => new { q.Query, q.SearchCount })
                .ToListAsync(cancellationToken);

        var research = new List<string>();
        if (!string.IsNullOrWhiteSpace(page.Seo?.FocusKeyword))
            research.Add(page.Seo!.FocusKeyword!);
        research.AddRange(KeywordIntel.LongTails(KeywordPlanner.TypeWord(page.Name), page.BrandName, page.FeatureValues));
        if (shortName.Length >= 3)
            research.Add(shortName);

        return KeywordIntel.Rank(page.Name,
                gsc.Select(g => (g.Query, g.Impressions)),
                internalSearch.Select(q => (q.Query, q.SearchCount)),
                research, 8)
            .Select(k => k.Phrase)
            .ToList();
    }

    // ── کار شبانه ───────────────────────────────────────────────────────

    public async Task<AutopilotNightResult> RunNightlyAsync(CancellationToken cancellationToken = default)
    {
        var options = await GetOptionsAsync(cancellationToken);
        if (!options.Enabled)
            return new AutopilotNightResult(0, 0, 0, 0, 0, "خلبان خودکار خاموش است.");

        var judged = 0;
        var reverted = 0;
        if (options.Can(options.Rewrite))
            (judged, reverted) = await JudgeTitleTestsAsync(cancellationToken);

        var (filled, rewritten) = await ApplyPendingAsync(options, cancellationToken);

        // صفحه‌هایی که هنوز پردازش نشده‌اند یا ۳۰ روز است بررسی نشده‌اند (مشخصات و قیمت عوض می‌شود؛ فیلدهای مشتق هم).
        var processed = 0;
        foreach (var (kind, id) in await BacklogAsync(NightlyBacklog, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await ProcessAsync(kind, id, cancellationToken);
                processed++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "SEO autopilot nightly failed for {Kind} {Id}", kind, id);
                context.ChangeTracker.Clear();
            }
        }

        var message = $"پردازش {PersianText.Digits(processed)} صفحه، پر کردن {PersianText.Digits(filled)}، بازنویسی داده‌محور {PersianText.Digits(rewritten)}، " +
                      $"داوری {PersianText.Digits(judged)} آزمایش عنوان ({PersianText.Digits(reverted)} برگشت).";
        var config = await GetConfigAsync(cancellationToken);
        config.LastNightlyRunAt = DateTimeOffset.UtcNow;
        config.LastNightlySummary = message.Length > 1000 ? message[..1000] : message;
        await context.SaveChangesAsync(cancellationToken);
        return new AutopilotNightResult(processed, filled, rewritten, judged, reverted, message);
    }

    /// <summary>صف صفحه‌های نیازمند پردازش: بدون وضعیت، یا آخرین پر کردن قدیمی‌تر از ۳۰ روز.</summary>
    private async Task<List<(SeoEntityKind Kind, Guid Id)>> BacklogAsync(int max, CancellationToken cancellationToken)
    {
        var cutoff = DateTimeOffset.UtcNow.AddDays(-30);
        var fresh = (await context.SeoAutoStates.AsNoTracking()
                .Where(s => s.LastFilledAt != null && s.LastFilledAt >= cutoff)
                .Select(s => new { s.Kind, s.EntityId })
                .ToListAsync(cancellationToken))
            .Select(s => (s.Kind, s.EntityId)).ToHashSet();

        var all = new List<(SeoEntityKind, Guid)>();
        all.AddRange((await context.Categories.AsNoTracking().Where(c => c.IsActive).Select(c => c.Id).ToListAsync(cancellationToken)).Select(i => (SeoEntityKind.Category, i)));
        all.AddRange((await context.Products.AsNoTracking().Where(p => p.IsActive).OrderByDescending(p => p.SalesCount).Select(p => p.Id).ToListAsync(cancellationToken)).Select(i => (SeoEntityKind.Product, i)));
        all.AddRange((await context.Brands.AsNoTracking().Where(b => b.IsActive).Select(b => b.Id).ToListAsync(cancellationToken)).Select(i => (SeoEntityKind.Brand, i)));
        all.AddRange((await context.BlogPosts.AsNoTracking().Where(b => b.IsPublished).Select(b => b.Id).ToListAsync(cancellationToken)).Select(i => (SeoEntityKind.BlogPost, i)));
        all.AddRange((await context.ContentPages.AsNoTracking().Where(c => c.IsActive).Select(c => c.Id).ToListAsync(cancellationToken)).Select(i => (SeoEntityKind.ContentPage, i)));
        return all.Where(x => !fresh.Contains(x)).Take(max).ToList();
    }

    /// <summary>
    /// پیشنهادهای در انتظار (ساخت موتور در اجرای زمان‌بندی‌شده): بخش‌های «پر کردن» همیشه، «بازنویسی» فقط با دادهٔ CTR.
    /// </summary>
    private async Task<(int Filled, int Rewritten)> ApplyPendingAsync(SeoAutopilotOptions options, CancellationToken cancellationToken)
    {
        var since = DateTimeOffset.UtcNow.AddDays(-30);
        var pending = await context.SeoSuggestions.AsNoTracking()
            .Where(s => s.Status == SeoSuggestionStatus.Pending && s.CreatedAt >= since)
            .OrderByDescending(s => s.ScoreAfter - s.ScoreBefore)
            .Take(300)
            .ToListAsync(cancellationToken);
        if (pending.Count == 0)
            return (0, 0);

        var now = DateTimeOffset.UtcNow;
        var budget = await RewriteBudgetAsync(options, cancellationToken);
        var stats = await PageStatsAsync(cancellationToken);
        var filled = 0;
        var rewritten = 0;

        using var _ = SeoChangeFeed.Suppress();
        foreach (var suggestion in pending)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var page = await SeoPages.LoadAsync(context, suggestion.Kind, suggestion.EntityId, cancellationToken);
                context.ChangeTracker.Clear();
                if (page is null || !page.IsLive)
                    continue;
                var state = FieldStateOf(page);
                var fill = options.Can(options.FillFields) ? AutoApprovePolicy.Fill(suggestion.Fields, state) : SeoFields.None;

                var rewrite = SeoFields.None;
                CtrSample? sample = null;
                if (options.Can(options.Rewrite) && budget > 0 && stats.TryGetValue(SeoPaths.Normalize(page.Path), out var found))
                {
                    sample = found;
                    var autoState = await context.SeoAutoStates.AsNoTracking()
                        .FirstOrDefaultAsync(s => s.Kind == page.Kind && s.EntityId == page.Id, cancellationToken);
                    var cooled = autoState?.LastRewriteAt is not { } last || now - last >= TimeSpan.FromDays(options.RewriteCooldownDays);
                    var running = await context.SeoTitleTests.AnyAsync(t => t.Kind == page.Kind && t.EntityId == page.Id && t.Status == SeoTitleTestStatus.Running, cancellationToken);
                    rewrite = AutoApprovePolicy.Rewrite(suggestion.Fields, state, cooled && !running && CtrModel.IsUnderperforming(found));
                }

                var fields = fill | rewrite;
                if (fields == SeoFields.None)
                    continue;

                var oldTitle = page.Seo?.Title;
                var oldMeta = page.Seo?.MetaDescription;
                var result = await engine.ApproveAsync(suggestion.Id, fields, null, ActorName, cancellationToken);
                context.ChangeTracker.Clear();
                if (!result.IsSuccess())
                    continue;
                if (fill != SeoFields.None)
                    filled++;
                if (rewrite != SeoFields.None && sample is not null)
                {
                    rewritten++;
                    budget--;
                    await StartTitleTestAsync(page, oldTitle, oldMeta, sample, now, cancellationToken);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "SEO autopilot could not apply suggestion {Id}", suggestion.Id);
                context.ChangeTracker.Clear();
            }
        }

        return (filled, rewritten);
    }

    private async Task StartTitleTestAsync(SeoPage before, string? oldTitle, string? oldMeta, CtrSample sample, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var after = await SeoPages.LoadAsync(context, before.Kind, before.Id, cancellationToken);
        if (after is null)
            return;
        var state = await StateAsync(before.Kind, before.Id, cancellationToken);
        state.LastRewriteAt = now;
        context.SeoTitleTests.Add(new SeoTitleTest
        {
            Kind = before.Kind,
            EntityId = before.Id,
            Path = Cut(before.Path, 500),
            OldTitle = Cut(oldTitle ?? before.Name, 300),
            NewTitle = Cut(after.Seo?.Title ?? after.Name, 300),
            OldMeta = oldMeta is null ? null : Cut(oldMeta, 500),
            NewMeta = after.Seo?.MetaDescription is { } meta ? Cut(meta, 500) : null,
            StartedAt = now,
            Status = SeoTitleTestStatus.Running,
            BaselineCtr = sample.Ctr,
            BaselinePosition = sample.Position,
            BaselineImpressions = sample.Impressions,
        });
        context.SeoRevisions.Add(Revision(after, SeoRevisionSource.Rewrite, "بازنویسی داده‌محور عنوان/توضیحات (نرخ کلیک پایین نسبت به جایگاه)؛ آزمایش ۲۸ روزه شروع شد",
            new Dictionary<string, string?> { ["Title"] = oldTitle, ["MetaDescription"] = oldMeta },
            new Dictionary<string, string?> { ["Title"] = after.Seo?.Title, ["MetaDescription"] = after.Seo?.MetaDescription }));
        await context.SaveChangesAsync(cancellationToken);
        context.ChangeTracker.Clear();
    }

    /// <summary>
    /// داوری آزمایش‌های عنوانی که ۲۸ روز از شروعشان گذشته و دادهٔ دورهٔ بعد از تغییر رسیده است. باخت ← عنوان قبلی برمی‌گردد
    /// (فقط اگر انسان در این مدت عنوان را عوض نکرده باشد).
    /// </summary>
    private async Task<(int Judged, int Reverted)> JudgeTitleTestsAsync(CancellationToken cancellationToken)
    {
        var due = DateTimeOffset.UtcNow.AddDays(-TitleTestDays);
        var tests = await context.SeoTitleTests.AsTracking()
            .Where(t => t.Status == SeoTitleTestStatus.Running && t.StartedAt <= due)
            .Take(200)
            .ToListAsync(cancellationToken);
        if (tests.Count == 0)
            return (0, 0);

        var periodStart = await context.SearchConsoleQueryStats.AsNoTracking().Select(s => (DateOnly?)s.PeriodStart).FirstOrDefaultAsync(cancellationToken);
        var stats = await PageStatsAsync(cancellationToken);
        var judged = 0;
        var reverted = 0;
        using var _ = SeoChangeFeed.Suppress();
        foreach (var test in tests)
        {
            // دادهٔ سرچ کنسول باید کاملاً مال بعد از تغییر باشد (دورهٔ آمار بعد از شروع آزمایش شروع شده باشد).
            if (periodStart is not { } start || start < DateOnly.FromDateTime(test.StartedAt.UtcDateTime.AddDays(2)))
                continue;
            var after = stats.GetValueOrDefault(SeoPaths.Normalize(test.Path)) ?? new CtrSample(0, 0, 0);
            var verdict = CtrModel.Judge(new CtrSample(test.BaselineImpressions, (int)Math.Round(test.BaselineCtr * test.BaselineImpressions), test.BaselinePosition), after);
            if (verdict == SeoTitleTestStatus.Inconclusive && DateTimeOffset.UtcNow - test.StartedAt < TimeSpan.FromDays(TitleTestDays * 3))
                continue; // دادهٔ کافی نیست؛ تا ۸۴ روز صبر
            test.Status = verdict;
            test.EvaluatedAt = DateTimeOffset.UtcNow;
            test.ResultCtr = after.Ctr;
            test.ResultPosition = after.Position;
            test.ResultImpressions = after.Impressions;
            judged++;

            if (verdict == SeoTitleTestStatus.Lost)
            {
                var page = await SeoPages.LoadAsync(context, test.Kind, test.EntityId, cancellationToken);
                if (page?.Seo is { } seo && seo.Title == test.NewTitle)
                {
                    seo.Title = test.OldTitle;
                    if (test.OldMeta is not null && seo.MetaDescription == test.NewMeta)
                        seo.MetaDescription = test.OldMeta;
                    context.SeoRevisions.Add(Revision(page, SeoRevisionSource.TitleTest, "آزمایش عنوان باخت؛ عنوان قبلی برگشت",
                        new Dictionary<string, string?> { ["Title"] = test.NewTitle, ["MetaDescription"] = test.NewMeta },
                        new Dictionary<string, string?> { ["Title"] = test.OldTitle, ["MetaDescription"] = test.OldMeta }));
                    test.Note = "عنوان قبلی برگشت.";
                    reverted++;
                }
                else
                {
                    test.Note = "عنوان در این مدت دستی تغییر کرده بود؛ برگردانده نشد.";
                }
            }

            await context.SaveChangesAsync(cancellationToken);
        }

        return (judged, reverted);
    }

    /// <summary>سقف بازنویسی این هفته: درصد مجاز از صفحه‌های زنده منهای بازنویسی‌های ۷ روز اخیر (حداقل ۱ در هفته).</summary>
    private async Task<int> RewriteBudgetAsync(SeoAutopilotOptions options, CancellationToken cancellationToken)
    {
        var pages = await context.SeoScores.CountAsync(cancellationToken);
        var allowed = Math.Max(1, pages * options.RewriteWeeklyPercent / 100);
        var weekAgo = DateTimeOffset.UtcNow.AddDays(-7);
        var used = await context.SeoRevisions.CountAsync(r => r.Source == SeoRevisionSource.Rewrite && r.CreatedAt >= weekAgo, cancellationToken);
        return Math.Max(0, allowed - used);
    }

    /// <summary>آمار جست‌وجوی هر آدرس (جمع همهٔ عبارت‌ها) از آخرین همگام‌سازی سرچ کنسول.</summary>
    private async Task<Dictionary<string, CtrSample>> PageStatsAsync(CancellationToken cancellationToken)
    {
        var rows = await context.SearchConsoleQueryStats.AsNoTracking()
            .GroupBy(s => s.Path)
            .Select(g => new { Path = g.Key, Clicks = g.Sum(s => s.Clicks), Impressions = g.Sum(s => s.Impressions), PositionSum = g.Sum(s => s.Position * s.Impressions) })
            .ToListAsync(cancellationToken);
        return rows.Where(r => r.Impressions > 0)
            .ToDictionary(r => r.Path, r => new CtrSample(r.Impressions, r.Clicks, r.PositionSum / r.Impressions), StringComparer.OrdinalIgnoreCase);
    }

    // ── برگرداندن ───────────────────────────────────────────────────────

    public async Task<OperationResult> RollbackAsync(Guid revisionId, string? byName, CancellationToken cancellationToken = default)
    {
        var revision = await context.SeoRevisions.AsTracking().FirstOrDefaultAsync(r => r.Id == revisionId, cancellationToken);
        if (revision is null)
            return OperationResult.NotFound("این تغییر در تاریخچه پیدا نشد.");
        if (revision.RevertedAt is not null)
            return OperationResult.Error("این تغییر قبلاً برگردانده شده است.");

        var page = await SeoPages.LoadAsync(context, revision.Kind, revision.EntityId, cancellationToken);
        if (page is null)
            return OperationResult.Error("صفحه حذف شده است؛ برگرداندن ممکن نیست.");

        var before = ReadMap(revision.BeforeJson);
        var after = ReadMap(revision.AfterJson);
        var current = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var (field, value) in before)
        {
            current[field] = page.Get(field);
            if (!page.Set(field, value))
                return OperationResult.Error($"فیلد «{field}» قابل برگرداندن نیست.");
        }

        revision.RevertedAt = DateTimeOffset.UtcNow;
        revision.RevertedBy = byName is null ? null : Cut(byName, 150);
        context.SeoRevisions.Add(Revision(page, SeoRevisionSource.Rollback, "برگرداندن: " + Cut(revision.Summary, 400), current, before));

        // مُهر فیلدهای برگشته پاک می‌شود تا سیستم دوباره همان تغییر را تکرار نکند (مقدار برگشته «انتخاب انسان» است).
        var state = await StateAsync(revision.Kind, revision.EntityId, cancellationToken);
        var stamps = ReadStamps(state.StampsJson);
        foreach (var field in before.Keys.Concat(after.Keys))
            stamps.Remove(field);
        state.StampsJson = JsonSerializer.Serialize(stamps, Json);
        if (before.ContainsKey(SeoFieldMap.BodyField))
            state.LastHtmlHash = PersianText.Hash(before[SeoFieldMap.BodyField] ?? "");

        if (DbLengthGuard.FindOverflow(context) is { } overflow)
        {
            context.ChangeTracker.Clear();
            return OperationResult.Error(overflow);
        }

        using (SeoChangeFeed.Suppress())
            await context.SaveChangesAsync(cancellationToken);
        return OperationResult.Success("تغییر برگردانده شد.");
    }

    // ── ابزارها ─────────────────────────────────────────────────────────

    private async Task<SeoAutoState> StateAsync(SeoEntityKind kind, Guid id, CancellationToken cancellationToken)
    {
        var state = context.SeoAutoStates.Local.FirstOrDefault(s => s.Kind == kind && s.EntityId == id)
                    ?? await context.SeoAutoStates.AsTracking().FirstOrDefaultAsync(s => s.Kind == kind && s.EntityId == id, cancellationToken);
        if (state is not null)
            return state;
        state = new SeoAutoState { Kind = kind, EntityId = id };
        context.SeoAutoStates.Add(state);
        return state;
    }

    private static SeoRevision Revision(SeoPage page, SeoRevisionSource source, string summary,
        Dictionary<string, string?> before, Dictionary<string, string?> after) => new()
    {
        Kind = page.Kind,
        EntityId = page.Id,
        Path = Cut(page.Path, 500),
        PageTitle = Cut(page.Name, 300),
        Source = source,
        Summary = Cut(summary, 1000),
        BeforeJson = JsonSerializer.Serialize(before, Json),
        AfterJson = JsonSerializer.Serialize(after, Json),
        ByName = ActorName,
    };

    /// <summary>خواندن مُهرها؛ JSON خراب ← خالی.</summary>
    public static Dictionary<string, string> ReadStamps(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json) is { } map
                ? new Dictionary<string, string>(map, StringComparer.Ordinal)
                : new Dictionary<string, string>(StringComparer.Ordinal);
        }
        catch (JsonException)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }
    }

    private static Dictionary<string, string?> ReadMap(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new Dictionary<string, string?>(StringComparer.Ordinal);
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string?>>(json) ?? new Dictionary<string, string?>(StringComparer.Ordinal);
        }
        catch (JsonException)
        {
            return new Dictionary<string, string?>(StringComparer.Ordinal);
        }
    }

    private static string FieldNames(SeoFields fields)
    {
        var names = new List<string>();
        if (fields.HasFlag(SeoFields.Title)) names.Add("عنوان");
        if (fields.HasFlag(SeoFields.MetaDescription)) names.Add("توضیحات متا");
        if (fields.HasFlag(SeoFields.FocusKeyword)) names.Add("کلمهٔ کلیدی");
        if (fields.HasFlag(SeoFields.Description)) names.Add("متن تکمیلی");
        if (fields.HasFlag(SeoFields.Faq)) names.Add("پرسش‌وپاسخ");
        if (fields.HasFlag(SeoFields.ImageAlt)) names.Add("Alt تصویرها");
        return string.Join("، ", names);
    }

    /// <summary>نام کاربری ایکس از آدرس صفحه (مثال: https://x.com/fratak ← @fratak).</summary>
    public static string? XHandle(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;
        var trimmed = url.Trim().TrimEnd('/');
        if (trimmed.StartsWith('@'))
            return trimmed.Length is > 1 and <= 50 ? trimmed : null;
        if (trimmed.Length <= 30 && trimmed.All(c => char.IsAsciiLetterOrDigit(c) || c == '_'))
            return "@" + trimmed; // نام خالی بدون @ (مثال: fratak ← @fratak)
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
            return null;
        if (!uri.Host.EndsWith("x.com", StringComparison.OrdinalIgnoreCase) && !uri.Host.EndsWith("twitter.com", StringComparison.OrdinalIgnoreCase))
            return null;
        var name = uri.AbsolutePath.Trim('/').Split('/')[0];
        return name.Length is > 0 and <= 30 && name.All(c => char.IsAsciiLetterOrDigit(c) || c == '_') ? "@" + name : null;
    }

    private static string Cut(string value, int max) => value.Length <= max ? value : value[..max];
}
