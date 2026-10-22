using Microsoft.AspNetCore.Mvc.RazorPages;
using FratakShop.Models;
using System.Collections.Generic;

namespace FratakShop.Pages;

public class ProductComparisionModel : PageModel
{
    public List<CompareProduct> CompareProducts { get; set; } = SampleData.CompareProducts;
    public List<CompareRow> CompareRows { get; set; } = SampleData.CompareRows;
    public void OnGet()
    {
    }
}
