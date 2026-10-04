using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Fratak_Shop.Core.DTOs.Auth.Query;
using Microsoft.IdentityModel.Tokens;

namespace Fratak_Shop.Web.Infrastructure;

public sealed class JwtTokenService(IConfiguration configuration)
{
    public string Create(AuthResultDto user)
    {
        var secret = configuration["Authentication:Jwt:Secret"];
        if (string.IsNullOrWhiteSpace(secret) || Encoding.UTF8.GetByteCount(secret) < 32)
            throw new InvalidOperationException("Authentication:Jwt:Secret must contain at least 32 bytes.");

        var issuer = configuration["Authentication:Jwt:Issuer"] ?? "Fratak-Shop";
        var audience = configuration["Authentication:Jwt:Audience"] ?? "Fratak-Shop.Client";
        var lifetimeMinutes = configuration.GetValue("Authentication:Jwt:LifetimeMinutes", 60);
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)),
            SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.UserId.ToString()),
            new(ClaimTypes.MobilePhone, user.PhoneNumber),
            new(ClaimTypes.Name, user.FullName ?? user.PhoneNumber),
        };
        claims.AddRange(user.Roles.Where(role => role != Fratak_Shop.Data.Entities.Users.UserRole.None)
            .Distinct()
            .Select(role => new Claim(ClaimTypes.Role, role.ToString())));

        var token = new JwtSecurityToken(
            issuer,
            audience,
            claims,
            DateTime.UtcNow,
            DateTime.UtcNow.AddMinutes(Math.Clamp(lifetimeMinutes, 5, 1440)),
            credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
