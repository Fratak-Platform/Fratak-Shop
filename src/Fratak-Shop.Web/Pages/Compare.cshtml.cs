using Fratak_Shop.Core.DTOs.Products.Query;
using Fratak_Shop.Core.Services.Products;
using Fratak_Shop.Web.Infrastructure;
using Fratak_Shop.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Pages;

/// <summary>Side-by-side comparison of up to four products (kept in a cookie).</summary>
public class CompareModel(IProductService productService, ICompareListService compareList) : ShopPageModel
{
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

        if (CanAddMore)
        {
            var result = await productService.GetProductByFilterAsync(new ProductFilterParams
            {
                Search = Query,
                CategoryId = string.IsNullOrWhiteSpace(Query) ? Products.FirstOrDefault()?.CategoryId : null,
                OnlyActive = true,
                SortBy = ProductSortBy.BestSelling,
                Take = 8,
            });

            var selected = Products.Select(p => p.Id).ToHashSet();
            Suggestions = result.Data.Where(p => !selected.Contains(p.Id)).ToList();
        }

        ViewData[PageMetaKeys.Title] = "مقایسه محصولات";
        ViewData[PageMetaKeys.Robots] = "noindex, follow";
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
