using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Fratak_Shop.Web.Pages.Auth;

[AllowAnonymous]
public class LogoutModel : Microsoft.AspNetCore.Mvc.RazorPages.PageModel
{
    public IActionResult OnGet() => Redirect("/");

    public async Task<IActionResult> OnPostAsync()
    {
        await Infrastructure.AuthSignIn.SignOutAsync(HttpContext);
        return Redirect("/");
    }
}
