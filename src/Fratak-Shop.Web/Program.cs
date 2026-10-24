using System.IO.Compression;
using System.Net;
using System.Text.Encodings.Web;
using System.Text.Unicode;
using Fratak_Shop.Core;
using Fratak_Shop.Core.Services.Locations;
using Fratak_Shop.Data.Extensions;
using Fratak_Shop.Web.Extensions;
using Fratak_Shop.Web.Infrastructure;
using Fratak_Shop.Web.Infrastructure.Auth;
using Fratak_Shop.Web.Infrastructure.Middleware;
using Fratak_Shop.Web.Infrastructure.Seeders;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.WebEncoders;
using Microsoft.Net.Http.Headers;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(options => options.AddServerHeader = false);

// ── Presentation ──────────────────────────────────────────────────────
builder.Services.AddRazorPages(options =>
{
    // Customer area: every profile page requires a signed-in customer.
    options.Conventions.AuthorizeFolder("/Profile");
    options.Conventions.AuthorizePage("/Checkout");
    options.Conventions.AuthorizePage("/CheckoutComplete");
    options.Conventions.AuthorizePage("/CheckoutFailed");
    options.Conventions.AuthorizeFolder("/Payment");
    options.Conventions.AllowAnonymousToPage("/Payment/Callback");
    options.Conventions.AuthorizePage("/Verify");
    options.Conventions.AuthorizePage("/Welcome");

    // Friendly aliases for the product listing.
    options.Conventions.AddPageRoute("/Search", "products");
    options.Conventions.AddPageRoute("/Search", "category/{category}");
    options.Conventions.AddPageRoute("/Search", "brand/{brand}");
    options.Conventions.AddPageRoute("/Blog", "blog/category/{category}");
    options.Conventions.AddPageRoute("/Error404", "404");
})
.AddMvcOptions(options => options.Filters.Add<SiteContextPageFilter>());

builder.Services.AddAntiforgery(options =>
{
    options.Cookie.Name = "fratak_af";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = Microsoft.AspNetCore.Http.SameSiteMode.Lax;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
});

// Render Persian text as-is instead of HTML entities (smaller pages, readable source).
builder.Services.Configure<WebEncoderOptions>(options =>
    options.TextEncoderSettings = new TextEncoderSettings(UnicodeRanges.All));

builder.Services.Configure<RouteOptions>(options =>
{
    options.LowercaseUrls = true;
    options.LowercaseQueryStrings = false;
});

// ── Security & performance ────────────────────────────────────────────
builder.Services.AddShopAuthentication(builder.Configuration);
builder.Services.AddShopRateLimiting();

// Static assets and XML/text only: HTML is not compressed here because it carries
// per-user secrets (BREACH); let the reverse proxy decide for HTML.
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.Providers.Add<BrotliCompressionProvider>();
    options.Providers.Add<GzipCompressionProvider>();
    options.MimeTypes = ["text/css", "application/javascript", "text/javascript", "image/svg+xml", "application/xml", "text/xml", "text/plain", "font/ttf", "application/font-woff"];
});
builder.Services.Configure<BrotliCompressionProviderOptions>(o => o.Level = CompressionLevel.Fastest);
builder.Services.Configure<GzipCompressionProviderOptions>(o => o.Level = CompressionLevel.Fastest);

var behindProxy = builder.Configuration.GetValue<bool>("Security:BehindProxy");
if (behindProxy)
{
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
        options.ForwardLimit = 1;
        // Only proxies listed here are trusted (loopback is trusted by default);
        // trusting everyone would let clients spoof their IP and bypass rate limits.
        foreach (var proxy in builder.Configuration.GetSection("Security:KnownProxies").Get<string[]>() ?? [])
        {
            if (IPAddress.TryParse(proxy, out var address))
                options.KnownProxies.Add(address);
        }
    });
}

builder.Services.AddHsts(options =>
{
    options.MaxAge = TimeSpan.FromDays(365);
    options.IncludeSubDomains = true;
});

// ── Application layers ────────────────────────────────────────────────
var connectionString = builder.Configuration["ConnectionDefault"]
                       ?? builder.Configuration.GetConnectionString("DefaultConnection")
                       ?? throw new InvalidOperationException("Connection string 'ConnectionDefault' is not configured.");

builder.Services.AddFratakShopData(connectionString);
builder.Services.AddFratakShopCore();
builder.Services.AddFratakShopWeb(builder.Configuration);

var app = builder.Build();

