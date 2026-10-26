using Fratak_Shop.Core.DTOs.Users.Command;
using Fratak_Shop.Core.DTOs.Users.Query;
using Fratak_Shop.Core.Mappers.Users;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Notifications;
using Fratak_Shop.Data.Entities.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Fratak_Shop.Core.Services.Users;

/// <summary>
/// مدیریت کاربران: ثبت‌نام و ورود (Argon2id)، ویرایش اطلاعات، تغییر رمز عبور و
/// لیست کاربران. رمز عبور هیچ‌گاه به‌صورت متن ساده ذخیره یا برگردانده نمی‌شود.
/// </summary>
public class UserService(FratakShopDbContext context, Events.IShopEvents events, IMemoryCache cache) : IUserService
{
    /// <summary>
    /// مهر امنیتی کاربر عوض شد: نسخهٔ کش‌شدهٔ آن (که اعتبارسنجی JWT می‌خواند) فوراً پاک می‌شود تا توکن‌های قدیمی
    /// همان لحظه رد شوند، نه تا ۲ دقیقهٔ بعد. هر جای این سرویس که SecurityStamp را عوض می‌کند باید این را صدا بزند.
    /// </summary>
    private void ForgetStamp(Guid userId) => cache.Remove(CacheKeys.SecurityStamp(userId));

