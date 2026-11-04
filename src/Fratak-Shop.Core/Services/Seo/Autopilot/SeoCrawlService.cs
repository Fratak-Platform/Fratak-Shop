using System.Text.Json;
using Fratak_Shop.Core.Services.Seo.Engine;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.SeoEngine;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Fratak_Shop.Core.Services.Seo.Autopilot;

/// <summary>پاسخ یک درخواست خزش.</summary>
/// <param name="Status">کد HTTP (۰ = خطای شبکه).</param>
/// <param name="Html">بدنه (فقط وقتی text/html است).</param>
/// <param name="Location">مقصد ریدایرکت (برای ۳xx).</param>
/// <param name="Milliseconds">زمان پاسخ.</param>
public sealed record CrawlResponse(int Status, string? Html, string? Location, int Milliseconds);

/// <summary>
/// گرفتن یک صفحهٔ خود سایت (پیاده‌سازی در Web با HttpClient و User-Agent «FratakSeoBot»).
/// </summary>
public interface ISeoPageFetcher
{
    /// <summary>آدرس نسبی (مثال: «/product/x») را بدون دنبال کردن ریدایرکت می‌گیرد. هرگز استثنا نمی‌دهد.</summary>
    Task<CrawlResponse> FetchAsync(string path, CancellationToken cancellationToken);
}

