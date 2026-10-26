using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Core.Services.Security;
using Fratak_Shop.Core.Services.Seo.Engine;
using Fratak_Shop.Core.Services.Seo.Tools;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.SeoEngine;
using Fratak_Shop.Data.Entities.SeoRedirects;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Fratak_Shop.Tests.Integration;

/// <summary>
/// تست ابزارهای سئو روی SQL Server واقعی (مرحلهٔ هفتم – فاز ۸ب). شبکه با پیاده‌سازی‌های ساختگی جایگزین شده است:
/// • یابندهٔ لینک: لینک داخلی خراب/ریدایرکت‌دار، لینک بیرونی ۴۰۴ (با کش)، تصویر ناامن و فایل گم‌شده ← اصلاح ریدایرکت‌ها،
///   برداشتن لینک (متن می‌ماند)، نادیده گرفتن، و پاک شدن موارد اصلاح‌شده در اجرای بعدی.
/// • ویرایش گروهی: ذخیره با پاک‌سازی و «بررسی‌شده»، محافظ ویرایش هم‌زمان، پیش‌نمایش و اعمال جایگزینی.
/// • Search Console: ذخیرهٔ کلید (رمزنگاری‌شده)، آزمایش اتصال، همگام‌سازی، گزارش، و خطای دسترسی با راهنمای فارسی.
/// </summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", "Integration")]
public class SeoToolsIntegrationTests(SqlServerFixture db)
{
    private static FratakShopDbContext Ctx(AsyncServiceScope scope) => scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();

    // ═══ یابندهٔ لینک شکسته ═════════════════════════════════════════════

    /// <summary>بررسی ساختگی: «gone.example» ۴۰۴ می‌دهد، بقیه سالم‌اند؛ فایل‌هایی که «missing» دارند وجود ندارند.</summary>
    private sealed class FakeProbe : ISeoLinkProbe
    {
        public ConcurrentDictionary<string, int> Calls { get; } = new(StringComparer.Ordinal);

        public IReadOnlySet<string> StaticPaths { get; } = new HashSet<string>(StringComparer.Ordinal) { "/" };

        public bool LocalFileExists(string path) => !path.Contains("missing", StringComparison.Ordinal);

        public Task<LinkProbeResult> CheckExternalAsync(Uri url, CancellationToken cancellationToken)
        {
            Calls.AddOrUpdate(url.AbsoluteUri, 1, (_, count) => count + 1);
            return Task.FromResult(url.Host == "gone.example" ? new LinkProbeResult(404) : new LinkProbeResult(200));
        }
    }

    private static SeoLinkAuditService Audit(AsyncServiceScope scope, FakeProbe probe) =>
        new(Ctx(scope), probe, scope.ServiceProvider.GetRequiredService<IStaffNotificationService>(), NullLogger<SeoLinkAuditService>.Instance);

    private async Task<List<SeoLinkIssue>> IssuesAsync(Guid productId)
    {
        await using var scope = db.Scope();
        return await Ctx(scope).SeoLinkIssues.AsNoTracking().Where(i => i.EntityId == productId).ToListAsync();
    }

    private async Task<string> DescriptionAsync(Guid productId)
    {
        await using var scope = db.Scope();
        return (await Ctx(scope).Products.AsNoTracking().SingleAsync(p => p.Id == productId)).Description ?? "";
    }

