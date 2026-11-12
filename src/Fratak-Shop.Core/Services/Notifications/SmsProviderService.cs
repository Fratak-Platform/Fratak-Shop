using System.Text.RegularExpressions;
using Fratak_Shop.Core.Services.Security;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Settings;
using Fratak_Shop.Data.Seeders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Fratak_Shop.Core.Services.Notifications;

/// <summary>آنچه فرم پنل نشان می‌دهد؛ خود کلید API هرگز بیرون نمی‌آید (فقط HasApiKey).</summary>
public sealed record SmsProviderView(SmsProvider Provider, bool HasApiKey, long? LineNumber, int? OtpTemplateId, string OtpParameterName);

/// <summary>
/// ورودی فرم «سرویس پیامک». اعداد به‌صورت متن می‌آیند تا رقم فارسی هم پذیرفته شود.
/// ApiKey خالی = کلید قبلی بماند؛ ClearApiKey = کلید پاک شود.
/// </summary>
public sealed record SmsProviderInput(SmsProvider Provider, string? ApiKey, bool ClearApiKey, string? LineNumber, string? OtpTemplateId, string? OtpParameterName);

/// <summary>تنظیمات آمادهٔ ارسال (کلید رمزگشایی‌شده) برای ارسال‌کنندهٔ پیامک در لایهٔ وب. ApiKey ممکن است null باشد.</summary>
public sealed record SmsRuntimeConfig(SmsProvider Provider, string? ApiKey, long? LineNumber, int? OtpTemplateId, string OtpParameterName)
{
    public static readonly SmsRuntimeConfig Default = new(SmsProvider.SmsIr, null, Round8DefaultsSeeder.DefaultSmsLineNumber,
        Round8DefaultsSeeder.DefaultSmsOtpTemplateId, Round8DefaultsSeeder.DefaultSmsOtpParameter);
}

/// <summary>
/// خواندن و ذخیرهٔ تنظیمات سرویس پیامک (جدول SmsProviderConfigs، یک ردیف).
/// استفاده: صفحهٔ پنل «فنی ← سرویس پیامک» (Get/Save) و ConfigurableSmsSender در لایهٔ وب (GetRuntime).
/// </summary>
public interface ISmsProviderService
{
    Task<SmsProviderView> GetAsync(CancellationToken cancellationToken = default);

    Task<OperationResult> SaveAsync(SmsProviderInput input, CancellationToken cancellationToken = default);

    /// <summary>تنظیمات با کلید رمزگشایی‌شده؛ یک دقیقه در حافظه می‌ماند و با ذخیره فوراً تازه می‌شود.</summary>
    Task<SmsRuntimeConfig> GetRuntimeAsync(CancellationToken cancellationToken = default);
}

public sealed partial class SmsProviderService(FratakShopDbContext context, ISecretProtector secrets, IMemoryCache cache) : ISmsProviderService
{
    public const string CacheKey = "sms:provider:runtime";
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(1);

    public async Task<SmsProviderView> GetAsync(CancellationToken cancellationToken = default)
    {
        var c = await LoadAsync(tracking: false, cancellationToken);
        return c is null
            ? new SmsProviderView(SmsRuntimeConfig.Default.Provider, false, SmsRuntimeConfig.Default.LineNumber, SmsRuntimeConfig.Default.OtpTemplateId, SmsRuntimeConfig.Default.OtpParameterName)
            : new SmsProviderView(c.Provider, !string.IsNullOrEmpty(c.ApiKeyProtected), c.LineNumber, c.OtpTemplateId, c.OtpParameterName);
    }

    public async Task<OperationResult> SaveAsync(SmsProviderInput input, CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(input.Provider))
            return OperationResult.Error("سرویس پیامک انتخاب‌شده معتبر نیست.");

        var key = input.ApiKey?.Trim();
        if (!string.IsNullOrEmpty(key) && !IsApiKey(key))
            return OperationResult.Error("کلید API معتبر نیست (۱۰ تا ۲۰۰ نویسهٔ انگلیسی/رقم، بدون فاصله).");

        if (!TryParsePositive(input.LineNumber, 5, 15, out var line))
            return OperationResult.Error("شمارهٔ خط ارسال باید فقط رقم باشد (۵ تا ۱۵ رقم). مثال: 30000123456");
        if (!TryParsePositive(input.OtpTemplateId, 1, 9, out var template) || template > int.MaxValue)
            return OperationResult.Error("شناسهٔ قالب کد تأیید باید عدد مثبت باشد. مثال: 718643");

