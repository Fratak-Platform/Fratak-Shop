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

// Cities of a province, used by the address forms (province → city select).
app.MapGet("/api/locations/provinces/{provinceId:guid}/cities", async (Guid provinceId, ILocationService locations, HttpContext http) =>
{
    var cities = await locations.GetCitiesAsync(provinceId);
    http.Response.Headers.CacheControl = "public, max-age=3600";
    return Results.Ok(cities.Select(city => new { id = city.Id, name = city.Name }));
});

// ── Database: migrations, reference data and optional demo catalog ────
await app.Services.InitializeFratakShopDataAsync();

if (app.Services.GetRequiredService<IOptions<ShopOptions>>().Value.SeedDemoData)
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<DemoDataSeeder>().SeedAsync();
}

app.Run();
