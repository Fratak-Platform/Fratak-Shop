using Microsoft.AspNetCore.Mvc.RazorPages;
using FratakShop.Models;
using System.Collections.Generic;

namespace FratakShop.Pages;

public class FaqModel : PageModel
{
    public List<FaqItem> FaqLeft { get; set; } = SampleData.FaqLeft;
    public List<FaqItem> FaqRight { get; set; } = SampleData.FaqRight;
    public void OnGet()
    {
    }
}
