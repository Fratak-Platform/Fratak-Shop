using System.ComponentModel.DataAnnotations;
using Fratak_Shop.Core.DTOs.Users.Command;
using Fratak_Shop.Core.Services.Users;
using Fratak_Shop.Data.Entities.Pages;
using Fratak_Shop.Data.Entities.Users;
using Fratak_Shop.Web.Infrastructure;
using Fratak_Shop.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Pages;

public class RegisterModel(IUserService userService, IShopAuthService authService, IOtpService otpService, IPageMetaService pageMeta) : ShopPageModel
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
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        await pageMeta.ApplyAsync(ViewData, SitePage.Register, "ثبت نام");

        if (!ModelState.IsValid)
            return Page();

        var phoneNumber = Input.PhoneNumber.Trim();

        var result = await userService.CreateUserAsync(new CreateUserCommand
        {
            PhoneNumber = phoneNumber,
            Email = string.IsNullOrWhiteSpace(Input.Email) ? null : Input.Email.Trim(),
            Password = Input.Password,
            FirstName = Input.FirstName?.Trim(),
            LastName = Input.LastName?.Trim(),
            Role = UserRole.Customer,
        });

        if (!Succeeded(result))
        {
            ModelState.AddModelError(string.Empty, result.Message);
            return Page();
        }

        var user = await userService.LoginAsync(new LoginCommand { PhoneNumber = phoneNumber, Password = Input.Password });
        if (user is null)
        {
            SuccessMessage = "ثبت نام انجام شد؛ لطفاً وارد شوید.";
            return RedirectToPage("/Login", new { returnUrl = ReturnUrl });
        }

        await authService.SignInAsync(user, isPersistent: true);
        await otpService.SendAsync(phoneNumber);

        SuccessMessage = "ثبت نام با موفقیت انجام شد؛ کد تایید برای شما پیامک شد.";
        return RedirectToPage("/Verify", new { returnUrl = ReturnUrl });
    }

    public class RegisterInput
    {
        [MaxLength(100)]
        public string? FirstName { get; set; }

        [MaxLength(100)]
        public string? LastName { get; set; }

        [EmailAddress(ErrorMessage = "ایمیل وارد شده معتبر نیست.")]
        [MaxLength(255)]
        public string? Email { get; set; }

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

        [Range(typeof(bool), "true", "true", ErrorMessage = "پذیرش قوانین و مقررات الزامی است.")]
        public bool AcceptRules { get; set; }
    }
}
