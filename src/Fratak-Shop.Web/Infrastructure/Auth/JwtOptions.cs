using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Fratak_Shop.Web.Infrastructure.Auth;

/// <summary>Token settings (section <c>Jwt</c>). Secrets stay in configuration / environment, never in the database.</summary>
public class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "fratak-shop";
    public string Audience { get; set; } = "fratak-shop";

    /// <summary>
    /// HMAC-SHA256 signing key, at least 32 characters (env: <c>Jwt__SigningKey</c>).
    /// When empty a random key is generated at startup: secure, but tokens of other
    /// instances / previous runs are not accepted (users are silently re-issued a token
    /// from their refresh token, so nobody is logged out).
    /// </summary>
    public string? SigningKey { get; set; }

    /// <summary>Short lived access token (minutes).</summary>
    public int AccessTokenMinutes { get; set; } = 10;

    /// <summary>Refresh token lifetime for "remember me" logins (days, sliding).</summary>
    public int RefreshTokenDays { get; set; } = 30;

    /// <summary>Refresh token lifetime for browser-session logins (hours, fixed).</summary>
    public int SessionRefreshTokenHours { get; set; } = 12;

    public TimeSpan AccessTokenLifetime => TimeSpan.FromMinutes(Math.Clamp(AccessTokenMinutes, 1, 60));
    public TimeSpan PersistentRefreshLifetime => TimeSpan.FromDays(Math.Clamp(RefreshTokenDays, 1, 365));
    public TimeSpan SessionRefreshLifetime => TimeSpan.FromHours(Math.Clamp(SessionRefreshTokenHours, 1, 72));
}

/// <summary>Resolves the signing key once per process.</summary>
public sealed class JwtSigningKeyProvider
{
    public SymmetricSecurityKey Key { get; }

    public JwtSigningKeyProvider(IOptions<JwtOptions> options, IWebHostEnvironment environment, ILogger<JwtSigningKeyProvider> logger)
    {
        var configured = options.Value.SigningKey;

        if (!string.IsNullOrWhiteSpace(configured))
        {
            if (configured.Length < 32)
                throw new InvalidOperationException("Jwt:SigningKey must be at least 32 characters long.");

            Key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(configured)) { KeyId = "configured" };
            return;
        }

        if (!environment.IsDevelopment())
            logger.LogWarning("Jwt:SigningKey is not configured; a random key is used. Configure it when running more than one instance.");

        Key = new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(64)) { KeyId = "ephemeral" };
    }
}
