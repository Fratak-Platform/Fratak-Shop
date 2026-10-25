using System.ComponentModel.DataAnnotations;
using Fratak_Shop.Data.Entities.Blogs;
using Fratak_Shop.Data.Entities.Comments;
using Fratak_Shop.Data.Entities.Favorites;
using Fratak_Shop.Data.Entities.Loyalty;
using Fratak_Shop.Data.Entities.Messages;
using Fratak_Shop.Data.Entities.Orders;
using Fratak_Shop.Data.Entities.Shopping;
using Fratak_Shop.Data.Entities.Wallets;
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

    /// <summary>
    /// Random value that changes whenever the credentials change (password change,
    /// "sign out everywhere"); issued tokens carrying an old stamp are rejected.
    /// </summary>
    [MaxLength(64)]
    public string? SecurityStamp { get; set; }

    /// <summary>Consecutive failed password sign-ins (reset on success).</summary>
    public int FailedLoginCount { get; set; }

    /// <summary>Password sign-in is blocked until this moment (brute-force protection).</summary>
    public DateTimeOffset? LockoutEndAt { get; set; }

    /// <summary>Current wallet balance in Toman (ledger: <see cref="WalletTransactions"/>).</summary>
    public decimal WalletBalance { get; set; }

    /// <summary>
    /// Part of <see cref="WalletBalance"/> that came from promotions (club rewards,
    /// gifts, admin credits). It can only be spent on orders and is never withdrawable.
    /// </summary>
    public decimal WalletBonusBalance { get; set; }

    /// <summary>Birthday (customer club gift).</summary>
    public DateOnly? BirthDate { get; set; }

    /// <summary>Password sign-in additionally requires a one-time SMS code.</summary>
    public bool TwoFactorEnabled { get; set; }

    /// <summary>Current customer-club points (ledger: <see cref="LoyaltyTransactions"/>).</summary>
    public int LoyaltyPoints { get; set; }

    /// <summary>All points ever earned; decides the club level.</summary>
    public int LoyaltyLifetimePoints { get; set; }

    /// <summary>Personal invite code (customer club referral).</summary>
    [MaxLength(20)]
    public string? ReferralCode { get; set; }

    /// <summary>The customer who invited this user.</summary>
    public Guid? ReferredByUserId { get; set; }

    /// <summary>Blocked by the shop: cannot sign in, existing sessions are revoked.</summary>
    public bool IsBlocked { get; set; }

    public DateTimeOffset? BlockedAt { get; set; }

    [MaxLength(500)]
    public string? BlockReason { get; set; }

    /// <summary>Internal note of the staff about this customer.</summary>
    [MaxLength(2000)]
    public string? AdminNote { get; set; }

    public List<UserAddress> Addresses { get; set; } = [];

    public List<Cart> Carts { get; set; } = [];

    public List<Order> Orders { get; set; } = [];

    public List<Comment> Comments { get; set; } = [];

    public List<BlogPost> BlogPosts { get; set; } = [];

    public List<Message> Messages { get; set; } = [];

    public List<Favorite> Favorites { get; set; } = [];

    public List<WalletTransaction> WalletTransactions { get; set; } = [];

    public List<WithdrawalRequest> WithdrawalRequests { get; set; } = [];

    public List<UserRefreshToken> RefreshTokens { get; set; } = [];

    public List<LoyaltyTransaction> LoyaltyTransactions { get; set; } = [];
}

public enum UserRole
{
    None = 0,
    SuperAdmin = 1,
    Admin = 2,
    SeoManager = 3,
    Customer = 4,
}
