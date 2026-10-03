using System.ComponentModel.DataAnnotations;

namespace Fratak_Shop.Core.DTOs.Auth.Command;

/// <summary>فرم ثبت‌نام مشتری با شماره موبایل و رمز عبور.</summary>
public class RegisterUserCommand
{
    [Required(ErrorMessage = "شماره موبایل را وارد کنید.")]
    public string PhoneNumber { get; set; } = null!;

    public string? Password { get; set; }

    public string? FirstName { get; set; }

    public string? LastName { get; set; }

    public string? Email { get; set; }

    public bool AcceptTerms { get; set; }
}

/// <summary>ورود با شماره موبایل و رمز عبور.</summary>
public class LoginCommand
{
    public string PhoneNumber { get; set; } = null!;

    public string Password { get; set; } = null!;

    public bool RememberMe { get; set; }
}

/// <summary>درخواست ارسال کد یک‌بار مصرف (ثبت‌نام، ورود بدون رمز یا بازیابی رمز).</summary>
public class SendVerificationCodeCommand
{
    public string PhoneNumber { get; set; } = null!;

    public Data.Entities.Users.UserTokenPurpose Purpose { get; set; } =
        Data.Entities.Users.UserTokenPurpose.PhoneVerification;
}

/// <summary>بررسی کد ارسال‌شده.</summary>
public class VerifyCodeCommand
{
    public string PhoneNumber { get; set; } = null!;

    public string Code { get; set; } = null!;

    public Data.Entities.Users.UserTokenPurpose Purpose { get; set; } =
        Data.Entities.Users.UserTokenPurpose.PhoneVerification;
}

/// <summary>تعیین رمز عبور جدید پس از تأیید کد بازیابی.</summary>
public class ResetPasswordCommand
{
    public string PhoneNumber { get; set; } = null!;

    public string Code { get; set; } = null!;

    public string NewPassword { get; set; } = null!;
}

/// <summary>تغییر رمز عبور کاربر وارد‌شده.</summary>
public class ChangePasswordCommand
{
    public Guid UserId { get; set; }

    public string? CurrentPassword { get; set; }

    public string NewPassword { get; set; } = null!;
}
