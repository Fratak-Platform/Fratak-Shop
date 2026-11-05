using System.IO.Compression;
using System.Net;
using System.Text;
using Fratak_Shop.Data.Diagnostics;
using Fratak_Shop.Data.Extensions;
using Fratak_Shop.Web.Areas.Admin.Pages.SystemPages;
using Fratak_Shop.Web.Infrastructure.Admin;
using Fratak_Shop.Web.Infrastructure.Http;
using Fratak_Shop.Web.Infrastructure.Ops;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace Fratak_Shop.Tests.Unit.Performance;

/// <summary>
/// دور نوزدهم – فاز A (کارایی و پایداری روی هاست ضعیف)، آزمون‌های بدون پایگاه داده:
/// • بودجهٔ پروفایل‌ها و اعمال تنظیمات دستی؛ قانون سطح فشار (از بهترین تا بدترین حالت).
/// • نوبت‌دهی کارها: حیاتی بی‌درنگ، سنگین یکی‌یکی، دورهٔ آرام شروع، ترمز فشار و لغو هنگام خاموشی.
/// • سیاست ری‌استارت کنترل‌شده (ساعت خلوت، فاصلهٔ ۶ ساعته، خط قرمز پایدار).
/// • ربات‌های پرمصرف، قطع‌کن مدار، نسخهٔ ذخیره‌شدهٔ صفحه‌ها، صف ورودی، شمارش پرس‌وجو و فایل‌های پیش‌فشرده.
/// </summary>
[Trait("Category", "Unit")]
[Trait("Area", "Performance")]
public class ResourceGovernorTests
{
    private static ResourceGovernor Governor(ResourceProfile profile = ResourceProfile.Low, int quietMinutes = 0, CancellationToken stopping = default) =>
        new(new ResourceOptions { Profile = profile, StartupQuietMinutes = quietMinutes }, DateTimeOffset.UtcNow, stopping, pollInterval: TimeSpan.FromMilliseconds(20));

    private static ResourceSample Sample(double memoryMb = 100, double limitMb = 1000, double cpu = 0.1, long pending = 0, int active = 0, int queued = 0) =>
        new(DateTimeOffset.UtcNow, memoryMb, limitMb, memoryMb / 2, cpu, pending, active, queued, 8);

    // ═══ بودجه و پروفایل ═══

    [Fact]
    public void Low_profile_is_the_safe_default()
    {
        var options = new ResourceOptions();
        Assert.Equal(ResourceProfile.Low, options.Profile);
        var budget = ResourceBudget.From(options);
        Assert.Equal(48, budget.OutputCacheMb);
        Assert.Equal(1, budget.HeavySlots);
        Assert.Equal(TimeSpan.FromMinutes(10), budget.StartupQuiet);
        Assert.False(budget.CustomerRealtime);
        Assert.True(budget.MaxConcurrentRequests <= 32);
    }

    [Fact]
    public void Profiles_grow_monotonically()
    {
        var low = ResourceBudget.For(ResourceProfile.Low);
        var normal = ResourceBudget.For(ResourceProfile.Normal);
        var high = ResourceBudget.For(ResourceProfile.High);
        Assert.True(low.OutputCacheMb < normal.OutputCacheMb && normal.OutputCacheMb < high.OutputCacheMb);
        Assert.True(low.MaxConcurrentRequests < normal.MaxConcurrentRequests && normal.MaxConcurrentRequests < high.MaxConcurrentRequests);
        Assert.True(low.StartupQuiet > normal.StartupQuiet && normal.StartupQuiet > high.StartupQuiet);
        Assert.True(low.SqlMaxPoolSize < high.SqlMaxPoolSize);
    }

    [Fact]
    public void Manual_settings_override_the_profile_within_safe_bounds()
    {
        var budget = ResourceBudget.From(new ResourceOptions { MemoryLimitMb = 800, MaxConcurrentRequests = 50, StartupQuietMinutes = 0 });
        Assert.Equal(800, budget.MemoryLimitMb);
        Assert.Equal(50, budget.MaxConcurrentRequests);
        Assert.Equal(100, budget.QueueLength);
        Assert.Equal(TimeSpan.Zero, budget.StartupQuiet);

        var absurd = ResourceBudget.From(new ResourceOptions { MemoryLimitMb = 1, MaxConcurrentRequests = 1, StartupQuietMinutes = 99999 });
        Assert.Equal(128, absurd.MemoryLimitMb);
        Assert.Equal(4, absurd.MaxConcurrentRequests);
        Assert.Equal(TimeSpan.FromMinutes(120), absurd.StartupQuiet);
    }

    [Fact]
    public void Effective_memory_limit_never_exceeds_the_profile_cap_on_a_big_shared_host()
    {
        var governor = Governor();
        // هاست ۸ گیگ با پروفایل ضعیف ← ۱۰۲۴ (سهم معقول ما از سرور مشترک).
        Assert.Equal(1024, governor.EffectiveMemoryLimitMb(8L * 1024 * 1024 * 1024));
        // سقف واقعی کمتر (Job Object استخر IIS) ← همان.
        Assert.Equal(700, governor.EffectiveMemoryLimitMb(700L * 1024 * 1024), 3);
        // تنظیم دستی همیشه برنده است.
        var manual = new ResourceGovernor(new ResourceOptions { MemoryLimitMb = 600 }, DateTimeOffset.UtcNow, default);
        Assert.Equal(600, manual.EffectiveMemoryLimitMb(8L * 1024 * 1024 * 1024));
    }

    // ═══ سطح فشار ═══

