using System.Diagnostics;
using Fratak_Shop.Core.Services.Categories;
using Fratak_Shop.Core.Services.Contents;
using Fratak_Shop.Core.Services.Settings;

namespace Fratak_Shop.Web.Infrastructure.Ops;

/// <summary>
/// دور نوزدهم (A26): «گرم کردن» برنامه بلافاصله بعد از آماده شدن پایگاه داده.
/// <para>بعد از هر ری‌استارت (بازیافت استخر IIS، بیکاری، انتشار نسخهٔ جدید) اولین بازدیدکننده باید منتظر بماند تا تنظیمات، منوها،
/// دسته‌ها و بخش‌های صفحهٔ اول از پایگاه داده خوانده و در کش حافظه گذاشته شوند و کدشان کامپایل (JIT) شود. این سرویس همان کارها را
/// یک بار در پس‌زمینه انجام می‌دهد تا اولین بازدیدکنندهٔ واقعی صفحهٔ سریع ببیند.</para>
/// <para>کاملاً بی‌خطر: هر مرحله جدا محافظت می‌شود و شکستش فقط لاگ می‌شود. مدت گرم شدن در صفحهٔ «منابع سرور» دیده می‌شود.</para>
/// <para>ثبت: WebServiceCollectionExtensions (AddHostedService). در آزمون‌های سرتاسری حذف می‌شود.</para>
/// </summary>
public sealed class WarmupService(IServiceScopeFactory scopeFactory, ResourceHistory history, ILogger<WarmupService> logger) : BackgroundService
{
    /// <summary>مدت آخرین گرم شدن (میلی‌ثانیه) یا null اگر هنوز انجام نشده.</summary>
    public static double? LastWarmupMs { get; private set; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            // صبر تا آماده شدن پایگاه داده (حداکثر ۱۰ دقیقه).
            for (var i = 0; i < 600 && !StartupGate.IsReady; i++)
                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
            if (!StartupGate.IsReady)
                return;

            var watch = Stopwatch.StartNew();
            using var scope = scopeFactory.CreateScope();
            var services = scope.ServiceProvider;
            await StepAsync("settings", () => services.GetRequiredService<IStoreSettingService>().GetAsync(stoppingToken));
            await StepAsync("site", () => services.GetRequiredService<ISiteContext>().LoadAsync(stoppingToken));
            await StepAsync("home", () => services.GetRequiredService<ISiteContentService>().GetActiveHomeSectionsAsync(stoppingToken));
            await StepAsync("categories", () => services.GetRequiredService<ICategoryService>().GetMenuCategoriesAsync());
            LastWarmupMs = Math.Round(watch.Elapsed.TotalMilliseconds);
            history.Note($"گرم شدن برنامه بعد از روشن شدن: {LastWarmupMs:0} میلی‌ثانیه");
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task StepAsync(string name, Func<Task> step)
    {
        try
        {
            await step();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogDebug(exception, "Warm-up step {Step} failed", name);
        }
    }
}
