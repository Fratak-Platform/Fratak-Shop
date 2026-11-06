using Fratak_Shop.Core.DTOs.Blogs.Categories.Query;
using Fratak_Shop.Core.DTOs.Blogs.Posts.Query;
using Fratak_Shop.Core.Services.Blogs;
using Fratak_Shop.Data.Entities.Pages;
using Fratak_Shop.Web.Infrastructure;
using Fratak_Shop.Web.Models;
using Fratak_Shop.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Pages;

[Microsoft.AspNetCore.OutputCaching.OutputCache(PolicyName = Fratak_Shop.Web.Infrastructure.PageCache.PageCachePolicy.Name)]
public class BlogModel(IPostService postService, IBlogCategoryService blogCategoryService, IPageMetaService pageMeta) : ShopPageModel
{
    [BindProperty(SupportsGet = true, Name = "category")]
    public string? CategorySlug { get; set; }

    [BindProperty(SupportsGet = true, Name = "q")]
    public string? Query { get; set; }

    // FromQuery: "page" is also a Razor Pages route value (the page path), which would win over the query string.
    [FromQuery(Name = "page")]
    public int PageNumber { get; set; } = 1;

    public BlogPostFilterResult Result { get; private set; } = null!;
    public List<BlogCategoryDto> Categories { get; private set; } = [];
    public BlogCategoryDto? Category { get; private set; }
    public PagerModel Pager { get; private set; } = null!;

    public async Task<IActionResult> OnGetAsync()
    {
        Categories = blogCategoryService.GetAllCategoriesAsync().Where(c => c.IsActive).ToList();

        if (!string.IsNullOrWhiteSpace(CategorySlug))
        {
            Category = Categories.FirstOrDefault(c => c.Slug == CategorySlug);
            if (Category is null)
                return NotFound();
        }

        Result = await postService.GetPostByFilterAsync(new BlogPostFilterParams
        {
            CategorySlug = CategorySlug,
            Search = Query,
            OnlyPublished = true,
            PageId = PageNumber,
            Take = 12,
        });

        Pager = PagerModel.From(Result, Request);

        if (Category is not null)
            pageMeta.Apply(ViewData, Category.Seo, $"مقالات {Category.Name}", Category.Description);
        else
            await pageMeta.ApplyAsync(ViewData, SitePage.Blog, "وبلاگ", "جدیدترین مقالات، راهنمای خرید و اخبار دنیای تکنولوژی.");

        if (Category is not null)
            pageMeta.AddBreadcrumbs(ViewData, ("وبلاگ", "/blog"), (Category.Name, null));
        else
            pageMeta.AddBreadcrumbs(ViewData, ("وبلاگ", null));

        if (PageNumber > 1 || !string.IsNullOrWhiteSpace(Query))
            ViewData[PageMetaKeys.Robots] = "noindex, follow";

        return Page();
    }
}
