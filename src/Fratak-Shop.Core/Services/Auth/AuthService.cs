using System.Security.Cryptography;
using Fratak_Shop.Core.DTOs.Auth.Command;
using Fratak_Shop.Core.DTOs.Auth.Query;
using Fratak_Shop.Core.Services.Messaging;
using Fratak_Shop.Core.Services.Notifications;
using Fratak_Shop.Core.Services.Settings;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Notifications;
using Fratak_Shop.Data.Entities.Users;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Core.Services.Auth;

public class AuthService(
    FratakShopDbContext context,
    ISmsSender smsSender,
    INotificationService notificationService,
    ISiteSettingService settingService) : IAuthService
{
    private const int CodeLength = 5;
    private static readonly TimeSpan VerificationCodeLifetime = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan PasswordResetCodeLifetime = TimeSpan.FromMinutes(10);
    private const int MaxCodesPerHour = 6;

    public async Task<OperationResult<AuthResultDto>> RegisterAsync(RegisterUserCommand command, string? ipAddress = null,
        CancellationToken cancellationToken = default)
    {
        if (command is null)
            return OperationResult<AuthResultDto>.Error("اطلاعات ثبت‌نام نامعتبر است.");

        var phoneNumber = SlugHelper.ToLatinDigits(command.PhoneNumber).Trim();
        if (!phoneNumber.IsValidMobile())
            return OperationResult<AuthResultDto>.Error("شماره موبایل باید با ۰۹ شروع شده و ۱۱ رقم باشد.");

        if (!command.AcceptTerms)
            return OperationResult<AuthResultDto>.Error("پذیرش قوانین و مقررات فروشگاه الزامی است.");

        if (!string.IsNullOrWhiteSpace(command.Password) && command.Password.Length < 6)
            return OperationResult<AuthResultDto>.Error("رمز عبور باید حداقل ۶ کاراکتر باشد.");

        var exists = await context.Users.AnyAsync(user => user.PhoneNumber == phoneNumber, cancellationToken);
        if (exists)
            return OperationResult<AuthResultDto>.Error("این شماره موبایل قبلاً در فروشگاه ثبت شده است. وارد شوید.");

        var user = new User
        {
            PhoneNumber = phoneNumber,
            PasswordHash = string.IsNullOrWhiteSpace(command.Password) ? null : PasswordHasher.Hash(command.Password),
            FirstName = command.FirstName?.Trim(),
            LastName = command.LastName?.Trim(),
            Email = string.IsNullOrWhiteSpace(command.Email) ? null : command.Email.Trim(),
            Role = UserRole.Customer,
            IsActive = true,
            IsPhoneVerified = false,
        };

        context.Users.Add(user);
        await context.SaveChangesAsync(cancellationToken);

        await notificationService.NotifyUserAsync(user.Id, "به فراتک خوش آمدید 🌟",
            "حساب کاربری شما ساخته شد. برای تکمیل ثبت‌نام شماره موبایل خود را تأیید کنید.",
            NotificationType.Success, "/profile", cancellationToken);

        var code = await CreateCodeAsync(user, UserTokenPurpose.PhoneVerification, VerificationCodeLifetime, ipAddress, cancellationToken);

        var result = BuildAuthResult(user, requiresPhoneVerification: true, developmentCode: code);
        return OperationResult<AuthResultDto>.Success(result, "ثبت‌نام با موفقیت انجام شد. کد تأیید برای شما ارسال شد.");
    }

    public async Task<OperationResult<AuthResultDto>> LoginAsync(LoginCommand command, string? ipAddress = null,
        CancellationToken cancellationToken = default)
    {
        var phoneNumber = SlugHelper.ToLatinDigits(command.PhoneNumber).Trim();
        if (!phoneNumber.IsValidMobile())
            return OperationResult<AuthResultDto>.Error("شماره موبایل معتبر نیست.");

        var user = await context.Users
            .AsTracking()
            .Include(item => item.RoleAssignments)
            .FirstOrDefaultAsync(item => item.PhoneNumber == phoneNumber, cancellationToken);

        if (user is null)
            return OperationResult<AuthResultDto>.Error("کاربری با این شماره موبایل ثبت نشده است.");

        if (!user.IsActive)
            return OperationResult<AuthResultDto>.Error("حساب کاربری شما غیرفعال شده است. با پشتیبانی تماس بگیرید.");

        if (!PasswordHasher.Verify(command.Password, user.PasswordHash))
            return OperationResult<AuthResultDto>.Error("شماره موبایل یا رمز عبور اشتباه است.");

        user.LastLoginAt = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync(cancellationToken);

        var result = BuildAuthResult(user, requiresPhoneVerification: !user.IsPhoneVerified);
        return OperationResult<AuthResultDto>.Success(result, $"خوش آمدید {result.FullName ?? string.Empty}".Trim());
    }

    public async Task<OperationResult<AuthResultDto>> SendPasswordlessLoginCodeAsync(string phoneNumber, string? ipAddress = null,
        CancellationToken cancellationToken = default)
    {
        var normalized = SlugHelper.ToLatinDigits(phoneNumber).Trim();
        if (!normalized.IsValidMobile())
            return OperationResult<AuthResultDto>.Error("شماره موبایل معتبر نیست.");

        var user = await context.Users
            .AsTracking()
            .Include(item => item.RoleAssignments)
            .FirstOrDefaultAsync(item => item.PhoneNumber == normalized, cancellationToken);

        if (user is null)
            return OperationResult<AuthResultDto>.Error("کاربری با این شماره موبایل ثبت نشده است. ابتدا ثبت‌نام کنید.");

        if (!user.IsActive)
            return OperationResult<AuthResultDto>.Error("حساب کاربری شما غیرفعال شده است.");

        var code = await CreateCodeAsync(user, UserTokenPurpose.PasswordlessLogin, VerificationCodeLifetime, ipAddress, cancellationToken);

        return OperationResult<AuthResultDto>.Success(
            BuildAuthResult(user, requiresPhoneVerification: true, developmentCode: code),
            "کد ورود برای شما ارسال شد.");
    }

    public async Task<OperationResult<AuthResultDto>> SendVerificationCodeAsync(SendVerificationCodeCommand command,
        string? ipAddress = null, CancellationToken cancellationToken = default)
    {
        var normalized = SlugHelper.ToLatinDigits(command.PhoneNumber).Trim();
        if (!normalized.IsValidMobile())
            return OperationResult<AuthResultDto>.Error("شماره موبایل معتبر نیست.");

        var user = await context.Users
            .AsTracking()
            .Include(item => item.RoleAssignments)
            .FirstOrDefaultAsync(item => item.PhoneNumber == normalized, cancellationToken);

        if (user is null)
            return OperationResult<AuthResultDto>.Error("کاربری با این شماره موبایل ثبت نشده است.");

        var lifetime = command.Purpose == UserTokenPurpose.PasswordReset ? PasswordResetCodeLifetime : VerificationCodeLifetime;
        var code = await CreateCodeAsync(user, command.Purpose, lifetime, ipAddress, cancellationToken);

        var message = command.Purpose == UserTokenPurpose.PasswordReset
            ? "کد بازیابی رمز عبور برای شما ارسال شد."
            : "کد تأیید برای شما ارسال شد.";

        return OperationResult<AuthResultDto>.Success(
            BuildAuthResult(user, requiresPhoneVerification: true, developmentCode: code), message);
    }

    public async Task<OperationResult<AuthResultDto>> VerifyCodeAsync(VerifyCodeCommand command,
        CancellationToken cancellationToken = default)
    {
        var normalized = SlugHelper.ToLatinDigits(command.PhoneNumber).Trim();
        var code = SlugHelper.ToLatinDigits(command.Code).Trim();

        if (string.IsNullOrWhiteSpace(code))
            return OperationResult<AuthResultDto>.Error("کد تأیید را وارد کنید.");

        var user = await context.Users
            .AsTracking()
            .Include(item => item.RoleAssignments)
            .FirstOrDefaultAsync(item => item.PhoneNumber == normalized, cancellationToken);

        if (user is null)
            return OperationResult<AuthResultDto>.Error("کاربری با این شماره موبایل ثبت نشده است.");

        var token = await context.UserTokens
            .AsTracking()
            .Where(item => item.UserId == user.Id && item.Purpose == command.Purpose && item.UsedAt == null)
            .OrderByDescending(item => item.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (token is null)
            return OperationResult<AuthResultDto>.Error("کد فعالی برای این شماره وجود ندارد. دوباره درخواست کد کنید.");

        if (token.ExpiresAt < DateTimeOffset.UtcNow)
            return OperationResult<AuthResultDto>.Error("کد وارد‌شده منقضی شده است. کد جدید دریافت کنید.");

        if (token.Attempts >= token.MaxAttempts)
            return OperationResult<AuthResultDto>.Error("تعداد تلاش‌های ناموفق بیش از حد مجاز است. کد جدید دریافت کنید.");

        if (!VerificationCodeHasher.IsCompare(token.TokenHash, code))
        {
            token.Attempts++;
            await context.SaveChangesAsync(cancellationToken);
            return OperationResult<AuthResultDto>.Error("کد وارد‌شده صحیح نیست.");
        }

        token.UsedAt = DateTimeOffset.UtcNow;

        if (command.Purpose is UserTokenPurpose.PhoneVerification or UserTokenPurpose.PasswordlessLogin)
            user.IsPhoneVerified = true;

        user.LastLoginAt = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync(cancellationToken);

        var result = BuildAuthResult(user, requiresPhoneVerification: false);
        return OperationResult<AuthResultDto>.Success(result, "شماره موبایل شما با موفقیت تأیید شد.");
    }

    public async Task<OperationResult> ResetPasswordAsync(ResetPasswordCommand command,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(command.NewPassword) || command.NewPassword.Length < 6)
            return OperationResult.Error("رمز عبور جدید باید حداقل ۶ کاراکتر باشد.");

        var verifyResult = await VerifyCodeAsync(new VerifyCodeCommand
        {
            PhoneNumber = command.PhoneNumber,
            Code = command.Code,
            Purpose = UserTokenPurpose.PasswordReset,
        }, cancellationToken);

        if (!verifyResult.IsSuccess())
            return OperationResult.Error(verifyResult.Message);

        var user = await context.Users
            .AsTracking()
            .FirstOrDefaultAsync(item => item.Id == verifyResult.Data.UserId, cancellationToken);

        if (user is null)
            return OperationResult.NotFound("کاربر مورد نظر یافت نشد.");

        user.PasswordHash = PasswordHasher.Hash(command.NewPassword);
        await context.SaveChangesAsync(cancellationToken);

        await notificationService.NotifyUserAsync(user.Id, "تغییر رمز عبور",
            "رمز عبور حساب شما تغییر کرد. اگر این تغییر توسط شما انجام نشده است، همین حالا با پشتیبانی تماس بگیرید.",
            NotificationType.Warning, "/profile", cancellationToken);

        return OperationResult.Success("رمز عبور با موفقیت تغییر کرد. اکنون می‌توانید وارد شوید.");
    }

    public async Task<OperationResult> ChangePasswordAsync(ChangePasswordCommand command,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(command.NewPassword) || command.NewPassword.Length < 6)
            return OperationResult.Error("رمز عبور جدید باید حداقل ۶ کاراکتر باشد.");

        var user = await context.Users.AsTracking().FirstOrDefaultAsync(item => item.Id == command.UserId, cancellationToken);
        if (user is null)
            return OperationResult.NotFound("کاربر مورد نظر یافت نشد.");

        if (!string.IsNullOrWhiteSpace(user.PasswordHash) &&
            !PasswordHasher.Verify(command.CurrentPassword ?? string.Empty, user.PasswordHash))
            return OperationResult.Error("رمز عبور فعلی صحیح نیست.");

        user.PasswordHash = PasswordHasher.Hash(command.NewPassword);
        await context.SaveChangesAsync(cancellationToken);

        return OperationResult.Success("رمز عبور با موفقیت تغییر کرد.");
    }

    public async Task<User?> GetUserAsync(Guid userId) =>
        await context.Users
            .Include(user => user.RoleAssignments)
            .FirstOrDefaultAsync(user => user.Id == userId);

    /// <summary>
    /// Creates, stores (hashed) and sends a one-time code. The rate limit protects
    /// the shop from abusing the SMS credit, and the code is only returned to the
    /// caller when the development setting allows showing it on screen.
    /// </summary>
    private async Task<string?> CreateCodeAsync(User user, UserTokenPurpose purpose, TimeSpan lifetime,
        string? ipAddress, CancellationToken cancellationToken)
    {
        var sentInLastHour = await context.UserTokens
            .CountAsync(token => token.UserId == user.Id && token.CreatedAt > DateTimeOffset.UtcNow.AddHours(-1),
                cancellationToken);

        if (sentInLastHour >= MaxCodesPerHour)
            return null;

        var code = RandomNumberGenerator.GetInt32(0, 100000).ToString($"D{CodeLength}");

        context.UserTokens.Add(new UserToken
        {
            UserId = user.Id,
            Purpose = purpose,
            TokenHash = VerificationCodeHasher.Hash(code),
            ExpiresAt = DateTimeOffset.UtcNow.Add(lifetime),
            CreatedByIp = ipAddress,
            MaxAttempts = 5,
        });

        await context.SaveChangesAsync(cancellationToken);

        var message = purpose switch
        {
            UserTokenPurpose.PasswordReset => $"کد بازیابی رمز عبور فراتک: {code}",
            UserTokenPurpose.PasswordlessLogin => $"کد ورود به فراتک: {code}",
            _ => $"کد تأیید شماره موبایل فراتک: {code}",
        };

        await smsSender.SendAsync(user.PhoneNumber, message, cancellationToken);

        var showCode = await settingService.GetValueAsync("General.ShowVerificationCodeInDevelopment", true);
        return showCode ? code : null;
    }

    private static AuthResultDto BuildAuthResult(User user, bool requiresPhoneVerification, string? developmentCode = null)
    {
        var roles = new List<UserRole>();
        if (user.Role != UserRole.None)
            roles.Add(user.Role);

        roles.AddRange(user.RoleAssignments.Select(assignment => assignment.Role));
        roles = roles.Distinct().ToList();

        var staffRoles = roles.Where(role => role.IsStaff()).ToList();

        return new AuthResultDto
        {
            UserId = user.Id,
            PhoneNumber = user.PhoneNumber,
            FullName = $"{user.FirstName} {user.LastName}".Trim() is { Length: > 0 } name ? name : null,
            PrimaryRole = user.Role,
            Roles = roles,
            IsPhoneVerified = user.IsPhoneVerified,
            RequiresPhoneVerification = requiresPhoneVerification,
            IsStaff = staffRoles.Count > 0,
            LandingPath = staffRoles.Count > 0
                ? staffRoles.OrderBy(role => role).First().GetAdminLandingPath()
                : "/profile",
            DevelopmentCode = developmentCode,
        };
    }
}
