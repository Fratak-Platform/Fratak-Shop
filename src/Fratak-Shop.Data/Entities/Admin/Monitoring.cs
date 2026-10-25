using System.ComponentModel.DataAnnotations;
using Fratak_Shop.Data.Utilities;

namespace Fratak_Shop.Data.Entities.Admin;

/// <summary>
/// What a staff member changed in the admin panel: written automatically for every saved entity
/// (with the changed values) and explicitly for important actions (status changes, refunds, ...).
/// </summary>
public class AuditLog : BaseEntity
{
    public Guid? UserId { get; set; }

    [MaxLength(150)]
    public string? UserName { get; set; }

    /// <summary>create / update / delete / or a named action such as "order.status".</summary>
    [Required]
    [MaxLength(60)]
    public string Action { get; set; } = null!;

    [Required]
    [MaxLength(100)]
    public string EntityType { get; set; } = null!;

    [MaxLength(64)]
    public string? EntityId { get; set; }

    [MaxLength(500)]
    public string? Summary { get; set; }

    /// <summary>JSON: { "Property": ["old", "new"] }. Secrets are never written.</summary>
    public string? Changes { get; set; }

    [MaxLength(64)]
    public string? IpAddress { get; set; }

    [MaxLength(300)]
    public string? Path { get; set; }
}

/// <summary>An error logged by the application (exception, failed request, failed job) with its context.</summary>
public class ErrorLog : BaseEntity
{
    [Required]
    [MaxLength(20)]
    public string Level { get; set; } = null!;

    /// <summary>Logger category (class that logged the error).</summary>
    [MaxLength(300)]
    public string? Category { get; set; }

    [Required]
    [MaxLength(4000)]
    public string Message { get; set; } = null!;

    [MaxLength(300)]
    public string? ExceptionType { get; set; }

    public string? StackTrace { get; set; }

    [MaxLength(500)]
    public string? Path { get; set; }

    [MaxLength(10)]
    public string? Method { get; set; }

    [MaxLength(1000)]
    public string? QueryString { get; set; }

    public Guid? UserId { get; set; }

    [MaxLength(64)]
    public string? IpAddress { get; set; }

    [MaxLength(500)]
    public string? UserAgent { get; set; }

    [MaxLength(100)]
    public string? TraceId { get; set; }

    [MaxLength(100)]
    public string? MachineName { get; set; }

    /// <summary>Hash of type + category + message template: groups repeats of the same error.</summary>
    [Required]
    [MaxLength(64)]
    public string Fingerprint { get; set; } = null!;

    public bool IsResolved { get; set; }

    public DateTimeOffset? ResolvedAt { get; set; }

    public Guid? ResolvedByUserId { get; set; }
}

/// <summary>Security relevant events: sign-ins, failures, lockouts, blocked accounts, denied admin access.</summary>
public class SecurityLog : BaseEntity
{
    public SecurityEventType Type { get; set; }

    public Guid? UserId { get; set; }

    /// <summary>Masked phone number / identifier that was used.</summary>
    [MaxLength(50)]
    public string? Identifier { get; set; }

    [MaxLength(64)]
    public string? IpAddress { get; set; }

    [MaxLength(500)]
    public string? UserAgent { get; set; }

    [MaxLength(500)]
    public string? Details { get; set; }
}

public enum SecurityEventType
{
    None = 0,
    LoginSucceeded = 1,
    LoginFailed = 2,
    LockedOut = 3,
    OtpFailed = 4,
    BlockedUserLogin = 5,
    AdminAccessDenied = 6,
    UserBlocked = 7,
    UserUnblocked = 8,
    RolesChanged = 9,
    SessionsRevoked = 10,
}