    [Fact]
    public async Task Link_audit_finds_fixes_and_forgets_broken_links()
    {
        var productId = await db.CreateProductAsync();
        var targetId = await db.CreateProductAsync();
        var key = Guid.NewGuid().ToString("N")[..8];
        string targetSlug;
        await using (var scope = db.Scope())
        {
            var context = Ctx(scope);
            targetSlug = (await context.Products.AsNoTracking().SingleAsync(p => p.Id == targetId)).Slug;
            var product = await context.Products.AsTracking().SingleAsync(p => p.Id == productId);
            product.Description =
                $"<p><a href=\"/product/{targetSlug}\">سالم</a> و <a href=\"/product/missing-{key}\">محصول خراب</a> و " +
                $"<a href=\"/product/old-{key}\">نامک قدیمی</a> و <a href=\"https://gone.example/{key}\">منبع بیرونی</a> و " +
                $"<a href=\"https://ok.example/{key}\">منبع سالم</a></p>" +
                $"<p><img src=\"http://insecure.example/{key}.jpg\" alt=\"ناامن\"><img src=\"/uploads/products/missing-{key}.webp\" alt=\"بی‌فایل\"></p>";
            context.SeoRedirects.Add(new SeoRedirect { FromPath = $"/product/old-{key}", ToUrl = $"/product/{targetSlug}" });
            await context.SaveChangesAsync();
        }

        var probe = new FakeProbe();
        await using (var scope = db.Scope())
        {
            var result = await Audit(scope, probe).RunAsync(manual: true);
            Assert.True(result.Ran, result.Message);
            Assert.True(result.Links >= 7);
        }

        var issues = await IssuesAsync(productId);
        Assert.Equal(5, issues.Count);
        var missing = Assert.Single(issues, i => i.Url == $"/product/missing-{key}");
        Assert.Equal(SeoLinkProblem.NotFound, missing.Problem);
        Assert.True(missing.IsInternal);
        Assert.Equal("محصول خراب", missing.AnchorText);
        var redirected = Assert.Single(issues, i => i.Url == $"/product/old-{key}");
        Assert.Equal(SeoLinkProblem.Redirected, redirected.Problem);
        Assert.Equal($"/product/{targetSlug}", redirected.SuggestedUrl);
        var gone = Assert.Single(issues, i => i.Url == $"https://gone.example/{key}");
        Assert.Equal(SeoLinkProblem.NotFound, gone.Problem);
        Assert.Equal(404, gone.StatusCode);
        Assert.False(gone.IsInternal);
        var insecure = Assert.Single(issues, i => i.Url == $"http://insecure.example/{key}.jpg");
        Assert.Equal(SeoLinkProblem.Insecure, insecure.Problem);
        Assert.Equal($"https://insecure.example/{key}.jpg", insecure.SuggestedUrl);
        Assert.True(insecure.IsImage);
        Assert.Equal(SeoLinkProblem.NotFound, Assert.Single(issues, i => i.Url == $"/uploads/products/missing-{key}.webp").Problem);
        Assert.All(issues, i => Assert.Equal(SeoEntityKind.Product, i.Kind));

        await using (var scope = db.Scope())
        {
            var audit = Audit(scope, probe);

            // آدرس ناامن هرگز در متن نوشته نمی‌شود.
            Assert.Equal(EnOperationResultStatus.Error, (await audit.FixAsync(gone.Id, "javascript:alert(1)")).Status);

            var fixedRedirects = await audit.FixRedirectsAsync();
            Assert.Equal(EnOperationResultStatus.Success, fixedRedirects.Status);
            Assert.True(fixedRedirects.Data >= 1);

            var unlinked = await audit.FixAsync(missing.Id, null);
            Assert.Equal(EnOperationResultStatus.Success, unlinked.Status);

            Assert.Equal(EnOperationResultStatus.Success, (await audit.IgnoreAsync(gone.Id, true)).Status);
        }

        var description = await DescriptionAsync(productId);
        Assert.DoesNotContain($"/product/old-{key}", description);
        Assert.Equal(2, description.Split($"href=\"/product/{targetSlug}\"").Length - 1); // لینک ریدایرکت‌دار مستقیم شد
        Assert.DoesNotContain($"/product/missing-{key}", description);
        Assert.Contains("محصول خراب", description); // متن لینک برداشته‌شده سر جایش ماند
        Assert.Contains($"https://gone.example/{key}", description);

        // اجرای دوباره: موارد اصلاح‌شده رفته‌اند، نادیده‌گرفته‌شده می‌ماند و لینک بیرونی از کش خوانده می‌شود.
        await using (var scope = db.Scope())
            Assert.True((await Audit(scope, probe).RunAsync(manual: true)).Ran);

        var after = await IssuesAsync(productId);
        Assert.Equal(3, after.Count);
        Assert.True(Assert.Single(after, i => i.Url == $"https://gone.example/{key}").IsIgnored);
        Assert.DoesNotContain(after, i => i.Url == $"/product/old-{key}" || i.Url == $"/product/missing-{key}");
        Assert.Equal(1, probe.Calls[$"https://gone.example/{key}"]);

        await using (var scope = db.Scope())
        {
            var audit = Audit(scope, probe);
            var ignored = await audit.ListAsync(new SeoLinkQuery("ignored", null, key, 1));
            Assert.Equal(gone.Id, Assert.Single(ignored.Items).Id);
            var images = await audit.ListAsync(new SeoLinkQuery("images", null, key, 1));
            Assert.Equal(2, images.Total);
            var notFound = await audit.ListAsync(new SeoLinkQuery("all", SeoLinkProblem.NotFound, key, 1));
            Assert.Equal($"/uploads/products/missing-{key}.webp", Assert.Single(notFound.Items).Url);

            var dashboard = await audit.GetDashboardAsync();
            Assert.True(dashboard.Total >= 2);
            Assert.True(dashboard.Ignored >= 1);
            Assert.NotNull(dashboard.LastRunAt);
            Assert.False(string.IsNullOrWhiteSpace(dashboard.LastSummary));
        }
    }

