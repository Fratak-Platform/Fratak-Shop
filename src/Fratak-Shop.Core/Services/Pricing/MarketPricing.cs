using Fratak_Shop.Core.Services.Settings;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Products;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Core.Services.Pricing;

/// <summary>یک ردیف از گزارش قیمت‌گذاری بازار (برای صفحهٔ «درون‌ریزی از سایت»).</summary>
public sealed record MarketPriceCheckRow(
    Guid ProductId, string ProductName, MarketPriceCheckStatus Status, int Attempts,
    decimal InitialPrice, decimal? SuggestedPrice, decimal? LowestPrice, int OfferCount,
    string? Note, DateTimeOffset CreatedAt, DateTimeOffset? CompletedAt, DateTimeOffset NextAttemptAt);

/// <summary>خلاصهٔ صف قیمت‌گذاری بازار.</summary>
public sealed record MarketPricingSummary(int Pending, int Applied, int Kept, int Skipped, IReadOnlyList<MarketPriceCheckRow> Recent);

/// <summary>
/// قیمت‌گذاری خودکار محصولات درون‌ریزی‌شده بر اساس قیمت بازار (مرحلهٔ سیزدهم).
/// <para>روند کامل:</para>
/// <list type="number">
/// <item>در «درون‌ریزی از سایت» با گزینهٔ «بر اساس قیمت بازار»، محصول با قیمت اولیهٔ «سود هدف» ساخته می‌شود (تا هیچ‌وقت بی‌قیمت نماند)
/// و <see cref="EnqueueAsync"/> یک ردیف در صف می‌گذارد.</item>
/// <item>کار پس‌زمینهٔ MarketPricingWorker (لایهٔ Web) هر چند ثانیه <see cref="ProcessNextAsync"/> را صدا می‌زند؛
/// این متد قیمت همان کالا را از ترب/دیجی‌کالا/باسلام می‌گیرد و با <see cref="PriceAdvisor.Suggest"/> (همان دکمهٔ «پیشنهاد قیمت فروش»)
/// قیمت فروش را تنظیم می‌کند: کمی ارزان‌تر از ارزان‌ترین رقیب معتبر، ولی هرگز کمتر از «کمترین سود» تنظیمات.</item>
/// <item>اگر منابع محدود بودند، ردیف با فاصلهٔ بیشتر دوباره امتحان می‌شود (۱۵ دقیقه، ۳۰، ۶۰، …؛ حداکثر ۶ بار).
/// اگر قیمت بازاری نبود، قیمت اولیه می‌ماند (Kept).</item>
/// <item>ایمنی: اگر کسی قیمت محصول را بعد از درون‌ریزی دستی عوض کرده باشد، به آن دست نمی‌زنیم (Skipped).</item>
/// </list>
/// <para>مثال: خرید ۲۰۰ هزار، قیمت اولیه ۲۶۰ هزار، رقبا ۲۸۵/۲۹۰/۳۱۰ هزار، ارزان‌تر ۲٪ ← قیمت فروش ۲۷۹ هزار.</para>
/// </summary>
public interface IMarketPricingService
{
    /// <summary>
    /// گذاشتن یک محصول در صف (و ذخیره). اگر برای این محصول ردیف «در صف» وجود داشته باشد، ردیف تازه ساخته نمی‌شود.
    /// </summary>
    /// <param name="productId">محصول ساخته‌شده.</param>
    /// <param name="query">عبارت جست‌وجو (نام محصول).</param>
    /// <param name="initialPrice">قیمت فروش اولیه که هنگام ساخت ثبت شد.</param>
    /// <param name="purchasePrice">قیمت خرید (قیمت سایت مبدأ).</param>
    Task EnqueueAsync(Guid productId, string query, decimal initialPrice, decimal? purchasePrice, CancellationToken cancellationToken = default);

    /// <summary>
    /// پردازش یک ردیف سررسیده. خروجی true یعنی ردیفی پردازش شد (پس احتمالاً ردیف بعدی هم هست).
    /// اگر ترب و دیجی‌کالا هر دو در حال استراحت باشند، چیزی پردازش نمی‌شود تا تلاش‌ها بیهوده مصرف نشوند.
    /// </summary>
    Task<bool> ProcessNextAsync(CancellationToken cancellationToken = default);

    /// <summary>شمار هر وضعیت و آخرین ردیف‌ها.</summary>
    Task<MarketPricingSummary> SummaryAsync(int take = 20, CancellationToken cancellationToken = default);
}

