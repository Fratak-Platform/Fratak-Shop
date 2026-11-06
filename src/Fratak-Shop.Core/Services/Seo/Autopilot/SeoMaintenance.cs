using System.Data;
using System.Data.Common;
using System.Text.RegularExpressions;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Utilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Logging;

namespace Fratak_Shop.Core.Services.Seo.Autopilot;

/// <summary>
/// کارهای یک‌بارهٔ «پاک‌سازی سئو» روی دادهٔ موجود:
/// <list type="number">
/// <item><b>نامک انگلیسی</b>: همهٔ نامک‌های فارسی/ناتمیز موجود (محصول، دسته، برند، مقاله، دستهٔ وبلاگ، صفحه، دستهٔ سؤالات و
/// فیلترها) انگلیسی می‌شوند و برای هر کدام ریدایرکت ۳۰۱ از آدرس قدیم ساخته می‌شود (همان قانون ذخیرهٔ DbContext).</item>
/// <item><b>ارجاع تصویرها به WebP</b>: بعد از اینکه لایهٔ وب فایل‌های JPG/PNG را به WebP تبدیل کرد، همهٔ ستون‌های متنی پایگاه
/// داده (نام تصویر، آدرس تصویر، HTML متن‌ها، تنظیمات) به نام تازه اشاره می‌کنند.</item>
/// </list>
/// <para>کجا استفاده می‌شود؟ کارگر خلبان خودکار (بار اول بعد از انتشار، سپس هر شب برای داده‌های واردشده از مسیرهای قدیمی)
/// و دکمه‌های صفحهٔ «سئوی خودکار» پنل.</para>
/// </summary>
public interface ISeoMaintenanceService
{
    /// <summary>انگلیسی کردن نامک‌های موجود. خروجی: تعداد صفحه‌هایی که نامکشان عوض شد.</summary>
    Task<int> MigrateSlugsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// جایگزینی ارجاع تصویرها در همهٔ ستون‌های متنی. کلید نقشه: نام فایل آپلودی (۳۲ نویسهٔ هگز + پسوند، مثال «9f3c….jpg») یا
    /// آدرس کامل تصویر قالب (مثال «/assets/image/blog/1.jpg»)؛ مقدار: نام/آدرس تازه. خروجی: تعداد ردیف‌های به‌روزشده.
    /// </summary>
    Task<int> ReplaceImageReferencesAsync(IReadOnlyDictionary<string, string> map, CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="ISeoMaintenanceService"/>
public sealed partial class SeoMaintenanceService(FratakShopDbContext context, ILogger<SeoMaintenanceService> logger) : ISeoMaintenanceService
{
    private const int Batch = 50;

    public async Task<int> MigrateSlugsAsync(CancellationToken cancellationToken = default)
    {
        var changed = 0;
        changed += await MigrateAsync(context.Products, cancellationToken);
        changed += await MigrateAsync(context.Categories, cancellationToken);
        changed += await MigrateAsync(context.Brands, cancellationToken);
        changed += await MigrateAsync(context.BlogCategories, cancellationToken);
        changed += await MigrateAsync(context.BlogPosts, cancellationToken);
        changed += await MigrateAsync(context.ContentPages, cancellationToken);
        changed += await MigrateAsync(context.FaqCategories, cancellationToken);
        changed += await MigrateAsync(context.ProductFilterAttributes, cancellationToken);
        return changed;
    }

    /// <summary>
    /// نامک‌های غیرانگلیسی یک جدول: فقط «Slug» علامت تغییر می‌خورد؛ قانون ذخیرهٔ DbContext (EnforceEnglishSlug) نامک انگلیسی
    /// یکتا می‌سازد و ریدایرکت ۳۰۱ آدرس قدیم را ثبت می‌کند. هر دسته ۵۰ تایی جدا ذخیره می‌شود تا خطای یکی بقیه را خراب نکند.
    /// </summary>
    private async Task<int> MigrateAsync<T>(DbSet<T> set, CancellationToken cancellationToken) where T : BaseEntity
    {
        var rows = await set.AsNoTracking().Select(e => new { e.Id, Slug = EF.Property<string?>(e, "Slug") }).ToListAsync(cancellationToken);
        var ids = rows.Where(r => !EnglishSlug.IsEnglish(r.Slug)).Select(r => r.Id).ToList();
        var changed = 0;
        using var _ = SeoChangeFeed.Suppress();
        foreach (var chunk in ids.Chunk(Batch))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var entities = await set.AsTracking().Where(e => chunk.Contains(e.Id)).ToListAsync(cancellationToken);
                foreach (var entity in entities)
                    context.Entry(entity).Property("Slug").IsModified = true;
                await context.SaveChangesAsync(cancellationToken);
                changed += entities.Count;
            }
            catch (DbUpdateException ex)
            {
                logger.LogWarning(ex, "English slug migration failed for a batch of {Type}", typeof(T).Name);
            }
            finally
            {
                context.ChangeTracker.Clear();
            }
        }

        return changed;
    }

