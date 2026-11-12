using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Fratak_Shop.Data.Entities.Users;
using Fratak_Shop.Data.Utilities;

namespace Fratak_Shop.Data.Entities.Notifications;

/// <summary>
/// Browser push subscription (Web Push / VAPID). Guests can subscribe too (broadcasts);
/// the subscription is linked to the customer once signed in.
/// </summary>
public class WebPushSubscription : BaseEntity
{
    public Guid? UserId { get; set; }

    /// <summary>Push service URL (only known push services are accepted).</summary>
    [Required]
    [MaxLength(800)]
    public string Endpoint { get; set; } = null!;

    /// <summary>SHA-256 of the endpoint (unique index; the URL itself is too long to index).</summary>
    [Required]
    [MaxLength(64)]
    public string EndpointHash { get; set; } = null!;

    /// <summary>Browser public key (base64url, P-256).</summary>
    [Required]
    [MaxLength(200)]
    public string P256dh { get; set; } = null!;

    /// <summary>Auth secret (base64url, 16 bytes).</summary>
    [Required]
    [MaxLength(100)]
    public string Auth { get; set; } = null!;

    [MaxLength(300)]
    public string? UserAgent { get; set; }

    public DateTimeOffset? LastSuccessAt { get; set; }

    public int FailureCount { get; set; }

    [ForeignKey(nameof(UserId))]
    public User? User { get; set; }
}
