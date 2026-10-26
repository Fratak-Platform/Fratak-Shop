using System.Security.Cryptography;
using Fratak_Shop.Core.Services.Notifications;
using Microsoft.Extensions.Caching.Memory;

namespace Fratak_Shop.Web.Services;

/// <summary>What a one-time code is for; a code of one purpose is never accepted for another.</summary>
public enum OtpPurpose
{
    VerifyPhone = 1,
    Login = 2,
    TwoFactor = 3,

    /// <summary>تأیید دوبارهٔ هویت کارمند پیش از کار حساس در پنل (مرحلهٔ هفتم – فاز ۱۰).</summary>
    Reauth = 4,
}

/// <summary>
/// One-time SMS codes. Codes live in memory for a short time, are scoped to a purpose,
/// are invalidated after too many wrong attempts and a phone number can only receive
/// a limited number of codes per hour (SMS bombing protection).
/// </summary>
public interface IOtpService
{
    TimeSpan Lifetime { get; }

    int CodeLength { get; }

    /// <summary>Creates (or re-creates) a code for the phone number and sends it by SMS.</summary>
    Task<OtpSendResult> SendAsync(string phoneNumber, OtpPurpose purpose = OtpPurpose.VerifyPhone);

    bool Verify(string phoneNumber, string code, OtpPurpose purpose = OtpPurpose.VerifyPhone);
}

/// <param name="Sent">False when the resend delay or the hourly limit prevented sending.</param>
/// <param name="RetryAfter">Time until the next code can be requested.</param>
/// <param name="DevelopmentCode">The code, only in the development environment (no SMS provider).</param>
/// <param name="Blocked">True when the hourly limit of the number is reached.</param>
public record OtpSendResult(bool Sent, TimeSpan RetryAfter, string? DevelopmentCode, bool Blocked = false);

public class OtpService(IMemoryCache cache, ISmsSender smsSender, IWebHostEnvironment environment) : IOtpService
{
    private const int MaxAttempts = 5;
    private const int MaxCodesPerHour = 6;
    private static readonly TimeSpan ResendDelay = TimeSpan.FromSeconds(60);

    public TimeSpan Lifetime => TimeSpan.FromMinutes(3);

    public int CodeLength => 5;

    public async Task<OtpSendResult> SendAsync(string phoneNumber, OtpPurpose purpose = OtpPurpose.VerifyPhone)
    {
        var phone = phoneNumber.Trim();
        var key = CacheKey(phone, purpose);
        var now = DateTimeOffset.UtcNow;

        if (cache.TryGetValue<OtpEntry>(key, out var existing) && existing is not null)
        {
            var elapsed = now - existing.SentAt;
            if (elapsed < ResendDelay)
                return new OtpSendResult(false, ResendDelay - elapsed, environment.IsDevelopment() ? existing.Code : null);
        }

        var counter = cache.GetOrCreate($"otp:hourly:{phone}", entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1);
            return new SendCounter(now.AddHours(1));
        })!;

        if (counter.Increment() > MaxCodesPerHour)
            return new OtpSendResult(false, counter.ResetAt - now, null, Blocked: true);

        var max = (int)Math.Pow(10, CodeLength);
        var code = RandomNumberGenerator.GetInt32(max / 10, max).ToString();
        cache.Set(key, new OtpEntry(code, now), Lifetime);

        var text = purpose switch
        {
            OtpPurpose.Login => $"کد ورود شما: {code}\nاین کد را در اختیار دیگران قرار ندهید.",
            OtpPurpose.TwoFactor => $"کد تایید دو مرحله‌ای: {code}\nاگر شما درخواست ورود نداده‌اید، رمز عبور خود را تغییر دهید.",
            OtpPurpose.Reauth => $"کد تایید عملیات حساس در پنل مدیریت: {code}\nاگر شما درخواست نداده‌اید، فوراً به مدیر فروشگاه خبر دهید.",
            _ => $"کد تایید شما: {code}",
        };

        await smsSender.SendAsync(phone, text);

        return new OtpSendResult(true, ResendDelay, environment.IsDevelopment() ? code : null);
    }

    public bool Verify(string phoneNumber, string code, OtpPurpose purpose = OtpPurpose.VerifyPhone)
    {
        var key = CacheKey(phoneNumber.Trim(), purpose);

        if (!cache.TryGetValue<OtpEntry>(key, out var entry) || entry is null)
            return false;

        var given = System.Text.Encoding.ASCII.GetBytes(code?.Trim() ?? string.Empty);
        var expected = System.Text.Encoding.ASCII.GetBytes(entry.Code);
        if (CryptographicOperations.FixedTimeEquals(given, expected))
        {
            cache.Remove(key);
            return true;
        }

        if (entry.RegisterFailedAttempt() >= MaxAttempts)
            cache.Remove(key);

        return false;
    }

    private static string CacheKey(string phoneNumber, OtpPurpose purpose) => $"otp:{(int)purpose}:{phoneNumber}";

    private sealed class OtpEntry(string code, DateTimeOffset sentAt)
    {
        public string Code { get; } = code;
        public DateTimeOffset SentAt { get; } = sentAt;
        private int _attempts;

        public int RegisterFailedAttempt() => Interlocked.Increment(ref _attempts);
    }

    private sealed class SendCounter(DateTimeOffset resetAt)
    {
        public DateTimeOffset ResetAt { get; } = resetAt;
        private int _count;

        public int Increment() => Interlocked.Increment(ref _count);
    }
}

public class LoggingSmsSender(ILogger<LoggingSmsSender> logger) : ISmsSender
{
    public Task SendAsync(string phoneNumber, string message)
    {
        logger.LogInformation("SMS to {PhoneNumber}: {Message}", phoneNumber, message);
        return Task.CompletedTask;
    }
}