        var parameter = string.IsNullOrWhiteSpace(input.OtpParameterName) ? Round8DefaultsSeeder.DefaultSmsOtpParameter : input.OtpParameterName.Trim().Trim('#');
        if (!ParameterRegex().IsMatch(parameter))
            return OperationResult.Error("نام متغیر قالب فقط حروف انگلیسی، رقم و _ (حداکثر ۵۰ نویسه). مثال: Code");

        if (input.Provider == SmsProvider.SmsIr && line is null && template is null)
            return OperationResult.Error("برای SMS.ir دست‌کم شمارهٔ خط ارسال یا شناسهٔ قالب کد تأیید لازم است.");

        var config = await LoadAsync(tracking: true, cancellationToken);
        if (config is null)
        {
            config = new SmsProviderConfig();
            context.SmsProviderConfigs.Add(config);
        }

        config.Provider = input.Provider;
        config.LineNumber = line;
        config.OtpTemplateId = template is null ? null : (int)template.Value;
        config.OtpParameterName = parameter;
        if (input.ClearApiKey)
            config.ApiKeyProtected = null;
        else if (!string.IsNullOrEmpty(key))
            config.ApiKeyProtected = secrets.Protect(key);

        await context.SaveChangesAsync(cancellationToken);
        cache.Remove(CacheKey);

        var warning = input.Provider == SmsProvider.SmsIr && string.IsNullOrEmpty(config.ApiKeyProtected)
            ? " کلید API در پنل ثبت نشده است؛ اگر روی سرور هم (SmsIr__ApiKey) تنظیم نباشد، پیامکی ارسال نمی‌شود."
            : "";
        return OperationResult.Success("تنظیمات سرویس پیامک ذخیره شد." + warning);
    }

    public async Task<SmsRuntimeConfig> GetRuntimeAsync(CancellationToken cancellationToken = default)
    {
        if (cache.TryGetValue<SmsRuntimeConfig>(CacheKey, out var cached) && cached is not null)
            return cached;

        var c = await LoadAsync(tracking: false, cancellationToken);
        var runtime = c is null
            ? SmsRuntimeConfig.Default
            : new SmsRuntimeConfig(c.Provider, string.IsNullOrEmpty(c.ApiKeyProtected) ? null : secrets.Unprotect(c.ApiKeyProtected),
                c.LineNumber, c.OtpTemplateId, string.IsNullOrWhiteSpace(c.OtpParameterName) ? Round8DefaultsSeeder.DefaultSmsOtpParameter : c.OtpParameterName);
        cache.Set(CacheKey, runtime, CacheLifetime);
        return runtime;
    }

    private Task<SmsProviderConfig?> LoadAsync(bool tracking, CancellationToken cancellationToken)
    {
        var query = tracking ? context.SmsProviderConfigs.AsTracking() : context.SmsProviderConfigs.AsNoTracking();
        return query.OrderBy(c => c.CreatedAt).FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>قالب کلید API پنل‌های پیامکی: ۱۰ تا ۲۰۰ نویسهٔ لاتین، رقم و - _ . = + /؛ بدون فاصله.</summary>
    public static bool IsApiKey(string value) => ApiKeyRegex().IsMatch(value);

    /// <summary>
    /// متن (با رقم فارسی هم) → عدد مثبت با تعداد رقم مجاز. خالی = null و معتبر.
    /// مثال: «۳۰۰۰۰۱۲۳۴۵۶» → 30000123456؛ «12a» → نامعتبر؛ «0» → نامعتبر.
    /// </summary>
    public static bool TryParsePositive(string? text, int minDigits, int maxDigits, out long? value)
    {
        value = null;
        var digits = SearchText.NormalizeDigits(text).Trim();
        if (digits.Length == 0)
            return true;
        if (digits.Length < minDigits || digits.Length > maxDigits || !digits.All(char.IsAsciiDigit))
            return false;
        var number = long.Parse(digits, System.Globalization.CultureInfo.InvariantCulture);
        if (number <= 0)
            return false;
        value = number;
        return true;
    }

    [GeneratedRegex(@"^[A-Za-z0-9_\-\.=\+/]{10,200}$")]
    private static partial Regex ApiKeyRegex();

    [GeneratedRegex(@"^[A-Za-z0-9_]{1,50}$")]
    private static partial Regex ParameterRegex();
}
