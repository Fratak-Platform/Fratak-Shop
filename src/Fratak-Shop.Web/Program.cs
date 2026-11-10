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

// فرهنگ (Culture) ثابت برای کل برنامه: روی ویندوزِ فارسی (fa-IR) تاریخ پیش‌فرض شمسی است و جداکنندهٔ اعشار «٫»؛
// آن‌وقت مثلاً شمارهٔ سفارش FR-1405… یا تاریخ JSON-LD «1405-08-05» می‌شد و رفتار سرور به زبان ویندوزش بستگی داشت.
// نمایش فارسی (تاریخ شمسی، رقم فارسی، جداکنندهٔ هزارگان) را کلاس‌های Format/IranClock خودشان صریح انجام می‌دهند.
System.Globalization.CultureInfo.DefaultThreadCurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = System.Globalization.CultureInfo.InvariantCulture;
System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
System.Globalization.CultureInfo.CurrentUICulture = System.Globalization.CultureInfo.InvariantCulture;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(options => options.AddServerHeader = false);
var startupWatch = System.Diagnostics.Stopwatch.StartNew();

// دور نوزدهم (A12): پروفایل منابع میزبان (Shop:Resources:Profile = Low/Normal/High؛ پیش‌فرض Low برای هاست اشتراکی ضعیف).
// بودجهٔ کش صفحه، استخر اتصال/DbContext، سقف درخواست هم‌زمان و دورهٔ آرام شروع از همین می‌آید – Infrastructure/Ops/ResourceGovernor.cs
var resourceOptions = builder.Configuration.GetSection(Fratak_Shop.Web.Infrastructure.Ops.ResourceOptions.Section)
                          .Get<Fratak_Shop.Web.Infrastructure.Ops.ResourceOptions>() ?? new Fratak_Shop.Web.Infrastructure.Ops.ResourceOptions();
var resourceBudget = Fratak_Shop.Web.Infrastructure.Ops.ResourceBudget.From(resourceOptions);
builder.Services.AddSingleton(resourceOptions);
builder.Services.AddSingleton(resourceBudget);

// مرحلهٔ هفدهم: خطای یک کار پس‌زمینه هرگز کل برنامه را متوقف نکند (پیش‌فرض دات‌نت «توقف میزبان» است؛ روی IIS یعنی
// خاموش شدن پروسه، چند بار تکرار ← توقف استخر برنامه و ۵۰۳ تا ری‌استارت دستی). خطا در «گزارش‌های سرور» و «کارهای پس‌زمینه» دیده می‌شود.
builder.Services.Configure<HostOptions>(options =>
{
    options.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.Ignore;
    options.ShutdownTimeout = TimeSpan.FromSeconds(20);
});

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
.AddMvcOptions(options =>
{
    options.Filters.Add<SiteContextPageFilter>();
    // مرحلهٔ هفدهم: شکست توکن ضدجعل فرم‌ها در «درخواست‌های ردشده» دیده شود.
    options.Filters.Add<Fratak_Shop.Web.Infrastructure.Ops.AntiforgeryMonitorFilter>();
});

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
    // دور نوزدهم (A5): سقف کش به اندازهٔ پروفایل (Low = ۴۸ مگ) تا کش خودش حافظهٔ استخر را پر نکند.
    options.SizeLimit = resourceBudget.OutputCacheMb * 1024L * 1024;
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

