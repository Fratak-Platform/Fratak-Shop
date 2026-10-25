using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Fratak_Shop.Core.DTOs.Admin;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Auditing;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Admin;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Core.Services.Admin;

/// <summary>Error log, audit log and security log: writing, browsing and clean-up.</summary>
public interface IMonitoringService
{
    /// <summary>Explicit audit entry for an important action (status change, refund, ...) of the current admin request.</summary>
    Task LogActionAsync(string action, string entityType, Guid? entityId, string summary);

    Task LogSecurityAsync(SecurityEventType type, Guid? userId, string? identifier, string? ipAddress, string? userAgent, string? details = null);

    Task<int> SaveErrorsAsync(IReadOnlyList<ErrorLogEntry> entries, CancellationToken cancellationToken = default);

    Task<PagedList<ErrorGroupDto>> GetErrorGroupsAsync(ErrorFilter filter);

    Task<PagedList<ErrorLog>> GetErrorsAsync(ErrorFilter filter);

    Task<ErrorLog?> GetErrorAsync(Guid id);

    Task<int> ResolveAsync(string fingerprint, Guid userId);

    Task<int> ResolveAllAsync(Guid userId);

    Task<ErrorStatsDto> GetErrorStatsAsync();

    Task<PagedList<AuditLog>> GetAuditAsync(AuditFilter filter);

    Task<List<string>> GetAuditEntityTypesAsync();

    Task<PagedList<SecurityLog>> GetSecurityAsync(SecurityFilter filter);

    Task<Dictionary<SecurityEventType, int>> GetSecurityStatsAsync(int days);

    Task<DatabaseInfoDto> GetDatabaseInfoAsync(CancellationToken cancellationToken = default);

    /// <summary>Removes log rows older than the retention settings.</summary>
    Task<int> PurgeAsync(int errorDays, int auditDays, CancellationToken cancellationToken = default);
}

public class MonitoringService(FratakShopDbContext context) : IMonitoringService
{
    public async Task LogActionAsync(string action, string entityType, Guid? entityId, string summary)
    {
        var actor = AuditContext.Current;
        var now = DateTimeOffset.UtcNow;
        context.AuditLogs.Add(new AuditLog
        {
            CreatedAt = now,
            UpdatedAt = now,
            Action = Cut(action, 60)!,
            EntityType = Cut(entityType, 100)!,
            EntityId = entityId?.ToString(),
            Summary = Cut(summary, 500),
            UserId = actor?.UserId,
            UserName = Cut(actor?.UserName, 150),
            IpAddress = Cut(actor?.IpAddress, 64),
            Path = Cut(actor?.Path, 300),
        });
        await context.SaveChangesAsync();
    }

    public async Task LogSecurityAsync(SecurityEventType type, Guid? userId, string? identifier, string? ipAddress, string? userAgent, string? details = null)
    {
        context.SecurityLogs.Add(new SecurityLog
        {
            Type = type,
            UserId = userId,
            Identifier = Cut(Mask(identifier), 50),
            IpAddress = Cut(ipAddress, 64),
            UserAgent = Cut(userAgent, 500),
            Details = Cut(details, 500),
        });
        await context.SaveChangesAsync();
    }

