namespace Fratak_Shop.Core.DTOs.Auth;

/// <summary>A freshly issued refresh token. <see cref="Token"/> is only known to the client.</summary>
public class IssuedRefreshToken
{
    public string Token { get; set; } = null!;
    public Guid TokenId { get; set; }
    public Guid FamilyId { get; set; }
    public Guid UserId { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public bool IsPersistent { get; set; }
}

public enum RefreshFailure
{
    None = 0,

    /// <summary>Unknown, expired or revoked token.</summary>
    Invalid = 1,

    /// <summary>The token was rotated a moment ago by a parallel request (another tab).</summary>
    Concurrent = 2,

    /// <summary>An already rotated token was presented again: the session was revoked.</summary>
    Reused = 3,
}

public class RefreshRotationResult
{
    public IssuedRefreshToken? Token { get; set; }
    public RefreshFailure Failure { get; set; }
    public bool Succeeded => Token is not null;
}

/// <summary>An active login (one refresh-token family) shown under "دستگاه‌های فعال".</summary>
public class UserSessionDto
{
    public Guid FamilyId { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset LastActiveAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public bool IsCurrent { get; set; }
}