    [Fact]
    public async Task Link_audit_respects_the_settings()
    {
        await using (var scope = db.Scope())
        {
            var audit = Audit(scope, new FakeProbe());
            Assert.Equal(EnOperationResultStatus.Success, (await audit.SaveSettingsAsync(enabled: false, checkExternal: false)).Status);
            // اجرای خودکار وقتی خاموش است کاری نمی‌کند؛ اجرای دستی همیشه اجرا می‌شود.
            Assert.False((await audit.RunAsync(manual: false)).Ran);
        }

        var probe = new FakeProbe();
        await using (var scope = db.Scope())
        {
            var result = await Audit(scope, probe).RunAsync(manual: true);
            Assert.True(result.Ran);
            Assert.Empty(probe.Calls); // لینک‌های بیرونی بررسی نشدند
            Assert.Contains("خاموش", result.Message);
        }

        await using (var scope = db.Scope())
        {
            var audit = Audit(scope, probe);
            await audit.SaveSettingsAsync(enabled: true, checkExternal: true);
            var dashboard = await audit.GetDashboardAsync();
            Assert.True(dashboard.Enabled);
            Assert.True(dashboard.CheckExternal);
        }
    }

    // ═══ ویرایش گروهی ═══════════════════════════════════════════════════

    [Fact]
    public async Task Bulk_edit_saves_cleaned_values_and_guards_concurrent_edits()
    {
        var productId = await db.CreateProductAsync();
        var userId = await db.CreateUserAsync();
        string name;
        await using (var scope = db.Scope())
            name = (await Ctx(scope).Products.AsNoTracking().SingleAsync(p => p.Id == productId)).Name;

        await using (var scope = db.Scope())
        {
            var bulk = scope.ServiceProvider.GetRequiredService<ISeoBulkEditService>();
            var saved = await bulk.SaveAsync(SeoEntityKind.Product,
                [new SeoBulkChange(productId, null, "  عنوان   تازه  ", null, "توضیح متای تازه برای تست", null, "کلمه تست")], userId);
            Assert.Equal(EnOperationResultStatus.Success, saved.Status);
            Assert.Equal(1, saved.Data!.Saved);

            // ردیف بدون تغییر ذخیره نمی‌شود.
            var nothing = await bulk.SaveAsync(SeoEntityKind.Product,
                [new SeoBulkChange(productId, "عنوان تازه", "عنوان تازه", null, null, null, null)], userId);
            Assert.Equal(EnOperationResultStatus.Error, nothing.Status);
        }

        await using (var scope = db.Scope())
        {
            var context = Ctx(scope);
            var product = await context.Products.AsNoTracking().SingleAsync(p => p.Id == productId);
            Assert.Equal("عنوان تازه", product.Seo?.Title);
            Assert.Equal("توضیح متای تازه برای تست", product.Seo?.MetaDescription);
            Assert.Equal("کلمه تست", product.Seo?.FocusKeyword);
            Assert.True(product.Seo?.IsReviewed);
            Assert.Equal(userId, product.Seo?.ReviewedByUserId);
            // نمره همان لحظه دوباره حساب شد.
            Assert.True(await context.SeoScores.AnyAsync(s => s.Kind == SeoEntityKind.Product && s.EntityId == productId));
        }

        await using (var scope = db.Scope())
        {
            var bulk = scope.ServiceProvider.GetRequiredService<ISeoBulkEditService>();
            // همکار عنوان را در این فاصله عوض کرده بود (عنوان اولیهٔ فرم این کاربر خالی بود).
            var stale = await bulk.SaveAsync(SeoEntityKind.Product,
                [new SeoBulkChange(productId, null, "عنوان دیگر", null, null, null, null)], userId);
            Assert.Equal(EnOperationResultStatus.Error, stale.Status);
            Assert.Contains("کس دیگری", stale.Message);

            var listed = await bulk.ListAsync(new SeoBulkQuery(SeoEntityKind.Product, "all", name, 1));
            var row = Assert.Single(listed.Items, r => r.Id == productId);
            Assert.Equal("عنوان تازه", row.SeoTitle);
            Assert.Equal($"/product/{(await Ctx(scope).Products.AsNoTracking().SingleAsync(p => p.Id == productId)).Slug}", row.Path);
            Assert.NotNull(row.Score);
            Assert.True((await bulk.CountsAsync(SeoEntityKind.Product))["all"] >= 1);
        }

        await using (var scope = db.Scope())
        {
            var bulk = scope.ServiceProvider.GetRequiredService<ISeoBulkEditService>();
            var preview = await bulk.PreviewReplaceAsync(SeoEntityKind.Product, [productId], SeoBulkField.Title, "تازه", "جدید | {name}");
            var item = Assert.Single(preview);
            Assert.Equal("عنوان تازه", item.Before);
            Assert.Equal("عنوان جدید | " + name, item.After);
            Assert.False(item.TooLong);

            var applied = await bulk.ApplyReplaceAsync(SeoEntityKind.Product, [productId], SeoBulkField.Title, "تازه", "جدید | {name}", userId);
            Assert.Equal(EnOperationResultStatus.Success, applied.Status);
            Assert.Equal(1, applied.Data);

            var missing = await bulk.ApplyReplaceAsync(SeoEntityKind.Product, [productId], SeoBulkField.MetaDescription, "یافت‌نشدنی", "x", userId);
            Assert.Equal(EnOperationResultStatus.Error, missing.Status);
        }

        await using (var scope = db.Scope())
            Assert.Equal("عنوان جدید | " + name, (await Ctx(scope).Products.AsNoTracking().SingleAsync(p => p.Id == productId)).Seo?.Title);
    }

