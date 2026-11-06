using System.Collections.Concurrent;
using Fratak_Shop.Data.Diagnostics;

namespace Fratak_Shop.Web.Infrastructure.Ops;

/// <summary>
/// آمار پرس‌وجوی هر «نوع صفحه» (A14): میانگین و بیشینهٔ تعداد پرس‌وجو و زمان پایگاه داده. کلید = دو بخش اول مسیر با * برای
/// بخش‌های متغیر. مثال: «/product/iphone-15» و «/product/galaxy» هر دو «/product/*».
/// <para>نمایش: صفحهٔ «فنی ← منابع سرور» (سنگین‌ترین صفحه‌ها برای بهینه‌سازی).</para>
/// </summary>
public static class PageQueryStats
{
    /// <summary>سقف تعداد کلیدها (جلوگیری از رشد بی‌پایان با آدرس‌های ساختگی).</summary>
    public const int MaxKeys = 200;

    public sealed class Entry
    {
        public long Requests;
        public long Queries;
        public int MaxQueries;
        public long DbMilliseconds;
    }

    /// <summary>یک ردیف گزارش.</summary>
    public sealed record Row(string Path, long Requests, double AverageQueries, int MaxQueries, double AverageDbMs);

    private static readonly ConcurrentDictionary<string, Entry> Entries = new(StringComparer.Ordinal);

    /// <summary>
    /// کلید گروه‌بندی مسیر. مثال: «/product/abc» → «/product/*»؛ «/» → «/»؛ «/blog» → «/blog»؛ «/admin/orders/5» → «/admin/orders/*».
    /// </summary>
    public static string KeyOf(string? path)
    {
        if (string.IsNullOrEmpty(path) || path == "/")
            return "/";
        var parts = path.Trim('/').Split('/', 4, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return "/";
        var first = parts[0].ToLowerInvariant();
        if (first == "admin" && parts.Length >= 2)
            return "/admin/" + parts[1].ToLowerInvariant() + (parts.Length > 2 ? "/*" : string.Empty);
        return "/" + first + (parts.Length > 1 ? "/*" : string.Empty);
    }

    /// <summary>ثبت یک درخواست. مثال: <c>PageQueryStats.Record("/product/x", 6, 14)</c>.</summary>
    public static void Record(string? path, int queries, long dbMilliseconds)
    {
        var key = KeyOf(path);
        if (key.Length > 80)
            return;
        if (!Entries.TryGetValue(key, out var entry))
        {
            if (Entries.Count >= MaxKeys)
                return;
            entry = Entries.GetOrAdd(key, _ => new Entry());
        }
        Interlocked.Increment(ref entry.Requests);
        Interlocked.Add(ref entry.Queries, queries);
        Interlocked.Add(ref entry.DbMilliseconds, dbMilliseconds);
        int current;
        while (queries > (current = Volatile.Read(ref entry.MaxQueries)))
        {
            if (Interlocked.CompareExchange(ref entry.MaxQueries, queries, current) == current)
                break;
        }
    }

    /// <summary>سنگین‌ترین مسیرها بر اساس میانگین پرس‌وجو.</summary>
    public static IReadOnlyList<Row> Top(int take = 20) => Entries
        .Select(pair => new Row(pair.Key, pair.Value.Requests,
            pair.Value.Requests == 0 ? 0 : (double)pair.Value.Queries / pair.Value.Requests,
            pair.Value.MaxQueries,
            pair.Value.Requests == 0 ? 0 : (double)pair.Value.DbMilliseconds / pair.Value.Requests))
        .OrderByDescending(r => r.AverageQueries)
        .Take(take)
        .ToList();

    /// <summary>فقط آزمون‌ها.</summary>
    public static void ResetForTests() => Entries.Clear();
}

/// <summary>
/// میان‌افزار شمارش پرس‌وجو (A14/A15): برای هر درخواست غیرایستا یک <see cref="QueryCounter"/> باز می‌کند، در پایان آمار را در
/// <see cref="PageQueryStats"/> ثبت می‌کند و اگر <c>Shop:Diagnostics:QueryHeader</c> روشن باشد (توسعه و آزمون‌ها) سرآیند
/// <c>X-Db-Queries</c> را به پاسخ اضافه می‌کند تا آزمون‌ها «بودجهٔ پرس‌وجو»ی هر صفحه را بسنجند.
/// <para>روی سایت واقعی سرآیند خاموش است (اطلاعات داخلی فاش نشود).</para>
/// </summary>
public sealed class QueryMetricsMiddleware(RequestDelegate next, IConfiguration configuration, IWebHostEnvironment environment)
{
    public const string Header = "X-Db-Queries";

    private readonly bool _header = configuration.GetValue("Shop:Diagnostics:QueryHeader", environment.IsDevelopment());

    public async Task InvokeAsync(HttpContext http)
    {
        using var scope = QueryCounter.Begin();
        if (_header)
        {
            http.Response.OnStarting(() =>
            {
                http.Response.Headers[Header] = scope.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
                return Task.CompletedTask;
            });
        }

        await next(http);

        if (scope.Count > 0)
            PageQueryStats.Record(http.Request.Path.Value, scope.Count, scope.Milliseconds);
    }
}
