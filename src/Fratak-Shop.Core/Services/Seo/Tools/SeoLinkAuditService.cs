using System.Text.Json;
using Fratak_Shop.Core.DTOs.Admin;
using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Core.Services.Seo.Engine;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Admin;
using Fratak_Shop.Data.Entities.SeoEngine;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Fratak_Shop.Core.Services.Seo.Tools;

// ═══════════════════════════════════════════════════════════════════════════
//  یابندهٔ لینک شکسته (مرحلهٔ هفتم – فاز ۸ب)
//  روزی یک بار (سرویس پس‌زمینهٔ SeoToolsWorker) یا دستی از /admin/seo/links:
//   ۱) متن همهٔ صفحه‌های فعال (توضیحات محصول، دسته، برند، متن مقاله و صفحه‌های محتوا) خوانده می‌شود.
//   ۲) هر لینک داخلی بدون درخواست اینترنتی با نامک‌های دیتابیس و ریدایرکت‌ها مقایسه می‌شود (سریع و دقیق).
//      تصویرهای داخلی روی دیسک بررسی می‌شوند.
//   ۳) لینک‌های بیرونی (اگر روشن باشد) با درخواست HEAD/GET بررسی و نتیجه ۷ روز کش می‌شود.
//   ۴) نتیجه در جدول SeoLinkIssues: مشکل‌های تازه اضافه، درست‌شده‌ها حذف. اعلان فقط برای مشکل جدی تازه.
//  اصلاح: «جایگزینی آدرس» یا «برداشتن لینک» مستقیم در HTML همان صفحه (با ثبت در تاریخچهٔ تغییرات و خالی شدن کش).
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// دسترسی به اینترنت و دیسک برای یابندهٔ لینک (پیاده‌سازی: Web/Infrastructure/SeoEngine/SeoLinkProbe).
/// پیاده‌سازی پیش‌فرض Core (<see cref="OfflineSeoLinkProbe"/>) هیچ درخواستی نمی‌فرستد (برای تست و محیط بدون وب).
/// </summary>
public interface ISeoLinkProbe
{
    /// <summary>مسیرهای ثابت سایت (بدون پارامتر)، یکسان‌شده. مثال: «/»، «/blog»، «/faq»، «/sitemap.xml».</summary>
    IReadOnlySet<string> StaticPaths { get; }

    /// <summary>آیا فایل داخلی (مثلاً «/uploads/products/a.webp») روی دیسک هست؟</summary>
    bool LocalFileExists(string path);

    /// <summary>بررسی HTTP یک لینک بیرونی (هرگز استثنا نمی‌دهد).</summary>
    Task<LinkProbeResult> CheckExternalAsync(Uri url, CancellationToken cancellationToken);
}

/// <summary>پیاده‌سازی بدون شبکه: لینک بیرونی «بررسی نشد» و فایل‌ها موجود فرض می‌شوند.</summary>
public sealed class OfflineSeoLinkProbe : ISeoLinkProbe
{
    public IReadOnlySet<string> StaticPaths { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "/", "/blog", "/products", "/search", "/cart", "/faq", "/contact-us", "/compare", "/track-order", "/club", "/register", "/sitemap.xml", "/robots.txt",
    };

    public bool LocalFileExists(string path) => true;

    public Task<LinkProbeResult> CheckExternalAsync(Uri url, CancellationToken cancellationToken) =>
        Task.FromResult(new LinkProbeResult(null, Error: "بررسی لینک بیرونی در این محیط فعال نیست"));
}

/// <summary>نتیجهٔ یک اجرای یابندهٔ لینک.</summary>
public sealed record SeoLinkAuditResult(bool Ran, int Pages, int Links, int Issues, int New, int Fixed, int ExternalChecked, string Message);

/// <summary>فیلتر فهرست: tab = all/internal/external/images/ignored؛ problem = یک نوع مشکل؛ q = جست‌وجو در نام صفحه یا آدرس.</summary>
public sealed record SeoLinkQuery(string Tab, SeoLinkProblem? Problem, string? Search, int Page, int Take = 30);

/// <summary>خلاصهٔ صفحهٔ «لینک‌های شکسته».</summary>
public sealed class SeoLinkDashboard
{
    public int Total { get; init; }
    public int Serious { get; init; }
    public int Redirected { get; init; }
    public int Ignored { get; init; }
    public IReadOnlyDictionary<SeoLinkProblem, int> ByProblem { get; init; } = new Dictionary<SeoLinkProblem, int>();
    public bool Enabled { get; init; }
    public bool CheckExternal { get; init; }
    public DateTimeOffset? LastRunAt { get; init; }
    public string? LastSummary { get; init; }
}

/// <summary>سرویس یابندهٔ لینک شکسته. محل استفاده: SeoToolsWorker و صفحهٔ /admin/seo/links.</summary>
public interface ISeoLinkAuditService
{
    Task<SeoLinkAuditResult> RunAsync(bool manual, CancellationToken cancellationToken = default);
    Task<SeoLinkDashboard> GetDashboardAsync(CancellationToken cancellationToken = default);
    Task<PagedList<SeoLinkIssue>> ListAsync(SeoLinkQuery query, CancellationToken cancellationToken = default);

