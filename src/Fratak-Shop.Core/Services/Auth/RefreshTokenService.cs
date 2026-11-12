using System.Security.Cryptography;
using System.Text;
using Fratak_Shop.Core.DTOs.Auth;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Users;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Core.Services.Auth;

public class RefreshTokenService(FratakShopDbContext context) : IRefreshTokenService
{
    private const string RotatedReason = "rotated";

    /// <summary>Parallel requests (several tabs) may present the same token for this long after a rotation.</summary>
    private static readonly TimeSpan ConcurrencyGrace = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan RetentionAfterExpiry = TimeSpan.FromDays(7);

    public async Task<IssuedRefreshToken> IssueAsync(Guid userId, bool persistent, TimeSpan lifetime,
        string? ipAddress, string? userAgent, Guid? familyId = null)
    {
        var token = GenerateToken();
        var entity = new UserRefreshToken
        {
            UserId = userId,
            TokenHash = Hash(token),
            FamilyId = familyId ?? Guid.NewGuid(),
            ExpiresAt = DateTimeOffset.UtcNow.Add(lifetime),
            IsPersistent = persistent,
            CreatedByIp = Limit(ipAddress, 64),
            UserAgent = Limit(userAgent, 512),
        };

        context.UserRefreshTokens.Add(entity);
        await context.SaveChangesAsync();

        return new IssuedRefreshToken
        {
            Token = token,
            TokenId = entity.Id,
            FamilyId = entity.FamilyId,
            UserId = userId,
            ExpiresAt = entity.ExpiresAt,
            IsPersistent = persistent,
        };
    }

    public async Task<RefreshRotationResult> RotateAsync(string token, TimeSpan lifetime, string? ipAddress, string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 200)
            return new RefreshRotationResult { Failure = RefreshFailure.Invalid };

        var hash = Hash(token);
        var now = DateTimeOffset.UtcNow;

        var current = await context.UserRefreshTokens
            .Where(t => t.TokenHash == hash)
            .Select(t => new { t.Id, t.UserId, t.FamilyId, t.IsPersistent, t.ExpiresAt, t.RevokedAt, t.RevokedReason })
            .FirstOrDefaultAsync();

        if (current is null)
            return new RefreshRotationResult { Failure = RefreshFailure.Invalid };

        if (current.RevokedAt is not null)
        {
            if (current.RevokedReason == RotatedReason)
            {
                if (now - current.RevokedAt.Value <= ConcurrencyGrace)
                    return await ConcurrentAsync(current.UserId, current.FamilyId, current.IsPersistent);

                // A replaced token came back: it was copied. Kill the whole session.
                await RevokeFamilyAsync(current.FamilyId, "reuse-detected", now);
                return new RefreshRotationResult { Failure = RefreshFailure.Reused };
            }

            return new RefreshRotationResult { Failure = RefreshFailure.Invalid };
        }

        if (current.ExpiresAt <= now)
            return new RefreshRotationResult { Failure = RefreshFailure.Invalid };

        // The user must still exist (soft-deleted users are filtered out).
        if (!await context.Users.AnyAsync(u => u.Id == current.UserId && !u.IsBlocked))
        {
            await RevokeFamilyAsync(current.FamilyId, "user-removed", now);
            return new RefreshRotationResult { Failure = RefreshFailure.Invalid };
        }

        await using var transaction = await context.Database.BeginTransactionAsync();

