using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Fratak_Shop.Data.Utilities;

namespace Fratak_Shop.Data.Entities.Users;

/// <summary>
/// Browsers / devices a customer has signed in from. A sign-in from a device that is not in
/// this list triggers the "new device" security alert.
/// </summary>
public class UserKnownDevice : BaseEntity
{
    public Guid UserId { get; set; }

    /// <summary>SHA-256 of the normalized device signature (browser family + OS + device type).</summary>
    [Required]
    [MaxLength(64)]
    public string DeviceHash { get; set; } = null!;

    /// <summary>Readable name, e.g. "Chrome روی Windows".</summary>
    [Required]
    [MaxLength(150)]
    public string DeviceName { get; set; } = null!;

    [MaxLength(50)]
    public string? LastIp { get; set; }

    public DateTimeOffset LastSeenAt { get; set; }

    [ForeignKey(nameof(UserId))]
    public User User { get; set; } = null!;
}
