using Fratak_Shop.Core.DTOs.Auth;

namespace Fratak_Shop.Core.Services.Auth;

/// <summary>
/// Refresh tokens with rotation and reuse detection. Only SHA-256 hashes are stored;
/// every refresh replaces the token, and presenting an already replaced token
/// revokes the whole session (token family), which defeats stolen-token replay.
/// </summary>
public interface IRefreshTokenService
{
    Task<IssuedRefreshToken> IssueAsync(Guid userId, bool persistent, TimeSpan lifetime,
        string? ipAddress, string? userAgent, Guid? familyId = null);

    Task<RefreshRotationResult> RotateAsync(string token, TimeSpan lifetime, string? ipAddress, string? userAgent);

    /// <summary>Revokes the session that owns the token (logout).</summary>
    Task RevokeAsync(string token, string reason);

    Task<bool> RevokeSessionAsync(Guid userId, Guid familyId, string reason);

    /// <summary>Revokes every session of the user, optionally keeping one (the current device).</summary>
    Task<int> RevokeAllAsync(Guid userId, string reason, Guid? exceptFamilyId = null);

    Task<List<UserSessionDto>> GetActiveSessionsAsync(Guid userId, Guid? currentFamilyId);

    /// <summary>Physically removes expired / long revoked tokens.</summary>
    Task<int> CleanupAsync(DateTimeOffset now, CancellationToken cancellationToken = default);
}