        // Atomic claim of the token: only one request can rotate it.
        var claimed = await context.UserRefreshTokens
            .Where(t => t.Id == current.Id && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.RevokedAt, now)
                .SetProperty(t => t.RevokedReason, RotatedReason)
                .SetProperty(t => t.UpdatedAt, now));

        if (claimed == 0)
        {
            await transaction.RollbackAsync();
            return await ConcurrentAsync(current.UserId, current.FamilyId, current.IsPersistent);
        }

        // Persistent sessions slide; browser-session logins keep their original end.
        var newLifetime = current.IsPersistent ? lifetime : current.ExpiresAt - now;
        var issued = await IssueAsync(current.UserId, current.IsPersistent, newLifetime, ipAddress, userAgent, current.FamilyId);

        await context.UserRefreshTokens
            .Where(t => t.Id == current.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.ReplacedByTokenId, issued.TokenId));

        await transaction.CommitAsync();
        return new RefreshRotationResult { Token = issued };
    }

    /// <summary>
    /// مرحلهٔ هفدهم: نتیجهٔ «هم‌زمان» با مشخصات نشست، فقط اگر نشست هنوز زنده است (توکن فعالی در همان خانواده هست و کاربر
    /// مسدود/حذف نشده). مثال: صفحه و سه درخواست fetch هم‌زمان با یک توکن منقضی ← اولی می‌چرخاند، بقیه همین را می‌گیرند و
    /// با توکن دسترسی کوتاه ادامه می‌دهند. اگر بین این دو کاربر خارج شده باشد ← Invalid (هیچ دسترسی‌ای داده نمی‌شود).
    /// </summary>
    private async Task<RefreshRotationResult> ConcurrentAsync(Guid userId, Guid familyId, bool persistent)
    {
        var now = DateTimeOffset.UtcNow;
        var alive = await context.UserRefreshTokens.AnyAsync(t => t.FamilyId == familyId && t.RevokedAt == null && t.ExpiresAt > now)
                    && await context.Users.AnyAsync(u => u.Id == userId && !u.IsBlocked);
        return alive
            ? new RefreshRotationResult { Failure = RefreshFailure.Concurrent, ConcurrentUserId = userId, ConcurrentFamilyId = familyId, ConcurrentPersistent = persistent }
            : new RefreshRotationResult { Failure = RefreshFailure.Invalid };
    }

    public async Task RevokeAsync(string token, string reason)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 200)
            return;

        var hash = Hash(token);
        var familyId = await context.UserRefreshTokens
            .Where(t => t.TokenHash == hash)
            .Select(t => (Guid?)t.FamilyId)
            .FirstOrDefaultAsync();

        if (familyId.HasValue)
            await RevokeFamilyAsync(familyId.Value, reason, DateTimeOffset.UtcNow);
    }

    public async Task<bool> RevokeSessionAsync(Guid userId, Guid familyId, string reason)
    {
        var now = DateTimeOffset.UtcNow;
        var affected = await context.UserRefreshTokens
            .Where(t => t.UserId == userId && t.FamilyId == familyId && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.RevokedAt, now)
                .SetProperty(t => t.RevokedReason, Limit(reason, 100))
                .SetProperty(t => t.UpdatedAt, now));

        return affected > 0;
    }

    public async Task<int> RevokeAllAsync(Guid userId, string reason, Guid? exceptFamilyId = null)
    {
        var now = DateTimeOffset.UtcNow;
        var query = context.UserRefreshTokens.Where(t => t.UserId == userId && t.RevokedAt == null);

        if (exceptFamilyId.HasValue)
            query = query.Where(t => t.FamilyId != exceptFamilyId.Value);

        return await query.ExecuteUpdateAsync(s => s
            .SetProperty(t => t.RevokedAt, now)
            .SetProperty(t => t.RevokedReason, Limit(reason, 100))
            .SetProperty(t => t.UpdatedAt, now));
    }

    public async Task<List<UserSessionDto>> GetActiveSessionsAsync(Guid userId, Guid? currentFamilyId)
    {
        var now = DateTimeOffset.UtcNow;

        var sessions = await context.UserRefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null && t.ExpiresAt > now)
            .OrderByDescending(t => t.CreatedAt)
            .Select(t => new UserSessionDto
            {
                FamilyId = t.FamilyId,
                LastActiveAt = t.CreatedAt,
                ExpiresAt = t.ExpiresAt,
                IpAddress = t.CreatedByIp,
                UserAgent = t.UserAgent,
                StartedAt = context.UserRefreshTokens
                    .Where(f => f.FamilyId == t.FamilyId)
                    .Min(f => f.CreatedAt),
            })
            .Take(50)
            .ToListAsync();

        foreach (var session in sessions)
            session.IsCurrent = session.FamilyId == currentFamilyId;

        return sessions
            .OrderByDescending(s => s.IsCurrent)
            .ThenByDescending(s => s.LastActiveAt)
            .ToList();
    }

    public async Task<int> CleanupAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var threshold = now - RetentionAfterExpiry;
        return await context.UserRefreshTokens
            .IgnoreQueryFilters()
            .Where(t => t.ExpiresAt < threshold || (t.RevokedAt != null && t.RevokedAt < threshold))
            .ExecuteDeleteAsync(cancellationToken);
    }

    private async Task RevokeFamilyAsync(Guid familyId, string reason, DateTimeOffset now)
    {
        await context.UserRefreshTokens
            .Where(t => t.FamilyId == familyId && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.RevokedAt, now)
                .SetProperty(t => t.RevokedReason, Limit(reason, 100))
                .SetProperty(t => t.UpdatedAt, now));
    }

    private static string GenerateToken()
    {
        return Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
    }

    private static string Hash(string token)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string? Limit(string? value, int length) =>
        string.IsNullOrEmpty(value) ? value : value.Length <= length ? value : value[..length];
}
