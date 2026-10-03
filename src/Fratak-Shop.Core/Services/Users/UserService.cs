using Fratak_Shop.Core.DTOs.Users.Command;
using Fratak_Shop.Core.DTOs.Users.Query;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Users;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Core.Services.Users;

public class UserService(FratakShopDbContext context) : IUserService
{
    public async Task<OperationResult> CreateUserAsync(User user)
    {
        if (user is null)
            return OperationResult.Error("کاربر نامعتبر است.");

        if (string.IsNullOrWhiteSpace(user.PhoneNumber))
            return OperationResult.Error("شماره موبایل را وارد کنید.");

        if (user.PhoneNumber.Length != 11)
            return OperationResult.Error("شماره موبایل باید 11 رقم باشد.");

        var exists = await context.Users.AnyAsync(x => x.PhoneNumber == user.PhoneNumber && !x.IsDeleted);
        if (exists)
            return OperationResult.Error("این شماره موبایل قبلاً ثبت شده است.");

        context.Users.Add(user);
        await context.SaveChangesAsync();

        return OperationResult.Success("کاربر با موفقیت ایجاد شد.");
    }

    public async Task<OperationResult> UpdateUserAsync(User user)
    {
        if (user is null)
            return OperationResult.Error("کاربر نامعتبر است.");

        var entity = await context.Users.AsTracking().FirstOrDefaultAsync(x => x.Id == user.Id && !x.IsDeleted);
        if (entity is null)
            return OperationResult.NotFound("کاربر مورد نظر یافت نشد.");

        if (string.IsNullOrWhiteSpace(user.PhoneNumber))
            return OperationResult.Error("شماره موبایل را وارد کنید.");

        var phoneExists = await context.Users
            .AnyAsync(x => x.Id != user.Id && x.PhoneNumber == user.PhoneNumber && !x.IsDeleted);
        if (phoneExists)
            return OperationResult.Error("این شماره موبایل قبلاً توسط کاربر دیگری ثبت شده است.");

        entity.PhoneNumber = user.PhoneNumber;
        entity.Email = user.Email;
        entity.PasswordHash = user.PasswordHash;
        entity.FirstName = user.FirstName;
        entity.LastName = user.LastName;
        entity.Role = user.Role;
        entity.IsPhoneVerified = user.IsPhoneVerified;
        entity.IsEmailVerified = user.IsEmailVerified;
        entity.LastLoginAt = user.LastLoginAt;

        await context.SaveChangesAsync();
        return OperationResult.Success("کاربر با موفقیت ویرایش شد.");
    }

    public async Task<OperationResult> DeleteUserAsync(Guid id)
    {
        var entity = await context.Users.AsTracking().FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
        if (entity is null)
            return OperationResult.NotFound("کاربر مورد نظر یافت نشد.");

        context.Users.Remove(entity);
        await context.SaveChangesAsync();

        return OperationResult.Success("کاربر با موفقیت حذف شد.");
    }

    public async Task<User?> GetUserByIdAsync(Guid id)
    {
        return await context.Users
            .Include(x => x.Addresses)
            .Include(x => x.Carts)
            .Include(x => x.Orders)
            .FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
    }

    public async Task<User?> GetUserByPhoneAsync(string phoneNumber)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
            return null;

