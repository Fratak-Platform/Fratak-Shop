using Fratak_Shop.Core.DTOs.Users.Command;
using Fratak_Shop.Core.Services.Auth;
using Fratak_Shop.Core.Services.Media;
using Fratak_Shop.Core.Services.Navigation;
using Fratak_Shop.Core.Services.Notifications;
using Fratak_Shop.Core.Services.Seo;
using Fratak_Shop.Core.Services.Settings;
using Fratak_Shop.Core.Services.Shopping;
using Fratak_Shop.Core.Services.Users;
using Fratak_Shop.Data.Entities.Seo;
using Fratak_Shop.Web.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Pages.Profile;

[Authorize(Policy = PolicyNames.CustomerOnly)]
public class PersonalInfoModel(
    ISiteSettingService settings,
    IMenuService menus,
    ICartService cart,
    INotificationService notifications,
    ISeoService seoService,
    IUserService users,
    IMediaService media)
    : FratakPageModel(settings, menus, cart, notifications, seoService)
{
    public UserProfileDto Profile { get; private set; } = null!;

    [BindProperty]
    public ProfileInput Input { get; set; } = new();

    [BindProperty]
    public IFormFile? Avatar { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        if (CurrentUserId is not { } userId)
            return Redirect("/login?returnUrl=/profile/personal-info");

        Profile = await users.GetProfileAsync(userId) ?? new UserProfileDto();
        Input = new ProfileInput
        {
            FirstName = Profile.FirstName,
            LastName = Profile.LastName,
            Email = Profile.Email,
        };

        ApplySeo(null, new SeoFallback
        {
            SiteName = Site.SiteName,
            SiteUrl = Site.SiteUrl,
            Path = "/profile/personal-info",
            ContentTitle = "اطلاعات شخصی",
            EntityType = SeoEntityType.Page,
            ForceNoIndex = true,
        });

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (CurrentUserId is not { } userId)
            return Redirect("/login");

        if (!ModelState.IsValid)
        {
            Profile = await users.GetProfileAsync(userId) ?? new UserProfileDto();
            return Page();
        }

        var result = await users.UpdateProfileAsync(new UpdateProfileCommand
        {
            UserId = userId,
            FirstName = Input.FirstName,
            LastName = Input.LastName,
            Email = Input.Email,
        });

        if (Avatar is { Length: > 0 })
        {
            var savedAvatar = await media.SaveImageAsync(Avatar, MediaFolders.Avatars);
            if (savedAvatar.IsSuccess() && savedAvatar.Data is not null)
                await users.UpdateAvatarAsync(userId, savedAvatar.Data);
            else
                Notify(savedAvatar.Message, "warning");
        }

        Notify(result.Message, result.IsSuccess() ? "success" : "error");
        return RedirectToPage("/Profile/PersonalInfo");
    }

    public class ProfileInput
    {
        public string? FirstName { get; set; }
        public string? LastName { get; set; }

        [System.ComponentModel.DataAnnotations.EmailAddress(ErrorMessage = "ایمیل معتبر نیست.")]
        public string? Email { get; set; }
    }
}
