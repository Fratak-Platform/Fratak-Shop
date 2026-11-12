using Fratak_Shop.Core.DTOs.Admin;
using Fratak_Shop.Core.Services.Seo.Engine;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.SeoEngine;
using Fratak_Shop.Data.Utilities.SeoManagement;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Fratak_Shop.Core.Services.Seo.Tools;

// ═══════════════════════════════════════════════════════════════════════════
//  ویرایش گروهی سئو (مرحلهٔ هفتم – فاز ۸ب) — صفحهٔ /admin/seo/bulk
//  جدولی از عنوان سئو، توضیح متا و کلمهٔ کلیدی همهٔ صفحه‌های یک نوع (محصول، دسته، …) با فیلترهای «عنوان ندارد»،
//  «عنوان تکراری»، «نمرهٔ کم» و …؛ کارشناس چند ردیف را همان‌جا ویرایش و یک‌جا ذخیره می‌کند.
//  • فقط فیلدهای عوض‌شده ذخیره می‌شوند و اگر کس دیگری (یا موتور سئو) در این فاصله همان فیلد را عوض کرده باشد،
//    آن ردیف ذخیره نمی‌شود («کهنه») تا کار دیگران از بین نرود.
//  • «جست‌وجو و جایگزینی» روی ردیف‌های انتخاب‌شده، با پیش‌نمایش قبل از اعمال.
//  • بعد از ذخیره، نمرهٔ همان صفحه‌ها فوراً دوباره حساب می‌شود (ISeoEngineService.RefreshScoresAsync).
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>یک ردیف جدول. DefaultTitle = عنوانی که بدون عنوان سئو نمایش داده می‌شود (نام صفحه).</summary>
public sealed record SeoBulkRow(Guid Id, string Name, string Path, string? SeoTitle, string? MetaDescription, string? FocusKeyword,
    int? Score, bool DuplicateTitle);

/// <summary>فیلتر: all، no-title، no-meta، no-keyword، duplicate، low (نمرهٔ کمتر از ۷۰).</summary>
public sealed record SeoBulkQuery(SeoEntityKind Kind, string Filter, string? Search, int Page, int Take = 25);

/// <summary>تغییر یک ردیف. Original* = مقدار هنگام باز کردن صفحه (برای تشخیص ویرایش هم‌زمان).</summary>
public sealed record SeoBulkChange(Guid Id, string? OriginalTitle, string? Title, string? OriginalMeta, string? Meta, string? OriginalFocus, string? Focus);

public sealed record SeoBulkSaveResult(int Saved, int Stale, IReadOnlyList<string> Errors);

public enum SeoBulkField : byte
{
    Title = 1,
    MetaDescription = 2,
}

/// <summary>پیش‌نمایش جایگزینی برای یک ردیف.</summary>
public sealed record SeoReplacePreview(Guid Id, string Name, string Before, string After, bool TooLong);

/// <summary>سرویس ویرایش گروهی. محل استفاده: Web/Areas/Admin/Pages/Seo/Bulk.</summary>
public interface ISeoBulkEditService
{
    Task<PagedList<SeoBulkRow>> ListAsync(SeoBulkQuery query, CancellationToken cancellationToken = default);

    /// <summary>تعداد ردیف‌های هر فیلتر (برای برچسب زبانه‌ها).</summary>
    Task<IReadOnlyDictionary<string, int>> CountsAsync(SeoEntityKind kind, CancellationToken cancellationToken = default);

