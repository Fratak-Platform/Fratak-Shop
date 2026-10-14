using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Entities.Users;

namespace Fratak_Shop.Core.DTOs.Users.Query;

/// <summary>
/// اطلاعات کاربر برای نمایش در پروفایل و پنل مدیریت (بدون رمز عبور).
/// </summary>
public class UserDto : BaseDto
{
    public string PhoneNumber { get; set; } = null!;
    public string? Email { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public UserRole Role { get; set; }
    public bool IsPhoneVerified { get; set; }
    public bool IsEmailVerified { get; set; }
    public DateTimeOffset? LastLoginAt { get; set; }

    public string? FullName => string.IsNullOrWhiteSpace(FirstName) && string.IsNullOrWhiteSpace(LastName)
        ? null
        : $"{FirstName} {LastName}".Trim();

    public List<UserAddressDto> Addresses { get; set; } = [];
}

/// <summary>
/// آدرس کاربر همراه نام استان و شهر.
/// </summary>
public class UserAddressDto : BaseDto
{
    public Guid UserId { get; set; }
    public Guid ProvinceId { get; set; }
    public Guid CityId { get; set; }

    public string? ProvinceName { get; set; }
    public string? CityName { get; set; }

    public string Title { get; set; } = null!;
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string AddressLine { get; set; } = null!;
    public string? BuildingNumber { get; set; }
    public string? Unit { get; set; }
    public string PostalCode { get; set; } = null!;
    public string PhoneNumber { get; set; } = null!;
    public bool IsDefault { get; set; }

    /// <summary>آدرس کامل به‌صورت یک متن.</summary>
    public string FullAddress =>
        $"{ProvinceName} - {CityName} - {AddressLine} {BuildingNumber} {Unit}".Trim();
}
