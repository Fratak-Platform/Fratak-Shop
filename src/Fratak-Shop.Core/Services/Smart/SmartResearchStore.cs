using System.Text.Json;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Smart;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Core.Services.Smart;

/// <summary>
/// کش پایدار نتیجهٔ تحقیق (B22) روی جدول SmartResearchCaches – پیش‌فرض ۳۰ روز.
/// <para>چرا؟ هر جست‌وجو در منابع بیرونی (دیجی‌کالا، ترب، بینگ، …) هم زمان می‌برد و هم اگر زیاد تکرار شود منبع ما را مسدود می‌کند.
/// با این کش، «تکمیل هوشمند» دوباره برای همان محصول، بدون هیچ درخواست بیرونی و فوری جواب می‌دهد.</para>
/// <para>کلیدها: «نوع:منبع:شناسه»، مثل «specs:digikala:12345» یا «images:q:هدفون x». کلیدهای بلند هش می‌شوند.</para>
/// <para>محل استفاده: SmartResearchClient و SmartImageFinder (لایهٔ وب)؛ پاک‌سازی شبانه در SmartWorker.</para>
/// </summary>
public interface ISmartResearchStore
{
    Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default);

    Task SetAsync<T>(string key, string source, T value, TimeSpan ttl, CancellationToken cancellationToken = default);

    /// <summary>حذف واقعی ردیف‌های منقضی (جدول کوچک بماند).</summary>
    Task<int> PruneAsync(CancellationToken cancellationToken = default);

    /// <summary>حذف همهٔ ردیف‌هایی که کلیدشان با این پیشوند شروع می‌شود (مثلاً «تحقیق دوباره» برای یک محصول).</summary>
    Task<int> ForgetAsync(string keyPrefix, CancellationToken cancellationToken = default);
}

public sealed class SmartResearchStore(FratakShopDbContext context) : ISmartResearchStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>کلید یکسان و حداکثر ۲۰۰ نویسه (کلید بلند ← پیشوند + هش).</summary>
    public static string NormalizeKey(string key)
    {
        var clean = (key ?? string.Empty).Trim().ToLowerInvariant();
        if (clean.Length <= 200)
            return clean;
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(clean)))[..32].ToLowerInvariant();
        return clean[..160] + "#" + hash;
    }

    public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeKey(key);
        var now = DateTimeOffset.UtcNow;
        var json = await context.SmartResearchCaches
            .Where(c => c.Key == normalized && c.ExpiresAt > now)
            .Select(c => c.Json).FirstOrDefaultAsync(cancellationToken);
        if (json is null)
            return default;
        try
        {
            return JsonSerializer.Deserialize<T>(json, Json);
        }
        catch (JsonException)
        {
            return default;
        }
    }

    public async Task SetAsync<T>(string key, string source, T value, TimeSpan ttl, CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeKey(key);
        var json = JsonSerializer.Serialize(value, Json);
        var expires = DateTimeOffset.UtcNow + ttl;
        var cleanSource = source.Length > 50 ? source[..50] : source;
        var now = DateTimeOffset.UtcNow;

        // به‌روزرسانی اگر هست؛ وگرنه افزودن (برخورد هم‌زمان ← نادیده، کش است).
        var updated = await context.SmartResearchCaches.IgnoreQueryFilters().Where(c => c.Key == normalized)
            .ExecuteUpdateAsync(s => s
                .SetProperty(c => c.Json, json)
                .SetProperty(c => c.Source, cleanSource)
                .SetProperty(c => c.ExpiresAt, expires)
                .SetProperty(c => c.IsDeleted, false)
                .SetProperty(c => c.UpdatedAt, now), cancellationToken);
        if (updated > 0)
            return;
        context.SmartResearchCaches.Add(new SmartResearchCache { Key = normalized, Source = cleanSource, Json = json, ExpiresAt = expires });
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // درج هم‌زمان همان کلید؛ یکی برنده شد و کافی است.
        }
        finally
        {
            context.ChangeTracker.Clear();
        }
    }

    public Task<int> PruneAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        return context.SmartResearchCaches.IgnoreQueryFilters().Where(c => c.ExpiresAt < now || c.IsDeleted).ExecuteDeleteAsync(cancellationToken);
    }

    public Task<int> ForgetAsync(string keyPrefix, CancellationToken cancellationToken = default)
    {
        var prefix = NormalizeKey(keyPrefix);
        return context.SmartResearchCaches.IgnoreQueryFilters().Where(c => c.Key.StartsWith(prefix)).ExecuteDeleteAsync(cancellationToken);
    }
}
