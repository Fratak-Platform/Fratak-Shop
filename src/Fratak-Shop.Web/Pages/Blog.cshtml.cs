using Microsoft.AspNetCore.Mvc.RazorPages;
using FratakShop.Models;
using System.Collections.Generic;

namespace FratakShop.Pages;

public class BlogModel : PageModel
{
    public List<BlogCard> BlogCards { get; set; } = SampleData.BlogCards;
    public List<PageItem> PaginationItems { get; set; } = SampleData.BlogPagination;
    public void OnGet()
    {
    }
}
