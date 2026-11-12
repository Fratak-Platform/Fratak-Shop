using System.Text.RegularExpressions;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Admin;
using Fratak_Shop.Data.Seeders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Fratak_Shop.Core.Services.Admin;

/// <summary>یک راهنمای صفحهٔ پنل برای نمایش یا ویرایش.</summary>
public sealed record AdminHelpDto(Guid Id, string Path, string Title, string Body, bool IsCustomized, bool HasDefault, DateTimeOffset UpdatedAt);

/// <summary>
/// راهنمای صفحه‌های پنل (دکمهٔ «راهنما» در نوار بالای هر صفحه).
/// <para>
/// مثال: برای آدرس <c>/admin/orders/5f0e…</c> به ترتیب کلیدهای <c>/admin/orders/*</c> و <c>/admin/orders</c>
/// جست‌وجو می‌شوند و اولین راهنمای موجود برگردانده می‌شود.
/// </para>
/// همهٔ راهنماها (چند ده ردیف کوچک) یک‌جا در حافظه کش می‌شوند و با هر ذخیره کش پاک می‌شود.
/// محل استفاده: <c>_AdminLayout</c> (کشوی راهنما) و صفحهٔ <c>/admin/help</c>.
/// </summary>
public interface IAdminHelpService
{
    Task<AdminHelpDto?> GetForPathAsync(string path);

    Task<List<AdminHelpDto>> ListAsync();

    Task<AdminHelpDto?> FindAsync(Guid id);

    /// <summary>ساخت یا ویرایش؛ متن با HtmlSanitizer پاک‌سازی می‌شود.</summary>
    Task<OperationResult<Guid>> SaveAsync(Guid? id, string path, string title, string body);

    /// <summary>بازگرداندن متن پیش‌فرض (فقط برای صفحه‌هایی که پیش‌فرض دارند).</summary>
    Task<OperationResult> ResetAsync(Guid id);

    Task<OperationResult> DeleteAsync(Guid id);
}

public sealed partial class AdminHelpService(FratakShopDbContext context, IMemoryCache cache) : IAdminHelpService
{
    private const string CacheKey = "admin:help:all";

    [GeneratedRegex("^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$|^\\d+$", RegexOptions.IgnoreCase)]
    private static partial Regex IdSegment();

    [GeneratedRegex("^/admin(/[a-z0-9*-]+)*$")]
    private static partial Regex ValidPath();

    /// <summary>
    /// کلیدهای قابل جست‌وجو برای یک آدرس، از دقیق به کلی.
    /// مثال: «/admin/manage/coupons/edit/5f0e…» →
    /// [«/admin/manage/coupons/edit/*»، «/admin/manage/coupons/edit»، «/admin/manage/coupons»، «/admin/manage»، «/admin»].
    /// </summary>
    public static List<string> ResolveKeys(string path)
    {
        var segments = (path ?? "").Split('?', '#')[0].ToLowerInvariant()
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => IdSegment().IsMatch(s) ? "*" : s).ToList();
        if (segments.Count == 0 || segments[0] != "admin")
            return ["/admin"];

        var keys = new List<string>();
        for (var length = segments.Count; length >= 1; length--)
        {
            var key = "/" + string.Join('/', segments.Take(length));
            if (!keys.Contains(key))
                keys.Add(key);
        }

