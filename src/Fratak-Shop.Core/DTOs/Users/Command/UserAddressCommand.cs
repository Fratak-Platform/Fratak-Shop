namespace Fratak_Shop.Core.DTOs.Users.Command;

/// <summary>
/// دستور ثبت آدرس کاربر.
/// </summary>
public class CreateUserAddressCommand
{
    public Guid UserId { get; set; }
    public Guid ProvinceId { get; set; }
    public Guid CityId { get; set; }

    public string Title { get; set; } = null!;
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string AddressLine { get; set; } = null!;
    public string? BuildingNumber { get; set; }
    public string? Unit { get; set; }
    public string PostalCode { get; set; } = null!;
    public string PhoneNumber { get; set; } = null!;

    public bool IsDefault { get; set; }
}

/// <summary>
/// دستور ویرایش آدرس کاربر.
/// </summary>
public class EditUserAddressCommand
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid ProvinceId { get; set; }
    public Guid CityId { get; set; }

    public string Title { get; set; } = null!;
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string AddressLine { get; set; } = null!;
    public string? BuildingNumber { get; set; }
    public string? Unit { get; set; }
    public string PostalCode { get; set; } = null!;
    public string PhoneNumber { get; set; } = null!;

    public bool IsDefault { get; set; }
}
