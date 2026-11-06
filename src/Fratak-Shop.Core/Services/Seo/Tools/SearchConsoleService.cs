using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Core.Services.Security;
using Fratak_Shop.Core.Services.Seo.Engine;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Admin;
using Fratak_Shop.Data.Entities.SeoEngine;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Fratak_Shop.Core.Services.Seo.Tools;

// ═══════════════════════════════════════════════════════════════════════════
//  سرویس Search Console (مرحلهٔ هفتم – فاز ۸ب)
//  • همگام‌سازی روزانه (SeoToolsWorker، هر ~۲۰ ساعت) یا دستی: آمار روزانهٔ ۹۲ روز و «صفحه × عبارت» ۲۸ روز.
//  • گزارش /admin/seo/search-console: مقایسهٔ ۲۸ روز با ۲۸ روز قبل، عبارت‌ها و صفحه‌های برتر و «فرصت‌ها».
//  • موتور سئو عبارت‌های واقعی گوگل هر صفحه را در انتخاب کلمهٔ کلیدی به کار می‌برد (KeywordPlanner.Candidates).
//  • ثبت نقشهٔ سایت در گوگل با یک کلیک.
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>درخواست HTTP به گوگل. Form = بدنهٔ x-www-form-urlencoded؛ Json = بدنهٔ JSON؛ Bearer = توکن؛ Proxy = آدرس پروکسی (اختیاری).</summary>
public sealed record GscHttpRequest(HttpMethod Method, string Url, IReadOnlyDictionary<string, string>? Form = null, string? Json = null,
    string? Bearer = null, string? Proxy = null);

/// <summary>پاسخ: Status = null یعنی اتصال برقرار نشد (Error = علت).</summary>
public sealed record GscHttpResponse(int? Status, string Body, string? Error = null)
{
    public bool IsSuccess => Status is >= 200 and < 300;
}

/// <summary>
/// لایهٔ پایین HTTP برای گوگل (پیاده‌سازی واقعی: Web/Infrastructure/SeoEngine/SearchConsoleHttp با HttpClient و پروکسی).
/// پیش‌فرض Core هیچ درخواستی نمی‌فرستد. در تست‌ها با نسخهٔ جعلی جایگزین می‌شود.
/// </summary>
public interface ISearchConsoleHttp
{
    Task<GscHttpResponse> SendAsync(GscHttpRequest request, CancellationToken cancellationToken);
}

public sealed class OfflineSearchConsoleHttp : ISearchConsoleHttp
{
    public Task<GscHttpResponse> SendAsync(GscHttpRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(new GscHttpResponse(null, "", "ارسال درخواست اینترنتی در این محیط فعال نیست"));
}

/// <summary>وضعیت اتصال برای صفحهٔ تنظیمات.</summary>
public sealed record SearchConsoleStatus(bool Enabled, string? Site, string? ServiceEmail, bool HasKey, string? Proxy,
    DateTimeOffset? SyncedAt, bool Ok, string? Message, int QueryRows, int Days, DateOnly? PeriodStart, DateOnly? PeriodEnd);

/// <summary>فرم تنظیمات. KeyJson خالی = کلید قبلی می‌ماند؛ ClearKey = حذف کلید.</summary>
public sealed record SearchConsoleSettingsInput(bool Enabled, string? Site, string? KeyJson, bool ClearKey, string? Proxy);

/// <summary>جمع آمار یک دوره.</summary>
public sealed record GscTotals(int Clicks, int Impressions, double Ctr, double Position);

/// <summary>یک عبارت جست‌وجو (جمع همهٔ صفحه‌ها).</summary>
public sealed record GscQueryRow(string Query, int Clicks, int Impressions, double Ctr, double Position, int Pages);

/// <summary>یک صفحه (جمع همهٔ عبارت‌ها) با پرنمایش‌ترین عبارتش و صفحهٔ متناظر در سایت.</summary>
public sealed record GscPageRow(string Path, string? Title, SeoEntityKind? Kind, Guid? EntityId, int Clicks, int Impressions, double Ctr, double Position, string? TopQuery);

public enum GscOpportunityKind : byte
{
    /// <summary>جایگاه ۴ تا ۲۰ با نمایش خوب: با کمی بهبود به صفحهٔ اول/سه نتیجهٔ اول می‌رسد.</summary>
    StrikingDistance = 1,

    /// <summary>جایگاه خوب ولی نرخ کلیک کمتر از نصف حد انتظار: عنوان/توضیح متا جذاب نیست.</summary>
    LowCtr = 2,

