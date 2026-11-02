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
        var refreshToken = await RefreshTokenToUseAsync(context, accessTokens);
        if (refreshToken is not null)
        {
            var jwt = jwtOptions.Value;
            var rotation = await refreshTokens.RotateAsync(refreshToken, jwt.PersistentRefreshLifetime,
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
            else if (rotation.Failure == RefreshFailure.Concurrent && rotation.ConcurrentUserId is { } userId)
            {
                // Concurrent: a parallel request of the same browser already rotated it and its
                // response carries the new refresh cookie. مرحلهٔ هفدهم: این درخواست هم «مهمان» نمی‌ماند؛ یک توکن دسترسی
                // برای همین نشست می‌گیرد (کوکی تازه‌سازی دست نمی‌خورد). قبلاً این درخواست‌ها به صفحهٔ ورود/«دسترسی ندارید» می‌رفتند.
                var user = await userService.GetUserByIdAsync(userId);
                var stamp = user is null ? null : await userService.GetSecurityStampAsync(user.Id);
                if (user is not null && stamp is not null)
                    AuthCookies.WriteAccess(context, accessTokens.Create(user, stamp, rotation.ConcurrentFamilyId!.Value, rotation.ConcurrentPersistent));
            }
        }

        await next(context);
    }

    /// <summary>
    /// کوکی تازه‌سازی، اگر این درخواست باید توکن تازه بگیرد؛ وگرنه null.
    /// <para>تازه‌سازی وقتی: توکن دسترسی نیست، کمتر از یک دقیقه عمر دارد، یا (مرحلهٔ هفدهم) امضایش معتبر نیست؛ مثلاً
    /// توکنی که پیش از بازیافت برنامه با کلید دیگری امضا شده بود. قبلاً فقط تاریخ انقضا خوانده می‌شد و چنین توکنی تا ۱۰ دقیقه
    /// کاربر را «مهمان» نگه می‌داشت (رفتن به صفحهٔ ورود و «دسترسی ندارید» وسط کار).</para>
    /// </summary>
    private static async Task<string?> RefreshTokenToUseAsync(HttpContext context, IAccessTokenService accessTokens)
    {
        var request = context.Request;

        if (request.Headers.Authorization.Count > 0)
            return null;

        if (request.Path.StartsWithSegments("/api/auth"))
            return null;

        if (!request.Cookies.TryGetValue(AuthCookies.RefreshCookie, out var refreshToken)
            || string.IsNullOrEmpty(refreshToken) || refreshToken.Length > 512)
            return null;

        if (!request.Cookies.TryGetValue(AuthCookies.AccessCookie, out var access) || string.IsNullOrEmpty(access))
            return refreshToken;

        var expiry = accessTokens.ReadExpiry(access);
        if (expiry is null || expiry.Value - DateTimeOffset.UtcNow < RefreshWindow)
            return refreshToken;

        return await accessTokens.IsValidAsync(access) ? null : refreshToken;
    }
}