    private const int MinimumPasswordLength = 6;
    private const int MaxFailedLogins = 5;
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    /// <summary>هش یک رمز تصادفی (یک بار در طول عمر برنامه ساخته می‌شود) فقط برای یکسان کردن زمان پاسخ ورود ناموفق.</summary>
    private static readonly Lazy<string> TimingEqualizerHash = new(() => PasswordHasher.Hash(Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16))));

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
            SecurityStamp = PasswordHasher.NewSecurityStamp(),
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

        // Changing the login identity or the permissions signs the user out everywhere.
        if (user.PhoneNumber != phoneNumber || user.Role != command.Role)
            user.SecurityStamp = PasswordHasher.NewSecurityStamp();

        user.PhoneNumber = phoneNumber;
        user.Email = command.Email?.Trim();
        user.FirstName = command.FirstName?.Trim();
        user.LastName = command.LastName?.Trim();
        user.Role = command.Role;
        user.IsPhoneVerified = command.IsPhoneVerified;
        user.IsEmailVerified = command.IsEmailVerified;

        await context.SaveChangesAsync();
        ForgetStamp(user.Id);

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

        if (command.BirthDate.HasValue)
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            if (command.BirthDate > today.AddYears(-5) || command.BirthDate < today.AddYears(-110))
                return OperationResult.Error("تاریخ تولد معتبر نیست.");
        }

        user.BirthDate = command.BirthDate;

        await context.SaveChangesAsync();
        await events.ProfileUpdatedAsync(user.Id);

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
        user.SecurityStamp = PasswordHasher.NewSecurityStamp();
        user.FailedLoginCount = 0;
        user.LockoutEndAt = null;

        await context.SaveChangesAsync();
        ForgetStamp(user.Id);
        await events.SecurityEventAsync(user.Id, NotificationEvent.PasswordChanged);

        return OperationResult.Success("رمز عبور با موفقیت تغییر کرد.");
    }

    public async Task<OperationResult> SetTwoFactorAsync(Guid userId, bool enabled)
    {
        var user = await context.Users.AsTracking().FirstOrDefaultAsync(x => x.Id == userId);
        if (user is null)
            return OperationResult.NotFound("کاربر مورد نظر یافت نشد.");

        if (enabled && string.IsNullOrWhiteSpace(user.PasswordHash))
            return OperationResult.Error("برای فعال‌سازی ورود دو مرحله‌ای ابتدا یک رمز عبور برای حساب خود تعیین کنید.");

        if (enabled && !user.IsPhoneVerified)
            return OperationResult.Error("برای فعال‌سازی ورود دو مرحله‌ای ابتدا شماره موبایل خود را تایید کنید.");

        if (user.TwoFactorEnabled == enabled)
            return OperationResult.Success(enabled ? "ورود دو مرحله‌ای از قبل فعال است." : "ورود دو مرحله‌ای از قبل غیرفعال است.");

        user.TwoFactorEnabled = enabled;
        await context.SaveChangesAsync();
        await events.SecurityEventAsync(user.Id, enabled ? NotificationEvent.TwoFactorEnabled : NotificationEvent.TwoFactorDisabled);

        return OperationResult.Success(enabled
            ? "ورود دو مرحله‌ای فعال شد؛ از این پس پس از رمز عبور، کد پیامکی هم لازم است."
            : "ورود دو مرحله‌ای غیرفعال شد.");
    }

    public const string BlockedMessage = "حساب کاربری شما مسدود شده است؛ برای پیگیری با پشتیبانی تماس بگیرید.";

    public async Task<OperationResult<PhoneSignInResult>> SignInWithPhoneAsync(string phoneNumber, bool allowRegistration = true)
    {
        var phone = phoneNumber?.Trim() ?? string.Empty;
        var error = Validate(phone, null, UserRole.Customer);
        if (error is not null)
            return OperationResult<PhoneSignInResult>.Error(error);

        var now = DateTimeOffset.UtcNow;
        var user = await context.Users.AsTracking().FirstOrDefaultAsync(x => x.PhoneNumber == phone);
        var created = false;

        if (user is null && !allowRegistration)
            return OperationResult<PhoneSignInResult>.Error("حسابی با این شماره موبایل ثبت نشده است؛ ابتدا از فرم ثبت نام استفاده کنید.");

        if (user is null)
        {
            // The one-time code proved the ownership of the number: passwordless account.
            user = new User
            {
                PhoneNumber = phone,
                Role = UserRole.Customer,
                IsPhoneVerified = true,
                SecurityStamp = PasswordHasher.NewSecurityStamp(),
                LastLoginAt = now,
            };
            context.Users.Add(user);
            created = true;

            try
            {
                await context.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                // Registered at the same moment by another request (unique phone number).
                context.ChangeTracker.Clear();
                user = await context.Users.AsTracking().FirstOrDefaultAsync(x => x.PhoneNumber == phone);
                if (user is null)
                    return OperationResult<PhoneSignInResult>.Error("ورود انجام نشد؛ لطفاً دوباره تلاش کنید.");
                created = false;
            }
        }

        if (!created && user.IsBlocked)
            return OperationResult<PhoneSignInResult>.Error(BlockedMessage);

        if (!created)
        {
            user.IsPhoneVerified = true;
            user.LastLoginAt = now;
            user.FailedLoginCount = 0;
            user.LockoutEndAt = null;
            user.SecurityStamp ??= PasswordHasher.NewSecurityStamp();
            await context.SaveChangesAsync();
        }

        return OperationResult<PhoneSignInResult>.Success(new PhoneSignInResult(user.Map(), created));
    }

    public async Task<OperationResult<UserDto>> LoginAsync(LoginCommand command)
    {
        const string invalidMessage = "شماره موبایل یا رمز عبور اشتباه است.";

        if (string.IsNullOrWhiteSpace(command.PhoneNumber) || string.IsNullOrWhiteSpace(command.Password)
            || command.Password.Length > 128)
            return OperationResult<UserDto>.Error(invalidMessage);

        var phoneNumber = command.PhoneNumber.Trim();
        var now = DateTimeOffset.UtcNow;

        var user = await context.Users.AsTracking().FirstOrDefaultAsync(x => x.PhoneNumber == phoneNumber);

        // Accounts without a password (OTP sign-up) can't log in with a password.
        if (user is null || string.IsNullOrWhiteSpace(user.PasswordHash))
        {
            // بررسی امنیتی دور ۸ (جلوگیری از شناسایی شماره‌های عضو با زمان پاسخ): هش Argon2 حدود چند صد میلی‌ثانیه
            // طول می‌کشد؛ اگر برای شمارهٔ ناموجود فوراً جواب بدهیم، مهاجم از روی زمان پاسخ می‌فهمد کدام شماره‌ها
            // عضو فروشگاه‌اند. پس همان کار را روی یک هش ساختگی انجام می‌دهیم تا زمان پاسخ یکسان شود.
            PasswordHasher.Verify(command.Password, TimingEqualizerHash.Value);
            return OperationResult<UserDto>.Error(invalidMessage);
        }

        if (user.LockoutEndAt > now)
        {
            var minutes = (int)Math.Ceiling((user.LockoutEndAt.Value - now).TotalMinutes);
            return OperationResult<UserDto>.Error($"به دلیل تلاش‌های ناموفق متعدد، ورود به این حساب تا {minutes} دقیقه دیگر امکان‌پذیر نیست.");
        }

        if (!PasswordHasher.Verify(command.Password, user.PasswordHash))
        {
            user.FailedLoginCount++;
            if (user.FailedLoginCount >= MaxFailedLogins)
            {
                user.LockoutEndAt = now.Add(LockoutDuration);
                user.FailedLoginCount = 0;
            }

            await context.SaveChangesAsync();
            return OperationResult<UserDto>.Error(invalidMessage);
        }

        // Checked after the password, so a wrong guess never reveals that an account is blocked.
        if (user.IsBlocked)
            return OperationResult<UserDto>.Error(BlockedMessage);

        user.FailedLoginCount = 0;
        user.LockoutEndAt = null;
        user.LastLoginAt = now;
        user.SecurityStamp ??= PasswordHasher.NewSecurityStamp();
        await context.SaveChangesAsync();

        return OperationResult<UserDto>.Success(user.Map());
    }

    public async Task RecordLoginAsync(Guid userId)
    {
        var now = DateTimeOffset.UtcNow;
        await context.Users
            .Where(x => x.Id == userId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.LastLoginAt, now)
                .SetProperty(x => x.FailedLoginCount, 0)
                .SetProperty(x => x.LockoutEndAt, (DateTimeOffset?)null));
    }

    public async Task<string?> GetSecurityStampAsync(Guid userId)
    {
        var user = await context.Users
            .Where(x => x.Id == userId)
            .Select(x => new { x.SecurityStamp })
            .FirstOrDefaultAsync();

        if (user is null)
            return null;

        if (!string.IsNullOrEmpty(user.SecurityStamp))
            return user.SecurityStamp;

        // Users created before security stamps existed get one on first use.
        var stamp = PasswordHasher.NewSecurityStamp();
        await context.Users
            .Where(x => x.Id == userId && x.SecurityStamp == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.SecurityStamp, stamp));

        return await context.Users.Where(x => x.Id == userId).Select(x => x.SecurityStamp).FirstOrDefaultAsync();
    }

    public async Task<string?> RotateSecurityStampAsync(Guid userId)
    {
        var stamp = PasswordHasher.NewSecurityStamp();
        var now = DateTimeOffset.UtcNow;
        var affected = await context.Users
            .Where(x => x.Id == userId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.SecurityStamp, stamp)
                .SetProperty(x => x.UpdatedAt, now));

        ForgetStamp(userId);
        return affected == 1 ? stamp : null;
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
