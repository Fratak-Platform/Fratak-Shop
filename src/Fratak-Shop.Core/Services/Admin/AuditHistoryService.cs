using System.Globalization;
using System.Text.Json;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Auditing;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Utilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Fratak_Shop.Core.Services.Admin;

/// <summary>یک فیلد تغییرکرده در یک ردیف گزارش فعالیت: نام فیلد، مقدار قبل و بعد.</summary>
public sealed record AuditFieldChange(string Field, string? Before, string? After)
{
    /// <summary>مقدار قبلی آن‌قدر طولانی بوده که بریده ذخیره شده (و قابل بازگردانی خودکار نیست).</summary>
    public bool IsTruncated => Before is { Length: > AuditSaveChangesInterceptor.MaxValueLength } && Before.EndsWith('…');
}

/// <summary>یک ردیف تاریخچهٔ یک رکورد، برای پنل «تاریخچهٔ تغییرات» صفحه‌های ویرایش.</summary>
public sealed record AuditHistoryItem(Guid Id, DateTimeOffset At, string Action, Guid? UserId, string? UserName, IReadOnlyList<AuditFieldChange> Changes)
{
    /// <summary>فقط «ویرایش»ها بازگشت‌پذیرند (ایجاد/حذف راه خودشان را دارند: حذف ← سطل بازیافت).</summary>
    public bool CanRevert => Action == "update" && Changes.Count > 0;
}

/// <summary>نتیجهٔ بازگردانی: کدام فیلدها برگشتند و کدام نه (با دلیل).</summary>
public sealed record AuditRevertReport(IReadOnlyList<string> Reverted, IReadOnlyList<string> Skipped);

/// <summary>
/// «تاریخچهٔ تغییرات و بازگردانی» برای هر رکورد.
/// <para>
/// منبع داده جدول <c>AuditLogs</c> است که <see cref="AuditSaveChangesInterceptor"/> برای هر ذخیرهٔ پنل با مقدار قبل و بعد پر می‌کند؛
/// پس هیچ جدول نسخهٔ جداگانه‌ای لازم نیست و همهٔ جدول‌ها (محصول، مقاله، جدول‌های مدیریت داده) خودکار تاریخچه دارند.
/// </para>
/// <para>
/// بازگردانی یعنی برگرداندن مقدار «قبل» همان ردیف تاریخچه. برای اینکه تغییرات بعدی دیگران خراب نشود فقط فیلدهایی برمی‌گردند
/// که مقدار فعلی‌شان هنوز همان مقدار «بعد» آن تغییر است؛ بقیه با دلیل گزارش می‌شوند.
/// خود بازگردانی هم یک «ویرایش» جدید است و در تاریخچه دیده می‌شود (پس بازگردانی هم برگشت‌پذیر است).
/// </para>
/// استفاده: صفحه‌های <c>/admin/products/edit</c>، <c>/admin/blog/edit</c> و <c>/admin/manage/{key}/edit</c> (partial <c>_EditHistory</c>).
/// </summary>
public interface IAuditHistoryService
{
    /// <summary>آخرین تغییرات یک رکورد، جدیدترین اول. <paramref name="entityType"/> نام کلاس موجودیت است، مثلاً <c>"Product"</c>.</summary>
    Task<IReadOnlyList<AuditHistoryItem>> ListAsync(string entityType, Guid entityId, int take = 30, CancellationToken cancellationToken = default);

    /// <summary>
    /// بازگردانی یک تغییر. <paramref name="entityType"/> و <paramref name="entityId"/> باید با ردیف تاریخچه یکی باشند
    /// (تا کسی با دستکاری شناسه، رکورد جدول دیگری را که مجوزش را ندارد برنگرداند).
    /// </summary>
    Task<OperationResult<AuditRevertReport>> RevertAsync(Guid auditId, string entityType, Guid entityId, CancellationToken cancellationToken = default);

    /// <summary>
    /// «نسخهٔ» فعلی رکورد برای تشخیص ویرایش هم‌زمان: زمان (تیک) آخرین تغییر ثبت‌شده از پنل، یا صفر.
    /// <para>
    /// چرا UpdatedAt نه؟ چون UpdatedAt با کارهای خودکار هم عوض می‌شود (مثلاً کم شدن موجودی بعد از هر خرید) و هشدار بیخود می‌دهد؛
    /// ما فقط می‌خواهیم بدانیم کارمند دیگری از پنل همین رکورد را ذخیره کرده یا نه.
    /// </para>
    /// </summary>
    Task<long> StampAsync(string entityType, Guid entityId, CancellationToken cancellationToken = default);

