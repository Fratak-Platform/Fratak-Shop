using System.Globalization;
using System.Text.Json;
using Fratak_Shop.Data.Entities.Admin;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Fratak_Shop.Data.Auditing;

/// <summary>
/// Writes an <see cref="AuditLog"/> row for every entity created, changed or deleted while an
/// <see cref="AuditContext"/> actor is set (admin panel requests), with the old and new values.
/// Secrets (password hashes, tokens, keys) are never written. One shared instance (no per-request state).
/// </summary>
public sealed class AuditSaveChangesInterceptor : SaveChangesInterceptor
{
    public static readonly AuditSaveChangesInterceptor Instance = new();

    private const int MaxEntriesPerSave = 100;
    // مقدار کامل متن‌ها (تا این طول) نگه داشته می‌شود تا «بازگردانی تغییر» برای توضیحات و متن مقاله هم ممکن باشد.
    // مقدار طولانی‌تر بریده می‌شود و با «…» تمام می‌شود؛ این فیلدها قابل بازگردانی خودکار نیستند (AuditHistoryService).
    public const int MaxValueLength = 60_000;

    private static readonly HashSet<string> IgnoredTypes = new(StringComparer.Ordinal)
    {
        nameof(AuditLog), nameof(ErrorLog), nameof(SecurityLog),
        "ProductPriceHistory", "SearchQueryLog", "SeoNotFoundLog", "UserRefreshToken", "UserKnownDevice",
        "NotificationRead", "WebPushSubscription", "CartItem", "Cart", "StaffNotification", "AdminUserPreference", "EditDraft",
        // بررسی تأمین‌کننده: گزارش خودش تاریخچه دارد؛ فقط تغییر واقعی روی Product (موجودی/قیمت) در تاریخچه ثبت می‌شود.
        "SupplierProductState", "SupplierSyncRun", "SupplierChange",
        // دفتر موجودی و دسته‌های تغییر قیمت خودشان سابقه‌اند (تغییر خود محصول جداگانه ثبت می‌شود).
        "StockMovement", "PriceChangeBatch", "PriceChangeItem", "ShipmentTracking", "ShipmentEvent", "VisitorDay", "AnalyticsCounter", "ScheduledReport",
        // موتور سئو: نمره، کش و تاریخچهٔ اجرا داده‌های داخلی‌اند؛ پیشنهاد هم خودش سابقه دارد (تغییر واقعی روی خود صفحه ثبت می‌شود).
        "SeoScore", "SeoSuggestion", "SeoResearchCache", "SeoEngineRun",
        // ابزارهای سئو: گزارش لینک و آمار گوگل داده‌های محاسبه‌شده‌اند؛ تنظیمات کلید رمزنگاری‌شده دارد.
        "SeoLinkIssue", "SearchConsoleQueryStat", "SearchConsoleDailyStat", "SeoToolsConfig",
    };

    private static readonly HashSet<string> IgnoredProperties = new(StringComparer.Ordinal)
    {
        "Id", "CreatedAt", "UpdatedAt", "DeletedAt", "IsDeleted",
    };

    private static readonly string[] SecretFragments =
        ["Password", "SecurityStamp", "TokenHash", "CodeHash", "ProtectedCode", "PrivateKey", "P256dh", "EndpointHash", "DeviceHash", "MerchantId"];

    private static readonly string[] LabelProperties = ["OrderNumber", "Name", "Title", "Code", "Question", "FromPath", "TicketNumber", "RequestNumber", "PhoneNumber", "Key", "Terms", "Permission"];

