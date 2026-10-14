using Fratak_Shop.Data.Entities.Users;

namespace Fratak_Shop.Core.DTOs.Users.Command;

/// <summary>
/// دستور ایجاد کاربر. رمز عبور اختیاری است (خرید مهمان و ورود با کد یک‌بارمصرف)
/// و در صورت ارسال، با Argon2id هش می‌شود.
/// </summary>
public class CreateUserCommand
{
    public string PhoneNumber { get; set; } = null!;
    public string? Email { get; set; }
    public string? Password { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public UserRole Role { get; set; } = UserRole.Customer;
    public bool IsPhoneVerified { get; set; }
    public bool IsEmailVerified { get; set; }
}

/// <summary>
/// دستور ویرایش اطلاعات کاربر (بدون رمز عبور).
/// </summary>
public class EditUserCommand
{
    public Guid Id { get; set; }
    public string PhoneNumber { get; set; } = null!;
    public string? Email { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public UserRole Role { get; set; } = UserRole.Customer;
    public bool IsPhoneVerified { get; set; }
    public bool IsEmailVerified { get; set; }
}

/// <summary>
/// دستور تغییر رمز عبور کاربر. کاربری که هنوز رمز ندارد می‌تواند بدون رمز فعلی
/// رمز جدید ثبت کند.
/// </summary>
public class ChangePasswordCommand
{
    public Guid UserId { get; set; }
    public string? CurrentPassword { get; set; }
    public string NewPassword { get; set; } = null!;
}

/// <summary>
/// دستور ورود با شماره موبایل و رمز عبور.
/// </summary>
public class LoginCommand
{
    public string PhoneNumber { get; set; } = null!;
    public string Password { get; set; } = null!;
}
