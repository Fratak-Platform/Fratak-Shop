using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Searches;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Fratak_Shop.Core.Services.Searches;

/// <summary>
/// دور ۲۴ (پیشنهاد ۳۰ – رتبه‌بندی یادگیرنده): یاد گرفتن از کلیک واقعی مشتری‌ها روی نتیجه‌های جست‌وجو.
/// <para>
/// چطور کار می‌کند؟ مشتری «هدفون بی سیم» را جست‌وجو می‌کند و روی کالای X کلیک می‌کند ← مرورگر با sendBeacon یک
/// POST /api/search/click می‌فرستد ← این سرویس شمارندهٔ (عبارت، کالا) را یکی زیاد می‌کند. در مرتب‌سازی «مرتبط‌ترین»
/// (ProductSortBy.Relevance) کالاهای پرکلیک همان عبارت بالاتر می‌آیند.
/// </para>
/// <para>ضد تقلب و سبکی: هر مرورگر برای هر جفت (عبارت، کالا) روزی فقط یک کلیک حساب می‌شود و حداکثر ۳۰ کلیک در ساعت؛
/// فقط کالای فعال؛ عبارت ۲ تا ۱۰۰ نویسه. هیچ دادهٔ شخصی ذخیره نمی‌شود (فقط عبارت و شناسهٔ کالا).</para>
/// <para>نگه‌داری روزانه (<see cref="MaintainAsync"/>): ردیف‌هایی که ۳۰ روز کلیک تازه نگرفته‌اند نصف می‌شوند (سلیقهٔ تازه مهم‌تر است)
/// و ردیف‌های قدیمی‌تر از ۱۸۰ روز پاک می‌شوند.</para>
/// <para>محل استفاده: Web/Infrastructure/Commerce/CommerceEndpoints.cs (ثبت کلیک)، Pages/Search.cshtml.cs (مرتب‌سازی)،
/// Web/Infrastructure/Engagement/EngagementMaintenanceService.cs (نگه‌داری).</para>
/// </summary>
public interface ISearchLearningService
{
    /// <summary>حداکثر کلیک شمرده‌شدهٔ هر مرورگر در ساعت.</summary>
    const int MaxClicksPerHour = 30;

    /// <summary>
    /// ثبت یک کلیک. مثال: («هدفون بی‌سیم»، X، «visitor-1») ← true؛ همان کلیک دوباره در همان روز ← false.
    /// </summary>
    Task<bool> RecordClickAsync(string? query, Guid productId, string? visitorKey, CancellationToken cancellationToken = default);

    /// <summary>کم‌وزن کردن کلیک‌های قدیمی و پاک کردن ردیف‌های خیلی قدیمی. خروجی: تعداد ردیف‌های تغییرکرده.</summary>
    Task<int> MaintainAsync(CancellationToken cancellationToken = default);
}

/// <summary>ابزار ساده و قابل آزمون برای کلید عبارت جست‌وجو.</summary>
public static class SearchLearning
{
    /// <summary>
    /// کلید نرمال‌شدهٔ عبارت: حروف عربی ← فارسی، ارقام ← لاتین، نیم‌فاصله ← فاصله، حروف کوچک، حداکثر ۱۰۰ نویسه.
    /// مثال: «  هدفون‌ بي سيم » ← «هدفون بی سیم»؛ «a» یا «» ← null (کوتاه‌تر از ۲ نویسه).
    /// </summary>
    public static string? Key(string? query)
    {
        var text = SearchText.Normalize(query).ToLowerInvariant();
        return text.Length is < 2 or > SearchText.MaxLength ? null : text;
    }
}

/// <inheritdoc cref="ISearchLearningService"/>
public sealed class SearchLearningService(FratakShopDbContext context, IMemoryCache cache) : ISearchLearningService
{
    private static readonly TimeSpan DedupeWindow = TimeSpan.FromDays(1);

    public async Task<bool> RecordClickAsync(string? query, Guid productId, string? visitorKey, CancellationToken cancellationToken = default)
    {
        var key = SearchLearning.Key(query);
        if (key is null || productId == Guid.Empty || string.IsNullOrWhiteSpace(visitorKey) || visitorKey.Length > 64)
            return false;

        // یک کلیک در روز برای هر جفت و سقف ساعتی هر مرورگر (در حافظه؛ بدون پایگاه داده).
        var dedupe = $"sclick:{visitorKey}:{productId:N}:{key}";
        if (cache.TryGetValue(dedupe, out _))
            return false;
        var hourKey = $"sclick-n:{visitorKey}";
        var count = cache.TryGetValue(hourKey, out int seen) ? seen : 0;
        if (count >= ISearchLearningService.MaxClicksPerHour)
            return false;

        if (!await context.Products.AnyAsync(p => p.Id == productId && p.IsActive, cancellationToken))
            return false;

        cache.Set(dedupe, true, new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = DedupeWindow, Size = 1 });
        cache.Set(hourKey, count + 1, new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1), Size = 1 });

        var now = DateTimeOffset.UtcNow;
        if (await IncrementAsync(key, productId, now, cancellationToken) > 0)
            return true;

        context.SearchClickStats.Add(new SearchClickStat { NormalizedQuery = key, ProductId = productId, Clicks = 1, LastClickAt = now });
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // دو کلیک هم‌زمان روی یک جفت تازه: ردیف را دیگری ساخته؛ فقط شمارنده را زیاد می‌کنیم.
            context.ChangeTracker.Clear();
            await IncrementAsync(key, productId, now, cancellationToken);
        }
        return true;
    }

    private Task<int> IncrementAsync(string key, Guid productId, DateTimeOffset now, CancellationToken cancellationToken) =>
        context.SearchClickStats
            .Where(s => s.NormalizedQuery == key && s.ProductId == productId)
            .ExecuteUpdateAsync(u => u
                .SetProperty(s => s.Clicks, s => s.Clicks + 1)
                .SetProperty(s => s.LastClickAt, now)
                .SetProperty(s => s.UpdatedAt, now), cancellationToken);

    public async Task<int> MaintainAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var stale = now.AddDays(-30);
        var old = now.AddDays(-180);

        var removed = await context.SearchClickStats.IgnoreQueryFilters()
            .Where(s => s.LastClickAt < old || s.IsDeleted)
            .ExecuteDeleteAsync(cancellationToken);

        // UpdatedAt «آخرین تغییر» است: ردیفی که ۳۰ روز نه کلیک گرفته نه نصف شده، یک بار دیگر نصف می‌شود.
        var halved = await context.SearchClickStats
            .Where(s => s.UpdatedAt < stale && s.Clicks > 1)
            .ExecuteUpdateAsync(u => u
                .SetProperty(s => s.Clicks, s => s.Clicks / 2)
                .SetProperty(s => s.UpdatedAt, now), cancellationToken);

        return removed + halved;
    }
}
