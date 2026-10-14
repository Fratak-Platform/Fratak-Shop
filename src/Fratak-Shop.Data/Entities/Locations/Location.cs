using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Fratak_Shop.Data.Utilities;

namespace Fratak_Shop.Data.Entities.Locations;

/// <summary>
/// Hierarchical location dictionary: Country → Province → City.
/// The tree is linked through <see cref="ParentId"/> so every level
/// (including future levels) can be stored in one table.
/// </summary>
public class Location : BaseEntity
{
    public Guid? ParentId { get; set; }
    public LocationType Type { get; set; }

    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = null!;

    [Required]
    [MaxLength(50)]
    public string Code { get; set; } = null!;
    public bool IsActive { get; set; } = true;

    [ForeignKey(nameof(ParentId))]
    public Location? Parent { get; set; }

    public List<Location> Children { get; set; } = [];
}

public enum LocationType
{
    None = 0,
    Country = 1,
    Province = 2,
    City = 3,
}