    /// <summary>کلمهٔ کلیدی کانونی صفحه با عبارتی که گوگل واقعاً برایش نشان می‌دهد فرق دارد.</summary>
    KeywordMismatch = 3,
}

/// <summary>یک فرصت بهبود. Potential = برآورد کلیک ماهانهٔ اضافه اگر اصلاح شود.</summary>
public sealed record GscOpportunity(GscOpportunityKind Kind, string Path, string Query, int Clicks, int Impressions, double Ctr, double Position,
    int Potential, string Advice, SeoEntityKind? EntityKind, Guid? EntityId, string? EntityTitle);

/// <summary>گزارش صفحهٔ Search Console.</summary>
public sealed class SearchConsoleReport
{
    public bool HasData { get; init; }
    public DateTimeOffset? SyncedAt { get; init; }
    public GscTotals Current { get; init; } = new(0, 0, 0, 0);
    public GscTotals Previous { get; init; } = new(0, 0, 0, 0);
    public IReadOnlyList<SearchConsoleDailyStat> Daily { get; init; } = [];
    public IReadOnlyList<GscQueryRow> TopQueries { get; init; } = [];
    public IReadOnlyList<GscPageRow> TopPages { get; init; } = [];
    public IReadOnlyList<GscOpportunity> Opportunities { get; init; } = [];
    public DateOnly? PeriodStart { get; init; }
    public DateOnly? PeriodEnd { get; init; }
}

/// <summary>سرویس Search Console. محل استفاده: SeoToolsWorker و صفحهٔ /admin/seo/search-console.</summary>
public interface ISearchConsoleService
{
    Task<SearchConsoleStatus> GetStatusAsync(CancellationToken cancellationToken = default);
    Task<OperationResult> SaveAsync(SearchConsoleSettingsInput input, CancellationToken cancellationToken = default);

    /// <summary>آزمایش اتصال: گرفتن توکن و فهرست ویژگی‌هایی که حساب سرویس به آن‌ها دسترسی دارد.</summary>
    Task<OperationResult<IReadOnlyList<string>>> TestAsync(CancellationToken cancellationToken = default);

    /// <summary>همگام‌سازی آمار. manual=false فقط وقتی فعال است اجرا می‌شود.</summary>
    Task<OperationResult<string>> SyncAsync(bool manual, CancellationToken cancellationToken = default);

    Task<OperationResult> SubmitSitemapAsync(CancellationToken cancellationToken = default);
    Task<SearchConsoleReport> GetReportAsync(CancellationToken cancellationToken = default);

