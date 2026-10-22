using Fratak_Shop.Core.DTOs.Users.Command;
using Fratak_Shop.Core.DTOs.Users.Query;
using Fratak_Shop.Core.Mappers.Users;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Users;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Core.Services.Users;

/// <summary>
/// مدیریت کاربران: ثبت‌نام و ورود (Argon2id)، ویرایش اطلاعات، تغییر رمز عبور و
/// لیست کاربران. رمز عبور هیچ‌گاه به‌صورت متن ساده ذخیره یا برگردانده نمی‌شود.
/// </summary>
public class UserService(FratakShopDbContext context) : IUserService
{
    private const int MinimumPasswordLength = 6;

    public async Task<OperationResult> CreateUserAsync(CreateUserCommand command)
    {
        var error = Validate(command.PhoneNumber, command.Email, command.Role);
        if (error is not null)
            return OperationResult.Error(error);

        if (!string.IsNullOrWhiteSpace(command.Password) && command.Password.Trim().Length < MinimumPasswordLength)
            return OperationResult.Error($"رمز عبور باید حداقل {MinimumPasswordLength} کاراکتر باشد.");

        var phoneNumber = command.PhoneNumber.Trim();

        var exists = await context.Users.AnyAsync(x => x.PhoneNumber == phoneNumber);
        if (exists)
            return OperationResult.Error("این شماره موبایل قبلاً ثبت شده است.");

        var user = new User
        {
            PhoneNumber = phoneNumber,
            Email = command.Email?.Trim(),
            PasswordHash = string.IsNullOrWhiteSpace(command.Password)
                ? null
                : PasswordHasher.Hash(command.Password),
            FirstName = command.FirstName?.Trim(),
            LastName = command.LastName?.Trim(),
            Role = command.Role,
            IsPhoneVerified = command.IsPhoneVerified,
            IsEmailVerified = command.IsEmailVerified,
        };

        context.Users.Add(user);
        await context.SaveChangesAsync();

        return OperationResult.Success("کاربر با موفقیت ایجاد شد.");
    }

    public async Task<OperationResult> EditUserAsync(EditUserCommand command)
    {
        var user = await context.Users.AsTracking().FirstOrDefaultAsync(x => x.Id == command.Id);
        if (user is null)
            return OperationResult.NotFound("کاربر مورد نظر یافت نشد.");

        var error = Validate(command.PhoneNumber, command.Email, command.Role);
        if (error is not null)
            return OperationResult.Error(error);

        var phoneNumber = command.PhoneNumber.Trim();

        var phoneExists = await context.Users
            .AnyAsync(x => x.Id != command.Id && x.PhoneNumber == phoneNumber);

        if (phoneExists)
            return OperationResult.Error("این شماره موبایل قبلاً توسط کاربر دیگری ثبت شده است.");

        user.PhoneNumber = phoneNumber;
        user.Email = command.Email?.Trim();
        user.FirstName = command.FirstName?.Trim();
        user.LastName = command.LastName?.Trim();
        user.Role = command.Role;
        user.IsPhoneVerified = command.IsPhoneVerified;
        user.IsEmailVerified = command.IsEmailVerified;

        await context.SaveChangesAsync();

        return OperationResult.Success("کاربر با موفقیت ویرایش شد.");
    }

    public async Task<OperationResult> UpdateProfileAsync(UpdateProfileCommand command)
    {
        var user = await context.Users.AsTracking().FirstOrDefaultAsync(x => x.Id == command.UserId);
        if (user is null)
            return OperationResult.NotFound("کاربر مورد نظر یافت نشد.");

        var email = string.IsNullOrWhiteSpace(command.Email) ? null : command.Email.Trim();

        if (email is not null && !email.IsValidEmail())
            return OperationResult.Error("ایمیل وارد‌شده معتبر نیست.");

        if (command.FirstName?.Trim().Length > 100 || command.LastName?.Trim().Length > 100)
            return OperationResult.Error("نام و نام خانوادگی حداکثر ۱۰۰ کاراکتر هستند.");

        // تغییر ایمیل، وضعیت تایید آن را باطل می‌کند.
        if (!string.Equals(user.Email, email, StringComparison.OrdinalIgnoreCase))
            user.IsEmailVerified = false;

        user.FirstName = string.IsNullOrWhiteSpace(command.FirstName) ? null : command.FirstName.Trim();
        user.LastName = string.IsNullOrWhiteSpace(command.LastName) ? null : command.LastName.Trim();
        user.Email = email;

        await context.SaveChangesAsync();

        return OperationResult.Success("اطلاعات حساب کاربری با موفقیت ذخیره شد.");
    }

    public async Task<OperationResult> ConfirmPhoneAsync(Guid userId)
    {
        var user = await context.Users.AsTracking().FirstOrDefaultAsync(x => x.Id == userId);
        if (user is null)
            return OperationResult.NotFound("کاربر مورد نظر یافت نشد.");

        if (user.IsPhoneVerified)
            return OperationResult.Success("شماره موبایل شما قبلاً تایید شده است.");

        user.IsPhoneVerified = true;
        await context.SaveChangesAsync();

        return OperationResult.Success("شماره موبایل شما با موفقیت تایید شد.");
    }

    public async Task<OperationResult> DeleteUserAsync(Guid id)
    {
        var user = await context.Users.AsTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (user is null)
            return OperationResult.NotFound("کاربر مورد نظر یافت نشد.");

        // جلوگیری از قفل‌شدن پنل مدیریت با حذف آخرین سوپر ادمین.
        if (user.Role == UserRole.SuperAdmin)
            return OperationResult.Error("حساب سوپر ادمین قابل حذف نیست.");

        context.Users.Remove(user);
        await context.SaveChangesAsync();

        return OperationResult.Success("کاربر با موفقیت حذف شد.");
    }

