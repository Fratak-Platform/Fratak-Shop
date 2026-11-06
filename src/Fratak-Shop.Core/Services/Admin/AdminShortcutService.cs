namespace Fratak_Shop.Core.Services.Admin;

/// <summary>یک صفحهٔ سنجاق‌شده در منوی کناری. مثال: <c>new PinItem("/admin/orders?status=Paid", "سفارش‌های پرداخت‌شده", "Orders")</c>.</summary>
public sealed record PinItem(string Url, string Title, string Icon);

/// <summary>صفحه‌ای که کارمند اخیراً دیده است (برای «صفحه‌های اخیر» در جست‌وجوی سریع و صفحهٔ میانبرها).</summary>
public sealed record RecentItem(string Url, string Title, DateTimeOffset At);

/// <summary>یک فیلتر ذخیره‌شده: نام دلخواه + رشتهٔ کوئری، مثلاً <c>("COD امروز", "?pay=COD&amp;from=1404/08/03")</c>.</summary>
public sealed record SavedFilter(string Name, string Query);

/// <summary>
/// میانبرهای شخصی هر کارمند در پنل: سنجاق‌ها، صفحه‌های اخیر و فیلترهای ذخیره‌شده.
/// <para>
/// داده‌ها با <see cref="IAdminPreferenceService"/> در جدول تنظیمات شخصی ذخیره می‌شوند
/// (کلیدهای <c>pins</c>، <c>recent</c> و <c>filters:{path}</c>)؛ پس جدول جدیدی لازم نیست.
/// </para>
/// محل استفاده: <c>_AdminLayout</c> (نمایش سنجاق‌ها و ثبت صفحهٔ اخیر)، صفحهٔ <c>/admin/ux</c> (هندلرهای JSON که
/// <c>admin-ux.js</c> صدا می‌زند) و صفحهٔ «میانبرهای من».
/// </summary>
public interface IAdminShortcutService
{
    Task<List<PinItem>> GetPinsAsync(Guid userId);

    /// <summary>اگر آدرس سنجاق شده باشد برداشته می‌شود و برعکس. خروجی: آیا الان سنجاق است؟ (null یعنی ورودی نامعتبر یا سقف پر)</summary>
    Task<bool?> TogglePinAsync(Guid userId, string url, string title, string? icon);

    Task<List<RecentItem>> GetRecentsAsync(Guid userId);

    /// <summary>ثبت بازدید یک صفحه؛ اگر همان صفحهٔ آخر باشد چیزی نوشته نمی‌شود (برای کم کردن نوشتن در دیتابیس).</summary>
    Task TrackRecentAsync(Guid userId, string url, string title);

    Task<List<SavedFilter>> GetFiltersAsync(Guid userId, string path);

    /// <summary>ذخیرهٔ فیلتر؛ اگر هم‌نام داشته باشد جایگزین می‌شود. خروجی: پیام خطا یا null.</summary>
    Task<string?> SaveFilterAsync(Guid userId, string path, string name, string query);

    Task DeleteFilterAsync(Guid userId, string path, string name);

    /// <summary>همهٔ فیلترهای ذخیره‌شدهٔ کاربر (برای صفحهٔ میانبرها): مسیر → فهرست.</summary>
    Task<Dictionary<string, List<SavedFilter>>> GetAllFiltersAsync(Guid userId);
}

public sealed class AdminShortcutService(IAdminPreferenceService preferences) : IAdminShortcutService
{
    public const int MaxPins = 12;
    public const int MaxRecents = 8;
    public const int MaxFiltersPerPage = 12;
    private const string PinsKey = "pins";
    private const string RecentKey = "recent";
    private const string FiltersPrefix = "filters:";
    private const string FilterIndexKey = "filters";

    /// <summary>
    /// فقط آدرس‌های داخلی پنل پذیرفته می‌شوند تا لینک خارجی یا جاوااسکریپتی ذخیره نشود.
    /// مثال: «/admin/orders?status=Paid» معتبر؛ «https://x.com» یا «//x.com» نامعتبر.
    /// </summary>
    public static bool IsAdminUrl(string? url) =>
        !string.IsNullOrWhiteSpace(url) && url.Length <= 300
        && url.StartsWith("/admin", StringComparison.OrdinalIgnoreCase)
        && !url.StartsWith("//", StringComparison.Ordinal) && !url.Contains('\\') && !url.Any(char.IsControl);

    /// <summary>مسیر بدون کوئری و با حروف کوچک (کلید فیلترهای یک صفحه). مثال: «/Admin/Orders/?x=1» → «/admin/orders».</summary>
    public static string NormalizePath(string path)
    {
        var p = path.Split('?', '#')[0].TrimEnd('/').ToLowerInvariant();
        return p.Length == 0 ? "/admin" : p;
    }

    private static string Clip(string? text, int max)
    {
        var value = (text ?? "").Trim();
        return value.Length > max ? value[..max] : value;
    }

