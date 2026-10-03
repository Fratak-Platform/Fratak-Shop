using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Entities.Users;

namespace Fratak_Shop.Core.DTOs.Users.Query;

public class UserDto : BaseDto
{
    public string PhoneNumber { get; set; } = null!;
    public string? Email { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? AvatarImageName { get; set; }
    public UserRole Role { get; set; }
    public List<UserRole> Roles { get; set; } = [];
    public bool IsActive { get; set; }
    public bool IsPhoneVerified { get; set; }
    public bool IsEmailVerified { get; set; }
    public DateTimeOffset? LastLoginAt { get; set; }
    public int OrdersCount { get; set; }
    public int AddressesCount { get; set; }
    public int FavoritesCount { get; set; }
    public decimal TotalPurchase { get; set; }

    public string FullName => string.IsNullOrWhiteSpace($"{FirstName} {LastName}".Trim())
        ? PhoneNumber
        : $"{FirstName} {LastName}".Trim();

    public string RoleTitles => Roles.Count == 0
        ? "بدون نقش"
        : string.Join(" ، ", Roles.Select(role => role.GetTitle()));

    public bool IsStaff => Roles.Any(role => role.IsStaff());
}

public class UserFilterParams : BaseFilterParam
{
    public string? Search { get; set; }
    public UserRole? Role { get; set; }
    public bool? IsActive { get; set; }
    public bool StaffOnly { get; set; }
}

public class UserFilterResult : BaseFilter<UserDto, UserFilterParams>;

/// <summary>خلاصه پروفایل کاربر در پنل کاربری.</summary>
public class UserProfileDto
{
    public Guid Id { get; set; }
    public string PhoneNumber { get; set; } = null!;
    public string? Email { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? AvatarImageName { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public bool IsPhoneVerified { get; set; }
    public int OrdersCount { get; set; }
    public int FavoritesCount { get; set; }
    public int UnreadNotifications { get; set; }
    public int AddressesCount { get; set; }

    public string FullName => string.IsNullOrWhiteSpace($"{FirstName} {LastName}".Trim())
        ? PhoneNumber
        : $"{FirstName} {LastName}".Trim();
}
