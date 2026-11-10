using Fratak_Shop.Core.DTOs.Products.Query;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Orders;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Core.Services.Products;

/// <summary>
/// Product recommendations from the store's own data (no external service):
/// "frequently bought together" from co-purchases, personal suggestions from the
/// customer's purchases, favourites and recently viewed products.
/// </summary>
public interface IRecommendationService
{
    /// <summary>Products that were bought in the same orders (fallback: cheaper best sellers of other categories).</summary>
    Task<List<ProductFilterData>> GetBoughtTogetherAsync(Guid productId, int take = 4);

    /// <summary>Suggestions for the cart: bought together with the cart items.</summary>
    Task<List<ProductFilterData>> GetForCartAsync(IReadOnlyCollection<Guid> cartProductIds, Guid? userId, int take = 8);

    /// <summary>Personal suggestions (purchases ×3, favourites ×2, viewed ×1); best sellers when nothing is known.</summary>
    Task<List<ProductFilterData>> GetPersonalizedAsync(Guid? userId, IReadOnlyCollection<Guid> recentlyViewed,
        IReadOnlyCollection<Guid> exclude, int take = 8);
}

public class RecommendationService(FratakShopDbContext context, IProductService productService) : IRecommendationService
{
    public async Task<List<ProductFilterData>> GetBoughtTogetherAsync(Guid productId, int take = 4)
    {
        take = Math.Clamp(take, 1, 12);
        var ids = await CoPurchasedAsync([productId], take * 2);

        if (ids.Count < take)
        {
            var product = await context.Products
                .Where(p => p.Id == productId)
                .Select(p => new { p.CategoryId, p.Price })
                .FirstOrDefaultAsync();

            if (product is not null)
            {
                // Accessory heuristic: popular, cheaper products of other categories.
                var fallback = await context.Products
                    .Where(p => p.IsActive && p.StockQuantity > 0 && p.Id != productId && !ids.Contains(p.Id)
                                && p.CategoryId != product.CategoryId && p.Price < product.Price)
                    .OrderByDescending(p => p.SalesCount)
                    .ThenByDescending(p => p.ViewCount)
                    .Select(p => p.Id)
                    .Take(take * 2)
                    .ToListAsync();
                ids.AddRange(fallback);
            }
        }

        return (await productService.GetCardsByIdsAsync(ids, onlyInStock: true)).Take(take).ToList();
    }

    public async Task<List<ProductFilterData>> GetForCartAsync(IReadOnlyCollection<Guid> cartProductIds, Guid? userId, int take = 8)
    {
        take = Math.Clamp(take, 1, 12);
        if (cartProductIds.Count == 0)
            return await GetPersonalizedAsync(userId, [], [], take);

        var ids = await CoPurchasedAsync(cartProductIds.Take(10).ToList(), take * 2);
        var cards = await productService.GetCardsByIdsAsync(ids, onlyInStock: true);

        if (cards.Count < take)
        {
            var more = await GetPersonalizedAsync(userId, [], cartProductIds.Concat(cards.Select(c => c.Id)).ToList(), take - cards.Count);
            cards.AddRange(more);
        }

        return cards.Take(take).ToList();
    }

