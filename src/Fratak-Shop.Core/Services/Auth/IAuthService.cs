using Fratak_Shop.Core.DTOs.Auth.Command;
using Fratak_Shop.Core.DTOs.Auth.Query;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Entities.Users;

namespace Fratak_Shop.Core.Services.Auth;

/// <summary>
/// Authentication of the shop: registration, password login, one-time-code login,
/// phone verification and password recovery. The service only decides *whether*
/// somebody may sign in; creating the actual cookie/session is the job of the web
/// layer, which keeps the core free of any HTTP concern.
/// </summary>
public interface IAuthService
{
    Task<OperationResult<AuthResultDto>> RegisterAsync(RegisterUserCommand command, string? ipAddress = null,
        CancellationToken cancellationToken = default);

    Task<OperationResult<AuthResultDto>> LoginAsync(LoginCommand command, string? ipAddress = null,
        CancellationToken cancellationToken = default);

    /// <summary>ورود بدون رمز: ارسال کد یک‌بار مصرف برای کاربری که از قبل ثبت‌نام کرده است.</summary>
    Task<OperationResult<AuthResultDto>> SendPasswordlessLoginCodeAsync(string phoneNumber, string? ipAddress = null,
        CancellationToken cancellationToken = default);

    Task<OperationResult<AuthResultDto>> SendVerificationCodeAsync(SendVerificationCodeCommand command,
        string? ipAddress = null, CancellationToken cancellationToken = default);

    Task<OperationResult<AuthResultDto>> VerifyCodeAsync(VerifyCodeCommand command,
        CancellationToken cancellationToken = default);

    Task<OperationResult> ResetPasswordAsync(ResetPasswordCommand command, CancellationToken cancellationToken = default);

    Task<OperationResult> ChangePasswordAsync(ChangePasswordCommand command, CancellationToken cancellationToken = default);

    /// <summary>پروفایل کاربر برای پر کردن فرم‌های پنل کاربری.</summary>
    Task<User?> GetUserAsync(Guid userId);
}
