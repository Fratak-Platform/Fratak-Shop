using System.Collections.Concurrent;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Options;

namespace Fratak_Shop.Web.Infrastructure.Http;

/// <summary>
/// دور نوزدهم (A20): «قطع‌کن مدار» برای سرویس‌های بیرونی (سایت تأمین‌کننده، ترب/دیجی‌کالا، سایت پست، موتورهای جست‌وجو،
/// IndexNow، ربات‌های گزارش، سرویس‌های پوش).
/// <para>مشکل: وقتی یک سایت بیرونی کند یا خاموش است، هر درخواست ما تا پایان مهلت (۱۰ تا ۳۰ ثانیه) منتظر می‌ماند و نخ و حافظه
/// نگه می‌دارد. اگر ده‌ها کار پشت‌سرهم همین کار را بکنند، سرور خودمان کند می‌شود.</para>
/// <para>راه‌حل: بعد از ۵ شکست پشت‌سرهم به یک میزبان، تا ۱ دقیقه هیچ درخواستی به آن فرستاده نمی‌شود (خطای فوری
/// HttpRequestException که کد فعلی آن را مثل قطعی شبکه مدیریت می‌کند). بعد یک درخواست آزمایشی؛ اگر باز شکست خورد مدت قطع
/// دو برابر می‌شود (حداکثر ۱۰ دقیقه)؛ اگر موفق شد مدار بسته می‌شود.</para>
/// <para>شکست = خطای شبکه، پایان مهلت، یا پاسخ ۵xx. پاسخ‌های ۴xx (مثل ۴۰۴) شکست حساب نمی‌شوند.</para>
/// <para>کلید مدار = نام کلاینت + میزبان؛ مثلاً خرابی digikala.com درخواست‌های torob.com را قطع نمی‌کند.</para>
/// <para>پیامک، درگاه پرداخت، بررسی لینک شکسته و خواندن صفحه‌های خود سایت عمداً قطع‌کن ندارند (<see cref="AppliesTo"/>).</para>
/// </summary>
public static class CircuitBreakers
{
    public const int FailureThreshold = 5;
    public static readonly TimeSpan FirstBreak = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan MaxBreak = TimeSpan.FromMinutes(10);

    /// <summary>وضعیت یک مدار (برای صفحهٔ «منابع سرور»).</summary>
    public sealed class State
    {
        public required string Key { get; init; }
        public int ConsecutiveFailures { get; set; }
        public DateTimeOffset? OpenUntil { get; set; }
        public TimeSpan LastBreak { get; set; }
        public long Rejected { get; set; }
        public DateTimeOffset? LastFailureAt { get; set; }
        public string? LastError { get; set; }
        public bool TrialInFlight { get; set; }
    }

    private static readonly ConcurrentDictionary<string, State> States = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// نام کلاینت‌هایی که قطع‌کن نمی‌گیرند. مثال: <c>AppliesTo("sms-ir")</c> → false؛ <c>AppliesTo("supplier")</c> → true.
    /// </summary>
    public static bool AppliesTo(string clientName) =>
        !string.IsNullOrEmpty(clientName)
        && !clientName.Contains("sms", StringComparison.OrdinalIgnoreCase)
        && !clientName.Contains("ZarinPal", StringComparison.OrdinalIgnoreCase)
        && !clientName.Contains("Payment", StringComparison.OrdinalIgnoreCase)
        && clientName is not ("seo-links" or "seo-self");

    /// <summary>همهٔ مدارهای دیده‌شده، باز اول.</summary>
    public static IReadOnlyList<State> All() => States.Values
        .OrderByDescending(s => s.OpenUntil ?? DateTimeOffset.MinValue)
        .ThenBy(s => s.Key, StringComparer.Ordinal)
        .ToList();

    /// <summary>
    /// اجازهٔ ارسال؟ مدار بسته → true؛ باز و هنوز در مهلت → false؛ مهلت تمام شده → فقط یک درخواست آزمایشی true.
    /// </summary>
    public static bool TryPass(string key, DateTimeOffset now)
    {
        var state = States.GetOrAdd(key, k => new State { Key = k });
        lock (state)
        {
            if (state.OpenUntil is not { } until)
                return true;
            if (now < until || state.TrialInFlight)
            {
                state.Rejected++;
                return false;
            }
            state.TrialInFlight = true;
            return true;
        }
    }

    /// <summary>ثبت موفقیت: مدار بسته و شمارنده صفر می‌شود.</summary>
    public static void Success(string key)
    {
        if (!States.TryGetValue(key, out var state))
            return;
        lock (state)
        {
            state.ConsecutiveFailures = 0;
            state.OpenUntil = null;
            state.LastBreak = TimeSpan.Zero;
            state.TrialInFlight = false;
        }
    }