    public async Task<List<ProductFilterData>> GetPersonalizedAsync(Guid? userId, IReadOnlyCollection<Guid> recentlyViewed,
        IReadOnlyCollection<Guid> exclude, int take = 8)
    {
        take = Math.Clamp(take, 1, 24);
        var categoryScore = new Dictionary<Guid, double>();
        var brandScore = new Dictionary<Guid, double>();
        var excluded = exclude.ToHashSet();

        void Score(IEnumerable<(Guid CategoryId, Guid? BrandId)> items, double weight)
        {
            foreach (var (categoryId, brandId) in items)
            {
                categoryScore[categoryId] = categoryScore.GetValueOrDefault(categoryId) + weight;
                if (brandId.HasValue)
                    brandScore[brandId.Value] = brandScore.GetValueOrDefault(brandId.Value) + weight * 0.5;
            }
        }

        if (userId.HasValue)
        {
            var purchased = await context.OrderItems
                .Where(i => i.Order.UserId == userId && i.ProductId != null && i.Order.Status != OrderStatus.Cancelled)
                .OrderByDescending(i => i.CreatedAt)
                .Select(i => new { ProductId = i.ProductId!.Value })
                .Take(50)
                .ToListAsync();

            var purchasedIds = purchased.Select(p => p.ProductId).Distinct().ToList();
            excluded.UnionWith(purchasedIds); // don't suggest what was just bought

            var purchasedInfo = await context.Products.IgnoreQueryFilters()
                .Where(p => purchasedIds.Contains(p.Id))
                .Select(p => new { p.CategoryId, p.BrandId })
                .ToListAsync();
            Score(purchasedInfo.Select(p => (p.CategoryId, p.BrandId)), 3);

            var favorites = await context.Favorites
                .Where(f => f.UserId == userId)
                .OrderByDescending(f => f.CreatedAt)
                .Select(f => new { f.ProductId, f.Product.CategoryId, f.Product.BrandId })
                .Take(50)
                .ToListAsync();
            Score(favorites.Select(f => (f.CategoryId, f.BrandId)), 2);
        }

        var viewedIds = recentlyViewed.Take(20).ToList();
        if (viewedIds.Count > 0)
        {
            var viewed = await context.Products
                .Where(p => viewedIds.Contains(p.Id))
                .Select(p => new { p.CategoryId, p.BrandId })
                .ToListAsync();
            Score(viewed.Select(v => (v.CategoryId, v.BrandId)), 1);
        }

        List<Guid> ids;
        var excludedIds = excluded.ToList();
        if (categoryScore.Count == 0)
        {
            ids = await context.Products
                .Where(p => p.IsActive && p.StockQuantity > 0 && !excludedIds.Contains(p.Id))
                .OrderByDescending(p => p.SalesCount)
                .ThenByDescending(p => p.ViewCount)
                .Select(p => p.Id)
                .Take(take)
                .ToListAsync();
        }
        else
        {
            var topCategories = categoryScore.OrderByDescending(x => x.Value).Take(6).Select(x => x.Key).ToList();
            var topBrands = brandScore.OrderByDescending(x => x.Value).Take(6).Select(x => x.Key).ToList();
            var excludedList = excludedIds;

            var candidates = await context.Products
                .Where(p => p.IsActive && p.StockQuantity > 0 && !excludedList.Contains(p.Id)
                            && (topCategories.Contains(p.CategoryId) || (p.BrandId != null && topBrands.Contains(p.BrandId.Value))))
                .Select(p => new { p.Id, p.CategoryId, p.BrandId, p.SalesCount, p.ViewCount })
                .Take(200)
                .ToListAsync();

            ids = candidates
                .OrderByDescending(c => categoryScore.GetValueOrDefault(c.CategoryId)
                                        + (c.BrandId.HasValue ? brandScore.GetValueOrDefault(c.BrandId.Value) : 0)
                                        + Math.Log10(1 + c.SalesCount) * 0.5
                                        + Math.Log10(1 + c.ViewCount) * 0.1)
                .Select(c => c.Id)
                .Take(take)
                .ToList();

            if (ids.Count < take)
            {
                var fill = await context.Products
                    .Where(p => p.IsActive && p.StockQuantity > 0 && !excludedList.Contains(p.Id) && !ids.Contains(p.Id))
                    .OrderByDescending(p => p.SalesCount)
                    .Select(p => p.Id)
                    .Take(take - ids.Count)
                    .ToListAsync();
                ids.AddRange(fill);
            }
        }

        return await productService.GetCardsByIdsAsync(ids, onlyInStock: true);
    }

    /// <summary>
    /// کالاهای سفارش‌های مشترک (لغو/مرجوع‌نشده). دور ۲۴ (پیشنهاد ۵۲): ۵۰ نامزد پرتکرار با شباهت کسینوسی آیتم‌به‌آیتم
    /// (<see cref="Learning.ItemSimilarity"/>) دوباره مرتب می‌شوند تا کالای واقعاً مرتبط بالاتر از «پرفروش عمومی» بیاید.
    /// </summary>
    private async Task<List<Guid>> CoPurchasedAsync(List<Guid> productIds, int take)
    {
        var validItems = context.OrderItems.Where(i => i.ProductId != null
                                                       && i.Order.Status != OrderStatus.Cancelled && i.Order.Status != OrderStatus.Returned);
        var orderIds = validItems.Where(i => productIds.Contains(i.ProductId!.Value)).Select(i => i.OrderId);

        var candidates = await validItems
            .Where(i => orderIds.Contains(i.OrderId) && !productIds.Contains(i.ProductId!.Value))
            .GroupBy(i => i.ProductId!.Value)
            .Select(g => new { ProductId = g.Key, Count = g.Select(i => i.OrderId).Distinct().Count() })
            .OrderByDescending(x => x.Count)
            .Take(50)
            .ToListAsync();
        if (candidates.Count <= 1)
            return candidates.Select(c => c.ProductId).Take(take).ToList();

        var candidateIds = candidates.Select(c => c.ProductId).ToList();
        var totals = await validItems
            .Where(i => candidateIds.Contains(i.ProductId!.Value))
            .GroupBy(i => i.ProductId!.Value)
            .Select(g => new { ProductId = g.Key, Orders = g.Select(i => i.OrderId).Distinct().Count() })
            .ToDictionaryAsync(x => x.ProductId, x => x.Orders);
        var baseOrders = await orderIds.Distinct().CountAsync();
        return Learning.ItemSimilarity.Rank(candidates.ToDictionary(c => c.ProductId, c => c.Count), totals, baseOrders, take);
    }
}
