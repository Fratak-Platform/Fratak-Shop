using System.ComponentModel.DataAnnotations;
using Fratak_Shop.Data.Entities.Blogs;
using Fratak_Shop.Data.Entities.Comments;
using Fratak_Shop.Data.Entities.Favorites;
using Fratak_Shop.Data.Entities.Messages;
using Fratak_Shop.Data.Entities.Orders;
using Fratak_Shop.Data.Entities.Shopping;
using Fratak_Shop.Data.Utilities;

namespace Fratak_Shop.Data.Entities.Users;

public class User : BaseEntity
{

    [Required]
    [MaxLength(11)]
    [MinLength(11)]
    public string PhoneNumber { get; set; } = null!;

    [EmailAddress]
    [MaxLength(255)]
    public string? Email { get; set; }

    public string? PasswordHash { get; set; }

    [MaxLength(100)]
    public string? FirstName { get; set; }

    [MaxLength(100)]
    public string? LastName { get; set; }

    public UserRole Role { get; set; }

    public bool IsPhoneVerified { get; set; }

    public bool IsEmailVerified { get; set; }

    public DateTimeOffset? LastLoginAt { get; set; }

    public List<UserAddress> Addresses { get; set; } = [];

    public List<Cart> Carts { get; set; } = [];

    public List<Order> Orders { get; set; } = [];

    public List<Comment> Comments { get; set; } = [];

    public List<BlogPost> BlogPosts { get; set; } = [];

    public List<Message> Messages { get; set; } = [];

    public List<Favorite> Favorites { get; set; } = [];
}

public enum UserRole
{
    None = 0,
    SuperAdmin = 1,
    Admin = 2,
    SeoManager = 3,
    Customer = 4,
}