    /// <summary>خواندن JSON ستون Changes به فهرست فیلدها (خطای JSON = فهرست خالی).</summary>
    static IReadOnlyList<AuditFieldChange> ParseChanges(string? json)
    {
        var list = new List<AuditFieldChange>();
        if (string.IsNullOrWhiteSpace(json))
            return list;
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return list;
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (property.Value.ValueKind != JsonValueKind.Array)
                    continue;
                var values = property.Value.EnumerateArray()
                    .Select(v => v.ValueKind == JsonValueKind.Null ? null : v.ValueKind == JsonValueKind.String ? v.GetString() : v.GetRawText())
                    .ToList();
                list.Add(new AuditFieldChange(property.Name, values.ElementAtOrDefault(0), values.ElementAtOrDefault(1)));
            }
        }
        catch (JsonException)
        {
            list.Clear();
        }
        return list;
    }
}

public sealed class AuditHistoryService(FratakShopDbContext context) : IAuditHistoryService
{
    public async Task<IReadOnlyList<AuditHistoryItem>> ListAsync(string entityType, Guid entityId, int take = 30, CancellationToken cancellationToken = default)
    {
        var id = entityId.ToString();
        var rows = await context.AuditLogs
            .Where(l => l.EntityType == entityType && l.EntityId == id)
            .OrderByDescending(l => l.CreatedAt)
            .Take(Math.Clamp(take, 1, 200))
            .Select(l => new { l.Id, l.CreatedAt, l.Action, l.UserId, l.UserName, l.Changes })
            .ToListAsync(cancellationToken);

        return rows.Select(l => new AuditHistoryItem(l.Id, l.CreatedAt, l.Action, l.UserId, l.UserName, IAuditHistoryService.ParseChanges(l.Changes))).ToList();
    }

    public async Task<long> StampAsync(string entityType, Guid entityId, CancellationToken cancellationToken = default)
    {
        var id = entityId.ToString();
        var last = await context.AuditLogs
            .Where(l => l.EntityType == entityType && l.EntityId == id)
            .OrderByDescending(l => l.CreatedAt)
            .Select(l => (DateTimeOffset?)l.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        return last?.UtcTicks ?? 0;
    }

    public async Task<OperationResult<AuditRevertReport>> RevertAsync(Guid auditId, string entityType, Guid entityId, CancellationToken cancellationToken = default)
    {
        var log = await context.AuditLogs.FirstOrDefaultAsync(l => l.Id == auditId, cancellationToken);
        if (log is null || log.EntityType != entityType || log.EntityId != entityId.ToString())
            return OperationResult<AuditRevertReport>.NotFound("این تغییر در تاریخچهٔ این مورد پیدا نشد.");
        if (log.Action != "update")
            return OperationResult<AuditRevertReport>.Error("فقط «ویرایش»ها قابل بازگردانی‌اند. موارد حذف‌شده را از سطل بازیافت برگردانید.");

        var changes = IAuditHistoryService.ParseChanges(log.Changes);
        if (changes.Count == 0)
            return OperationResult<AuditRevertReport>.Error("این تغییر جزئیاتی برای بازگردانی ندارد.");

        // نوع موجودیت از روی نام کلاس (مثلاً Product) – فقط موجودیت‌های اصلی که از BaseEntity ارث می‌برند.
        var type = context.Model.GetEntityTypes()
            .FirstOrDefault(t => !t.IsOwned() && t.ClrType.Name == entityType && typeof(BaseEntity).IsAssignableFrom(t.ClrType));
        if (type is null)
            return OperationResult<AuditRevertReport>.Error("این نوع داده قابل بازگردانی نیست.");

        // Find نسخهٔ ردیابی‌شده را برمی‌گرداند (همراه بخش‌های Owned مثل Seo).
        var entity = await context.FindAsync(type.ClrType, [entityId], cancellationToken);
        if (entity is not BaseEntity baseEntity || baseEntity.IsDeleted)
            return OperationResult<AuditRevertReport>.NotFound("این مورد وجود ندارد یا حذف شده است.");

        // رفتار پیش‌فرض DbContext «بدون ردیابی» است و Find در این حالت ممکن است نمونهٔ ردیابی‌نشده برگرداند؛
        // در آن صورت تغییر مقدارها ذخیره نمی‌شد (بازگردانی «موفق» اما بی‌اثر). با Attach حتماً ردیابی می‌شود.
        if (context.Entry(entity).State == EntityState.Detached)
            context.Attach(entity);

        var entry = context.Entry(entity);
        var reverted = new List<string>();
        var skipped = new List<string>();

        foreach (var change in changes)
        {
            var property = FindProperty(entry, change.Field);
            if (property is null || property.Metadata.IsPrimaryKey() || property.Metadata.IsShadowProperty()
                || AuditSaveChangesInterceptor.IsProtectedProperty(property.Metadata.Name))
            {
                skipped.Add($"{change.Field}: قابل بازگردانی نیست");
                continue;
            }
            if (change.IsTruncated)
            {
                skipped.Add($"{change.Field}: مقدار قبلی خیلی طولانی بوده و کامل ذخیره نشده است");
                continue;
            }

            // اگر بعد از این تغییر دوباره عوض شده، دست نمی‌زنیم تا کار بعدی کسی پاک نشود.
            var current = AuditSaveChangesInterceptor.FormatValue(property.CurrentValue);
            if (!string.Equals(current, change.After, StringComparison.Ordinal))
            {
                skipped.Add($"{change.Field}: بعد از این تغییر دوباره ویرایش شده است");
                continue;
            }

            if (!TryParse(change.Before, property.Metadata.ClrType, out var value))
            {
                skipped.Add($"{change.Field}: مقدار قبلی قابل خواندن نیست");
                continue;
            }
            if (value is null && !property.Metadata.IsNullable)
            {
                skipped.Add($"{change.Field}: نمی‌تواند خالی باشد");
                continue;
            }

            property.CurrentValue = value;
            reverted.Add(change.Field);
        }

        if (reverted.Count == 0)
        {
            context.ChangeTracker.Clear();
            return OperationResult<AuditRevertReport>.Error("هیچ فیلدی برگردانده نشد: " + string.Join("؛ ", skipped));
        }

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            context.ChangeTracker.Clear();
            return OperationResult<AuditRevertReport>.Error("بازگردانی ذخیره نشد؛ احتمالاً مقدار قبلی (مثلاً نامک) اکنون برای مورد دیگری استفاده شده است.");
        }

        var report = new AuditRevertReport(reverted, skipped);
        var message = $"{reverted.Count} فیلد به مقدار قبلی برگشت." + (skipped.Count > 0 ? $" {skipped.Count} فیلد برنگشت." : "");
        return OperationResult<AuditRevertReport>.Success(report, message);
    }

