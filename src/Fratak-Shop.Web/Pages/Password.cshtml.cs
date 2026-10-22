using Microsoft.AspNetCore.Mvc.RazorPages;
using FratakShop.Models;
using System.Collections.Generic;

namespace FratakShop.Pages;

public class PasswordModel : PageModel
{
    public List<TextItem> RuleItems { get; set; } = SampleData.PasswordRules;
    public void OnGet()
    {
    }
}
