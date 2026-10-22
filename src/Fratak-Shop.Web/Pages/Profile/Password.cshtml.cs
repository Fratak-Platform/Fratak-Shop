using System.ComponentModel.DataAnnotations;
using Fratak_Shop.Core.DTOs.Users.Command;
using Fratak_Shop.Core.Services.Users;
using Fratak_Shop.Web.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Pages.Profile;

public class PasswordModel(IUserService userService) : ShopPageModel
{
    public static readonly string[] Rules =
    [
        "حداقل ۶ کاراکتر داشته باشد.",
        "ترکیبی از حروف و اعداد باشد.",
        "با رمز عبور فعلی یکسان نباشد.",
    ];

    [BindProperty]
    public PasswordInput Input { get; set; } = new();

    public void OnGet()
    {
        ViewData[PageMetaKeys.Title] = "تغییر رمز عبور";
        ViewData[PageMetaKeys.Robots] = "noindex, nofollow";
    }

    public async Task<IActionResult> OnPostAsync()
    {
        ViewData[PageMetaKeys.Title] = "تغییر رمز عبور";

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

        SuccessMessage = result.Message;
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
