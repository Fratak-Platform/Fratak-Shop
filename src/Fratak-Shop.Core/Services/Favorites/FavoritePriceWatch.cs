using Fratak_Shop.Core.DTOs.Notifications;
using Fratak_Shop.Core.Services.Notifications;
using Fratak_Shop.Core.Services.Smart;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Notifications;
using Fratak_Shop.Data.Entities.Products;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Fratak_Shop.Core.Services.Favorites;

// ═══════════════════════════════════════════════════════════════════════════
//  دور ۲۵ (الف) – «کالای مورد علاقه‌ات ارزان شد»
//  وقتی مشتری کالایی را به علاقه‌مندی‌ها اضافه می‌کند، قیمت نهایی همان لحظه (WatchPrice) ذخیره می‌شود.
//  هر ۵ دقیقه (نگه‌داری تعامل) اگر قیمت نهایی کالای موجود دست‌کم «درصد تنظیم‌شده» (پیش‌فرض ۵٪) پایین‌تر از WatchPrice باشد،
//  یک اعلان «کاهش قیمت» (سایت + پوش طبق قالب PriceDrop) برای مشتری می‌رود و WatchPrice = قیمت جدید می‌شود.
//  ضد مزاحمت: هر علاقه‌مندی حداکثر یک اعلان در ۷ روز؛ هر مشتری حداکثر ۲ اعلان در ۲۴ ساعت؛ در ساعت سکوت شبانه هیچ؛
//  اگر مشتری برای همان کالا «خبرم کن – کاهش قیمت» فعال دارد، همان سیستم خبر می‌دهد (اعلان تکراری نمی‌رود).
//  علاقه‌مندی‌های قدیمی (پیش از این قابلیت) بی‌اعلان با قیمت فعلی پر می‌شوند.
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>نتیجهٔ یک نوبت: پر شدن قیمت قدیمی‌ها، تعداد اعلان، ردشده‌ها (سقف/خبرم‌کن)، و آیا ساعت سکوت بود.</summary>
public sealed record FavoriteWatchResult(int Backfilled, int Notified, int Skipped, bool Quiet);