    Task<OperationResult<SeoBulkSaveResult>> SaveAsync(SeoEntityKind kind, IReadOnlyList<SeoBulkChange> changes, Guid? userId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SeoReplacePreview>> PreviewReplaceAsync(SeoEntityKind kind, IReadOnlyCollection<Guid> ids, SeoBulkField field, string find, string replacement, CancellationToken cancellationToken = default);
    Task<OperationResult<int>> ApplyReplaceAsync(SeoEntityKind kind, IReadOnlyCollection<Guid> ids, SeoBulkField field, string find, string replacement, Guid? userId, CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="ISeoBulkEditService"/>
public sealed class SeoBulkEditService(FratakShopDbContext context, ISeoEngineService engine, ILogger<SeoBulkEditService> logger) : ISeoBulkEditService
{
    public const int TitleMax = 200;
    public const int MetaMax = 500;
    public const int FocusMax = 200;

    /// <summary>حداکثر ردیف در یک ذخیره/جایگزینی.</summary>
    public const int MaxBatch = 300;

    public static readonly string[] Filters = ["all", "no-title", "no-meta", "no-keyword", "duplicate", "low"];

    /// <summary>ردیف خام (مستقل از نوع صفحه).</summary>
    private sealed record Item(Guid Id, string Name, string Path, string? SeoTitle, string? Meta, string? Focus);

    /// <summary>صفحهٔ tracked برای ذخیره: نام و دسترسی به فیلد Seo.</summary>
    private sealed record Holder(string Name, Func<SeoMetaData?> Get, Action<SeoMetaData> Set);

    // ── فهرست ────────────────────────────────────────────────────────────

    public async Task<PagedList<SeoBulkRow>> ListAsync(SeoBulkQuery query, CancellationToken cancellationToken = default)
    {
        var rows = await RowsAsync(query.Kind, cancellationToken);
        IEnumerable<SeoBulkRow> filtered = Apply(rows, query.Filter);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = PersianText.Normalize(query.Search);
            filtered = filtered.Where(r => PersianText.Normalize(r.Name + " " + r.SeoTitle + " " + r.FocusKeyword).Contains(term, StringComparison.Ordinal));
        }
        var list = filtered
            .OrderBy(r => r.Score ?? -1)
            .ThenBy(r => r.Name, StringComparer.Ordinal)
            .ToList();
        var take = Math.Clamp(query.Take, 10, 100);
        var page = Math.Clamp(query.Page, 1, Math.Max(1, (list.Count + take - 1) / take));
        return new PagedList<SeoBulkRow>(list.Skip((page - 1) * take).Take(take).ToList(), list.Count, page, take);
    }

    public async Task<IReadOnlyDictionary<string, int>> CountsAsync(SeoEntityKind kind, CancellationToken cancellationToken = default)
    {
        var rows = await RowsAsync(kind, cancellationToken);
        return Filters.ToDictionary(f => f, f => Apply(rows, f).Count(), StringComparer.Ordinal);
    }

    /// <summary>
    /// اعمال فیلتر. «تکراری» = عنوان مؤثر (عنوان سئو یا نام) بعد از یکسان‌سازی فارسی در بیش از یک صفحه از همین نوع.
    /// </summary>
    public static IEnumerable<SeoBulkRow> Apply(IReadOnlyList<SeoBulkRow> rows, string? filter) => filter switch
    {
        "no-title" => rows.Where(r => string.IsNullOrWhiteSpace(r.SeoTitle)),
        "no-meta" => rows.Where(r => string.IsNullOrWhiteSpace(r.MetaDescription)),
        "no-keyword" => rows.Where(r => string.IsNullOrWhiteSpace(r.FocusKeyword)),
        "duplicate" => rows.Where(r => r.DuplicateTitle),
        "low" => rows.Where(r => r.Score is null or < 70),
        _ => rows,
    };

    private async Task<List<SeoBulkRow>> RowsAsync(SeoEntityKind kind, CancellationToken cancellationToken)
    {
        var items = await ItemsAsync(kind, null, cancellationToken);
        var scores = await context.SeoScores.AsNoTracking()
            .Where(s => s.Kind == kind)
            .Select(s => new { s.EntityId, s.Score })
            .ToDictionaryAsync(s => s.EntityId, s => s.Score, cancellationToken);
        var titleCounts = items
            .GroupBy(i => PersianText.Normalize(string.IsNullOrWhiteSpace(i.SeoTitle) ? i.Name : i.SeoTitle))
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        return items.Select(i => new SeoBulkRow(i.Id, i.Name, i.Path, i.SeoTitle, i.Meta, i.Focus,
                scores.TryGetValue(i.Id, out var score) ? score : null,
                titleCounts.GetValueOrDefault(PersianText.Normalize(string.IsNullOrWhiteSpace(i.SeoTitle) ? i.Name : i.SeoTitle)) > 1))
            .ToList();
    }

    /// <summary>صفحه‌های فعال/منتشرشدهٔ یک نوع (ids = فقط همین‌ها).</summary>
    private async Task<List<Item>> ItemsAsync(SeoEntityKind kind, IReadOnlyCollection<Guid>? ids, CancellationToken cancellationToken)
    {
        var idList = ids?.ToList();
        switch (kind)
        {
            case SeoEntityKind.Product:
                return (await context.Products.AsNoTracking().Where(p => p.IsActive && (idList == null || idList.Contains(p.Id)))
                        .Select(p => new { p.Id, p.Name, p.Slug, T = p.Seo!.Title, M = p.Seo!.MetaDescription, F = p.Seo!.FocusKeyword })
                        .ToListAsync(cancellationToken))
                    .Select(p => new Item(p.Id, p.Name, "/product/" + p.Slug, p.T, p.M, p.F)).ToList();
            case SeoEntityKind.Category:
                return (await context.Categories.AsNoTracking().Where(c => c.IsActive && (idList == null || idList.Contains(c.Id)))
                        .Select(c => new { c.Id, c.Name, c.Slug, T = c.Seo!.Title, M = c.Seo!.MetaDescription, F = c.Seo!.FocusKeyword })
                        .ToListAsync(cancellationToken))
                    .Select(c => new Item(c.Id, c.Name, "/category/" + c.Slug, c.T, c.M, c.F)).ToList();
            case SeoEntityKind.Brand:
                return (await context.Brands.AsNoTracking().Where(b => b.IsActive && (idList == null || idList.Contains(b.Id)))
                        .Select(b => new { b.Id, b.Name, b.Slug, T = b.Seo!.Title, M = b.Seo!.MetaDescription, F = b.Seo!.FocusKeyword })
                        .ToListAsync(cancellationToken))
                    .Select(b => new Item(b.Id, b.Name, "/brand/" + b.Slug, b.T, b.M, b.F)).ToList();
            case SeoEntityKind.BlogPost:
                return (await context.BlogPosts.AsNoTracking().Where(p => p.IsPublished && (idList == null || idList.Contains(p.Id)))
                        .Select(p => new { p.Id, p.Title, p.Slug, T = p.Seo!.Title, M = p.Seo!.MetaDescription, F = p.Seo!.FocusKeyword })
                        .ToListAsync(cancellationToken))
                    .Select(p => new Item(p.Id, p.Title, "/blog/" + p.Slug, p.T, p.M, p.F)).ToList();
            case SeoEntityKind.ContentPage:
                return (await context.ContentPages.AsNoTracking().Where(p => p.IsActive && (idList == null || idList.Contains(p.Id)))
                        .Select(p => new { p.Id, p.Title, p.Slug, T = p.Seo!.Title, M = p.Seo!.MetaDescription, F = p.Seo!.FocusKeyword })
                        .ToListAsync(cancellationToken))
                    .Select(p => new Item(p.Id, p.Title, "/page/" + p.Slug, p.T, p.M, p.F)).ToList();
            case SeoEntityKind.StaticPage:
            {
                // صفحه‌های ثابت (مرحلهٔ شانزدهم): همیشه همهٔ صفحه‌ها نشان داده می‌شوند، حتی اگر هنوز ردیف PageSeo نداشته باشند.
                var pages = StaticSeoPages.All.Where(p => idList == null || idList.Contains(StaticSeoPages.IdOf(p.Page))).ToList();
                var keys = pages.Select(p => p.Page).ToList();
                var rows = await context.PageSeos.AsNoTracking().Where(p => keys.Contains(p.Page))
                    .Select(p => new { p.Page, T = p.Seo!.Title, M = p.Seo!.MetaDescription, F = p.Seo!.FocusKeyword })
                    .ToListAsync(cancellationToken);
                return pages.Select(p =>
                {
                    var row = rows.FirstOrDefault(r => r.Page == p.Page);
                    return new Item(StaticSeoPages.IdOf(p.Page), StaticSeoPages.Label(p.Page), p.Path, row?.T, row?.M, row?.F);
                }).ToList();
            }
            default:
                return [];
        }
    }

    /// <summary>صفحه‌های tracked برای نوشتن (فقط فعال‌ها).</summary>
    private async Task<Dictionary<Guid, Holder>> TrackedAsync(SeoEntityKind kind, IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        var idList = ids.ToList();
        switch (kind)
        {
            case SeoEntityKind.Product:
                return (await context.Products.AsTracking().Where(p => p.IsActive && idList.Contains(p.Id)).ToListAsync(cancellationToken))
                    .ToDictionary(p => p.Id, p => new Holder(p.Name, () => p.Seo, s => p.Seo = s));
            case SeoEntityKind.Category:
                return (await context.Categories.AsTracking().Where(c => c.IsActive && idList.Contains(c.Id)).ToListAsync(cancellationToken))
                    .ToDictionary(c => c.Id, c => new Holder(c.Name, () => c.Seo, s => c.Seo = s));
            case SeoEntityKind.Brand:
                return (await context.Brands.AsTracking().Where(b => b.IsActive && idList.Contains(b.Id)).ToListAsync(cancellationToken))
                    .ToDictionary(b => b.Id, b => new Holder(b.Name, () => b.Seo, s => b.Seo = s));
            case SeoEntityKind.BlogPost:
                return (await context.BlogPosts.AsTracking().Where(p => p.IsPublished && idList.Contains(p.Id)).ToListAsync(cancellationToken))
                    .ToDictionary(p => p.Id, p => new Holder(p.Title, () => p.Seo, s => p.Seo = s));
            case SeoEntityKind.ContentPage:
                return (await context.ContentPages.AsTracking().Where(p => p.IsActive && idList.Contains(p.Id)).ToListAsync(cancellationToken))
                    .ToDictionary(p => p.Id, p => new Holder(p.Title, () => p.Seo, s => p.Seo = s));
            case SeoEntityKind.StaticPage:
            {
                var pages = StaticSeoPages.All.Where(p => idList.Contains(StaticSeoPages.IdOf(p.Page))).ToList();
                var keys = pages.Select(p => p.Page).ToList();
                var rows = await context.PageSeos.AsTracking().Where(p => keys.Contains(p.Page)).ToListAsync(cancellationToken);
                var result = new Dictionary<Guid, Holder>();
                foreach (var page in pages)
                {
                    var row = rows.FirstOrDefault(r => r.Page == page.Page);
                    if (row is null)
                    {
                        // ردیف فقط وقتی واقعاً ذخیره شود ساخته می‌شود (Set صدا زده شود).
                        var created = new Fratak_Shop.Data.Entities.Pages.PageSeo { Page = page.Page };
                        result[StaticSeoPages.IdOf(page.Page)] = new Holder(StaticSeoPages.Label(page.Page), () => created.Seo, s =>
                        {
                            created.Seo = s;
                            if (context.Entry(created).State == EntityState.Detached)
                                context.PageSeos.Add(created);
                        });
                    }
                    else
                        result[StaticSeoPages.IdOf(page.Page)] = new Holder(StaticSeoPages.Label(page.Page), () => row.Seo, s => row.Seo = s);
                }
                return result;
            }
            default:
                return new Dictionary<Guid, Holder>();
        }
    }

    // ── ذخیره ────────────────────────────────────────────────────────────

    /// <summary>متن فرم → مقدار ذخیره: فاصله‌های اضافه حذف، خالی = null.</summary>
    public static string? Clean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        return string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    public async Task<OperationResult<SeoBulkSaveResult>> SaveAsync(SeoEntityKind kind, IReadOnlyList<SeoBulkChange> changes, Guid? userId,
        CancellationToken cancellationToken = default)
    {
        // فقط ردیف‌هایی که واقعاً تغییری دارند.
        var effective = changes
            .Where(c => Clean(c.Title) != Clean(c.OriginalTitle) || Clean(c.Meta) != Clean(c.OriginalMeta) || Clean(c.Focus) != Clean(c.OriginalFocus))
            .GroupBy(c => c.Id)
            .Select(g => g.Last())
            .ToList();
        if (effective.Count == 0)
            return OperationResult<SeoBulkSaveResult>.Error("تغییری برای ذخیره نیست.");
        if (effective.Count > MaxBatch)
            return OperationResult<SeoBulkSaveResult>.Error($"در هر بار حداکثر {PersianText.Digits(MaxBatch)} ردیف ذخیره می‌شود.");

        var holders = await TrackedAsync(kind, effective.Select(c => c.Id).ToList(), cancellationToken);
        var errors = new List<string>();
        var saved = new List<Guid>();
        var stale = 0;
        var now = DateTimeOffset.UtcNow;
        foreach (var change in effective)
        {
            if (!holders.TryGetValue(change.Id, out var holder))
            {
                errors.Add("یک صفحه پیدا نشد (حذف یا غیرفعال شده).");
                continue;
            }
            var current = holder.Get();
            var title = Clean(change.Title);
            var meta = Clean(change.Meta);
            var focus = Clean(change.Focus);
            var titleChanged = title != Clean(change.OriginalTitle);
            var metaChanged = meta != Clean(change.OriginalMeta);
            var focusChanged = focus != Clean(change.OriginalFocus);

            // ویرایش هم‌زمان: فیلدی که می‌خواهیم عوض کنیم نباید از زمان باز شدن صفحه تغییر کرده باشد.
            if ((titleChanged && Clean(current?.Title) != Clean(change.OriginalTitle)) ||
                (metaChanged && Clean(current?.MetaDescription) != Clean(change.OriginalMeta)) ||
                (focusChanged && Clean(current?.FocusKeyword) != Clean(change.OriginalFocus)))
            {
                stale++;
                errors.Add($"«{holder.Name}»: در این فاصله کس دیگری آن را ویرایش کرده؛ صفحه را تازه کنید.");
                continue;
            }
            if (title?.Length > TitleMax || meta?.Length > MetaMax || focus?.Length > FocusMax)
            {
                errors.Add($"«{holder.Name}»: عنوان حداکثر {PersianText.Digits(TitleMax)}، توضیح حداکثر {PersianText.Digits(MetaMax)} و کلمهٔ کلیدی حداکثر {PersianText.Digits(FocusMax)} نویسه.");
                continue;
            }

            var seo = Copy(current);
            if (titleChanged) seo.Title = title;
            if (metaChanged) seo.MetaDescription = meta;
            if (focusChanged) seo.FocusKeyword = focus;
            seo.IsReviewed = true;
            seo.ReviewedAt = now;
            seo.ReviewedByUserId = userId;
            holder.Set(seo);
            saved.Add(change.Id);
        }

        if (saved.Count > 0)
        {
            try
            {
                await context.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException ex)
            {
                context.ChangeTracker.Clear();
                logger.LogError(ex, "SEO bulk save failed");
                return OperationResult<SeoBulkSaveResult>.Error("ذخیره ناموفق بود؛ چیزی تغییر نکرد.");
            }
            context.ChangeTracker.Clear();
            await engine.RefreshScoresAsync(kind, saved, cancellationToken);
        }

        var result = new SeoBulkSaveResult(saved.Count, stale, errors);
        var message = saved.Count > 0
            ? $"{PersianText.Digits(saved.Count)} صفحه ذخیره و نمره‌اش دوباره حساب شد." + (errors.Count > 0 ? $" {PersianText.Digits(errors.Count)} ردیف ذخیره نشد." : "")
            : "هیچ ردیفی ذخیره نشد.";
        return saved.Count > 0 || errors.Count == 0
            ? OperationResult<SeoBulkSaveResult>.Success(result, message)
            : OperationResult<SeoBulkSaveResult>.Error(message + " " + string.Join(" ", errors.Take(3)));
    }

    /// <summary>کپی کامل SeoMetaData (نوع owned با جایگزینی کامل مطمئن‌تر ذخیره می‌شود؛ همان روش موتور سئو).</summary>
    private static SeoMetaData Copy(SeoMetaData? existing)
    {
        var seo = new SeoMetaData();
        if (existing is not null)
            foreach (var property in typeof(SeoMetaData).GetProperties().Where(p => p.CanRead && p.CanWrite))
                property.SetValue(seo, property.GetValue(existing));
        return seo;
    }

    // ── جست‌وجو و جایگزینی ───────────────────────────────────────────────

    /// <summary>
    /// جایگزینی بدون حساسیت به حروف بزرگ/کوچک. در متن جایگزین «{name}» با نام صفحه عوض می‌شود.
    /// برای عنوان، اگر عنوان سئو خالی باشد روی نام صفحه کار می‌شود (نتیجه در عنوان سئو ذخیره می‌شود).
    /// <example>find=«خرید» replacement=«خرید آنلاین» : «خرید شامپو» → «خرید آنلاین شامپو».</example>
    /// </summary>
    public static string? ReplaceValue(string? value, string name, string find, string replacement)
    {
        if (string.IsNullOrEmpty(value) || string.IsNullOrEmpty(find) || value.IndexOf(find, StringComparison.OrdinalIgnoreCase) < 0)
            return null;
        var result = value.Replace(find, replacement.Replace("{name}", name, StringComparison.OrdinalIgnoreCase), StringComparison.OrdinalIgnoreCase);
        result = Clean(result) ?? "";
        return result == value ? null : result;
    }

    public async Task<IReadOnlyList<SeoReplacePreview>> PreviewReplaceAsync(SeoEntityKind kind, IReadOnlyCollection<Guid> ids, SeoBulkField field,
        string find, string replacement, CancellationToken cancellationToken = default)
    {
        if (ids.Count == 0 || string.IsNullOrEmpty(find) || ids.Count > MaxBatch)
            return [];
        var items = await ItemsAsync(kind, ids, cancellationToken);
        var max = field == SeoBulkField.Title ? TitleMax : MetaMax;
        var list = new List<SeoReplacePreview>();
        foreach (var item in items.OrderBy(i => i.Name, StringComparer.Ordinal))
        {
            var before = field == SeoBulkField.Title ? (string.IsNullOrWhiteSpace(item.SeoTitle) ? item.Name : item.SeoTitle) : item.Meta;
            if (ReplaceValue(before, item.Name, find, replacement ?? "") is { } after)
                list.Add(new SeoReplacePreview(item.Id, item.Name, before!, after, after.Length > max || after.Length == 0));
        }
        return list;
    }

    public async Task<OperationResult<int>> ApplyReplaceAsync(SeoEntityKind kind, IReadOnlyCollection<Guid> ids, SeoBulkField field,
        string find, string replacement, Guid? userId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(find) || find.Length > 100 || (replacement ?? "").Length > 200)
            return OperationResult<int>.Error("عبارت جست‌وجو (حداکثر ۱۰۰ نویسه) لازم است و متن جایگزین حداکثر ۲۰۰ نویسه.");
        if (ids.Count == 0)
            return OperationResult<int>.Error("هیچ ردیفی انتخاب نشده است.");
        if (ids.Count > MaxBatch)
            return OperationResult<int>.Error($"حداکثر {PersianText.Digits(MaxBatch)} ردیف در هر بار.");

        var holders = await TrackedAsync(kind, ids, cancellationToken);
        var max = field == SeoBulkField.Title ? TitleMax : MetaMax;
        var now = DateTimeOffset.UtcNow;
        var changed = new List<Guid>();
        var skipped = 0;
        foreach (var (id, holder) in holders)
        {
            var current = holder.Get();
            var before = field == SeoBulkField.Title ? (string.IsNullOrWhiteSpace(current?.Title) ? holder.Name : current!.Title) : current?.MetaDescription;
            if (ReplaceValue(before, holder.Name, find, replacement ?? "") is not { } after)
                continue;
            if (after.Length == 0 || after.Length > max)
            {
                skipped++;
                continue;
            }
            var seo = Copy(current);
            if (field == SeoBulkField.Title) seo.Title = after;
            else seo.MetaDescription = after;
            seo.IsReviewed = true;
            seo.ReviewedAt = now;
            seo.ReviewedByUserId = userId;
            holder.Set(seo);
            changed.Add(id);
        }
        if (changed.Count == 0)
            return OperationResult<int>.Error(skipped > 0 ? "نتیجهٔ جایگزینی برای همهٔ ردیف‌ها خالی یا بیش از حد بلند می‌شد؛ چیزی تغییر نکرد." : "عبارت در ردیف‌های انتخاب‌شده پیدا نشد.");

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            context.ChangeTracker.Clear();
            logger.LogError(ex, "SEO bulk replace failed");
            return OperationResult<int>.Error("ذخیره ناموفق بود؛ چیزی تغییر نکرد.");
        }
        context.ChangeTracker.Clear();
        await engine.RefreshScoresAsync(kind, changed, cancellationToken);
        return OperationResult<int>.Success(changed.Count, $"{PersianText.Digits(changed.Count)} صفحه تغییر کرد" +
            (skipped > 0 ? $" ({PersianText.Digits(skipped)} ردیف چون بیش از حد بلند یا خالی می‌شد کنار گذاشته شد)." : "."));
    }
}