    /// <summary>آدرس لینک را در متن صفحه عوض می‌کند (newUrl) یا لینک را برمی‌دارد (newUrl = null).</summary>
    Task<OperationResult> FixAsync(Guid issueId, string? newUrl, CancellationToken cancellationToken = default);

    /// <summary>همهٔ لینک‌های داخلی ریدایرکت‌دار را یک‌جا به آدرس نهایی‌شان عوض می‌کند.</summary>
    Task<OperationResult<int>> FixRedirectsAsync(CancellationToken cancellationToken = default);

    Task<OperationResult> IgnoreAsync(Guid issueId, bool ignore, CancellationToken cancellationToken = default);
    Task<OperationResult> SaveSettingsAsync(bool enabled, bool checkExternal, CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="ISeoLinkAuditService"/>
public sealed class SeoLinkAuditService(
    FratakShopDbContext context,
    ISeoLinkProbe probe,
    IStaffNotificationService notifications,
    ILogger<SeoLinkAuditService> logger) : ISeoLinkAuditService
{
    /// <summary>یک اجرا در هر سرور.</summary>
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public static bool IsRunning => Gate.CurrentCount == 0;

    /// <summary>حداکثر لینک بیرونی تازه (کش‌نشده) در هر اجرا؛ بقیه در اجرای بعدی.</summary>
    public const int ExternalPerRun = 400;

    /// <summary>حداکثر زمان بخش لینک‌های بیرونی در هر اجرا.</summary>
    private static readonly TimeSpan ExternalBudget = TimeSpan.FromMinutes(12);

    /// <summary>یک صفحه با متن HTML آن.</summary>
    private sealed record Source(SeoEntityKind Kind, Guid Id, string Title, string Path, string? Html);

    /// <summary>یک مشکل پیداشده در این اجرا (پیش از ذخیره).</summary>
    private sealed record Found(Source Source, ExtractedLink Link, bool Internal, SeoLinkCheck Check);

    // ── اجرا ─────────────────────────────────────────────────────────────

    public async Task<SeoLinkAuditResult> RunAsync(bool manual, CancellationToken cancellationToken = default)
    {
        if (!await Gate.WaitAsync(0, cancellationToken))
            return new SeoLinkAuditResult(false, 0, 0, 0, 0, 0, 0, "یک بررسی دیگر در جریان است.");
        try
        {
            var settings = await SeoToolsConfigStore.GetAsync(context, tracking: false, cancellationToken);
            if (!manual && !settings.IsLinkCheckEnabled)
                return new SeoLinkAuditResult(false, 0, 0, 0, 0, 0, 0, "یابندهٔ لینک خاموش است.");

            var result = await RunCoreAsync(settings.CheckExternalLinks, cancellationToken);
            // RunCoreAsync در پایان ChangeTracker را پاک می‌کند؛ ردیف تنظیمات دوباره و tracked خوانده می‌شود.
            var config = await SeoToolsConfigStore.GetAsync(context, tracking: true, cancellationToken);
            config.LinkCheckAt = DateTimeOffset.UtcNow;
            config.LinkCheckSummary = Cut(result.Message, 500);
            await context.SaveChangesAsync(cancellationToken);

            // دور ۲۳ (مورد ۲۴ – «بدون صف دستی»): در اجرای خودکار، لینک داخلی ریدایرکت‌دار به آدرس نهایی و لینک داخلی ۴۰۴ (غیر از تصویر)
            // به متن ساده تبدیل می‌شود. لینک بیرونی و تصویر برای تصمیم مدیر می‌ماند (حذف خودکار تصویر ممکن است متن را خراب کند).
            if (!manual)
                await AutoFixAsync(cancellationToken);

            var serious = await context.SeoLinkIssues.AsNoTracking()
                .CountAsync(i => !i.IsIgnored && (i.Problem == SeoLinkProblem.NotFound || i.Problem == SeoLinkProblem.Gone), cancellationToken);
            if (result.New > 0 || manual)
            {
                await notifications.NotifyAsync(new StaffAlert(StaffNotificationKind.SeoSuggestions,
                    result.New > 0 ? $"{PersianText.Digits(result.New)} لینک خراب تازه در متن صفحه‌ها" : "گزارش بررسی لینک‌ها",
                    result.Message + (serious > 0 ? $" · {PersianText.Digits(serious)} لینک ۴۰۴ منتظر اصلاح" : ""),
                    "/admin/seo/links", [AdminPermissions.SeoManage]));
            }
            return result;
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>بیشترین اصلاح خودکار در هر اجرا (هر اصلاح یک ذخیره است؛ هاست ضعیف).</summary>
    public const int AutoFixPerRun = 200;

    /// <summary>
    /// دور ۲۳: اصلاح خودکار امن. ریدایرکت داخلی ← آدرس نهایی (<see cref="FixRedirectsAsync"/>)؛ ۴۰۴/حذف‌شدهٔ داخلی غیرتصویری ← برداشتن لینک
    /// با حفظ متن (<see cref="FixAsync"/> با آدرس null). موارد «نادیده گرفته‌شده» دست نمی‌خورند. خطا فقط ثبت می‌شود.
    /// </summary>
    private async Task AutoFixAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (await context.SeoLinkIssues.AnyAsync(i => !i.IsIgnored && i.IsInternal && i.Problem == SeoLinkProblem.Redirected && i.SuggestedUrl != null, cancellationToken))
                await FixRedirectsAsync(cancellationToken);
            var broken = await context.SeoLinkIssues.AsNoTracking()
                .Where(i => !i.IsIgnored && i.IsInternal && !i.IsImage && (i.Problem == SeoLinkProblem.NotFound || i.Problem == SeoLinkProblem.Gone))
                .OrderBy(i => i.FirstSeenAt)
                .Select(i => i.Id)
                .Take(AutoFixPerRun)
                .ToListAsync(cancellationToken);
            foreach (var id in broken)
                await FixAsync(id, null, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            context.ChangeTracker.Clear();
            logger.LogWarning(ex, "Automatic link fix failed");
        }
    }

    private async Task<SeoLinkAuditResult> RunCoreAsync(bool checkExternal, CancellationToken cancellationToken)
    {
        var index = await BuildIndexAsync(cancellationToken);
        var hosts = await OwnHostsAsync(cancellationToken);
        var sources = await LoadSourcesAsync(null, null, cancellationToken);

        var found = new List<Found>();
        var external = new Dictionary<string, (Uri Uri, List<(Source Source, ExtractedLink Link)> Uses)>(StringComparer.Ordinal);
        var stillPresent = new HashSet<(SeoEntityKind, Guid, string)>();
        var links = 0;

        foreach (var source in sources)
        {
            foreach (var link in SeoLinkExtractor.Extract(source.Html))
            {
                links++;
                stillPresent.Add((source.Kind, source.Id, SeoPaths.Hash(link.Url)));
                var target = SeoLinkTarget.Classify(link.Url, source.Path, hosts);
                if (target is null)
                {
                    found.Add(new Found(source, link, true, new SeoLinkCheck(SeoLinkProblem.NotFound, null, "آدرس نامعتبر است")));
                    continue;
                }
                if (link.IsImage && target.IsHttp)
                {
                    // تصویر http در سایت https: مرورگرها نمایش نمی‌دهند یا هشدار ناامن بودن می‌دهند.
                    var secure = link.Url.Trim().StartsWith("http://", StringComparison.OrdinalIgnoreCase) ? "https://" + link.Url.Trim()[7..] : null;
                    found.Add(new Found(source, link, target.IsInternal, new SeoLinkCheck(SeoLinkProblem.Insecure, secure, "تصویر با http در سایت https بارگذاری نمی‌شود")));
                    continue;
                }
                if (target.IsInternal)
                {
                    var check = index.Check(target.Path!) ?? (probe.LocalFileExists(target.FilePath ?? target.Path!)
                        ? SeoLinkCheck.Ok
                        : new SeoLinkCheck(SeoLinkProblem.NotFound, null, link.IsImage ? "فایل تصویر روی سرور نیست" : "فایل روی سرور نیست"));
                    if (!check.IsOk)
                        found.Add(new Found(source, link, true, check));
                }
                else if (checkExternal)
                {
                    var key = target.External!.AbsoluteUri;
                    if (!external.TryGetValue(key, out var entry))
                        external[key] = entry = (target.External!, []);
                    entry.Uses.Add((source, link));
                }
            }
        }

        // لینک‌های بیرونی: اول کش، بعد شبکه (با سقف تعداد و زمان). بررسی‌نشده‌ها در اجرای بعدی.
        var unchecked_ = new HashSet<string>(StringComparer.Ordinal);
        var checkedNow = 0;
        if (external.Count > 0)
        {
            var results = await CheckExternalAsync(external.Keys.ToList(), external.ToDictionary(e => e.Key, e => e.Value.Uri, StringComparer.Ordinal), cancellationToken);
            checkedNow = results.CheckedNow;
            foreach (var (key, entry) in external)
            {
                if (!results.Checks.TryGetValue(key, out var check))
                {
                    unchecked_.Add(key);
                    continue;
                }
                if (!check.IsOk)
                    foreach (var (source, link) in entry.Uses)
                        found.Add(new Found(source, link, false, check));
            }
        }

        var (added, removed) = await SaveIssuesAsync(found, stillPresent, external, unchecked_, cancellationToken);
        var total = found.Count;
        var message = $"{PersianText.Digits(sources.Count)} صفحه و {PersianText.Digits(links)} لینک بررسی شد · {PersianText.Digits(total)} مشکل" +
                      (added > 0 ? $" ({PersianText.Digits(added)} تازه)" : "") +
                      (removed > 0 ? $" · {PersianText.Digits(removed)} مورد درست شده بود" : "") +
                      (checkExternal ? $" · {PersianText.Digits(external.Count)} لینک بیرونی" + (unchecked_.Count > 0 ? $" ({PersianText.Digits(unchecked_.Count)} در اجرای بعدی)" : "") : " · لینک‌های بیرونی بررسی نشد (خاموش)");
        return new SeoLinkAuditResult(true, sources.Count, links, total, added, removed, checkedNow, message);
    }

    /// <summary>
    /// بررسی لینک‌های بیرونی با کش ۷ روزه (شکست: ۱ روز) در SeoResearchCaches. حداکثر ۴ درخواست هم‌زمان
    /// (فاصلهٔ مؤدبانه برای هر دامنه را خود ISeoLinkProbe رعایت می‌کند).
    /// </summary>
    private async Task<(Dictionary<string, SeoLinkCheck> Checks, int CheckedNow)> CheckExternalAsync(
        List<string> urls, Dictionary<string, Uri> uris, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var checks = new Dictionary<string, SeoLinkCheck>(StringComparer.Ordinal);
        var keyOf = urls.ToDictionary(u => u, CacheKey, StringComparer.Ordinal);
        var keys = keyOf.Values.Distinct().ToList();
        var cached = new Dictionary<string, SeoResearchCache>(StringComparer.Ordinal);
        foreach (var chunk in keys.Chunk(500))
        {
            var rows = await context.SeoResearchCaches.AsNoTracking()
                .Where(c => c.Kind == SeoResearchKind.Link && chunk.Contains(c.Key))
                .ToListAsync(cancellationToken);
            foreach (var row in rows)
                cached[row.Key] = row;
        }

        var toFetch = new List<string>();
        foreach (var url in urls)
        {
            if (cached.TryGetValue(keyOf[url], out var row) && row.ExpiresAt > now && TryRead(row.Json) is { } probeResult)
                checks[url] = SeoLinkRules.FromProbe(probeResult, uris[url]);
            else
                toFetch.Add(url);
        }

        var fetched = new System.Collections.Concurrent.ConcurrentDictionary<string, LinkProbeResult>(StringComparer.Ordinal);
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(ExternalBudget);
        try
        {
            await Parallel.ForEachAsync(toFetch.Take(ExternalPerRun), new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = budget.Token },
                async (url, token) => fetched[url] = await probe.CheckExternalAsync(uris[url], token));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogInformation("Link audit: external budget reached after {Count} links", fetched.Count);
        }

        // ردیف‌های کش موجودِ آدرس‌های تازه‌بررسی‌شده، tracked و یک‌جا (نه یک پرس‌وجو برای هر آدرس).
        var tracked = new Dictionary<string, SeoResearchCache>(StringComparer.Ordinal);
        foreach (var chunk in fetched.Keys.Select(u => keyOf[u]).Where(cached.ContainsKey).Distinct().Chunk(500))
        {
            var rows = await context.SeoResearchCaches.AsTracking()
                .Where(c => c.Kind == SeoResearchKind.Link && chunk.Contains(c.Key))
                .ToListAsync(cancellationToken);
            foreach (var row in rows)
                tracked[row.Key] = row;
        }

        foreach (var (url, result) in fetched)
        {
            var check = SeoLinkRules.FromProbe(result, uris[url]);
            checks[url] = check;
            var key = keyOf[url];
            if (!tracked.TryGetValue(key, out var row))
                tracked[key] = row = context.SeoResearchCaches.Add(new SeoResearchCache { Kind = SeoResearchKind.Link, Key = key }).Entity;
            row.Source = Cut(uris[url].Host, 40);
            row.Json = JsonSerializer.Serialize(result);
            // «مسدود» و «در دسترس نیست» زود دوباره امتحان می‌شوند؛ سالم و ۴۰۴ یک هفته معتبرند.
            row.Ok = check.IsOk;
            row.FetchedAt = now;
            row.ExpiresAt = now + (check.Problem is SeoLinkProblem.None or SeoLinkProblem.NotFound or SeoLinkProblem.Gone or SeoLinkProblem.Redirected
                ? TimeSpan.FromDays(7)
                : TimeSpan.FromDays(1));
        }
        if (fetched.Count > 0)
            await context.SaveChangesAsync(cancellationToken);
        return (checks, fetched.Count);
    }

    /// <summary>کلید کش: خود آدرس (اگر بلند بود هش آن).</summary>
    private static string CacheKey(string url) => url.Length <= 440 ? url : "sha256:" + SeoPaths.Hash(url);

    private static LinkProbeResult? TryRead(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<LinkProbeResult>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// ذخیرهٔ نتیجه: ردیف تازه برای مشکل تازه، به‌روزرسانی ردیف موجود، حذف (واقعی) ردیف‌هایی که درست شده‌اند.
    /// لینک بیرونی که این بار بررسی نشد (سقف اجرا) ردیف قبلی‌اش را نگه می‌دارد.
    /// </summary>
    private async Task<(int Added, int Removed)> SaveIssuesAsync(List<Found> found, HashSet<(SeoEntityKind, Guid, string)> stillPresent,
        Dictionary<string, (Uri Uri, List<(Source Source, ExtractedLink Link)> Uses)> external, HashSet<string> unchecked_,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var existing = await context.SeoLinkIssues.AsTracking().ToListAsync(cancellationToken);
        var byKey = existing
            .GroupBy(i => (i.Kind, i.EntityId, i.UrlHash))
            .ToDictionary(g => g.Key, g => g.First());

        // لینک‌های بیرونی بررسی‌نشده: کلیدهایی که نباید حذف شوند.
        var keep = new HashSet<(SeoEntityKind, Guid, string)>();
        foreach (var url in unchecked_)
            foreach (var (source, link) in external[url].Uses)
                keep.Add((source.Kind, source.Id, SeoPaths.Hash(link.Url)));

        var added = 0;
        var current = new HashSet<(SeoEntityKind, Guid, string)>();
        foreach (var item in found)
        {
            var hash = SeoPaths.Hash(item.Link.Url);
            var key = (item.Source.Kind, item.Source.Id, hash);
            if (!current.Add(key))
                continue; // همان آدرس هم به‌صورت لینک و هم تصویر؛ یک ردیف کافی است
            if (!byKey.TryGetValue(key, out var issue))
            {
                issue = context.SeoLinkIssues.Add(new SeoLinkIssue
                {
                    Kind = item.Source.Kind,
                    EntityId = item.Source.Id,
                    UrlHash = hash,
                    FirstSeenAt = now,
                }).Entity;
                byKey[key] = issue;
                added++;
            }
            issue.EntityTitle = Cut(item.Source.Title, 200)!;
            issue.Path = Cut(item.Source.Path, 400)!;
            issue.Url = Cut(item.Link.Url, 2048)!;
            issue.AnchorText = Cut(item.Link.Anchor, 200);
            issue.IsImage = item.Link.IsImage;
            issue.IsInternal = item.Internal;
            issue.Problem = item.Check.Problem;
            issue.StatusCode = item.Check.StatusCode;
            issue.SuggestedUrl = Cut(item.Check.SuggestedUrl, 2048);
            issue.Detail = Cut(item.Check.Detail, 300);
            issue.Occurrences = item.Link.Count;
            issue.LastSeenAt = now;
        }

        var obsolete = existing
            .Where(i => !current.Contains((i.Kind, i.EntityId, i.UrlHash)) && !(keep.Contains((i.Kind, i.EntityId, i.UrlHash)) && stillPresent.Contains((i.Kind, i.EntityId, i.UrlHash))))
            .Select(i => i.Id)
            .ToList();
        await context.SaveChangesAsync(cancellationToken);
        foreach (var chunk in obsolete.Chunk(500))
            await context.SeoLinkIssues.Where(i => chunk.Contains(i.Id)).ExecuteDeleteAsync(cancellationToken);
        context.ChangeTracker.Clear();
        return (added, obsolete.Count);
    }

    // ── داده ─────────────────────────────────────────────────────────────

    /// <summary>نامک‌های فعال، ریدایرکت‌ها و مسیرهای ثابت برای بررسی لینک داخلی.</summary>
    private async Task<InternalLinkIndex> BuildIndexAsync(CancellationToken cancellationToken)
    {
        static string N(string slug) => SeoPaths.Normalize("/" + slug).TrimStart('/');

        var products = await context.Products.AsNoTracking().Select(p => new { p.Slug, p.IsActive }).ToListAsync(cancellationToken);
        var index = new InternalLinkIndex
        {
            Products = products.Where(p => p.IsActive).Select(p => N(p.Slug)).ToHashSet(StringComparer.Ordinal),
            InactiveProducts = products.Where(p => !p.IsActive).Select(p => N(p.Slug)).ToHashSet(StringComparer.Ordinal),
            Categories = (await context.Categories.AsNoTracking().Where(c => c.IsActive).Select(c => c.Slug).ToListAsync(cancellationToken)).Select(N).ToHashSet(StringComparer.Ordinal),
            Brands = (await context.Brands.AsNoTracking().Where(b => b.IsActive).Select(b => b.Slug).ToListAsync(cancellationToken)).Select(N).ToHashSet(StringComparer.Ordinal),
            Posts = (await context.BlogPosts.AsNoTracking().Where(p => p.IsPublished).Select(p => p.Slug).ToListAsync(cancellationToken)).Select(N).ToHashSet(StringComparer.Ordinal),
            BlogCategories = (await context.BlogCategories.AsNoTracking().Where(c => c.IsActive).Select(c => c.Slug).ToListAsync(cancellationToken)).Select(N).ToHashSet(StringComparer.Ordinal),
            Pages = (await context.ContentPages.AsNoTracking().Where(p => p.IsActive).Select(p => p.Slug).ToListAsync(cancellationToken)).Select(N).ToHashSet(StringComparer.Ordinal),
            StaticPaths = new HashSet<string>(probe.StaticPaths.Select(SeoPaths.Normalize), StringComparer.Ordinal),
        };
        var redirects = await context.SeoRedirects.AsNoTracking().Where(r => r.IsActive).Select(r => new { r.FromPath, r.ToUrl }).ToListAsync(cancellationToken);
        foreach (var redirect in redirects)
            index.Redirects.TryAdd(SeoPaths.Normalize(redirect.FromPath), redirect.ToUrl);
        return index;
    }

    /// <summary>
    /// دامنه‌های خود سایت: دامنهٔ ویژگی Search Console (اگر تنظیم شده) و دامنه‌ای که کارکنان پنل با آن باز کرده‌اند.
    /// لینک مطلق به این دامنه‌ها داخلی (بدون درخواست HTTP) بررسی می‌شود.
    /// </summary>
    private async Task<HashSet<string>> OwnHostsAsync(CancellationToken cancellationToken)
    {
        var hosts = new HashSet<string>(StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(SeoEngineHost.Current) && SeoEngineHost.Current is not ("localhost" or "127.0.0.1"))
            hosts.Add(SeoPaths.BareHost(SeoEngineHost.Current));
        var site = await context.SeoToolsConfigs.AsNoTracking().Select(c => c.SearchConsoleSite).FirstOrDefaultAsync(cancellationToken);
        if (SearchConsoleSites.Host(site) is { } host)
            hosts.Add(host);
        return hosts;
    }

    /// <summary>صفحه‌های فعال با متن HTML. kind/id = فقط یک صفحه (برای اصلاح).</summary>
    private async Task<List<Source>> LoadSourcesAsync(SeoEntityKind? kind, Guid? id, CancellationToken cancellationToken)
    {
        var list = new List<Source>();
        if (kind is null or SeoEntityKind.Product)
            list.AddRange((await context.Products.AsNoTracking().Where(p => p.IsActive && (id == null || p.Id == id) && p.Description != null && p.Description.Contains("<"))
                .Select(p => new { p.Id, p.Name, p.Slug, p.Description }).ToListAsync(cancellationToken))
                .Select(p => new Source(SeoEntityKind.Product, p.Id, p.Name, "/product/" + p.Slug, p.Description)));
        if (kind is null or SeoEntityKind.Category)
            list.AddRange((await context.Categories.AsNoTracking().Where(c => c.IsActive && (id == null || c.Id == id) && c.Description != null && c.Description.Contains("<"))
                .Select(c => new { c.Id, c.Name, c.Slug, c.Description }).ToListAsync(cancellationToken))
                .Select(c => new Source(SeoEntityKind.Category, c.Id, c.Name, "/category/" + c.Slug, c.Description)));
        if (kind is null or SeoEntityKind.Brand)
            list.AddRange((await context.Brands.AsNoTracking().Where(b => b.IsActive && (id == null || b.Id == id) && b.Description != null && b.Description.Contains("<"))
                .Select(b => new { b.Id, b.Name, b.Slug, b.Description }).ToListAsync(cancellationToken))
                .Select(b => new Source(SeoEntityKind.Brand, b.Id, b.Name, "/brand/" + b.Slug, b.Description)));
        if (kind is null or SeoEntityKind.BlogPost)
            list.AddRange((await context.BlogPosts.AsNoTracking().Where(p => p.IsPublished && (id == null || p.Id == id) && p.Content != null && p.Content.Contains("<"))
                .Select(p => new { p.Id, p.Title, p.Slug, p.Content }).ToListAsync(cancellationToken))
                .Select(p => new Source(SeoEntityKind.BlogPost, p.Id, p.Title, "/blog/" + p.Slug, p.Content)));
        if (kind is null or SeoEntityKind.ContentPage)
            list.AddRange((await context.ContentPages.AsNoTracking().Where(p => p.IsActive && (id == null || p.Id == id) && p.Content != null && p.Content.Contains("<"))
                .Select(p => new { p.Id, p.Title, p.Slug, p.Content }).ToListAsync(cancellationToken))
                .Select(p => new Source(SeoEntityKind.ContentPage, p.Id, p.Title, "/page/" + p.Slug, p.Content)));
        return list;
    }

    // ── پنل ──────────────────────────────────────────────────────────────

    public async Task<SeoLinkDashboard> GetDashboardAsync(CancellationToken cancellationToken = default)
    {
        var config = await SeoToolsConfigStore.GetAsync(context, tracking: false, cancellationToken);
        var rows = await context.SeoLinkIssues.AsNoTracking()
            .GroupBy(i => new { i.Problem, i.IsIgnored })
            .Select(g => new { g.Key.Problem, g.Key.IsIgnored, Count = g.Count() })
            .ToListAsync(cancellationToken);
        var active = rows.Where(r => !r.IsIgnored).ToList();
        return new SeoLinkDashboard
        {
            Total = active.Sum(r => r.Count),
            Serious = active.Where(r => SeoLinkRules.Severity(r.Problem) >= 3).Sum(r => r.Count),
            Redirected = active.Where(r => r.Problem == SeoLinkProblem.Redirected).Sum(r => r.Count),
            Ignored = rows.Where(r => r.IsIgnored).Sum(r => r.Count),
            ByProblem = active.GroupBy(r => r.Problem).ToDictionary(g => g.Key, g => g.Sum(r => r.Count)),
            Enabled = config.IsLinkCheckEnabled,
            CheckExternal = config.CheckExternalLinks,
            LastRunAt = config.LinkCheckAt,
            LastSummary = config.LinkCheckSummary,
        };
    }

    public async Task<PagedList<SeoLinkIssue>> ListAsync(SeoLinkQuery query, CancellationToken cancellationToken = default)
    {
        var items = context.SeoLinkIssues.AsNoTracking().AsQueryable();
        items = query.Tab switch
        {
            "ignored" => items.Where(i => i.IsIgnored),
            "internal" => items.Where(i => !i.IsIgnored && i.IsInternal && !i.IsImage),
            "external" => items.Where(i => !i.IsIgnored && !i.IsInternal && !i.IsImage),
            "images" => items.Where(i => !i.IsIgnored && i.IsImage),
            _ => items.Where(i => !i.IsIgnored),
        };
        if (query.Problem is { } problem)
            items = items.Where(i => i.Problem == problem);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            items = items.Where(i => i.EntityTitle.Contains(term) || i.Url.Contains(term) || (i.AnchorText != null && i.AnchorText.Contains(term)));
        }

        var total = await items.CountAsync(cancellationToken);
        var take = Math.Clamp(query.Take, 10, 100);
        var page = Math.Clamp(query.Page, 1, Math.Max(1, (total + take - 1) / take));
        // جدی‌ترها اول: ۴۰۴/۴۱۰، بعد خطای سرور/در دسترس نبودن، بعد ریدایرکت و مسدود.
        var list = await items
            .OrderByDescending(i => i.Problem == SeoLinkProblem.NotFound || i.Problem == SeoLinkProblem.Gone)
            .ThenByDescending(i => i.Problem == SeoLinkProblem.ServerError || i.Problem == SeoLinkProblem.Unreachable || i.Problem == SeoLinkProblem.Insecure)
            .ThenByDescending(i => i.FirstSeenAt)
            .ThenBy(i => i.Id)
            .Skip((page - 1) * take)
            .Take(take)
            .ToListAsync(cancellationToken);
        return new PagedList<SeoLinkIssue>(list, total, page, take);
    }

    public async Task<OperationResult> FixAsync(Guid issueId, string? newUrl, CancellationToken cancellationToken = default)
    {
        newUrl = string.IsNullOrWhiteSpace(newUrl) ? null : newUrl.Trim();
        if (newUrl is not null && !SeoLinkRewriter.IsSafeUrl(newUrl))
            return OperationResult.Error("آدرس تازه باید با «/» شروع شود (لینک داخلی) یا آدرس کامل http/https باشد.");

        var issue = await context.SeoLinkIssues.AsTracking().FirstOrDefaultAsync(i => i.Id == issueId, cancellationToken);
        if (issue is null)
            return OperationResult.NotFound("این مورد دیگر وجود ندارد (احتمالاً قبلاً اصلاح شده).");

        var changed = await RewriteAsync(issue.Kind, issue.EntityId, issue.Url, issue.IsImage, newUrl, cancellationToken);
        if (changed < 0)
            return OperationResult.Error("صفحهٔ این لینک پیدا نشد (حذف یا غیرفعال شده).");
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            context.ChangeTracker.Clear();
            logger.LogError(ex, "Link fix failed for issue {Id}", issueId);
            return OperationResult.Error("ذخیرهٔ متن صفحه ناموفق بود؛ چیزی تغییر نکرد.");
        }
        // حذف واقعی (نه حذف نرم) تا جدول گزارش فقط مشکل‌های فعلی را داشته باشد.
        await context.SeoLinkIssues.Where(i => i.Id == issueId).ExecuteDeleteAsync(cancellationToken);

        if (changed == 0)
            return OperationResult.Success("این لینک دیگر در متن صفحه نبود؛ مورد از فهرست برداشته شد.");
        return OperationResult.Success(newUrl is null
            ? $"لینک از متن «{issue.EntityTitle}» برداشته شد ({PersianText.Digits(changed)} مورد)؛ متن آن سر جایش ماند."
            : $"آدرس در متن «{issue.EntityTitle}» عوض شد ({PersianText.Digits(changed)} مورد).");
    }

    public async Task<OperationResult<int>> FixRedirectsAsync(CancellationToken cancellationToken = default)
    {
        var issues = await context.SeoLinkIssues.AsTracking()
            .Where(i => !i.IsIgnored && i.IsInternal && i.Problem == SeoLinkProblem.Redirected && i.SuggestedUrl != null)
            .ToListAsync(cancellationToken);
        if (issues.Count == 0)
            return OperationResult<int>.Error("لینک داخلی ریدایرکت‌داری برای اصلاح نیست.");

        var fixedCount = 0;
        var done = new List<Guid>();
        foreach (var issue in issues)
        {
            if (!SeoLinkRewriter.IsSafeUrl(issue.SuggestedUrl))
                continue;
            var changed = await RewriteAsync(issue.Kind, issue.EntityId, issue.Url, issue.IsImage, issue.SuggestedUrl, cancellationToken);
            if (changed > 0)
                fixedCount++;
            done.Add(issue.Id); // ۰ = لینک دیگر در متن نیست، ۱- = صفحه حذف شده؛ در هر دو حالت ردیف گزارش کهنه است
        }
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            context.ChangeTracker.Clear();
            logger.LogError(ex, "Bulk redirect fix failed");
            return OperationResult<int>.Error("ذخیره ناموفق بود؛ چیزی تغییر نکرد.");
        }
        foreach (var chunk in done.Chunk(500))
            await context.SeoLinkIssues.Where(i => chunk.Contains(i.Id)).ExecuteDeleteAsync(cancellationToken);
        return OperationResult<int>.Success(fixedCount, $"{PersianText.Digits(fixedCount)} لینک به آدرس نهایی‌اش عوض شد.");
    }

