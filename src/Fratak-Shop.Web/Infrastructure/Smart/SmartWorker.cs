using Fratak_Shop.Core.Services.Smart;
using Fratak_Shop.Data.Entities.Smart;
using Fratak_Shop.Web.Infrastructure.Admin;

namespace Fratak_Shop.Web.Infrastructure.Smart;

/// <summary>
/// کارگر پس‌زمینهٔ محتوای هوشمند (B21، C19–C21).
/// <list type="bullet">
/// <item>هر ۲۰ ثانیه (صف خالی: هر ۶۰ ثانیه) یک کار از صف هوشمند برمی‌دارد و با <see cref="SmartOrchestrator"/> اجرا می‌کند؛
/// خطا ← تلاش دوباره با فاصلهٔ فزاینده (حداکثر ۳ بار، بعد «ناموفق» با پیام خطا در پنل).</item>
/// <item>هر ۳۰ دقیقه: یادآوری‌های مشتری (درخواست نظر و تمدید کالای مصرفی، با سکوت شبانه).</item>
/// <item>هر ۶ ساعت: تکمیل تقویم محتوای ۴ هفتهٔ آینده و گذاشتن مقاله‌های سررسیده در صف؛ پاک‌سازی کش تحقیق و کارهای قدیمی.</item>
/// </list>
/// <para>همه از مدیر منابع می‌گذرند (کار هوشمند «سنگین» است: یکی‌یکی و فقط وقتی سرور زیر فشار نیست – A1)
/// و از صفحهٔ «فنی ← کارهای پس‌زمینه» قابل توقف‌اند. هیچ استثنایی از ExecuteAsync بیرون نمی‌رود.</para>
/// </summary>
public sealed class SmartWorker(IServiceScopeFactory scopeFactory, ILogger<SmartWorker> logger) : BackgroundService
{
    public const string JobName = "محتوای هوشمند";
    public const string ReminderJobName = "یادآوری‌های مشتری";
    public const string PlannerJobName = "برنامه‌ریز محتوای هوشمند";

    private static readonly TimeSpan BusyDelay = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan IdleDelay = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan ReminderEvery = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan PlannerEvery = TimeSpan.FromHours(6);

    private readonly string _workerId = Environment.MachineName + ":" + Environment.ProcessId;
    private DateTimeOffset _lastReminders = DateTimeOffset.MinValue;
    private DateTimeOffset _lastPlanner = DateTimeOffset.MinValue;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        JobMonitor.Register(JobName, TimeSpan.FromHours(12));
        JobMonitor.Register(ReminderJobName, TimeSpan.FromHours(2));
        JobMonitor.Register(PlannerJobName, TimeSpan.FromHours(12));
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(90), stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = IdleDelay;
            try
            {
                var now = DateTimeOffset.UtcNow;
                if (now - _lastReminders >= ReminderEvery)
                {
                    _lastReminders = now;
                    await RunScopedAsync(ReminderJobName, async sp =>
                    {
                        var result = await sp.GetRequiredService<ICustomerReminderService>().RunAsync(DateTimeOffset.UtcNow, stoppingToken);
                        return result.Sent + result.Scheduled;
                    });
                }
                if (now - _lastPlanner >= PlannerEvery)
                {
                    _lastPlanner = now;
                    await RunScopedAsync(PlannerJobName, sp => PlanAsync(sp, stoppingToken));
                }
                var processed = await RunScopedAsync(JobName, sp => ProcessOneAsync(sp, stoppingToken));
                if (processed > 0)
                    delay = BusyDelay;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Smart worker iteration failed");
                delay = TimeSpan.FromMinutes(2);
            }

            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task<int> RunScopedAsync(string job, Func<IServiceProvider, Task<int>> run)
    {
        using var scope = scopeFactory.CreateScope();
        return await JobMonitor.TrackAsync(job, () => run(scope.ServiceProvider));
    }

    /// <summary>برنامه‌ریزی تقویم، صف کردن مقاله‌های سررسیده و پاک‌سازی.</summary>
    private static async Task<int> PlanAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var options = await services.GetRequiredService<ISmartConfigService>().GetAsync(cancellationToken);
        var queue = services.GetRequiredService<ISmartTaskQueue>();
        var count = 0;
        if (options.Enabled && options.BlogPlanner && options.BlogPostsPerWeek > 0)
        {
            var plan = services.GetRequiredService<IBlogPlanService>();
            count += await plan.PlanAsync(options.BlogPostsPerWeek, cancellationToken);
            foreach (var row in await plan.DueAsync(DateTimeOffset.UtcNow, 2, cancellationToken))
            {
                await queue.EnqueueAsync(SmartTaskKind.BlogArticle, SmartTarget.BlogPlan, row.Id, row.Topic, SmartPriority.Nightly,
                    userName: SmartOrchestrator.Actor, cancellationToken: cancellationToken);
                count++;
            }
        }
        await services.GetRequiredService<ISmartResearchStore>().PruneAsync(cancellationToken);
        await queue.PurgeAsync(cancellationToken);
        return count;
    }

    private async Task<int> ProcessOneAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var options = await services.GetRequiredService<ISmartConfigService>().GetAsync(cancellationToken);
        if (!options.Enabled)
            return 0;
        var queue = services.GetRequiredService<ISmartTaskQueue>();
        var task = await queue.ClaimNextAsync(_workerId, Lease, cancellationToken);
        if (task is null)
            return 0;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(Lease - TimeSpan.FromMinutes(1));
            var outcome = await services.GetRequiredService<SmartOrchestrator>().RunAsync(task, timeout.Token);
            await queue.CompleteAsync(task.Id, outcome.Status, outcome.Summary, outcome.ResultJson, cancellationToken: cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // خاموش شدن برنامه: کار با پایان اجاره دوباره در صف قرار می‌گیرد.
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Smart task {Task} ({Kind}) failed", task.Id, task.Kind);
            var message = exception is OperationCanceledException ? "زمان کار تمام شد (منابع بیرونی کند بودند)." : "خطا: " + exception.Message;
            await queue.RetryLaterAsync(task.Id, TimeSpan.FromMinutes(15 * Math.Max(1, task.Attempts)), message, CancellationToken.None);
        }
        return 1;
    }
}
