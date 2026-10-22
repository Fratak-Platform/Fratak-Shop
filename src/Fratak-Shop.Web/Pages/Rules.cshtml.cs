using Microsoft.AspNetCore.Mvc.RazorPages;
using FratakShop.Models;
using System.Collections.Generic;

namespace FratakShop.Pages;

public class RulesModel : PageModel
{
    public List<RuleSection> RulesLeft { get; set; } = SampleData.RulesLeft;
    public List<RuleSection> RulesRight { get; set; } = SampleData.RulesRight;
    public void OnGet()
    {
    }
}
