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
        if (user.BirthDate is { } birthDate)
        {
            var date = birthDate.ToDateTime(TimeOnly.MinValue);
            (Input.BirthYear, Input.BirthMonth, Input.BirthDay) = (Persian.GetYear(date), Persian.GetMonth(date), Persian.GetDayOfMonth(date));
        }
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

        DateOnly? birthDate = null;
        var parts = new[] { Input.BirthYear, Input.BirthMonth, Input.BirthDay };
        if (parts.Any(p => p.HasValue))
        {
            if (parts.Any(p => !p.HasValue))
            {
                ErrorMessage = "روز، ماه و سال تولد را کامل انتخاب کنید.";
                return RedirectToPage();
            }

            try
            {
                birthDate = DateOnly.FromDateTime(Persian.ToDateTime(Input.BirthYear!.Value, Input.BirthMonth!.Value, Input.BirthDay!.Value, 0, 0, 0, 0));
            }
            catch (ArgumentOutOfRangeException)
            {
                ErrorMessage = "تاریخ تولد معتبر نیست.";
                return RedirectToPage();
            }
        }

        var result = await userService.UpdateProfileAsync(new UpdateProfileCommand
        {
            UserId = CurrentUserId!.Value,
            FirstName = Input.FirstName,
            LastName = Input.LastName,
            Email = Input.Email,
            BirthDate = birthDate,
        });

        if (Succeeded(result))
        {
            // Re-issue the access token so the new name shows up in the header.
            await authService.RefreshClaimsAsync(CurrentUserId!.Value);
        }

        ShowResult(result);
        return RedirectToPage();
    }

    private static readonly System.Globalization.PersianCalendar Persian = new();

    /// <summary>Selectable birth years (Persian), newest first.</summary>
    public static IEnumerable<int> BirthYears
    {
        get
        {
            var current = Persian.GetYear(DateTime.UtcNow);
            return Enumerable.Range(current - 100, 96).Reverse();
        }
    }

    public static readonly string[] PersianMonths =
        ["فروردین", "اردیبهشت", "خرداد", "تیر", "مرداد", "شهریور", "مهر", "آبان", "آذر", "دی", "بهمن", "اسفند"];

    public class ProfileInput
    {
        [Range(1300, 1500)]
        public int? BirthYear { get; set; }

        [Range(1, 12)]
        public int? BirthMonth { get; set; }

        [Range(1, 31)]
        public int? BirthDay { get; set; }

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