// مرحلهٔ هفدهم: سرآیندهای پراکسی همیشه پردازش می‌شوند ولی فقط از فرستندهٔ مورد اعتماد: خود سرور (loopback، پیش‌فرض –
// مثل Nginx/IIS روی همان ماشین)، Security:KnownProxies (IP تکی) و Security:KnownNetworks (بازه، مثلاً شبکهٔ CDN ابرآروان).
// پس جعل IP از بیرون ممکن نیست، و اگر سایت پشت پراکسی محلی باشد دیگر همهٔ کاربران «یک IP» دیده نمی‌شوند (۴۲۹/۴۰۳ جمعی).
// Security:BehindProxy = true فقط X-Forwarded-Host را هم می‌پذیرد (برای پراکسی‌ای که دامنه را عوض می‌کند).
var behindProxy = builder.Configuration.GetValue<bool>("Security:BehindProxy");
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
                               | (behindProxy ? ForwardedHeaders.XForwardedHost : ForwardedHeaders.None);
    options.ForwardLimit = 1;
    // Only proxies listed here are trusted (loopback is trusted by default);
    // trusting everyone would let clients spoof their IP and bypass rate limits.
    foreach (var proxy in builder.Configuration.GetSection("Security:KnownProxies").Get<string[]>() ?? [])
    {
        if (IPAddress.TryParse(proxy?.Trim(), out var address))
            options.KnownProxies.Add(address);
    }
    foreach (var network in builder.Configuration.GetSection("Security:KnownNetworks").Get<string[]>() ?? [])
    {
        if (System.Net.IPNetwork.TryParse(network?.Trim(), out var range))
            options.KnownIPNetworks.Add(range);
    }
});

builder.Services.AddHsts(options =>
{
    options.MaxAge = TimeSpan.FromDays(365);
    options.IncludeSubDomains = true;
});

// ── Application layers ────────────────────────────────────────────────
// رشتهٔ اتصال روی سرور: متغیر محیطی ConnectionDefault (یا ConnectionStrings__DefaultConnection) — راهنما: docs/DEPLOYMENT.md.
var connectionString = new[] { builder.Configuration["ConnectionDefault"], builder.Configuration.GetConnectionString("DefaultConnection") }
                           .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
                       ?? throw new InvalidOperationException(
                           "Connection string is not configured. Set the environment variable 'ConnectionDefault' (or ConnectionStrings__DefaultConnection); see docs/DEPLOYMENT.md.");

// دور نوزدهم (A16): سقف استخر اتصال SQL و استخر DbContext به اندازهٔ پروفایل منابع.
builder.Services.AddFratakShopData(
    FratakShopBootstrapper.WithPoolLimit(connectionString, resourceBudget.SqlMaxPoolSize),
    resourceOptions.DbContextPooling ? resourceBudget.DbContextPoolSize : 0);
// دور نوزدهم (A20): مهلت کوتاه‌تر و قطع‌کن مدار برای همهٔ سرویس‌های بیرونی (پیش از ثبت کلاینت‌ها).
Fratak_Shop.Web.Infrastructure.Http.CircuitBreakerSetup.AddShopHttpResilience(builder.Services);
builder.Services.AddFratakShopCore();
builder.Services.AddFratakShopWeb(builder.Configuration);

var app = builder.Build();

// دور نوزدهم (A8–A10): مدیر منابع سراسری (نوبت کارهای پس‌زمینه، دورهٔ آرام شروع، صف درخواست‌ها).
Fratak_Shop.Web.Infrastructure.Ops.ResourceGovernor.Configure(resourceOptions, app.Lifetime.ApplicationStopping);
// دور نوزدهم (A4): خواندن سقف واقعی حافظه؛ سقف هیپ GC (۵۵٪) را ResourceMonitorService بعد از شروع اعمال می‌کند (MemoryLimits).
Fratak_Shop.Web.Infrastructure.Ops.MemoryLimits.Detect();

// ── Lifecycle & crash diagnostics (مرحلهٔ هفدهم) ──────────────────────
// تاریخچهٔ روشن/خاموش شدن و خطای مرگبار در App_Data/logs/lifecycle.json (صفحهٔ سلامت: «روشن/خاموش شدن برنامه»).
Fratak_Shop.Web.Infrastructure.Ops.AppLifecycle.Start(app.Environment.ContentRootPath,
    typeof(Program).Assembly.GetName().Version?.ToString() ?? "-");
app.Lifetime.ApplicationStopping.Register(Fratak_Shop.Web.Infrastructure.Ops.AppLifecycle.Stopping);
AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
{
    var exception = eventArgs.ExceptionObject as Exception;
    Fratak_Shop.Web.Infrastructure.Ops.AppLifecycle.Crash(exception);
    app.Logger.LogCritical(exception, "Unhandled exception: the process is terminating");
};
TaskScheduler.UnobservedTaskException += (_, eventArgs) =>
{
    app.Logger.LogError(eventArgs.Exception, "Unobserved task exception");
    eventArgs.SetObserved();
};

