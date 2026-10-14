using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Entities.Users;

namespace Fratak_Shop.Core.DTOs.Users.Query;

/// <summary>
/// پارامترهای فیلتر و صفحه‌بندی لیست کاربران.
/// </summary>
public class UserFilterParams : BaseFilterParam
{
    /// <summary>جستجو در شماره موبایل، ایمیل، نام و نام خانوادگی.</summary>
    public string? Search { get; set; }

    public UserRole? Role { get; set; }

    public bool? IsPhoneVerified { get; set; }

    public bool? IsEmailVerified { get; set; }
}

public class UserFilterResult : BaseFilter<UserDto, UserFilterParams>;