    public async Task<List<PinItem>> GetPinsAsync(Guid userId) =>
        await preferences.GetAsync<List<PinItem>>(userId, PinsKey) ?? [];

    public async Task<bool?> TogglePinAsync(Guid userId, string url, string title, string? icon)
    {
        if (!IsAdminUrl(url) || string.IsNullOrWhiteSpace(title))
            return null;

        var pins = await GetPinsAsync(userId);
        var index = pins.FindIndex(p => string.Equals(p.Url, url, StringComparison.OrdinalIgnoreCase));
        if (index >= 0)
        {
            pins.RemoveAt(index);
            await preferences.SetAsync(userId, PinsKey, pins);
            return false;
        }

        if (pins.Count >= MaxPins)
            return null;

        // نام آیکن فقط حروف لاتین باشد (به Icons.Svg داده می‌شود).
        var safeIcon = icon is { Length: > 0 and <= 40 } && icon.All(char.IsAsciiLetterOrDigit) ? icon : "PushPin";
        pins.Add(new PinItem(url, Clip(title, 60), safeIcon));
        await preferences.SetAsync(userId, PinsKey, pins);
        return true;
    }

    public async Task<List<RecentItem>> GetRecentsAsync(Guid userId) =>
        await preferences.GetAsync<List<RecentItem>>(userId, RecentKey) ?? [];

    public async Task TrackRecentAsync(Guid userId, string url, string title)
    {
        if (!IsAdminUrl(url) || string.IsNullOrWhiteSpace(title))
            return;

        var recents = await GetRecentsAsync(userId);
        if (recents.Count > 0 && string.Equals(recents[0].Url, url, StringComparison.OrdinalIgnoreCase))
            return;

        recents.RemoveAll(r => string.Equals(r.Url, url, StringComparison.OrdinalIgnoreCase));
        recents.Insert(0, new RecentItem(url, Clip(title, 80), DateTimeOffset.UtcNow));
        if (recents.Count > MaxRecents)
            recents.RemoveRange(MaxRecents, recents.Count - MaxRecents);
        await preferences.SetAsync(userId, RecentKey, recents);
    }

    public async Task<List<SavedFilter>> GetFiltersAsync(Guid userId, string path) =>
        await preferences.GetAsync<List<SavedFilter>>(userId, FiltersPrefix + NormalizePath(path)) ?? [];

    public async Task<string?> SaveFilterAsync(Guid userId, string path, string name, string query)
    {
        path = NormalizePath(path);
        name = Clip(name, 40);
        query = (query ?? "").Trim();
        if (!IsAdminUrl(path))
            return "آدرس صفحه نامعتبر است.";
        if (name.Length == 0)
            return "یک نام برای فیلتر بنویسید.";
        if (query.Length == 0 || query == "?")
            return "ابتدا فیلتری اعمال کنید، بعد ذخیره کنید.";
        if (!query.StartsWith('?'))
            query = "?" + query;
        if (query.Length > 600 || query.Any(char.IsControl))
            return "فیلتر بیش از حد طولانی است.";

        var filters = await GetFiltersAsync(userId, path);
        filters.RemoveAll(f => string.Equals(f.Name, name, StringComparison.Ordinal));
        if (filters.Count >= MaxFiltersPerPage)
            return $"حداکثر {MaxFiltersPerPage} فیلتر برای هر صفحه قابل ذخیره است.";

        filters.Add(new SavedFilter(name, query));
        if (!await preferences.SetAsync(userId, FiltersPrefix + path, filters))
            return "ذخیره انجام نشد. دوباره تلاش کنید.";

        // فهرست مسیرهایی که فیلتر دارند (برای صفحهٔ میانبرها).
        var index = await preferences.GetAsync<List<string>>(userId, FilterIndexKey) ?? [];
        if (!index.Contains(path))
        {
            index.Add(path);
            await preferences.SetAsync(userId, FilterIndexKey, index);
        }

        return null;
    }

    public async Task DeleteFilterAsync(Guid userId, string path, string name)
    {
        path = NormalizePath(path);
        var filters = await GetFiltersAsync(userId, path);
        if (filters.RemoveAll(f => string.Equals(f.Name, name, StringComparison.Ordinal)) == 0)
            return;

        if (filters.Count == 0)
        {
            await preferences.RemoveAsync(userId, FiltersPrefix + path);
            var index = await preferences.GetAsync<List<string>>(userId, FilterIndexKey) ?? [];
            if (index.Remove(path))
                await preferences.SetAsync(userId, FilterIndexKey, index);
        }
        else
        {
            await preferences.SetAsync(userId, FiltersPrefix + path, filters);
        }
    }

    public async Task<Dictionary<string, List<SavedFilter>>> GetAllFiltersAsync(Guid userId)
    {
        var result = new Dictionary<string, List<SavedFilter>>();
        foreach (var path in await preferences.GetAsync<List<string>>(userId, FilterIndexKey) ?? [])
        {
            var filters = await GetFiltersAsync(userId, path);
            if (filters.Count > 0)
                result[path] = filters;
        }

        return result;
    }
}
