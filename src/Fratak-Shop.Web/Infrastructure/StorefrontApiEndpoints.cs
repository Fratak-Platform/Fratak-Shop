using Fratak_Shop.Core.Services.Comments;
using Fratak_Shop.Core.Services.Notifications;
using Fratak_Shop.Core.Services.Products;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Entities.Products;
using Fratak_Shop.Web.Helpers;
using Fratak_Shop.Web.Infrastructure.Security;
using Fratak_Shop.Web.Services;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.Extensions.Caching.Memory;

namespace Fratak_Shop.Web.Infrastructure;

/// <summary>
/// Small JSON endpoints used by shop.js (they also work on cached pages): antiforgery token,
/// captcha refresh, web-push subscription, product view beacon, price history, «خبرم کن»,
/// Q&amp;A votes and answers. State-changing calls require the antiforgery header
/// (<c>RequestVerificationToken</c>) and, where personal, a signed-in customer.
/// </summary>
public static class StorefrontApiEndpoints
{
    private const string LoginRequired = "برای این کار ابتدا وارد حساب کاربری شوید.";

    public static void MapStorefrontApi(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api");

        api.MapGet("/antiforgery", (HttpContext http, IAntiforgery antiforgery) =>
        {
            var tokens = antiforgery.GetAndStoreTokens(http);
            NoStore(http);
            return Results.Ok(new { token = tokens.RequestToken });
        });

        api.MapGet("/captcha", (HttpContext http, ICaptchaService captcha) =>
        {
            var challenge = captcha.Create();
            NoStore(http);
            return Results.Ok(new { token = challenge.Token, image = challenge.Image });
        });

        // ── Web push ────────────────────────────────────────────────
        api.MapGet("/push/config", async (HttpContext http, ISiteContext site, IWebPushService push) =>
        {
            await site.LoadAsync(http.RequestAborted);
            var keys = site.Settings.CanUseWebPush ? await push.GetVapidKeysAsync() : null;
            NoStore(http);
            return Results.Ok(new { enabled = keys is not null, publicKey = keys?.PublicKey, signedIn = http.User.IsSignedIn() });
        });

        api.MapPost("/push/subscribe", async (HttpContext http, IAntiforgery antiforgery, IWebPushService push, PushSubscriptionInput input) =>
        {
            if (await Guard(http, antiforgery, requireUser: true) is { } denied)
                return denied;

            var result = await push.SubscribeAsync(http.User.GetUserId(), input.Endpoint, input.Keys?.P256dh, input.Keys?.Auth, ClientInfo.UserAgent(http));
            return ToJson(result);
        });

        api.MapPost("/push/unsubscribe", async (HttpContext http, IAntiforgery antiforgery, IWebPushService push, PushSubscriptionInput input) =>
        {
            if (await Guard(http, antiforgery, requireUser: false) is { } denied)
                return denied;

            await push.UnsubscribeAsync(input.Endpoint);
            return Results.Ok(new { ok = true });
        });

        // ── اعلان‌ها ─────────────────────────────────────────────────
        // تعداد اعلان‌های خوانده‌نشده؛ live.js بعد از «refresh» یا وصل دوباره صدا می‌زند.
        api.MapGet("/notifications/unread", async (HttpContext http, INotificationService notifications) =>
        {
            NoStore(http);
            var userId = http.User.GetUserId();
            if (userId is null)
                return Results.Ok(new { unread = 0 });
            return Results.Ok(new { unread = await notifications.GetUnreadCountAsync(userId.Value) });
        });

        // ── Products ────────────────────────────────────────────────
        // View beacon: counts a visit at most once per visitor and product every 30 minutes and
        // remembers the product in "recently viewed" (works on cached product pages too).
        api.MapPost("/products/{id:guid}/view", async (Guid id, HttpContext http, IProductService products, IMemoryCache cache) =>
        {
            var visitor = http.User.GetUserId()?.ToString("N") ?? http.Connection.RemoteIpAddress?.ToString() ?? "-";
            var key = $"view:{visitor}:{id:N}";
            if (!cache.TryGetValue(key, out _))
            {
                cache.Set(key, true, TimeSpan.FromMinutes(30));
                await products.AddProductVisitAsync(id);
            }

            RecentlyViewed.Push(http, id);
            NoStore(http);
            return Results.NoContent();
        });

        api.MapGet("/products/{id:guid}/price-history", async (Guid id, int? days, HttpContext http, ISiteContext site, IPriceHistoryService history) =>
        {
            await site.LoadAsync(http.RequestAborted);
            if (!site.Settings.IsPriceHistoryEnabled)
                return Results.NotFound();

            var chart = await history.GetChartAsync(id, days ?? site.Settings.PriceHistoryDays, http.RequestAborted);
            http.Response.Headers.CacheControl = "public, max-age=600";
            return Results.Ok(new
            {
                days = chart.Days,
                current = chart.Current,
                lowest = chart.Lowest,
                highest = chart.Highest,
                hasData = chart.HasData,
                points = chart.Points.Select(p => new
                {
                    t = p.At.ToUnixTimeMilliseconds(),
                    date = Format.Date(p.At),
                    price = p.FinalPrice,
                    list = p.Price,
                    inStock = p.IsInStock,
                }),
            });
        });

        api.MapPost("/products/{id:guid}/alerts", async (Guid id, HttpContext http, IAntiforgery antiforgery, IProductAlertService alerts, AlertInput input) =>
        {
            if (await Guard(http, antiforgery, requireUser: true) is { } denied)
                return denied;

            if (input.Type is not (ProductAlertType.BackInStock or ProductAlertType.PriceDrop))
                return Results.BadRequest(new { error = "نوع اطلاع‌رسانی معتبر نیست." });

            var userId = http.User.GetUserId()!.Value;
            var result = input.Active
                ? await alerts.SubscribeAsync(userId, id, input.Type, input.TargetPrice)
                : await alerts.UnsubscribeAsync(userId, id, input.Type);
            var state = await alerts.GetStateAsync(userId, id);
            return result.Status == EnOperationResultStatus.Success
                ? Results.Ok(new { message = result.Message, backInStock = state.BackInStock, priceDrop = state.PriceDrop, targetPrice = state.TargetPrice })
                : Results.BadRequest(new { error = result.Message });
        });

        // ── Questions & answers ─────────────────────────────────────
        api.MapPost("/comments/{id:guid}/vote", async (Guid id, HttpContext http, IAntiforgery antiforgery, ICommentEngagementService engagement, VoteInput input) =>
        {
            if (await Guard(http, antiforgery, requireUser: true) is { } denied)
                return denied;

            var result = await engagement.VoteAsync(http.User.GetUserId()!.Value, id, input.Helpful);
            return result.Status == EnOperationResultStatus.Success && result.Data is { } votes
                ? Results.Ok(new { helpful = votes.HelpfulCount, notHelpful = votes.NotHelpfulCount, myVote = votes.MyVote })
                : Results.BadRequest(new { error = result.Message });
        });

        api.MapPost("/comments/{id:guid}/answer", async (Guid id, HttpContext http, IAntiforgery antiforgery, ICommentEngagementService engagement, AnswerInput input) =>
        {
            if (await Guard(http, antiforgery, requireUser: true) is { } denied)
                return denied;

            var result = await engagement.AnswerQuestionAsync(http.User.GetUserId()!.Value, id, input.Content);
            return ToJson(result);
        });
    }

