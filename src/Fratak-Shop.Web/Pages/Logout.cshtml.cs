using Fratak_Shop.Web.Infrastructure;
using Fratak_Shop.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Pages;

/// <summary>Sign-out endpoint (POST only, protected by the antiforgery token).</summary>
public class LogoutModel(IShopAuthService authService) : ShopPageModel
{
    public IActionResult OnGet() => RedirectToPage("/Index");

    public async Task<IActionResult> OnPostAsync()
    {
        await authService.SignOutAsync();
        SuccessMessage = "از حساب کاربری خود خارج شدید.";
        return RedirectToPage("/Index");
    }
}