// IPهای مورد اعتماد (Security:TrustedIps): هرگز با دیوار آتش یا محدودیت تعداد درخواست رد نمی‌شوند (راه برگشت مالک بدون پنل).
var trustedIps = new List<Fratak_Shop.Core.Services.Ops.IpRange>();
foreach (var value in app.Configuration.GetSection("Security:TrustedIps").Get<string[]>() ?? [])
{
    if (Fratak_Shop.Core.Services.Ops.IpRange.TryParse(value, out var range))
        trustedIps.Add(range);
    else
        app.Logger.LogWarning("Ignoring invalid Security:TrustedIps entry {Value}", value);
}
Fratak_Shop.Core.Services.Ops.FirewallState.SetTrusted(trustedIps);

// ── HTTP pipeline ─────────────────────────────────────────────────────
app.UseForwardedHeaders();
// X-Forwarded-For که هنوز مانده = از فرستندهٔ نامطمئن (CDN/پراکسی تنظیم‌نشده) ← هشدار «پراکسی / CDN» در صفحهٔ سلامت.
app.Use((context, next) =>
{
    Fratak_Shop.Web.Infrastructure.Ops.RequestMonitor.InspectForwarding(context);
    return next(context);
});
// مرحلهٔ هفدهم: تا آماده شدن پایگاه داده، صفحهٔ کوتاه «در حال آماده‌سازی» (۵۰۳) به‌جای از کار افتادن برنامه.
app.UseMiddleware<Fratak_Shop.Web.Infrastructure.Ops.StartupGateMiddleware>();

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
// فاز ۹: دیوار آتش IP/ربات (پیش از هر کار دیگری تا درخواست مسدود هزینه‌ای نداشته باشد) – /admin/system/firewall
app.UseMiddleware<Fratak_Shop.Web.Infrastructure.Ops.FirewallMiddleware>();
// مرحلهٔ سیزدهم/هفدهم: یک دامنهٔ اصلی (Shop:CanonicalHost = shop.fratak.ir)؛ فقط www همان دامنه و Shop:CanonicalAliases با ۳۰۱ به آن می‌روند.
// بعد از هدرهای امنیتی (پاسخ تغییر مسیر هم HSTS/CSP دارد) و دیوار آتش (ربات مخرب همان ۴۰۳ را می‌گیرد، نه تغییر مسیر).
app.UseMiddleware<CanonicalHostMiddleware>();
app.UseStatusCodePagesWithReExecute("/404", "?code={0}");
app.UseResponseCompression();
// دور سئوی خودکار: آدرس قدیمی JPG/PNG تصویری که WebP شده ← ۳۰۱ به نسخهٔ WebP (پیش از فایل‌های ایستا).
app.UseMiddleware<Fratak_Shop.Web.Infrastructure.Media.LegacyImageRedirectMiddleware>();

// دور نوزدهم (A17): فایل‌های پیش‌فشرده (.br/.gz کنار فایل اصلی، ساخته‌شده در انتشار) بدون فشرده‌سازی دوباره در هر درخواست.
app.UseMiddleware<Fratak_Shop.Web.Infrastructure.Ops.PrecompressedStaticFilesMiddleware>();
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

// دور نوزدهم (A18/A19): صف ورودی (سقف درخواست هم‌زمان مهمان + نسخهٔ ذخیره‌شده هنگام شلوغی) و آهسته کردن ربات‌های پرمصرف.
app.UseMiddleware<Fratak_Shop.Web.Infrastructure.Ops.LoadSheddingMiddleware>();
// دور نوزدهم (A14): شمارش پرس‌وجوهای هر درخواست (آمار «سنگین‌ترین صفحه‌ها» و سرآیند آزمون‌ها).
app.UseMiddleware<Fratak_Shop.Web.Infrastructure.Ops.QueryMetricsMiddleware>();