    /// <summary>
    /// جایگزینی در متن خود صفحه (ردیف tracked؛ ذخیره با فراخواننده). خروجی: تعداد تغییر، یا ۱- اگر صفحه پیدا نشد.
    /// اگر در یک فراخوانی چند لینک یک صفحه عوض شود، همان ردیف tracked دوباره استفاده می‌شود.
    /// </summary>
    private async Task<int> RewriteAsync(SeoEntityKind kind, Guid id, string url, bool isImage, string? newUrl, CancellationToken cancellationToken)
    {
        switch (kind)
        {
            case SeoEntityKind.Product:
            {
                var entity = await context.Products.AsTracking().FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
                if (entity is null) return -1;
                var (html, changed) = SeoLinkRewriter.Replace(entity.Description, url, isImage, newUrl);
                if (changed > 0) entity.Description = html;
                return changed;
            }
            case SeoEntityKind.Category:
            {
                var entity = await context.Categories.AsTracking().FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
                if (entity is null) return -1;
                var (html, changed) = SeoLinkRewriter.Replace(entity.Description, url, isImage, newUrl);
                if (changed > 0) entity.Description = html;
                return changed;
            }
            case SeoEntityKind.Brand:
            {
                var entity = await context.Brands.AsTracking().FirstOrDefaultAsync(b => b.Id == id, cancellationToken);
                if (entity is null) return -1;
                var (html, changed) = SeoLinkRewriter.Replace(entity.Description, url, isImage, newUrl);
                if (changed > 0) entity.Description = html;
                return changed;
            }
            case SeoEntityKind.BlogPost:
            {
                var entity = await context.BlogPosts.AsTracking().FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
                if (entity is null) return -1;
                var (html, changed) = SeoLinkRewriter.Replace(entity.Content, url, isImage, newUrl);
                if (changed > 0) entity.Content = html;
                return changed;
            }
            case SeoEntityKind.ContentPage:
            {
                var entity = await context.ContentPages.AsTracking().FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
                if (entity is null) return -1;
                var (html, changed) = SeoLinkRewriter.Replace(entity.Content, url, isImage, newUrl);
                if (changed > 0) entity.Content = html;
                return changed;
            }
            default:
                return -1;
        }
    }