/// <summary>پیاده‌سازی <see cref="IMarketPricingService"/>.</summary>
public sealed class MarketPricingService(
    FratakShopDbContext context,
    ICompetitorPriceService competitors,
    IStoreSettingService settings,
    IExternalSourceGate gate,
    IProductCompetitorService? snapshots = null) : IMarketPricingService
{
    /// <summary>بیشترین تعداد تلاش برای یک محصول وقتی منابع محدودند.</summary>
    public const int MaxAttempts = 6;

    public async Task EnqueueAsync(Guid productId, string query, decimal initialPrice, decimal? purchasePrice, CancellationToken cancellationToken = default)
    {
        query = (query ?? "").Trim();
        if (query.Length > 200)
            query = query[..200];
        if (query.Length < 2)
            return;
        if (await context.MarketPriceChecks.AnyAsync(c => c.ProductId == productId && c.Status == MarketPriceCheckStatus.Pending, cancellationToken))
            return;

        context.MarketPriceChecks.Add(new MarketPriceCheck
        {
            ProductId = productId,
            Query = query,
            Status = MarketPriceCheckStatus.Pending,
            NextAttemptAt = DateTimeOffset.UtcNow,
            InitialPrice = initialPrice,
            PurchasePrice = purchasePrice,
        });
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> ProcessNextAsync(CancellationToken cancellationToken = default)
    {
        if (gate.CoolingUntil(CompetitorPriceService.Torob) is not null && gate.CoolingUntil(CompetitorPriceService.DigikalaSource) is not null)
            return false;

        var now = DateTimeOffset.UtcNow;
        var job = await context.MarketPriceChecks.AsTracking()
            .Where(c => c.Status == MarketPriceCheckStatus.Pending && c.NextAttemptAt <= now)
            .OrderBy(c => c.NextAttemptAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (job is null)
            return false;

        job.Attempts++;
        var product = await context.Products.AsTracking().FirstOrDefaultAsync(p => p.Id == job.ProductId, cancellationToken);
        if (product is null)
        {
            Complete(job, MarketPriceCheckStatus.Skipped, "محصول حذف شده است.");
        }
        else if (product.Price != job.InitialPrice)
        {
            Complete(job, MarketPriceCheckStatus.Skipped, "قیمت محصول بعد از درون‌ریزی دستی تغییر کرده است؛ به آن دست زده نشد.");
        }
        else if (await settings.GetAsync(cancellationToken) is { IsCompetitorPriceLookupEnabled: false })
        {
            Complete(job, MarketPriceCheckStatus.Kept, "جست‌وجوی قیمت رقبا در تنظیمات («پیشنهاد قیمت فروش») خاموش است؛ قیمت با سود هدف ماند.");
        }
        else
        {
            var lookup = await competitors.SearchAsync(job.Query, cancellationToken);
            var store = await settings.GetAsync(cancellationToken);
            var costs = PricingCosts.From(store);
            var purchase = product.PurchasePrice ?? job.PurchasePrice ?? 0;
            // مرحلهٔ پانزدهم: داوری با نام کالا (عنوان نامشابه/لوازم جانبی کنار می‌رود) و بدون قیمت فروشگاه خودمان.
            var ownShops = ProductCompetitorService.OwnShops(store);
            var decision = Decide(purchase, costs, lookup, job.Attempts, new OfferReviewOptions(job.Query, OwnShops: ownShops));
            if (snapshots is not null)
                await snapshots.SaveAsync(product.Id, job.Query, lookup,
                    decision.Status == MarketPriceCheckStatus.Applied ? decision.Suggestion.Suggested : product.Price, ownShops, cancellationToken);

            job.LowestPrice = decision.Suggestion.Lowest;
            job.MedianPrice = decision.Suggestion.Median;
            job.OfferCount = decision.Suggestion.Offers.Count;
            job.SuggestedPrice = decision.Suggestion.Suggested;
            job.MatchedTitle = Clip(lookup.MatchedTitle, 200);

            switch (decision.Status)
            {
                case MarketPriceCheckStatus.Applied:
                    product.Price = decision.Suggestion.Suggested;
                    if (product.DiscountPrice is { } discount && discount >= product.Price)
                        product.DiscountPrice = null;
                    Complete(job, MarketPriceCheckStatus.Applied, decision.Note);
                    break;
                case MarketPriceCheckStatus.Pending:
                    job.NextAttemptAt = now + RetryDelay(job.Attempts);
                    job.Note = Clip(decision.Note, 1000);
                    break;
                default:
                    Complete(job, decision.Status, decision.Note);
                    break;
            }
        }

        await context.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>
    /// تصمیم خالص (بدون دیتابیس؛ تست‌پذیر):
    /// <list type="bullet">
    /// <item>قیمت معتبر رقبا هست ← Applied با قیمت پیشنهادی PriceAdvisor.</item>
    /// <item>نیست و منابع محدود بودند و هنوز تلاش باقی است ← Pending (تلاش دوباره).</item>
    /// <item>بقیه ← Kept (قیمت اولیهٔ سود هدف می‌ماند).</item>
    /// </list>
    /// </summary>
    public static (MarketPriceCheckStatus Status, PriceSuggestion Suggestion, string Note) Decide(decimal purchase, PricingCosts costs, CompetitorLookup lookup, int attempts,
        OfferReviewOptions? review = null)
    {
        var suggestion = PriceAdvisor.Suggest(purchase, 0, costs, lookup.Offers, review);
        if (suggestion.Offers.Count > 0)
        {
            var sources = string.Join("، ", suggestion.Offers.Select(o => o.Source).Distinct());
            var cached = lookup.CachedAt is { } at ? $" (قیمت‌های ذخیره‌شدهٔ {Math.Max(1, (int)Math.Round((DateTimeOffset.UtcNow - at).TotalHours))} ساعت پیش)" : "";
            return (MarketPriceCheckStatus.Applied, suggestion,
                $"{suggestion.Reason} بر اساس {suggestion.Offers.Count} قیمت از {sources}{cached}.");
        }

        var problems = lookup.Problems.Count > 0 ? " " + string.Join(" | ", lookup.Problems) : "";
        if (lookup.Limited && attempts < MaxAttempts)
            return (MarketPriceCheckStatus.Pending, suggestion, $"منابع قیمت موقتاً محدود بودند؛ تلاش {attempts} از {MaxAttempts}.{problems}");
        return (MarketPriceCheckStatus.Kept, suggestion, $"قیمت بازار پیدا نشد؛ قیمت با سود هدف ماند.{problems}");
    }

    /// <summary>فاصلهٔ تلاش دوباره: ۱۵ دقیقه × ۲^(تلاش−۱)، حداکثر ۴ ساعت. مثال: تلاش ۱ ← ۱۵ دقیقه، ۳ ← ۶۰ دقیقه.</summary>
    public static TimeSpan RetryDelay(int attempts)
        => TimeSpan.FromMinutes(Math.Min(240, 15 * Math.Pow(2, Math.Clamp(attempts, 1, 10) - 1)));

    public async Task<MarketPricingSummary> SummaryAsync(int take = 20, CancellationToken cancellationToken = default)
    {
        var counts = await context.MarketPriceChecks.AsNoTracking()
            .GroupBy(c => c.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);
        int Count(MarketPriceCheckStatus status) => counts.FirstOrDefault(c => c.Status == status)?.Count ?? 0;

        var recent = await context.MarketPriceChecks.AsNoTracking()
            .OrderByDescending(c => c.UpdatedAt)
            .Take(Math.Clamp(take, 1, 100))
            .Select(c => new MarketPriceCheckRow(
                c.ProductId,
                context.Products.IgnoreQueryFilters().Where(p => p.Id == c.ProductId).Select(p => p.Name).FirstOrDefault() ?? "—",
                c.Status, c.Attempts, c.InitialPrice, c.SuggestedPrice, c.LowestPrice, c.OfferCount,
                c.Note, c.CreatedAt, c.CompletedAt, c.NextAttemptAt))
            .ToListAsync(cancellationToken);

        return new MarketPricingSummary(
            Count(MarketPriceCheckStatus.Pending), Count(MarketPriceCheckStatus.Applied),
            Count(MarketPriceCheckStatus.Kept), Count(MarketPriceCheckStatus.Skipped), recent);
    }

    private static void Complete(MarketPriceCheck job, MarketPriceCheckStatus status, string note)
    {
        job.Status = status;
        job.CompletedAt = DateTimeOffset.UtcNow;
        job.Note = Clip(note, 1000);
    }

    private static string? Clip(string? text, int max)
        => text is null ? null : text.Length <= max ? text : text[..max];
}