    [Theory]
    [InlineData(100, 0.1, 0, 0, 0, PressureLevel.Normal)]
    [InlineData(720, 0.1, 0, 0, 0, PressureLevel.Elevated)]
    [InlineData(100, 0.75, 0, 0, 0, PressureLevel.Elevated)]
    [InlineData(100, 0.1, 60, 0, 0, PressureLevel.Elevated)]
    [InlineData(830, 0.1, 0, 0, 0, PressureLevel.High)]
    [InlineData(100, 0.95, 0, 0, 0, PressureLevel.High)]
    [InlineData(100, 0.1, 0, 30, 0, PressureLevel.High)]
    [InlineData(930, 0.1, 0, 0, 0, PressureLevel.Critical)]
    [InlineData(100, 0.1, 1500, 0, 0, PressureLevel.Critical)]
    [InlineData(100, 0.1, 0, 0, 64, PressureLevel.Critical)]
    public void Pressure_levels_follow_the_documented_thresholds(double memoryMb, double cpu, long pending, int active, int queued, PressureLevel expected)
    {
        var budget = ResourceBudget.For(ResourceProfile.Low); // سقف هم‌زمان ۳۲، صف ۶۴
        Assert.Equal(expected, ResourceGovernor.Evaluate(Sample(memoryMb, 1000, cpu, pending, active, queued), budget));
    }

    [Fact]
    public void Unknown_memory_limit_never_counts_as_pressure()
    {
        Assert.Equal(PressureLevel.Normal, ResourceGovernor.Evaluate(Sample(5000, 0), ResourceBudget.For(ResourceProfile.Low)));
    }

    // ═══ نوبت‌دهی کارها ═══

    [Fact]
    public async Task Critical_jobs_never_wait_even_in_critical_pressure_and_startup_quiet()
    {
        var governor = Governor(quietMinutes: 60);
        governor.Update(Sample(990, 1000));
        Assert.Equal(PressureLevel.Critical, governor.Pressure);

        var enter = governor.EnterAsync("send", JobWeight.Critical);
        Assert.True(enter.IsCompleted);
        (await enter).Dispose();
    }

