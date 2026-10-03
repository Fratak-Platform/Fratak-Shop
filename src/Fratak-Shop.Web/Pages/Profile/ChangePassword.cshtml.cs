using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Core.DTOs.Seo.Query;
using Fratak_Shop.Core.DTOs.Auth.Command;
using Fratak_Shop.Core.Services.Auth;
using Fratak_Shop.Core.Services.Navigation;
using Fratak_Shop.Core.Services.Notifications;
using Fratak_Shop.Core.Services.Seo;
using Fratak_Shop.Core.Services.Settings;
using Fratak_Shop.Core.Services.Shopping;
using Fratak_Shop.Data.Entities.Seo;
using Fratak_Shop.Web.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Pages.Profile;

[Authorize(Policy = PolicyNames.CustomerOnly)]
public class ChangePasswordModel(
    ISiteSettingService settings,
    IMenuService menus,
    ICartService cart,
    INotificationService notifications,
    ISeoService seoService,
    IAuthService auth)
    : FratakPageModel(settings, menus, cart, notifications, seoService)
{
    [BindProperty]
    public PasswordInput Input { get; set; } = new();

    public void OnGet()
    {
        ApplySeo(null, new SeoFallback
        {
            SiteName = Site.SiteName,
            SiteUrl = Site.SiteUrl,
            Path = "/profile/change-password",
            ContentTitle = "تغییر رمز عبور",
            EntityType = SeoEntityType.Page,
            ForceNoIndex = true,
        });
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (CurrentUserId is not { } userId)
            return Redirect("/login");

        if (!ModelState.IsValid)
            return Page();

        if (Input.NewPassword != Input.ConfirmPassword)
        {
            ModelState.AddModelError(string.Empty, "تکرار رمز عبور مطابقت ندارد.");
            return Page();
        }

        var result = await auth.ChangePasswordAsync(new ChangePasswordCommand
        {
            UserId = userId,
            CurrentPassword = Input.CurrentPassword,
            NewPassword = Input.NewPassword,
        });

        if (!result.IsSuccess())
        {
            ModelState.AddModelError(string.Empty, result.Message);
            return Page();
        }

        Notify(result.Message);
        return RedirectToPage("/Profile/PersonalInfo");
    }

    public class PasswordInput
    {
        public string? CurrentPassword { get; set; }

        [System.ComponentModel.DataAnnotations.Required(ErrorMessage = "رمز عبور جدید را وارد کنید.")]
        [System.ComponentModel.DataAnnotations.MinLength(6, ErrorMessage = "رمز عبور باید حداقل ۶ کاراکتر باشد.")]
        public string NewPassword { get; set; } = null!;

        [System.ComponentModel.DataAnnotations.Required(ErrorMessage = "تکرار رمز عبور را وارد کنید.")]
        public string ConfirmPassword { get; set; } = null!;
    }
}
