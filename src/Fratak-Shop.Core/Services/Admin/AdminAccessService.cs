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

    /// <summary>
    /// Creates the default roles once, moves legacy Admin / SeoManager users onto roles and bootstraps the
    /// default admins from configuration (<c>Shop:DefaultAdmins</c>, any number of them).
    /// <para>مرحلهٔ پانزدهم — در <b>هر</b> اجرای برنامه، برای هر مدیر اصلی تنظیمات (مالک، شریک …):</para>
    /// <list type="number">
    /// <item>حسابش با کلید ثابت (<see cref="DefaultAdminAccount.StableKey"/>) پیدا می‌شود؛ اگر هنوز کلید نخورده، با شمارهٔ تنظیمات
    /// پیدا و کلید می‌خورد؛ اگر اصلاً نیست با رمز اولیه (Argon2id) ساخته می‌شود.</item>
    /// <item>نام، شماره و رمزی که خودش عوض کرده هرگز به مقدار اولیه برنمی‌گردد (فقط حساب بی‌رمز، هنگام اولین شناسایی، رمز اولیه می‌گیرد).</item>
    /// <item>حساب حذف‌شده برمی‌گردد، حساب مسدود باز می‌شود و دسترسی مالک/نقش‌ها دوباره داده می‌شود.</item>
    /// <item>کاربری که کلید مدیر اصلی داشت ولی دیگر در تنظیمات نیست، کاربر عادی می‌شود (فقط حفاظت برداشته می‌شود).</item>
    /// </list>
    /// <para>خروجی: یادداشت‌های قابل لاگ (بدون رمز و با شمارهٔ پوشیده)، مثل «ساخته شد 0901***1090» یا «بازگردانی شد …».</para>
    /// <para>مثال: <c>EnsureDefaultsAsync([new("09013181090", "محمدامین", "ناظری", "12345678")])</c> در اولین اجرا مالک را با رمز
    /// 12345678 می‌سازد؛ اجراهای بعدی فقط بودن و دسترسی‌اش را تضمین می‌کنند.</para>
    /// </summary>
    Task<IReadOnlyList<string>> EnsureDefaultsAsync(IReadOnlyList<DefaultAdminAccount>? defaultAdmins = null);
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
            .Select(u => new { u.Id, u.FirstName, u.LastName, u.PhoneNumber, u.Role, u.IsBlocked, u.TwoFactorEnabled, TotpEnabled = u.TotpEnabledAt != null, u.LastLoginAt, IsSystemAdmin = u.SystemAdminKey != null })
            .ToListAsync();

        return users
            .Select(u => new StaffMemberDto
            {
                UserId = u.Id,
                FullName = $"{u.FirstName} {u.LastName}".Trim() is { Length: > 0 } n ? n : u.PhoneNumber,
                PhoneNumber = u.PhoneNumber,
                IsOwner = u.Role == UserRole.SuperAdmin,
                IsBlocked = u.IsBlocked,
                IsSystemAdmin = u.IsSystemAdmin,
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

        // مرحلهٔ پانزدهم: مالکیت مدیر اصلی تنظیمات سرور (Shop:DefaultAdmins) برداشته نمی‌شود.
        if (!isOwner && user.SystemAdminKey is not null && user.Role == UserRole.SuperAdmin)
            return OperationResult.Error("این حساب «مدیر اصلی» تعریف‌شده در تنظیمات سرور است و دسترسی مالکیتش برداشته نمی‌شود.");

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

    public async Task<IReadOnlyList<string>> EnsureDefaultsAsync(IReadOnlyList<DefaultAdminAccount>? defaultAdmins = null)
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

        var notes = new List<string>();
        var configuredKeys = new HashSet<string>(StringComparer.Ordinal);
        var entries = new List<(DefaultAdminAccount Admin, string? Key)>();
        // مرحلهٔ هفدهم: Key تکراری/نامعتبر دیگر باعث نادیده ماندن مدیر نمی‌شود (کلید شماره + هشدار) – DefaultAdminAccount.Resolve.
        foreach (var resolved in DefaultAdminAccount.Resolve(defaultAdmins))
        {
            if (resolved.Note is not null)
                notes.Add(resolved.Note);
            if (resolved.Key is null)
                continue;
            configuredKeys.Add(resolved.Key);
            entries.Add((resolved.Admin, resolved.Key));
        }

        foreach (var (admin, key) in entries)
        {
            try
            {
                notes.Add(await EnsureDefaultAdminAsync(admin, key, configuredKeys));
            }
            catch (DbUpdateException)
            {
                // نمونهٔ دیگری از برنامه هم‌زمان همین حساب را ساخت (شماره و کلید یکتا هستند) یا شماره در اختیار حساب دیگری است؛
                // اجرای بعدی دوباره بررسی می‌کند.
                context.ChangeTracker.Clear();
                notes.Add("ذخیره نشد (هم‌زمانی یا شمارهٔ تکراری)، در اجرای بعدی دوباره بررسی می‌شود: " + Describe(admin));
            }
        }

        // فقط وقتی فهرست تنظیمات داده شده (Program.cs): مدیری که از تنظیمات برداشته شده، حفاظتش برداشته می‌شود
        // (حسابش و دسترسی‌هایش سر جایش می‌ماند؛ از این به بعد مثل بقیهٔ کارکنان از پنل مدیریت می‌شود).
        if (defaultAdmins is not null)
        {
            var keepKeys = configuredKeys.ToList();
            var released = await context.Users.IgnoreQueryFilters().AsTracking()
                .Where(u => u.SystemAdminKey != null && !keepKeys.Contains(u.SystemAdminKey))
                .ToListAsync();
            foreach (var user in released)
            {
                user.SystemAdminKey = null;
                user.SystemAdminPhone = null;
                notes.Add("دیگر در تنظیمات نیست؛ حفاظت مدیر اصلی برداشته شد: " + DefaultAdminAccount.Mask(user.PhoneNumber));
            }
            if (released.Count > 0)
                await context.SaveChangesAsync();
        }

        InvalidateAll();
        return notes;
    }

    /// <summary>متن امن یک مورد تنظیمات برای لاگ: Key یا شمارهٔ پوشیده، هرگز رمز.</summary>
    private static string Describe(DefaultAdminAccount admin)
        => !string.IsNullOrWhiteSpace(admin.Key) ? "Key=" + admin.Key.Trim() : DefaultAdminAccount.Mask(NormalizeOwnerPhone(admin.PhoneNumber) ?? "");

    /// <summary>
    /// یک مدیر اصلی را تضمین می‌کند و یک یادداشت بدون رمز برمی‌گرداند (برای لاگ راه‌اندازی).
    /// <para>ترتیب تصمیم: شماره/کلید نامعتبر ← نادیده؛ رمز نامعتبر (کمتر از ۸ یا بیشتر از ۱۰۰ نویسه) ← نادیده (حساب بی‌رمز
    /// نمی‌سازیم چون صاحب سرور رمز خواسته بود)؛ پیدا کردن با کلید ← با شماره (و کلید زدن) ← ساخت؛ سپس بازگردانی حذف،
    /// رفع مسدودی، مالکیت/نقش‌ها. نام، شماره و رمز موجود هرگز تغییر نمی‌کند.</para>
    /// <para>مثال: مالک شماره‌اش را از 09013181090 به 09350000000 عوض کرده ← با کلید «phone:09013181090» همان حساب پیدا می‌شود
    /// و حساب تازه‌ای با شمارهٔ قدیمی ساخته نمی‌شود.</para>
    /// </summary>
    private async Task<string> EnsureDefaultAdminAsync(DefaultAdminAccount admin, string? key, IReadOnlySet<string> configuredKeys)
    {
        var phone = NormalizeOwnerPhone(admin.PhoneNumber);
        if (phone is null)
            return "نادیده (شمارهٔ موبایل نامعتبر در Shop:DefaultAdmins).";
        var masked = DefaultAdminAccount.Mask(phone);
        if (key is null)
            return $"نادیده {masked}: Key نامعتبر است (فقط حروف و رقم لاتین، - _ . و حداکثر {DefaultAdminAccount.MaxKeyLength} نویسه).";

        var hasPassword = !string.IsNullOrEmpty(admin.Password);
        if (hasPassword && !DefaultAdminAccount.IsValidPassword(admin.Password))
            return $"نادیده {masked}: رمز اولیه باید {DefaultAdminAccount.MinPasswordLength} تا {DefaultAdminAccount.MaxPasswordLength} نویسه باشد.";

        // نقش‌های محدود (FullAccess = false) با نام؛ نام ناشناخته نادیده و گزارش می‌شود.
        var roleIds = new List<Guid>();
        var unknownRoles = new List<string>();
        if (!admin.FullAccess)
        {
            var wanted = (admin.Roles ?? Array.Empty<string>()).Where(r => !string.IsNullOrWhiteSpace(r)).Select(r => r.Trim()).Distinct().ToList();
            var known = await context.AdminRoles.Where(r => wanted.Contains(r.Name)).Select(r => new { r.Id, r.Name }).ToListAsync();
            roleIds.AddRange(known.Select(r => r.Id));
            unknownRoles.AddRange(wanted.Except(known.Select(r => r.Name)));
            if (roleIds.Count == 0)
                return $"نادیده {masked}: FullAccess خاموش است ولی هیچ نقش معتبری در Roles نیامده.";
        }

        var unknownNote = unknownRoles.Count > 0 ? " (نقش ناشناخته: " + string.Join("، ", unknownRoles) + ")" : "";
        var changes = new List<string>();

        // ۱) با کلید ثابت (حتی اگر حذف‌شده باشد).
        var user = await context.Users.IgnoreQueryFilters().AsTracking()
            .Where(u => u.SystemAdminKey == key)
            .OrderBy(u => u.IsDeleted).ThenBy(u => u.CreatedAt)
            .FirstOrDefaultAsync();

        var firstLink = false;

        // ۱-ب) مرحلهٔ هفدهم: صاحب سرور شمارهٔ این مدیر را در تنظیمات عوض کرده (مثلاً 09013181090 ← 09913181090)؟
        // SystemAdminPhone = آخرین شمارهٔ تنظیماتِ اعمال‌شده. فرق داشتن یعنی تنظیمات عوض شده (نه اینکه کاربر از پنل عوضش کرده).
        if (user is not null && (user.SystemAdminPhone ?? user.PhoneNumber) != phone)
        {
            var moved = await ApplyConfiguredPhoneAsync(user, key, phone, masked, admin.Password, configuredKeys, changes);
            if (moved.Error is not null)
                return moved.Error;
            if (moved.Replacement is not null)
            {
                user = moved.Replacement;
                firstLink = true;
            }
        }

        // ۲) هنوز کلید نخورده (نسخهٔ قبلی برنامه یا اولین اجرا): حسابِ همین شماره که مدیر اصلی دیگری در تنظیمات فعلی نیست.
        // مثال: Key تازه برای مالک گذاشته شد ← حسابش که کلید قدیمی «phone:…» داشت با کلید تازه ادامه می‌دهد.
        if (user is null)
        {
            var keys = configuredKeys.ToList();
            user = await context.Users.IgnoreQueryFilters().AsTracking()
                .Where(u => u.PhoneNumber == phone && (u.SystemAdminKey == null || !keys.Contains(u.SystemAdminKey)))
                .OrderBy(u => u.IsDeleted).ThenByDescending(u => u.UpdatedAt)
                .FirstOrDefaultAsync();
            if (user is not null)
            {
                user.SystemAdminKey = key;
                firstLink = true;
                changes.Add("به‌عنوان مدیر اصلی ثبت شد");
            }
        }

        // ۳) اصلاً نیست: ساخت با رمز اولیه.
        if (user is null)
        {
            if (await context.Users.AnyAsync(u => u.PhoneNumber == phone))
                return $"ساخته نشد {masked}: این شماره در اختیار مدیر اصلی دیگری در تنظیمات است.";

            var created = new User
            {
                PhoneNumber = phone,
                FirstName = Clip(admin.FirstName),
                LastName = Clip(admin.LastName),
                Role = admin.FullAccess ? UserRole.SuperAdmin : UserRole.Customer,
                PasswordHash = hasPassword ? PasswordHasher.Hash(admin.Password) : null,
                IsPhoneVerified = true,
                SecurityStamp = PasswordHasher.NewSecurityStamp(),
                SystemAdminKey = key,
                SystemAdminPhone = phone,
            };
            context.Users.Add(created);
            foreach (var roleId in roleIds)
                context.UserAdminRoles.Add(new UserAdminRole { User = created, RoleId = roleId });
            await context.SaveChangesAsync();
            return $"ساخته شد {masked}{(admin.FullAccess ? " (مالک، همهٔ دسترسی‌ها)" : $" ({roleIds.Count} نقش)")}{(hasPassword ? " با رمز اولیه" : " بدون رمز")}{unknownNote}.";
        }

        // شمارهٔ تنظیمات که از این به بعد «اعمال‌شده» حساب می‌شود (تغییر بعدی شماره از پنل دیگر برگردانده نمی‌شود).
        if (user.SystemAdminPhone != phone)
            user.SystemAdminPhone = phone;

        var current = DefaultAdminAccount.Mask(user.PhoneNumber);

        // حساب حذف‌شده برمی‌گردد؛ مگر اینکه کسی دیگر در این فاصله با همان شماره ثبت‌نام کرده باشد (شمارهٔ فعال یکتاست).
        if (user.IsDeleted)
        {
            if (await context.Users.AnyAsync(u => u.PhoneNumber == user.PhoneNumber && u.Id != user.Id))
                return $"بازگردانی نشد {current}: شمارهٔ این حساب حالا در اختیار حساب دیگری است؛ از پنل بررسی کنید.";
            user.IsDeleted = false;
            user.DeletedAt = null;
            changes.Add("حساب حذف‌شده بازگردانی شد");
        }

        if (user.IsBlocked)
        {
            user.IsBlocked = false;
            user.BlockedAt = null;
            user.BlockReason = null;
            changes.Add("مسدودی برداشته شد");
        }

        if (admin.FullAccess && user.Role != UserRole.SuperAdmin)
        {
            user.Role = UserRole.SuperAdmin;
            changes.Add("دسترسی مالک داده شد");
        }

        foreach (var roleId in roleIds)
        {
            if (!await context.UserAdminRoles.AnyAsync(m => m.UserId == user.Id && m.RoleId == roleId))
            {
                context.UserAdminRoles.Add(new UserAdminRole { UserId = user.Id, RoleId = roleId });
                changes.Add("نقش افزوده شد");
            }
        }

        // فقط هنگام اولین شناسایی و فقط اگر حساب هیچ رمزی ندارد (مثلاً با کد پیامکی ثبت‌نام کرده) رمز اولیه می‌گیرد؛
        // رمز انتخابی کاربر مقدس است و بعد از آن، برنامه هیچ‌وقت سراغ رمز نمی‌رود.
        if (firstLink && hasPassword && string.IsNullOrEmpty(user.PasswordHash))
        {
            user.PasswordHash = PasswordHasher.Hash(admin.Password);
            changes.Add("رمز اولیه تنظیم شد");
        }

        if (changes.Count == 0)
            return $"بدون تغییر {current}{unknownNote}.";
        await context.SaveChangesAsync();
        return $"به‌روز شد {current}: {string.Join("، ", changes.Distinct())}{unknownNote}.";
    }

    /// <summary>
    /// مرحلهٔ هفدهم: اعمال شمارهٔ تازهٔ تنظیمات برای مدیر اصلی‌ای که با کلید پیدا شده ولی شمارهٔ تنظیماتش عوض شده است.
    /// <para>سه حالت:</para>
    /// <list type="bullet">
    /// <item>هیچ حساب فعالی شمارهٔ تازه را ندارد ← شمارهٔ همین حساب عوض می‌شود (رمز و نام می‌مانند) و نشست‌های قبلی بسته می‌شوند.</item>
    /// <item>صاحب شمارهٔ تازه قبلاً ثبت‌نام کرده (مثلاً با کد پیامکی) ← همان حساب مدیر اصلی می‌شود. حساب قدیمی اگر هرگز استفاده
    /// نشده یا هنوز رمز اولیه را دارد (یعنی حسابی که برای شمارهٔ اشتباه ساخته شده بود) دسترسی پنلش را از دست می‌دهد تا با رمز
    /// عمومی ۱۲۳۴۵۶۷۸ کسی وارد پنل نشود؛ وگرنه فقط حفاظت «مدیر اصلی» از آن برداشته می‌شود و از پنل مدیریت می‌شود.</item>
    /// <item>شمارهٔ تازه مال مدیر اصلی دیگری در همین تنظیمات است ← تغییری نمی‌دهد و خطا برمی‌گرداند.</item>
    /// </list>
    /// <para>مثال: کلید «key:owner» با حساب 09013181090؛ تنظیمات حالا 09913181090 ← اگر 09913181090 حساب ندارد، شمارهٔ حساب
    /// 09913181090 می‌شود و مالک با همان رمز قبلی و شمارهٔ درست وارد می‌شود.</para>
    /// </summary>
    private async Task<(User? Replacement, string? Error)> ApplyConfiguredPhoneAsync(
        User user, string key, string phone, string masked, string? initialPassword, IReadOnlySet<string> configuredKeys, List<string> changes)
    {
        if (user.PhoneNumber == phone)
        {
            user.SystemAdminPhone = phone;
            return (null, null);
        }

        var owner = await context.Users.AsTracking()
            .Where(u => u.PhoneNumber == phone && u.Id != user.Id)
            .FirstOrDefaultAsync();

        if (owner is null)
        {
            var oldMasked = DefaultAdminAccount.Mask(user.PhoneNumber);
            user.PhoneNumber = phone;
            user.SystemAdminPhone = phone;
            user.IsPhoneVerified = true;
            user.SecurityStamp = PasswordHasher.NewSecurityStamp();
            changes.Add($"شماره طبق تنظیمات از {oldMasked} به {masked} اصلاح شد");
            return (null, null);
        }

        if (owner.SystemAdminKey is not null && configuredKeys.Contains(owner.SystemAdminKey))
            return (null, $"تداخل {masked}: این شماره مال مدیر اصلی دیگری در Shop:DefaultAdmins است؛ شماره‌ها را در تنظیمات بررسی کنید.");

        // صاحب واقعی شمارهٔ تازه حساب دارد: کلید به حساب او منتقل می‌شود (کلید یکتاست؛ اول از حساب قدیمی برداشته و ذخیره می‌شود).
        var oldPhone = DefaultAdminAccount.Mask(user.PhoneNumber);
        var neverUsed = user.LastLoginAt is null
                        || (!string.IsNullOrEmpty(initialPassword) && !string.IsNullOrEmpty(user.PasswordHash)
                            && PasswordHasher.Verify(initialPassword, user.PasswordHash));
        user.SystemAdminKey = null;
        user.SystemAdminPhone = null;
        if (neverUsed)
        {
            user.Role = UserRole.Customer;
            user.SecurityStamp = PasswordHasher.NewSecurityStamp();
            var memberships = await context.UserAdminRoles.AsTracking().Where(m => m.UserId == user.Id).ToListAsync();
            context.UserAdminRoles.RemoveRange(memberships);
            changes.Add($"حساب قبلی {oldPhone} (ساخته‌شده برای شمارهٔ قبلی تنظیمات و استفاده‌نشده) از پنل خارج شد");
        }
        else
        {
            changes.Add($"حفاظت مدیر اصلی از حساب قبلی {oldPhone} برداشته شد (دسترسی‌هایش از پنل قابل مدیریت است)");
        }
        await context.SaveChangesAsync();

        owner.SystemAdminKey = key;
        owner.SystemAdminPhone = phone;
        changes.Add($"حساب موجود {masked} مدیر اصلی شد");
        return (owner, null);
    }

    /// <summary>
    /// شمارهٔ مدیر پیش‌فرض از تنظیمات: رقم فارسی/عربی به لاتین، حذف فاصله و خط تیره؛ فقط قالب 09xxxxxxxxx پذیرفته می‌شود.
    /// مثال: «۰۹۰۱ ۳۱۸ ۱۰۹۰» ← «09013181090»؛ «+989013181090» ← «09013181090»؛ «123» ← null.
    /// </summary>
    public static string? NormalizeOwnerPhone(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var digits = new string(SearchText.NormalizeDigits(value).Where(char.IsAsciiDigit).ToArray());
        if (digits.StartsWith("989", StringComparison.Ordinal) && digits.Length == 12)
            digits = "0" + digits[2..];
        return digits.Length == 11 && digits.StartsWith("09", StringComparison.Ordinal) ? digits : null;
    }

    private static string? Clip(string? value)
    {
        var text = value?.Trim();
        return string.IsNullOrEmpty(text) ? null : text.Length <= 100 ? text : text[..100];
    }
}
