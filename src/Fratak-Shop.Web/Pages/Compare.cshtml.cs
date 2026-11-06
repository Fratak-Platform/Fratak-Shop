using Fratak_Shop.Core.DTOs.Products.Query;
using Fratak_Shop.Core.Services.Products;
using Fratak_Shop.Web.Infrastructure;
using Fratak_Shop.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Pages;

/// <summary>Side-by-side comparison of up to four products (kept in a cookie).</summary>
public class CompareModel(
    IProductService productService,
    ICompareListService compareList,
    Fratak_Shop.Core.Services.Searches.ISearchInsightsService insights) : ShopPageModel
{
    /// <summary>One comparison row: a value per product, whether all values match and the best column (if any).</summary>
    public record CompareRow(string Title, List<string?> Values, bool IsSame, int? BestIndex = null, string? BestLabel = null);

    public List<CompareRow> Rows { get; private set; } = [];

    /// <summary>The compared products belong to different categories (comparison is less meaningful).</summary>
    public bool MixedCategories { get; private set; }

    public List<ProductDto> Products { get; private set; } = [];
    public List<ProductFilterData> Suggestions { get; private set; } = [];

    /// <summary>Union of all feature keys, in order of first appearance.</summary>
    public List<string> FeatureKeys { get; private set; } = [];

    [BindProperty(SupportsGet = true, Name = "q")]
    public string? Query { get; set; }

    public bool CanAddMore => Products.Count < ICompareListService.MaxItems;

    public async Task OnGetAsync()
    {
        Products = await productService.GetProductsByIdsAsync(compareList.GetProductIds());

        FeatureKeys = Products
            .SelectMany(p => p.Features.OrderBy(f => f.DisplayOrder).Select(f => f.Key))
            .Distinct()
            .ToList();

        MixedCategories = Products.Select(p => p.CategoryId).Distinct().Count() > 1;
        BuildRows();

        if (CanAddMore)
            Suggestions = await SuggestAsync();

        ViewData[PageMetaKeys.Title] = "مقایسه محصولات";
        ViewData[PageMetaKeys.Robots] = "noindex, follow";
    }

    /// <summary>
    /// Smart suggestions: a search (with synonyms) when the visitor typed something; otherwise
    /// best sellers of the same category in a similar price range, falling back to the whole category.
    /// </summary>
    private async Task<List<ProductFilterData>> SuggestAsync()
    {
        var selected = Products.Select(p => p.Id).ToHashSet();
        var filter = new ProductFilterParams { OnlyActive = true, SortBy = ProductSortBy.BestSelling, Take = 12 };

        if (!string.IsNullOrWhiteSpace(Query))
        {
            Query = Query.Trim();
            if (Query.Length > 100) Query = Query[..100];
            filter.Search = Query;
            filter.SearchTermGroups = await insights.ExpandAsync(Query);
            filter.SortBy = ProductSortBy.Newest;
            return (await productService.GetProductByFilterAsync(filter)).Data.Where(p => !selected.Contains(p.Id)).Take(8).ToList();
        }

        var main = Products.GroupBy(p => p.CategoryId).OrderByDescending(g => g.Count()).FirstOrDefault();
        if (main is null)
            return (await productService.GetProductByFilterAsync(filter)).Data.Take(8).ToList();

        filter.CategoryId = main.Key;
        var prices = main.Select(p => p.FinalPrice).Where(p => p > 0).ToList();
        var list = new List<ProductFilterData>();
        if (prices.Count > 0)
        {
            filter.MinPrice = Math.Floor(prices.Min() * 0.6m);
            filter.MaxPrice = Math.Ceiling(prices.Max() * 1.5m);
            list = (await productService.GetProductByFilterAsync(filter)).Data.Where(p => !selected.Contains(p.Id)).ToList();
        }

        if (list.Count < 4)
        {
            filter.MinPrice = filter.MaxPrice = null;
            var more = (await productService.GetProductByFilterAsync(filter)).Data;
            list.AddRange(more.Where(p => !selected.Contains(p.Id) && list.All(l => l.Id != p.Id)));
        }

        return list.Take(8).ToList();
    }

    private void BuildRows()
    {
        if (Products.Count == 0)
            return;

        static string Normalize(string? v) => Fratak_Shop.Core.Utilities.SearchText.Normalize(v).ToLowerInvariant();

        CompareRow Row(string title, List<string?> values, int? best = null, string? bestLabel = null)
        {
            var same = Products.Count > 1 && values.All(v => !string.IsNullOrWhiteSpace(v))
                       && values.Select(Normalize).Distinct().Count() == 1;
            return new CompareRow(title, values, same, Products.Count > 1 && !same ? best : null, bestLabel);
        }

        static int? IndexOfBest<T>(List<T> values, Func<T, bool> valid, Func<T, decimal> key, bool lowest)
        {
            int? index = null;
            for (var i = 0; i < values.Count; i++)
            {
                if (!valid(values[i])) continue;
                if (index is null || (lowest ? key(values[i]) < key(values[index.Value]) : key(values[i]) > key(values[index.Value])))
                    index = i;
            }
            return index;
        }

        var inStock = Products.Select(p => p.IsInStock).ToList();
        Rows.Add(Row("قیمت",
            Products.Select(p => (string?)(Helpers.Format.Price(p.FinalPrice) + " تومان")).ToList(),
            IndexOfBest(Products, p => p.IsInStock && p.FinalPrice > 0, p => p.FinalPrice, lowest: true), "ارزان‌ترین"));
        Rows.Add(Row("امتیاز خریداران",
            Products.Select(p => p.ReviewCount > 0 ? $"{p.AverageRating:0.0} از ۵ ({p.ReviewCount} نظر)" : null).ToList(),
            IndexOfBest(Products, p => p.ReviewCount > 0, p => (decimal)p.AverageRating, lowest: false), "محبوب‌ترین"));
        Rows.Add(Row("وضعیت", inStock.Select(s => (string?)(s ? "موجود" : "ناموجود")).ToList()));
        Rows.Add(Row("برند", Products.Select(p => p.BrandName).ToList()));
        Rows.Add(Row("گارانتی", Products.Select(p => p.WarrantyTitle).ToList(),
            IndexOfBest(Products, p => p.WarrantyMonths > 0, p => p.WarrantyMonths ?? 0, lowest: false), "گارانتی بیشتر"));

        foreach (var key in FeatureKeys)
            Rows.Add(Row(key, Products.Select(p => FeatureValue(p, key)).ToList()));
    }

    public string? FeatureValue(ProductDto product, string key) =>
        product.Features.FirstOrDefault(f => f.Key == key)?.Value;

    public IActionResult OnPostAdd(Guid productId)
    {
        if (compareList.Add(productId))
            SuccessMessage = "محصول به لیست مقایسه اضافه شد.";
        else
            ErrorMessage = $"حداکثر {ICompareListService.MaxItems} محصول را می توانید مقایسه کنید.";

        return RedirectToPage();
    }

    public IActionResult OnPostRemove(Guid productId)
    {
        compareList.Remove(productId);
        return RedirectToPage();
    }

    public IActionResult OnPostClear()
    {
        compareList.Clear();
        return RedirectToPage();
    }
}
