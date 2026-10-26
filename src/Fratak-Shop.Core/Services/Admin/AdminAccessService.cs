using Fratak_Shop.Core.DTOs.Admin;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Auditing;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Admin;
using Fratak_Shop.Data.Entities.Users;
using Fratak_Shop.Data.Utilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Fratak_Shop.Core.Services.Admin;

/// <summary>Staff roles, permissions and role membership of the admin panel.</summary>
public interface IAdminAccessService
{
    /// <summary>Permissions of a user (cached briefly, dropped on every role / membership change).</summary>
    Task<AdminAccess> GetAccessAsync(Guid userId);

    Task<List<AdminRoleDto>> GetRolesAsync();

    Task<AdminRoleDto?> GetRoleAsync(Guid id);

    /// <summary>Creates / updates a role. A staff member can only grant permissions they have themselves.</summary>
    Task<OperationResult<Guid>> SaveRoleAsync(SaveAdminRoleCommand command, AdminAccess actor);

    Task<OperationResult> DeleteRoleAsync(Guid id, AdminAccess actor);

    Task<List<StaffMemberDto>> GetStaffAsync();

    Task<List<AdminRoleRef>> GetUserRolesAsync(Guid userId);

    /// <summary>Replaces the roles of a user (not oneself, not the owner, no permission escalation).</summary>
    Task<OperationResult> SetUserRolesAsync(Guid userId, IReadOnlyCollection<Guid> roleIds, AdminAccess actor);

    /// <summary>Grants / revokes the store-owner flag (owners only; the last owner cannot be removed).</summary>
    Task<OperationResult> SetOwnerAsync(Guid userId, bool isOwner, AdminAccess actor);

    /// <summary>Creates the default roles once and moves legacy Admin / SeoManager users onto roles.</summary>
    Task EnsureDefaultsAsync(string? ownerPhoneNumber = null);
}

public class AdminAccessService(FratakShopDbContext context, IMemoryCache cache) : IAdminAccessService
{
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(5);
    private static long _localVersion;

    /// <summary>Changes whenever roles, permissions or memberships change (cache key part).</summary>
    private static long Version =>
        Interlocked.Read(ref _localVersion)
        + DataChangeNotifier.VersionOf(typeof(AdminRole))
        + DataChangeNotifier.VersionOf(typeof(AdminRolePermission))
        + DataChangeNotifier.VersionOf(typeof(UserAdminRole));

    /// <summary>Drops every cached access (e.g. after the owner flag or a block changed).</summary>
    public static void InvalidateAll() => Interlocked.Increment(ref _localVersion);

