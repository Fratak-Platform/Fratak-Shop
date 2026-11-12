using System.ComponentModel.DataAnnotations;
using Fratak_Shop.Core.DTOs.Users.Command;
using Fratak_Shop.Core.Services.Users;
using Fratak_Shop.Data.Entities.Pages;
using Fratak_Shop.Web.Infrastructure;
using Fratak_Shop.Web.Infrastructure.Email;
using Fratak_Shop.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Pages.Profile;

public class PersonalInfoModel(IUserService userService, IShopAuthService authService, IPageMetaService pageMeta, EmailVerification emailVerification) : ShopPageModel
{
    [BindProperty]
    public ProfileInput Input { get; set; } = new();

    /// <summary>
    /// true وقتی کاربر تازه ثبت‌نام کرده (از /verify یا ورود پیامکی حساب جدید می‌آید): پیام خوش‌آمد و دکمهٔ «بعداً» نمایش داده می‌شود
    /// و بعد از ذخیره، کاربر به همان صفحه‌ای که می‌خواست (ReturnUrl) برمی‌گردد. مثال: /profile/info?welcome=true&amp;returnUrl=/cart
    /// </summary>
    [BindProperty(SupportsGet = true)]
    public bool Welcome { get; set; }

    /// <summary>صفحهٔ مقصد پس از تکمیل یا رد شدن (فقط آدرس داخلی سایت پذیرفته می‌شود).</summary>
    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    /// <summary>آدرس امن ادامه: ReturnUrl داخلی یا صفحهٔ اصلی.</summary>
    public string ContinueUrl => !string.IsNullOrWhiteSpace(ReturnUrl) && Url.IsLocalUrl(ReturnUrl) ? ReturnUrl : "/";

    public string PhoneNumber { get; private set; } = string.Empty;
    public bool IsPhoneVerified { get; private set; }

    /// <summary>مرحلهٔ دوازدهم: ایمیل ذخیره‌شدهٔ حساب و وضعیت تأیید آن (نشان «تأیید نشده» و دکمهٔ «ارسال لینک تأیید»).</summary>
    public string? SavedEmail { get; private set; }
    public bool IsEmailVerified { get; private set; }

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
        SavedEmail = user.Email;
        IsEmailVerified = user.IsEmailVerified;

        await pageMeta.ApplyAsync(ViewData, SitePage.UserProfile, "اطلاعات حساب کاربری");
        ViewData[PageMetaKeys.Robots] = "noindex, nofollow";
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            ErrorMessage = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).FirstOrDefault();
            return Stay();
        }

        DateOnly? birthDate = null;
        var parts = new[] { Input.BirthYear, Input.BirthMonth, Input.BirthDay };
        if (parts.Any(p => p.HasValue))
        {
            if (parts.Any(p => !p.HasValue))
            {
                ErrorMessage = "روز، ماه و سال تولد را کامل انتخاب کنید.";
                return Stay();
            }

            try
            {
                birthDate = DateOnly.FromDateTime(Persian.ToDateTime(Input.BirthYear!.Value, Input.BirthMonth!.Value, Input.BirthDay!.Value, 0, 0, 0, 0));
            }
            catch (ArgumentOutOfRangeException)
            {
                ErrorMessage = "تاریخ تولد معتبر نیست.";
                return Stay();
            }
        }

        var before = await userService.GetUserByIdAsync(CurrentUserId!.Value);
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

        // مرحلهٔ دوازدهم: ایمیل تازه → لینک تأیید خودکار ارسال می‌شود (اگر سرویس ایمیل فعال باشد).
        var newEmail = string.IsNullOrWhiteSpace(Input.Email) ? null : Input.Email.Trim();
        if (Succeeded(result) && newEmail is not null && !string.Equals(before?.Email?.Trim(), newEmail, StringComparison.OrdinalIgnoreCase))
        {
            var sent = await emailVerification.SendAsync(CurrentUserId!.Value, HttpContext.RequestAborted);
            if (Succeeded(sent))
                SuccessMessage = $"{result.Message} {sent.Message}";
        }
        // کاربر تازه‌وارد بعد از ذخیرهٔ موفق به ادامهٔ مسیرش (مثلاً سبد خرید) می‌رود.
        if (Welcome && Succeeded(result))
            return LocalRedirect(ContinueUrl);
        return Stay();
    }

    /// <summary>
    /// مرحلهٔ دوازدهم: ارسال دوبارهٔ لینک تأیید ایمیل (هر ۲ دقیقه یک بار، حداکثر ۵ بار در روز – EmailVerification).
    /// </summary>
    public async Task<IActionResult> OnPostSendVerificationAsync()
    {
        var result = await emailVerification.SendAsync(CurrentUserId!.Value, HttpContext.RequestAborted);
        ShowResult(result);
        return Stay();
    }

    /// <summary>ماندن در همین صفحه با حفظ حالت خوش‌آمد و مقصد.</summary>
    private IActionResult Stay() => Welcome
        ? RedirectToPage(new { welcome = true, returnUrl = ReturnUrl })
        : RedirectToPage();

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