    public async Task<int> SaveErrorsAsync(IReadOnlyList<ErrorLogEntry> entries, CancellationToken cancellationToken = default)
    {
        foreach (var entry in entries)
        {
            context.ErrorLogs.Add(new ErrorLog
            {
                CreatedAt = entry.OccurredAt,
                UpdatedAt = entry.OccurredAt,
                Level = Cut(entry.Level, 20)!,
                Category = Cut(entry.Category, 300),
                Message = Cut(string.IsNullOrWhiteSpace(entry.Message) ? "(بدون پیام)" : entry.Message, 4000)!,
                ExceptionType = Cut(entry.ExceptionType, 300),
                StackTrace = Cut(entry.StackTrace, 20_000),
                Path = Cut(entry.Path, 500),
                Method = Cut(entry.Method, 10),
                QueryString = Cut(entry.QueryString, 1000),
                UserId = entry.UserId,
                IpAddress = Cut(entry.IpAddress, 64),
                UserAgent = Cut(entry.UserAgent, 500),
                TraceId = Cut(entry.TraceId, 100),
                MachineName = Cut(entry.MachineName, 100),
                Fingerprint = Fingerprint(entry),
            });
        }

        return await context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Same exception type + logger + message template (not the values) = same problem.</summary>
    public static string Fingerprint(ErrorLogEntry entry)
    {
        var template = entry.MessageTemplate ?? entry.Message;
        var text = $"{entry.ExceptionType}|{entry.Category}|{template}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..32];
    }

    public async Task<PagedList<ErrorGroupDto>> GetErrorGroupsAsync(ErrorFilter filter)
    {
        var query = FilterErrors(filter);
        var groups = query.GroupBy(e => e.Fingerprint).Select(g => new
        {
            Fingerprint = g.Key,
            Count = g.Count(),
            Unresolved = g.Count(e => !e.IsResolved),
            FirstAt = g.Min(e => e.CreatedAt),
            LastAt = g.Max(e => e.CreatedAt),
        });

        var total = await groups.CountAsync();
        var page = Math.Max(1, filter.Page);
        var take = Math.Clamp(filter.Take, 10, 100);
        var pageItems = await groups.OrderByDescending(g => g.LastAt).Skip((page - 1) * take).Take(take).ToListAsync();

        var fingerprints = pageItems.Select(g => g.Fingerprint).ToList();
        var latest = new List<LatestError>();
        foreach (var fingerprint in fingerprints)
        {
            var last = await context.ErrorLogs
                .Where(e => e.Fingerprint == fingerprint)
                .OrderByDescending(e => e.CreatedAt)
                .Select(e => new LatestError(e.Id, e.Fingerprint, e.Level, e.Message, e.ExceptionType, e.Category, e.Path))
                .FirstOrDefaultAsync();
            if (last is not null)
                latest.Add(last);
        }

        var items = pageItems.Select(g =>
        {
            var last = latest.FirstOrDefault(l => l.Fingerprint == g.Fingerprint);
            return new ErrorGroupDto
            {
                Fingerprint = g.Fingerprint,
                Count = g.Count,
                UnresolvedCount = g.Unresolved,
                FirstAt = g.FirstAt,
                LastAt = g.LastAt,
                LastId = last?.Id ?? Guid.Empty,
                Level = last?.Level ?? "Error",
                Message = last?.Message ?? "",
                ExceptionType = last?.ExceptionType,
                Category = last?.Category,
                LastPath = last?.Path,
            };
        }).ToList();

        return new PagedList<ErrorGroupDto>(items, total, page, take);
    }

    public async Task<PagedList<ErrorLog>> GetErrorsAsync(ErrorFilter filter)
    {
        var query = FilterErrors(filter);
        var page = Math.Max(1, filter.Page);
        var take = Math.Clamp(filter.Take, 10, 100);
        var total = await query.CountAsync();
        var items = await query.OrderByDescending(e => e.CreatedAt).Skip((page - 1) * take).Take(take)
            .Select(e => new ErrorLog
            {
                Id = e.Id, CreatedAt = e.CreatedAt, Level = e.Level, Message = e.Message, ExceptionType = e.ExceptionType,
                Category = e.Category, Path = e.Path, Method = e.Method, UserId = e.UserId, IpAddress = e.IpAddress,
                Fingerprint = e.Fingerprint, IsResolved = e.IsResolved, TraceId = e.TraceId,
            })
            .ToListAsync();
        return new PagedList<ErrorLog>(items, total, page, take);
    }

    private IQueryable<ErrorLog> FilterErrors(ErrorFilter filter)
    {
        var since = DateTimeOffset.UtcNow.AddDays(-Math.Clamp(filter.Days, 1, 3650));
        var query = context.ErrorLogs.Where(e => e.CreatedAt >= since);
        if (!filter.IncludeResolved)
            query = query.Where(e => !e.IsResolved);
        if (!string.IsNullOrWhiteSpace(filter.Level))
            query = query.Where(e => e.Level == filter.Level);
        if (!string.IsNullOrWhiteSpace(filter.Fingerprint))
            query = query.Where(e => e.Fingerprint == filter.Fingerprint);
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim();
            query = query.Where(e => e.Message.Contains(term) || (e.Path != null && e.Path.Contains(term))
                                     || (e.ExceptionType != null && e.ExceptionType.Contains(term)) || (e.TraceId != null && e.TraceId == term));
        }
        return query;
    }

    public Task<ErrorLog?> GetErrorAsync(Guid id) => context.ErrorLogs.FirstOrDefaultAsync(e => e.Id == id);