    /// <summary>نام‌هایی که جایگزین می‌شوند: آدرس تصویر قالب یا نام تصادفی ۳۲ هگز آپلودها (هرگز نام کوتاه عمومی مثل «1.png»).</summary>
    [GeneratedRegex(@"(?:/assets/image/[A-Za-z0-9_\-/]+|[A-Fa-f0-9]{32})\.(?:jpe?g|png|gif)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, 1000)]
    private static partial Regex ImageToken();

    /// <summary>
    /// جایگزینی در یک متن (ایستا برای تست). مثال: («&lt;img src="/uploads/media/9f…a.jpg"&gt;»، {«9f…a.jpg»: «9f…a.webp»}) ← «…9f…a.webp…».
    /// </summary>
    public static string Replace(string value, IReadOnlyDictionary<string, string> map)
    {
        if (string.IsNullOrEmpty(value) || map.Count == 0)
            return value;
        try
        {
            return ImageToken().Replace(value, m => map.TryGetValue(m.Value, out var replacement) ? replacement : m.Value);
        }
        catch (RegexMatchTimeoutException)
        {
            return value;
        }
    }

    public async Task<int> ReplaceImageReferencesAsync(IReadOnlyDictionary<string, string> map, CancellationToken cancellationToken = default)
    {
        if (map.Count == 0)
            return 0;
        var lookup = new Dictionary<string, string>(map, StringComparer.OrdinalIgnoreCase);
        var updated = 0;
        var connection = context.Database.GetDbConnection();
        var opened = connection.State != ConnectionState.Open;
        if (opened)
            await connection.OpenAsync(cancellationToken);
        try
        {
            foreach (var (table, key, column) in TextColumns())
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    updated += await ReplaceColumnAsync(connection, table, key, column, lookup, cancellationToken);
                }
                catch (DbException ex)
                {
                    logger.LogWarning(ex, "Image reference update failed for {Table}.{Column}", table, column);
                }
            }
        }
        finally
        {
            if (opened)
                await connection.CloseAsync();
        }

        // کتابخانهٔ رسانه: نوع فایل هم باید WebP شود.
        await context.MediaFiles.IgnoreQueryFilters().Where(m => m.FileName.EndsWith(".webp") && m.ContentType != "image/webp")
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.ContentType, "image/webp"), cancellationToken);
        return updated;
    }

    /// <summary>همهٔ ستون‌های متنی جدول‌های مدل (با نام جدول، ستون کلید و ستون متن) – شامل ستون‌های SEO جاسازی‌شده.</summary>
    private IEnumerable<(string Table, string Key, string Column)> TextColumns()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entityType in context.Model.GetEntityTypes())
        {
            var table = entityType.GetTableName();
            if (table is null || entityType.GetViewName() is not null)
                continue;
            var store = StoreObjectIdentifier.Table(table, entityType.GetSchema());
            var keyProperty = entityType.FindPrimaryKey()?.Properties;
            if (keyProperty is not { Count: 1 })
                continue;
            var key = keyProperty[0].GetColumnName(store);
            if (key is null)
                continue;
            foreach (var property in entityType.GetProperties())
            {
                if (property.ClrType != typeof(string) || property.IsKey())
                    continue;
                var column = property.GetColumnName(store);
                if (column is null || !seen.Add(table + "|" + column))
                    continue;
                yield return (table, key, column);
            }
        }
    }

    private static async Task<int> ReplaceColumnAsync(DbConnection connection, string table, string key, string column,
        IReadOnlyDictionary<string, string> map, CancellationToken cancellationToken)
    {
        var rows = new List<(object Id, string Value)>();
        await using (var select = connection.CreateCommand())
        {
            var c = Quote(column);
            select.CommandText = $"SELECT {Quote(key)}, {c} FROM {Quote(table)} WHERE {c} LIKE N'%.jp%g%' OR {c} LIKE N'%.png%' OR {c} LIKE N'%.gif%'";
            select.CommandTimeout = 120;
            await using var reader = await select.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                if (!reader.IsDBNull(1))
                    rows.Add((reader.GetValue(0), reader.GetString(1)));
            }
        }

        var updated = 0;
        foreach (var (id, value) in rows)
        {
            var replaced = Replace(value, map);
            if (ReferenceEquals(replaced, value) || replaced == value)
                continue;
            await using var update = connection.CreateCommand();
            update.CommandText = $"UPDATE {Quote(table)} SET {Quote(column)} = @value WHERE {Quote(key)} = @id";
            var valueParameter = update.CreateParameter();
            valueParameter.ParameterName = "@value";
            valueParameter.Value = replaced;
            update.Parameters.Add(valueParameter);
            var idParameter = update.CreateParameter();
            idParameter.ParameterName = "@id";
            idParameter.Value = id;
            update.Parameters.Add(idParameter);
            updated += await update.ExecuteNonQueryAsync(cancellationToken);
        }

        return updated;
    }

    /// <summary>نام جدول/ستون از مدل EF است (نه ورودی کاربر)؛ باز هم کروشه‌ها escape می‌شوند.</summary>
    private static string Quote(string name) => "[" + name.Replace("]", "]]", StringComparison.Ordinal) + "]";
}
