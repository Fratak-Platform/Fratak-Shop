using Fratak_Shop.Core;
using Fratak_Shop.Core.Services.Locations;
using Fratak_Shop.Data.Extensions;
using Fratak_Shop.Web.Extensions;
using Fratak_Shop.Web.Infrastructure;
using Fratak_Shop.Web.Infrastructure.Middleware;
using Fratak_Shop.Web.Infrastructure.Seeders;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

// ── Presentation ──────────────────────────────────────────────────────
builder.Services.AddRazorPages(options =>
{
    // Customer area: every profile page requires a signed-in customer.
    options.Conventions.AuthorizeFolder("/Profile");
    options.Conventions.AuthorizePage("/Checkout");
    options.Conventions.AuthorizePage("/CheckoutComplete");
    options.Conventions.AuthorizePage("/CheckoutFailed");
    options.Conventions.AuthorizeFolder("/Payment");
    options.Conventions.AuthorizePage("/Verify");
    options.Conventions.AuthorizePage("/Welcome");

    // Friendly aliases for the product listing.
    options.Conventions.AddPageRoute("/Search", "products");
    options.Conventions.AddPageRoute("/Search", "category/{category}");
    options.Conventions.AddPageRoute("/Search", "brand/{brand}");
    options.Conventions.AddPageRoute("/Blog", "blog/category/{category}");
    options.Conventions.AddPageRoute("/Error404", "404");
});

builder.Services.Configure<RouteOptions>(options =>
{
    options.LowercaseUrls = true;
    options.LowercaseQueryStrings = false;
});

builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/login";
        options.LogoutPath = "/logout";
        options.AccessDeniedPath = "/login";
        options.Cookie.Name = ".Fratak.Auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.ExpireTimeSpan = TimeSpan.FromDays(30);
        options.SlidingExpiration = true;
    });

builder.Services.AddAuthorization();

// ── Application layers ────────────────────────────────────────────────
var connectionString = builder.Configuration["ConnectionDefault"]
                       ?? builder.Configuration.GetConnectionString("DefaultConnection")
                       ?? throw new InvalidOperationException("Connection string 'ConnectionDefault' is not configured.");

builder.Services.AddFratakShopData(connectionString);
builder.Services.AddFratakShopCore();
builder.Services.AddFratakShopWeb(builder.Configuration);

var app = builder.Build();

// ── HTTP pipeline ─────────────────────────────────────────────────────
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}
else
{
    app.UseExceptionHandler("/Error404");
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseStatusCodePagesWithReExecute("/Error404");

app.UseStaticFiles();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.UseMiddleware<SeoRedirectMiddleware>();

app.MapRazorPages();

// Cities of a province, used by the address forms (province → city select).
app.MapGet("/api/locations/provinces/{provinceId:guid}/cities", async (Guid provinceId, ILocationService locations) =>
{
    var cities = await locations.GetCitiesAsync(provinceId);
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
