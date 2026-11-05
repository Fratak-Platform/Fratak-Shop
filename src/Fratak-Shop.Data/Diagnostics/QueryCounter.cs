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

    internal static void Hit(long milliseconds)
    {
        Interlocked.Increment(ref _total);
        CurrentScope.Value?.Add(milliseconds);
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
        QueryCounter.Hit((long)eventData.Duration.TotalMilliseconds);
        return result;
    }

    public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
    {
        QueryCounter.Hit((long)eventData.Duration.TotalMilliseconds);
        return ValueTask.FromResult(result);
    }

    public override object? ScalarExecuted(DbCommand command, CommandExecutedEventData eventData, object? result)
    {
        QueryCounter.Hit((long)eventData.Duration.TotalMilliseconds);
        return result;
    }

    public override ValueTask<object?> ScalarExecutedAsync(DbCommand command, CommandExecutedEventData eventData, object? result, CancellationToken cancellationToken = default)
    {
        QueryCounter.Hit((long)eventData.Duration.TotalMilliseconds);
        return ValueTask.FromResult(result);
    }

    public override int NonQueryExecuted(DbCommand command, CommandExecutedEventData eventData, int result)
    {
        QueryCounter.Hit((long)eventData.Duration.TotalMilliseconds);
        return result;
    }

    public override ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        QueryCounter.Hit((long)eventData.Duration.TotalMilliseconds);
        return ValueTask.FromResult(result);
    }
}
