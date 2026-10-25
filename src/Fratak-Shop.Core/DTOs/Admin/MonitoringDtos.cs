using Fratak_Shop.Data.Entities.Admin;

namespace Fratak_Shop.Core.DTOs.Admin;

/// <summary>An error captured by the logger, before it is stored.</summary>
public sealed class ErrorLogEntry
{
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
    public string Level { get; init; } = "Error";
    public string? Category { get; init; }
    public string Message { get; init; } = "";
    public string? MessageTemplate { get; init; }
    public string? ExceptionType { get; init; }
    public string? StackTrace { get; init; }
    public string? Path { get; init; }
    public string? Method { get; init; }
    public string? QueryString { get; init; }
    public Guid? UserId { get; init; }
    public string? IpAddress { get; init; }
    public string? UserAgent { get; init; }
    public string? TraceId { get; init; }
    public string? MachineName { get; init; }
}

public sealed class ErrorGroupDto
{
    public string Fingerprint { get; set; } = null!;
    public string Level { get; set; } = null!;
    public string Message { get; set; } = null!;
    public string? ExceptionType { get; set; }
    public string? Category { get; set; }
    public string? LastPath { get; set; }
    public int Count { get; set; }
    public int UnresolvedCount { get; set; }
    public DateTimeOffset FirstAt { get; set; }
    public DateTimeOffset LastAt { get; set; }
    public Guid LastId { get; set; }
}

public sealed class ErrorFilter
{
    public string? Search { get; set; }
    public string? Level { get; set; }
    public bool IncludeResolved { get; set; }
    public int Days { get; set; } = 7;
    public string? Fingerprint { get; set; }
    public int Page { get; set; } = 1;
    public int Take { get; set; } = 30;
}

public sealed class AuditFilter
{
    public string? Search { get; set; }
    public Guid? UserId { get; set; }
    public string? EntityType { get; set; }
    public string? EntityId { get; set; }
    public string? Action { get; set; }
    public DateTimeOffset? From { get; set; }
    public DateTimeOffset? To { get; set; }
    public int Page { get; set; } = 1;
    public int Take { get; set; } = 50;
}

public sealed class SecurityFilter
{
    public SecurityEventType? Type { get; set; }
    public Guid? UserId { get; set; }
    public string? Search { get; set; }
    public int Days { get; set; } = 30;
    public int Page { get; set; } = 1;
    public int Take { get; set; } = 50;
}

public sealed record PagedList<T>(List<T> Items, int Total, int Page, int Take)
{
    public int PageCount => Take <= 0 ? 1 : Math.Max(1, (int)Math.Ceiling(Total / (double)Take));
}

public sealed class ErrorStatsDto
{
    public int LastHour { get; set; }
    public int Last24Hours { get; set; }
    public int Last7Days { get; set; }
    public int Unresolved { get; set; }
    public int DistinctLast24Hours { get; set; }
    public List<(DateTime Hour, int Count)> Hourly { get; set; } = [];
}

public sealed class DatabaseInfoDto
{
    public string? DatabaseName { get; set; }
    public string? ServerVersion { get; set; }
    public decimal DataSizeMb { get; set; }
    public decimal LogSizeMb { get; set; }
    public int AppliedMigrations { get; set; }
    public string? LastMigration { get; set; }
    public List<string> PendingMigrations { get; set; } = [];
    public double PingMilliseconds { get; set; }
    public List<(string Table, long Rows)> LargestTables { get; set; } = [];
    public string? Error { get; set; }
}