    // ═══ Search Console ═════════════════════════════════════════════════

    private const string ServiceEmail = "seo-bot@fratak-tests.iam.gserviceaccount.com";

    private static string ServiceAccountJson()
    {
        using var rsa = RSA.Create(2048);
        return JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["type"] = "service_account",
            ["project_id"] = "fratak-tests",
            ["private_key_id"] = "k1",
            ["private_key"] = rsa.ExportPkcs8PrivateKeyPem(),
            ["client_email"] = ServiceEmail,
            ["token_uri"] = "https://oauth2.googleapis.com/token",
        });
    }

    /// <summary>پاسخ‌های ساختگی گوگل با همان ساختار API واقعی. failAnalytics = خطای ۴۰۳ برای آمار.</summary>
    private sealed class FakeGoogle(bool failAnalytics = false) : ISearchConsoleHttp
    {
        public List<GscHttpRequest> Requests { get; } = [];

        public Task<GscHttpResponse> SendAsync(GscHttpRequest request, CancellationToken cancellationToken)
        {
            lock (Requests)
                Requests.Add(request);
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            string Day(int ago) => today.AddDays(-ago).ToString("yyyy-MM-dd");

            if (request.Url.StartsWith("https://oauth2.googleapis.com/token", StringComparison.Ordinal))
                return Respond(200, """{"access_token":"ya29.test-token","expires_in":3599,"token_type":"Bearer"}""");
            if (request.Method == HttpMethod.Get)
                return Respond(200, """{"siteEntry":[{"siteUrl":"sc-domain:fratak-tests.ir","permissionLevel":"siteOwner"}]}""");
            if (request.Url.EndsWith("/searchAnalytics/query", StringComparison.Ordinal))
            {
                if (failAnalytics)
                    return Respond(403, """{"error":{"code":403,"message":"User does not have sufficient permission for site 'sc-domain:fratak-tests.ir'.","status":"PERMISSION_DENIED"}}""");
                if (request.Json!.Contains("\"date\"", StringComparison.Ordinal))
                    return Respond(200, $$"""{"rows":[{"keys":["{{Day(5)}}"],"clicks":4,"impressions":200,"ctr":0.02,"position":9.5},{"keys":["{{Day(2)}}"],"clicks":6,"impressions":300,"ctr":0.02,"position":8.5}]}""");
                return Respond(200, """{"rows":[{"keys":["https://fratak-tests.ir/product/gsc-a","قیمت محصول الف"],"clicks":3,"impressions":400,"ctr":0.0075,"position":7.2},{"keys":["https://fratak-tests.ir/product/gsc-a?utm_source=x","خرید محصول الف"],"clicks":1,"impressions":60,"ctr":0.016,"position":12}]}""");
            }
            return Respond(404, "{}");
        }

        private static Task<GscHttpResponse> Respond(int status, string body) => Task.FromResult(new GscHttpResponse(status, body));
    }

    private static SearchConsoleService Gsc(AsyncServiceScope scope, ISearchConsoleHttp http) =>
        new(Ctx(scope), http, scope.ServiceProvider.GetRequiredService<ISecretProtector>(),
            scope.ServiceProvider.GetRequiredService<IStaffNotificationService>(), scope.ServiceProvider.GetRequiredService<IMemoryCache>(),
            NullLogger<SearchConsoleService>.Instance);

    [Fact]
    public async Task Search_console_connects_syncs_and_reports()
    {
        var google = new FakeGoogle();
        await using (var scope = db.Scope())
        {
            var gsc = Gsc(scope, google);

            var invalid = await gsc.SaveAsync(new SearchConsoleSettingsInput(true, "fratak-tests.ir", "{\"type\":\"authorized_user\"}", false, null));
            Assert.Equal(EnOperationResultStatus.Error, invalid.Status);
            var badProxy = await gsc.SaveAsync(new SearchConsoleSettingsInput(false, "fratak-tests.ir", null, false, "not a proxy"));
            Assert.Equal(EnOperationResultStatus.Error, badProxy.Status);

            var saved = await gsc.SaveAsync(new SearchConsoleSettingsInput(true, "fratak-tests.ir", ServiceAccountJson(), false, null));
            Assert.Equal(EnOperationResultStatus.Success, saved.Status);
        }

        await using (var scope = db.Scope())
        {
            var config = await Ctx(scope).SeoToolsConfigs.AsNoTracking().SingleAsync();
            Assert.Equal("sc-domain:fratak-tests.ir", config.SearchConsoleSite);
            Assert.Equal(ServiceEmail, config.ServiceAccountEmail);
            Assert.StartsWith("test-protected:", config.SearchConsoleKeyProtected); // کلید هرگز خام ذخیره نمی‌شود

            var gsc = Gsc(scope, google);
            var test = await gsc.TestAsync();
            Assert.Equal(EnOperationResultStatus.Success, test.Status);
            Assert.Contains("مالک", test.Message);

            var sync = await gsc.SyncAsync(manual: true);
            Assert.Equal(EnOperationResultStatus.Success, sync.Status);
        }

        // درخواست توکن با JWT امضاشده و درخواست‌های API با همان توکن.
        var token = Assert.Single(google.Requests, r => r.Form is not null);
        Assert.Equal("urn:ietf:params:oauth:grant-type:jwt-bearer", token.Form!["grant_type"]);
        Assert.Equal(3, token.Form["assertion"].Split('.').Length);
        var analytics = google.Requests.Where(r => r.Url.EndsWith("/searchAnalytics/query", StringComparison.Ordinal)).ToList();
        Assert.Equal(2, analytics.Count);
        Assert.All(analytics, r => Assert.Equal("ya29.test-token", r.Bearer));
        Assert.All(analytics, r => Assert.Contains("sc-domain%3Afratak-tests.ir", r.Url));

        await using (var scope = db.Scope())
        {
            var gsc = Gsc(scope, google);
            var status = await gsc.GetStatusAsync();
            Assert.True(status.HasKey);
            Assert.True(status.Ok);
            Assert.NotNull(status.SyncedAt);
            Assert.Equal(2, status.QueryRows); // یک صفحه (آدرس با utm یکی شد) × دو عبارت
            Assert.Equal(2, status.Days);

            var report = await gsc.GetReportAsync();
            Assert.True(report.HasData);
            Assert.Equal(10, report.Current.Clicks);
            Assert.Equal(500, report.Current.Impressions);
            Assert.Equal(8.9, report.Current.Position, 6); // میانگین وزنی: (9.5×200 + 8.5×300) / 500
            Assert.Equal(2, report.Daily.Count);
            Assert.Equal("قیمت محصول الف", report.TopQueries[0].Query);
            var page = Assert.Single(report.TopPages);
            Assert.Equal("/product/gsc-a", page.Path);
            Assert.Equal(4, page.Clicks);
            Assert.Equal("قیمت محصول الف", page.TopQuery);
            // جایگاه ۷٫۲ با ۴۰۰ نمایش: «نزدیک صفحهٔ اول».
            Assert.Contains(report.Opportunities, o => o.Kind == GscOpportunityKind.StrikingDistance && o.Query == "قیمت محصول الف");

            var pageQueries = await gsc.PageQueriesAsync("/product/gsc-a/", 10);
            Assert.Equal(2, pageQueries.Count);
            Assert.Equal("قیمت محصول الف", pageQueries[0].Query);
        }

        // خطای دسترسی: راهنمای فارسی با ایمیل حساب سرویس، وضعیت «ناموفق» و داده‌های قبلی دست‌نخورده.
        await using (var scope = db.Scope())
        {
            var gsc = Gsc(scope, new FakeGoogle(failAnalytics: true));
            var failed = await gsc.SyncAsync(manual: true);
            Assert.Equal(EnOperationResultStatus.Error, failed.Status);
            Assert.Contains(ServiceEmail, failed.Message);

            var status = await gsc.GetStatusAsync();
            Assert.False(status.Ok);
            Assert.Equal(2, status.QueryRows);
        }

        // حذف کلید، همگام‌سازی خودکار را هم غیرممکن می‌کند.
        await using (var scope = db.Scope())
        {
            var gsc = Gsc(scope, google);
            var cleared = await gsc.SaveAsync(new SearchConsoleSettingsInput(false, "fratak-tests.ir", null, true, null));
            Assert.Equal(EnOperationResultStatus.Success, cleared.Status);
            Assert.False((await gsc.GetStatusAsync()).HasKey);
            Assert.Equal(EnOperationResultStatus.Error, (await gsc.SyncAsync(manual: true)).Status);
            var enable = await gsc.SaveAsync(new SearchConsoleSettingsInput(true, "fratak-tests.ir", null, false, null));
            Assert.Equal(EnOperationResultStatus.Error, enable.Status); // بدون کلید روشن نمی‌شود
        }
    }
}