        return await context.Users
            .FirstOrDefaultAsync(x => x.PhoneNumber == phoneNumber && !x.IsDeleted);
    }

    public async Task<List<User>> GetUsersAsync()
    {
        return await context.Users
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync();
    }
    // =============================================================== management

    public async Task<UserDto?> GetUserDtoAsync(Guid id)
    {
        var user = await context.Users
            .Include(item => item.RoleAssignments)
            .FirstOrDefaultAsync(item => item.Id == id);

        if (user is null)
            return null;

        return await MapAsync(user);
    }

    public async Task<UserProfileDto?> GetProfileAsync(Guid id)
    {
        var user = await context.Users
            .Include(item => item.Addresses)
            .FirstOrDefaultAsync(item => item.Id == id);

        if (user is null)
            return null;

        return new UserProfileDto
        {
            Id = user.Id,
            PhoneNumber = user.PhoneNumber,
            Email = user.Email,
            FirstName = user.FirstName,
            LastName = user.LastName,
            AvatarImageName = user.AvatarImageName,
            CreatedAt = user.CreatedAt,
            IsPhoneVerified = user.IsPhoneVerified,
            OrdersCount = await context.Orders.CountAsync(order => order.UserId == id),
            FavoritesCount = await context.Favorites.CountAsync(favorite => favorite.UserId == id),
            AddressesCount = user.Addresses.Count,
            UnreadNotifications = await context.Notifications.CountAsync(notification =>
                (notification.UserId == id || notification.IsBroadcast) && !notification.IsRead),
        };
    }

    public async Task<UserFilterResult> GetFilterAsync(UserFilterParams filterParams)
    {
        var query = context.Users
            .Include(user => user.RoleAssignments)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(filterParams.Search))
        {
            var term = filterParams.Search.Trim();
            query = query.Where(user => user.PhoneNumber.Contains(term) ||
                                        (user.FirstName != null && user.FirstName.Contains(term)) ||
                                        (user.LastName != null && user.LastName.Contains(term)) ||
                                        (user.Email != null && user.Email.Contains(term)));
        }

        if (filterParams.IsActive.HasValue)
            query = query.Where(user => user.IsActive == filterParams.IsActive.Value);

        if (filterParams.Role.HasValue)
        {
            var role = filterParams.Role.Value;
            query = query.Where(user => user.Role == role || user.RoleAssignments.Any(assignment => assignment.Role == role));
        }

        if (filterParams.StaffOnly)
        {
            query = query.Where(user => user.Role != UserRole.Customer && user.Role != UserRole.None);
        }

        var total = await query.CountAsync();
        var users = await query
            .OrderByDescending(user => user.CreatedAt)
            .Skip((filterParams.PageId - 1) * filterParams.Take)
            .Take(filterParams.Take)
            .ToListAsync();

        var items = new List<UserDto>();
        foreach (var user in users)
            items.Add(await MapAsync(user));

        var result = new UserFilterResult
        {
            Data = items,
            FilterParams = filterParams,
        };

        result.GeneratePaging(total, filterParams.Take, filterParams.PageId);
        return result;
    }

    public async Task<List<UserDto>> GetStaffAsync()
    {
        var users = await context.Users
            .Include(user => user.RoleAssignments)
            .Where(user => user.Role != UserRole.Customer && user.Role != UserRole.None)
            .OrderBy(user => user.Role)
            .ThenBy(user => user.FirstName)
            .ToListAsync();

        var result = new List<UserDto>();
        foreach (var user in users)
            result.Add(await MapAsync(user));

        return result;
    }

    public async Task<OperationResult> CreateStaffAsync(SaveStaffUserCommand command)
    {
        var phone = SlugHelper.ToLatinDigits(command.PhoneNumber).Trim();

        if (!phone.IsValidMobile())
            return OperationResult.Error("شماره موبایل باید با ۰۹ شروع شود و ۱۱ رقم باشد.");

        if (!string.IsNullOrWhiteSpace(command.Email) && !command.Email.Trim().IsValidEmail())
            return OperationResult.Error("ایمیل وارد‌شده معتبر نیست.");

        if (string.IsNullOrWhiteSpace(command.Password) || command.Password.Length < 6)
            return OperationResult.Error("رمز عبور باید حداقل ۶ کاراکتر باشد.");

        var exists = await context.Users.AnyAsync(user => user.PhoneNumber == phone);
        if (exists)
            return OperationResult.Error("این شماره موبایل قبلاً ثبت شده است.");

        var roles = NormalizeRoles(command.Roles, command.PrimaryRole);
        if (roles.Count == 0)
            return OperationResult.Error("حداقل یک نقش برای کاربر انتخاب کنید.");

        var user = new User
        {
            PhoneNumber = phone,
            Email = command.Email?.Trim(),
            FirstName = command.FirstName?.Trim(),
            LastName = command.LastName?.Trim(),
            PasswordHash = PasswordHasher.Hash(command.Password),
            IsActive = command.IsActive,
            IsPhoneVerified = true,
            Role = roles[0],
        };

        foreach (var role in roles.Skip(1))
        {
            user.RoleAssignments.Add(new UserRoleAssignment
            {
                UserId = user.Id,
                Role = role,
                GrantedAt = DateTimeOffset.UtcNow,
                GrantedByUserId = command.GrantedByUserId,
            });
        }

        context.Users.Add(user);
        await context.SaveChangesAsync();

        return OperationResult.Success("کاربر پنل با موفقیت ایجاد شد.");
    }

    public async Task<OperationResult> UpdateStaffAsync(SaveStaffUserCommand command)
    {
        if (!command.Id.HasValue)
            return await CreateStaffAsync(command);

        var user = await context.Users.AsTracking()
            .Include(item => item.RoleAssignments)
            .FirstOrDefaultAsync(item => item.Id == command.Id.Value);

        if (user is null)
            return OperationResult.NotFound("کاربر مورد نظر یافت نشد.");

        var phone = SlugHelper.ToLatinDigits(command.PhoneNumber).Trim();
        if (!phone.IsValidMobile())
            return OperationResult.Error("شماره موبایل باید با ۰۹ شروع شود و ۱۱ رقم باشد.");

        var duplicate = await context.Users.AnyAsync(item => item.Id != user.Id && item.PhoneNumber == phone);
        if (duplicate)
            return OperationResult.Error("این شماره موبایل برای کاربر دیگری ثبت شده است.");

        if (!string.IsNullOrWhiteSpace(command.Email) && !command.Email.Trim().IsValidEmail())
            return OperationResult.Error("ایمیل وارد‌شده معتبر نیست.");

        user.PhoneNumber = phone;
        user.Email = command.Email?.Trim();
        user.FirstName = command.FirstName?.Trim();
        user.LastName = command.LastName?.Trim();
        user.IsActive = command.IsActive;

        if (!string.IsNullOrWhiteSpace(command.Password))
        {
            if (command.Password.Length < 6)
                return OperationResult.Error("رمز عبور باید حداقل ۶ کاراکتر باشد.");

            user.PasswordHash = PasswordHasher.Hash(command.Password);
        }

        await SaveRolesAsync(user, command.Roles, command.PrimaryRole, command.GrantedByUserId);
        await context.SaveChangesAsync();

        return OperationResult.Success("اطلاعات کاربر پنل ذخیره شد.");
    }

    public async Task<OperationResult> SetRolesAsync(SetUserRolesCommand command)
    {
        var user = await context.Users.AsTracking()
            .Include(item => item.RoleAssignments)
            .FirstOrDefaultAsync(item => item.Id == command.UserId);

        if (user is null)
            return OperationResult.NotFound("کاربر مورد نظر یافت نشد.");

        await SaveRolesAsync(user, command.Roles, command.PrimaryRole, command.GrantedByUserId);
        await context.SaveChangesAsync();

        return OperationResult.Success("نقش‌های کاربر با موفقیت به‌روزرسانی شد.");
    }

    public async Task<OperationResult> UpdateProfileAsync(UpdateProfileCommand command)
    {
        var user = await context.Users.AsTracking().FirstOrDefaultAsync(item => item.Id == command.UserId);
        if (user is null)
            return OperationResult.NotFound("کاربر مورد نظر یافت نشد.");

        if (!string.IsNullOrWhiteSpace(command.Email) && !command.Email.Trim().IsValidEmail())
            return OperationResult.Error("ایمیل وارد‌شده معتبر نیست.");

        user.FirstName = command.FirstName?.Trim();
        user.LastName = command.LastName?.Trim();
        user.Email = command.Email?.Trim();

        if (!string.IsNullOrWhiteSpace(command.AvatarImageName))
            user.AvatarImageName = command.AvatarImageName;

        await context.SaveChangesAsync();
        return OperationResult.Success("اطلاعات پروفایل ذخیره شد.");
    }

    public async Task<OperationResult> UpdateAvatarAsync(Guid userId, string? avatarImageName)
    {
        var user = await context.Users.AsTracking().FirstOrDefaultAsync(item => item.Id == userId);
        if (user is null)
            return OperationResult.NotFound("کاربر مورد نظر یافت نشد.");

        user.AvatarImageName = avatarImageName;
        await context.SaveChangesAsync();

        return OperationResult.Success("تصویر پروفایل به‌روزرسانی شد.");
    }

    public async Task<OperationResult> SetActiveAsync(Guid userId, bool isActive)
    {
        var user = await context.Users.AsTracking().FirstOrDefaultAsync(item => item.Id == userId);
        if (user is null)
            return OperationResult.NotFound("کاربر مورد نظر یافت نشد.");

        if (!isActive && user.Role == UserRole.SuperAdmin)
            return OperationResult.Error("حساب مدیر ارشد قابل غیرفعال‌سازی نیست.");

        user.IsActive = isActive;
        await context.SaveChangesAsync();

        return OperationResult.Success(isActive ? "حساب کاربر فعال شد." : "حساب کاربر غیرفعال شد.");
    }

    /// <summary>
    /// ذخیره نقش‌ها: نقش اصلی در User.Role و نقش‌های دیگر به‌صورت رکورد در
    /// UserRoleAssignments نگه داشته می‌شوند تا یک نفر چند نقش هم‌زمان داشته باشد.
    /// </summary>
    private async Task SaveRolesAsync(User user, List<UserRole> roles, UserRole primaryRole, Guid? grantedByUserId)
    {
        var normalized = NormalizeRoles(roles, primaryRole);
        if (normalized.Count == 0)
            normalized.Add(UserRole.Customer);

        var primary = normalized.Contains(primaryRole) ? primaryRole : normalized[0];
        user.Role = primary;

        var existing = user.RoleAssignments.ToList();
        var desired = normalized.Where(role => role != primary).Distinct().ToList();

        var toRemove = existing.Where(assignment => !desired.Contains(assignment.Role)).ToList();
        if (toRemove.Count > 0)
            context.UserRoleAssignments.RemoveRange(toRemove);

        foreach (var role in desired.Where(role => existing.All(assignment => assignment.Role != role)))
        {
            context.UserRoleAssignments.Add(new UserRoleAssignment
            {
                UserId = user.Id,
                Role = role,
                GrantedAt = DateTimeOffset.UtcNow,
                GrantedByUserId = grantedByUserId,
            });
        }

        await Task.CompletedTask;
    }

    /// <summary>پاک‌سازی فهرست نقش‌ها: بدون تکرار، بدون None و همراه با نقش اصلی.</summary>
    private static List<UserRole> NormalizeRoles(IEnumerable<UserRole>? roles, UserRole primaryRole)
    {
        var normalized = (roles ?? [])
            .Where(role => role != UserRole.None)
            .Distinct()
            .ToList();

        if (primaryRole != UserRole.None && !normalized.Contains(primaryRole))
            normalized.Insert(0, primaryRole);

        return normalized;
    }

    private async Task<UserDto> MapAsync(User user)
    {
        var roles = new List<UserRole>();
        if (user.Role != UserRole.None)
            roles.Add(user.Role);

        roles.AddRange(user.RoleAssignments
            .Where(assignment => assignment.Role != UserRole.None && !roles.Contains(assignment.Role))
            .Select(assignment => assignment.Role));

        var paidOrders = await context.Orders
            .Where(order => order.UserId == user.Id && order.PaymentStatus == Data.Entities.Orders.PaymentStatus.Paid)
            .Select(order => order.PayableAmount)
            .ToListAsync();

        return new UserDto
        {
            Id = user.Id,
            CreatedAt = user.CreatedAt,
            UpdatedAt = user.UpdatedAt,
            PhoneNumber = user.PhoneNumber,
            Email = user.Email,
            FirstName = user.FirstName,
            LastName = user.LastName,
            AvatarImageName = user.AvatarImageName,
            Role = user.Role,
            Roles = roles,
            IsActive = user.IsActive,
            IsPhoneVerified = user.IsPhoneVerified,
            IsEmailVerified = user.IsEmailVerified,
            LastLoginAt = user.LastLoginAt,
            OrdersCount = await context.Orders.CountAsync(order => order.UserId == user.Id),
            AddressesCount = await context.Addresses.CountAsync(address => address.UserId == user.Id),
            FavoritesCount = await context.Favorites.CountAsync(favorite => favorite.UserId == user.Id),
            TotalPurchase = paidOrders.Sum(),
        };
    }
}