    public async Task<int> ResolveAsync(string fingerprint, Guid userId)
    {
        var now = DateTimeOffset.UtcNow;
        return await context.ErrorLogs.Where(e => e.Fingerprint == fingerprint && !e.IsResolved)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.IsResolved, true).SetProperty(e => e.ResolvedAt, now)
                .SetProperty(e => e.ResolvedByUserId, userId).SetProperty(e => e.UpdatedAt, now));
    }

    public async Task<int> ResolveAllAsync(Guid userId)
    {
        var now = DateTimeOffset.UtcNow;
        return await context.ErrorLogs.Where(e => !e.IsResolved)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.IsResolved, true).SetProperty(e => e.ResolvedAt, now)
                .SetProperty(e => e.ResolvedByUserId, userId).SetProperty(e => e.UpdatedAt, now));
    }

    public async Task<ErrorStatsDto> GetErrorStatsAsync()
    {
        var now = DateTimeOffset.UtcNow;
        var dayAgo = now.AddDays(-1);
        var weekAgo = now.AddDays(-7);
        var hourAgo = now.AddHours(-1);

        var recent = await context.ErrorLogs.Where(e => e.CreatedAt >= dayAgo)
            .Select(e => new { e.CreatedAt, e.Fingerprint }).ToListAsync();

        var stats = new ErrorStatsDto
        {
            LastHour = recent.Count(e => e.CreatedAt >= hourAgo),
            Last24Hours = recent.Count,
            DistinctLast24Hours = recent.Select(e => e.Fingerprint).Distinct().Count(),
            Last7Days = await context.ErrorLogs.CountAsync(e => e.CreatedAt >= weekAgo),
            Unresolved = await context.ErrorLogs.CountAsync(e => !e.IsResolved),
        };

        var start = new DateTime(now.UtcDateTime.Year, now.UtcDateTime.Month, now.UtcDateTime.Day, now.UtcDateTime.Hour, 0, 0, DateTimeKind.Utc).AddHours(-23);
        for (var hour = start; hour <= now.UtcDateTime; hour = hour.AddHours(1))
        {
            var end = hour.AddHours(1);
            stats.Hourly.Add((hour, recent.Count(e => e.CreatedAt.UtcDateTime >= hour && e.CreatedAt.UtcDateTime < end)));
        }

        return stats;
    }

    public async Task<PagedList<AuditLog>> GetAuditAsync(AuditFilter filter)
    {
        var query = context.AuditLogs.AsQueryable();
        if (filter.UserId is { } userId)
            query = query.Where(a => a.UserId == userId);
        if (!string.IsNullOrWhiteSpace(filter.EntityType))
            query = query.Where(a => a.EntityType == filter.EntityType);
        if (!string.IsNullOrWhiteSpace(filter.EntityId))
            query = query.Where(a => a.EntityId == filter.EntityId);
        if (!string.IsNullOrWhiteSpace(filter.Action))
            query = query.Where(a => a.Action == filter.Action);
        if (filter.From is { } from)
            query = query.Where(a => a.CreatedAt >= from);
        if (filter.To is { } to)
            query = query.Where(a => a.CreatedAt < to);
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim();
            query = query.Where(a => (a.Summary != null && a.Summary.Contains(term)) || (a.UserName != null && a.UserName.Contains(term))
                                     || (a.EntityId != null && a.EntityId == term) || (a.Changes != null && a.Changes.Contains(term)));
        }

        var page = Math.Max(1, filter.Page);
        var take = Math.Clamp(filter.Take, 10, 200);
        var total = await query.CountAsync();
        var items = await query.OrderByDescending(a => a.CreatedAt).Skip((page - 1) * take).Take(take).ToListAsync();
        return new PagedList<AuditLog>(items, total, page, take);
    }

    public Task<List<string>> GetAuditEntityTypesAsync() =>
        context.AuditLogs.Select(a => a.EntityType).Distinct().OrderBy(t => t).Take(200).ToListAsync();

    public async Task<PagedList<SecurityLog>> GetSecurityAsync(SecurityFilter filter)
    {
        var since = DateTimeOffset.UtcNow.AddDays(-Math.Clamp(filter.Days, 1, 3650));
        var query = context.SecurityLogs.Where(s => s.CreatedAt >= since);
        if (filter.Type is { } type)
            query = query.Where(s => s.Type == type);
        if (filter.UserId is { } userId)
            query = query.Where(s => s.UserId == userId);
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim();
            query = query.Where(s => (s.IpAddress != null && s.IpAddress.Contains(term)) || (s.Identifier != null && s.Identifier.Contains(term))
                                     || (s.Details != null && s.Details.Contains(term)));
        }

        var page = Math.Max(1, filter.Page);
        var take = Math.Clamp(filter.Take, 10, 200);
        var total = await query.CountAsync();
        var items = await query.OrderByDescending(s => s.CreatedAt).Skip((page - 1) * take).Take(take).ToListAsync();
        return new PagedList<SecurityLog>(items, total, page, take);
    }

    public async Task<Dictionary<SecurityEventType, int>> GetSecurityStatsAsync(int days)
    {
        var since = DateTimeOffset.UtcNow.AddDays(-Math.Clamp(days, 1, 365));
        return await context.SecurityLogs.Where(s => s.CreatedAt >= since)
            .GroupBy(s => s.Type).Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Key, g => g.Count);
    }

    public async Task<DatabaseInfoDto> GetDatabaseInfoAsync(CancellationToken cancellationToken = default)
    {
        var info = new DatabaseInfoDto();
        try
        {
            var watch = Stopwatch.StartNew();
            await context.Database.CanConnectAsync(cancellationToken);
            info.PingMilliseconds = watch.Elapsed.TotalMilliseconds;

            var applied = (await context.Database.GetAppliedMigrationsAsync(cancellationToken)).ToList();
            info.AppliedMigrations = applied.Count;
            info.LastMigration = applied.LastOrDefault();
            info.PendingMigrations = (await context.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();

            if (context.Database.IsSqlServer())
            {
                info.DatabaseName = (await context.Database.SqlQueryRaw<string>("SELECT DB_NAME() AS [Value]").ToListAsync(cancellationToken)).FirstOrDefault();
                info.ServerVersion = (await context.Database.SqlQueryRaw<string>("SELECT CAST(SERVERPROPERTY('ProductVersion') AS nvarchar(128)) AS [Value]").ToListAsync(cancellationToken)).FirstOrDefault();
                info.DataSizeMb = (await context.Database.SqlQueryRaw<decimal>(
                    "SELECT CAST(ISNULL(SUM(CAST(size AS bigint)), 0) * 8 / 1024.0 AS decimal(18,2)) AS [Value] FROM sys.database_files WHERE type = 0").ToListAsync(cancellationToken)).FirstOrDefault();
                info.LogSizeMb = (await context.Database.SqlQueryRaw<decimal>(
                    "SELECT CAST(ISNULL(SUM(CAST(size AS bigint)), 0) * 8 / 1024.0 AS decimal(18,2)) AS [Value] FROM sys.database_files WHERE type = 1").ToListAsync(cancellationToken)).FirstOrDefault();
                var tables = await context.Database.SqlQueryRaw<TableRows>(
                    "SELECT TOP 15 t.name AS [Name], CAST(SUM(p.rows) AS bigint) AS [Rows] FROM sys.tables t JOIN sys.partitions p ON p.object_id = t.object_id AND p.index_id IN (0, 1) GROUP BY t.name ORDER BY SUM(p.rows) DESC")
                    .ToListAsync(cancellationToken);
                info.LargestTables = tables.Select(t => (t.Name, t.Rows)).ToList();
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            info.Error = exception.Message;
        }

        return info;
    }

    private sealed record LatestError(Guid Id, string Fingerprint, string Level, string Message, string? ExceptionType, string? Category, string? Path);

    public sealed class TableRows
    {
        public string Name { get; set; } = "";
        public long Rows { get; set; }
    }

    public async Task<int> PurgeAsync(int errorDays, int auditDays, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var errorsBefore = now.AddDays(-Math.Max(1, errorDays));
        var auditBefore = now.AddDays(-Math.Max(1, auditDays));

        var removed = await context.ErrorLogs.IgnoreQueryFilters().Where(e => e.CreatedAt < errorsBefore).ExecuteDeleteAsync(cancellationToken);
        removed += await context.AuditLogs.IgnoreQueryFilters().Where(a => a.CreatedAt < auditBefore).ExecuteDeleteAsync(cancellationToken);
        removed += await context.SecurityLogs.IgnoreQueryFilters().Where(s => s.CreatedAt < auditBefore).ExecuteDeleteAsync(cancellationToken);
        return removed;
    }

    /// <summary>09121234567 → 0912***4567 (logs must not become a phone-number list).</summary>
    public static string? Mask(string? identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier))
            return null;
        var value = identifier.Trim();
        return value.Length >= 8 ? value[..4] + "***" + value[^4..] : value;
    }

    private static string? Cut(string? value, int max) => value is null ? null : value.Length <= max ? value : value[..max];
}
