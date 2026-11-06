using Fratak_Shop.Core.DTOs.Users.Query;
using Fratak_Shop.Data.Entities.Users;

namespace Fratak_Shop.Core.Mappers.Users;

public static class UserAddressMapper
{
    public static UserAddressDto Map(this UserAddress address)
    {
        return new UserAddressDto
        {
            Id = address.Id,
            CreatedAt = address.CreatedAt,
            UpdatedAt = address.UpdatedAt,
            IsDeleted = address.IsDeleted,
            DeletedAt = address.DeletedAt,
            UserId = address.UserId,
            ProvinceId = address.ProvinceId,
            CityId = address.CityId,
            ProvinceName = address.Province?.Name,
            CityName = address.City?.Name,
            Title = address.Title,
            FirstName = address.FirstName,
            LastName = address.LastName,
            AddressLine = address.AddressLine,
            BuildingNumber = address.BuildingNumber,
            Unit = address.Unit,
            PostalCode = address.PostalCode,
            PhoneNumber = address.PhoneNumber,
            IsDefault = address.IsDefault,
            Latitude = address.Latitude,
            Longitude = address.Longitude,
        };
    }

    public static List<UserAddressDto> Map(this List<UserAddress> addresses)
    {
        return addresses.Select(address => address.Map()).ToList();
    }
}
