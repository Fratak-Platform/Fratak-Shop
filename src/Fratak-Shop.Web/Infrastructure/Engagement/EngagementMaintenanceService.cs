using Fratak_Shop.Core.Services.Marketing;
using Fratak_Shop.Core.Services.Products;
using Fratak_Shop.Core.Services.Seo;
using Fratak_Shop.Core.Services.Settings;

namespace Fratak_Shop.Web.Infrastructure.Engagement;

/// <summary>
/// Background jobs of the round-5 features.
/// Every 5 minutes: «خبرم کن» alerts (back in stock / price drop), SEO traffic counters.
/// Daily: price-history snapshot (sales starting / ending) and pruning, expired gift cards, old 404 rows.
/// Each job runs in its own scope and failures are logged without stopping the others.
/// </summary>
public sealed class EngagementMaintenanceService(IServiceScopeFactory scopeFactory, ILogger<EngagementMaintenanceService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan DailyInterval = TimeSpan.FromHours(24);
    private DateTimeOffset _lastDailyRun = DateTimeOffset.MinValue;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(45), stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        using var timer = new PeriodicTimer(Interval);
        do
        {
            await RunOnceAsync(stoppingToken);
        }
        while (await WaitAsync(timer, stoppingToken));

        // Write what is still buffered when the app stops.
        await RunAsync("SEO traffic flush", sp => sp.GetRequiredService<ISeoTrafficService>().FlushAsync(CancellationToken.None), CancellationToken.None);
    }

    private static async Task<bool> WaitAsync(PeriodicTimer timer, CancellationToken token)
    {
        try
        {
            return await timer.WaitForNextTickAsync(token);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    private async Task RunOnceAsync(CancellationToken token)
    {
        await RunAsync("Product alerts", sp => sp.GetRequiredService<IProductAlertService>().ProcessAsync(token), token);
        await RunAsync("SEO traffic flush", sp => sp.GetRequiredService<ISeoTrafficService>().FlushAsync(token), token);

        var now = DateTimeOffset.UtcNow;
        if (now - _lastDailyRun < DailyInterval)
            return;
        _lastDailyRun = now;

        await RunAsync("Price history snapshot", sp => sp.GetRequiredService<IPriceHistoryService>().SnapshotAsync(token), token);
        await RunAsync("Price history prune", async sp =>
        {
            var settings = await sp.GetRequiredService<IStoreSettingService>().GetAsync();
            // Keep twice the displayed range (at least a year) so changing the setting later still has data.
            return await sp.GetRequiredService<IPriceHistoryService>().PruneAsync(Math.Max(365, settings.PriceHistoryDays * 2), token);
        }, token);
        await RunAsync("Gift card maintenance", sp => sp.GetRequiredService<IGiftCardService>().MaintainAsync(token), token);
        await RunAsync("404 log prune", sp => sp.GetRequiredService<ISeoTrafficService>().PruneAsync(token), token);
        // اعلان‌های پنل کارکنان بیش از ۹۰ روز نگه داشته نمی‌شوند.
        await RunAsync("Staff notification prune", sp => sp.GetRequiredService<Fratak_Shop.Core.Services.Admin.IStaffNotificationService>().PurgeAsync(TimeSpan.FromDays(90)), token);
    }

    private async Task RunAsync(string name, Func<IServiceProvider, Task<int>> job, CancellationToken token)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var count = await Admin.JobMonitor.TrackAsync(name, () => job(scope.ServiceProvider));
            if (count > 0)
                logger.LogInformation("{Job}: {Count} item(s)", name, count);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !token.IsCancellationRequested)
        {
            logger.LogError(exception, "{Job} failed", name);
        }
    }
}
