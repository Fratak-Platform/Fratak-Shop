using System.ComponentModel.DataAnnotations;
using Fratak_Shop.Data.Utilities;

namespace Fratak_Shop.Data.Entities.Users;

public class User : BaseEntity
{

    [Required]
    [MaxLength(11)]
    [MinLength(11)]
    public string PhoneNumber { get; set; } = null!;

    [EmailAddress]
    [MaxLength(100)]
    public string? Email { get; set; }

    public string? PasswordHash { get; set; }

    [MaxLength(100)]
    public string? FirstName { get; set; }

    [MaxLength(100)]
    public string? LastName { get; set; }

    public UserRole Role { get; set; }

    public bool IsPhoneVerified { get; set; }

    public bool IsEmailVerified { get; set; }

    public DateTimeOffset LastLoginAt { get; set; }
}

public enum UserRole
{
    None = 0,
    SuperAdmin = 1,
}