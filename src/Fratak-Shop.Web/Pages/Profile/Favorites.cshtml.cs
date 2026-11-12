using Fratak_Shop.Core.DTOs.Favorites.Command;
using Fratak_Shop.Core.DTOs.Favorites.Query;
using Fratak_Shop.Core.Services.Favorites;
using Fratak_Shop.Data.Entities.Pages;
using Fratak_Shop.Web.Infrastructure;
using Fratak_Shop.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Pages.Profile;

public class FavoritesModel(IFavoriteService favoriteService, IPageMetaService pageMeta) : ShopPageModel
{
    public List<FavoriteDto> Favorites { get; private set; } = [];

    public async Task OnGetAsync()
    {
        Favorites = await favoriteService.GetFavoritesByUserIdAsync(CurrentUserId!.Value);
        await pageMeta.ApplyAsync(ViewData, SitePage.UserFavorites, "علاقه مندی ها");
        ViewData[PageMetaKeys.Robots] = "noindex, nofollow";
    }

    /// <summary>Adds or removes a product (used by product cards and the product page).</summary>
    public async Task<IActionResult> OnPostToggleAsync(Guid productId, string? returnUrl = null)
    {
        var userId = CurrentUserId!.Value;

        if (await favoriteService.IsFavoriteAsync(userId, productId))
        {
            var removed = await favoriteService.RemoveFavoriteAsync(userId, productId);
            if (Succeeded(removed))
                removed.Message = "محصول از علاقه مندی ها حذف شد.";
            ShowResult(removed);
        }
        else
        {
            ShowResult(await favoriteService.AddFavoriteAsync(new CreateFavoriteCommand { UserId = userId, ProductId = productId }));
        }

        return RedirectToLocal(returnUrl, "/Profile/Favorites");
    }
}
