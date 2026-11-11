using Fratak_Shop.Core.Services.Assistant;

namespace Fratak_Shop.Web.Infrastructure.Assistant;

/// <summary>
/// دور ۲۸ – سرویس پس‌زمینهٔ یک‌باره: چند ثانیه بعد از راه‌اندازی، ردیف‌های تازهٔ فایل «دانش دستیار» را در جدول AssistantFacts می‌ریزد.
/// <para>تا وقتی بذر کامل نشده دستیار از همان فایل داخلی استفاده می‌کند (هیچ قابلیتی منتظر نمی‌ماند). ردیف‌های ویرایش‌شده یا حذف‌شدهٔ
/// کارکنان هرگز دوباره ساخته یا بازنویسی نمی‌شوند. هر خطا فقط لاگ می‌شود.</para>
/// </summary>
public sealed class AssistantFactSeedWorker(IServiceScopeFactory scopeFactory, ILogger<AssistantFactSeedWorker> logger) : BackgroundService
{
    /// <summary>تأخیر پیش از شروع (بعد از مهاجرت‌ها و بذرهای اصلی).</summary>
    public static readonly TimeSpan StartDelay = TimeSpan.FromSeconds(8);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(StartDelay, stoppingToken);
            using var scope = scopeFactory.CreateScope();
            var added = await scope.ServiceProvider.GetRequiredService<IAssistantFactService>().EnsureSeededAsync(stoppingToken);
            if (added > 0)
                logger.LogInformation("Assistant knowledge seeded with {Count} rows", added);
        }
        catch (OperationCanceledException)
        {
            // خاموش شدن پیش از پایان: بار بعد ادامه پیدا می‌کند.
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Assistant knowledge seeding failed; the embedded seed stays in use.");
        }
    }
}
