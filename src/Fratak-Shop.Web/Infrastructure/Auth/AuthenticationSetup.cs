using Fratak_Shop.Core.Services.Users;
using Fratak_Shop.Core.Utilities;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Fratak_Shop.Web.Infrastructure.Auth;

public static class AuthenticationSetup
{
    private static readonly TimeSpan StampCacheLifetime = TimeSpan.FromMinutes(2);

    public static IServiceCollection AddShopAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));
        services.AddSingleton<JwtSigningKeyProvider>();
        services.AddSingleton<IAccessTokenService, AccessTokenService>();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();

        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>, JwtSigningKeyProvider>((options, jwt, keys) =>
            {
                options.MapInboundClaims = false;
                options.SaveToken = false;
                options.TokenValidationParameters = AccessTokenService.ValidationParameters(jwt.Value, keys.Key);
                options.Events = new JwtBearerEvents
                {
                    OnMessageReceived = OnMessageReceived,
                    OnTokenValidated = OnTokenValidated,
                    OnChallenge = OnChallenge,
                    OnForbidden = OnForbidden,
                };
            });

        services.AddAuthorization();
        return services;
    }

    /// <summary>Bearer header (API clients) or the HttpOnly cookie (browser).</summary>
    private static Task OnMessageReceived(MessageReceivedContext context)
    {
        if (context.HttpContext.Items[AuthCookies.FreshAccessTokenItem] is string fresh)
        {
            context.Token = fresh;
            return Task.CompletedTask;
        }

        if (context.Request.Headers.Authorization.Count == 0
            && context.Request.Cookies.TryGetValue(AuthCookies.AccessCookie, out var cookie)
            && !string.IsNullOrEmpty(cookie))
        {
            context.Token = cookie;
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Rejects tokens issued before the user's security stamp changed (password change,
    /// "log out other devices", deactivation). The stamp is cached briefly.
    /// </summary>
    private static async Task OnTokenValidated(TokenValidatedContext context)
    {
        var principal = context.Principal;
        var userId = principal.GetUserId();
        var tokenStamp = principal?.FindFirst(ShopClaimTypes.SecurityStamp)?.Value;

        if (userId is null || string.IsNullOrEmpty(tokenStamp))
        {
            context.Fail("Malformed token.");
            return;
        }

        var services = context.HttpContext.RequestServices;
        var cache = services.GetRequiredService<IMemoryCache>();

        var currentStamp = await cache.GetOrCreateAsync(CacheKeys.SecurityStamp(userId.Value), entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = StampCacheLifetime;
            entry.Size = 1;
            return services.GetRequiredService<IUserService>().GetSecurityStampAsync(userId.Value);
        });

        if (!string.Equals(currentStamp, tokenStamp, StringComparison.Ordinal))
            context.Fail("Security stamp changed.");
    }

    private static Task OnChallenge(JwtBearerChallengeContext context)
    {
        if (IsApiRequest(context.Request))
            return Task.CompletedTask;

        context.HandleResponse();
        var returnUrl = context.Request.PathBase + context.Request.Path + context.Request.QueryString;
        context.Response.Redirect($"/login?returnUrl={Uri.EscapeDataString(returnUrl)}");
        return Task.CompletedTask;
    }

    private static Task OnForbidden(ForbiddenContext context)
    {
        if (!IsApiRequest(context.Request))
            context.Response.Redirect("/");

        return Task.CompletedTask;
    }

    private static bool IsApiRequest(HttpRequest request) => request.Path.StartsWithSegments("/api");
}
