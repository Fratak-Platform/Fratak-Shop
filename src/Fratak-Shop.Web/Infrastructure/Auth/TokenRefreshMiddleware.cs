using Fratak_Shop.Core.DTOs.Auth;
using Fratak_Shop.Core.Services.Auth;
using Fratak_Shop.Core.Services.Users;
using Fratak_Shop.Web.Services;
using Microsoft.Extensions.Options;

namespace Fratak_Shop.Web.Infrastructure.Auth;

/// <summary>
/// Silent refresh for browser sessions: when the access-token cookie is missing or about
/// to expire and a refresh-token cookie exists, the refresh token is rotated and a new
/// access token is issued before authentication runs. API clients that send an
/// <c>Authorization</c> header use <c>/api/auth/refresh</c> instead.
/// </summary>
public class TokenRefreshMiddleware(RequestDelegate next, ILogger<TokenRefreshMiddleware> logger)
{
    private static readonly TimeSpan RefreshWindow = TimeSpan.FromMinutes(1);

    public async Task InvokeAsync(
        HttpContext context,
        IRefreshTokenService refreshTokens,
        IAccessTokenService accessTokens,
        IUserService userService,
        IOptions<JwtOptions> jwtOptions)
    {
        if (ShouldRefresh(context, accessTokens, out var refreshToken))
        {
            var jwt = jwtOptions.Value;
            var rotation = await refreshTokens.RotateAsync(refreshToken!, jwt.PersistentRefreshLifetime,
                ClientInfo.IpAddress(context), ClientInfo.UserAgent(context));

            if (rotation.Succeeded)
            {
                var issued = rotation.Token!;
                var user = await userService.GetUserByIdAsync(issued.UserId);
                var stamp = user is null ? null : await userService.GetSecurityStampAsync(user.Id);

                if (user is not null && stamp is not null)
                {
                    var access = accessTokens.Create(user, stamp, issued.FamilyId, issued.IsPersistent);
                    AuthCookies.Write(context, access, issued.Token, issued.ExpiresAt, issued.IsPersistent);
                }
                else
                {
                    await refreshTokens.RevokeAsync(issued.Token, "user unavailable");
                    AuthCookies.Clear(context);
                }
            }
            else if (rotation.Failure is RefreshFailure.Invalid or RefreshFailure.Reused)
            {
                if (rotation.Failure == RefreshFailure.Reused)
                    logger.LogWarning("Refresh token reuse detected from {Ip}; the session was revoked.", ClientInfo.IpAddress(context));

                AuthCookies.Clear(context);
            }
            // Concurrent: a parallel request of the same browser already rotated it; that
            // response carries the new cookies, this one continues with what it has.
        }

        await next(context);
    }

    private static bool ShouldRefresh(HttpContext context, IAccessTokenService accessTokens, out string? refreshToken)
    {
        refreshToken = null;
        var request = context.Request;

        if (request.Headers.Authorization.Count > 0)
            return false;

        if (request.Path.StartsWithSegments("/api/auth"))
            return false;

        if (!request.Cookies.TryGetValue(AuthCookies.RefreshCookie, out refreshToken)
            || string.IsNullOrEmpty(refreshToken) || refreshToken.Length > 512)
            return false;

        if (!request.Cookies.TryGetValue(AuthCookies.AccessCookie, out var access) || string.IsNullOrEmpty(access))
            return true;

        var expiry = accessTokens.ReadExpiry(access);
        return expiry is null || expiry.Value - DateTimeOffset.UtcNow < RefreshWindow;
    }
}
