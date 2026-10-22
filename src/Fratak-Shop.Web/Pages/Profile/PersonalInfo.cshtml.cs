using System.ComponentModel.DataAnnotations;
using Fratak_Shop.Core.DTOs.Users.Command;
using Fratak_Shop.Core.Services.Users;
using Fratak_Shop.Data.Entities.Pages;
using Fratak_Shop.Web.Infrastructure;
using Fratak_Shop.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Pages.Profile;

public class PersonalInfoModel(IUserService userService, IShopAuthService authService, IPageMetaService pageMeta) : ShopPageModel
{
    [BindProperty]
    public ProfileInput Input { get; set; } = new();

    public string PhoneNumber { get; private set; } = string.Empty;
    public bool IsPhoneVerified { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var user = await userService.GetUserByIdAsync(CurrentUserId!.Value);
        if (user is null)
            return RedirectToPage("/Logout");

        Input = new ProfileInput { FirstName = user.FirstName, LastName = user.LastName, Email = user.Email };
        PhoneNumber = user.PhoneNumber;
        IsPhoneVerified = user.IsPhoneVerified;

        await pageMeta.ApplyAsync(ViewData, SitePage.UserProfile, "اطلاعات حساب کاربری");
        ViewData[PageMetaKeys.Robots] = "noindex, nofollow";
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            ErrorMessage = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).FirstOrDefault();
            return RedirectToPage();
        }

        var result = await userService.UpdateProfileAsync(new UpdateProfileCommand
        {
            UserId = CurrentUserId!.Value,
            FirstName = Input.FirstName,
            LastName = Input.LastName,
            Email = Input.Email,
        });

        if (Succeeded(result))
        {
            // Refresh the auth cookie so the new name shows up in the header.
            var user = await userService.GetUserByIdAsync(CurrentUserId!.Value);
            if (user is not null)
                await authService.SignInAsync(user, isPersistent: true);
        }

        ShowResult(result);
        return RedirectToPage();
    }

    public class ProfileInput
    {
        [Required(ErrorMessage = "نام را وارد کنید.")]
        [MaxLength(100)]
        public string? FirstName { get; set; }

        [Required(ErrorMessage = "نام خانوادگی را وارد کنید.")]
        [MaxLength(100)]
        public string? LastName { get; set; }

        [EmailAddress(ErrorMessage = "ایمیل وارد شده معتبر نیست.")]
        [MaxLength(255)]
        public string? Email { get; set; }
    }
}