    /// <summary>ثبت شکست: بعد از ۵ شکست پشت‌سرهم (یا شکست درخواست آزمایشی) مدار باز می‌شود.</summary>
    public static void Failure(string key, string error, DateTimeOffset now)
    {
        var state = States.GetOrAdd(key, k => new State { Key = k });
        lock (state)
        {
            state.ConsecutiveFailures++;
            state.LastFailureAt = now;
            state.LastError = error.Length <= 200 ? error : error[..200];
            var trialFailed = state.TrialInFlight;
            state.TrialInFlight = false;
            if (trialFailed || state.ConsecutiveFailures >= FailureThreshold)
            {
                var next = state.LastBreak == TimeSpan.Zero ? FirstBreak : TimeSpan.FromTicks(Math.Min(state.LastBreak.Ticks * 2, MaxBreak.Ticks));
                state.LastBreak = next;
                state.OpenUntil = now + next;
            }
        }
    }

    /// <summary>فقط آزمون‌ها.</summary>
    public static void ResetForTests() => States.Clear();
}

/// <summary>
/// هندلر HttpClient که <see cref="CircuitBreakers"/> را اعمال می‌کند. ثبت سراسری برای همهٔ کلاینت‌های نام‌دار در
/// WebServiceCollectionExtensions (<see cref="CircuitBreakerSetup"/>).
/// </summary>
public sealed class CircuitBreakerHandler(string clientName) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var key = clientName + "|" + (request.RequestUri?.IdnHost ?? "-");
        if (!CircuitBreakers.TryPass(key, DateTimeOffset.UtcNow))
            throw new HttpRequestException($"Circuit open for {key}: the remote service failed repeatedly; retry later.");

        try
        {
            var response = await base.SendAsync(request, cancellationToken);
            if ((int)response.StatusCode >= 500)
                CircuitBreakers.Failure(key, "HTTP " + (int)response.StatusCode, DateTimeOffset.UtcNow);
            else
                CircuitBreakers.Success(key);
            return response;
        }
        catch (Exception exception) when (exception is HttpRequestException or TimeoutException
                                              || (exception is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            CircuitBreakers.Failure(key, exception.GetType().Name + ": " + exception.Message, DateTimeOffset.UtcNow);
            throw;
        }
        catch (OperationCanceledException)
        {
            // لغو از طرف خود ما (خاموش شدن/لغو کار) شکست سرویس بیرونی نیست؛ درخواست آزمایشی آزاد می‌شود.
            CircuitBreakers.Success(key);
            throw;
        }
    }
}

/// <summary>
/// نصب سراسری: به همهٔ کلاینت‌های نام‌دار (به جز <see cref="CircuitBreakers.AppliesTo"/> = false) قطع‌کن اضافه می‌کند و مهلت
/// پیش‌فرض ۱۰۰ ثانیه‌ای HttpClient را به ۳۰ ثانیه کم می‌کند (کلاینتی که خودش مهلت داده دست نمی‌خورد).
/// <para>مثال: <c>services.AddShopHttpResilience();</c> پیش از ثبت کلاینت‌ها.</para>
/// </summary>
public static class CircuitBreakerSetup
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    public static IServiceCollection AddShopHttpResilience(this IServiceCollection services)
    {
        services.ConfigureAll<HttpClientFactoryOptions>(options =>
        {
            // پیش از تنظیم‌های خود کلاینت اجرا می‌شود؛ پس هر کلاینتی که مهلت خودش را دارد آن را بازنویسی می‌کند.
            options.HttpClientActions.Insert(0, client => client.Timeout = DefaultTimeout);
        });
        services.AddSingleton<IConfigureOptions<HttpClientFactoryOptions>, BreakerOptions>();
        return services;
    }

    /// <summary>برای هر کلاینت نام‌دار، هندلر قطع‌کن (با نام همان کلاینت) را به انتهای زنجیره اضافه می‌کند.</summary>
    private sealed class BreakerOptions : IConfigureNamedOptions<HttpClientFactoryOptions>
    {
        public void Configure(HttpClientFactoryOptions options) => Configure(Microsoft.Extensions.Options.Options.DefaultName, options);

        public void Configure(string? name, HttpClientFactoryOptions options)
        {
            var clientName = name ?? string.Empty;
            if (!CircuitBreakers.AppliesTo(clientName))
                return;
            options.HttpMessageHandlerBuilderActions.Add(builder => builder.AdditionalHandlers.Add(new CircuitBreakerHandler(clientName)));
        }
    }
}
