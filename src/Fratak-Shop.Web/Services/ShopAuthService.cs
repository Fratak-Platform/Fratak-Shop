using Fratak_Shop.Core.DTOs.Users.Query;
using Fratak_Shop.Core.Services.Auth;
using Fratak_Shop.Core.Services.Users;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Web.Infrastructure;
using Fratak_Shop.Web.Infrastructure.Auth;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Fratak_Shop.Web.Services;

/// <summary>
/// Storefront sign-in based on JWT: a short lived access token plus a rotating refresh
/// token, both in HttpOnly cookies. Signing in also moves the guest cart into the
/// customer's cart so nothing added before login is lost.
/// </summary>
public interface IShopAuthService
{
    Task SignInAsync(UserDto user, bool isPersistent);

    /// <summary>Re-issues the access token of the current session (after the name / phone changed).</summary>
    Task RefreshClaimsAsync(Guid userId);

    /// <summary>Logs out the current device.</summary>
    Task SignOutAsync();

    /// <summary>
    /// Invalidates every token of the user (new security stamp) and starts a fresh
    /// session on the current device. Used after a password change.
    /// </summary>
    Task SignOutOtherDevicesAsync(Guid userId, bool keepCurrent = true);
}

public class ShopAuthService(
    IHttpContextAccessor httpContextAccessor,
    ICartSessionService cartSession,
    IUserService userService,
    IRefreshTokenService refreshTokens,
    IAccessTokenService accessTokens,
    IMemoryCache cache,
    IKnownDeviceService knownDevices,
    ILogger<ShopAuthService> logger,
    IOptions<JwtOptions> jwtOptions) : IShopAuthService
{
    private HttpContext Http => httpContextAccessor.HttpContext ?? throw new InvalidOperationException("No active HTTP context.");

    public async Task SignInAsync(UserDto user, bool isPersistent)
    {
        var http = Http;
        var jwt = jwtOptions.Value;

        // A previous session in this browser is replaced, not left dangling.
        if (http.Request.Cookies.TryGetValue(AuthCookies.RefreshCookie, out var previous) && !string.IsNullOrEmpty(previous))
            await refreshTokens.RevokeAsync(previous, "replaced by new login");

        var stamp = await userService.GetSecurityStampAsync(user.Id)
                    ?? throw new InvalidOperationException("User no longer exists.");

        var refresh = await refreshTokens.IssueAsync(user.Id, isPersistent,
            isPersistent ? jwt.PersistentRefreshLifetime : jwt.SessionRefreshLifetime,
            ClientInfo.IpAddress(http), ClientInfo.UserAgent(http));

        var access = accessTokens.Create(user, stamp, refresh.FamilyId, isPersistent);
        AuthCookies.Write(http, access, refresh.Token, refresh.ExpiresAt, isPersistent);

        await cartSession.MergeGuestCartAsync(user.Id);

        // New-device alert (all sign-in paths end here). Never blocks the login.
        try
        {
            await knownDevices.RegisterSignInAsync(user.Id, ClientInfo.UserAgent(http), ClientInfo.IpAddress(http));
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Registering the sign-in device failed for {UserId}", user.Id);
        }
    }

    public async Task RefreshClaimsAsync(Guid userId)
    {
        var http = Http;
        var sessionId = http.User.GetSessionId();
        var user = await userService.GetUserByIdAsync(userId);
        var stamp = await userService.GetSecurityStampAsync(userId);
        if (user is null || stamp is null || sessionId is null)
            return;

        cache.Remove(CacheKeys.SecurityStamp(userId));
        AuthCookies.WriteAccess(http, accessTokens.Create(user, stamp, sessionId.Value, http.User.IsPersistentSession()));
    }

    public async Task SignOutAsync()
    {
        var http = Http;
        if (http.Request.Cookies.TryGetValue(AuthCookies.RefreshCookie, out var refresh) && !string.IsNullOrEmpty(refresh))
            await refreshTokens.RevokeAsync(refresh, "logout");

        AuthCookies.Clear(http);
    }

    public async Task SignOutOtherDevicesAsync(Guid userId, bool keepCurrent = true)
    {
        var http = Http;
        var currentSession = http.User.GetSessionId();
        var wasPersistent = http.User.IsPersistentSession();

        await userService.RotateSecurityStampAsync(userId);
        cache.Remove(CacheKeys.SecurityStamp(userId));
        await refreshTokens.RevokeAllAsync(userId, "security stamp changed");

        if (!keepCurrent || currentSession is null)
        {
            AuthCookies.Clear(http);
            return;
        }

        var user = await userService.GetUserByIdAsync(userId);
        if (user is null)
        {
            AuthCookies.Clear(http);
            return;
        }

        await SignInAsync(user, wasPersistent);
    }
}

public static class ClientInfo
{
    public static string? IpAddress(HttpContext context) => context.Connection.RemoteIpAddress?.ToString();

    public static string? UserAgent(HttpContext context)
    {
        var value = context.Request.Headers.UserAgent.ToString();
        return string.IsNullOrWhiteSpace(value) ? null : value.Length > 256 ? value[..256] : value;
    }
}
