using System.Security.Claims;
using Fratak_Shop.Core.DTOs.Users.Query;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Fratak_Shop.Web.Infrastructure.Auth;

public record AccessToken(string Token, DateTimeOffset ExpiresAt);

public interface IAccessTokenService
{
    AccessToken Create(UserDto user, string securityStamp, Guid sessionId, bool persistent);

    /// <summary>Expiry of a token without validating it (used to refresh shortly before it expires).</summary>
    DateTimeOffset? ReadExpiry(string token);
}

public class AccessTokenService(IOptions<JwtOptions> options, JwtSigningKeyProvider keyProvider) : IAccessTokenService
{
    private static readonly JsonWebTokenHandler Handler = new() { SetDefaultTimesOnTokenCreation = false };

    public AccessToken Create(UserDto user, string securityStamp, Guid sessionId, bool persistent)
    {
        var jwt = options.Value;
        var now = DateTimeOffset.UtcNow;
        var expires = now.Add(jwt.AccessTokenLifetime);

        var claims = new Dictionary<string, object>
        {
            [ShopClaimTypes.UserId] = user.Id.ToString(),
            [ShopClaimTypes.Phone] = user.PhoneNumber,
            [ShopClaimTypes.Role] = user.Role.ToString(),
            [ShopClaimTypes.SecurityStamp] = securityStamp,
            [ShopClaimTypes.SessionId] = sessionId.ToString(),
            [ShopClaimTypes.Persistent] = persistent ? "1" : "0",
            [JwtRegisteredClaimNames.Jti] = Guid.NewGuid().ToString("N"),
        };

        if (!string.IsNullOrWhiteSpace(user.FullName))
            claims[ShopClaimTypes.Name] = user.FullName;

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = jwt.Issuer,
            Audience = jwt.Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expires.UtcDateTime,
            Claims = claims,
            SigningCredentials = new SigningCredentials(keyProvider.Key, SecurityAlgorithms.HmacSha256),
        };

        return new AccessToken(Handler.CreateToken(descriptor), expires);
    }

    public DateTimeOffset? ReadExpiry(string token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 4096 || !Handler.CanReadToken(token))
            return null;

        try
        {
            var parsed = Handler.ReadJsonWebToken(token);
            return parsed.ValidTo == DateTime.MinValue ? null : new DateTimeOffset(parsed.ValidTo, TimeSpan.Zero);
        }
        catch (Exception exception) when (exception is ArgumentException or SecurityTokenMalformedException)
        {
            return null;
        }
    }

    /// <summary>Validation rules shared by the JwtBearer handler.</summary>
    public static TokenValidationParameters ValidationParameters(JwtOptions jwt, SecurityKey key) => new()
    {
        ValidateIssuer = true,
        ValidIssuer = jwt.Issuer,
        ValidateAudience = true,
        ValidAudience = jwt.Audience,
        ValidateLifetime = true,
        RequireExpirationTime = true,
        RequireSignedTokens = true,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = key,
        ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
        ClockSkew = TimeSpan.FromSeconds(30),
        NameClaimType = ShopClaimTypes.Name,
        RoleClaimType = ShopClaimTypes.Role,
    };
}

public static class ClaimsIdentityExtensions
{
    public static Guid? GetSessionId(this ClaimsPrincipal? principal) =>
        Guid.TryParse(principal?.FindFirst(ShopClaimTypes.SessionId)?.Value, out var id) ? id : null;

    public static bool IsPersistentSession(this ClaimsPrincipal? principal) =>
        principal?.FindFirst(ShopClaimTypes.Persistent)?.Value == "1";
}
