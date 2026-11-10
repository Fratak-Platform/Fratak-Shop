using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Fratak_Shop.Data.Diagnostics;

/// <summary>
/// دور نوزدهم (A14/A15): شمارندهٔ پرس‌وجوهای پایگاه داده در هر درخواست.
/// <para>چرا: کندی صفحه روی هاست ضعیف بیشتر از «تعداد زیاد پرس‌وجو» (مشکل N+1) است تا از کد. با این شمارنده هر صفحه می‌گوید
/// چند پرس‌وجو زده و آزمون‌ها بودجه می‌گذارند (مثلاً صفحهٔ محصول حداکثر N پرس‌وجو) تا کسی ناخواسته N+1 اضافه نکند.</para>
/// <para>استفاده:
/// <code>using var scope = QueryCounter.Begin();   // شروع شمارش (میان‌افزار هر درخواست)
/// … کار با DbContext …
/// var count = scope.Count;                       // تعداد پرس‌وجوها</code></para>
/// <para>هزینه: یک افزایش اتمی به ازای هر پرس‌وجو؛ بیرون از Begin هیچ کاری نمی‌کند.</para>
/// </summary>
public static class QueryCounter
{
    private static readonly AsyncLocal<QueryScope?> CurrentScope = new();
    private static long _total;

    /// <summary>دامنهٔ شمارش فعلی (یا null).</summary>
    public static QueryScope? Current => CurrentScope.Value;

    /// <summary>کل پرس‌وجوهای اجراشده از زمان روشن شدن (صفحهٔ منابع).</summary>
    public static long Total => Interlocked.Read(ref _total);

    /// <summary>
    /// شروع شمارش برای جریان فعلی (و کارهای async زیرمجموعه‌اش). با Dispose دامنهٔ قبلی برمی‌گردد.
    /// </summary>
    public static QueryScope Begin()
    {
        var scope = new QueryScope(CurrentScope.Value);
        CurrentScope.Value = scope;
        return scope;
    }

    internal static void Hit(DbCommand command, TimeSpan duration)
    {
        var milliseconds = (long)duration.TotalMilliseconds;
        Interlocked.Increment(ref _total);
        CurrentScope.Value?.Add(milliseconds);
        // دور ۲۳ (مورد ۷۷): پرس‌وجوی کند در دفتر سبک حافظه ثبت می‌شود (هزینه برای پرس‌وجوی سریع: یک مقایسه).
        if (milliseconds >= SlowQueryLog.ThresholdMilliseconds)
            SlowQueryLog.Record(command.CommandText, milliseconds, DateTimeOffset.UtcNow);
    }

    internal static void Restore(QueryScope? previous) => CurrentScope.Value = previous;
}

/// <summary>یک دامنهٔ شمارش؛ Count = تعداد پرس‌وجو، Milliseconds = جمع زمان اجرای آن‌ها.</summary>
public sealed class QueryScope(QueryScope? previous) : IDisposable
{
    private int _count;
    private long _milliseconds;

    public int Count => Volatile.Read(ref _count);

    public long Milliseconds => Interlocked.Read(ref _milliseconds);

    internal void Add(long milliseconds)
    {
        Interlocked.Increment(ref _count);
        Interlocked.Add(ref _milliseconds, milliseconds);
    }

    public void Dispose() => QueryCounter.Restore(previous);
}

/// <summary>
/// رهگیر EF Core که هر پرس‌وجوی اجراشده را به <see cref="QueryCounter"/> گزارش می‌دهد. بی‌حالت؛ یک نمونهٔ سراسری.
/// ثبت: FratakShopBootstrapper.AddFratakShopData.
/// </summary>
public sealed class QueryCountingInterceptor : DbCommandInterceptor
{
    public static readonly QueryCountingInterceptor Instance = new();

    public override DbDataReader ReaderExecuted(DbCommand command, CommandExecutedEventData eventData, DbDataReader result)
    {
        QueryCounter.Hit(command, eventData.Duration);
        return result;
    }

    public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
    {
        QueryCounter.Hit(command, eventData.Duration);
        return ValueTask.FromResult(result);
    }