        return keys;
    }

    private async Task<Dictionary<string, AdminHelpDto>> AllAsync() =>
        (await cache.GetOrCreateAsync(CacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(30);
            var defaults = AdminHelpDefaults.All.Select(d => d.Path).ToHashSet();
            var rows = await context.AdminHelpArticles.OrderBy(h => h.Path).ToListAsync();
            return rows.ToDictionary(h => h.Path, h => new AdminHelpDto(h.Id, h.Path, h.Title, h.Body, h.IsCustomized, defaults.Contains(h.Path), h.UpdatedAt));
        }))!;

    public async Task<AdminHelpDto?> GetForPathAsync(string path)
    {
        var all = await AllAsync();
        foreach (var key in ResolveKeys(path))
        {
            if (all.TryGetValue(key, out var help))
                return help;
        }

        return null;
    }

    public async Task<List<AdminHelpDto>> ListAsync() => (await AllAsync()).Values.ToList();

    public async Task<AdminHelpDto?> FindAsync(Guid id) => (await AllAsync()).Values.FirstOrDefault(h => h.Id == id);

    public async Task<OperationResult<Guid>> SaveAsync(Guid? id, string path, string title, string body)
    {
        path = (path ?? "").Trim().TrimEnd('/').ToLowerInvariant();
        title = (title ?? "").Trim();
        var html = body.SanitizeOrNull();
        if (!ValidPath().IsMatch(path) || path.Length > 150)
            return OperationResult<Guid>.Error("آدرس صفحه باید با /admin شروع شود و فقط حروف لاتین کوچک، عدد، «-» و «*» داشته باشد.");
        if (title.Length is 0 or > 150)
            return OperationResult<Guid>.Error("عنوان راهنما را (حداکثر ۱۵۰ نویسه) بنویسید.");
        if (html is null || html.Length > 20000)
            return OperationResult<Guid>.Error("متن راهنما خالی یا بیش از حد طولانی است.");

        var duplicate = await context.AdminHelpArticles.AnyAsync(h => h.Path == path && h.Id != (id ?? Guid.Empty));
        if (duplicate)
            return OperationResult<Guid>.Error("برای این آدرس قبلاً راهنما ثبت شده است.");

        AdminHelpArticle? row;
        if (id is { } existingId)
        {
            row = await context.AdminHelpArticles.AsTracking().FirstOrDefaultAsync(h => h.Id == existingId);
            if (row is null)
                return OperationResult<Guid>.NotFound();
        }
        else
        {
            // اگر راهنمای این آدرس قبلاً حذف شده بود، همان ردیف زنده می‌شود (ایندکس یکتا).
            row = await context.AdminHelpArticles.IgnoreQueryFilters().AsTracking().FirstOrDefaultAsync(h => h.Path == path);
            if (row is null)
            {
                row = new AdminHelpArticle();
                context.AdminHelpArticles.Add(row);
            }

            row.IsDeleted = false;
            row.DeletedAt = null;
        }

        row.Path = path;
        row.Title = title;
        row.Body = html;
        row.IsCustomized = true;
        await context.SaveChangesAsync();
        cache.Remove(CacheKey);
        return OperationResult<Guid>.Success(row.Id);
    }

    public async Task<OperationResult> ResetAsync(Guid id)
    {
        var row = await context.AdminHelpArticles.AsTracking().FirstOrDefaultAsync(h => h.Id == id);
        var preset = row is null ? null : AdminHelpDefaults.All.FirstOrDefault(d => d.Path == row.Path);
        if (row is null || preset is null)
            return OperationResult.Error("این صفحه متن پیش‌فرض ندارد.");

        row.Title = preset.Title;
        row.Body = preset.Body;
        row.IsCustomized = false;
        await context.SaveChangesAsync();
        cache.Remove(CacheKey);
        return OperationResult.Success();
    }

    public async Task<OperationResult> DeleteAsync(Guid id)
    {
        var row = await context.AdminHelpArticles.AsTracking().FirstOrDefaultAsync(h => h.Id == id);
        if (row is null)
            return OperationResult.Error("راهنما پیدا نشد.");

        // علامت «ویرایش‌شده» می‌ماند تا به‌روزرسانی پیش‌فرض‌ها آن را برنگرداند.
        row.IsCustomized = true;
        context.AdminHelpArticles.Remove(row);
        await context.SaveChangesAsync();
        cache.Remove(CacheKey);
        return OperationResult.Success();
    }
}
