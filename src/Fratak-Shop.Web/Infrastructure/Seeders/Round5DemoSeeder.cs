using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Products;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Web.Infrastructure.Seeders;

/// <summary>
/// Demo-only extras of round 5 (runs with <c>Shop:SeedDemoData</c>, also on already seeded
/// demo databases): a 90-day price history so the chart has something to show, and one sample
/// product video (direct link). Both are ordinary rows that can be deleted.
/// </summary>
public static class Round5DemoSeeder
{
    private const string SampleVideo = "https://interactive-examples.mdn.mozilla.net/media/cc0-videos/flower.mp4";

    public static async Task SeedAsync(FratakShopDbContext context, ILogger logger, CancellationToken cancellationToken = default)
    {
        await SeedPriceHistoryAsync(context, logger, cancellationToken);
        await SeedVideoAsync(context, cancellationToken);
    }

    private static async Task SeedPriceHistoryAsync(FratakShopDbContext context, ILogger logger, CancellationToken cancellationToken)
    {
        var old = DateTimeOffset.UtcNow.AddDays(-3);
        if (await context.ProductPriceHistories.AnyAsync(h => h.CreatedAt < old, cancellationToken))
            return;

        var products = await context.Products
            .Select(p => new { p.Id, p.Price, p.DiscountPrice, p.StockQuantity })
            .ToListAsync(cancellationToken);
        if (products.Count == 0)
            return;

        // Five earlier points per product, dated 90 … 6 days ago, ending near today's price.
        int[] daysAgo = [90, 66, 41, 20, 6];
        var rows = new List<(ProductPriceHistory Row, int DaysAgo)>();

        foreach (var product in products)
        {
            var random = new Random(product.Id.GetHashCode());
            var current = product.DiscountPrice is { } sale && sale < product.Price ? sale : product.Price;

            for (var i = 0; i < daysAgo.Length; i++)
            {
                // Older prices were up to ~12% higher (inflation-like drift), with an occasional sale.
                var drift = 1m + (decimal)(random.NextDouble() * 0.12) * (daysAgo.Length - i) / daysAgo.Length;
                var list = Round(product.Price * drift);
                var final = random.Next(3) == 0 ? Round(list * 0.9m) : list;
                if (i == daysAgo.Length - 1)
                    final = Math.Max(final, current);

                rows.Add((new ProductPriceHistory
                {
                    ProductId = product.Id,
                    Price = list,
                    FinalPrice = Math.Min(final, list),
                    IsInStock = product.StockQuantity > 0 || i < daysAgo.Length - 1,
                }, daysAgo[i]));
            }
        }

        context.ProductPriceHistories.AddRange(rows.Select(r => r.Row));
        await context.SaveChangesAsync(cancellationToken);

        // CreatedAt is set by the audit hook on insert; move the rows back in time afterwards.
        var now = DateTimeOffset.UtcNow;
        foreach (var (row, ago) in rows)
            row.CreatedAt = now.AddDays(-ago).AddHours(-(row.ProductId.GetHashCode() & 15));
        await context.SaveChangesAsync(cancellationToken);
        context.ChangeTracker.Clear();

        logger.LogInformation("Demo price history added for {Count} products.", products.Count);
    }

    private static async Task SeedVideoAsync(FratakShopDbContext context, CancellationToken cancellationToken)
    {
        if (await context.ProductVideos.IgnoreQueryFilters().AnyAsync(cancellationToken))
            return;

        var productId = await context.Products
            .Where(p => p.Slug == "roborock-s8")
            .Select(p => (Guid?)p.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (productId is null)
            return;

        context.ProductVideos.Add(new ProductVideo
        {
            ProductId = productId.Value,
            Provider = ProductVideoProvider.DirectUrl,
            Source = SampleVideo,
            Title = "ویدیوی نمونه (قابل حذف از پنل مدیریت)",
            DurationSeconds = 5,
            DisplayOrder = 1,
        });
        await context.SaveChangesAsync(cancellationToken);
    }

    private static decimal Round(decimal value) => Math.Round(value / 10_000m, MidpointRounding.AwayFromZero) * 10_000m;
}
