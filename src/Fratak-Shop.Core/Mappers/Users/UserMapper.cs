using Fratak_Shop.Core.DTOs.Users.Query;
using Fratak_Shop.Data.Entities.Users;

namespace Fratak_Shop.Core.Mappers.Users;

public static class UserMapper
{
    public static UserDto Map(this User user)
    {
        return new UserDto
        {
            Id = user.Id,
            CreatedAt = user.CreatedAt,
            UpdatedAt = user.UpdatedAt,
            IsDeleted = user.IsDeleted,
            DeletedAt = user.DeletedAt,
            PhoneNumber = user.PhoneNumber,
            Email = user.Email,
            FirstName = user.FirstName,
            LastName = user.LastName,
            Role = user.Role,
            IsPhoneVerified = user.IsPhoneVerified,
            IsEmailVerified = user.IsEmailVerified,
            LastLoginAt = user.LastLoginAt,
            WalletBalance = user.WalletBalance,
            HasPassword = !string.IsNullOrWhiteSpace(user.PasswordHash),
            TwoFactorEnabled = user.TwoFactorEnabled,
            BirthDate = user.BirthDate,
            LoyaltyPoints = user.LoyaltyPoints,
            ReferralCode = user.ReferralCode,
            Addresses = user.Addresses.Select(address => address.Map()).ToList(),
        };
    }

    public static List<UserDto> Map(this List<User> users)
    {
        return users.Select(user => user.Map()).ToList();
    }

    /// <summary>
    /// نام نمایشی کاربر برای نویسندگی مقاله، پاسخ پیام‌ها و ... .
    /// اگر نام و نام خانوادگی ثبت نشده باشد، شماره موبایل برگردانده می‌شود.
    /// </summary>
    public static string? DisplayName(this User? user)
    {
        if (user is null)
            return null;

        var fullName = $"{user.FirstName} {user.LastName}".Trim();

        if (!string.IsNullOrWhiteSpace(fullName))
            return fullName;

        return string.IsNullOrWhiteSpace(user.PhoneNumber) ? null : user.PhoneNumber;
    }
}
