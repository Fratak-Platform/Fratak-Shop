using System.ComponentModel.DataAnnotations;
using Fratak_Shop.Core.DTOs.Users.Command;
using Fratak_Shop.Core.Services.Users;
using Fratak_Shop.Data.Entities.Pages;
using Fratak_Shop.Data.Entities.Users;
using Fratak_Shop.Web.Infrastructure;
using Fratak_Shop.Web.Infrastructure.Security;
using Fratak_Shop.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Pages;

public class RegisterModel(
    IUserService userService,
    IShopAuthService authService,
    IOtpService otpService,
    Core.Services.Events.IShopEvents events,
    ISiteContext site,
    IPageMetaService pageMeta) : ShopPageModel
{
    [BindProperty]
    public RegisterInput Input { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        if (IsSignedIn)
            return RedirectToLocal(ReturnUrl, "/Profile/Index");

        await pageMeta.ApplyAsync(ViewData, SitePage.Register, "ثبت نام");

        // Invite links (/register?ref=CODE or any page with ?ref=) are remembered in a cookie.
        var referral = Request.Query["ref"].ToString().Trim();
        Input.ReferralCode = referral.Length is > 0 and <= 20 ? referral : ReferralCookie.Read(HttpContext);

        PrepareCaptcha(CaptchaActions.Register);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        await pageMeta.ApplyAsync(ViewData, SitePage.Register, "ثبت نام");

        if (!VerifyCaptcha(CaptchaActions.Register) || !ModelState.IsValid)
            return Page();

        // Every attempt counts (account-enumeration / bulk sign-up guard).
        CaptchaFailed(CaptchaActions.Register);
        var phoneNumber = Input.PhoneNumber.Trim();

        var result = await userService.CreateUserAsync(new CreateUserCommand
        {
            PhoneNumber = phoneNumber,
            Password = Input.Password,
            Role = UserRole.Customer,
        });

        if (!Succeeded(result))
        {
            ModelState.AddModelError(string.Empty, result.Message);
            return Page();
        }

        var login = await userService.LoginAsync(new LoginCommand { PhoneNumber = phoneNumber, Password = Input.Password });
        var user = login.Data;
        if (!Succeeded(login) || user is null)
        {
            SuccessMessage = "ثبت نام انجام شد؛ لطفاً وارد شوید.";
            return RedirectToPage("/Login", new { returnUrl = ReturnUrl });
        }

        await authService.SignInAsync(user, isPersistent: true);
        var referralCode = string.IsNullOrWhiteSpace(Input.ReferralCode) ? ReferralCookie.Read(HttpContext) : Input.ReferralCode.Trim();
        await events.UserRegisteredAsync(user.Id, site.Settings.IsReferralEnabled ? referralCode : null);
        ReferralCookie.Clear(HttpContext);
        await otpService.SendAsync(phoneNumber, OtpPurpose.VerifyPhone);

        SuccessMessage = "ثبت نام با موفقیت انجام شد؛ کد تایید برای شما پیامک شد.";
        return RedirectToPage("/Verify", new { returnUrl = ReturnUrl });
    }

    /// <summary>
    /// فرم ثبت‌نام کوتاه (مرحلهٔ دهم): فقط موبایل، رمز و تکرار آن، کد معرف (اختیاری) و پذیرش قوانین.
    /// نام، نام خانوادگی، ایمیل و تاریخ تولد عمداً پرسیده نمی‌شوند تا کاربر وسط ثبت‌نام منصرف نشود؛
    /// بعد از تأیید شماره، کاربر به «اطلاعات حساب» (/profile/info?welcome=true) می‌رود و می‌تواند آن‌ها را کامل کند یا رد شود.
    /// </summary>
    public class RegisterInput
    {
        [Required(ErrorMessage = "شماره موبایل را وارد کنید.")]
        [RegularExpression(@"^09\d{9}$", ErrorMessage = "شماره موبایل باید با ۰۹ شروع شود و ۱۱ رقم باشد.")]
        public string PhoneNumber { get; set; } = string.Empty;

        [Required(ErrorMessage = "رمز عبور را وارد کنید.")]
        [StringLength(100, MinimumLength = 6, ErrorMessage = "رمز عبور باید حداقل ۶ کاراکتر باشد.")]
        [DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;

        [Compare(nameof(Password), ErrorMessage = "تکرار رمز عبور با رمز عبور یکسان نیست.")]
        [DataType(DataType.Password)]
        public string ConfirmPassword { get; set; } = string.Empty;

        [MaxLength(20)]
        public string? ReferralCode { get; set; }

        [Range(typeof(bool), "true", "true", ErrorMessage = "پذیرش قوانین و مقررات الزامی است.")]
        public bool AcceptRules { get; set; }
    }
}
