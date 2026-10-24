using Fratak_Shop.Core.Services.Auth;
using Fratak_Shop.Core.Services.Orders;

namespace Fratak_Shop.Web.Infrastructure;

/// <summary>
/// Background housekeeping every few minutes: cancels online orders whose payment
/// deadline passed (stock is released) and deletes expired / revoked refresh tokens.
/// </summary>
public class OrderMaintenanceService(IServiceScopeFactory scopeFactory, ILogger<OrderMaintenanceService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

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
            var expired = await orders.ExpireUnpaidOrdersAsync(now, stoppingToken);
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
            await tokens.CleanupAsync(now, stoppingToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Refresh token cleanup failed");
        }
    }
}
