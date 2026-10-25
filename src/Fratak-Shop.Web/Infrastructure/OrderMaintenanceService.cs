using Fratak_Shop.Core.Services.Auth;
using Fratak_Shop.Core.Services.Loyalty;
using Fratak_Shop.Core.Services.Orders;
using Fratak_Shop.Core.Services.Shopping;

namespace Fratak_Shop.Web.Infrastructure;

/// <summary>
/// Background housekeeping every few minutes: cancels online orders whose payment
/// deadline passed (stock is released) and deletes expired / revoked refresh tokens.
/// Abandoned carts of signed-in customers are detected and reminded on the same cycle.
/// Hourly: loyalty birthday gifts (once per Persian year) and expiry of old points.
/// </summary>
public class OrderMaintenanceService(IServiceScopeFactory scopeFactory, ILogger<OrderMaintenanceService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan LoyaltyInterval = TimeSpan.FromHours(1);
    private DateTimeOffset _lastLoyaltyRun = DateTimeOffset.MinValue;

    /// <summary>هشدار دیرکرد سفارش‌ها هر ۱۵ دقیقه بررسی می‌شود (هر سفارش برای هر مرحله فقط یک بار هشدار می‌گیرد).</summary>
    private static readonly TimeSpan SlaInterval = TimeSpan.FromMinutes(15);
    private DateTimeOffset _lastSlaRun = DateTimeOffset.MinValue;

    /// <summary>نام کار در صفحهٔ «کارهای پس‌زمینه»ی پنل.</summary>
    public const string SlaJobName = "هشدار زمان‌بندی سفارش‌ها";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Let the application finish starting (migrations, seeding) first.
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
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

    private async Task RunOnceAsync(CancellationToken stoppingToken)
    {
        var now = DateTimeOffset.UtcNow;
        try
        {
            using var scope = scopeFactory.CreateScope();
            var orders = scope.ServiceProvider.GetRequiredService<IOrderService>();
            var expired = await Admin.JobMonitor.TrackAsync("لغو سفارش‌های پرداخت‌نشده", () => orders.ExpireUnpaidOrdersAsync(now, stoppingToken));
            if (expired > 0)
                logger.LogInformation("Cancelled {Count} unpaid orders after their payment deadline", expired);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Expiring unpaid orders failed");
        }

        try
        {
            using var scope = scopeFactory.CreateScope();
            var tokens = scope.ServiceProvider.GetRequiredService<IRefreshTokenService>();
            await Admin.JobMonitor.TrackAsync("پاک‌سازی نشست‌های منقضی", () => tokens.CleanupAsync(now, stoppingToken));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Refresh token cleanup failed");
        }

        try
        {
            using var scope = scopeFactory.CreateScope();
            var abandoned = scope.ServiceProvider.GetRequiredService<IAbandonedCartService>();
            var result = await Admin.JobMonitor.TrackAsync("سبدهای رهاشده", () => abandoned.ProcessAsync(now, stoppingToken));
            if (result.Detected + result.RemindersSent + result.Closed > 0)
                logger.LogInformation("Abandoned carts: {Detected} detected, {Sent} reminders sent, {Closed} closed",
                    result.Detected, result.RemindersSent, result.Closed);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Abandoned cart processing failed");
        }

        if (now - _lastSlaRun >= SlaInterval)
        {
            _lastSlaRun = now;
            try
            {
                using var scope = scopeFactory.CreateScope();
                var sla = scope.ServiceProvider.GetRequiredService<IOrderSlaService>();
                var alerted = await Admin.JobMonitor.TrackAsync(SlaJobName, () => sla.AlertOverdueAsync(now, stoppingToken));
                if (alerted > 0)
                    logger.LogInformation("Order SLA: {Count} overdue orders reported to staff", alerted);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "Order SLA alerting failed");
            }
        }

        if (now - _lastLoyaltyRun < LoyaltyInterval)
            return;

        _lastLoyaltyRun = now;
        try
        {
            using var scope = scopeFactory.CreateScope();
            var loyalty = scope.ServiceProvider.GetRequiredService<ILoyaltyService>();

            // Both are idempotent (birthday gifts use a per-year reference), so reruns are harmless.
            var birthdays = await Admin.JobMonitor.TrackAsync("هدیه تولد باشگاه", () => loyalty.AwardBirthdaysAsync(now, stoppingToken));
            var expired = await Admin.JobMonitor.TrackAsync("انقضای امتیازها", () => loyalty.ExpirePointsAsync(now, stoppingToken));
            if (birthdays > 0 || expired > 0)
                logger.LogInformation("Loyalty: {Birthdays} birthday gifts, points expired for {Expired} customers", birthdays, expired);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Loyalty maintenance failed");
        }
    }
}
