using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Fratak_Shop.Data.Entities.Locations;
using Fratak_Shop.Data.Utilities;

namespace Fratak_Shop.Data.Entities.Users;

public class UserAddress : BaseEntity
{
    public Guid UserId { get; set; }

    public Guid ProvinceId { get; set; }

    public Guid CityId { get; set; }

    [Required]
    [MaxLength(100)]
    public string Title { get; set; } = null!;

    [MaxLength(100)]
    public string? FirstName { get; set; }

    [MaxLength(100)]
    public string? LastName { get; set; }

    [Required]
    [MaxLength(500)]
    public string AddressLine { get; set; } = null!;

    [MaxLength(50)]
    public string? BuildingNumber { get; set; }

    [MaxLength(50)]
    public string? Unit { get; set; }

    [Required]
    [MaxLength(10)]
    [MinLength(10)]
    public string PostalCode { get; set; } = null!;

    [Required]
    [MaxLength(11)]
    [MinLength(11)]
    public string PhoneNumber { get; set; } = null!;

    public bool IsDefault { get; set; }

    [ForeignKey(nameof(UserId))] 
    public User User { get; set; } = null!;

    [ForeignKey(nameof(ProvinceId))]
    public Location Province { get; set; } = null!;

    [ForeignKey(nameof(CityId))]
    public Location City { get; set; } = null!;
}