// دور سئوی خودکار: ثبت بازدید ربات‌های موتور جست‌وجو (فقط صفحه‌ها؛ برای مرورگر معمولی بی‌هزینه).
app.UseMiddleware<Fratak_Shop.Web.Infrastructure.Seo.BotLogMiddleware>();
app.UseMiddleware<Fratak_Shop.Web.Infrastructure.Admin.ActivityTrackingMiddleware>();
app.UseMiddleware<CanonicalUrlMiddleware>();
app.UseRouting();
app.UseRateLimiter();

app.UseMiddleware<TokenRefreshMiddleware>();
app.UseAuthentication();
app.UseAuthorization();

// فاز ۹: حالت تعمیرات (بعد از شناسایی کاربر تا کارکنان سایت را ببینند، و پیش از کش صفحات).
app.UseMiddleware<Fratak_Shop.Web.Infrastructure.Ops.MaintenanceMiddleware>();
app.UseMiddleware<SeoRedirectMiddleware>();
app.UseReferralCapture();
// دور نوزدهم (A13): «آخرین نسخهٔ سالم» صفحه‌های عمومی مهمان روی دیسک؛ هنگام خطای ساخت صفحه به‌جای صفحهٔ خطا نشان داده می‌شود.
app.UseMiddleware<Fratak_Shop.Web.Infrastructure.Ops.PageSnapshotMiddleware>();
app.UseOutputCache();

app.MapRazorPages();
// اتصال زندهٔ سایت و پنل (اعلان لحظه‌ای، هشدار ویرایش هم‌زمان) – فایل سمت مرورگر: assets/js/live.js
app.MapHub<Fratak_Shop.Web.Infrastructure.Realtime.LiveHub>(Fratak_Shop.Web.Infrastructure.Realtime.LiveHub.Path);
app.MapAuthApi();
app.MapSeoEndpoints();
Fratak_Shop.Web.Infrastructure.Seo.SeoFeedEndpoints.MapSeoAutopilotEndpoints(app);
app.MapPwaEndpoints();
app.MapStorefrontApi();
Fratak_Shop.Web.Infrastructure.Marketing.BannerEndpoints.MapBannerEndpoints(app);
Fratak_Shop.Web.Infrastructure.Assistant.AssistantEndpoints.MapAssistantEndpoints(app);
Fratak_Shop.Web.Infrastructure.Analytics.AnalyticsEndpoints.MapAnalyticsEndpoints(app);
app.MapMediaEndpoints();
Fratak_Shop.Web.Infrastructure.Seo.OgImageRenderer.Map(app);

// Uptime monitors: database reachability only (details are in the admin panel).
app.MapGet("/health", async (HttpContext http, Fratak_Shop.Web.Infrastructure.Admin.HealthService health, CancellationToken cancellationToken) =>
{
    var (ok, milliseconds) = await health.PingDatabaseAsync(cancellationToken);
    http.Response.Headers.CacheControl = "no-store";
    http.Response.Headers["X-Robots-Tag"] = "noindex";
    return Results.Json(new { status = ok ? "ok" : "down", db = Math.Round(milliseconds) }, statusCode: ok ? 200 : 503);
});

// فاز ۹: /healthz با توکن اختیاری (جزئیات برای سرویس‌های مانیتورینگ).
Fratak_Shop.Web.Infrastructure.Ops.HealthEndpoint.MapHealthz(app);

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
        categoryFirst = result.CategoryFirst,
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

