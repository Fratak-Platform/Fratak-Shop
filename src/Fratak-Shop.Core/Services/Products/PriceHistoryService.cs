using Fratak_Shop.Core.DTOs.Engagement;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Products;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Core.Services.Products;

/// <summary>
/// Price history of products. Changes made through EF are recorded by the DbContext itself;
/// the daily snapshot adds the changes nobody "saved" – a discount that started or ended on its
/// date, stock that ran out through orders (bulk updates) – so the chart is always truthful.
/// </summary>
public interface IPriceHistoryService
{
    Task<PriceHistoryDto> GetChartAsync(Guid productId, int days, CancellationToken cancellationToken = default);

    /// <summary>Background job: records the current state of products whose last record differs. Returns the number of new points.</summary>
    Task<int> SnapshotAsync(CancellationToken cancellationToken = default);

    /// <summary>Removes points older than the given number of days (keeps the last one before the window).</summary>
    Task<int> PruneAsync(int keepDays, CancellationToken cancellationToken = default);
}

public class PriceHistoryService(FratakShopDbContext context) : IPriceHistoryService
{
    public async Task<PriceHistoryDto> GetChartAsync(Guid productId, int days, CancellationToken cancellationToken = default)
    {
        days = Math.Clamp(days, 7, 730);
        var now = DateTimeOffset.UtcNow;
        var from = now.AddDays(-days);

        var product = await context.Products
            .Where(p => p.Id == productId)
            .Select(p => new { p.Price, p.DiscountPrice, p.DiscountStartAt, p.DiscountEndAt, p.StockQuantity })
            .FirstOrDefaultAsync(cancellationToken);
        if (product is null)
            return new PriceHistoryDto { Days = days };

        var inWindow = await context.ProductPriceHistories
            .Where(h => h.ProductId == productId && h.CreatedAt >= from)
            .OrderBy(h => h.CreatedAt)
            .Select(h => new PricePoint(h.CreatedAt, h.FinalPrice, h.Price, h.IsInStock))
            .Take(1000)
            .ToListAsync(cancellationToken);

        // The value that was valid when the window started.
        var before = await context.ProductPriceHistories
            .Where(h => h.ProductId == productId && h.CreatedAt < from)
            .OrderByDescending(h => h.CreatedAt)
            .Select(h => new PricePoint(from, h.FinalPrice, h.Price, h.IsInStock))
            .FirstOrDefaultAsync(cancellationToken);

        var points = new List<PricePoint>(inWindow.Count + 2);
        if (before is not null)
            points.Add(before);
        points.AddRange(inWindow);

        var current = FratakShopDbContext.FinalPrice(product.Price, product.DiscountPrice, product.DiscountStartAt, product.DiscountEndAt, now);
        if (points.Count > 0)
            points.Add(new PricePoint(now, current, product.Price, product.StockQuantity > 0));

        var prices = points.Where(p => p.IsInStock).Select(p => p.FinalPrice).DefaultIfEmpty(current).ToList();
        return new PriceHistoryDto
        {
            Points = points,
            Current = current,
            Lowest = prices.Min(),
            Highest = prices.Max(),
            Days = days,
        };
    }

    public async Task<int> SnapshotAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;

        var products = await context.Products
            .Where(p => p.IsActive)
            .Select(p => new
            {
                p.Id, p.Price, p.DiscountPrice, p.DiscountStartAt, p.DiscountEndAt, p.StockQuantity,
                Last = context.ProductPriceHistories
                    .Where(h => h.ProductId == p.Id)
                    .OrderByDescending(h => h.CreatedAt)
                    .Select(h => new { h.Price, h.FinalPrice, h.IsInStock })
                    .FirstOrDefault(),
            })
            .ToListAsync(cancellationToken);

        var added = 0;
        foreach (var product in products)
        {
            var finalPrice = FratakShopDbContext.FinalPrice(product.Price, product.DiscountPrice, product.DiscountStartAt, product.DiscountEndAt, now);
            var inStock = product.StockQuantity > 0;

            if (product.Last is not null && product.Last.Price == product.Price && product.Last.FinalPrice == finalPrice && product.Last.IsInStock == inStock)
                continue;

            context.ProductPriceHistories.Add(new ProductPriceHistory
            {
                ProductId = product.Id,
                Price = product.Price,
                FinalPrice = finalPrice,
                IsInStock = inStock,
            });
            added++;
        }

        if (added > 0)
            await context.SaveChangesAsync(cancellationToken);

        return added;
    }

    public async Task<int> PruneAsync(int keepDays, CancellationToken cancellationToken = default)
    {
        var cutoff = DateTimeOffset.UtcNow.AddDays(-Math.Max(30, keepDays));

        // Keep the newest record before the cut-off of every product: it is the chart's starting value.
        return await context.ProductPriceHistories
            .IgnoreQueryFilters()
            .Where(h => h.CreatedAt < cutoff
                        && context.ProductPriceHistories.IgnoreQueryFilters()
                            .Any(newer => newer.ProductId == h.ProductId && newer.CreatedAt < cutoff && newer.CreatedAt > h.CreatedAt))
            .ExecuteDeleteAsync(cancellationToken);
    }
}
