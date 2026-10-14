using Fratak_Shop.Core.DTOs.Users.Command;
using Fratak_Shop.Core.DTOs.Users.Query;
using Fratak_Shop.Core.Utilities;

namespace Fratak_Shop.Core.Services.Users;

public interface IUserService
{
    Task<OperationResult> CreateUserAsync(CreateUserCommand command);
    Task<OperationResult> EditUserAsync(EditUserCommand command);
    Task<OperationResult> DeleteUserAsync(Guid id);

    /// <summary>تغییر رمز عبور با بررسی رمز فعلی (کاربر بدون رمز، رمز فعلی ندارد).</summary>
    Task<OperationResult> ChangePasswordAsync(ChangePasswordCommand command);

    /// <summary>
    /// بررسی شماره موبایل و رمز عبور؛ در صورت درستی، کاربر را برمی‌گرداند و
    /// زمان آخرین ورود را ثبت می‌کند.
    /// </summary>
    Task<UserDto?> LoginAsync(LoginCommand command);

    Task<UserFilterResult> GetUsersByFilterAsync(UserFilterParams filterParams);
    List<UserDto> GetUsersAsync();
    Task<UserDto?> GetUserByIdAsync(Guid id);
    Task<UserDto?> GetUserByPhoneAsync(string phoneNumber);
}
