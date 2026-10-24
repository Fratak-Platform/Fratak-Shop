using Fratak_Shop.Core.DTOs.Users.Command;
using Fratak_Shop.Core.DTOs.Users.Query;
using Fratak_Shop.Core.Utilities;

namespace Fratak_Shop.Core.Services.Users;

public interface IUserService
{
    Task<OperationResult> CreateUserAsync(CreateUserCommand command);
    Task<OperationResult> EditUserAsync(EditUserCommand command);
    Task<OperationResult> DeleteUserAsync(Guid id);

    /// <summary>ویرایش نام، نام خانوادگی و ایمیل توسط خود کاربر.</summary>
    Task<OperationResult> UpdateProfileAsync(UpdateProfileCommand command);

    /// <summary>ثبت تایید شماره موبایل پس از وارد کردن صحیح کد یک‌بارمصرف.</summary>
    Task<OperationResult> ConfirmPhoneAsync(Guid userId);

    /// <summary>تغییر رمز عبور با بررسی رمز فعلی (کاربر بدون رمز، رمز فعلی ندارد).</summary>
    Task<OperationResult> ChangePasswordAsync(ChangePasswordCommand command);

    /// <summary>
    /// بررسی شماره موبایل و رمز عبور؛ در صورت درستی، کاربر را برمی‌گرداند و
    /// زمان آخرین ورود را ثبت می‌کند. پس از ۵ تلاش ناموفق، ورود با رمز ۱۵ دقیقه قفل می‌شود.
    /// </summary>
    Task<OperationResult<UserDto>> LoginAsync(LoginCommand command);

    /// <summary>
    /// Sign-in with a verified one-time SMS code: returns the user of the number and creates a
    /// passwordless customer account when the number is new (the phone counts as verified).
    /// </summary>
    Task<OperationResult<PhoneSignInResult>> SignInWithPhoneAsync(string phoneNumber);

    /// <summary>Two-step sign-in (password + SMS code); needs a password and a verified phone.</summary>
    Task<OperationResult> SetTwoFactorAsync(Guid userId, bool enabled);

    /// <summary>ثبت ورود موفق با کد یک‌بارمصرف (زمان ورود و پاک شدن قفل).</summary>
    Task RecordLoginAsync(Guid userId);

    /// <summary>
    /// Security stamp of the user (null when the user no longer exists). Access tokens
    /// carry the stamp; changing it invalidates every issued token.
    /// </summary>
    Task<string?> GetSecurityStampAsync(Guid userId);

    /// <summary>Signs the user out of every device (new security stamp).</summary>
    Task<string?> RotateSecurityStampAsync(Guid userId);

    Task<UserFilterResult> GetUsersByFilterAsync(UserFilterParams filterParams);
    List<UserDto> GetUsersAsync();
    Task<UserDto?> GetUserByIdAsync(Guid id);
    Task<UserDto?> GetUserByPhoneAsync(string phoneNumber);
}

/// <param name="User">The signed-in user.</param>
/// <param name="Created">True when the account was created by this sign-in.</param>
public record PhoneSignInResult(UserDto User, bool Created);
