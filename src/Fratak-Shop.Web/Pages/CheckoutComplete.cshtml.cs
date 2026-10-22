using Microsoft.AspNetCore.Mvc.RazorPages;
using FratakShop.Models;
using System.Collections.Generic;

namespace FratakShop.Pages;

public class CheckoutCompleteModel : PageModel
{
    public List<SummaryItem> ReceiptRows { get; set; } = SampleData.ReceiptRows;
    public void OnGet()
    {
    }
}
