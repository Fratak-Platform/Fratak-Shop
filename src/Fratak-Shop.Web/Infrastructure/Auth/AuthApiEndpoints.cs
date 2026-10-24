using Fratak_Shop.Core.DTOs.Users.Command;
using Fratak_Shop.Core.Services.Auth;
using Fratak_Shop.Core.Services.Users;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Web.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace Fratak_Shop.Web.Infrastructure.Auth;

/// <summary>
/// Token API for non-browser clients (mobile app, SPA, admin panel): tokens travel in the
/// JSON body and the access token is sent back as <c>Authorization: Bearer ...</c>.
/// No cookies are involved, so these endpoints are not exposed to CSRF.
/// </summary>
public static class AuthApiEndpoints
{
    public record TokenRequest(string? PhoneNumber, string? Password);

    public record RefreshRequest(string? RefreshToken);

    public record TokenResponse(
        string AccessToken,
        DateTimeOffset AccessTokenExpiresAt,
        string RefreshToken,
        DateTimeOffset RefreshTokenExpiresAt,
        string TokenType = "Bearer");

    public static IEndpointRouteBuilder MapAuthApi(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth").DisableAntiforgery();

        group.MapPost("/token", async (TokenRequest request, HttpContext http, IUserService users,
            IRefreshTokenService refreshTokens, IAccessTokenService accessTokens, IOptions<JwtOptions> jwt) =>
        {
            if (string.IsNullOrWhiteSpace(request.PhoneNumber) || string.IsNullOrEmpty(request.Password)
                || request.PhoneNumber.Length > 20 || request.Password.Length > 128)
                return Error(StatusCodes.Status400BadRequest, "شماره موبایل و رمز عبور الزامی است.");

            var login = await users.LoginAsync(new LoginCommand { PhoneNumber = request.PhoneNumber, Password = request.Password });
            if (!login.IsSuccess() || login.Data is null)
                return Error(StatusCodes.Status401Unauthorized, login.Message);

            var user = login.Data;
            var stamp = await users.GetSecurityStampAsync(user.Id);
            if (stamp is null)
                return Error(StatusCodes.Status401Unauthorized, "حساب کاربری یافت نشد.");

            var refresh = await refreshTokens.IssueAsync(user.Id, true, jwt.Value.PersistentRefreshLifetime,
                ClientInfo.IpAddress(http), ClientInfo.UserAgent(http));
            var access = accessTokens.Create(user, stamp, refresh.FamilyId, true);

            return Results.Ok(new TokenResponse(access.Token, access.ExpiresAt, refresh.Token, refresh.ExpiresAt));
        });

        group.MapPost("/refresh", async (RefreshRequest request, HttpContext http, IUserService users,
            IRefreshTokenService refreshTokens, IAccessTokenService accessTokens, IOptions<JwtOptions> jwt) =>
        {
            if (string.IsNullOrWhiteSpace(request.RefreshToken) || request.RefreshToken.Length > 512)
                return Error(StatusCodes.Status400BadRequest, "توکن نامعتبر است.");

            var rotation = await refreshTokens.RotateAsync(request.RefreshToken, jwt.Value.PersistentRefreshLifetime,
                ClientInfo.IpAddress(http), ClientInfo.UserAgent(http));
            if (!rotation.Succeeded)
                return Error(StatusCodes.Status401Unauthorized, "نشست شما منقضی شده است؛ دوباره وارد شوید.");

            var issued = rotation.Token!;
            var user = await users.GetUserByIdAsync(issued.UserId);
            var stamp = user is null ? null : await users.GetSecurityStampAsync(user.Id);
            if (user is null || stamp is null)
            {
                await refreshTokens.RevokeAsync(issued.Token, "user unavailable");
                return Error(StatusCodes.Status401Unauthorized, "حساب کاربری یافت نشد.");
            }

            var access = accessTokens.Create(user, stamp, issued.FamilyId, issued.IsPersistent);
            return Results.Ok(new TokenResponse(access.Token, access.ExpiresAt, issued.Token, issued.ExpiresAt));
        });

        group.MapPost("/revoke", async (RefreshRequest request, IRefreshTokenService refreshTokens) =>
        {
            if (!string.IsNullOrWhiteSpace(request.RefreshToken) && request.RefreshToken.Length <= 512)
                await refreshTokens.RevokeAsync(request.RefreshToken, "revoked by client");

            return Results.NoContent();
        });

        group.MapGet("/me", [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (HttpContext http, IUserService users) =>
        {
            var userId = http.User.GetUserId();
            var user = userId is null ? null : await users.GetUserByIdAsync(userId.Value);
            if (user is null)
                return Results.Unauthorized();

            return Results.Ok(new
            {
                id = user.Id,
                phoneNumber = user.PhoneNumber,
                firstName = user.FirstName,
                lastName = user.LastName,
                email = user.Email,
                isPhoneVerified = user.IsPhoneVerified,
                walletBalance = user.WalletBalance,
            });
        });

        return app;
    }

    private static IResult Error(int status, string? message) =>
        Results.Json(new { error = message ?? "درخواست نامعتبر است." }, statusCode: status);
}
