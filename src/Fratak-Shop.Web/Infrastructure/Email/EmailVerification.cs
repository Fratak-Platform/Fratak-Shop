using System.Security.Cryptography;
using Fratak_Shop.Core.Services.Notifications;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Settings;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Fratak_Shop.Web.Infrastructure.Email;

/// <summary>
/// تأیید نشانی ایمیل کاربر (مرحلهٔ دوازدهم).
/// <para>
/// روند: کاربر در «پروفایل ← اطلاعات حساب» روی «ارسال لینک تأیید» می‌زند (یا ایمیلش را عوض می‌کند) → ایمیلی با لینک
/// <c>/verify-email?token=…</c> می‌رود → کاربر لینک را باز می‌کند و «تأیید ایمیل» را می‌زند → اگر ایمیل فعلی حساب همان ایمیل داخل
/// توکن باشد، IsEmailVerified روشن می‌شود و از این به بعد اعلان‌های ایمیلی برایش ارسال می‌شود (NotificationService فقط به
/// ایمیل تأییدشده می‌فرستد).
/// </para>
/// <para>
/// امنیت:
/// <list type="bullet">
/// <item>توکن با Data Protection امضا و رمزنگاری می‌شود (قابل جعل یا خواندن نیست) و ۲۴ ساعت اعتبار دارد.</item>
/// <item>توکن به «شناسهٔ کاربر + همان ایمیل» گره خورده؛ اگر کاربر بعداً ایمیلش را عوض کند، لینک قبلی بی‌اثر است.</item>
/// <item>باز کردن لینک به‌تنهایی چیزی را تأیید نمی‌کند (دکمهٔ POST لازم است) تا اسکنرهای لینک ایمیل خودکار تأیید نکنند.</item>
/// <item>ارسال محدود است: هر کاربر هر ۲ دقیقه یک بار و حداکثر ۵ بار در روز (جلوی سوءاستفاده برای اسپم کردن دیگران).</item>
/// </list>
/// </para>
/// </summary>
public sealed class EmailVerification(
    IDataProtectionProvider protection,
    IMemoryCache cache,
    IEmailSender sender,
    IEmailProviderService provider,
    FratakShopDbContext context,
    IWebHostEnvironment environment)
{
    /// <summary>هدف Data Protection (توکن‌های هدف‌های دیگر، مثل ورود دومرحله‌ای، اینجا پذیرفته نمی‌شوند).</summary>
    public const string Purpose = "Fratak.EmailVerify";

    /// <summary>اعتبار لینک.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(24);

    /// <summary>کمترین فاصلهٔ دو ارسال برای یک کاربر.</summary>
    public static readonly TimeSpan Cooldown = TimeSpan.FromMinutes(2);

    /// <summary>حداکثر ارسال در ۲۴ ساعت برای یک کاربر.</summary>
    public const int DailyLimit = 5;

    /// <summary>مسیر صفحهٔ تأیید (Pages/VerifyEmail.cshtml).</summary>
    public const string PagePath = "/verify-email";

    private ITimeLimitedDataProtector Protector => protection.CreateProtector(Purpose).ToTimeLimitedDataProtector();

    /// <summary>ساخت توکن. مثال ورودی: (userId, "Ali@Example.com") → متن رمزشدهٔ امن برای آدرس اینترنتی.</summary>
    public string CreateToken(Guid userId, string email) =>
        Protector.Protect($"{userId:N}|{email.Trim().ToLowerInvariant()}", Lifetime);

    /// <summary>خواندن توکن؛ نامعتبر، دست‌کاری‌شده یا منقضی → null.</summary>
    public (Guid UserId, string Email)? ReadToken(string? token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 2000)
            return null;
        try
        {
            var payload = Protector.Unprotect(token.Trim(), out _);
            var bar = payload.IndexOf('|');
            if (bar <= 0 || !Guid.TryParseExact(payload[..bar], "N", out var userId))
                return null;
            var email = payload[(bar + 1)..];
            return EmailRules.IsValidAddress(email) ? (userId, email) : null;
        }
        catch (CryptographicException)
        {
            return null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    /// <summary>
    /// ارسال لینک تأیید به ایمیل فعلی کاربر (با محدودیت تعداد). خروجی پیام فارسی برای کاربر.
    /// </summary>
    public async Task<OperationResult> SendAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await context.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.Email, u.IsEmailVerified })
            .FirstOrDefaultAsync(cancellationToken);
        if (user is null)
            return OperationResult.NotFound("حساب کاربری پیدا نشد.");
        if (!EmailRules.IsValidAddress(user.Email))
            return OperationResult.Error("ابتدا یک نشانی ایمیل معتبر در اطلاعات حساب ثبت کنید.");
        if (user.IsEmailVerified)
            return OperationResult.Success("ایمیل شما قبلاً تأیید شده است.");

        var config = await provider.GetRuntimeAsync(cancellationToken);
        var canSend = config.Mode == EmailMode.Smtp || (config.Mode == EmailMode.LogOnly && !environment.IsProduction());
        if (!canSend)
            return OperationResult.Error("ارسال ایمیل در فروشگاه هنوز فعال نشده است؛ لطفاً بعداً دوباره امتحان کنید.");
        if (EmailRules.NormalizeBaseUrl(config.SiteBaseUrl) is null)
            return OperationResult.Error("ارسال لینک تأیید فعلاً ممکن نیست (نشانی سایت در تنظیمات ایمیل ثبت نشده است).");

        if (!TryReserve(userId, out var limitMessage))
            return OperationResult.Error(limitMessage);

        var token = CreateToken(userId, user.Email!);
        var message = new EmailMessage(user.Email!.Trim(), "تأیید نشانی ایمیل",
            "برای تأیید نشانی ایمیل حساب کاربری خود روی دکمهٔ زیر بزنید و در صفحهٔ باز شده «تأیید ایمیل» را انتخاب کنید.\n" +
            "این لینک تا ۲۴ ساعت معتبر است.\nاگر شما این درخواست را نداده‌اید، این ایمیل را نادیده بگیرید.")
        {
            ActionUrl = $"{PagePath}?token={Uri.EscapeDataString(token)}",
            ActionText = "تأیید ایمیل",
            Category = "verify-email",
        };
        if (!sender.Enqueue(message))
            return OperationResult.Error("ارسال ایمیل ممکن نشد؛ لطفاً چند دقیقهٔ دیگر دوباره امتحان کنید.");
        return OperationResult.Success($"لینک تأیید به {EmailRules.Mask(user.Email)} ارسال شد. صندوق ورودی (و پوشهٔ اسپم) را ببینید.");
    }

    /// <summary>
    /// تأیید با توکن. فقط وقتی موفق است که ایمیل فعلی حساب همان ایمیل داخل توکن باشد.
    /// </summary>
    public async Task<OperationResult> ConfirmAsync(string? token, CancellationToken cancellationToken = default)
    {
        if (ReadToken(token) is not { } data)
            return OperationResult.Error("لینک تأیید نامعتبر یا منقضی است. از صفحهٔ اطلاعات حساب، لینک تازه بخواهید.");

        var user = await context.Users.AsTracking().FirstOrDefaultAsync(u => u.Id == data.UserId, cancellationToken);
        if (user is null || string.IsNullOrWhiteSpace(user.Email)
            || !string.Equals(user.Email.Trim(), data.Email, StringComparison.OrdinalIgnoreCase))
            return OperationResult.Error("این لینک برای ایمیل فعلی حساب نیست (ممکن است ایمیل عوض شده باشد). لینک تازه بخواهید.");

        if (!user.IsEmailVerified)
        {
            user.IsEmailVerified = true;
            await context.SaveChangesAsync(cancellationToken);
        }
        return OperationResult.Success("ایمیل شما تأیید شد. از این پس اعلان‌ها به این ایمیل هم ارسال می‌شود.");
    }

    /// <summary>
    /// محدودیت ارسال: هر <see cref="Cooldown"/> یک بار و حداکثر <see cref="DailyLimit"/> بار در ۲۴ ساعت.
    /// شمارنده در حافظهٔ همین سرور است (برای یک نمونهٔ برنامه کافی است).
    /// </summary>
    private bool TryReserve(Guid userId, out string message)
    {
        var lastKey = $"email-verify:last:{userId:N}";
        var dayKey = $"email-verify:day:{userId:N}";
        var now = DateTimeOffset.UtcNow;
        if (cache.TryGetValue<DateTimeOffset>(lastKey, out var last) && now - last < Cooldown)
        {
            var wait = (int)Math.Ceiling((Cooldown - (now - last)).TotalSeconds);
            message = $"لینک تأیید همین الان ارسال شد؛ {wait} ثانیهٔ دیگر می‌توانید دوباره درخواست کنید.";
            return false;
        }

        var day = cache.Get<DayCounter>(dayKey);
        if (day is not null && day.Count >= DailyLimit)
        {
            message = "امروز بیش از حد مجاز لینک تأیید درخواست کرده‌اید؛ فردا دوباره امتحان کنید.";
            return false;
        }

        if (day is null)
            cache.Set(dayKey, new DayCounter { Count = 1 }, now.Add(TimeSpan.FromHours(24)));
        else
            Interlocked.Increment(ref day.Count);
        cache.Set(lastKey, now, now.Add(Cooldown));
        message = "";
        return true;
    }

    private sealed class DayCounter
    {
        public int Count;
    }
}
