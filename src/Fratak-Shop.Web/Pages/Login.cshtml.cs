using System.ComponentModel.DataAnnotations;
using Fratak_Shop.Core.DTOs.Users.Command;
using Fratak_Shop.Core.Services.Users;
using Fratak_Shop.Data.Entities.Pages;
using Fratak_Shop.Web.Infrastructure;
using Fratak_Shop.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Pages;

public class LoginModel(IUserService userService, IShopAuthService authService, IPageMetaService pageMeta) : ShopPageModel
{
    [BindProperty]
    public LoginInput Input { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        if (IsSignedIn)
            return RedirectToLocal(ReturnUrl, "/Profile/Index");

        await pageMeta.ApplyAsync(ViewData, SitePage.Login, "ورود به حساب کاربری");
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        await pageMeta.ApplyAsync(ViewData, SitePage.Login, "ورود به حساب کاربری");

        if (!ModelState.IsValid)
            return Page();

        var result = await userService.LoginAsync(new LoginCommand
        {
            PhoneNumber = Input.PhoneNumber.Trim(),
            Password = Input.Password,
        });

        if (!Succeeded(result) || result.Data is null)
        {
            ModelState.AddModelError(string.Empty, result.Message);
            return Page();
        }

        var user = result.Data;
        await authService.SignInAsync(user, Input.RememberMe);
        SuccessMessage = $"{user.FullName ?? "کاربر"} عزیز، خوش آمدید.";

        return RedirectToLocal(ReturnUrl, "/Index");
    }

    public class LoginInput
    {
        [Required(ErrorMessage = "شماره موبایل را وارد کنید.")]
        [RegularExpression(@"^09\d{9}$", ErrorMessage = "شماره موبایل باید با ۰۹ شروع شود و ۱۱ رقم باشد.")]
        public string PhoneNumber { get; set; } = string.Empty;

        [Required(ErrorMessage = "رمز عبور را وارد کنید.")]
        [DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;

        public bool RememberMe { get; set; } = true;
    }
}
