using Fratak_Shop.Core.Services.Pricing;
using Fratak_Shop.Web.Infrastructure.Admin;

namespace Fratak_Shop.Web.Infrastructure.Suppliers;

/// <summary>
/// کار پس‌زمینهٔ «قیمت‌گذاری بازار» محصولات درون‌ریزی‌شده (مرحلهٔ سیزدهم).
/// <para>هر چند ثانیه یک ردیف سررسیدهٔ صف MarketPriceChecks را با <see cref="IMarketPricingService.ProcessNextAsync"/> پردازش می‌کند.
/// سرعت عمداً کم است (هر محصول چند درخواست به ترب/دیجی‌کالا/باسلام می‌فرستد و دروازهٔ منابع بینشان فاصله می‌گذارد)
/// تا سرور ما محدود نشود؛ ۳۰ محصول معمولاً در کمتر از ۵ دقیقه قیمت‌گذاری می‌شوند.</para>
/// <para>از صفحهٔ «فنی ← کارهای پس‌زمینه» قابل توقف است. هیچ استثنایی از ExecuteAsync بیرون نمی‌رود.</para>
/// </summary>
public sealed class MarketPricingWorker(IServiceScopeFactory scopeFactory, ILogger<MarketPricingWorker> logger) : BackgroundService
{
    public const string JobName = "قیمت‌گذاری بازار درون‌ریزی";

    /// <summary>فاصله وقتی ردیف پردازش شد (ردیف بعدی احتمالاً آماده است).</summary>
    private static readonly TimeSpan BusyDelay = TimeSpan.FromSeconds(3);

    /// <summary>فاصله وقتی صف خالی است یا منابع در حال استراحت‌اند.</summary>
    private static readonly TimeSpan IdleDelay = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // صف خالی هم «سالم» است؛ بازهٔ انتظار بزرگ تا هشدار گیرکردن بی‌دلیل نیاید.
        JobMonitor.Register(JobName, TimeSpan.FromHours(6));
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(45), stoppingToken);
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
                using var scope = scopeFactory.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<IMarketPricingService>();
                var processed = await JobMonitor.TrackAsync(JobName, async () => await service.ProcessNextAsync(stoppingToken) ? 1 : 0);
                if (processed > 0)
                    delay = BusyDelay;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Market pricing step failed");
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
}
