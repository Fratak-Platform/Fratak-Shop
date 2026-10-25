using System.Text.Json;
using System.Text.RegularExpressions;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Admin;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Core.Services.Admin;

/// <summary>یک پیش‌نویس برای نمایش/بازیابی در فرم. <see cref="Data"/> همان JSON ذخیره‌شده است.</summary>
public sealed record EditDraftDto(string FormKey, long BaseStamp, string? Title, string? Url, string Data, DateTimeOffset SavedAt);

/// <summary>خلاصهٔ یک پیش‌نویس برای فهرست «پیش‌نویس‌های من» (بدون داده‌های حجیم).</summary>
public sealed record EditDraftSummary(string FormKey, string? Title, string? Url, DateTimeOffset SavedAt);

/// <summary>
/// ذخیرهٔ خودکار فرم‌های ویرایش پنل (پیش‌نویس) و بازیابی آن.
/// <para>
/// چرا لازم است؟ کارمند ممکن است وسط نوشتن متن یک مقاله مرورگر را ببندد، اینترنتش قطع شود یا هم‌زمان کس دیگری
/// همان محصول را ذخیره کند. اسکریپت <c>admin-edit.js</c> مقدارهای فرم را هر چند ثانیه با
/// <c>POST /admin/ux?handler=Draft</c> به این سرویس می‌دهد و هنگام باز کردن دوبارهٔ فرم با
/// <c>GET /admin/ux?handler=Draft&amp;key=…</c> پس می‌گیرد.
/// </para>
/// <para>
/// قالب داده: <c>{"base":{"Name":["…"]},"mine":{"Name":["…"]}}</c> – base مقدارهای فرم هنگام باز شدن و mine مقدارهای فعلی کارمند.
/// ادغام سه‌طرفه (base / mine / نسخهٔ فعلی سرور) در مرورگر انجام می‌شود.
/// </para>
/// پیش‌نویس هر کارمند فقط برای خودش است؛ بعد از ذخیرهٔ موفق فرم صفحه‌ها <see cref="DeleteAsync"/> را صدا می‌زنند.
/// </summary>
public interface IEditDraftService
{
    /// <summary>پیش‌نویس کارمند برای یک فرم یا null.</summary>
    Task<EditDraftDto?> GetAsync(Guid userId, string formKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// ذخیره یا جایگزینی پیش‌نویس. برمی‌گرداند: null یعنی موفق، در غیر این صورت متن خطا (کلید نامعتبر، حجم زیاد، JSON خراب).
    /// </summary>
    /// <example><c>await drafts.SaveAsync(userId, "product:new", 0, "گوشی A15", "/admin/products/edit", json);</c></example>
    Task<string?> SaveAsync(Guid userId, string formKey, long baseStamp, string? title, string? url, string data, CancellationToken cancellationToken = default);

    /// <summary>حذف پیش‌نویس (بعد از ذخیرهٔ موفق یا وقتی کارمند «دور انداختن» را می‌زند).</summary>
    Task DeleteAsync(Guid userId, string formKey, CancellationToken cancellationToken = default);

    /// <summary>پیش‌نویس‌های کارمند، جدیدترین اول (صفحهٔ «میانبرهای من»).</summary>
    Task<IReadOnlyList<EditDraftSummary>> ListAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>پاک کردن پیش‌نویس‌های قدیمی‌تر از <paramref name="olderThan"/> (کار روزانهٔ نگهداری). تعداد حذف‌شده را برمی‌گرداند.</summary>
    Task<int> PurgeAsync(TimeSpan olderThan, CancellationToken cancellationToken = default);
}

public sealed partial class EditDraftService(FratakShopDbContext context) : IEditDraftService
{
    /// <summary>حداکثر طول JSON یک پیش‌نویس (حدود ۲ میلیون نویسه؛ متن یک مقالهٔ خیلی بلند هم جا می‌شود).</summary>
    public const int MaxDataLength = 2_000_000;

    /// <summary>هر کارمند حداکثر این تعداد پیش‌نویس دارد؛ قدیمی‌ترها خودکار حذف می‌شوند.</summary>
    public const int MaxDraftsPerUser = 50;

    /// <summary>کلید معتبر فرم: حروف کوچک انگلیسی، عدد و «: _ -»؛ مثل <c>manage:faq:3f2a…</c>.</summary>
    public static bool IsValidKey(string? key) => !string.IsNullOrEmpty(key) && KeyRegex().IsMatch(key);