    /// <summary>عبارت‌های گوگل یک صفحه (برای نمایش در تحلیل صفحه). path = مسیر عمومی، مثلاً «/product/x».</summary>
    Task<IReadOnlyList<SearchConsoleQueryStat>> PageQueriesAsync(string path, int take, CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="ISearchConsoleService"/>
public sealed class SearchConsoleService(
    FratakShopDbContext context,
    ISearchConsoleHttp http,
    ISecretProtector secrets,
    IStaffNotificationService notifications,
    IMemoryCache cache,
    ILogger<SearchConsoleService> logger) : ISearchConsoleService
{
    private const string ApiBase = "https://www.googleapis.com/webmasters/v3/sites";

    /// <summary>یک همگام‌سازی در هر سرور.</summary>
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public static bool IsRunning => Gate.CurrentCount == 0;

    /// <summary>توکن‌های دسترسی (کلید: ایمیل|scope) تا ۵ دقیقه پیش از انقضا دوباره استفاده می‌شوند.</summary>
    private static readonly ConcurrentDictionary<string, (string Token, DateTimeOffset ExpiresAt)> Tokens = new(StringComparer.Ordinal);

    /// <summary>سقف ردیف «صفحه × عبارت» در هر همگام‌سازی (۲ صفحهٔ ۲۵ هزارتایی).</summary>
    public const int MaxQueryRows = 50_000;

    /// <summary>اتصال آماده (حساب، ویژگی، پروکسی).</summary>
    private sealed record Connection(GscServiceAccount Account, string Site, string? Proxy);

    // ── تنظیمات ──────────────────────────────────────────────────────────

    public async Task<SearchConsoleStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var config = await SeoToolsConfigStore.GetAsync(context, tracking: false, cancellationToken);
        var rows = await context.SearchConsoleQueryStats.AsNoTracking().CountAsync(cancellationToken);
        var period = await context.SearchConsoleQueryStats.AsNoTracking()
            .OrderBy(s => s.Id)
            .Select(s => new { s.PeriodStart, s.PeriodEnd })
            .FirstOrDefaultAsync(cancellationToken);
        var days = await context.SearchConsoleDailyStats.AsNoTracking().CountAsync(cancellationToken);
        return new SearchConsoleStatus(config.IsSearchConsoleEnabled, config.SearchConsoleSite, config.ServiceAccountEmail,
            !string.IsNullOrEmpty(config.SearchConsoleKeyProtected), config.SearchConsoleProxy, config.SearchConsoleSyncedAt,
            config.SearchConsoleOk, config.SearchConsoleStatus, rows, days, period?.PeriodStart, period?.PeriodEnd);
    }

    public async Task<OperationResult> SaveAsync(SearchConsoleSettingsInput input, CancellationToken cancellationToken = default)
    {
        var config = await SeoToolsConfigStore.GetAsync(context, tracking: true, cancellationToken);

        var site = SearchConsoleSites.Normalize(input.Site);
        if (!string.IsNullOrWhiteSpace(input.Site) && site is null)
            return OperationResult.Error("آدرس ویژگی معتبر نیست. نمونه: sc-domain:fratak.ir یا https://fratak.ir/");
        var proxy = string.IsNullOrWhiteSpace(input.Proxy) ? null : input.Proxy.Trim();
        if (proxy is not null && !SearchConsoleSites.IsValidProxy(proxy))
            return OperationResult.Error("آدرس پروکسی معتبر نیست. نمونه: http://127.0.0.1:10809 یا socks5://127.0.0.1:1080");

        if (input.ClearKey)
        {
            config.SearchConsoleKeyProtected = null;
            config.ServiceAccountEmail = null;
        }
        else if (!string.IsNullOrWhiteSpace(input.KeyJson))
        {
            if (!GscServiceAccount.TryParse(input.KeyJson, out var account, out var error))
                return OperationResult.Error(error);
            config.SearchConsoleKeyProtected = secrets.Protect(input.KeyJson.Trim());
            config.ServiceAccountEmail = Cut(account!.ClientEmail, 300);
        }

        if (input.Enabled && (string.IsNullOrEmpty(config.SearchConsoleKeyProtected) || site is null))
            return OperationResult.Error("برای روشن کردن همگام‌سازی، فایل کلید حساب سرویس و آدرس ویژگی هر دو لازم است.");

        config.IsSearchConsoleEnabled = input.Enabled;
        config.SearchConsoleSite = Cut(site, 300);
        config.SearchConsoleProxy = Cut(proxy, 300);
        await context.SaveChangesAsync(cancellationToken);
        Tokens.Clear();
        return OperationResult.Success("تنظیمات Search Console ذخیره شد. «آزمایش اتصال» را بزنید تا مطمئن شوید همه‌چیز درست است.");
    }

    // ── اتصال ────────────────────────────────────────────────────────────

    /// <summary>خواندن تنظیمات و رمزگشایی کلید؛ خطا به فارسی.</summary>
    private async Task<(Connection? Connection, string? Error)> ConnectAsync(CancellationToken cancellationToken)
    {
        var config = await SeoToolsConfigStore.GetAsync(context, tracking: false, cancellationToken);
        if (string.IsNullOrEmpty(config.SearchConsoleKeyProtected))
            return (null, "فایل کلید حساب سرویس هنوز بارگذاری نشده است.");
        if (string.IsNullOrEmpty(config.SearchConsoleSite))
            return (null, "آدرس ویژگی Search Console وارد نشده است.");
        string? json;
        try
        {
            json = secrets.Unprotect(config.SearchConsoleKeyProtected);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not unprotect the Search Console key");
            json = null;
        }
        if (json is null)
            return (null, "کلید ذخیره‌شده رمزگشایی نشد (کلید رمزنگاری سرور عوض شده). فایل JSON را دوباره بارگذاری کنید.");
        if (!GscServiceAccount.TryParse(json, out var account, out var error))
            return (null, error);
        return (new Connection(account!, config.SearchConsoleSite, config.SearchConsoleProxy), null);
    }

    /// <summary>توکن دسترسی (از کش یا تازه). خطا = راهنمای فارسی.</summary>
    private async Task<(string? Token, string? Error)> TokenAsync(Connection connection, string scope, CancellationToken cancellationToken)
    {
        var key = connection.Account.ClientEmail + "|" + scope;
        if (Tokens.TryGetValue(key, out var cached) && cached.ExpiresAt > DateTimeOffset.UtcNow.AddMinutes(5))
            return (cached.Token, null);

        var jwt = GscJwt.Create(connection.Account, scope, DateTimeOffset.UtcNow);
        var response = await http.SendAsync(new GscHttpRequest(HttpMethod.Post, connection.Account.TokenUri, Form: new Dictionary<string, string>
        {
            ["grant_type"] = "urn:ietf:params:oauth:grant-type:jwt-bearer",
            ["assertion"] = jwt,
        }, Proxy: connection.Proxy), cancellationToken);
        if (!response.IsSuccess)
            return (null, GscErrors.Explain(response.Status, response.Body, response.Error, connection.Account.ClientEmail));
        var (token, expiresIn) = GscParsers.Token(response.Body);
        if (token is null)
            return (null, "پاسخ توکن گوگل قابل خواندن نبود.");
        Tokens[key] = (token, DateTimeOffset.UtcNow.AddSeconds(Math.Clamp(expiresIn, 60, 3600)));
        return (token, null);
    }

    /// <summary>فراخوانی API با توکن. خروجی: پاسخ موفق یا متن خطای فارسی.</summary>
    private async Task<(GscHttpResponse? Response, string? Error)> CallAsync(Connection connection, string scope, HttpMethod method, string url, string? json,
        CancellationToken cancellationToken)
    {
        var (token, error) = await TokenAsync(connection, scope, cancellationToken);
        if (token is null)
            return (null, error);
        var response = await http.SendAsync(new GscHttpRequest(method, url, Json: json, Bearer: token, Proxy: connection.Proxy), cancellationToken);
        if (response.Status == 401)
            Tokens.TryRemove(connection.Account.ClientEmail + "|" + scope, out _);
        return response.IsSuccess
            ? (response, null)
            : (null, GscErrors.Explain(response.Status, response.Body, response.Error, connection.Account.ClientEmail));
    }

    private static string SiteUrl(string site) => ApiBase + "/" + Uri.EscapeDataString(site);

    public async Task<OperationResult<IReadOnlyList<string>>> TestAsync(CancellationToken cancellationToken = default)
    {
        var (connection, error) = await ConnectAsync(cancellationToken);
        if (connection is null)
            return OperationResult<IReadOnlyList<string>>.Error(error!);
        var (response, callError) = await CallAsync(connection, GscJwt.ReadOnlyScope, HttpMethod.Get, ApiBase, null, cancellationToken);
        if (response is null)
        {
            await SaveStatusAsync(false, callError!, null, cancellationToken);
            return OperationResult<IReadOnlyList<string>>.Error(callError!);
        }
        var sites = GscParsers.Sites(response.Body);
        var names = sites.Select(s => s.SiteUrl + " (" + PermissionTitle(s.Permission) + ")").ToList();
        var match = sites.FirstOrDefault(s => string.Equals(s.SiteUrl, connection.Site, StringComparison.OrdinalIgnoreCase));
        if (match.SiteUrl is null)
        {
            var message = sites.Count == 0
                ? $"اتصال برقرار شد ولی حساب سرویس به هیچ ویژگی‌ای دسترسی ندارد. در Search Console ← تنظیمات ← کاربران و مجوزها، ایمیل {connection.Account.ClientEmail} را اضافه کنید."
                : "اتصال برقرار شد ولی ویژگی «" + connection.Site + "» در دسترس حساب سرویس نیست. ویژگی‌های در دسترس: " + string.Join("، ", names);
            await SaveStatusAsync(false, message, null, cancellationToken);
            return OperationResult<IReadOnlyList<string>>.Error(message);
        }
        var ok = "اتصال برقرار است · دسترسی: " + PermissionTitle(match.Permission) +
                 (match.Permission is "siteRestrictedUser" or "siteUnverifiedUser" ? " (برای ثبت نقشهٔ سایت دسترسی کامل/مالک لازم است)" : "");
        await SaveStatusAsync(true, ok, null, cancellationToken);
        return OperationResult<IReadOnlyList<string>>.Success(names, ok);
    }

    private static string PermissionTitle(string permission) => permission switch
    {
        "siteOwner" => "مالک",
        "siteFullUser" => "کامل",
        "siteRestrictedUser" => "محدود",
        "siteUnverifiedUser" => "تأییدنشده",
        _ => permission,
    };

    // ── همگام‌سازی ───────────────────────────────────────────────────────

    public async Task<OperationResult<string>> SyncAsync(bool manual, CancellationToken cancellationToken = default)
    {
        if (!await Gate.WaitAsync(0, cancellationToken))
            return OperationResult<string>.Error("یک همگام‌سازی دیگر در جریان است.");
        try
        {
            var config = await SeoToolsConfigStore.GetAsync(context, tracking: false, cancellationToken);
            if (!manual && !config.IsSearchConsoleEnabled)
                return OperationResult<string>.Error("همگام‌سازی Search Console خاموش است.");
            var wasOk = config.SearchConsoleOk;
            var (connection, error) = await ConnectAsync(cancellationToken);
            if (connection is null)
                return OperationResult<string>.Error(error!);

            var today = DateOnly.FromDateTime(DateTime.UtcNow);

            // ۱) روزانه (۹۲ روز تا دیروز، شامل دادهٔ تازهٔ غیرنهایی).
            var dailyStart = today.AddDays(-92);
            var dailyEnd = today.AddDays(-1);
            var (dailyRows, dailyError) = await QueryAsync(connection, dailyStart, dailyEnd, ["date"], "all", 1000, cancellationToken);
            if (dailyRows is null)
                return await FailAsync(dailyError!, wasOk, manual, cancellationToken);

            // ۲) صفحه × عبارت (۲۸ روز تا ۳ روز پیش؛ داده‌های نهایی).
            var periodEnd = today.AddDays(-3);
            var periodStart = periodEnd.AddDays(-27);
            var (queryRows, queryError) = await QueryAsync(connection, periodStart, periodEnd, ["page", "query"], null, MaxQueryRows, cancellationToken);
            if (queryRows is null)
                return await FailAsync(queryError!, wasOk, manual, cancellationToken);

            var daily = BuildDaily(dailyRows);
            var stats = BuildQueryStats(queryRows, periodStart, periodEnd);
            await ReplaceAsync(daily, dailyStart, stats, cancellationToken);
            cache.Remove("seo-engine:models"); // موتور سئو عبارت‌های تازهٔ گوگل را بخواند

            var message = $"{PersianText.Digits(daily.Count)} روز و {PersianText.Digits(stats.Count)} ردیف «صفحه × عبارت» دریافت شد " +
                          $"({PersianText.Digits(stats.Select(s => s.Path).Distinct().Count())} صفحه، {PersianText.Digits(stats.Select(s => s.Query).Distinct().Count())} عبارت)";
            await SaveStatusAsync(true, message, DateTimeOffset.UtcNow, cancellationToken);
            return OperationResult<string>.Success(message, message);
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>ثبت خطا در وضعیت و (برای اجرای خودکاری که قبلاً سالم بود) اعلان به کارشناس سئو.</summary>
    private async Task<OperationResult<string>> FailAsync(string error, bool wasOk, bool manual, CancellationToken cancellationToken)
    {
        await SaveStatusAsync(false, error, null, cancellationToken);
        if (!manual && wasOk)
            await notifications.NotifyAsync(new StaffAlert(StaffNotificationKind.SeoSuggestions, "همگام‌سازی Search Console ناموفق بود",
                error.Length > 300 ? error[..300] : error, "/admin/seo/search-console", [AdminPermissions.SeoManage]));
        return OperationResult<string>.Error(error);
    }

    /// <summary>searchAnalytics/query با صفحه‌بندی (هر بار ۲۵ هزار ردیف تا سقف max).</summary>
    private async Task<(List<GscRow>? Rows, string? Error)> QueryAsync(Connection connection, DateOnly start, DateOnly end, string[] dimensions,
        string? dataState, int max, CancellationToken cancellationToken)
    {
        var all = new List<GscRow>();
        const int pageSize = 25_000;
        while (all.Count < max)
        {
            var limit = Math.Min(pageSize, max - all.Count);
            var body = new Dictionary<string, object>
            {
                ["startDate"] = start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ["endDate"] = end.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ["dimensions"] = dimensions,
                ["type"] = "web",
                ["rowLimit"] = limit,
                ["startRow"] = all.Count,
            };
            if (dataState is not null)
                body["dataState"] = dataState;
            var (response, error) = await CallAsync(connection, GscJwt.ReadOnlyScope, HttpMethod.Post,
                SiteUrl(connection.Site) + "/searchAnalytics/query", JsonSerializer.Serialize(body), cancellationToken);
            if (response is null)
                return (null, error);
            var rows = GscParsers.Rows(response.Body);
            all.AddRange(rows);
            if (rows.Count < limit)
                break;
        }
        return (all, null);
    }

    /// <summary>ردیف‌های dimension=date → آمار روزانه (تاریخ نامعتبر کنار گذاشته می‌شود).</summary>
    public static List<SearchConsoleDailyStat> BuildDaily(IEnumerable<GscRow> rows)
    {
        var list = new Dictionary<DateOnly, SearchConsoleDailyStat>();
        foreach (var row in rows)
            if (row.Keys.Count > 0 && DateOnly.TryParseExact(row.Keys[0], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                list[date] = new SearchConsoleDailyStat
                {
                    Date = date,
                    Clicks = (int)row.Clicks,
                    Impressions = (int)row.Impressions,
                    Ctr = row.Ctr,
                    Position = Math.Round(row.Position, 2),
                };
        return list.Values.OrderBy(d => d.Date).ToList();
    }

    /// <summary>
    /// ردیف‌های [page, query] → آمار یکسان‌شده. چند آدرس با یک مسیر (مثلاً با ?utm یا / پایانی) جمع می‌شوند:
    /// کلیک و نمایش جمع، جایگاه میانگین وزنی بر اساس نمایش، CTR = کلیک/نمایش.
    /// <example>(/a?x=1، «q»، ۱ کلیک، ۱۰ نمایش، جایگاه ۴) + (/a، «q»، ۰، ۳۰، ۶) → (/a، «q»، ۱، ۴۰، ۵٫۵).</example>
    /// </summary>
    public static List<SearchConsoleQueryStat> BuildQueryStats(IEnumerable<GscRow> rows, DateOnly periodStart, DateOnly periodEnd)
    {
        var map = new Dictionary<(string Path, string Query), (double Clicks, double Impressions, double PositionSum)>();
        foreach (var row in rows)
        {
            if (row.Keys.Count < 2 || SeoPaths.FromUrl(row.Keys[0]) is not { } path)
                continue;
            var query = PersianText.Clean(row.Keys[1]);
            if (query.Length == 0 || path.Length > 400)
                continue;
            if (query.Length > 200)
                query = query[..200];
            var key = (path, query);
            var current = map.GetValueOrDefault(key);
            map[key] = (current.Clicks + row.Clicks, current.Impressions + row.Impressions, current.PositionSum + row.Position * Math.Max(1, row.Impressions));
        }
        return map.Select(e => new SearchConsoleQueryStat
            {
                Path = e.Key.Path,
                Query = e.Key.Query,
                Clicks = (int)e.Value.Clicks,
                Impressions = (int)e.Value.Impressions,
                Ctr = e.Value.Impressions > 0 ? Math.Round(e.Value.Clicks / e.Value.Impressions, 4) : 0,
                Position = Math.Round(e.Value.PositionSum / Math.Max(1, e.Value.Impressions), 2),
                PeriodStart = periodStart,
                PeriodEnd = periodEnd,
            })
            .ToList();
    }

    /// <summary>جایگزینی داده‌ها در یک تراکنش (حذف واقعی + درج دسته‌ای ۲۰۰۰تایی).</summary>
    private async Task ReplaceAsync(List<SearchConsoleDailyStat> daily, DateOnly dailyStart, List<SearchConsoleQueryStat> stats, CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await context.SearchConsoleDailyStats.IgnoreQueryFilters().Where(d => d.Date >= dailyStart).ExecuteDeleteAsync(cancellationToken);
        await context.SearchConsoleQueryStats.IgnoreQueryFilters().ExecuteDeleteAsync(cancellationToken);
        context.SearchConsoleDailyStats.AddRange(daily);
        await context.SaveChangesAsync(cancellationToken);
        context.ChangeTracker.Clear();
        foreach (var chunk in stats.Chunk(2000))
        {
            context.SearchConsoleQueryStats.AddRange(chunk);
            await context.SaveChangesAsync(cancellationToken);
            context.ChangeTracker.Clear();
        }
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task SaveStatusAsync(bool ok, string message, DateTimeOffset? syncedAt, CancellationToken cancellationToken)
    {
        var config = await SeoToolsConfigStore.GetAsync(context, tracking: true, cancellationToken);
        config.SearchConsoleOk = ok;
        config.SearchConsoleStatus = Cut(message, 500);
        if (syncedAt is not null)
            config.SearchConsoleSyncedAt = syncedAt;
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<OperationResult> SubmitSitemapAsync(CancellationToken cancellationToken = default)
    {
        var (connection, error) = await ConnectAsync(cancellationToken);
        if (connection is null)
            return OperationResult.Error(error!);
        var sitemap = SearchConsoleSites.SitemapUrl(connection.Site);
        if (sitemap is null)
            return OperationResult.Error("آدرس نقشهٔ سایت از روی ویژگی ساخته نشد.");
        var (response, callError) = await CallAsync(connection, GscJwt.FullScope, HttpMethod.Put,
            SiteUrl(connection.Site) + "/sitemaps/" + Uri.EscapeDataString(sitemap), null, cancellationToken);
        return response is null
            ? OperationResult.Error(callError!)
            : OperationResult.Success("نقشهٔ سایت (" + sitemap + ") در گوگل ثبت شد؛ گوگل معمولاً ظرف چند روز آن را می‌خواند.");
    }

    // ── گزارش ────────────────────────────────────────────────────────────

    public async Task<SearchConsoleReport> GetReportAsync(CancellationToken cancellationToken = default)
    {
        var config = await SeoToolsConfigStore.GetAsync(context, tracking: false, cancellationToken);
        var daily = await context.SearchConsoleDailyStats.AsNoTracking().OrderByDescending(d => d.Date).Take(92).ToListAsync(cancellationToken);
        daily.Reverse();
        var hasQueries = await context.SearchConsoleQueryStats.AsNoTracking().AnyAsync(cancellationToken);
        if (daily.Count == 0 && !hasQueries)
            return new SearchConsoleReport { SyncedAt = config.SearchConsoleSyncedAt };

        var last = daily.Count > 0 ? daily[^1].Date : DateOnly.FromDateTime(DateTime.UtcNow);
        var current = Totals(daily.Where(d => d.Date > last.AddDays(-28)));
        var previous = Totals(daily.Where(d => d.Date <= last.AddDays(-28) && d.Date > last.AddDays(-56)));

        var queries = await context.SearchConsoleQueryStats.AsNoTracking()
            .GroupBy(s => s.Query)
            .Select(g => new
            {
                Query = g.Key,
                Clicks = g.Sum(s => s.Clicks),
                Impressions = g.Sum(s => s.Impressions),
                PositionSum = g.Sum(s => s.Position * s.Impressions),
                Pages = g.Count(),
            })
            .OrderByDescending(g => g.Clicks).ThenByDescending(g => g.Impressions)
            .Take(50)
            .ToListAsync(cancellationToken);

        var pages = await context.SearchConsoleQueryStats.AsNoTracking()
            .GroupBy(s => s.Path)
            .Select(g => new
            {
                Path = g.Key,
                Clicks = g.Sum(s => s.Clicks),
                Impressions = g.Sum(s => s.Impressions),
                PositionSum = g.Sum(s => s.Position * s.Impressions),
            })
            .OrderByDescending(g => g.Clicks).ThenByDescending(g => g.Impressions)
            .Take(50)
            .ToListAsync(cancellationToken);

        // ردیف‌های پرنمایش برای «فرصت‌ها» و پرنمایش‌ترین عبارت هر صفحه.
        var rows = await context.SearchConsoleQueryStats.AsNoTracking()
            .Where(s => s.Impressions >= 10)
            .OrderByDescending(s => s.Impressions)
            .Take(20_000)
            .ToListAsync(cancellationToken);
        var entities = await EntityMapAsync(cancellationToken);
        var topQuery = rows.GroupBy(r => r.Path, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var period = rows.FirstOrDefault() ?? await context.SearchConsoleQueryStats.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        return new SearchConsoleReport
        {
            HasData = true,
            SyncedAt = config.SearchConsoleSyncedAt,
            Current = current,
            Previous = previous,
            Daily = daily.Where(d => d.Date > last.AddDays(-28)).ToList(),
            TopQueries = queries.Select(q => new GscQueryRow(q.Query, q.Clicks, q.Impressions,
                q.Impressions > 0 ? (double)q.Clicks / q.Impressions : 0, q.Impressions > 0 ? q.PositionSum / q.Impressions : 0, q.Pages)).ToList(),
            TopPages = pages.Select(p =>
            {
                var entity = entities.GetValueOrDefault(p.Path);
                return new GscPageRow(p.Path, entity?.Title, entity?.Kind, entity?.Id, p.Clicks, p.Impressions,
                    p.Impressions > 0 ? (double)p.Clicks / p.Impressions : 0, p.Impressions > 0 ? p.PositionSum / p.Impressions : 0,
                    topQuery.GetValueOrDefault(p.Path)?.Query);
            }).ToList(),
            Opportunities = FindOpportunities(rows, entities),
            PeriodStart = period?.PeriodStart,
            PeriodEnd = period?.PeriodEnd,
        };
    }

    public async Task<IReadOnlyList<SearchConsoleQueryStat>> PageQueriesAsync(string path, int take, CancellationToken cancellationToken = default)
    {
        var normalized = SeoPaths.Normalize(path);
        return await context.SearchConsoleQueryStats.AsNoTracking()
            .Where(s => s.Path == normalized)
            .OrderByDescending(s => s.Impressions)
            .Take(Math.Clamp(take, 1, 200))
            .ToListAsync(cancellationToken);
    }

    private static GscTotals Totals(IEnumerable<SearchConsoleDailyStat> days)
    {
        var list = days.ToList();
        var clicks = list.Sum(d => d.Clicks);
        var impressions = list.Sum(d => d.Impressions);
        var position = impressions > 0 ? list.Sum(d => d.Position * d.Impressions) / impressions : 0;
        return new GscTotals(clicks, impressions, impressions > 0 ? (double)clicks / impressions : 0, position);
    }

    /// <summary>صفحهٔ سایت برای هر مسیر (از جدول نمره‌های سئو) با کلمهٔ کلیدی کانونی.</summary>
    public sealed record PageEntity(SeoEntityKind Kind, Guid Id, string Title, string? FocusKeyword);

    private async Task<Dictionary<string, PageEntity>> EntityMapAsync(CancellationToken cancellationToken)
    {
        var scores = await context.SeoScores.AsNoTracking()
            .Select(s => new { s.Kind, s.EntityId, s.Title, s.Path, s.FocusKeyword })
            .ToListAsync(cancellationToken);
        var map = new Dictionary<string, PageEntity>(StringComparer.Ordinal);
        foreach (var score in scores)
            map.TryAdd(SeoPaths.Normalize(score.Path), new PageEntity(score.Kind, score.EntityId, score.Title, score.FocusKeyword));
        return map;
    }

    /// <summary>
    /// فرصت‌ها (مرتب بر اساس کلیک ماهانهٔ قابل‌به‌دست‌آوردن):
    /// <list type="bullet">
    /// <item>«نزدیک صفحهٔ اول»: جایگاه ۴ تا ۲۰ و حداقل ۵۰ نمایش. Potential = نمایش × (CTR جایگاه ۳ − CTR فعلی).</item>
    /// <item>«نرخ کلیک کم»: جایگاه ≤ ۱۰، حداقل ۵۰ نمایش و CTR کمتر از نصف حد انتظار. Potential = نمایش × (CTR مورد انتظار − CTR فعلی).</item>
    /// <item>«کلمهٔ کلیدی ناهمخوان»: پرنمایش‌ترین عبارت صفحه (≥ ۵۰ نمایش) با کلمهٔ کانونی هم‌پوشانی ندارد.</item>
    /// </list>
    /// محل استفاده: گزارش Search Console. عمومی و ایستا برای تست.
    /// </summary>
    public static List<GscOpportunity> FindOpportunities(IReadOnlyList<SearchConsoleQueryStat> rows, IReadOnlyDictionary<string, PageEntity> entities)
    {
        var list = new List<GscOpportunity>();
        foreach (var row in rows)
        {
            if (row.Impressions < 50)
                continue;
            var entity = entities.GetValueOrDefault(row.Path);
            if (row.Position is >= 4 and <= 20)
            {
                var potential = (int)Math.Round(row.Impressions * Math.Max(0, GscCtr.Expected(3) - row.Ctr));
                if (potential >= 1)
                    list.Add(Opportunity(GscOpportunityKind.StrikingDistance, row, potential, entity,
                        $"در جایگاه {PersianText.Decimal1(row.Position)} است؛ «{row.Query}» را در عنوان، اولین پاراگراف و یک زیرعنوان بیاورید " +
                        "و از صفحه‌های مرتبط سایت به این صفحه لینک بدهید تا به سه نتیجهٔ اول برسد."));
            }

            if (row.Position <= 10 && row.Ctr < 0.5 * GscCtr.Expected(row.Position))
            {
                var potential = (int)Math.Round(row.Impressions * (GscCtr.Expected(row.Position) - row.Ctr));
                if (potential >= 1)
                    list.Add(Opportunity(GscOpportunityKind.LowCtr, row, potential, entity,
                        $"نرخ کلیک {Percent(row.Ctr)} است، در حالی که برای جایگاه {PersianText.Decimal1(row.Position)} حدود {Percent(GscCtr.Expected(row.Position))} انتظار می‌رود. " +
                        "عنوان و توضیح متا را جذاب‌تر کنید (قیمت، موجودی، ضمانت یا مزیت اصلی را بیاورید)."));
            }
        }

        foreach (var group in rows.GroupBy(r => r.Path, StringComparer.Ordinal))
        {
            var top = group.OrderByDescending(r => r.Impressions).First();
            if (top.Impressions < 50 || !entities.TryGetValue(group.Key, out var entity) || string.IsNullOrWhiteSpace(entity.FocusKeyword))
                continue;
            var focus = PersianText.Normalize(entity.FocusKeyword);
            var query = PersianText.Normalize(top.Query);
            if (focus.Length == 0 || query.Contains(focus, StringComparison.Ordinal) || focus.Contains(query, StringComparison.Ordinal))
                continue;
            var shared = PersianText.ContentStems(top.Query).Intersect(PersianText.ContentStems(entity.FocusKeyword)).Count();
            if (shared > 0 && group.Any(r => KeywordMatcher.Contains(r.Query, entity.FocusKeyword)))
                continue; // کلمهٔ کانونی هم بین عبارت‌های واقعی صفحه هست؛ ناهمخوانی جدی نیست
            list.Add(Opportunity(GscOpportunityKind.KeywordMismatch, top, Math.Max(1, (int)Math.Round(top.Impressions * 0.02)), entity,
                $"گوگل این صفحه را بیشتر برای «{top.Query}» نشان می‌دهد ولی کلمهٔ کانونی «{entity.FocusKeyword}» است. " +
                "اگر «" + top.Query + "» به هدف صفحه نزدیک‌تر است، کلمهٔ کانونی را عوض کنید تا موتور سئو متن را بر اساس آن بهینه کند."));
        }

        return list
            .OrderByDescending(o => o.Potential)
            .ThenByDescending(o => o.Impressions)
            .GroupBy(o => (o.Kind, o.Path, o.Query))
            .Select(g => g.First())
            .Take(60)
            .ToList();
    }

    private static GscOpportunity Opportunity(GscOpportunityKind kind, SearchConsoleQueryStat row, int potential, PageEntity? entity, string advice) =>
        new(kind, row.Path, row.Query, row.Clicks, row.Impressions, row.Ctr, row.Position, potential, advice, entity?.Kind, entity?.Id, entity?.Title);

    /// <summary>درصد با یک رقم اعشار. مثال: 0.034 → «۳٫۴٪».</summary>
    public static string Percent(double ratio) => PersianText.Decimal1(ratio * 100) + "٪";

    private static string? Cut(string? value, int max) => value is null ? null : value.Length <= max ? value : value[..max];
}