// ── HTTP pipeline ─────────────────────────────────────────────────────
if (behindProxy)
    app.UseForwardedHeaders();

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}
else
{
    app.UseExceptionHandler("/404");
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseStatusCodePagesWithReExecute("/404", "?code={0}");
app.UseResponseCompression();

app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = context =>
    {
        // Versioned URLs (asp-append-version → ?v=hash) never change: cache for a year.
        var headers = context.Context.Response.Headers;
        var path = context.Context.Request.Path;

        // The service worker and the offline page must always be revalidated (updates).
        if (path.Equals("/sw.js") || path.Equals("/offline.html"))
        {
            headers[HeaderNames.CacheControl] = "no-cache";
            return;
        }

        headers[HeaderNames.CacheControl] = context.Context.Request.Query.ContainsKey("v")
            ? "public, max-age=31536000, immutable"
            : "public, max-age=604800";
    },
});

app.UseMiddleware<CanonicalUrlMiddleware>();
app.UseRouting();
app.UseRateLimiter();

app.UseMiddleware<TokenRefreshMiddleware>();
app.UseAuthentication();
app.UseAuthorization();

app.UseMiddleware<SeoRedirectMiddleware>();

app.MapRazorPages();
app.MapAuthApi();
app.MapSeoEndpoints();
app.MapPwaEndpoints();

// Cities of a province, used by the address forms (province → city select).
app.MapGet("/api/locations/provinces/{provinceId:guid}/cities", async (Guid provinceId, ILocationService locations, HttpContext http) =>
{
    var cities = await locations.GetCitiesAsync(provinceId);
    http.Response.Headers.CacheControl = "public, max-age=3600";
    return Results.Ok(cities.Select(city => new { id = city.Id, name = city.Name }));
});

// Instant suggestions of the header search box (products, categories, brands).
app.MapGet("/api/search/suggest", async (string? q, Fratak_Shop.Core.Services.Products.ISearchSuggestService suggestions,
    HttpContext http, CancellationToken cancellationToken) =>
{
    var result = await suggestions.SuggestAsync(q, cancellationToken);
    http.Response.Headers.CacheControl = "public, max-age=60";
    return Results.Ok(new
    {
        query = result.Query,
        total = result.TotalProducts,
        searchUrl = "/search?q=" + Uri.EscapeDataString(result.Query),
        products = result.Products.Select(p => new
        {
            name = p.Name,
            url = "/product/" + p.Slug,
            image = Fratak_Shop.Web.Helpers.ImageHelper.Product(p.ImageName),
            price = Fratak_Shop.Web.Helpers.Format.Toman(p.FinalPrice),
            oldPrice = p.FinalPrice < p.Price ? Fratak_Shop.Web.Helpers.Format.Price(p.Price) : null,
            available = p.IsAvailable,
        }),
        categories = result.Categories.Select(c => new { name = c.Name, url = "/category/" + c.Slug }),
        brands = result.Brands.Select(b => new { name = b.Name, url = "/brand/" + b.Slug }),
    });
});

// Development only, from the local machine only: the management reports / abandoned-cart
// figures as JSON, to check the services until the admin panel shows them. Never mapped in production.
if (app.Environment.IsDevelopment())
{
    app.MapGet("/_dev/reports", async (HttpContext http, int? days,
        Fratak_Shop.Core.Services.Reports.IReportService reports,
        Fratak_Shop.Core.Services.Shopping.IAbandonedCartService abandonedCarts,
        CancellationToken cancellationToken) =>
    {
        var remote = http.Connection.RemoteIpAddress;
        if (remote is null || !IPAddress.IsLoopback(remote))
            return Results.NotFound();

        var period = Fratak_Shop.Core.DTOs.Reports.ReportPeriod.LastDays(days ?? 30);
        http.Response.Headers.CacheControl = "no-store";
        return Results.Ok(new
        {
            discounts = await reports.GetDiscountReportAsync(period, cancellationToken),
            loyalty = await reports.GetLoyaltyReportAsync(period, cancellationToken),
            profit = await reports.GetProfitReportAsync(period, 10, cancellationToken),
            abandonedCarts = await abandonedCarts.GetStatsAsync(period.From, period.To),
        });
    });
}

// ── Database: migrations, reference data and optional demo catalog ────
await app.Services.InitializeFratakShopDataAsync();

if (app.Services.GetRequiredService<IOptions<ShopOptions>>().Value.SeedDemoData)
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<DemoDataSeeder>().SeedAsync();
    await Fratak_Shop.Web.Infrastructure.Seeders.Round5DemoSeeder.SeedAsync(
        scope.ServiceProvider.GetRequiredService<Fratak_Shop.Data.Contexts.FratakShopDbContext>(),
        scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Fratak.DemoData"));
}

app.Run();
