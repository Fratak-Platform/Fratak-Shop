using System.Text.Json;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Admin;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Fratak_Shop.Core.Services.Admin;

/// <summary>
/// تنظیمات شخصی هر کارمند در پنل (ذخیره در جدول <see cref="AdminUserPreference"/>).
/// <para>
/// مثال کاربرد:
/// <c>await prefs.SetAsync(userId, "pins", new[] { "/admin/orders", "/admin/products" });</c>
/// <c>var pins = await prefs.GetAsync&lt;string[]&gt;(userId, "pins") ?? [];</c>
/// </para>
/// کلیدهای استفاده‌شده: <c>pins</c>، <c>recent</c>، <c>filters:{path}</c>، <c>columns:{table}</c>، <c>dashboard</c>،
/// <c>staff-notify:muted</c>. کلید حداکثر ۱۵۰ و مقدار حداکثر ۲۰ هزار نویسه است.
/// </summary>
public interface IAdminPreferenceService
{
    Task<T?> GetAsync<T>(Guid userId, string key);

    /// <summary>ذخیره (یا جایگزینی) یک مقدار؛ اگر بزرگ‌تر از حد مجاز باشد false برمی‌گرداند.</summary>
    Task<bool> SetAsync<T>(Guid userId, string key, T value);

    Task RemoveAsync(Guid userId, string key);
}

public sealed class AdminPreferenceService(FratakShopDbContext context, IMemoryCache cache) : IAdminPreferenceService
{
    private const int MaxValueLength = 20000;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<T?> GetAsync<T>(Guid userId, string key)
    {
        var raw = await cache.GetOrCreateAsync(CacheKey(userId, key), async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10);
            return await context.AdminUserPreferences
                .Where(p => p.UserId == userId && p.Key == key)
                .Select(p => p.Value).FirstOrDefaultAsync();
        });
        if (string.IsNullOrEmpty(raw))
            return default;

        try
        {
            return JsonSerializer.Deserialize<T>(raw, Json);
        }
        catch (JsonException)
        {
            // مقدار خراب (مثلاً بعد از تغییر ساختار) نادیده گرفته می‌شود.
            return default;
        }
    }

    public async Task<bool> SetAsync<T>(Guid userId, string key, T value)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > 150)
            return false;

        var json = JsonSerializer.Serialize(value, Json);
        if (json.Length > MaxValueLength)
            return false;

        var row = await context.AdminUserPreferences.AsTracking().FirstOrDefaultAsync(p => p.UserId == userId && p.Key == key);
        if (row is null)
            context.AdminUserPreferences.Add(new AdminUserPreference { UserId = userId, Key = key, Value = json });
        else
            row.Value = json;

        try
        {
            await context.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // دو درخواست هم‌زمان برای ایجاد یک کلید: یکی برنده می‌شود و این یکی نادیده گرفته می‌شود.
            context.ChangeTracker.Clear();
            return false;
        }

        cache.Remove(CacheKey(userId, key));
        return true;
    }

    public async Task RemoveAsync(Guid userId, string key)
    {
        var row = await context.AdminUserPreferences.AsTracking().FirstOrDefaultAsync(p => p.UserId == userId && p.Key == key);
        if (row is null)
            return;

        context.AdminUserPreferences.Remove(row);
        await context.SaveChangesAsync();
        cache.Remove(CacheKey(userId, key));
    }

    private static string CacheKey(Guid userId, string key) => $"admin:pref:{userId:N}:{key}";
}