    [Fact]
    public async Task Heavy_jobs_wait_for_the_startup_quiet_period()
    {
        using var stopping = new CancellationTokenSource();
        var governor = Governor(quietMinutes: 60, stopping: stopping.Token);
        Assert.True(governor.InStartupQuiet(DateTimeOffset.UtcNow));
        var reasons = new List<string?>();
        var enter = governor.EnterAsync("heavy", JobWeight.Heavy, reason => { lock (reasons) reasons.Add(reason); });
        await Task.Delay(150);
        Assert.False(enter.IsCompleted);
        lock (reasons)
            Assert.Contains(reasons, r => r is not null && r.Contains("آرام", StringComparison.Ordinal));
        Assert.True(governor.Deferrals > 0);
        await stopping.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => enter.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task Only_one_heavy_job_runs_at_a_time_on_the_low_profile()
    {
        var governor = Governor();
        var first = await governor.EnterAsync("supplier", JobWeight.Heavy);
        Assert.Equal("supplier", governor.HeavyRunning);

        var second = governor.EnterAsync("seo", JobWeight.Heavy);
        await Task.Delay(100);
        Assert.False(second.IsCompleted);

        first.Dispose();
        first.Dispose(); // آزاد کردن دوباره بی‌اثر است (شمارنده خراب نمی‌شود)
        var lease = await second.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("seo", governor.HeavyRunning);
        lease.Dispose();
        Assert.Null(governor.HeavyRunning);

        // بعد از آزاد شدن هر دو، دقیقاً یک جا خالی است (نه دو).
        var a = await governor.EnterAsync("a", JobWeight.Heavy);
        var b = governor.EnterAsync("b", JobWeight.Heavy);
        await Task.Delay(60);
        Assert.False(b.IsCompleted);
        a.Dispose();
        (await b.WaitAsync(TimeSpan.FromSeconds(5))).Dispose();
    }

    [Fact]
    public async Task High_profile_allows_two_heavy_jobs()
    {
        var governor = Governor(ResourceProfile.High);
        using var a = await governor.EnterAsync("a", JobWeight.Heavy);
        using var b = await governor.EnterAsync("b", JobWeight.Heavy).WaitAsync(TimeSpan.FromSeconds(5));
        var c = governor.EnterAsync("c", JobWeight.Heavy);
        await Task.Delay(60);
        Assert.False(c.IsCompleted);
        b.Dispose();
        (await c.WaitAsync(TimeSpan.FromSeconds(5))).Dispose();
    }

    [Fact]
    public async Task Heavy_jobs_brake_while_pressure_is_high_and_continue_when_it_drops()
    {
        var governor = Governor();
        governor.Update(Sample(850, 1000));
        Assert.Equal(PressureLevel.High, governor.Pressure);

        var enter = governor.EnterAsync("backup", JobWeight.Heavy);
        await Task.Delay(120);
        Assert.False(enter.IsCompleted);

        governor.Update(Sample(300, 1000));
        (await enter.WaitAsync(TimeSpan.FromSeconds(5))).Dispose();
    }

    [Fact]
    public async Task Light_jobs_only_wait_in_critical_pressure()
    {
        var governor = Governor();
        governor.Update(Sample(850, 1000)); // High
        var enter = governor.EnterAsync("cleanup", JobWeight.Light);
        Assert.True(enter.IsCompleted);
        (await enter).Dispose();

        governor.Update(Sample(990, 1000)); // Critical
        var waiting = governor.EnterAsync("cleanup", JobWeight.Light);
        await Task.Delay(100);
        Assert.False(waiting.IsCompleted);
        governor.Update(Sample(100, 1000));
        (await waiting.WaitAsync(TimeSpan.FromSeconds(5))).Dispose();
    }

    [Fact]
    public async Task Waiting_is_cancelled_when_the_application_stops()
    {
        using var stopping = new CancellationTokenSource();
        var governor = Governor(quietMinutes: 60, stopping: stopping.Token);
        var enter = governor.EnterAsync("heavy", JobWeight.Heavy);
        await stopping.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => enter.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task Default_global_governor_never_blocks_unit_tests_or_tools()
    {
        // نمونهٔ پیش‌فرض پیش از Configure (یا وقتی Program آن را با تنظیمات آزمون ساخته) نباید کار سنگین را نگه دارد.
        var governor = new ResourceGovernor(new ResourceOptions(), DateTimeOffset.UtcNow, default, unlimited: true);
        governor.Update(Sample(990, 1000));
        Assert.Equal(PressureLevel.Normal, governor.Pressure);
        var enter = governor.EnterAsync("heavy", JobWeight.Heavy);
        Assert.True(enter.IsCompleted);
        (await enter).Dispose();
        Assert.True(governor.IsUnlimited);
    }

    [Fact]
    public void Job_weights_classify_the_real_workers()
    {
        Assert.Equal(JobWeight.Heavy, JobWeights.Of(Fratak_Shop.Web.Infrastructure.Suppliers.SupplierSyncWorker.JobName));
        Assert.Equal(JobWeight.Heavy, JobWeights.Of(Fratak_Shop.Web.Infrastructure.Seo.SeoAutopilotWorker.JobName));
        Assert.Equal(JobWeight.Heavy, JobWeights.Of(BackupWorker.JobName));
        Assert.Equal(JobWeight.Critical, JobWeights.Of(Fratak_Shop.Web.Infrastructure.Email.EmailDispatcher.JobName));
        Assert.Equal(JobWeight.Critical, JobWeights.Of(Fratak_Shop.Web.Infrastructure.Engagement.WebPushDispatcher.JobName));
        Assert.Equal(JobWeight.Critical, JobWeights.Of("لغو سفارش‌های پرداخت‌نشده"));
        Assert.Equal(JobWeight.Light, JobWeights.Of("کار ناشناخته"));
        Assert.Contains(JobWeights.All, pair => pair.Value == JobWeight.Heavy);
    }

    [Fact]
    public async Task Non_urgent_heavy_jobs_wait_for_normal_pressure_during_busy_hours()
    {
        // ۰ تا ۲۴ = همیشه شلوغ (مستقل از ساعت اجرای آزمون).
        var governor = new ResourceGovernor(new ResourceOptions { StartupQuietMinutes = 0, BusyHourStart = 0, BusyHourEnd = 24 },
            DateTimeOffset.UtcNow, default, pollInterval: TimeSpan.FromMilliseconds(20));
        Assert.True(governor.IsBusyHour(DateTimeOffset.UtcNow));
        governor.Update(Sample(750, 1000)); // افزایش‌یافته (زیر «بالا»)

        // کار فوری‌تر (قیمت/موجودی تأمین‌کننده) منتظر نمی‌ماند…
        (await governor.EnterAsync(Fratak_Shop.Web.Infrastructure.Suppliers.SupplierSyncWorker.JobName, JobWeight.Heavy).WaitAsync(TimeSpan.FromSeconds(5))).Dispose();

        // …ولی سئوی شبانه تا عادی شدن فشار صبر می‌کند.
        var seo = governor.EnterAsync(Fratak_Shop.Web.Infrastructure.Seo.SeoAutopilotWorker.JobName, JobWeight.Heavy);
        await Task.Delay(120);
        Assert.False(seo.IsCompleted);
        governor.Update(Sample(200, 1000));
        (await seo.WaitAsync(TimeSpan.FromSeconds(5))).Dispose();
    }

    [Fact]
    public async Task Non_urgent_jobs_run_normally_outside_busy_hours()
    {
        var governor = new ResourceGovernor(new ResourceOptions { StartupQuietMinutes = 0, BusyHourStart = 5, BusyHourEnd = 5 },
            DateTimeOffset.UtcNow, default, pollInterval: TimeSpan.FromMilliseconds(20));
        Assert.False(governor.IsBusyHour(DateTimeOffset.UtcNow));
        governor.Update(Sample(750, 1000));
        (await governor.EnterAsync(Fratak_Shop.Web.Infrastructure.Seo.SeoAutopilotWorker.JobName, JobWeight.Heavy).WaitAsync(TimeSpan.FromSeconds(5))).Dispose();
    }

    [Fact]
    public void Only_deferrable_jobs_prefer_quiet_hours()
    {
        Assert.True(JobWeights.PrefersQuietHours(Fratak_Shop.Web.Infrastructure.Seo.SeoAutopilotWorker.JobName));
        Assert.True(JobWeights.PrefersQuietHours(Fratak_Shop.Web.Infrastructure.SeoEngine.SeoToolsWorker.LinkJobName));
        Assert.False(JobWeights.PrefersQuietHours(Fratak_Shop.Web.Infrastructure.Suppliers.SupplierSyncWorker.JobName));
        Assert.False(JobWeights.PrefersQuietHours(Fratak_Shop.Web.Infrastructure.Shipping.PostTrackingWorker.JobName));
        Assert.False(JobWeights.PrefersQuietHours(BackupWorker.JobName));
        Assert.All(JobWeights.All.Keys.Where(JobWeights.PrefersQuietHours), name => Assert.Equal(JobWeight.Heavy, JobWeights.Of(name)));
    }

    [Fact]
    public void Waiting_jobs_are_shown_as_waiting_for_their_turn_not_as_failed()
    {
        var state = new JobState { Name = "wait-" + Guid.NewGuid().ToString("N"), WaitReason = "دورهٔ آرام بعد از روشن شدن", WaitingSince = DateTimeOffset.UtcNow };
        Assert.Equal(("در انتظار نوبت", "a-badge-blue"), JobsModel.Status(state));
        Assert.Equal(JobWeight.Light, state.Weight);
    }

    // ═══ ری‌استارت کنترل‌شده ═══

    private static readonly DateTimeOffset Now = new(2026, 11, 5, 0, 30, 0, TimeSpan.Zero);

    [Fact]
    public void Self_restart_happens_only_in_quiet_hours_after_a_sustained_red_line()
    {
        var options = new ResourceOptions();
        var started = Now.AddHours(-5);
        // ساعت ۴ بامداد ایران، ۵ دقیقه حافظهٔ ۹۴٪
        Assert.True(SelfRestartPolicy.ShouldRestart(options, Now, started, Now.AddMinutes(-5), null, 0.94, 4));
        // ساعت ۲۱ (پرترافیک) ← نه
        Assert.False(SelfRestartPolicy.ShouldRestart(options, Now, started, Now.AddMinutes(-5), null, 0.94, 21));
        // ولی کشته شدن قریب‌الوقوع (۹۸٪) حتی در ساعت شلوغ ← بله (بهتر از قطع ناگهانی)
        Assert.True(SelfRestartPolicy.ShouldRestart(options, Now, started, Now.AddMinutes(-5), null, 0.98, 21));
    }

    [Fact]
    public void Self_restart_has_guard_rails()
    {
        var options = new ResourceOptions();
        var started = Now.AddHours(-5);
        Assert.False(SelfRestartPolicy.ShouldRestart(options, Now, started, Now.AddMinutes(-1), null, 0.95, 4)); // کوتاه‌تر از ۳ دقیقه
        Assert.False(SelfRestartPolicy.ShouldRestart(options, Now, Now.AddMinutes(-10), Now.AddMinutes(-5), null, 0.95, 4)); // تازه روشن شده
        Assert.False(SelfRestartPolicy.ShouldRestart(options, Now, started, Now.AddMinutes(-5), Now.AddHours(-2), 0.95, 4)); // ۶ ساعت نگذشته
        Assert.False(SelfRestartPolicy.ShouldRestart(options, Now, started, null, null, 0.95, 4)); // خط قرمز ثبت نشده
        Assert.False(SelfRestartPolicy.ShouldRestart(options, Now, started, Now.AddMinutes(-5), null, 0.85, 4)); // زیر ۹۲٪
        Assert.False(SelfRestartPolicy.ShouldRestart(new ResourceOptions { SelfRestart = false }, Now, started, Now.AddMinutes(-5), null, 0.99, 4)); // خاموش
    }

    [Theory]
    [InlineData(3, 6, 3, true)]
    [InlineData(3, 6, 5, true)]
    [InlineData(3, 6, 6, false)]
    [InlineData(3, 6, 2, false)]
    [InlineData(23, 5, 0, true)]
    [InlineData(23, 5, 23, true)]
    [InlineData(23, 5, 12, false)]
    [InlineData(4, 4, 4, false)]
    public void Quiet_hours_support_ranges_across_midnight(int start, int end, int hour, bool expected)
    {
        Assert.Equal(expected, SelfRestartPolicy.IsQuietHour(new ResourceOptions { QuietHourStart = start, QuietHourEnd = end }, hour));
    }

    // ═══ ربات‌های پرمصرف ═══

    [Theory]
    [InlineData("Mozilla/5.0 (compatible; AhrefsBot/7.0; +http://ahrefs.com/robot/)", "ahrefsbot")]
    [InlineData("Mozilla/5.0 (compatible; SemrushBot/7~bl; +http://www.semrush.com/bot.html)", "semrushbot")]
    [InlineData("Mozilla/5.0 (Linux; Android 5.0) AppleWebKit/537.36 (KHTML, like Gecko) Mobile Safari/537.36 (compatible; Bytespider; spider-feedback@bytedance.com)", "bytespider")]
    [InlineData("Mozilla/5.0 (compatible; Googlebot/2.1; +http://www.google.com/bot.html)", null)]
    [InlineData("Mozilla/5.0 (compatible; bingbot/2.0; +http://www.bing.com/bingbot.htm)", null)]
    [InlineData("TelegramBot (like TwitterBot)", null)]
    [InlineData("Mozilla/5.0 (Windows NT 10.0; Win64; x64) Chrome/141.0 Safari/537.36", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Heavy_crawlers_are_recognised_but_search_engines_and_people_are_not(string? userAgent, string? expected)
    {
        Assert.Equal(expected, HeavyCrawlers.Match(userAgent));
    }

    [Fact]
    public void Heavy_crawlers_get_one_request_per_gap_and_nothing_under_pressure()
    {
        var family = "testbot-" + Guid.NewGuid().ToString("N");
        var now = DateTimeOffset.UtcNow;
        Assert.False(HeavyCrawlers.ShouldThrottle(family, PressureLevel.Normal, now));
        Assert.True(HeavyCrawlers.ShouldThrottle(family, PressureLevel.Normal, now.AddSeconds(1)));
        Assert.False(HeavyCrawlers.ShouldThrottle(family, PressureLevel.Normal, now.AddSeconds(4)));
        Assert.True(HeavyCrawlers.ShouldThrottle(family, PressureLevel.Elevated, now.AddMinutes(10)));
    }

    [Fact]
    public void Absurdly_long_user_agents_are_ignored_quickly()
    {
        Assert.Null(HeavyCrawlers.Match(new string('a', 5000) + "AhrefsBot"));
    }

    // ═══ قطع‌کن مدار ═══

    [Fact]
    public void Circuit_opens_after_five_failures_and_recovers_after_a_successful_trial()
    {
        var key = "test|" + Guid.NewGuid().ToString("N") + ".example";
        var now = DateTimeOffset.UtcNow;
        for (var i = 0; i < CircuitBreakers.FailureThreshold - 1; i++)
        {
            Assert.True(CircuitBreakers.TryPass(key, now));
            CircuitBreakers.Failure(key, "HTTP 503", now);
        }
        Assert.True(CircuitBreakers.TryPass(key, now));
        CircuitBreakers.Failure(key, "HTTP 503", now);

        Assert.False(CircuitBreakers.TryPass(key, now.AddSeconds(30)));
        // بعد از مهلت فقط یک درخواست آزمایشی
        Assert.True(CircuitBreakers.TryPass(key, now.AddMinutes(2)));
        Assert.False(CircuitBreakers.TryPass(key, now.AddMinutes(2)));
        CircuitBreakers.Success(key);
        Assert.True(CircuitBreakers.TryPass(key, now.AddMinutes(2)));
        var state = Assert.Single(CircuitBreakers.All(), s => s.Key == key);
        Assert.Equal(0, state.ConsecutiveFailures);
        Assert.True(state.Rejected >= 2);
    }

    [Fact]
    public void A_failed_trial_doubles_the_break_up_to_ten_minutes()
    {
        var key = "test|" + Guid.NewGuid().ToString("N") + ".example";
        var now = DateTimeOffset.UtcNow;
        for (var i = 0; i < CircuitBreakers.FailureThreshold; i++)
            CircuitBreakers.Failure(key, "timeout", now);
        var state = Assert.Single(CircuitBreakers.All(), s => s.Key == key);
        Assert.Equal(CircuitBreakers.FirstBreak, state.LastBreak);

        for (var round = 0; round < 10; round++)
        {
            var at = state.OpenUntil!.Value.AddSeconds(1);
            Assert.True(CircuitBreakers.TryPass(key, at));
            CircuitBreakers.Failure(key, "timeout", at);
        }
        Assert.Equal(CircuitBreakers.MaxBreak, state.LastBreak);
    }

    [Theory]
    [InlineData("supplier", true)]
    [InlineData("seo-engine", true)]
    [InlineData("post-tracking", true)]
    [InlineData("sms-ir", false)]
    [InlineData("IPaymentGateway", false)]
    [InlineData("seo-links", false)]
    [InlineData("seo-self", false)]
    [InlineData("", false)]
    public void Payments_sms_and_link_checks_never_get_a_breaker(string client, bool expected)
    {
        Assert.Equal(expected, CircuitBreakers.AppliesTo(client));
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public int Calls;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Calls);
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(respond(request));
        }
    }

    [Fact]
    public async Task Breaker_handler_stops_calling_a_dead_host_but_not_other_hosts()
    {
        var dead = Guid.NewGuid().ToString("N") + ".dead.example";
        var alive = Guid.NewGuid().ToString("N") + ".alive.example";
        var stub = new StubHandler(request => new HttpResponseMessage(request.RequestUri!.Host == dead ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK));
        using var client = new HttpClient(new CircuitBreakerHandler("supplier") { InnerHandler = stub });

        for (var i = 0; i < CircuitBreakers.FailureThreshold; i++)
        {
            using var response = await client.GetAsync($"https://{dead}/p");
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        }
        var calls = stub.Calls;
        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync($"https://{dead}/p"));
        Assert.Equal(calls, stub.Calls); // درخواست اصلاً فرستاده نشد

        using var ok = await client.GetAsync($"https://{alive}/p");
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
    }

    [Fact]
    public async Task Slow_external_services_that_time_out_open_the_breaker_but_our_own_cancellation_does_not()
    {
        var slow = Guid.NewGuid().ToString("N") + ".slow.example";
        // مهلت HttpClient تمام شد (TaskCanceledException بدون لغو از طرف ما) = شکست سرویس بیرونی.
        var stub = new StubHandler(_ => throw new TaskCanceledException("timeout"));
        using var client = new HttpClient(new CircuitBreakerHandler("seo-engine") { InnerHandler = stub });
        for (var i = 0; i < CircuitBreakers.FailureThreshold; i++)
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetAsync($"https://{slow}/"));
        Assert.NotNull(Assert.Single(CircuitBreakers.All(), s => s.Key == "seo-engine|" + slow).OpenUntil);

        // لغو از طرف خود برنامه (خاموش شدن) شکست حساب نمی‌شود.
        var own = Guid.NewGuid().ToString("N") + ".own.example";
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var never = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var client2 = new HttpClient(new CircuitBreakerHandler("seo-engine") { InnerHandler = never });
        for (var i = 0; i < CircuitBreakers.FailureThreshold + 2; i++)
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client2.GetAsync($"https://{own}/", cts.Token));
        Assert.DoesNotContain(CircuitBreakers.All(), s => s.Key == "seo-engine|" + own && s.OpenUntil is not null);
    }

    [Fact]
    public async Task Client_errors_like_404_do_not_open_the_breaker()
    {
        var host = Guid.NewGuid().ToString("N") + ".example";
        var stub = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        using var client = new HttpClient(new CircuitBreakerHandler("supplier") { InnerHandler = stub });
        for (var i = 0; i < CircuitBreakers.FailureThreshold * 2; i++)
        {
            using var response = await client.GetAsync($"https://{host}/missing");
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
        Assert.Equal(CircuitBreakers.FailureThreshold * 2, stub.Calls);
    }

    // ═══ نسخهٔ ذخیره‌شدهٔ صفحه‌ها ═══

    private static string TempDir() => Path.Combine(Path.GetTempPath(), "fratak-snap-" + Guid.NewGuid().ToString("N"));

    private static DefaultHttpContext Http(string path, string? query = null, string method = "GET", string? cookie = null, string? acceptEncoding = null)
    {
        var http = new DefaultHttpContext();
        http.Request.Method = method;
        http.Request.Path = path;
        if (query is not null)
            http.Request.QueryString = new QueryString(query);
        if (cookie is not null)
            http.Request.Headers.Cookie = cookie;
        if (acceptEncoding is not null)
            http.Request.Headers.AcceptEncoding = acceptEncoding;
        http.Response.Body = new MemoryStream();
        return http;
    }

    [Theory]
    [InlineData("/", null, "GET", null, true)]
    [InlineData("/product/iphone-15", null, "GET", null, true)]
    [InlineData("/blog", "?page=2", "HEAD", null, true)]
    [InlineData("/product/x", null, "POST", null, false)]
    [InlineData("/admin/orders", null, "GET", null, false)]
    [InlineData("/api/search/suggest", null, "GET", null, false)]
    [InlineData("/checkout", null, "GET", null, false)]
    [InlineData("/profile/orders", null, "GET", null, false)]
    [InlineData("/assets/js/main.js", null, "GET", null, false)]
    [InlineData("/product/x", "?handler=AddToCart", "GET", null, false)]
    [InlineData("/product/x", null, "GET", "fratak_rt=abc", false)]
    public void Only_anonymous_public_pages_are_snapshot_candidates(string path, string? query, string method, string? cookie, bool expected)
    {
        Assert.Equal(expected, PageSnapshotStore.IsCandidate(Http(path, query, method, cookie).Request));
    }

    [Fact]
    public void Snapshot_keys_are_stable_and_case_insensitive_for_the_path_only()
    {
        Assert.Equal(PageSnapshotStore.Key("/Product/A", "?x=1"), PageSnapshotStore.Key("/product/a", "?x=1"));
        Assert.NotEqual(PageSnapshotStore.Key("/product/a", "?x=1"), PageSnapshotStore.Key("/product/a", "?x=2"));
        Assert.Equal(32, PageSnapshotStore.Key("/", null).Length);
        Assert.Matches("^[0-9a-f]{32}$", PageSnapshotStore.Key("/../../etc/passwd", null));
    }

    [Fact]
    public async Task Snapshots_round_trip_and_are_served_gzip_or_plain_with_status_200()
    {
        var dir = TempDir();
        try
        {
            var store = new PageSnapshotStore(dir, 100, TimeSpan.FromMinutes(10));
            var html = Encoding.UTF8.GetBytes("<!doctype html><html><body>سلام فروشگاه</body></html>");
            var key = PageSnapshotStore.Key("/faq", null);
            Assert.True(store.ShouldCapture(key, DateTimeOffset.UtcNow));
            await store.SaveAsync(key, html);
            Assert.False(store.ShouldCapture(key, DateTimeOffset.UtcNow));
            Assert.True(store.ShouldCapture(key, DateTimeOffset.UtcNow.AddMinutes(11)));
            Assert.Equal(1, store.Count());

            var plain = Http("/faq");
            Assert.True(await store.TryServeAsync(plain, "error"));
            Assert.Equal(200, plain.Response.StatusCode);
            Assert.Equal("error", plain.Response.Headers[PageSnapshotStore.Header].ToString());
            Assert.Equal("no-store", plain.Response.Headers.CacheControl.ToString());
            Assert.Equal(html, ((MemoryStream)plain.Response.Body).ToArray());

            var gzip = Http("/faq", acceptEncoding: "gzip, br");
            Assert.True(await store.TryServeAsync(gzip, "busy"));
            Assert.Equal("gzip", gzip.Response.Headers.ContentEncoding.ToString());
            await using var unzip = new GZipStream(new MemoryStream(((MemoryStream)gzip.Response.Body).ToArray()), CompressionMode.Decompress);
            using var output = new MemoryStream();
            await unzip.CopyToAsync(output);
            Assert.Equal(html, output.ToArray());
            Assert.Equal(2, store.Served);
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, true);
        }
    }

    [Fact]
    public async Task Snapshots_are_never_served_to_signed_in_users_or_when_disabled()
    {
        var dir = TempDir();
        try
        {
            var store = new PageSnapshotStore(dir, 100, TimeSpan.FromMinutes(10));
            await store.SaveAsync(PageSnapshotStore.Key("/faq", null), "<html>x</html>"u8.ToArray());
            Assert.False(await store.TryServeAsync(Http("/faq", cookie: "fratak_rt=token"), "error"));
            Assert.False(await store.TryServeAsync(Http("/other"), "error"));

            var off = new PageSnapshotStore(dir, 100, TimeSpan.FromMinutes(10), enabled: false);
            Assert.False(off.ShouldCapture("k", DateTimeOffset.UtcNow));
            Assert.False(await off.TryServeAsync(Http("/faq"), "error"));
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, true);
        }
    }

    [Fact]
    public async Task Huge_or_empty_pages_are_not_stored()
    {
        var dir = TempDir();
        try
        {
            var store = new PageSnapshotStore(dir, 100, TimeSpan.FromMinutes(10));
            await store.SaveAsync("a", new byte[PageSnapshotStore.MaxHtmlBytes + 1]);
            await store.SaveAsync("b", Array.Empty<byte>());
            Assert.Equal(0, store.Count());
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, true);
        }
    }

    [Fact]
    public async Task Snapshot_middleware_serves_the_last_good_copy_when_the_page_throws()
    {
        var dir = TempDir();
        try
        {
            var store = new PageSnapshotStore(dir, 100, TimeSpan.FromMinutes(10));
            await store.SaveAsync(PageSnapshotStore.Key("/blog", null), "<html>نسخهٔ سالم</html>"u8.ToArray());
            var middleware = new PageSnapshotMiddleware(_ => throw new InvalidOperationException("database down"), store, NullLogger<PageSnapshotMiddleware>.Instance);

            var http = Http("/blog");
            await middleware.InvokeAsync(http);
            Assert.Equal(200, http.Response.StatusCode);
            Assert.Contains("نسخهٔ سالم", Encoding.UTF8.GetString(((MemoryStream)http.Response.Body).ToArray()), StringComparison.Ordinal);

            // صفحه‌ای که نسخه ندارد: همان خطای معمول به بالا می‌رسد.
            await Assert.ThrowsAsync<InvalidOperationException>(() => middleware.InvokeAsync(Http("/no-copy")));
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Capture_stream_copies_until_the_limit_then_gives_up_without_breaking_the_response()
    {
        var inner = new MemoryStream();
        using var tee = new PageSnapshotMiddleware.CaptureStream(inner, 10);
        tee.Write("12345"u8);
        Assert.Equal(5, tee.Length);
        tee.Write("67890ABC"u8);
        Assert.True(tee.Overflowed);
        Assert.Empty(tee.ToArray());
        Assert.Equal("1234567890ABC", Encoding.ASCII.GetString(inner.ToArray()));
    }

    // ═══ صف ورودی ═══

    [Fact]
    public async Task Load_shedding_queues_then_falls_back_to_a_fast_busy_page_with_retry_after()
    {
        var dir = TempDir();
        try
        {
            var governor = new ResourceGovernor(new ResourceOptions { MaxConcurrentRequests = 4, StartupQuietMinutes = 0 }, DateTimeOffset.UtcNow, default);
            var store = new PageSnapshotStore(dir, 100, TimeSpan.FromMinutes(10));
            var release = new TaskCompletionSource();
            var middleware = new LoadSheddingMiddleware(async _ => await release.Task, store);

            // ۴ درخواست ظرفیت را پر می‌کنند
            var running = Enumerable.Range(0, 4).Select(i => middleware.HandleAsync(Http("/p" + i), governor)).ToList();
            await Task.Delay(50);
            Assert.Equal(4, governor.ActiveRequests);

            // درخواست پنجم بیش از QueueWait (۴ ثانیه در Low) منتظر می‌ماند و بعد صفحهٔ «شلوغ» می‌گیرد
            var busy = Http("/p5");
            var watch = System.Diagnostics.Stopwatch.StartNew();
            await middleware.HandleAsync(busy, governor);
            Assert.True(watch.Elapsed >= TimeSpan.FromSeconds(3));
            Assert.Equal(503, busy.Response.StatusCode);
            Assert.Equal("10", busy.Response.Headers.RetryAfter.ToString());
            Assert.Contains("شلوغ", Encoding.UTF8.GetString(((MemoryStream)busy.Response.Body).ToArray()), StringComparison.Ordinal);

            release.SetResult();
            await Task.WhenAll(running);
            Assert.Equal(0, governor.ActiveRequests);
            Assert.Equal(0, governor.QueuedRequests);
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, true);
        }
    }

    [Fact]
    public async Task Load_shedding_serves_the_snapshot_instead_of_503_when_one_exists()
    {
        var dir = TempDir();
        try
        {
            var governor = new ResourceGovernor(new ResourceOptions { MaxConcurrentRequests = 4, StartupQuietMinutes = 0 }, DateTimeOffset.UtcNow, default);
            var store = new PageSnapshotStore(dir, 100, TimeSpan.FromMinutes(10));
            await store.SaveAsync(PageSnapshotStore.Key("/popular", null), "<html>محبوب</html>"u8.ToArray());
            var release = new TaskCompletionSource();
            var middleware = new LoadSheddingMiddleware(async _ => await release.Task, store);
            var running = Enumerable.Range(0, 4).Select(i => middleware.HandleAsync(Http("/x" + i), governor)).ToList();
            await Task.Delay(50);

            var visitor = Http("/popular");
            await middleware.HandleAsync(visitor, governor);
            Assert.Equal(200, visitor.Response.StatusCode);
            Assert.Equal("busy", visitor.Response.Headers[PageSnapshotStore.Header].ToString());

            release.SetResult();
            await Task.WhenAll(running);
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, true);
        }
    }

    [Fact]
    public async Task A_flood_of_visitors_never_sees_an_error_when_the_page_has_a_snapshot()
    {
        var dir = TempDir();
        try
        {
            var governor = new ResourceGovernor(new ResourceOptions { MaxConcurrentRequests = 4, StartupQuietMinutes = 0 }, DateTimeOffset.UtcNow, default);
            var store = new PageSnapshotStore(dir, 100, TimeSpan.FromMinutes(10));
            await store.SaveAsync(PageSnapshotStore.Key("/", null), "<html>صفحهٔ اول</html>"u8.ToArray());
            var middleware = new LoadSheddingMiddleware(async ctx =>
            {
                await Task.Delay(30);
                ctx.Response.StatusCode = 200;
            }, store);

            // ۲۰۰ بازدید هم‌زمان با ظرفیت ۴ و صف ۸: بقیه نسخهٔ ذخیره‌شده (۲۰۰) می‌گیرند، نه ۵۰۳.
            var visitors = Enumerable.Range(0, 200).Select(_ => Http("/")).ToList();
            await Task.WhenAll(visitors.Select(v => middleware.HandleAsync(v, governor)));

            Assert.All(visitors, v => Assert.Equal(200, v.Response.StatusCode));
            Assert.Contains(visitors, v => v.Response.Headers.ContainsKey(PageSnapshotStore.Header));
            Assert.Equal(0, governor.ActiveRequests);
            Assert.Equal(0, governor.QueuedRequests);
            Assert.Equal(4, governor.RequestSlots.CurrentCount);
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, true);
        }
    }

    [Fact]
    public async Task When_memory_is_critical_pages_with_a_snapshot_are_not_rebuilt()
    {
        var dir = TempDir();
        try
        {
            var governor = new ResourceGovernor(new ResourceOptions { StartupQuietMinutes = 0 }, DateTimeOffset.UtcNow, default);
            governor.Update(Sample(950, 1000));
            Assert.True(LoadSheddingMiddleware.MemoryIsCritical(governor));

            var store = new PageSnapshotStore(dir, 100, TimeSpan.FromMinutes(10));
            await store.SaveAsync(PageSnapshotStore.Key("/blog", null), "<html>بلاگ</html>"u8.ToArray());
            var built = 0;
            var middleware = new LoadSheddingMiddleware(ctx =>
            {
                Interlocked.Increment(ref built);
                ctx.Response.StatusCode = 200;
                return Task.CompletedTask;
            }, store);

            var cached = Http("/blog");
            await middleware.HandleAsync(cached, governor);
            Assert.Equal(200, cached.Response.StatusCode);
            Assert.Equal("memory", cached.Response.Headers[PageSnapshotStore.Header].ToString());
            Assert.Equal(0, built);

            // صفحه‌ای که نسخهٔ ذخیره‌شده ندارد باز هم ساخته می‌شود (نه ۵۰۳).
            var fresh = Http("/faq");
            await middleware.HandleAsync(fresh, governor);
            Assert.Equal(200, fresh.Response.StatusCode);
            Assert.Equal(1, built);

            // حافظهٔ عادی ← صفحه طبق معمول ساخته می‌شود.
            governor.Update(Sample(300, 1000));
            Assert.False(LoadSheddingMiddleware.MemoryIsCritical(governor));
            var normal = Http("/blog");
            await middleware.HandleAsync(normal, governor);
            Assert.Equal(2, built);
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, true);
        }
    }

    [Fact]
    public async Task Purchases_logins_and_signed_in_users_are_never_queued()
    {
        var governor = new ResourceGovernor(new ResourceOptions { MaxConcurrentRequests = 4, StartupQuietMinutes = 0 }, DateTimeOffset.UtcNow, default);
        var store = new PageSnapshotStore(TempDir(), 100, TimeSpan.FromMinutes(10), enabled: false);
        var release = new TaskCompletionSource();
        var hits = 0;
        var middleware = new LoadSheddingMiddleware(async ctx =>
        {
            Interlocked.Increment(ref hits);
            if (ctx.Request.Path.StartsWithSegments("/slow"))
                await release.Task;
        }, store);
        var running = Enumerable.Range(0, 4).Select(i => middleware.HandleAsync(Http("/slow/" + i), governor)).ToList();
        await Task.Delay(50);

        await middleware.HandleAsync(Http("/checkout", method: "POST"), governor);
        await middleware.HandleAsync(Http("/product/x", cookie: "fratak_rt=t"), governor);
        await middleware.HandleAsync(Http("/admin/orders"), governor);
        await middleware.HandleAsync(Http("/api/auth/login", method: "POST"), governor);
        Assert.Equal(8, hits);

        release.SetResult();
        await Task.WhenAll(running);
    }

    [Fact]
    public async Task Heavy_crawlers_get_429_from_the_entry_queue()
    {
        var governor = new ResourceGovernor(new ResourceOptions { StartupQuietMinutes = 0 }, DateTimeOffset.UtcNow, default);
        var middleware = new LoadSheddingMiddleware(_ => Task.CompletedTask, new PageSnapshotStore(TempDir(), 100, TimeSpan.FromMinutes(10), enabled: false));
        governor.Update(Sample(750, 1000)); // افزایش‌یافته ← ربات پرمصرف اصلاً پاسخ نمی‌گیرد
        var http = Http("/product/x");
        http.Request.Headers.UserAgent = "Mozilla/5.0 (compatible; MJ12bot/v1.4.8; http://mj12bot.com/)";
        await middleware.HandleAsync(http, governor);
        Assert.Equal(429, http.Response.StatusCode);
        Assert.Equal("600", http.Response.Headers.RetryAfter.ToString());
    }

    // ═══ شمارش پرس‌وجو ═══

    [Theory]
    [InlineData("/", "/")]
    [InlineData(null, "/")]
    [InlineData("/blog", "/blog")]
    [InlineData("/product/iphone-15", "/product/*")]
    [InlineData("/Product/Galaxy/Extra", "/product/*")]
    [InlineData("/admin/orders", "/admin/orders")]
    [InlineData("/admin/orders/5", "/admin/orders/*")]
    public void Query_stats_group_pages_by_kind(string? path, string expected)
    {
        Assert.Equal(expected, PageQueryStats.KeyOf(path));
    }

    [Fact]
    public void Query_stats_track_average_and_max()
    {
        var path = "/zz" + Guid.NewGuid().ToString("N")[..6] + "/a";
        PageQueryStats.Record(path, 4, 10);
        PageQueryStats.Record(path, 8, 30);
        var row = Assert.Single(PageQueryStats.Top(500), r => r.Path == PageQueryStats.KeyOf(path));
        Assert.Equal(2, row.Requests);
        Assert.Equal(6, row.AverageQueries);
        Assert.Equal(8, row.MaxQueries);
        Assert.Equal(20, row.AverageDbMs);
    }

    [Fact]
    public void Query_counter_scopes_nest_and_restore()
    {
        var before = QueryCounter.Current;
        using (var outer = QueryCounter.Begin())
        {
            Assert.Same(outer, QueryCounter.Current);
            using (var inner = QueryCounter.Begin())
                Assert.Same(inner, QueryCounter.Current);
            Assert.Same(outer, QueryCounter.Current);
            Assert.Equal(0, outer.Count);
        }
        Assert.Same(before, QueryCounter.Current);
    }

    // ═══ استخر اتصال ═══

    [Fact]
    public void Pool_limit_is_added_only_when_missing()
    {
        var added = FratakShopBootstrapper.WithPoolLimit("Server=.;Database=Shop;Trusted_Connection=True;TrustServerCertificate=True", 40);
        Assert.Contains("Max Pool Size=40", added, StringComparison.OrdinalIgnoreCase);
        const string explicitPool = "Server=.;Database=Shop;Max Pool Size=500;TrustServerCertificate=True";
        Assert.Equal(explicitPool, FratakShopBootstrapper.WithPoolLimit(explicitPool, 40));
        Assert.Equal("garbage", FratakShopBootstrapper.WithPoolLimit("garbage", 40));
        Assert.Equal("Server=.", FratakShopBootstrapper.WithPoolLimit("Server=.", 0));
    }

    // ═══ فایل‌های پیش‌فشرده ═══

    [Theory]
    [InlineData("gzip, deflate, br", "br")]
    [InlineData("gzip", "gzip")]
    [InlineData("br;q=0, gzip", "gzip")]
    [InlineData("br; q=0.0, gzip;q=0", null)]
    [InlineData("identity", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Best_accepted_encoding_is_chosen(string? header, string? expected)
    {
        Assert.Equal(expected, PrecompressedStaticFilesMiddleware.ChooseEncoding(header));
    }

    [Theory]
    [InlineData("br")]
    [InlineData("gzip")]
    public async Task Precompressed_bytes_round_trip_and_are_smaller(string encoding)
    {
        var css = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat(".a-card{display:flex;padding:12px;border-radius:8px}\n", 400)));
        var compressed = await PrecompressedStaticFilesMiddleware.CompressAsync(new MemoryStream(css), encoding, CancellationToken.None);
        Assert.True(compressed.Length < css.Length / 5);
        Stream decoder = encoding == "br"
            ? new BrotliStream(new MemoryStream(compressed), CompressionMode.Decompress)
            : new GZipStream(new MemoryStream(compressed), CompressionMode.Decompress);
        await using (decoder)
        {
            using var output = new MemoryStream();
            await decoder.CopyToAsync(output);
            Assert.Equal(css, output.ToArray());
        }
    }

    // ═══ تاریخچهٔ منابع ═══

    [Fact]
    public void Resource_history_summarises_each_minute_and_keeps_peaks()
    {
        var history = new ResourceHistory();
        var minute = new DateTimeOffset(2026, 11, 5, 10, 0, 0, TimeSpan.Zero);
        history.Add(new ResourceSample(minute.AddSeconds(5), 300, 1000, 100, 0.2, 0, 3, 0, 8), PressureLevel.Normal);
        history.Add(new ResourceSample(minute.AddSeconds(35), 500, 1000, 100, 0.4, 0, 9, 0, 8), PressureLevel.High);
        history.Add(new ResourceSample(minute.AddMinutes(1).AddSeconds(5), 200, 1000, 100, 0.1, 0, 1, 0, 8), PressureLevel.Normal);
        var point = Assert.Single(history.Points());
        Assert.Equal(400, point.MemoryMb);
        Assert.Equal(500, point.PeakMemoryMb);
        Assert.Equal(9, point.PeakRequests);
        Assert.Equal(PressureLevel.High, point.Worst);
        Assert.Equal(500, history.PeakMemoryMb);

        for (var i = 0; i < ResourceHistory.MaxEvents + 10; i++)
            history.Note("رویداد " + i);
        Assert.Equal(ResourceHistory.MaxEvents, history.Events().Count);
        Assert.StartsWith("رویداد " + (ResourceHistory.MaxEvents + 9), history.Events()[0].Text, StringComparison.Ordinal);
    }
}