    // «Name» ← ویژگی خود موجودیت؛ «Seo.Title» ← ویژگی بخش Owned با پیشوند (همان قالب AuditSaveChangesInterceptor).
    private static PropertyEntry? FindProperty(EntityEntry entry, string field)
    {
        var dot = field.IndexOf('.');
        if (dot < 0)
            return entry.Properties.FirstOrDefault(p => p.Metadata.Name == field);

        var prefix = field[..dot];
        var name = field[(dot + 1)..];
        foreach (var reference in entry.References)
        {
            if (!reference.Metadata.TargetEntityType.IsOwned() || reference.TargetEntry is null)
                continue;
            if (reference.Metadata.TargetEntityType.ClrType.Name.Replace("MetaData", "") != prefix)
                continue;
            return reference.TargetEntry.Properties.FirstOrDefault(p => p.Metadata.Name == name);
        }
        return null;
    }

    /// <summary>
    /// تبدیل متن ذخیره‌شده به نوع ویژگی. قالب‌ها همان‌اند که <see cref="AuditSaveChangesInterceptor.FormatValue"/> می‌نویسد
    /// (عدد با فرهنگ Invariant، تاریخ «yyyy-MM-dd HH:mm» به وقت جهانی، enum با نام عضو).
    /// </summary>
    public static bool TryParse(string? text, Type type, out object? value)
    {
        value = null;
        var underlying = Nullable.GetUnderlyingType(type) ?? type;
        if (text is null)
            return !underlying.IsValueType || Nullable.GetUnderlyingType(type) is not null || type == typeof(string);

        var inv = CultureInfo.InvariantCulture;
        switch (underlying)
        {
            case not null when underlying == typeof(string):
                value = text;
                return true;
            case not null when underlying == typeof(bool):
                if (!bool.TryParse(text, out var b)) return false;
                value = b;
                return true;
            case not null when underlying == typeof(int):
                if (!int.TryParse(text, NumberStyles.Integer, inv, out var i)) return false;
                value = i;
                return true;
            case not null when underlying == typeof(long):
                if (!long.TryParse(text, NumberStyles.Integer, inv, out var l)) return false;
                value = l;
                return true;
            case not null when underlying == typeof(decimal):
                if (!decimal.TryParse(text, NumberStyles.Number, inv, out var d)) return false;
                value = d;
                return true;
            case not null when underlying == typeof(double):
                if (!double.TryParse(text, NumberStyles.Float, inv, out var dbl)) return false;
                value = dbl;
                return true;
            case not null when underlying == typeof(Guid):
                if (!Guid.TryParse(text, out var g)) return false;
                value = g;
                return true;
            case not null when underlying == typeof(DateTimeOffset):
                if (!DateTimeOffset.TryParseExact(text, "yyyy-MM-dd HH:mm", inv, DateTimeStyles.AssumeUniversal, out var date)) return false;
                value = date;
                return true;
            case not null when underlying == typeof(DateOnly):
                if (!DateOnly.TryParse(text, inv, out var day)) return false;
                value = day;
                return true;
            case not null when underlying == typeof(TimeOnly):
                if (!TimeOnly.TryParse(text, inv, out var time)) return false;
                value = time;
                return true;
            case not null when underlying.IsEnum:
                if (!Enum.TryParse(underlying, text, out var member) || !Enum.IsDefined(underlying, member!)) return false;
                value = member;
                return true;
            default:
                return false;
        }
    }
}
