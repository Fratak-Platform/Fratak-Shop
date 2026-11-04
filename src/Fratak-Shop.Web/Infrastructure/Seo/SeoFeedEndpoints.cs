using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Xml;
using Fratak_Shop.Core.Services.Seo.Autopilot;
using Fratak_Shop.Core.Services.Seo.Feeds;
using Fratak_Shop.Data.Contexts;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Web.Infrastructure.Seo;

/// <summary>
/// مسیرهای عمومی دور سئوی خودکار:
/// <list type="bullet">
/// <item><c>POST /torob_api/v3/products</c>: API رسمی ترب نسخهٔ ۳ (فقط با توکن امضاشدهٔ ترب – EdDSA).</item>
/// <item><c>GET /feeds/products.xml</c>: فید عمومی محصولات (RSS 2.0 با فضای نام g: – برای مقایسه‌گرها و Merchant).</item>
/// <item><c>GET /indexnow/{key}.txt</c>: فایل تأیید مالکیت کلید IndexNow.</item>
/// <item><c>POST /api/rum</c>: سرعت واقعی کاربران (<see cref="RumEndpoint"/>).</item>
/// </list>
/// <para>کجا ثبت می‌شود؟ Program.cs (<c>app.MapSeoAutopilotEndpoints()</c>).</para>
/// </summary>
public static class SeoFeedEndpoints
{
    private static readonly JsonSerializerOptions TorobJson = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public const int MaxTorobBody = 200_000;
    public const int FeedMaxProducts = 5000;

    public static IEndpointRouteBuilder MapSeoAutopilotEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/torob_api/v3/products", async (HttpContext http, ISeoLinkTargetProvider provider, ITorobFeedService torob, SeoHost host) =>
        {
            http.Response.Headers.CacheControl = "no-store";
            http.Response.Headers["X-Robots-Tag"] = "noindex";
            var options = await provider.OptionsAsync(http.RequestAborted);
            if (!options.ProductFeeds)
                return Results.Json(new { error = "product api is disabled" }, statusCode: StatusCodes.Status404NotFound);

            var version = http.Request.Headers["X-Torob-Token-Version"].ToString();
            if (version.Length > 0 && version != "1")
                return Results.Json(new { error = "unsupported token version" }, statusCode: StatusCodes.Status401Unauthorized);
            // aud باید دقیقاً دامنهٔ API باشد: از تنظیمات (نه هدر Host که جعل‌شدنی است).
            var audience = host.Host ?? http.Request.Host.Value;
            var tokenError = TorobTokenValidator.Validate(http.Request.Headers["X-Torob-Token"].ToString(), audience, DateTimeOffset.UtcNow);
            if (tokenError is not null)
                return Results.Json(new { error = tokenError }, statusCode: StatusCodes.Status401Unauthorized);

            if (http.Request.ContentLength is > MaxTorobBody)
                return Results.Json(new { error = "request body is too large" }, statusCode: StatusCodes.Status400BadRequest);
            using var reader = new StreamReader(http.Request.Body, Encoding.UTF8);
            var buffer = new char[MaxTorobBody + 1];
            var length = await reader.ReadBlockAsync(buffer.AsMemory(), http.RequestAborted);
            if (length > MaxTorobBody)
                return Results.Json(new { error = "request body is too large" }, statusCode: StatusCodes.Status400BadRequest);

            var (query, error) = TorobFeedService.Parse(new string(buffer, 0, length));
            if (query is null)
                return Results.Json(new { error }, statusCode: StatusCodes.Status400BadRequest);
            var response = await torob.QueryAsync(query, host.BaseFor(http), http.RequestAborted);
            return Results.Json(response, TorobJson);
        }).DisableAntiforgery();

        app.MapGet("/feeds/products.xml", async (HttpContext http, ISeoLinkTargetProvider provider, ITorobFeedService torob, SeoHost host,
            Fratak_Shop.Core.Services.Settings.IStoreSettingService settings) =>
        {
            var options = await provider.OptionsAsync(http.RequestAborted);
            if (!options.ProductFeeds)
                return Results.NotFound();
            var store = await settings.GetAsync(http.RequestAborted);
            var baseUrl = host.BaseFor(http);
            var products = await torob.AllAsync(baseUrl, FeedMaxProducts, http.RequestAborted);
            http.Response.Headers.CacheControl = "public, max-age=1800";
            return Results.Bytes(Feed(products, store.SiteName ?? "فروشگاه", baseUrl), "application/xml; charset=utf-8");
        });

        app.MapGet("/indexnow/{key}.txt", async (string key, FratakShopDbContext context, CancellationToken ct) =>
        {
            if (key.Length is < 8 or > 128 || !key.All(c => char.IsAsciiLetterOrDigit(c) || c == '-'))
                return Results.NotFound();
            var stored = await context.SeoAutopilotConfigs.AsNoTracking().OrderBy(c => c.CreatedAt).Select(c => c.IndexNowKey).FirstOrDefaultAsync(ct);
            return stored is not null && string.Equals(stored, key, StringComparison.Ordinal)
                ? Results.Text(stored, "text/plain; charset=utf-8")
                : Results.NotFound();
        });

        RumEndpoint.MapRum(app);
        return app;
    }

    /// <summary>
    /// فید RSS 2.0 محصولات (ایستا برای تست). قیمت به ریال (IRR = تومان × ۱۰) طبق استاندارد ISO 4217.
    /// </summary>
    public static byte[] Feed(IReadOnlyList<TorobProduct> products, string siteName, string baseUrl)
    {
        const string g = "http://base.google.com/ns/1.0";
        var settings = new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = false };
        using var buffer = new MemoryStream();
        using (var writer = XmlWriter.Create(buffer, settings))
        {
            writer.WriteStartDocument();
            writer.WriteStartElement("rss");
            writer.WriteAttributeString("version", "2.0");
            writer.WriteAttributeString("xmlns", "g", null, g);
            writer.WriteStartElement("channel");
            writer.WriteElementString("title", siteName);
            writer.WriteElementString("link", baseUrl);
            writer.WriteElementString("description", "فهرست محصولات " + siteName);
            foreach (var product in products)
            {
                writer.WriteStartElement("item");
                writer.WriteElementString("g", "id", g, product.PageUnique);
                writer.WriteElementString("title", product.Title);
                writer.WriteElementString("link", product.PageUrl);
                if (product.ShortDesc is not null)
                    writer.WriteElementString("description", product.ShortDesc);
                if (product.ImageLinks.Count > 0)
                    writer.WriteElementString("g", "image_link", g, product.ImageLinks[0]);
                foreach (var extra in product.ImageLinks.Skip(1).Take(9))
                    writer.WriteElementString("g", "additional_image_link", g, extra);
                writer.WriteElementString("g", "availability", g, product.Availability ? "in_stock" : "out_of_stock");
                var price = product.OldPrice ?? product.CurrentPrice;
                if (product.Availability && price > 0)
                {
                    writer.WriteElementString("g", "price", g, (price * 10).ToString(CultureInfo.InvariantCulture) + " IRR");
                    if (product.OldPrice is not null)
                        writer.WriteElementString("g", "sale_price", g, (product.CurrentPrice * 10).ToString(CultureInfo.InvariantCulture) + " IRR");
                }

                writer.WriteElementString("g", "condition", g, "new");
                if (product.CategoryName is not null)
                    writer.WriteElementString("g", "product_type", g, product.CategoryName);
                writer.WriteEndElement();
            }

            writer.WriteEndElement();
            writer.WriteEndElement();
            writer.WriteEndDocument();
        }

        return buffer.ToArray();
    }
}
