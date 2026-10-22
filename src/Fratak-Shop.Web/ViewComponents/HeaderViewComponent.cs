using Fratak_Shop.Core.DTOs.Categories.Query;
using Fratak_Shop.Core.DTOs.Shopping.Query;
using Fratak_Shop.Core.Services.Categories;
using Fratak_Shop.Web.Infrastructure;
using Fratak_Shop.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

namespace Fratak_Shop.Web.ViewComponents;

public class HeaderViewModel
{
    public List<CategoryDto> Categories { get; init; } = [];
    public CartDto? Cart { get; init; }
    public bool IsSignedIn { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    public string? SearchQuery { get; init; }
    public string CurrentPath { get; init; } = "/";

    public int CartCount => Cart?.TotalQuantity ?? 0;
}

/// <summary>
/// Site header: category mega menu (cached), mini cart and account menu.
/// </summary>
public class HeaderViewComponent(
    ICategoryService categoryService,
    ICartSessionService cartSession,
    IMemoryCache cache) : ViewComponent
{
    public const string MenuCacheKey = "header:menu-categories";

    public async Task<IViewComponentResult> InvokeAsync()
    {
        var categories = await cache.GetOrCreateAsync(MenuCacheKey, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10);
            return categoryService.GetMenuCategoriesAsync();
        }) ?? [];

        var model = new HeaderViewModel
        {
            Categories = categories,
            Cart = await cartSession.GetCartAsync(),
            IsSignedIn = UserClaimsPrincipal.IsSignedIn(),
            DisplayName = UserClaimsPrincipal.GetDisplayName(),
            SearchQuery = HttpContext.Request.Query["q"].ToString(),
            CurrentPath = HttpContext.Request.Path.Value ?? "/",
        };

        return View(model);
    }
}
