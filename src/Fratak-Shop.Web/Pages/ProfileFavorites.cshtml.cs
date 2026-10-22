using Microsoft.AspNetCore.Mvc.RazorPages;
using FratakShop.Models;
using System.Collections.Generic;

namespace FratakShop.Pages;

public class ProfileFavoritesModel : PageModel
{
    public List<ProfileMenuItem> SidebarItems { get; set; } = SampleData.ProfileSidebarProfileFavorites;
    public void OnGet()
    {
    }
}
