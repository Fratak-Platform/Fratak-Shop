using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Entities.Locations;

namespace Fratak_Shop.Core.DTOs.Locations.Query;

public class LocationDto : BaseDto
{
    public string Name { get; set; } = null!;
    public string Code { get; set; } = null!;
    public LocationType Type { get; set; }
    public bool IsActive { get; set; }
    public List<LocationProvinceDto> Province { get; set; } = [];
}

public class LocationProvinceDto : BaseDto
{
    public Guid ParentId { get; set; }
    public string Name { get; set; } = null!;
    public string Code { get; set; } = null!;
    public LocationType Type { get; set; }
    public bool IsActive { get; set; }
    public List<LocationCityDto> Cities { get; set; } = [];
}

public class LocationCityDto : BaseDto
{
    public Guid ParentId { get; set; }
    public string Name { get; set; } = null!;
    public string Code { get; set; } = null!;
    public LocationType Type { get; set; }
    public bool IsActive { get; set; }
}