// ── Database initialization behind the startup gate (مرحلهٔ هفدهم) ─────────
// همهٔ کارهای آغازین پایگاه داده در InitializeDatabaseAsync است. اگر SQL Server (سرور جدا) لحظه‌ای در دسترس نباشد،
// چند بار تا ~۴۰ ثانیه دوباره امتحان می‌شود؛ اگر باز هم نشد برنامه از کار نمی‌افتد (قبلاً ← Rapid-Fail IIS ← ۵۰۳ دائمی)،
// بلکه صفحهٔ «در حال آماده‌سازی» نشان می‌دهد و در پس‌زمینه تلاش می‌کند تا خودکار باز شود (Infrastructure/Ops/StartupGate.cs).
// همهٔ مرحله‌ها تکرارپذیرند (مهاجرت‌ها و دادهٔ پیش‌فرض فقط کار انجام‌نشده را انجام می‌دهند).
var shopOptions = app.Services.GetRequiredService<IOptions<ShopOptions>>().Value;
if (await Fratak_Shop.Web.Infrastructure.Ops.StartupGate.TryRunAsync(InitializeDatabaseAsync, app.Logger, TimeSpan.FromSeconds(40), app.Lifetime.ApplicationStopping))
    Fratak_Shop.Web.Infrastructure.Ops.AppLifecycle.MarkReady(startupWatch.Elapsed); // دور نوزدهم (A3): مدت آماده شدن در «منابع سرور»
else
    Fratak_Shop.Web.Infrastructure.Ops.StartupGate.RetryInBackground(InitializeDatabaseAsync, app.Logger, app.Lifetime.ApplicationStopping);

app.Run();