    public async Task<OperationResult> ChangePasswordAsync(ChangePasswordCommand command)
    {
        if (command.UserId == Guid.Empty)
            return OperationResult.Error("شناسه کاربر معتبر نیست.");

        if (string.IsNullOrWhiteSpace(command.NewPassword) || command.NewPassword.Trim().Length < MinimumPasswordLength)
            return OperationResult.Error($"رمز عبور جدید باید حداقل {MinimumPasswordLength} کاراکتر باشد.");

        if (!string.IsNullOrWhiteSpace(command.CurrentPassword) && command.NewPassword == command.CurrentPassword)
            return OperationResult.Error("رمز عبور جدید نباید با رمز عبور فعلی یکسان باشد.");

        var user = await context.Users.AsTracking().FirstOrDefaultAsync(x => x.Id == command.UserId);
        if (user is null)
            return OperationResult.NotFound("کاربر مورد نظر یافت نشد.");

        // کاربری که رمز ندارد (ورود با کد یک‌بارمصرف) بدون رمز فعلی، رمز جدید ثبت می‌کند.
        if (!string.IsNullOrWhiteSpace(user.PasswordHash)
            && !PasswordHasher.Verify(command.CurrentPassword ?? string.Empty, user.PasswordHash))
            return OperationResult.Error("رمز عبور فعلی صحیح نیست.");

        user.PasswordHash = PasswordHasher.Hash(command.NewPassword);

        await context.SaveChangesAsync();

        return OperationResult.Success("رمز عبور با موفقیت تغییر کرد.");
    }

    public async Task<UserDto?> LoginAsync(LoginCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.PhoneNumber) || string.IsNullOrWhiteSpace(command.Password))
            return null;

        var phoneNumber = command.PhoneNumber.Trim();

        var user = await context.Users.AsTracking().FirstOrDefaultAsync(x => x.PhoneNumber == phoneNumber);
        if (user is null)
            return null;

        // کاربری که رمز عبور ثبت نکرده است (ورود با کد یک‌بارمصرف) با رمز وارد نمی‌شود؛
        // PasswordHasher.Verify برای هش خالی مقدار true برمی‌گرداند و اینجا قابل استفاده نیست.
        if (string.IsNullOrWhiteSpace(user.PasswordHash))
            return null;

        if (!PasswordHasher.Verify(command.Password, user.PasswordHash))
            return null;

        user.LastLoginAt = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync();

        return user.Map();
    }

    public async Task<UserFilterResult> GetUsersByFilterAsync(UserFilterParams filterParams)
    {
        var query = context.Users.AsQueryable();

        if (!string.IsNullOrWhiteSpace(filterParams.Search))
        {
            var search = filterParams.Search.Trim();
            query = query.Where(x => x.PhoneNumber.Contains(search)
                                     || (x.Email != null && x.Email.Contains(search))
                                     || (x.FirstName != null && x.FirstName.Contains(search))
                                     || (x.LastName != null && x.LastName.Contains(search)));
        }

        if (filterParams.Role.HasValue)
            query = query.Where(x => x.Role == filterParams.Role.Value);

        if (filterParams.IsPhoneVerified.HasValue)
            query = query.Where(x => x.IsPhoneVerified == filterParams.IsPhoneVerified.Value);

        if (filterParams.IsEmailVerified.HasValue)
            query = query.Where(x => x.IsEmailVerified == filterParams.IsEmailVerified.Value);

        query = query.OrderByDescending(x => x.CreatedAt);

        var skip = (filterParams.PageId - 1) * filterParams.Take;

        var users = await query
            .Skip(skip)
            .Take(filterParams.Take)
            .ToListAsync();

        var result = new UserFilterResult
        {
            Data = users.Map(),
            FilterParams = filterParams,
        };

        result.GeneratePaging(query, filterParams.Take, filterParams.PageId);

        return result;
    }

    public List<UserDto> GetUsersAsync()
    {
        return context.Users
            .OrderByDescending(x => x.CreatedAt)
            .ToList()
            .Map();
    }

    public async Task<UserDto?> GetUserByIdAsync(Guid id)
    {
        var user = await context.Users
            .Include(x => x.Addresses)
            .ThenInclude(address => address.Province)
            .Include(x => x.Addresses)
            .ThenInclude(address => address.City)
            .FirstOrDefaultAsync(x => x.Id == id);

        return user?.Map();
    }

    public async Task<UserDto?> GetUserByPhoneAsync(string phoneNumber)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
            return null;

        var user = await context.Users
            .Include(x => x.Addresses)
            .FirstOrDefaultAsync(x => x.PhoneNumber == phoneNumber.Trim());

        return user?.Map();
    }

    private static string? Validate(string? phoneNumber, string? email, UserRole role)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
            return "شماره موبایل را وارد کنید.";

        if (!phoneNumber.Trim().IsValidMobile())
            return "شماره موبایل باید با ۰۹ شروع شود و ۱۱ رقم باشد.";

        if (!string.IsNullOrWhiteSpace(email) && !email.IsValidEmail())
            return "ایمیل وارد‌شده معتبر نیست.";

        if (role == UserRole.None)
            return "نقش کاربر را انتخاب کنید.";

        return null;
    }
}