    /// <summary>Antiforgery header + (optionally) a signed-in customer. Null = allowed.</summary>
    private static async Task<IResult?> Guard(HttpContext http, IAntiforgery antiforgery, bool requireUser)
    {
        NoStore(http);
        if (!await antiforgery.IsRequestValidAsync(http))
            return Results.BadRequest(new { error = "نشست شما منقضی شده است؛ صفحه را دوباره بارگذاری کنید." });

        if (requireUser && !http.User.IsSignedIn())
        {
            var referer = http.Request.Headers.Referer.ToString();
            var returnUrl = Uri.TryCreate(referer, UriKind.Absolute, out var uri) && uri.Host == http.Request.Host.Host ? uri.PathAndQuery : "/";
            return Results.Json(new { error = LoginRequired, login = "/login?returnUrl=" + Uri.EscapeDataString(returnUrl) }, statusCode: StatusCodes.Status401Unauthorized);
        }

        return null;
    }

    private static IResult ToJson(OperationResult result) => result.Status == EnOperationResultStatus.Success
        ? Results.Ok(new { message = result.Message })
        : Results.BadRequest(new { error = result.Message });

    private static void NoStore(HttpContext http) => http.Response.Headers.CacheControl = "no-store";

    public sealed record PushKeys(string? P256dh, string? Auth);

    public sealed record PushSubscriptionInput(string? Endpoint, PushKeys? Keys);

    public sealed record AlertInput(ProductAlertType Type, bool Active, decimal? TargetPrice);

    public sealed record VoteInput(bool Helpful);

    public sealed record AnswerInput(string? Content);
}
