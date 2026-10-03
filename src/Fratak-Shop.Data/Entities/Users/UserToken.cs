using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Fratak_Shop.Data.Utilities;

namespace Fratak_Shop.Data.Entities.Users;

/// <summary>
/// Short-lived security token of a user: phone verification code, password reset
/// code or two-factor code. Only the hash of the code is stored, the code itself
/// is delivered through <c>ISmsSender</c> so a leaked database never exposes codes.
/// </summary>
public class UserToken : BaseEntity
{
    public Guid UserId { get; set; }

    public UserTokenPurpose Purpose { get; set; }

    /// <summary>
    /// HMAC hash of the code (see <c>VerificationCodeHasher</c>).
    /// </summary>
    [Required]
    [MaxLength(200)]
    public string TokenHash { get; set; } = null!;

    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset? UsedAt { get; set; }

    /// <summary>
    /// Wrong attempts, used to lock a code after <see cref="MaxAttempts"/> tries.
    /// </summary>
    public int Attempts { get; set; }

    public int MaxAttempts { get; set; } = 5;

    [MaxLength(50)]
    public string? CreatedByIp { get; set; }

    [ForeignKey(nameof(UserId))]
    public User User { get; set; } = null!;
}

public enum UserTokenPurpose
{
    None = 0,
    /// <summary>کد تأیید شماره موبایل در زمان ثبت‌نام یا ورود</summary>
    PhoneVerification = 1,
    /// <summary>کد بازیابی رمز عبور</summary>
    PasswordReset = 2,
    EmailVerification = 3,
    TwoFactor = 4,
    /// <summary>ورود بدون رمز با کد یک‌بار مصرف</summary>
    PasswordlessLogin = 5,
}
