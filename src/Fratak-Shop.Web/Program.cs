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
using Fratak_Shop.Web.Infrastructure.Media;
using Fratak_Shop.Web.Infrastructure.PageCache;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.OutputCaching;
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

    // Admin panel: signed-in users only; AdminPageFilter then checks staff membership and permissions.
    options.Conventions.AuthorizeAreaFolder("Admin", "/");
    options.Conventions.AddAreaFolderApplicationModelConvention("Admin", "/",
        model => model.Filters.Add(new Microsoft.AspNetCore.Mvc.ServiceFilterAttribute(typeof(Fratak_Shop.Web.Infrastructure.Admin.AdminPageFilter))));

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

// Cacheable pages render forms without a token; shop.js fetches it before submitting.
builder.Services.AddSingleton<IHtmlGenerator, DeferredAntiforgeryHtmlGenerator>();
// TempData without the "delete cookie" header on every page (it made pages uncacheable).
builder.Services.AddSingleton<Microsoft.AspNetCore.Mvc.ViewFeatures.CookieTempDataProvider>();
builder.Services.AddSingleton<Microsoft.AspNetCore.Mvc.ViewFeatures.ITempDataProvider, CacheFriendlyTempDataProvider>();

// Keys of antiforgery / captcha / gift-card code encryption survive restarts and deployments.
builder.Services.AddDataProtection()
    .SetApplicationName("Fratak-Shop")
    .PersistKeysToFileSystem(new DirectoryInfo(builder.Configuration["Security:DataProtectionKeysPath"]
                                               ?? Path.Combine(builder.Environment.ContentRootPath, "App_Data", "keys")));