    private AuditSaveChangesInterceptor()
    {
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Capture(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Capture(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private static void Capture(DbContext? context)
    {
        var actor = AuditContext.Current;
        if (context is null || actor is null)
            return;

        var entries = context.ChangeTracker.Entries()
            .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .Where(e => !IgnoredTypes.Contains(OwnerName(e)))
            .ToList();
        if (entries.Count == 0)
            return;

        var now = DateTimeOffset.UtcNow;
        var logs = new List<AuditLog>();

        if (entries.Count > MaxEntriesPerSave)
        {
            foreach (var group in entries.GroupBy(OwnerName))
                logs.Add(NewLog(actor, now, "bulk", group.Key, null, $"تغییر گروهی {group.Count()} ردیف", null));
        }
        else
        {
            foreach (var entry in entries)
            {
                var log = Describe(entry, actor, now);
                if (log is not null)
                    logs.Add(log);
            }
        }

        if (logs.Count > 0)
            context.Set<AuditLog>().AddRange(logs);
    }

    private static AuditLog? Describe(EntityEntry entry, AuditActor actor, DateTimeOffset now)
    {
        var deleted = entry.State == EntityState.Deleted
                      || (entry.State == EntityState.Modified
                          && entry.Metadata.FindProperty("IsDeleted") is not null
                          && entry.Property("IsDeleted").IsModified
                          && entry.Property("IsDeleted").CurrentValue is true
                          && entry.Property("IsDeleted").OriginalValue is false);

        // بازیابی از سطل بازیافت: IsDeleted از true به false.
        var restored = entry.State == EntityState.Modified
                       && entry.Metadata.FindProperty("IsDeleted") is not null
                       && entry.Property("IsDeleted").IsModified
                       && entry.Property("IsDeleted").CurrentValue is false
                       && entry.Property("IsDeleted").OriginalValue is true;

        var action = entry.State == EntityState.Added ? "create" : deleted ? "delete" : restored ? "restore" : "update";
        var changes = new Dictionary<string, object?[]>(StringComparer.Ordinal);
        var prefix = entry.Metadata.IsOwned() ? entry.Metadata.ClrType.Name.Replace("MetaData", "") + "." : "";

        foreach (var property in entry.Properties)
        {
            var name = property.Metadata.Name;
            if (IgnoredProperties.Contains(name) || property.Metadata.IsShadowProperty() || property.Metadata.IsPrimaryKey())
                continue;

            var secret = name == "Auth" || Array.Exists(SecretFragments, f => name.Contains(f, StringComparison.Ordinal));
            if (entry.State == EntityState.Added)
            {
                if (property.CurrentValue is null || deleted)
                    continue;
                changes[prefix + name] = [null, secret ? "***" : Format(property.CurrentValue)];
            }
            else if (!deleted && property.IsModified && !Equals(property.OriginalValue, property.CurrentValue))
            {
                changes[prefix + name] = secret ? ["***", "***"] : [Format(property.OriginalValue), Format(property.CurrentValue)];
            }
        }

        if (action == "update" && changes.Count == 0)
            return null;

        var label = LabelProperties
            .Select(p => entry.Metadata.FindProperty(p) is null ? null : entry.Property(p).CurrentValue as string)
            .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

        return NewLog(actor, now, action, OwnerName(entry), KeyOf(entry), label, changes.Count == 0 ? null : JsonSerializer.Serialize(changes));
    }

    private static AuditLog NewLog(AuditActor actor, DateTimeOffset now, string action, string type, string? id, string? summary, string? changes) => new()
    {
        Id = Guid.NewGuid(),
        CreatedAt = now,
        UpdatedAt = now,
        UserId = actor.UserId,
        UserName = Truncate(actor.UserName, 150),
        IpAddress = Truncate(actor.IpAddress, 64),
        Path = Truncate(actor.Path, 300),
        Action = action,
        EntityType = Truncate(type, 100)!,
        EntityId = id,
        Summary = Truncate(summary, 500),
        Changes = changes,
    };

    private static string OwnerName(EntityEntry entry) => entry.Metadata.IsOwned()
        ? entry.Metadata.FindOwnership()?.PrincipalEntityType.ClrType.Name ?? entry.Metadata.ClrType.Name
        : entry.Metadata.ClrType.Name;

    private static string? KeyOf(EntityEntry entry)
    {
        var key = entry.Metadata.FindPrimaryKey();
        if (key is null)
            return null;
        var values = key.Properties.Select(p => entry.Property(p.Name).CurrentValue?.ToString()).ToArray();
        return Truncate(string.Join(",", values), 64);
    }

    /// <summary>
    /// متن یک مقدار همان‌طور که در ستون Changes ذخیره می‌شود. AuditHistoryService برای مقایسهٔ «مقدار فعلی»
    /// با «مقدار بعد از تغییر» از همین تابع استفاده می‌کند تا قالب‌ها یکی باشند.
    /// </summary>
    public static string? FormatValue(object? value) => Format(value);

    /// <summary>
    /// فیلدهایی که هرگز ثبت نمی‌شوند (رمز، توکن، کلید و فیلدهای سیستمی Id/CreatedAt/…).
    /// بازگردانی تغییر هم روی این فیلدها انجام نمی‌شود.
    /// </summary>
    public static bool IsProtectedProperty(string name) =>
        IgnoredProperties.Contains(name) || name == "Auth" || Array.Exists(SecretFragments, f => name.Contains(f, StringComparison.Ordinal));

    private static string? Format(object? value) => value switch
    {
        null => null,
        // همیشه به وقت جهانی (UTC) تا بازگردانی (AuditHistoryService.TryParse) همان لحظه را بسازد، هر offsetی که مقدار داشته باشد.
        DateTimeOffset date => date.ToUniversalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
        decimal number => number.ToString("0.##", CultureInfo.InvariantCulture),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => Truncate(value.ToString(), MaxValueLength),
    };

    private static string? Truncate(string? value, int max) =>
        value is null ? null : value.Length <= max ? value : value[..max] + "…";
}