    public async Task<AdminAccess> GetAccessAsync(Guid userId)
    {
        var key = $"admin:access:{userId:N}:{Version}";
        if (cache.TryGetValue<AdminAccess>(key, out var cached) && cached is not null)
            return cached;

        var user = await context.Users
            .Where(u => u.Id == userId)
            .Select(u => new { u.Id, u.FirstName, u.LastName, u.PhoneNumber, u.Role, u.IsBlocked })
            .FirstOrDefaultAsync();

        AdminAccess access;
        if (user is null || user.IsBlocked)
        {
            access = AdminAccess.None(userId);
        }
        else
        {
            var roles = await context.UserAdminRoles
                .Where(m => m.UserId == userId)
                .Select(m => new { m.Role.Name, Permissions = m.Role.Permissions.Select(p => p.Permission).ToList() })
                .ToListAsync();

            var permissions = roles.SelectMany(r => r.Permissions).Where(AdminPermissions.IsKnown).ToHashSet(StringComparer.Ordinal);
            var name = $"{user.FirstName} {user.LastName}".Trim();
            access = new AdminAccess(user.Id, string.IsNullOrEmpty(name) ? user.PhoneNumber : name, user.PhoneNumber,
                user.Role == UserRole.SuperAdmin, permissions, roles.Select(r => r.Name).ToList());
        }

        cache.Set(key, access, new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = CacheLifetime, Size = 1 });
        return access;
    }

    public async Task<List<AdminRoleDto>> GetRolesAsync()
    {
        return await context.AdminRoles
            .OrderByDescending(r => r.IsSystem).ThenBy(r => r.Name)
            .Select(r => new AdminRoleDto
            {
                Id = r.Id,
                Name = r.Name,
                Description = r.Description,
                Color = r.Color,
                IsSystem = r.IsSystem,
                Permissions = r.Permissions.Select(p => p.Permission).ToList(),
                UserCount = r.Users.Count,
                UpdatedAt = r.UpdatedAt,
            })
            .ToListAsync();
    }

    public async Task<AdminRoleDto?> GetRoleAsync(Guid id) => (await GetRolesAsync()).FirstOrDefault(r => r.Id == id);

    public async Task<OperationResult<Guid>> SaveRoleAsync(SaveAdminRoleCommand command, AdminAccess actor)
    {
        var name = command.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name) || name.Length > 100)
            return OperationResult<Guid>.Error("نام نقش را (حداکثر ۱۰۰ نویسه) وارد کنید.");

        if (command.Description?.Length > 500)
            return OperationResult<Guid>.Error("توضیحات نقش حداکثر ۵۰۰ نویسه است.");

        var color = string.IsNullOrWhiteSpace(command.Color) ? null : command.Color.Trim();
        if (color is not null && !System.Text.RegularExpressions.Regex.IsMatch(color, "^#[0-9a-fA-F]{6}$"))
            return OperationResult<Guid>.Error("رنگ نقش معتبر نیست.");

        var permissions = command.Permissions.Where(AdminPermissions.IsKnown).Distinct(StringComparer.Ordinal).ToList();
        if (permissions.Count == 0)
            return OperationResult<Guid>.Error("حداقل یک دسترسی برای نقش انتخاب کنید.");

        if (!actor.IsOwner && permissions.Any(p => !actor.Permissions.Contains(p)))
            return OperationResult<Guid>.Error("نمی‌توانید دسترسی‌ای را به نقش بدهید که خودتان ندارید.");

        if (await context.AdminRoles.AnyAsync(r => r.Name == name && r.Id != command.Id))
            return OperationResult<Guid>.Error("نقشی با این نام وجود دارد.");

        AdminRole? role;
        if (command.Id is { } id)
        {
            role = await context.AdminRoles.AsTracking().Include(r => r.Permissions).FirstOrDefaultAsync(r => r.Id == id);
            if (role is null)
                return OperationResult<Guid>.NotFound("نقش مورد نظر یافت نشد.");

            // Editing a role that has permissions the actor lacks would let them strip or keep powers they cannot judge.
            if (!actor.IsOwner && role.Permissions.Any(p => !actor.Permissions.Contains(p.Permission)))
                return OperationResult<Guid>.Error("این نقش دسترسی‌هایی دارد که شما ندارید؛ فقط مالک فروشگاه می‌تواند آن را ویرایش کند.");

            if (!actor.IsOwner && await context.UserAdminRoles.AnyAsync(m => m.RoleId == id && m.UserId == actor.UserId))
                return OperationResult<Guid>.Error("نقش خودتان را نمی‌توانید ویرایش کنید.");
        }
        else
        {
            role = new AdminRole();
            context.AdminRoles.Add(role);
        }

        role.Name = name;
        role.Description = string.IsNullOrWhiteSpace(command.Description) ? null : command.Description.Trim();
        role.Color = color;

        foreach (var removed in role.Permissions.Where(p => !permissions.Contains(p.Permission)).ToList())
            context.AdminRolePermissions.Remove(removed);

        var existing = role.Permissions.Select(p => p.Permission).ToHashSet(StringComparer.Ordinal);
        foreach (var added in permissions.Where(p => !existing.Contains(p)))
            role.Permissions.Add(new AdminRolePermission { Permission = added });

        await context.SaveChangesAsync();
        InvalidateAll();
        return OperationResult<Guid>.Success(role.Id, "نقش ذخیره شد.");
    }

    public async Task<OperationResult> DeleteRoleAsync(Guid id, AdminAccess actor)
    {
        var role = await context.AdminRoles.AsTracking().Include(r => r.Permissions).Include(r => r.Users).FirstOrDefaultAsync(r => r.Id == id);
        if (role is null)
            return OperationResult.NotFound("نقش مورد نظر یافت نشد.");

        if (role.IsSystem)
            return OperationResult.Error("نقش‌های پیش‌فرض قابل حذف نیستند؛ می‌توانید دسترسی‌های آن را تغییر دهید.");

        if (!actor.IsOwner && role.Permissions.Any(p => !actor.Permissions.Contains(p.Permission)))
            return OperationResult.Error("این نقش دسترسی‌هایی دارد که شما ندارید.");

        context.UserAdminRoles.RemoveRange(role.Users);
        context.AdminRolePermissions.RemoveRange(role.Permissions);
        context.AdminRoles.Remove(role);
        await context.SaveChangesAsync();
        InvalidateAll();
        return OperationResult.Success($"نقش «{role.Name}» حذف شد.");
    }

    public async Task<List<StaffMemberDto>> GetStaffAsync()
    {
        var memberships = await context.UserAdminRoles
            .Select(m => new { m.UserId, Role = new AdminRoleRef(m.RoleId, m.Role.Name, m.Role.Color) })
            .ToListAsync();
        var ids = memberships.Select(m => m.UserId).ToHashSet();

        var users = await context.Users
            .Where(u => u.Role == UserRole.SuperAdmin || ids.Contains(u.Id))
            .Select(u => new { u.Id, u.FirstName, u.LastName, u.PhoneNumber, u.Role, u.IsBlocked, u.TwoFactorEnabled, TotpEnabled = u.TotpEnabledAt != null, u.LastLoginAt })
            .ToListAsync();

        return users
            .Select(u => new StaffMemberDto
            {
                UserId = u.Id,
                FullName = $"{u.FirstName} {u.LastName}".Trim() is { Length: > 0 } n ? n : u.PhoneNumber,
                PhoneNumber = u.PhoneNumber,
                IsOwner = u.Role == UserRole.SuperAdmin,
                IsBlocked = u.IsBlocked,
                TwoFactorEnabled = u.TwoFactorEnabled,
                TotpEnabled = u.TotpEnabled,
                LastLoginAt = u.LastLoginAt,
                Roles = memberships.Where(m => m.UserId == u.Id).Select(m => m.Role).ToList(),
            })
            .OrderByDescending(s => s.IsOwner).ThenBy(s => s.FullName)
            .ToList();
    }

    public async Task<List<AdminRoleRef>> GetUserRolesAsync(Guid userId) =>
        await context.UserAdminRoles.Where(m => m.UserId == userId)
            .Select(m => new AdminRoleRef(m.RoleId, m.Role.Name, m.Role.Color))
            .ToListAsync();

    public async Task<OperationResult> SetUserRolesAsync(Guid userId, IReadOnlyCollection<Guid> roleIds, AdminAccess actor)
    {
        if (userId == actor.UserId)
            return OperationResult.Error("نقش‌های خودتان را نمی‌توانید تغییر دهید.");

        var user = await context.Users.Where(u => u.Id == userId).Select(u => new { u.Role, u.IsBlocked }).FirstOrDefaultAsync();
        if (user is null)
            return OperationResult.NotFound("کاربر یافت نشد.");

        if (user.Role == UserRole.SuperAdmin && !actor.IsOwner)
            return OperationResult.Error("نقش‌های مالک فروشگاه فقط توسط مالک قابل تغییر است.");

        var wanted = roleIds.Distinct().ToList();
        var roles = await context.AdminRoles.Where(r => wanted.Contains(r.Id))
            .Select(r => new { r.Id, Permissions = r.Permissions.Select(p => p.Permission).ToList() })
            .ToListAsync();
        if (roles.Count != wanted.Count)
            return OperationResult.Error("یکی از نقش‌های انتخاب‌شده یافت نشد.");

        var current = await context.UserAdminRoles.AsTracking().Where(m => m.UserId == userId).ToListAsync();
        var touched = current.Select(m => m.RoleId).Except(wanted).Concat(wanted.Except(current.Select(m => m.RoleId))).ToHashSet();

        if (!actor.IsOwner)
        {
            // Every role added or removed must be within the actor's own permissions.
            var touchedPermissions = await context.AdminRolePermissions.Where(p => touched.Contains(p.RoleId)).Select(p => p.Permission).ToListAsync();
            if (touchedPermissions.Any(p => !actor.Permissions.Contains(p)))
                return OperationResult.Error("نمی‌توانید نقشی را بدهید یا بگیرید که دسترسی‌هایی فراتر از دسترسی خودتان دارد.");
        }

        foreach (var membership in current.Where(m => !wanted.Contains(m.RoleId)))
            context.UserAdminRoles.Remove(membership);

        foreach (var roleId in wanted.Where(r => current.All(m => m.RoleId != r)))
            context.UserAdminRoles.Add(new UserAdminRole { UserId = userId, RoleId = roleId });

        if (touched.Count > 0)
        {
            context.SecurityLogs.Add(new SecurityLog
            {
                Type = SecurityEventType.RolesChanged,
                UserId = userId,
                Details = $"نقش‌ها توسط {actor.DisplayName} تغییر کرد ({wanted.Count} نقش).",
                IpAddress = AuditContext.Current?.IpAddress,
            });
        }

        await context.SaveChangesAsync();
        InvalidateAll();
        return OperationResult.Success("نقش‌های کاربر ذخیره شد.");
    }

    public async Task<OperationResult> SetOwnerAsync(Guid userId, bool isOwner, AdminAccess actor)
    {
        if (!actor.IsOwner)
            return OperationResult.Error("فقط مالک فروشگاه می‌تواند مالک جدید تعیین کند.");

        var user = await context.Users.AsTracking().FirstOrDefaultAsync(u => u.Id == userId);
        if (user is null)
            return OperationResult.NotFound("کاربر یافت نشد.");

        if (!isOwner && user.Role == UserRole.SuperAdmin
                     && await context.Users.CountAsync(u => u.Role == UserRole.SuperAdmin) <= 1)
            return OperationResult.Error("فروشگاه باید حداقل یک مالک داشته باشد.");

        if (isOwner && user.IsBlocked)
            return OperationResult.Error("کاربر مسدود است.");

        user.Role = isOwner ? UserRole.SuperAdmin : UserRole.Customer;
        await context.SaveChangesAsync();
        InvalidateAll();
        return OperationResult.Success(isOwner ? "کاربر به مالک فروشگاه تبدیل شد." : "دسترسی مالکیت برداشته شد.");
    }

    public async Task EnsureDefaultsAsync(string? ownerPhoneNumber = null)
    {
        if (!await context.AdminRoles.IgnoreQueryFilters().AnyAsync())
        {
            foreach (var (name, description, color, permissions) in AdminPermissions.DefaultRoles)
            {
                context.AdminRoles.Add(new AdminRole
                {
                    Name = name,
                    Description = description,
                    Color = color,
                    IsSystem = true,
                    Permissions = permissions.Select(p => new AdminRolePermission { Permission = p }).ToList(),
                });
            }

            await context.SaveChangesAsync();
        }

        // New permissions added to the catalog later are granted to the full-access default role.
        var fullRole = await context.AdminRoles.AsTracking().Include(r => r.Permissions)
            .FirstOrDefaultAsync(r => r.IsSystem && r.Name == AdminPermissions.DefaultRoles[0].Name);
        if (fullRole is not null)
        {
            var missing = AdminPermissions.All.Select(p => p.Key).Except(fullRole.Permissions.Select(p => p.Permission)).ToList();
            foreach (var key in missing)
                fullRole.Permissions.Add(new AdminRolePermission { Permission = key });
            if (missing.Count > 0)
                await context.SaveChangesAsync();
        }

        // Legacy enum roles (before dynamic roles): Admin → full role, SeoManager → SEO role.
        var legacy = await context.Users.AsTracking().Where(u => u.Role == UserRole.Admin || u.Role == UserRole.SeoManager).ToListAsync();
        if (legacy.Count > 0)
        {
            var roles = await context.AdminRoles.Where(r => r.IsSystem).ToDictionaryAsync(r => r.Name, r => r.Id);
            foreach (var user in legacy)
            {
                var roleName = user.Role == UserRole.Admin ? AdminPermissions.DefaultRoles[0].Name : "کارشناس سئو";
                if (roles.TryGetValue(roleName, out var roleId)
                    && !await context.UserAdminRoles.AnyAsync(m => m.UserId == user.Id && m.RoleId == roleId))
                    context.UserAdminRoles.Add(new UserAdminRole { UserId = user.Id, RoleId = roleId });
                user.Role = UserRole.Customer;
            }

            await context.SaveChangesAsync();
        }

        // Bootstrap: the configured phone number becomes the store owner (first installation).
        var phone = ownerPhoneNumber?.Trim();
        if (!string.IsNullOrEmpty(phone))
        {
            var owner = await context.Users.AsTracking().FirstOrDefaultAsync(u => u.PhoneNumber == phone);
            if (owner is not null && owner.Role != UserRole.SuperAdmin && !owner.IsBlocked)
            {
                owner.Role = UserRole.SuperAdmin;
                await context.SaveChangesAsync();
            }
        }

        InvalidateAll();
    }
}
