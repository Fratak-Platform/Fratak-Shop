using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Tests.E2E.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Fratak_Shop.Tests.E2E.Auth;

/// <summary>
/// شبیه‌سازی حمله به توکن‌ها: توکن بدون امضا (alg=none)، امضا با کلید دیگر، دستکاری نقش، توکن منقضی، صادرکننده/مخاطب
/// جعلی، توکن قدیمی بعد از تغییر رمز، استفادهٔ دوباره از refresh token دزدیده‌شده و لغو توکن.
/// انتظار: همه رد می‌شوند (401) و در حالت سرقت refresh token، کل خانوادهٔ نشست باطل می‌شود.
/// </summary>
[Collection(E2ECollection.Name)]
[Trait("Category", "E2E")]
[Trait("Category", "Security")]
[Trait("Area", "Auth")]
public class JwtAttackE2ETests(ShopApp app)
{
    private sealed record Tokens(string AccessToken, string RefreshToken);

    private static async Task<Tokens> IssueAsync(ShopClient client, string phone, string password)
    {
        using var response = await client.PostJsonAsync("/api/auth/token", new { phoneNumber = phone, password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return new Tokens(json.RootElement.GetProperty("accessToken").GetString()!, json.RootElement.GetProperty("refreshToken").GetString()!);
    }

    private static async Task<HttpStatusCode> MeAsync(ShopClient client, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await client.Http.SendAsync(request);
        return response.StatusCode;
    }

    private static async Task<HttpResponseMessage> RefreshAsync(ShopClient client, string refreshToken) =>
        await client.PostJsonAsync("/api/auth/refresh", new { refreshToken });

    private static string Sign(Dictionary<string, object> claims, string key, DateTime? expires = null, string issuer = ShopApp.Issuer,
        string audience = ShopApp.Audience, string keyId = "configured")
    {
        var now = DateTime.UtcNow;
        return new JsonWebTokenHandler { SetDefaultTimesOnTokenCreation = false }.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            IssuedAt = now.AddMinutes(-1),
            NotBefore = now.AddMinutes(-1),
            Expires = expires ?? now.AddMinutes(10),
            Claims = claims,
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)) { KeyId = keyId }, SecurityAlgorithms.HmacSha256),
        });
    }

    private static Dictionary<string, object> ClaimsOf(string token)
    {
        var jwt = new JsonWebToken(token);
        return jwt.Claims.Where(c => c.Type is not ("iss" or "aud" or "exp" or "nbf" or "iat"))
            .GroupBy(c => c.Type).ToDictionary(g => g.Key, g => (object)g.First().Value);
    }

    private static string Base64Url(string json) => Base64UrlEncoder.Encode(json);

    [Fact]
    public async Task Genuine_token_is_accepted()
    {
        var customer = await app.CreateCustomerAsync();
        using var client = app.CreateClient();
        var tokens = await IssueAsync(client, customer.Phone, customer.Password);

        Assert.Equal(HttpStatusCode.OK, await MeAsync(client, tokens.AccessToken));
    }

    [Fact]
    public async Task Unsigned_alg_none_token_is_rejected()
    {
        var customer = await app.CreateCustomerAsync();
        using var client = app.CreateClient();
        var tokens = await IssueAsync(client, customer.Phone, customer.Password);
        var payload = tokens.AccessToken.Split('.')[1];

        var forged = Base64Url("{\"alg\":\"none\",\"typ\":\"JWT\"}") + "." + payload + ".";

        Assert.Equal(HttpStatusCode.Unauthorized, await MeAsync(client, forged));
    }

    [Fact]
    public async Task Token_signed_with_another_key_is_rejected()
    {
        var customer = await app.CreateCustomerAsync();
        using var client = app.CreateClient();
        var tokens = await IssueAsync(client, customer.Phone, customer.Password);

        var forged = Sign(ClaimsOf(tokens.AccessToken), "attacker-key-attacker-key-attacker-key-0001");

        Assert.Equal(HttpStatusCode.Unauthorized, await MeAsync(client, forged));
    }

    [Fact]
    public async Task Changing_the_role_inside_a_token_breaks_its_signature()
    {
        var customer = await app.CreateCustomerAsync();
        using var client = app.CreateClient();
        var tokens = await IssueAsync(client, customer.Phone, customer.Password);
        var parts = tokens.AccessToken.Split('.');
        var payload = Encoding.UTF8.GetString(Base64UrlEncoder.DecodeBytes(parts[1]));
        Assert.Contains("\"role\":\"Customer\"", payload, StringComparison.Ordinal);

        var escalated = parts[0] + "." + Base64Url(payload.Replace("\"role\":\"Customer\"", "\"role\":\"SuperAdmin\"", StringComparison.Ordinal)) + "." + parts[2];

        Assert.Equal(HttpStatusCode.Unauthorized, await MeAsync(client, escalated));
        using var request = new HttpRequestMessage(HttpMethod.Get, "/admin");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", escalated);
        using var admin = await client.Http.SendAsync(request);
        Assert.NotEqual(HttpStatusCode.OK, admin.StatusCode);
    }

    [Fact]
    public async Task Expired_token_is_rejected_even_with_a_valid_signature()
    {
        var customer = await app.CreateCustomerAsync();
        using var client = app.CreateClient();
        var tokens = await IssueAsync(client, customer.Phone, customer.Password);
        var claims = ClaimsOf(tokens.AccessToken);

        var expired = new JsonWebTokenHandler { SetDefaultTimesOnTokenCreation = false }.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = ShopApp.Issuer,
            Audience = ShopApp.Audience,
            IssuedAt = DateTime.UtcNow.AddHours(-2),
            NotBefore = DateTime.UtcNow.AddHours(-2),
            Expires = DateTime.UtcNow.AddMinutes(-5),
            Claims = claims,
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(ShopApp.SigningKey)) { KeyId = "configured" },
                SecurityAlgorithms.HmacSha256),
        });

        Assert.Equal(HttpStatusCode.Unauthorized, await MeAsync(client, expired));
    }

    [Theory]
    [InlineData("evil-issuer", ShopApp.Audience)]
    [InlineData(ShopApp.Issuer, "evil-audience")]
    public async Task Token_for_another_issuer_or_audience_is_rejected(string issuer, string audience)
    {
        var customer = await app.CreateCustomerAsync();
        using var client = app.CreateClient();
        var tokens = await IssueAsync(client, customer.Phone, customer.Password);

        var forged = Sign(ClaimsOf(tokens.AccessToken), ShopApp.SigningKey, issuer: issuer, audience: audience);

        Assert.Equal(HttpStatusCode.Unauthorized, await MeAsync(client, forged));
    }

    [Fact]
    public async Task Correctly_signed_token_with_an_old_security_stamp_is_rejected()
    {
        // حتی اگر کلید امضا لو برود، مهر امنیتی (sst) باید با پایگاه داده یکی باشد.
        var customer = await app.CreateCustomerAsync();
        using var client = app.CreateClient();
        var tokens = await IssueAsync(client, customer.Phone, customer.Password);
        var claims = ClaimsOf(tokens.AccessToken);
        claims["sst"] = Guid.NewGuid().ToString("N");

        Assert.Equal(HttpStatusCode.Unauthorized, await MeAsync(client, Sign(claims, ShopApp.SigningKey)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-jwt")]
    [InlineData("a.b.c")]
    [InlineData("eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxIn0.")]
    public async Task Garbage_tokens_are_rejected_without_server_errors(string token)
    {
        using var client = app.CreateClient();
        var started = DateTimeOffset.UtcNow;

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + token);
        using var response = await client.Http.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(app.Logs.ErrorsSince(started));
    }

    [Fact]
    public async Task Garbage_auth_cookie_is_ignored_and_pages_still_work()
    {
        using var client = app.CreateClient();
        var started = DateTimeOffset.UtcNow;

        async Task<HttpResponseMessage> GetWithCookie(string url)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation("Cookie", "fratak_at=xxx.yyy.zzz; fratak_rt=" + new string('A', 600));
            return await client.Http.SendAsync(request);
        }

        using var home = await GetWithCookie("/");
        Assert.Equal(HttpStatusCode.OK, home.StatusCode);
        using var profile = await GetWithCookie("/profile");
        Assert.Equal(HttpStatusCode.Redirect, profile.StatusCode);
        Assert.Empty(app.Logs.ErrorsSince(started));
    }

    [Fact]
    public async Task Access_token_is_rejected_after_the_password_changes()
    {
        var customer = await app.CreateCustomerAsync();
        using var client = app.CreateClient();
        var tokens = await IssueAsync(client, customer.Phone, customer.Password);
        Assert.Equal(HttpStatusCode.OK, await MeAsync(client, tokens.AccessToken));

        await using (var scope = app.Scope())
        {
            var users = scope.ServiceProvider.GetRequiredService<Fratak_Shop.Core.Services.Users.IUserService>();
            var result = await users.ChangePasswordAsync(new Fratak_Shop.Core.DTOs.Users.Command.ChangePasswordCommand
            {
                UserId = customer.Id,
                CurrentPassword = customer.Password,
                NewPassword = "Changed#789",
            });
            Assert.True(result.IsSuccess(), result.Message);
        }

        Assert.Equal(HttpStatusCode.Unauthorized, await MeAsync(client, tokens.AccessToken));
    }

    [Fact]
    public async Task Refresh_rotates_the_token_and_a_stolen_old_token_burns_the_whole_session()
    {
        var customer = await app.CreateCustomerAsync();
        using var victim = app.CreateClient();
        var first = await IssueAsync(victim, customer.Phone, customer.Password);

        using var rotated = await RefreshAsync(victim, first.RefreshToken);
        Assert.Equal(HttpStatusCode.OK, rotated.StatusCode);
        var second = await rotated.Content.ReadFromJsonAsync<JsonElement>();
        var secondRefresh = second.GetProperty("refreshToken").GetString()!;
        Assert.NotEqual(first.RefreshToken, secondRefresh);

        // مهاجم بعد از بازهٔ هم‌زمانی (۳۰ ثانیه) توکن قدیمی را استفاده می‌کند → تشخیص استفادهٔ دوباره.
        await app.WithDbAsync(db => db.UserRefreshTokens.Where(t => t.UserId == customer.Id && t.RevokedAt != null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, DateTimeOffset.UtcNow.AddMinutes(-5))));
        using var attacker = app.CreateClient();
        using var stolen = await RefreshAsync(attacker, first.RefreshToken);
        Assert.Equal(HttpStatusCode.Unauthorized, stolen.StatusCode);

        // نتیجه: توکن جدید قربانی هم باطل شده و باید دوباره وارد شود (مهاجم هم چیزی به دست نمی‌آورد).
        using var afterTheft = await RefreshAsync(victim, secondRefresh);
        Assert.Equal(HttpStatusCode.Unauthorized, afterTheft.StatusCode);
        Assert.Equal(0, await app.WithDbAsync(db => db.UserRefreshTokens.CountAsync(t => t.UserId == customer.Id && t.RevokedAt == null)));
    }

    [Fact]
    public async Task Parallel_refresh_with_the_same_token_does_not_mint_two_sessions()
    {
        var customer = await app.CreateCustomerAsync();
        using var client = app.CreateClient();
        var tokens = await IssueAsync(client, customer.Phone, customer.Password);

        var responses = await Task.WhenAll(Enumerable.Range(0, 6).Select(async _ =>
        {
            using var c = app.CreateClient();
            using var r = await RefreshAsync(c, tokens.RefreshToken);
            return r.StatusCode;
        }));

        Assert.Equal(1, responses.Count(s => s == HttpStatusCode.OK));
        Assert.Equal(1, await app.WithDbAsync(db => db.UserRefreshTokens.CountAsync(t => t.UserId == customer.Id && t.RevokedAt == null)));
    }

    [Fact]
    public async Task Revoked_refresh_token_cannot_be_used()
    {
        var customer = await app.CreateCustomerAsync();
        using var client = app.CreateClient();
        var tokens = await IssueAsync(client, customer.Phone, customer.Password);

        using var revoke = await client.PostJsonAsync("/api/auth/revoke", new { refreshToken = tokens.RefreshToken });
        Assert.Equal(HttpStatusCode.NoContent, revoke.StatusCode);

        using var refresh = await RefreshAsync(client, tokens.RefreshToken);
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
    }

    [Theory]
    [InlineData(null, "123456")]
    [InlineData("09120000000", null)]
    [InlineData("09120000000", "")]
    public async Task Token_endpoint_validates_its_input(string? phone, string? password)
    {
        using var client = app.CreateClient();

        using var response = await client.PostJsonAsync("/api/auth/token", new { phoneNumber = phone, password });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Oversized_credentials_are_refused_before_hashing()
    {
        using var client = app.CreateClient();

        using var response = await client.PostJsonAsync("/api/auth/token", new { phoneNumber = "09120000000", password = new string('x', 100_000) });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Token_endpoint_is_rate_limited_per_ip()
    {
        using var client = app.CreateClient();
        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 12; i++)
        {
            // شمارهٔ ناموجود: حساب واقعی‌ای قفل نمی‌شود.
            using var response = await client.PostJsonAsync("/api/auth/token", new { phoneNumber = ShopApp.NewPhone(), password = "wrong-" + i });
            statuses.Add(response.StatusCode);
        }

        Assert.Contains(HttpStatusCode.TooManyRequests, statuses);
        Assert.True(statuses.Take(10).All(s => s != HttpStatusCode.TooManyRequests));
    }

    [Fact]
    public async Task Customer_token_cannot_open_the_admin_panel()
    {
        var customer = await app.CreateCustomerAsync();
        using var client = app.CreateClient();
        var tokens = await IssueAsync(client, customer.Phone, customer.Password);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/admin/orders");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        using var response = await client.Http.SendAsync(request);

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
    }
}
