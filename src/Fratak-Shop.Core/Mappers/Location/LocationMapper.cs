using Fratak_Shop.Core.DTOs.Locations.Query;

namespace Fratak_Shop.Core.Mappers.Location;

public static class LocationMapper
{
    public static List<LocationDto> Map(this List<Data.Entities.Locations.Location> locations)
    {
        var locationsDto = new List<LocationDto>();
        locations.ForEach(a => a.Map());
        return locationsDto;
    }

    public static LocationDto Map(this Data.Entities.Locations.Location location)
    {
        return new LocationDto
        {
            Id = location.Id,
            CreatedAt = location.CreatedAt,
            UpdatedAt = location.UpdatedAt,
            IsDeleted = location.IsDeleted,
            DeletedAt = location.DeletedAt,
            Name = location.Name,
            Code = location.Code,
            Type = location.Type,
            IsActive = location.IsActive,
            Province = location.Children.Select(c => new LocationProvinceDto
            {
                Id = c.Id,
                CreatedAt = c.CreatedAt,
                UpdatedAt = c.UpdatedAt,
                IsDeleted = c.IsDeleted,
                DeletedAt = c.DeletedAt,
                ParentId = (Guid)c.ParentId!,
                Name = c.Name,
                Code = c.Code,
                Type = c.Type,
                IsActive = c.IsActive,
                Cities = c.Children.Select(cc => new LocationCityDto
                {
                    Id = cc.Id,
                    CreatedAt = cc.CreatedAt,
                    UpdatedAt = cc.UpdatedAt,
                    IsDeleted = cc.IsDeleted,
                    DeletedAt = cc.DeletedAt,
                    ParentId = (Guid)cc.ParentId!,
                    Name = cc.Name,
                    Code = cc.Code,
                    Type = cc.Type,
                    IsActive = cc.IsActive
                }).ToList()
            }).ToList()
        };
    }
}