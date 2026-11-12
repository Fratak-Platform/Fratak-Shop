using Fratak_Shop.Core.DTOs.Categories.Query;
using Fratak_Shop.Core.DTOs.Shopping.Query;
using Fratak_Shop.Core.Services.Categories;
using Fratak_Shop.Core.Services.Contents;
using Fratak_Shop.Core.Services.Notifications;
using Fratak_Shop.Data.Entities.Contents;
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

    /// <summary>دور ۲۹: خطاب مناسب نسل («محمد عزیز»، «ناظری گرامی») برای سرِ منوی کاربر.</summary>
    public string Greeting { get; init; } = string.Empty;
    public string? SearchQuery { get; init; }
    public string CurrentPath { get; init; } = "/";

    /// <summary>Main navigation (DB menu "header-main").</summary>
    public List<MenuItemDto> MainMenu { get; init; } = [];

    /// <summary>Shortcut links beside the category mega menu (DB menu "header-shortcuts").</summary>
    public List<MenuItemDto> Shortcuts { get; init; } = [];

    public int UnreadNotifications { get; init; }

    /// <summary>Staff (owner or any admin role) see a link to the admin panel.</summary>
    public bool IsStaff { get; init; }

    public int CartCount => Cart?.TotalQuantity ?? 0;
}

/// <summary>
/// Site header: category mega menu (cached), mini cart and account menu.
/// </summary>
public class HeaderViewComponent(
    ICategoryService categoryService,
    ICartSessionService cartSession,
    ISiteContext site,
    INotificationService notifications,
    Core.Services.Admin.IAdminAccessService adminAccess,
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

        var userId = UserClaimsPrincipal.GetUserId();
        var unread = userId.HasValue ? await notifications.GetUnreadCountAsync(userId.Value) : 0;
        var isStaff = userId.HasValue && (await adminAccess.GetAccessAsync(userId.Value)).IsStaff;

        var model = new HeaderViewModel
        {
            Categories = categories,
            Cart = await cartSession.GetCartAsync(),
            IsSignedIn = UserClaimsPrincipal.IsSignedIn(),
            DisplayName = UserClaimsPrincipal.GetDisplayName(),
            Greeting = UserClaimsPrincipal.GetGreeting(),
            SearchQuery = HttpContext.Request.Query["q"].ToString(),
            CurrentPath = HttpContext.Request.Path.Value ?? "/",
            MainMenu = site.MenuItems(MenuLocation.HeaderMain),
            Shortcuts = site.MenuItems(MenuLocation.HeaderShortcuts),
            UnreadNotifications = unread,
            IsStaff = isStaff,
        };

        return View(model);
    }
}