// Page cache of public pages for anonymous visitors (see PageCachePolicy).
builder.Services.AddOutputCache(options =>
{
    options.SizeLimit = 256L * 1024 * 1024;
    options.MaximumBodySize = 4L * 1024 * 1024;
    options.AddPolicy(PageCachePolicy.Name, new PageCachePolicy());
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

        // Customer return photos: reachable only by their random name, never indexed or cached by proxies.
        if (path.StartsWithSegments("/uploads/returns"))
        {
            headers["X-Robots-Tag"] = "noindex, nofollow";
            headers[HeaderNames.CacheControl] = "private, max-age=86400";
            return;
        }

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

app.UseMiddleware<Fratak_Shop.Web.Infrastructure.Admin.ActivityTrackingMiddleware>();
app.UseMiddleware<CanonicalUrlMiddleware>();
app.UseRouting();
app.UseRateLimiter();

app.UseMiddleware<TokenRefreshMiddleware>();
app.UseAuthentication();
app.UseAuthorization();

app.UseMiddleware<SeoRedirectMiddleware>();
app.UseReferralCapture();
app.UseOutputCache();

app.MapRazorPages();
// اتصال زندهٔ سایت و پنل (اعلان لحظه‌ای، هشدار ویرایش هم‌زمان) – فایل سمت مرورگر: assets/js/live.js
app.MapHub<Fratak_Shop.Web.Infrastructure.Realtime.LiveHub>(Fratak_Shop.Web.Infrastructure.Realtime.LiveHub.Path);
app.MapAuthApi();
app.MapSeoEndpoints();
app.MapPwaEndpoints();
app.MapStorefrontApi();
Fratak_Shop.Web.Infrastructure.Analytics.AnalyticsEndpoints.MapAnalyticsEndpoints(app);
app.MapMediaEndpoints();

// Uptime monitors: database reachability only (details are in the admin panel).
app.MapGet("/health", async (HttpContext http, Fratak_Shop.Web.Infrastructure.Admin.HealthService health, CancellationToken cancellationToken) =>
{
    var (ok, milliseconds) = await health.PingDatabaseAsync(cancellationToken);
    http.Response.Headers.CacheControl = "no-store";
    http.Response.Headers["X-Robots-Tag"] = "noindex";
    return Results.Json(new { status = ok ? "ok" : "down", db = Math.Round(milliseconds) }, statusCode: ok ? 200 : 503);
});

// Cities of a province, used by the address forms (province → city select).
app.MapGet("/api/locations/provinces/{provinceId:guid}/cities", async (Guid provinceId, ILocationService locations, HttpContext http) =>
{
    var cities = await locations.GetCitiesAsync(provinceId);
    http.Response.Headers.CacheControl = "public, max-age=3600";
    return Results.Ok(cities.Select(city => new { id = city.Id, name = city.Name }));
});

// Instant suggestions of the header search box (products, categories, brands).
app.MapGet("/api/search/suggest", async (string? q, Fratak_Shop.Core.Services.Products.ISearchSuggestService suggestions,
    Fratak_Shop.Core.Services.Searches.ISearchInsightsService insights,
    HttpContext http, CancellationToken cancellationToken) =>
{
    var result = await suggestions.SuggestAsync(q, cancellationToken);
    var nothing = result.Products.Count == 0 && result.Categories.Count == 0 && result.Brands.Count == 0;
    var correction = nothing && result.Query.Length >= 2 ? await insights.DidYouMeanAsync(result.Query) : null;
    http.Response.Headers.CacheControl = "public, max-age=60";
    return Results.Ok(new
    {
        query = result.Query,
        total = result.TotalProducts,
        correction,
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

// Popular searches of the shop (empty box of the header search).
app.MapGet("/api/search/popular", async (Fratak_Shop.Core.Services.Searches.ISearchInsightsService insights, HttpContext http) =>
{
    var items = await insights.GetPopularAsync();
    http.Response.Headers.CacheControl = "public, max-age=300";
    return Results.Ok(new { items });
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

// ── Caches follow data changes (storefront today, admin panel later) ──
Fratak_Shop.Core.Utilities.CacheInvalidation.Wire(app.Services.GetRequiredService<Microsoft.Extensions.Caching.Memory.IMemoryCache>());
PageCachePolicy.WireEviction(app.Services.GetRequiredService<IOutputCacheStore>(), app.Logger);

// ── Database: migrations, reference data and optional demo catalog ────
await app.Services.InitializeFratakShopDataAsync();

// Admin panel: default roles (once), legacy staff moved onto roles, owner from Shop:OwnerPhoneNumber.
using (var adminScope = app.Services.CreateScope())
{
    try
    {
        await adminScope.ServiceProvider.GetRequiredService<Fratak_Shop.Core.Services.Admin.IAdminAccessService>()
            .EnsureDefaultsAsync(app.Services.GetRequiredService<IOptions<ShopOptions>>().Value.OwnerPhoneNumber);
    }
    catch (Exception exception)
    {
        app.Logger.LogError(exception, "Preparing the admin roles failed");
    }
}

// Web push needs a VAPID key pair: generated once and stored in the settings row.
using (var pushScope = app.Services.CreateScope())
{
    try
    {
        await pushScope.ServiceProvider.GetRequiredService<Fratak_Shop.Core.Services.Notifications.IWebPushService>().EnsureVapidKeysAsync();
    }
    catch (Exception exception)
    {
        app.Logger.LogError(exception, "Preparing the web push keys failed; push notifications stay off");
    }
}

if (app.Services.GetRequiredService<IOptions<ShopOptions>>().Value.SeedDemoData)
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<DemoDataSeeder>().SeedAsync();
    await Fratak_Shop.Web.Infrastructure.Seeders.Round5DemoSeeder.SeedAsync(
        scope.ServiceProvider.GetRequiredService<Fratak_Shop.Data.Contexts.FratakShopDbContext>(),
        scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Fratak.DemoData"));
    await Round6DemoSeeder.SeedAsync(
        scope.ServiceProvider.GetRequiredService<Fratak_Shop.Data.Contexts.FratakShopDbContext>(),
        app.Environment,
        scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Fratak.DemoData"));
}

app.Run();
