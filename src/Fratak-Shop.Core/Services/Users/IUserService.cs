using Fratak_Shop.Core.DTOs.Users.Command;
using Fratak_Shop.Core.DTOs.Users.Query;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Entities.Users;

namespace Fratak_Shop.Core.Services.Users;

public interface IUserService
{
    Task<OperationResult> CreateUserAsync(User user);
    Task<OperationResult> UpdateUserAsync(User user);
    Task<OperationResult> DeleteUserAsync(Guid id);
    Task<User?> GetUserByIdAsync(Guid id);
    Task<User?> GetUserByPhoneAsync(string phoneNumber);
    Task<List<User>> GetUsersAsync();

    // ------------------------------------------------------------- management
    Task<UserDto?> GetUserDtoAsync(Guid id);

    /// <summary>خلاصه پروفایل کاربر برای پنل کاربری.</summary>
    Task<UserProfileDto?> GetProfileAsync(Guid id);

    Task<UserFilterResult> GetFilterAsync(UserFilterParams filterParams);

    Task<List<UserDto>> GetStaffAsync();

    /// <summary>ثبت عضو جدید پنل همراه با نقش‌های چندگانه.</summary>
    Task<OperationResult> CreateStaffAsync(SaveStaffUserCommand command);

    /// <summary>ویرایش عضو پنل و نقش‌های او.</summary>
    Task<OperationResult> UpdateStaffAsync(SaveStaffUserCommand command);

    /// <summary>تعیین نقش اصلی و نقش‌های اضافی یک کاربر (چند نقش هم‌زمان).</summary>
    Task<OperationResult> SetRolesAsync(SetUserRolesCommand command);

    Task<OperationResult> UpdateProfileAsync(UpdateProfileCommand command);

    Task<OperationResult> UpdateAvatarAsync(Guid userId, string? avatarImageName);

    Task<OperationResult> SetActiveAsync(Guid userId, bool isActive);
}