/// <summary>
/// خزندهٔ سئوی داخلی (موارد ۶۸، ۱۵۳، ۱۵۹ و ۲۰۶ پیشنهاد سئو): هر شب سایت را مثل ربات گوگل از صفحهٔ اصلی و نقشهٔ سایت
/// می‌خزد، هر صفحه را با <see cref="PageAuditor"/> بررسی می‌کند و نتیجه را به «کارهای سئو» (SeoTask) تبدیل می‌کند:
/// صفحهٔ خطادار، زنجیرهٔ ریدایرکت، صفحهٔ یتیم/عمیق، عنوان/توضیحات تکراری، محتوای تقریباً تکراری، تصویر بدون Alt و ...
/// <para>کاری که خودکار درست‌شدنی است (Alt، کامنت، WebP) را خلبان خودکار انجام می‌دهد؛ بقیه برای انسان با اولویت فهرست می‌شود.
/// مشکلی که در خزش بعدی دیده نشود خودکار «حل‌شده» می‌شود.</para>
/// </summary>
public interface ISeoCrawlService
{
    /// <summary>یک خزش کامل. <paramref name="sitemapPaths"/> = آدرس‌های نقشهٔ سایت.</summary>
    Task<SeoCrawlRun> CrawlAsync(IReadOnlyCollection<string> sitemapPaths, int maxPages, CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="ISeoCrawlService"/>
public sealed class SeoCrawlService(FratakShopDbContext context, ISeoPageFetcher fetcher, ILogger<SeoCrawlService> logger, ISeoHost? host = null) : ISeoCrawlService
{
    /// <summary>تعداد خزش‌هایی که نگه داشته می‌شوند (قدیمی‌ترها پاک می‌شوند).</summary>
    public const int KeepRuns = 8;

    /// <summary>فاصلهٔ بین درخواست‌ها (میلی‌ثانیه) تا سرور زیر بار نرود.</summary>
    public const int DelayMs = 250;

    private static readonly string[] SkipPrefixes =
    [
        "/admin", "/api", "/account", "/profile", "/cart", "/checkout", "/login", "/register", "/logout", "/verify", "/pay",
        "/hubs", "/media", "/uploads", "/assets", "/lib", "/css", "/js", "/compare", "/track-order", "/welcome",
    ];

    public async Task<SeoCrawlRun> CrawlAsync(IReadOnlyCollection<string> sitemapPaths, int maxPages, CancellationToken cancellationToken = default)
    {
        var run = new SeoCrawlRun { StartedAt = DateTimeOffset.UtcNow };
        context.SeoCrawlRuns.Add(run);
        await context.SaveChangesAsync(cancellationToken);

        var sitemap = sitemapPaths.Select(NormalizePath).Where(p => p is not null).Select(p => p!).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<(string Path, int Depth)>();
        var depth = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var inLinks = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var results = new List<(SeoCrawlPage Page, PageAudit? Audit)>();

        void Enqueue(string path, int d)
        {
            if (depth.ContainsKey(path))
                return;
            depth[path] = d;
            queue.Enqueue((path, d));
        }

        Enqueue("/", 0);
        // صفحه‌های نقشهٔ سایت که از لینک‌ها پیدا نشوند هم خزیده می‌شوند (عمق ۹۹ = یتیم تا خلافش ثابت شود).
        var pending = new Queue<string>(sitemap);
        try
        {
            while ((queue.Count > 0 || pending.Count > 0) && results.Count < maxPages)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (queue.Count == 0)
                    Enqueue(pending.Dequeue(), 99);
                if (queue.Count == 0)
                    continue;
                var (path, d) = queue.Dequeue();

                var response = await fetcher.FetchAsync(path, cancellationToken);
                var page = new SeoCrawlPage
                {
                    RunId = run.Id,
                    Path = Cut(path, 500),
                    Status = response.Status,
                    Depth = d,
                    Ms = response.Milliseconds,
                    Bytes = response.Html?.Length ?? 0,
                    InSitemap = sitemap.Contains(path),
                };
                PageAudit? audit = null;
                if (response.Status is >= 300 and < 400 && NormalizePath(response.Location) is { } redirectTarget)
                {
                    page.Canonical = Cut(redirectTarget, 500);
                    Enqueue(redirectTarget, d); // ریدایرکت عمق اضافه نمی‌کند
                }
                else if (response.Status == 200 && response.Html is not null)
                {
                    audit = PageAuditor.Audit(response.Html, path, host?.Host);
                    Fill(page, audit);
                    foreach (var link in audit.Links.Where(l => l.Internal && !l.NoFollow))
                    {
                        if (NormalizePath(link.Href) is not { } target || target == path)
                            continue;
                        inLinks[target] = inLinks.GetValueOrDefault(target) + 1;
                        if (d < 6)
                            Enqueue(target, d + 1);
                    }
                }

                results.Add((page, audit));
                await Task.Delay(DelayMs, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            run.Note = "خزش نیمه‌کاره متوقف شد (خاموش شدن سرور).";
        }

        foreach (var (page, _) in results)
            page.InLinks = inLinks.GetValueOrDefault(page.Path);

        var issues = Analyze(results, sitemap);
        foreach (var (page, audit) in results)
        {
            var list = issues.GetValueOrDefault(page.Path) ?? [];
            page.IssuesJson = list.Count == 0 ? null : JsonSerializer.Serialize(list);
            context.SeoCrawlPages.Add(page);
        }

        run.FinishedAt = DateTimeOffset.UtcNow;
        run.Pages = results.Count;
        var all = issues.Values.SelectMany(i => i).ToList();
        run.Errors = all.Count(c => SeoIssueCatalog.Get(c).Severity == SeoTaskSeverity.Critical);
        run.Warnings = all.Count(c => SeoIssueCatalog.Get(c).Severity == SeoTaskSeverity.Warning);
        run.HealthScore = HealthScore(results.Count, issues);
        run.SummaryJson = JsonSerializer.Serialize(all.GroupBy(c => c).ToDictionary(g => g.Key, g => g.Count()));
        await context.SaveChangesAsync(CancellationToken.None);

        await SyncTasksAsync(issues, CancellationToken.None);
        await PruneAsync(CancellationToken.None);
        logger.LogInformation("SEO crawl finished: {Pages} pages, health {Score}", run.Pages, run.HealthScore);
        return run;
    }

    /// <summary>
    /// نمرهٔ سلامت ۰ تا ۱۰۰: میانگین وزنی «صفحه‌های بی‌مشکل». هر مشکل بحرانی ۱۰، هشدار ۴ و اطلاع ۱ امتیاز از صفحه کم می‌کند.
    /// </summary>
    public static int HealthScore(int pages, IReadOnlyDictionary<string, List<string>> issues)
    {
        if (pages == 0)
            return 0;
        var penalty = issues.Values.Sum(list => Math.Min(100, list.Sum(code => SeoIssueCatalog.Get(code).Severity switch
        {
            SeoTaskSeverity.Critical => 10,
            SeoTaskSeverity.Warning => 4,
            _ => 1,
        })));
        return Math.Clamp(100 - (int)Math.Round(penalty / (double)pages), 0, 100);
    }

    /// <summary>
    /// تحلیل سطح سایت: مشکل‌های تک‌صفحه + تکراری بودن عنوان/توضیحات، محتوای تقریباً یکسان، یتیم، عمیق، کند، سنگین و نقشهٔ سایت.
    /// </summary>
    public static Dictionary<string, List<string>> Analyze(IReadOnlyList<(SeoCrawlPage Page, PageAudit? Audit)> results, IReadOnlySet<string> sitemap)
    {
        var issues = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        void Add(string path, string code)
        {
            if (!issues.TryGetValue(path, out var list))
                issues[path] = list = [];
            if (!list.Contains(code))
                list.Add(code);
        }

        var redirects = results.Where(r => r.Page.Status is >= 300 and < 400).ToDictionary(r => r.Page.Path, r => r.Page.Canonical, StringComparer.OrdinalIgnoreCase);
        foreach (var (page, audit) in results)
        {
            if (page.Status is >= 400 or 0)
                Add(page.Path, "http-error");
            if (page.Status is >= 300 and < 400 && page.Canonical is { } next && redirects.ContainsKey(next))
                Add(page.Path, "redirect-chain");
            if (audit is null)
                continue;
            foreach (var code in audit.Issues)
                Add(page.Path, code);
            if (page.Ms > 1500)
                Add(page.Path, "slow-page");
            if (page.Bytes > 300_000)
                Add(page.Path, "heavy-page");
            if (!audit.NoIndex && page.Depth is > 4 and < 99)
                Add(page.Path, "deep-page");
            if (!audit.NoIndex && page.InLinks == 0 && page.Path != "/")
                Add(page.Path, "orphan");
            if (audit.NoIndex && page.InSitemap)
                Add(page.Path, "noindex-in-sitemap");
            if (!audit.NoIndex && !page.InSitemap && PageAuditor.IsContentPath(page.Path))
                Add(page.Path, "not-in-sitemap");
        }

        var indexable = results.Where(r => r.Audit is { NoIndex: false } && r.Page.Status == 200).ToList();
        foreach (var group in indexable.Where(r => !string.IsNullOrWhiteSpace(r.Audit!.Title)).GroupBy(r => PersianText.Normalize(r.Audit!.Title)).Where(g => g.Count() > 1))
            foreach (var r in group)
                Add(r.Page.Path, "title-duplicate");
        foreach (var group in indexable.Where(r => !string.IsNullOrWhiteSpace(r.Audit!.Description)).GroupBy(r => PersianText.Normalize(r.Audit!.Description)).Where(g => g.Count() > 1))
            foreach (var r in group)
                Add(r.Page.Path, "meta-duplicate");

        // محتوای تقریباً یکسان (فقط صفحه‌های محتوایی؛ فهرست‌ها طبیعتاً شبیه‌اند)
        var content = indexable.Where(r => r.Audit!.SimHash != 0 && PageAuditor.IsContentPath(r.Page.Path)).ToList();
        for (var i = 0; i < content.Count; i++)
        {
            for (var j = i + 1; j < content.Count; j++)
            {
                if (SeoPenaltyGuard.Distance(content[i].Audit!.SimHash, content[j].Audit!.SimHash) <= SeoPenaltyGuard.NearDuplicateDistance)
                {
                    Add(content[i].Page.Path, "near-duplicate");
                    Add(content[j].Page.Path, "near-duplicate");
                }
            }
        }

        return issues;
    }

    /// <summary>
    /// همگام‌سازی «کارهای سئو»: برای هر نوع مشکل یک کار (با فهرست صفحه‌ها)؛ کار باز که دیگر دیده نشد ← حل‌شده.
    /// کار «نادیده گرفته‌شده» دوباره باز نمی‌شود.
    /// </summary>
    private async Task SyncTasksAsync(Dictionary<string, List<string>> issues, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var byCode = issues.SelectMany(kv => kv.Value.Select(code => (Code: code, Path: kv.Key)))
            .GroupBy(x => x.Code)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Path).OrderBy(p => p.Length).ToList());

        var existing = await context.SeoTasks.AsTracking().Where(t => t.Fingerprint.StartsWith("crawl:")).ToListAsync(cancellationToken);
        var map = existing.ToDictionary(t => t.Fingerprint, StringComparer.Ordinal);
        foreach (var (code, paths) in byCode)
        {
            var info = SeoIssueCatalog.Get(code);
            var fingerprint = "crawl:" + code;
            var detail = $"{info.Fix} — {PersianText.Digits(paths.Count)} صفحه: {string.Join("، ", paths.Take(15))}{(paths.Count > 15 ? " …" : "")}";
            if (!map.TryGetValue(fingerprint, out var task))
            {
                task = new SeoTask { Fingerprint = fingerprint, Code = code, Status = SeoTaskStatus.Open };
                context.SeoTasks.Add(task);
            }
            else if (task.Status is SeoTaskStatus.Fixed or SeoTaskStatus.AutoFixed)
            {
                task.Status = SeoTaskStatus.Open; // مشکل برگشته
                task.ResolvedAt = null;
                task.ResolvedBy = null;
            }

            task.Severity = info.Severity;
            task.Title = Cut($"{info.Title} ({PersianText.Digits(paths.Count)})", 300);
            task.Detail = Cut(detail, 2000);
            task.Path = Cut(paths[0], 500);
            task.Impact = Math.Clamp(info.Impact + Math.Min(20, paths.Count), 1, 100);
            task.AutoFixable = info.AutoFixable;
            task.LastSeenAt = now;
        }

        foreach (var task in existing.Where(t => t.Status == SeoTaskStatus.Open && !byCode.ContainsKey(t.Code)))
        {
            task.Status = task.AutoFixable ? SeoTaskStatus.AutoFixed : SeoTaskStatus.Fixed;
            task.ResolvedAt = now;
            task.ResolvedBy = "خزش سئو";
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    private async Task PruneAsync(CancellationToken cancellationToken)
    {
        var old = await context.SeoCrawlRuns.AsNoTracking().OrderByDescending(r => r.StartedAt).Skip(KeepRuns).Select(r => r.Id).ToListAsync(cancellationToken);
        if (old.Count == 0)
            return;
        await context.SeoCrawlPages.IgnoreQueryFilters().Where(p => old.Contains(p.RunId)).ExecuteDeleteAsync(cancellationToken);
        await context.SeoCrawlRuns.IgnoreQueryFilters().Where(r => old.Contains(r.Id)).ExecuteDeleteAsync(cancellationToken);
    }

    private static void Fill(SeoCrawlPage page, PageAudit audit)
    {
        page.Title = audit.Title is null ? null : Cut(audit.Title, 300);
        page.Description = audit.Description is null ? null : Cut(audit.Description, 500);
        page.H1 = audit.H1.FirstOrDefault() is { } h1 ? Cut(h1, 300) : null;
        page.H1Count = audit.H1.Count;
        page.Words = audit.Words;
        page.Images = audit.Images.Count;
        page.ImagesWithoutAlt = audit.Images.Count(i => string.IsNullOrWhiteSpace(i.Alt));
        page.OutLinks = audit.Links.Count(l => l.Internal);
        page.ExternalLinks = audit.Links.Count(l => !l.Internal);
        page.Canonical = audit.Canonical is null ? null : Cut(audit.Canonical, 500);
        page.NoIndex = audit.NoIndex;
        page.SchemaTypes = audit.SchemaTypes.Count == 0 ? null : Cut(string.Join(",", audit.SchemaTypes.Distinct()), 300);
        page.SchemaErrors = audit.SchemaErrors;
        page.SimHash = audit.SimHash;
    }

    /// <summary>
    /// آدرس قابل خزش (نسبی، بدون پرس‌وجو و #). آدرس خصوصی/فایل/خارجی ← null.
    /// مثال: «https://shop.fratak.ir/product/x?utm=1#a» ← «/product/x»؛ «/admin» ← null.
    /// </summary>
    public static string? NormalizePath(string? href)
    {
        if (string.IsNullOrWhiteSpace(href))
            return null;
        var value = href.Trim();
        if (Uri.TryCreate(value, UriKind.Absolute, out var absolute))
        {
            if (absolute.Scheme is not ("http" or "https"))
                return null;
            value = absolute.PathAndQuery;
        }

        if (!value.StartsWith('/') || value.StartsWith("//", StringComparison.Ordinal))
            return null;
        var cut = value.IndexOfAny(['?', '#']);
        if (cut >= 0)
            value = value[..cut];
        if (value.Length > 1)
            value = value.TrimEnd('/');
        var lower = value.ToLowerInvariant();
        if (SkipPrefixes.Any(p => lower == p || lower.StartsWith(p + "/", StringComparison.Ordinal)))
            return null;
        if (System.IO.Path.HasExtension(lower) && !lower.EndsWith(".html", StringComparison.Ordinal))
            return null;
        return value.Length > 500 ? null : value;
    }

    private static string Cut(string value, int max) => value.Length <= max ? value : value[..max];
}
