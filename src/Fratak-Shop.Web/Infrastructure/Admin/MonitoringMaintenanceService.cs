using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Core.Services.Settings;

namespace Fratak_Shop.Web.Infrastructure.Admin;

/// <summary>Daily removal of old error / audit / security log rows (retention from the settings).</summary>
public sealed class MonitoringMaintenanceService(IServiceScopeFactory scopeFactory, ILogger<MonitoringMaintenanceService> logger) : BackgroundService
{
    public const string JobName = "پاک‌سازی لاگ‌های قدیمی";

    public static readonly (string Name, TimeSpan Interval)[] KnownJobs =
    [
        ("لغو سفارش‌های پرداخت‌نشده", TimeSpan.FromMinutes(5)),
        ("پاک‌سازی نشست‌های منقضی", TimeSpan.FromMinutes(5)),
        ("سبدهای رهاشده", TimeSpan.FromMinutes(5)),
        (OrderMaintenanceService.SlaJobName, TimeSpan.FromMinutes(15)),
        ("هدیه تولد باشگاه", TimeSpan.FromHours(1)),
        ("انقضای امتیازها", TimeSpan.FromHours(1)),
        ("Product alerts", TimeSpan.FromMinutes(5)),
        ("SEO traffic flush", TimeSpan.FromMinutes(5)),
        ("Price history snapshot", TimeSpan.FromHours(24)),
        ("Price history prune", TimeSpan.FromHours(24)),
        ("Gift card maintenance", TimeSpan.FromHours(24)),
        ("404 log prune", TimeSpan.FromHours(24)),
        (JobName, TimeSpan.FromHours(24)),
    ];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        foreach (var (name, interval) in KnownJobs)
            JobMonitor.Register(name, interval);

        try
        {
            await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);
            using var timer = new PeriodicTimer(TimeSpan.FromHours(24));
            do
            {
                try
                {
                    using var scope = scopeFactory.CreateScope();
                    var settings = await scope.ServiceProvider.GetRequiredService<IStoreSettingService>().GetAsync(stoppingToken);
                    var removed = await JobMonitor.TrackAsync(JobName, async () =>
                    {
                        var logs = await scope.ServiceProvider.GetRequiredService<IMonitoringService>()
                            .PurgeAsync(settings.ErrorLogRetentionDays, settings.AuditLogRetentionDays, stoppingToken);
                        // پیش‌نویس‌های خودکار فرم‌ها که ۳۰ روز دست نخورده‌اند (EditDraftService).
                        var drafts = await scope.ServiceProvider.GetRequiredService<IEditDraftService>()
                            .PurgeAsync(TimeSpan.FromDays(30), stoppingToken);
                        return logs + drafts;
                    });
                    if (removed > 0)
                        logger.LogInformation("Removed {Count} old log rows", removed);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    logger.LogError(exception, "Log retention cleanup failed");
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
        }
    }
}
