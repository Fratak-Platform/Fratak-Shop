using System.Text;
using Fratak_Shop.Core;
using Fratak_Shop.Core.DTOs.Auth.Command;
using Fratak_Shop.Core.Services.Auth;
using Fratak_Shop.Core.Services.Content;
using Fratak_Shop.Core.Services.Media;
using Fratak_Shop.Core.Services.Navigation;
using Fratak_Shop.Core.Services.Pages;
using Fratak_Shop.Core.Services.Seo;
using Fratak_Shop.Core.Services.Settings;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Entities.Users;
using Fratak_Shop.Data.Extensions;
using Fratak_Shop.Data.Seeders;
using Fratak_Shop.Web.Infrastructure;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Net.Http.Headers;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------- services
builder.Services.AddFratakShopData(builder.Configuration["ConnectionDefault"]!);
builder.Services.AddFratakShopCore();
builder.Services.AddHttpClient<Fratak_Shop.Core.Services.Payments.ZarinPalGateway>();

builder.Services.Configure<MediaOptions>(options =>
{
    options.RootPath = builder.Environment.WebRootPath;
    options.PublicBasePath = "/uploads";
});

builder.Services.AddRazorPages(options =>
{
    options.Conventions.AuthorizeAreaFolder("Admin", "/", PolicyNames.StaffOnly);

    options.Conventions.AuthorizeFolder("/Profile", PolicyNames.CustomerOnly);

    options.Conventions.AuthorizePage("/Checkout", PolicyNames.CustomerOnly);

    // Stable public aliases for dynamic content pages.
    options.Conventions.AddPageRoute("/Page", "/faq");
    options.Conventions.AddPageRoute("/Page", "/faqs");
    options.Conventions.AddPageRoute("/Page", "/about");
    options.Conventions.AddPageRoute("/Page", "/contact");
    options.Conventions.AddPageRoute("/Page", "/rules");

    // The admin panel lives in its own Area: Areas/Admin/Pages/** is served under
    // /admin/** automatically, and pages that need a different path declare it with
    // an absolute route in their own @page directive (e.g. @page "/admin/coupons").
});

const string SmartAuthentication = "SmartAuthentication";
const string JwtAuthentication = JwtBearerDefaults.AuthenticationScheme;

builder.Services.AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = SmartAuthentication;
        options.DefaultChallengeScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    })
    .AddPolicyScheme(SmartAuthentication, "Cookie or JWT", options =>
    {
        options.ForwardDefaultSelector = context =>
            context.Request.Headers.ContainsKey("Authorization")
                ? JwtAuthentication
                : CookieAuthenticationDefaults.AuthenticationScheme;
    })
    .AddCookie(options =>
    {
        options.LoginPath = "/auth/login";
        options.LogoutPath = "/auth/logout";
        options.AccessDeniedPath = "/status/403";
        options.ExpireTimeSpan = TimeSpan.FromDays(14);
        options.SlidingExpiration = true;
        options.Cookie.Name = "fratak_auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = Microsoft.AspNetCore.Http.SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    })
    .AddJwtBearer(options =>
    {
        var secret = builder.Configuration["Authentication:Jwt:Secret"]
            ?? throw new InvalidOperationException("Authentication:Jwt:Secret is required.");
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)),
            ValidateIssuer = true,
            ValidIssuer = builder.Configuration["Authentication:Jwt:Issuer"] ?? "Fratak-Shop",
            ValidateAudience = true,
            ValidAudience = builder.Configuration["Authentication:Jwt:Audience"] ?? "Fratak-Shop.Client",
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            RoleClaimType = System.Security.Claims.ClaimTypes.Role,
            NameClaimType = System.Security.Claims.ClaimTypes.Name,
        };
    });

builder.Services.AddSingleton<JwtTokenService>();

