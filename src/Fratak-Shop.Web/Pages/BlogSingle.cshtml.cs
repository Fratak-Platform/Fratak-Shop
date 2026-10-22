using Microsoft.AspNetCore.Mvc.RazorPages;
using FratakShop.Models;
using System.Collections.Generic;

namespace FratakShop.Pages;

public class BlogSingleModel : PageModel
{
    public List<MetaItem> PostMeta { get; set; } = SampleData.BlogMeta;
    public List<ShareLink> ShareLinks { get; set; } = SampleData.ShareLinks;
    public void OnGet()
    {
    }
}
