using Fratak_Shop.Core;
using Fratak_Shop.Data.Extensions;
using Fratak_Shop.Web.Extensions;
using Fratak_Shop.Web.Infrastructure.Middleware;

var builder = WebApplication.CreateBuilder(args);

// ── MVC / Razor Pages ──────────────────────────────────────────────
builder.Services.AddRazorPages(options =>
{
    options.Conventions.AddPageRoute("/Error404", "/404");
})
.AddRazorPagesOptions(o =>
{
    // Lower-case URLs
    o.Conventions.ConfigureFilter(new Microsoft.AspNetCore.Mvc.ViewFeatures.Filters.AutoValidateAntiforgeryTokenAttribute());
});

builder.Services.AddControllers();

// ── Infrastructure ─────────────────────────────────────────────────
builder.Services.AddHttpContextAccessor();
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromHours(48);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.Name = ".Fratak.Session";
});

builder.Services.AddAuthentication(Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Login";
        options.LogoutPath = "/Logout";
        options.AccessDeniedPath = "/Login";
        options.Cookie.Name = ".Fratak.Auth";
        options.Cookie.HttpOnly = true;
        options.ExpireTimeSpan = TimeSpan.FromDays(30);
        options.SlidingExpiration = true;
    });

builder.Services.AddAntiforgery(o => o.HeaderName = "X-CSRF-TOKEN");

// ── App layers ─────────────────────────────────────────────────────
var connectionString = builder.Configuration["ConnectionDefault"]
    ?? builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("ConnectionDefault not configured.");

builder.Services.AddFratakShopData(connectionString);
builder.Services.AddFratakShopCore();
builder.Services.AddFratakShopWeb();

// ── Build ──────────────────────────────────────────────────────────
var app = builder.Build();

// ── Pipeline ───────────────────────────────────────────────────────
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error404");
    app.UseHsts();
}
else
{
    app.UseDeveloperExceptionPage();
}

app.UseStatusCodePagesWithReExecute("/Error404");

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

app.UseSession();
app.UseAuthentication();
app.UseAuthorization();

// SEO 301 redirects — must be after routing so we have the path, before endpoints.
app.UseMiddleware<SeoRedirectMiddleware>();

app.MapStaticAssets();
app.MapRazorPages();
app.MapControllers();

// ── DB migrate + seed ──────────────────────────────────────────────
await app.Services.InitializeFratakShopDataAsync();

app.Run();