builder.Services.AddAuthorization(options =>
{
    var staff = new[]
    {
        UserRole.SuperAdmin, UserRole.Admin, UserRole.ProductManager, UserRole.OrderManager,
        UserRole.ContentManager, UserRole.SeoManager, UserRole.SupportAgent,
    };

    options.AddPolicy(PolicyNames.StaffOnly, policy => policy
        .RequireAuthenticatedUser()
        .RequireClaim(System.Security.Claims.ClaimTypes.Role, staff.Select(role => role.ToString())));

    options.AddPolicy(PolicyNames.AdminOnly, policy => policy
        .RequireAuthenticatedUser()
        .RequireClaim(System.Security.Claims.ClaimTypes.Role, nameof(UserRole.SuperAdmin), nameof(UserRole.Admin)));

    options.AddPolicy(PolicyNames.SuperAdminOnly, policy => policy
        .RequireAuthenticatedUser()
        .RequireClaim(System.Security.Claims.ClaimTypes.Role, nameof(UserRole.SuperAdmin)));

    options.AddPolicy(PolicyNames.Products, policy => policy
        .RequireAuthenticatedUser()
        .RequireClaim(System.Security.Claims.ClaimTypes.Role,
            nameof(UserRole.SuperAdmin), nameof(UserRole.Admin), nameof(UserRole.ProductManager), nameof(UserRole.SeoManager)));

    options.AddPolicy(PolicyNames.Orders, policy => policy
        .RequireAuthenticatedUser()
        .RequireClaim(System.Security.Claims.ClaimTypes.Role,
            nameof(UserRole.SuperAdmin), nameof(UserRole.Admin), nameof(UserRole.OrderManager), nameof(UserRole.SupportAgent)));

    options.AddPolicy(PolicyNames.Content, policy => policy
        .RequireAuthenticatedUser()
        .RequireClaim(System.Security.Claims.ClaimTypes.Role,
            nameof(UserRole.SuperAdmin), nameof(UserRole.Admin), nameof(UserRole.ContentManager)));

    options.AddPolicy(PolicyNames.Seo, policy => policy
        .RequireAuthenticatedUser()
        .RequireClaim(System.Security.Claims.ClaimTypes.Role,
            nameof(UserRole.SuperAdmin), nameof(UserRole.Admin), nameof(UserRole.SeoManager)));

    options.AddPolicy(PolicyNames.Support, policy => policy
        .RequireAuthenticatedUser()
        .RequireClaim(System.Security.Claims.ClaimTypes.Role,
            nameof(UserRole.SuperAdmin), nameof(UserRole.Admin), nameof(UserRole.SupportAgent), nameof(UserRole.OrderManager)));

    options.AddPolicy(PolicyNames.Settings, policy => policy
        .RequireAuthenticatedUser()
        .RequireClaim(System.Security.Claims.ClaimTypes.Role, nameof(UserRole.SuperAdmin), nameof(UserRole.Admin)));

    options.AddPolicy(PolicyNames.CustomerOnly, policy => policy.RequireAuthenticatedUser());
});

builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromDays(7);
    options.Cookie.Name = "fratak_session";
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

builder.Services.AddMemoryCache();
builder.Services.AddHttpContextAccessor();
builder.Services.AddAntiforgery(options => options.HeaderName = "RequestVerificationToken");

var app = builder.Build();

// -------------------------------------------------------------------- middleware
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/error");
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/status/{0}");

app.UseHttpsRedirection();

app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = context =>
    {
        // Uploaded images and template assets may be cached aggressively.
        if (context.File.Name.EndsWith(".webp", StringComparison.OrdinalIgnoreCase) ||
            context.File.Name.EndsWith(".woff2", StringComparison.OrdinalIgnoreCase) ||
            context.File.Name.EndsWith(".css", StringComparison.OrdinalIgnoreCase) ||
            context.File.Name.EndsWith(".js", StringComparison.OrdinalIgnoreCase))
        {
            context.Context.Response.Headers[HeaderNames.CacheControl] = "public,max-age=604800";
        }
    },
});

app.UseRouting();
app.UseSession();
app.UseAuthentication();
app.UseAuthorization();

// Smart SEO of the shop: old URLs are redirected to their new home and every 404
// is recorded so the SEO centre can turn it into a redirect later.
app.UseMiddleware<SeoRoutingMiddleware>();

app.MapStaticAssets();

// --------------------------------------------------------------- SEO endpoints
app.MapGet("/sitemap.xml", async (ISeoService seoService, CancellationToken cancellationToken) =>
    Results.Content(await seoService.GetSitemapXmlAsync(cancellationToken), "application/xml", Encoding.UTF8));

app.MapGet("/robots.txt", async (ISeoService seoService, CancellationToken cancellationToken) =>
    Results.Text(await seoService.GetRobotsTxtAsync(cancellationToken), "text/plain", Encoding.UTF8));

app.MapGet("/health", () => Results.Ok(new { status = "ok", time = DateTimeOffset.UtcNow }));

app.MapPost("/api/auth/token", async (LoginCommand command, IAuthService auth, JwtTokenService tokens,
    CancellationToken cancellationToken) =>
{
    var result = await auth.LoginAsync(command, cancellationToken: cancellationToken);
    if (!result.IsSuccess() || result.Data is null)
        return Results.Unauthorized();

    return Results.Ok(new
    {
        accessToken = tokens.Create(result.Data),
        tokenType = "Bearer",
        expiresIn = 3600,
        user = result.Data,
    });
}).AllowAnonymous();

app.MapRazorPages()
   .WithStaticAssets();

// ------------------------------------------------------------- database startup
await app.Services.InitializeFratakShopDataAsync();

// Create the initial owner account once. It uses the existing passwordless
// one-time-code flow and is never overwritten on subsequent application starts.
using (var adminScope = app.Services.CreateScope())
{
    var dataContext = adminScope.ServiceProvider.GetRequiredService<Fratak_Shop.Data.Contexts.FratakShopDbContext>();
    await DefaultAdminSeeder.EnsureAsync(dataContext);
}

// Defaults of the shop are created on the first run so the panel and the storefront
// never start empty (settings, menus, content pages and page-SEO records).
using (var scope = app.Services.CreateScope())
{
    await scope.ServiceProvider.GetRequiredService<ISiteSettingService>().EnsureDefaultsAsync();
    await scope.ServiceProvider.GetRequiredService<IMenuService>().EnsureDefaultsAsync();
    await scope.ServiceProvider.GetRequiredService<IContentPageService>().EnsureDefaultsAsync();
    await scope.ServiceProvider.GetRequiredService<IPageSeoService>().EnsureDefaultsAsync();
}

app.Run();