    public override object? ScalarExecuted(DbCommand command, CommandExecutedEventData eventData, object? result)
    {
        QueryCounter.Hit(command, eventData.Duration);
        return result;
    }

    public override ValueTask<object?> ScalarExecutedAsync(DbCommand command, CommandExecutedEventData eventData, object? result, CancellationToken cancellationToken = default)
    {
        QueryCounter.Hit(command, eventData.Duration);
        return ValueTask.FromResult(result);
    }

    public override int NonQueryExecuted(DbCommand command, CommandExecutedEventData eventData, int result)
    {
        QueryCounter.Hit(command, eventData.Duration);
        return result;
    }

    public override ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        QueryCounter.Hit(command, eventData.Duration);
        return ValueTask.FromResult(result);
    }
}

/// <summary>
/// دور ۲۳ (مورد ۷۷): دفتر «پرس‌وجوهای کند» – ۵۰ پرس‌وجوی آخری که بیش از <see cref="ThresholdMilliseconds"/> طول کشیده‌اند.
/// <para>چرا: روی هاست ضعیف یک پرس‌وجوی کند (مثلاً بدون ایندکس) کل سایت را کند می‌کند؛ این دفتر نشان می‌دهد کدام است.</para>
/// <para>امنیت: فقط متن SQL (با نام پارامترها مثل @p0) ذخیره می‌شود، نه مقدار پارامترها (شماره تلفن، رمز و… هرگز).
/// متن تا ۶۰۰ نویسه بریده می‌شود و هر الگوی تکراری فقط یک ردیف (با شمارنده) دارد تا دفتر با یک پرس‌وجوی پرتکرار پر نشود.</para>
/// <para>نمایش: «فنی ← منابع سرور». حافظه: در بدترین حالت حدود ۵۰ × ۶۰۰ نویسه.</para>
/// </summary>
public static class SlowQueryLog
{
    /// <summary>آستانهٔ «کند» به میلی‌ثانیه.</summary>
    public const long ThresholdMilliseconds = 700;

    /// <summary>بیشترین تعداد ردیف نگه‌داری‌شده.</summary>
    public const int Capacity = 50;

    /// <summary>بیشترین طول متن SQL ذخیره‌شده.</summary>
    public const int MaxSqlLength = 600;

    /// <summary>یک الگوی کند: متن، آخرین زمان، بیشترین و آخرین مدت، تعداد دفعات.</summary>
    public sealed record Entry(string Sql, DateTimeOffset LastAt, long LastMilliseconds, long MaxMilliseconds, int Count);

    private static readonly object Gate = new();
    private static readonly List<Entry> Entries = [];

    /// <summary>ثبت. مثال: <c>SlowQueryLog.Record("SELECT … FROM Products …", 1200, now)</c>.</summary>
    public static void Record(string? sql, long milliseconds, DateTimeOffset at)
    {
        var text = Shorten(sql);
        if (text.Length == 0)
            return;
        lock (Gate)
        {
            var index = Entries.FindIndex(e => e.Sql == text);
            if (index >= 0)
            {
                var old = Entries[index];
                Entries.RemoveAt(index);
                Entries.Insert(0, new Entry(text, at, milliseconds, Math.Max(old.MaxMilliseconds, milliseconds), old.Count == int.MaxValue ? old.Count : old.Count + 1));
                return;
            }
            Entries.Insert(0, new Entry(text, at, milliseconds, milliseconds, 1));
            if (Entries.Count > Capacity)
                Entries.RemoveAt(Entries.Count - 1);
        }
    }

    /// <summary>ردیف‌ها، تازه‌ترین اول.</summary>
    public static IReadOnlyList<Entry> Recent()
    {
        lock (Gate)
            return Entries.ToList();
    }

    /// <summary>فاصله‌های اضافه حذف و متن کوتاه می‌شود. مثال: «SELECT  *\n FROM x» ← «SELECT * FROM x».</summary>
    public static string Shorten(string? sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
            return string.Empty;
        var text = string.Join(' ', sql.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return text.Length > MaxSqlLength ? text[..MaxSqlLength] + "…" : text;
    }

    /// <summary>فقط آزمون‌ها.</summary>
    public static void ResetForTests()
    {
        lock (Gate)
            Entries.Clear();
    }
}
