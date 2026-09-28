using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Fratak_Shop.Data.Utilities;

namespace Fratak_Shop.Data.Entities.Users;

public class UserAddress : BaseEntity
{
    public Guid UserId { get; set; }

    [MaxLength(50)] 
    public string Title { get; set; } = null!;

    [MaxLength(100)]
    public string? FirstName { get; set; }

    [MaxLength(100)]
    public string? LastName { get; set; }

    public Guid ProvinceId { get; set; } //TODO
    public Guid CityId { get; set; } //TODO

    [Required]
    [MaxLength(300)]
    public string AddressLine { get; set; } = null!;

    [Required]
    [MaxLength(10)]
    [MinLength(10)]
    public string PostalCode { get; set; } = null!;

    [Required]
    [MaxLength(11)]
    [MinLength(11)]
    public string PhoneNumber { get; set; } = null!;

    [ForeignKey(nameof(UserId))] 
    public User User { get; set; } = null!;

}