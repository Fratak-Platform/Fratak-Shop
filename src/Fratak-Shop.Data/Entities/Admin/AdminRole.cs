using System.ComponentModel.DataAnnotations;
using Fratak_Shop.Data.Entities.Users;
using Fratak_Shop.Data.Utilities;

namespace Fratak_Shop.Data.Entities.Admin;

/// <summary>
/// A staff role of the admin panel (e.g. "پشتیبانی", "کارشناس سئو"). Roles are created in the panel and
/// get any set of permissions from the code catalog; users get any number of roles. The store owner
/// (<see cref="UserRole.SuperAdmin"/>) always has every permission and needs no role.
/// </summary>
public class AdminRole : BaseEntity
{
    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = null!;

    [MaxLength(500)]
    public string? Description { get; set; }

    /// <summary>Seeded default role: can be edited but not deleted.</summary>
    public bool IsSystem { get; set; }

    /// <summary>Badge color in the panel (#hex).</summary>
    [MaxLength(9)]
    public string? Color { get; set; }

    public List<AdminRolePermission> Permissions { get; set; } = [];

    public List<UserAdminRole> Users { get; set; } = [];
}

/// <summary>One permission key (see the permission catalog in Core) granted to a role.</summary>
public class AdminRolePermission : BaseEntity
{
    public Guid RoleId { get; set; }

    [Required]
    [MaxLength(100)]
    public string Permission { get; set; } = null!;

    public AdminRole Role { get; set; } = null!;
}

/// <summary>Role membership of a staff user.</summary>
public class UserAdminRole : BaseEntity
{
    public Guid UserId { get; set; }

    public Guid RoleId { get; set; }

    public User User { get; set; } = null!;

    public AdminRole Role { get; set; } = null!;
}
