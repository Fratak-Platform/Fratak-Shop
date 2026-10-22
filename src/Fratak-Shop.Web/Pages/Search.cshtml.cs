using Microsoft.AspNetCore.Mvc.RazorPages;
using FratakShop.Models;
using System.Collections.Generic;

namespace FratakShop.Pages;

public class SearchModel : PageModel
{
    public List<BrandFilterOption> BrandFilters { get; set; } = SampleData.BrandFilters;
    public List<FilterOption> CategoryFilters { get; set; } = SampleData.CategoryFilters;
    public List<ProductGridCard> Products { get; set; } = SampleData.SearchProducts;
    public List<FilterOption> SellerFilters { get; set; } = SampleData.SellerFilters;
    public List<SortOption> SortOptions { get; set; } = SampleData.SortOptions;
    public void OnGet()
    {
    }
}
