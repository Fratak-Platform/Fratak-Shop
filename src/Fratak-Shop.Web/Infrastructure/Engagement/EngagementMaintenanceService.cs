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
        // دور ۲۴: امضای تصویر کالاها برای «جست‌وجو با عکس» (هر نوبت حداکثر ۴۰ کالا؛ فقط وقتی قابلیت روشن است).
        await RunAsync("Image search signatures", async sp =>
        {
            var smart = await sp.GetRequiredService<Fratak_Shop.Core.Services.Smart.ISmartConfigService>().GetAsync(token);
            return smart.ImageSearch ? await sp.GetRequiredService<Fratak_Shop.Web.Infrastructure.Commerce.ImageSearchService>().BuildSignaturesAsync(token) : 0;
        }, token);

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
        // دور ۲۳: پیام‌های دستیار فروشگاه ۱۸۰ روز نگه داشته می‌شوند؛ کالاهایی که پرسش بی‌جواب دارند به «دریافت مشخصات» هوشمند می‌روند.
        await RunAsync("Assistant prune", sp => sp.GetRequiredService<Fratak_Shop.Core.Services.Assistant.IAssistantService>().PruneAsync(180, token), token);
        await RunAsync("Assistant gaps feed", sp => sp.GetRequiredService<Fratak_Shop.Core.Services.Assistant.IAssistantService>().FeedGapsAsync(10, token), token);
        // دور ۲۴: مدل یادگیرندهٔ نیت پیام هر شب با پیام‌های واقعی ۹۰ روز اخیر دوباره آموزش می‌بیند؛ گفت‌وگوهای کارشناس بی‌پیام ۲۴ ساعته بسته می‌شوند.
        await RunAsync("Assistant intent model", sp => sp.GetRequiredService<Fratak_Shop.Core.Services.Assistant.IAssistantService>().RetrainIntentModelAsync(token), token);
        await RunAsync("Assistant handoff auto-close", sp => sp.GetRequiredService<Fratak_Shop.Core.Services.Assistant.IAssistantHandoffService>().AutoCloseAsync(token), token);
        // دور ۲۴: کلیک‌های جست‌وجوی قدیمی کم‌وزن و خیلی قدیمی‌ها پاک می‌شوند (رتبه‌بندی یادگیرنده سلیقهٔ تازه را دنبال کند).
        await RunAsync("Search learning maintenance", sp => sp.GetRequiredService<Fratak_Shop.Core.Services.Searches.ISearchLearningService>().MaintainAsync(token), token);
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
