using System.Security.Cryptography;
using Microsoft.Extensions.Caching.Memory;

namespace Fratak_Shop.Web.Services;

/// <summary>
/// One-time verification codes for phone numbers. Codes live in memory for a
/// short time and are invalidated after too many wrong attempts.
/// </summary>
public interface IOtpService
{
    TimeSpan Lifetime { get; }

    /// <summary>Creates (or re-creates) a code for the phone number and sends it by SMS.</summary>
    Task<OtpSendResult> SendAsync(string phoneNumber);

    bool Verify(string phoneNumber, string code);
}

public record OtpSendResult(bool Sent, TimeSpan RetryAfter, string? DevelopmentCode);

public class OtpService(IMemoryCache cache, ISmsSender smsSender, IWebHostEnvironment environment) : IOtpService
{
    private const int MaxAttempts = 5;
    private static readonly TimeSpan ResendDelay = TimeSpan.FromSeconds(60);

    public TimeSpan Lifetime => TimeSpan.FromMinutes(3);

    public async Task<OtpSendResult> SendAsync(string phoneNumber)
    {
        var key = CacheKey(phoneNumber);

        if (cache.TryGetValue<OtpEntry>(key, out var existing) && existing is not null)
        {
            var elapsed = DateTimeOffset.UtcNow - existing.SentAt;
            if (elapsed < ResendDelay)
                return new OtpSendResult(false, ResendDelay - elapsed, environment.IsDevelopment() ? existing.Code : null);
        }

        var code = RandomNumberGenerator.GetInt32(1000, 10000).ToString();
        cache.Set(key, new OtpEntry(code, DateTimeOffset.UtcNow), Lifetime);

        await smsSender.SendAsync(phoneNumber, $"کد تایید شما: {code}");

        return new OtpSendResult(true, ResendDelay, environment.IsDevelopment() ? code : null);
    }

    public bool Verify(string phoneNumber, string code)
    {
        var key = CacheKey(phoneNumber);

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

    private static string CacheKey(string phoneNumber) => $"otp:{phoneNumber.Trim()}";

    private sealed class OtpEntry(string code, DateTimeOffset sentAt)
    {
        public string Code { get; } = code;
        public DateTimeOffset SentAt { get; } = sentAt;
        private int _attempts;

        public int RegisterFailedAttempt() => Interlocked.Increment(ref _attempts);
    }
}

/// <summary>
/// SMS provider abstraction. Replace <see cref="LoggingSmsSender"/> with a real
/// provider (Kavenegar, SMS.ir, ...) in production.
/// </summary>
public interface ISmsSender
{
    Task SendAsync(string phoneNumber, string message);
}

public class LoggingSmsSender(ILogger<LoggingSmsSender> logger) : ISmsSender
{
    public Task SendAsync(string phoneNumber, string message)
    {
        logger.LogInformation("SMS to {PhoneNumber}: {Message}", phoneNumber, message);
        return Task.CompletedTask;
    }
}
