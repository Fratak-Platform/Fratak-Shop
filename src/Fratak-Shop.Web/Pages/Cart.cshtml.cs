using Microsoft.AspNetCore.Mvc.RazorPages;
using FratakShop.Models;
using System.Collections.Generic;

namespace FratakShop.Pages;

public class CartModel : PageModel
{
    public List<ProductAttribute> CartAttributes { get; set; } = SampleData.CartAttributes;
    public void OnGet()
    {
    }
}