    public async Task<OperationResult> IgnoreAsync(Guid issueId, bool ignore, CancellationToken cancellationToken = default)
    {
        var issue = await context.SeoLinkIssues.AsTracking().FirstOrDefaultAsync(i => i.Id == issueId, cancellationToken);
        if (issue is null)
            return OperationResult.NotFound("این مورد دیگر وجود ندارد.");
        issue.IsIgnored = ignore;
        await context.SaveChangesAsync(cancellationToken);
        return OperationResult.Success(ignore
            ? "نادیده گرفته شد؛ تا وقتی همین لینک در متن باشد دوباره گزارش نمی‌شود."
            : "به فهرست مشکل‌ها برگشت.");
    }

    public async Task<OperationResult> SaveSettingsAsync(bool enabled, bool checkExternal, CancellationToken cancellationToken = default)
    {
        var config = await SeoToolsConfigStore.GetAsync(context, tracking: true, cancellationToken);
        config.IsLinkCheckEnabled = enabled;
        config.CheckExternalLinks = checkExternal;
        await context.SaveChangesAsync(cancellationToken);
        return OperationResult.Success("تنظیمات یابندهٔ لینک ذخیره شد.");
    }

    private static string? Cut(string? value, int max) => value is null ? null : value.Length <= max ? value : value[..max];
}

/// <summary>خواندن/ساختن تنها ردیف <see cref="SeoToolsConfig"/>.</summary>
public static class SeoToolsConfigStore
{
    /// <summary>ردیف تنظیمات؛ اگر نبود ساخته می‌شود (با tracking=true ذخیره با فراخواننده است، با false فقط نمونهٔ پیش‌فرض).</summary>
    public static async Task<SeoToolsConfig> GetAsync(FratakShopDbContext context, bool tracking, CancellationToken cancellationToken)
    {
        var query = tracking ? context.SeoToolsConfigs.AsTracking() : context.SeoToolsConfigs.AsNoTracking();
        var config = await query.OrderBy(c => c.CreatedAt).FirstOrDefaultAsync(cancellationToken);
        if (config is not null)
            return config;
        config = new SeoToolsConfig();
        if (tracking)
            context.SeoToolsConfigs.Add(config);
        return config;
    }
}