    public async Task<EditDraftDto?> GetAsync(Guid userId, string formKey, CancellationToken cancellationToken = default)
    {
        if (!IsValidKey(formKey))
            return null;

        return await context.EditDrafts
            .Where(d => d.UserId == userId && d.FormKey == formKey)
            .Select(d => new EditDraftDto(d.FormKey, d.BaseStamp, d.Title, d.Url, d.Data, d.UpdatedAt))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<string?> SaveAsync(Guid userId, string formKey, long baseStamp, string? title, string? url, string data,
        CancellationToken cancellationToken = default)
    {
        if (!IsValidKey(formKey))
            return "کلید فرم نامعتبر است.";
        if (string.IsNullOrEmpty(data) || data.Length > MaxDataLength)
            return "حجم پیش‌نویس بیش از حد مجاز است.";
        if (!IsValidJson(data))
            return "دادهٔ پیش‌نویس نامعتبر است.";
        // آدرس فقط داخل پنل (جلوگیری از ذخیرهٔ لینک خارجی که بعداً در فهرست کلیک شود).
        if (url is not null && !AdminShortcutService.IsAdminUrl(url))
            url = null;
        title = string.IsNullOrWhiteSpace(title) ? null : Truncate(title.Trim(), 200);

        var draft = await context.EditDrafts.AsTracking()
            .FirstOrDefaultAsync(d => d.UserId == userId && d.FormKey == formKey, cancellationToken);
        if (draft is null)
        {
            draft = new EditDraft { UserId = userId, FormKey = formKey, Data = data };
            context.EditDrafts.Add(draft);
        }
        else
        {
            // حتی اگر داده عوض نشده باشد زمان ذخیره به‌روز شود (برای نمایش «ذخیره شد ۱۲:۳۰»).
            context.Entry(draft).Property(d => d.UpdatedAt).IsModified = true;
        }

        draft.BaseStamp = Math.Max(0, baseStamp);
        draft.Title = title;
        draft.Url = url;
        draft.Data = data;

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // دو تب هم‌زمان پیش‌نویس جدید ساختند (ایندکس یکتا) – تب دوم دفعهٔ بعد جایگزین می‌کند.
            context.ChangeTracker.Clear();
            return null;
        }

        await TrimAsync(userId, cancellationToken);
        return null;
    }

    public async Task DeleteAsync(Guid userId, string formKey, CancellationToken cancellationToken = default)
    {
        if (!IsValidKey(formKey))
            return;

        var drafts = await context.EditDrafts.AsTracking()
            .Where(d => d.UserId == userId && d.FormKey == formKey)
            .ToListAsync(cancellationToken);
        if (drafts.Count == 0)
            return;

        // پیش‌نویس ارزش نگهداری ندارد: حذف واقعی (نه نرم) تا جدول بزرگ نشود.
        context.EditDrafts.RemoveRange(drafts);
        await HardDeleteAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<EditDraftSummary>> ListAsync(Guid userId, CancellationToken cancellationToken = default) =>
        await context.EditDrafts
            .Where(d => d.UserId == userId)
            .OrderByDescending(d => d.UpdatedAt)
            .Select(d => new EditDraftSummary(d.FormKey, d.Title, d.Url, d.UpdatedAt))
            .Take(MaxDraftsPerUser)
            .ToListAsync(cancellationToken);

    public async Task<int> PurgeAsync(TimeSpan olderThan, CancellationToken cancellationToken = default)
    {
        var before = DateTimeOffset.UtcNow - olderThan;
        return await context.EditDrafts.IgnoreQueryFilters()
            .Where(d => d.UpdatedAt < before || d.IsDeleted)
            .ExecuteDeleteAsync(cancellationToken);
    }

    // فقط آخرین MaxDraftsPerUser پیش‌نویس هر کارمند نگه داشته می‌شود.
    private async Task TrimAsync(Guid userId, CancellationToken cancellationToken)
    {
        var extra = await context.EditDrafts.AsTracking()
            .Where(d => d.UserId == userId)
            .OrderByDescending(d => d.UpdatedAt)
            .Skip(MaxDraftsPerUser)
            .ToListAsync(cancellationToken);
        if (extra.Count == 0)
            return;
        context.EditDrafts.RemoveRange(extra);
        await HardDeleteAsync(cancellationToken);
    }

    // DbContext حذف‌ها را نرم می‌کند؛ اینجا ردیف‌های علامت‌خورده واقعاً پاک می‌شوند.
    private async Task HardDeleteAsync(CancellationToken cancellationToken)
    {
        var ids = context.ChangeTracker.Entries<EditDraft>()
            .Where(e => e.State == EntityState.Deleted)
            .Select(e => e.Entity.Id)
            .ToList();
        context.ChangeTracker.Clear();
        if (ids.Count > 0)
            await context.EditDrafts.IgnoreQueryFilters().Where(d => ids.Contains(d.Id)).ExecuteDeleteAsync(cancellationToken);
    }

    private static bool IsValidJson(string data)
    {
        try
        {
            using var document = JsonDocument.Parse(data);
            return document.RootElement.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];

    [GeneratedRegex(@"^[a-z0-9:_-]{1,120}\z")]
    private static partial Regex KeyRegex();
}
