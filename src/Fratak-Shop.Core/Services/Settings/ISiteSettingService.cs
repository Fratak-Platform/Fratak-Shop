using Fratak_Shop.Core.DTOs.Settings.Query;
using Fratak_Shop.Core.Utilities;

namespace Fratak_Shop.Core.Services.Settings;

/// <summary>
/// Reads and writes the key/value configuration of the shop. The service is the
/// only place that knows the setting keys, so no view or controller ever contains
/// a hard-coded configuration value.
/// </summary>
public interface ISiteSettingService
{
    /// <summary>تمام تنظیمات (اختیاری محدود به یک گروه) برای صفحه تنظیمات پنل مدیریت.</summary>
    Task<List<SiteSettingDto>> GetAllAsync(string? group = null);

    /// <summary>تنظیمات گروه‌بندی‌شده برای نمایش تب‌بندی‌شده در پنل مدیریت.</summary>
    Task<List<SettingGroupDto>> GetGroupedAsync();

    Task<SiteSettingDto?> GetAsync(string key);

    Task<string?> GetValueAsync(string key);

    Task<T> GetValueAsync<T>(string key, T fallback);

    /// <summary>تنظیمات آماده قالب سایت (هدر، فوتر، تماس، پرداخت).</summary>
    Task<StorefrontSettings> GetStorefrontSettingsAsync();

    Task<OperationResult> SetAsync(string key, string? value);

    /// <summary>ذخیره گروهی مقادیر ارسال‌شده از فرم تنظیمات.</summary>
    Task<OperationResult> SetManyAsync(IDictionary<string, string?> values);

    /// <summary>ساخت تنظیمات پیش‌فرض در اولین اجرا تا پنل مدیریت از ابتدا پر باشد.</summary>
    Task EnsureDefaultsAsync(CancellationToken cancellationToken = default);
}