/// <summary>پایش قیمت کالاهای مورد علاقه.</summary>
public interface IFavoritePriceWatch
{
    /// <summary>یک نوبت پایش (هر ۵ دقیقه). <paramref name="now"/> برای تست.</summary>
    Task<FavoriteWatchResult> ProcessAsync(DateTimeOffset now, CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public sealed class FavoritePriceWatch(
    FratakShopDbContext context,
    INotificationService notifications,
    ISmartConfigService smart,
    ILogger<FavoritePriceWatch> logger) : IFavoritePriceWatch
{
    /// <summary>کمترین فاصلهٔ دو اعلان برای یک علاقه‌مندی.</summary>
    public static readonly TimeSpan PerFavoriteGap = TimeSpan.FromDays(7);

    /// <summary>بیشترین اعلان برای هر مشتری در ۲۴ ساعت.</summary>
    public const int PerUserDaily = 2;

    /// <summary>بیشترین اعلان در هر نوبت (برای سبک ماندن سرور).</summary>
    public const int MaxPerRun = 200;

    /// <summary>
    /// قیمت به اندازهٔ کافی پایین آمده؟ مثال: (watch ۱٬۰۰۰٬۰۰۰، قیمت ۹۵۰٬۰۰۰، ۵٪) ← true؛ (۱٬۰۰۰٬۰۰۰، ۹۶۰٬۰۰۰، ۵٪) ← false.
    /// قیمت صفر/منفی هرگز (دادهٔ خراب).
    /// </summary>
    public static bool IsDrop(decimal watchPrice, decimal finalPrice, int percent) =>
        watchPrice > 0 && finalPrice > 0 && finalPrice <= watchPrice * (1 - Math.Clamp(percent, 3, 50) / 100m);

    /// <inheritdoc />
    public async Task<FavoriteWatchResult> ProcessAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var options = await smart.GetAsync(cancellationToken);
        if (!options.FavoritePriceAlert)
            return new FavoriteWatchResult(0, 0, 0, false);

        var backfilled = await BackfillAsync(now, cancellationToken);
        if (CustomerQuietHours.IsQuiet(now, options.QuietFromHour, options.QuietToHour))
            return new FavoriteWatchResult(backfilled, 0, 0, true);

        var factor = 1 - Math.Clamp(options.FavoritePriceDropPercent, 3, 50) / 100m;
        var gapStart = now - PerFavoriteGap;
        var dayStart = now.AddDays(-1);

        // کالاهای فعال و موجودی که دست‌کم یک علاقه‌مندیِ آمادهٔ اعلان دارند (محدود به اندازهٔ کاتالوگ).
        var products = await context.Products.AsNoTracking()
            .Where(p => p.IsActive && p.StockQuantity > 0
                        && context.Favorites.Any(f => f.ProductId == p.Id && f.WatchPrice != null && (f.PriceAlertedAt == null || f.PriceAlertedAt < gapStart)))
            .Select(p => new { p.Id, p.Name, p.Slug, p.Price, p.DiscountPrice, p.DiscountStartAt, p.DiscountEndAt })
            .ToListAsync(cancellationToken);

        var sentToday = new Dictionary<Guid, int>();
        int notified = 0, skipped = 0;
        foreach (var product in products)
        {
            if (notified >= MaxPerRun || cancellationToken.IsCancellationRequested)
                break;
            var finalPrice = FratakShopDbContext.FinalPrice(product.Price, product.DiscountPrice, product.DiscountStartAt, product.DiscountEndAt, now);
            if (finalPrice <= 0)
                continue;
            // WatchPrice × (1 − درصد) ≥ قیمت فعلی  ⇔  به اندازهٔ کافی ارزان شده.
            var due = await context.Favorites.AsNoTracking()
                .Where(f => f.ProductId == product.Id && f.WatchPrice != null && f.WatchPrice * factor >= finalPrice
                            && (f.PriceAlertedAt == null || f.PriceAlertedAt < gapStart))
                .Select(f => new { f.Id, f.UserId, WatchPrice = f.WatchPrice!.Value })
                .Take(500)
                .ToListAsync(cancellationToken);
            if (due.Count == 0)
                continue;

            var userIds = due.Select(f => f.UserId).Distinct().ToList();
            // «خبرم کن – کاهش قیمت» فعال ← همان سیستم خبر می‌دهد.
            var watching = (await context.ProductAlerts.AsNoTracking()
                .Where(a => a.ProductId == product.Id && a.IsActive && a.Type == ProductAlertType.PriceDrop && userIds.Contains(a.UserId))
                .Select(a => a.UserId).ToListAsync(cancellationToken)).ToHashSet();
            // اعلان‌های ۲۴ ساعت اخیر هر مشتری (همهٔ کالاها).
            foreach (var row in await context.Favorites.AsNoTracking()
                         .Where(f => userIds.Contains(f.UserId) && f.PriceAlertedAt >= dayStart)
                         .GroupBy(f => f.UserId).Select(g => new { g.Key, Count = g.Count() })
                         .ToListAsync(cancellationToken))
                sentToday.TryAdd(row.Key, row.Count);

            foreach (var favorite in due)
            {
                if (notified >= MaxPerRun)
                    break;
                if (watching.Contains(favorite.UserId))
                {
                    // بی‌صدا: قیمت پایه به‌روز می‌شود تا بعد از اعلان «خبرم کن» همین کاهش دوباره اعلام نشود.
                    await context.Favorites.Where(f => f.Id == favorite.Id)
                        .ExecuteUpdateAsync(s => s.SetProperty(f => f.WatchPrice, finalPrice).SetProperty(f => f.UpdatedAt, now), cancellationToken);
                    skipped++;
                    continue;
                }
                if (sentToday.GetValueOrDefault(favorite.UserId) >= PerUserDaily)
                {
                    // سقف روزانه: بعداً (نوبت‌های بعد) دوباره بررسی می‌شود.
                    skipped++;
                    continue;
                }

                // اول «ادعا» (حتی با دو نمونهٔ برنامه فقط یک‌بار)، بعد اعلان.
                var claimed = await context.Favorites
                    .Where(f => f.Id == favorite.Id && (f.PriceAlertedAt == null || f.PriceAlertedAt < gapStart))
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(f => f.PriceAlertedAt, now)
                        .SetProperty(f => f.WatchPrice, finalPrice)
                        .SetProperty(f => f.UpdatedAt, now), cancellationToken);
                if (claimed == 0)
                    continue;

                await notifications.NotifyAsync(new NotificationRequest
                {
                    UserId = favorite.UserId,
                    Event = NotificationEvent.PriceDrop,
                    Category = NotificationCategory.Promotion,
                    LinkUrl = "/product/" + product.Slug,
                    Values =
                    {
                        ["ProductName"] = product.Name,
                        ["Price"] = finalPrice.ToString("N0"),
                        ["OldPrice"] = favorite.WatchPrice.ToString("N0"),
                        ["Saving"] = Math.Max(0, favorite.WatchPrice - finalPrice).ToString("N0"),
                    },
                });
                sentToday[favorite.UserId] = sentToday.GetValueOrDefault(favorite.UserId) + 1;
                notified++;
            }
        }

        if (notified > 0)
            logger.LogInformation("Favorite price watch: {Count} customers notified.", notified);
        return new FavoriteWatchResult(backfilled, notified, skipped, false);
    }

    /// <summary>علاقه‌مندی‌های بدون قیمت پایه ← قیمت نهایی فعلی کالا (بی‌اعلان). هر نوبت حداکثر ۵۰۰ کالا.</summary>
    private async Task<int> BackfillAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var products = await context.Products.AsNoTracking()
            .Where(p => context.Favorites.Any(f => f.ProductId == p.Id && f.WatchPrice == null))
            .Select(p => new { p.Id, p.Price, p.DiscountPrice, p.DiscountStartAt, p.DiscountEndAt })
            .Take(500)
            .ToListAsync(cancellationToken);
        var total = 0;
        foreach (var product in products)
        {
            var price = FratakShopDbContext.FinalPrice(product.Price, product.DiscountPrice, product.DiscountStartAt, product.DiscountEndAt, now);
            if (price <= 0)
                continue;
            total += await context.Favorites.Where(f => f.ProductId == product.Id && f.WatchPrice == null)
                .ExecuteUpdateAsync(s => s.SetProperty(f => f.WatchPrice, price), cancellationToken);
        }
        return total;
    }
}