async Task InitializeDatabaseAsync()
{
    // ── Database: migrations, location dictionary, default data (only on a brand-new database) ────
    // Shop:SeedDefaultData = true → دادهٔ پیش‌فرض سیستم فقط بار اولی که پایگاه داده خالی است ساخته می‌شود؛
    // اجراهای بعدی هرگز چیزی را دوباره نمی‌سازند (Data/Seeders/DefaultDataSeeder.cs).
    // دور نوزدهم (A23): فهرست شهرها و راهنمای پنل فقط وقتی نسخهٔ برنامه یا پایگاه داده عوض شده باشد دوباره بررسی می‌شوند
    // (فایل نشانهٔ App_Data/startup-sync.txt)؛ ری‌استارت‌های معمولی سبک‌ترند.
    var defaultData = await app.Services.InitializeFratakShopDataAsync(shopOptions.SeedDefaultData,
        Path.Combine(app.Environment.ContentRootPath, "App_Data", "startup-sync.txt"));
    switch (defaultData)
    {
        case Fratak_Shop.Data.Seeders.DefaultDataOutcome.Seeded:
            app.Logger.LogInformation("New database: the default store data was created (Shop:SeedDefaultData).");
            break;
        case Fratak_Shop.Data.Seeders.DefaultDataOutcome.Disabled:
            app.Logger.LogWarning("New database without default data (Shop:SeedDefaultData = false): settings, pages and menus are empty until they are created in the admin panel.");
            break;
    }

    // Admin panel: default roles (once), legacy staff moved onto roles, default admins from Shop:DefaultAdmins
    // (مرحلهٔ چهاردهم: هر تعداد مدیر، هر کدام فقط یک بار و با رمز اولیه ساخته می‌شوند؛ رمز هرگز لاگ نمی‌شود).
    using (var adminScope = app.Services.CreateScope())
    {
        try
        {
            var adminNotes = await adminScope.ServiceProvider.GetRequiredService<Fratak_Shop.Core.Services.Admin.IAdminAccessService>()
                .EnsureDefaultsAsync(shopOptions.DefaultAdminAccounts());
            // مرحلهٔ هفدهم: یادداشت‌ها در صفحهٔ سلامت هم دیده می‌شوند و مشکل‌ها (Key تکراری، شمارهٔ نامعتبر، …) هشدار لاگ می‌شوند.
            Fratak_Shop.Web.Infrastructure.Admin.DefaultAdminStartupReport.Set(adminNotes);
            foreach (var note in adminNotes)
            {
                if (Fratak_Shop.Web.Infrastructure.Admin.DefaultAdminStartupReport.IsProblem(note))
                    app.Logger.LogWarning("Default admin: {Note}", note);
                else
                    app.Logger.LogInformation("Default admin: {Note}", note);
            }
        }
        catch (Exception exception)
        {
            app.Logger.LogError(exception, "Preparing the admin roles failed");
        }
    }

    // روی سرور واقعی بدون کلید SMS.ir (نه در پنل، نه SmsIr__ApiKey) کد ورود، فراموشی رمز و تأیید دومرحله‌ای پیامکی کار نمی‌کند؛
    // مدیران پیش‌فرض با رمز وارد می‌شوند ولی هشدار بلند لازم است.
    if (app.Environment.IsProduction())
    {
        using var smsScope = app.Services.CreateScope();
        try
        {
            var sms = await smsScope.ServiceProvider.GetRequiredService<Fratak_Shop.Core.Services.Notifications.ISmsProviderService>().GetRuntimeAsync();
            if (string.IsNullOrWhiteSpace(sms.ApiKey) && string.IsNullOrWhiteSpace(app.Configuration[Fratak_Shop.Web.Infrastructure.Sms.ConfigurableSmsSender.ConfigurationKey]))
                app.Logger.LogCritical("SMS.ir API key is not configured (admin panel or SmsIr__ApiKey): sign-in codes, password reset and SMS two-step login cannot work; only password sign-in is possible. Set SmsIr__ApiKey on the server.");
        }
        catch (Exception exception)
        {
            app.Logger.LogWarning(exception, "Checking the SMS configuration failed");
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

    // فاز ۹: قانون‌های پیش‌فرض ربات‌های مخرب و هشدارها، بارگذاری فایروال و کارهای متوقف پیش از اولین درخواست.
    using (var opsScope = app.Services.CreateScope())
    {
        try
        {
            var firewall = opsScope.ServiceProvider.GetRequiredService<Fratak_Shop.Core.Services.Ops.IFirewallService>();
            await firewall.EnsureDefaultsAsync();
            await firewall.ReloadAsync();
            await opsScope.ServiceProvider.GetRequiredService<Fratak_Shop.Core.Services.Ops.IAlertService>().EnsureDefaultsAsync();
            Fratak_Shop.Web.Infrastructure.Admin.JobMonitor.SetPaused(
                await opsScope.ServiceProvider.GetRequiredService<Fratak_Shop.Core.Services.Ops.IBackgroundJobControl>().GetPausedAsync());
        }
        catch (Exception exception)
        {
            app.Logger.LogError(exception, "Preparing the firewall / alerts failed");
        }
    }

    // ── Demo data (Shop:SeedDemoData): fake catalog, demo accounts and sample coupons ────
    // فقط روی فروشگاهی که هنوز هیچ محصولی (حتی حذف‌شده) ندارد؛ پس روشن ماندن اشتباهی این گزینه روی سایت واقعی،
    // محصول یا حساب ساختگی (با رمز 123456) را قاطی دادهٔ واقعی نمی‌کند.
    if (shopOptions.SeedDemoData)
    {
        using var scope = app.Services.CreateScope();
        var demoContext = scope.ServiceProvider.GetRequiredService<Fratak_Shop.Data.Contexts.FratakShopDbContext>();
        var demoLogger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Fratak.DemoData");
        if (await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.AnyAsync(
                Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.IgnoreQueryFilters(demoContext.Products)))
        {
            demoLogger.LogInformation("Demo data skipped: the catalog already has products (demo data is only added to an empty shop).");
        }
        else
        {
            // نمونه‌های تبلیغاتی پیش از کاتالوگ (کدهای تخفیف نمونه فقط روی جدول خالی ساخته می‌شوند).
            await Fratak_Shop.Data.Seeders.SampleContentSeeder.SeedAsync(demoContext);
            await scope.ServiceProvider.GetRequiredService<DemoDataSeeder>().SeedAsync();
            await Fratak_Shop.Web.Infrastructure.Seeders.Round5DemoSeeder.SeedAsync(demoContext, demoLogger);
            await Round6DemoSeeder.SeedAsync(demoContext, app.Environment, demoLogger);
            if (!app.Environment.IsDevelopment())
                demoLogger.LogWarning("Demo data was created outside Development: demo customer/staff accounts use the password 123456. Never enable Shop:SeedDemoData on the live site.");
        }
    }
}

/// <summary>نقطهٔ ورود برنامه؛ partial و public تا آزمون‌های سرتاسری بتوانند آن را با WebApplicationFactory بالا بیاورند.</summary>
public partial class Program;
