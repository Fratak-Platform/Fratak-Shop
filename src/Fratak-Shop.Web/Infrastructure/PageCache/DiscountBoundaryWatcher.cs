using Fratak_Shop.Data.Contexts;
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
/// ثبت: <c>WebServiceCollectionExtensions</c> با <c>AddHostedService</c>.
/// </summary>
public sealed class DiscountBoundaryWatcher(IServiceScopeFactory scopeFactory, ILogger<DiscountBoundaryWatcher> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // از لحظهٔ شروع برنامه؛ صفحه‌های کش‌شدهٔ قبل از آن با راه‌اندازی دوباره از بین رفته‌اند.
        var last = DateTimeOffset.UtcNow;
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
}
