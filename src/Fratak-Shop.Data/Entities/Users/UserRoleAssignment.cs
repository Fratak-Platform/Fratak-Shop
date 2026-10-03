using System.ComponentModel.DataAnnotations.Schema;
using Fratak_Shop.Data.Utilities;

namespace Fratak_Shop.Data.Entities.Users;

/// <summary>
/// Additional role granted to a user. Roles themselves are code-defined values of
/// <see cref="UserRole"/> (kept as an enum so no lookup table is required), while
/// this table allows one user to hold several roles at the same time
/// (for example product manager + blog editor).
/// The primary role of <see cref="User"/> stays in <c>User.Role</c>; the effective
/// role set is the union of both sources (<see cref="UserRoleExtensions"/>).
/// </summary>
public class UserRoleAssignment : BaseEntity
{
    public Guid UserId { get; set; }

    public UserRole Role { get; set; }

    /// <summary>
    /// Moment the role was granted to the user.
    /// </summary>
    public DateTimeOffset GrantedAt { get; set; }

    /// <summary>
    /// Administrator (super admin) who granted the role.
    /// </summary>
    public Guid? GrantedByUserId { get; set; }

    [ForeignKey(nameof(UserId))]
    public User User { get; set; } = null!;

    [ForeignKey(nameof(GrantedByUserId))]
    public User? GrantedByUser { get; set; }
}
