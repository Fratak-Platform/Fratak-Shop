using Fratak_Shop.Core.DTOs.Search;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Fratak_Shop.Core.Services.Products;

/// <summary>
/// Instant search suggestions (products, categories, brands) for the header search box.
/// Uses the same word matching as the search page; results are cached briefly per query
/// so typing doesn't hit the database for every key stroke of every visitor.
/// </summary>
public interface ISearchSuggestService
{
    public const int MinLength = 2;

    Task<SearchSuggestionsDto> SuggestAsync(string? query, CancellationToken cancellationToken = default);
}

public class SearchSuggestService(FratakShopDbContext context, IMemoryCache cache, Searches.ISearchInsightsService insights) : ISearchSuggestService
{
    private const int ProductCount = 6;
    private const int LinkCount = 4;
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromSeconds(60);

    public async Task<SearchSuggestionsDto> SuggestAsync(string? query, CancellationToken cancellationToken = default)
    {
        var normalized = SearchText.Normalize(query);
        if (normalized.Length > 50)
            normalized = normalized[..50].Trim();

        if (normalized.Length < ISearchSuggestService.MinLength)
            return new SearchSuggestionsDto { Query = normalized };

        // Version-keyed: a product/category/brand change makes every cached suggestion obsolete at once.
        var key = $"core:search-suggest:{CacheInvalidation.CatalogVersion}:{normalized.ToLowerInvariant()}";
        return await cache.GetOrCreateAsync(key, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheLifetime;
            return await LoadAsync(normalized, cancellationToken);
        }) ?? new SearchSuggestionsDto { Query = normalized };
    }

    private async Task<SearchSuggestionsDto> LoadAsync(string normalized, CancellationToken cancellationToken)
    {
        var terms = SearchText.Terms(normalized);
        var first = terms[0];
        var now = DateTimeOffset.UtcNow;

        // Same matching as the search page, synonyms included.
        var groups = await insights.ExpandAsync(normalized);
        var products = ProductSearch.Apply(context.Products.Where(p => p.IsActive && p.Category.IsActive), groups);

        var total = await products.CountAsync(cancellationToken);
        var rows = await products
            .OrderByDescending(p => p.Name.StartsWith(first) || (p.EnglishName != null && p.EnglishName.StartsWith(first)))
            .ThenByDescending(p => p.StockQuantity > 0)
            .ThenByDescending(p => p.SalesCount)
            .ThenByDescending(p => p.ViewCount)
            .Take(ProductCount)
            .Select(p => new
            {
                p.Name,
                p.Slug,
                Image = p.Images.OrderByDescending(i => i.IsPrimary).ThenBy(i => i.DisplayOrder).Select(i => i.ImageName).FirstOrDefault(),
                p.Price,
                p.DiscountPrice,
                p.DiscountStartAt,
                p.DiscountEndAt,
                p.StockQuantity,
            })
            .ToListAsync(cancellationToken);

        var categories = context.Categories.Where(c => c.IsActive);
        var brands = context.Brands.Where(b => b.IsActive);
        foreach (var term in terms)
        {
            categories = categories.Where(c => c.Name.Contains(term) || c.Slug.Contains(term));
            brands = brands.Where(b => b.Name.Contains(term) || (b.EnglishName != null && b.EnglishName.Contains(term)) || b.Slug.Contains(term));
        }

        var categoryRows = await categories
            .OrderByDescending(c => c.Name.StartsWith(first))
            .ThenBy(c => c.Name)
            .Take(LinkCount)
            .Select(c => new LinkSuggestion(c.Name, c.Slug))
            .ToListAsync(cancellationToken);

        var brandRows = await brands
            .OrderByDescending(b => b.Name.StartsWith(first) || (b.EnglishName != null && b.EnglishName.StartsWith(first)))
            .ThenBy(b => b.Name)
            .Take(LinkCount)
            .Select(b => new LinkSuggestion(b.Name, b.Slug))
            .ToListAsync(cancellationToken);

        return new SearchSuggestionsDto
        {
            Query = normalized,
            TotalProducts = total,
            Categories = categoryRows,
            Brands = brandRows,
            Products = rows.Select(p =>
            {
                var discounted = p.DiscountPrice.HasValue && p.DiscountPrice < p.Price
                                 && (!p.DiscountStartAt.HasValue || p.DiscountStartAt <= now)
                                 && (!p.DiscountEndAt.HasValue || p.DiscountEndAt >= now);
                return new ProductSuggestion(p.Name, p.Slug, p.Image, p.Price, discounted ? p.DiscountPrice!.Value : p.Price, p.StockQuantity > 0);
            }).ToList(),
        };
    }
}
