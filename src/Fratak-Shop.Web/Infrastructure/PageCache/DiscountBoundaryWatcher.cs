using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Core.Services.Smart;
using Fratak_Shop.Data.Entities.Marketing;
using Fratak_Shop.Data.Entities.Products;
using Fratak_Shop.Data.Utilities;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Web.Infrastructure.PageCache;

/// <summary>
/// نگهبان «مرز حراج زمان‌دار» (مرحلهٔ هفتم – فاز ۵).
/// <para>
/// مشکل: کش صفحه‌ها فقط با ذخیرهٔ دیتابیس پاک می‌شود. حراجی که ساعت ۱۸:۰۰ شروع یا تمام می‌شود هیچ ذخیره‌ای ندارد،
/// پس صفحهٔ کش‌شده ممکن است تا انقضای کش قیمت قدیمی را نشان دهد.
/// </para>
/// <para>
/// راه‌حل: هر دقیقه بررسی می‌شود که آیا از بررسی قبلی تا الان حراجی شروع یا تمام شده است؛ اگر بله، رویداد
/// «محصول تغییر کرد» (<see cref="DataChangeNotifier.Publish(Type[])"/>) منتشر می‌شود تا کش صفحه‌ها و به‌روزرسانی زندهٔ
/// سایت دقیقاً مثل یک ویرایش دستی عمل کنند. هزینه: یک کوئری سبک با ایندکس در دقیقه.
/// </para>
/// <para>
/// دور ۲۹: همین نگهبان مرزهای بنر را هم می‌پاید: شروع/پایان زمان‌بندی بنر، و مرز «پنجرهٔ چرخش» بنرهای اضافه
/// (<see cref="Fratak_Shop.Core.Services.Marketing.BannerRules.Slots"/>؛ هر BannerRotateHours ساعت) ← صفحهٔ اصلی کش‌شده
/// بنرهای نوبت تازه را نشان می‌دهد. مثال: پنجرهٔ ۶ ساعته ← ساعت‌های ۰، ۶، ۱۲ و ۱۸ (UTC) کش صفحه یک بار خالی می‌شود.
/// </para>
/// ثبت: <c>WebServiceCollectionExtensions</c> با <c>AddHostedService</c>.
/// </summary>
public sealed class DiscountBoundaryWatcher(IServiceScopeFactory scopeFactory, ILogger<DiscountBoundaryWatcher> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // از لحظهٔ شروع برنامه؛ صفحه‌های کش‌شدهٔ قبل از آن با راه‌اندازی دوباره از بین رفته‌اند.
        var last = DateTimeOffset.UtcNow;
        long? lastWindow = null;
        using var timer = new PeriodicTimer(Interval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                var now = DateTimeOffset.UtcNow;
                try
                {
                    await using var scope = scopeFactory.CreateAsyncScope();
                    var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
                    var crossed = await context.Set<Product>().AnyAsync(p => p.DiscountPrice != null
                        && ((p.DiscountStartAt > last && p.DiscountStartAt <= now) || (p.DiscountEndAt > last && p.DiscountEndAt <= now)), stoppingToken);
                    if (crossed)
                    {
                        logger.LogInformation("Timed discount boundary crossed; refreshing product caches");
                        DataChangeNotifier.Publish(typeof(Product));
                    }

                    // دور ۲۹: مرز زمان‌بندی بنرها و پنجرهٔ چرخش.
                    var bannerCrossed = await context.Set<Banner>().AnyAsync(b => b.IsActive
                        && ((b.StartAt > last && b.StartAt <= now) || (b.EndAt > last && b.EndAt <= now)), stoppingToken);
                    var hours = (await scope.ServiceProvider.GetRequiredService<ISmartConfigService>().GetAsync(stoppingToken)).BannerRotateHours;
                    var window = RotationWindow(now, hours);
                    if (bannerCrossed || (lastWindow is { } previous && previous != window))
                    {
                        logger.LogInformation("Banner schedule/rotation boundary crossed; refreshing page caches");
                        DataChangeNotifier.Publish(typeof(Banner));
                    }
                    lastWindow = window;
                    last = now;
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    // بازهٔ بعدی همین بازه را هم پوشش می‌دهد (last جلو نمی‌رود).
                    logger.LogWarning(exception, "Discount boundary check failed");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // توقف برنامه.
        }
    }

    /// <summary>
    /// شمارهٔ «پنجرهٔ چرخش» بنرها (همان فرمول <c>BannerRules.Slots</c>). مثال: ساعت ۷ با پنجرهٔ ۶ ساعته ← پنجرهٔ بعد از ساعت ۶.
    /// </summary>
    public static long RotationWindow(DateTimeOffset now, int rotateHours) => now.ToUnixTimeSeconds() / 3600 / Math.Max(1, rotateHours);
}
