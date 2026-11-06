using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Fratak_Shop.Data.Utilities;

namespace Fratak_Shop.Data.Entities.Users;

/// <summary>
/// Refresh token of a signed-in device. Only the SHA-256 hash of the token is
/// stored. Tokens rotate on every use; all tokens of one sign-in share a
/// <see cref="FamilyId"/>, so re-using a rotated token revokes the whole family
/// (token theft detection).
/// </summary>
public class UserRefreshToken : BaseEntity
{
    public Guid UserId { get; set; }

    [Required]
    [MaxLength(128)]
    public string TokenHash { get; set; } = null!;

    public Guid FamilyId { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>"Remember me": the browser cookie outlives the session.</summary>
    public bool IsPersistent { get; set; }

    [MaxLength(64)]
    public string? CreatedByIp { get; set; }

    [MaxLength(512)]
    public string? UserAgent { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }

    [MaxLength(100)]
    public string? RevokedReason { get; set; }

    public Guid? ReplacedByTokenId { get; set; }

    [ForeignKey(nameof(UserId))]
    public User User { get; set; } = null!;
}
