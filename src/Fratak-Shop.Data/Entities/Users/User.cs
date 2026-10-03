using System.ComponentModel.DataAnnotations;
using Fratak_Shop.Data.Entities.Blogs;
using Fratak_Shop.Data.Entities.Comments;
using Fratak_Shop.Data.Entities.Favorites;
using Fratak_Shop.Data.Entities.Messages;
using Fratak_Shop.Data.Entities.Orders;
using Fratak_Shop.Data.Entities.Notifications;
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

    /// <summary>
    /// Additional roles of the user. Together with <see cref="Role"/> they build the
    /// effective role set, so a staff member can hold more than one role at a time.
    /// </summary>
    public List<UserRoleAssignment> RoleAssignments { get; set; } = [];

    /// <summary>
    /// Inactive users cannot sign in (used to suspend a staff or customer account).
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// File name of the profile picture stored on the server.
    /// </summary>
    [MaxLength(250)]
    public string? AvatarImageName { get; set; }

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

    public List<Notification> Notifications { get; set; } = [];
}

/// <summary>
/// Code-defined roles of the shop. A user holds one primary role
/// (<see cref="User.Role"/>) plus any number of extra roles through
/// <see cref="UserRoleAssignment"/>.
/// Existing numeric values are kept stable: new roles are only appended.
/// </summary>
public enum UserRole
{
    None = 0,
    SuperAdmin = 1,
    Admin = 2,
    SeoManager = 3,
    Customer = 4,
    ProductManager = 5,
    OrderManager = 6,
    ContentManager = 7,
    SupportAgent = 8,
}
