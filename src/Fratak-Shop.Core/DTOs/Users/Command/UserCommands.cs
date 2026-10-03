using Fratak_Shop.Data.Entities.Users;

namespace Fratak_Shop.Core.DTOs.Users.Command;

/// <summary>ثبت یا ویرایش عضو پنل با نقش‌های چندگانه.</summary>
public class SaveStaffUserCommand
{
    public Guid? Id { get; set; }
    public string PhoneNumber { get; set; } = null!;
    public string? Email { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }

    /// <summary>نقش اصلی کاربر؛ باید یکی از نقش‌های انتخاب‌شده باشد.</summary>
    public UserRole PrimaryRole { get; set; }

    /// <summary>همه نقش‌های کاربر (چند نقش هم‌زمان).</summary>
    public List<UserRole> Roles { get; set; } = [];

    /// <summary>رمز عبور اولیه؛ در ویرایش خالی گذاشتن یعنی تغییر نکند.</summary>
    public string? Password { get; set; }

    public bool IsActive { get; set; } = true;
    public Guid? GrantedByUserId { get; set; }
}

public class SetUserRolesCommand
{
    public Guid UserId { get; set; }
    public UserRole PrimaryRole { get; set; }
    public List<UserRole> Roles { get; set; } = [];
    public Guid? GrantedByUserId { get; set; }
}

public class UpdateProfileCommand
{
    public Guid UserId { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
    public string? AvatarImageName { get; set; }
}
