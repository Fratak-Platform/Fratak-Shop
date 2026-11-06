using System.ComponentModel.DataAnnotations;
using Fratak_Shop.Core.DTOs.Users.Command;
using Fratak_Shop.Core.Services.Users;
using Fratak_Shop.Web.Infrastructure;
using Fratak_Shop.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Pages.Profile;

public class PasswordModel(IUserService userService, IShopAuthService authService) : ShopPageModel
{
    public static readonly string[] Rules =
    [
        "حداقل ۶ کاراکتر داشته باشد.",
        "ترکیبی از حروف و اعداد باشد.",
        "با رمز عبور فعلی یکسان نباشد.",
    ];

    [BindProperty]
    public PasswordInput Input { get; set; } = new();

    public Core.DTOs.Users.Query.UserDto? CurrentUser { get; private set; }

    public async Task OnGetAsync()
    {
        ViewData[PageMetaKeys.Title] = "رمز عبور و امنیت";
        ViewData[PageMetaKeys.Robots] = "noindex, nofollow";
        CurrentUser = await userService.GetUserByIdAsync(CurrentUserId!.Value);
    }

    /// <summary>Turns two-step sign-in (password + SMS code) on or off.</summary>
    public async Task<IActionResult> OnPostTwoFactorAsync(bool enable)
    {
        ShowResult(await userService.SetTwoFactorAsync(CurrentUserId!.Value, enable));
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        ViewData[PageMetaKeys.Title] = "رمز عبور و امنیت";
        CurrentUser = await userService.GetUserByIdAsync(CurrentUserId!.Value);

        if (!ModelState.IsValid)
            return Page();

        var result = await userService.ChangePasswordAsync(new ChangePasswordCommand
        {
            UserId = CurrentUserId!.Value,
            CurrentPassword = Input.CurrentPassword,
            NewPassword = Input.NewPassword,
        });

        if (!Succeeded(result))
        {
            ModelState.AddModelError(string.Empty, result.Message);
            return Page();
        }

        // The password change rotated the security stamp: every other device is signed
        // out (refresh tokens revoked) and this browser gets a fresh session.
        await authService.SignOutOtherDevicesAsync(CurrentUserId!.Value, keepCurrent: true);

        SuccessMessage = result.Message + " سایر دستگاه‌ها از حساب شما خارج شدند.";
        return RedirectToPage("/Profile/Index");
    }

    public class PasswordInput
    {
        [DataType(DataType.Password)]
        public string? CurrentPassword { get; set; }

        [Required(ErrorMessage = "رمز عبور جدید را وارد کنید.")]
        [StringLength(100, MinimumLength = 6, ErrorMessage = "رمز عبور جدید باید حداقل ۶ کاراکتر باشد.")]
        [DataType(DataType.Password)]
        public string NewPassword { get; set; } = string.Empty;

        [Compare(nameof(NewPassword), ErrorMessage = "تکرار رمز عبور با رمز جدید یکسان نیست.")]
        [DataType(DataType.Password)]
        public string ConfirmPassword { get; set; } = string.Empty;
    }
}
