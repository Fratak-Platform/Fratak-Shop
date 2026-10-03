using Fratak_Shop.Data.Entities.Locations;

namespace Fratak_Shop.Core.DTOs.Locations.Command;

public class CreateLocationCommand
{
    public Guid? ParentId { get; set; }
    public string Name { get; set; } = null!;
    public string Code { get; set; } = null!;
    public LocationType Type { get; set; }
    public bool IsActive { get; set; } = true;
}

public class EditLocationCommand
{
    public Guid Id { get; set; }
    public Guid? ParentId { get; set; }
    public string Name { get; set; } = null!;
    public string Code { get; set; } = null!;
    public LocationType Type { get; set; }
    public bool IsActive { get; set; } = true;
}
