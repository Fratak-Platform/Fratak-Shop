namespace Fratak_Shop.Core.DTOs.Admin;

/// <summary>What a signed-in user may do in the admin panel (cached per user).</summary>
public sealed record AdminAccess(Guid UserId, string DisplayName, string PhoneNumber, bool IsOwner, IReadOnlySet<string> Permissions, IReadOnlyList<string> RoleNames)
{
    public bool IsStaff => IsOwner || Permissions.Count > 0;

    public bool Has(string permission) => IsOwner || Permissions.Contains(permission);

    public bool HasAny(params string[] permissions) => IsOwner || permissions.Any(Permissions.Contains);

    public static AdminAccess None(Guid userId) => new(userId, "", "", false, new HashSet<string>(), []);
}

public sealed class AdminRoleDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public string? Color { get; set; }
    public bool IsSystem { get; set; }
    public List<string> Permissions { get; set; } = [];
    public int UserCount { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class SaveAdminRoleCommand
{
    public Guid? Id { get; set; }
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public string? Color { get; set; }
    public List<string> Permissions { get; set; } = [];
}

public sealed class StaffMemberDto
{
    public Guid UserId { get; set; }
    public string FullName { get; set; } = null!;
    public string PhoneNumber { get; set; } = null!;
    public bool IsOwner { get; set; }
    public bool IsBlocked { get; set; }
    public bool TwoFactorEnabled { get; set; }

    /// <summary>برنامهٔ احراز هویت (TOTP) فعال است – فاز ۱۰.</summary>
    public bool TotpEnabled { get; set; }
    public DateTimeOffset? LastLoginAt { get; set; }
    public List<AdminRoleRef> Roles { get; set; } = [];
}

public sealed record AdminRoleRef(Guid Id, string Name, string? Color);
