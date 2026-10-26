using System.Text.Json;
using Fratak_Shop.Core.Services.Settings;

namespace Fratak_Shop.Web.Infrastructure;

/// <summary>
/// Web app manifest built from the store settings (name, short name, theme colour).
/// Returns 404 while the PWA is disabled, so browsers don't offer installation.
/// The service worker itself is the static file <c>/sw.js</c>.
/// </summary>
public static class PwaEndpoints
{
    public static IEndpointRouteBuilder MapPwaEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/manifest.webmanifest", async (HttpContext http, IStoreSettingService settingService) =>
        {
            var settings = await settingService.GetAsync();
            if (!settings.IsPwaEnabled)
                return Results.NotFound();

            var themeColor = SafeColor(settings.ThemeColor, BrandIdentity.DefaultThemeColor);
            var manifest = new Dictionary<string, object?>
            {
                ["id"] = "/",
                ["name"] = settings.SiteName,
                ["short_name"] = string.IsNullOrWhiteSpace(settings.PwaShortName) ? settings.SiteName : settings.PwaShortName,
                ["description"] = settings.SiteDescription ?? settings.Tagline,
                ["lang"] = "fa",
                ["dir"] = "rtl",
                ["start_url"] = "/?source=pwa",
                ["scope"] = "/",
                ["display"] = "standalone",
                ["background_color"] = "#ffffff",
                ["theme_color"] = themeColor,
                ["icons"] = new object[]
                {
                    // آیکون‌های هویت بصری (wwwroot/assets/brand): گرادیان برند با گوشهٔ گرد + نسخهٔ maskable تمام‌صفحه.
                    new { src = BrandIdentity.AppIcon192, sizes = "192x192", type = "image/png", purpose = "any" },
                    new { src = BrandIdentity.AppIcon512, sizes = "512x512", type = "image/png", purpose = "any" },
                    new { src = BrandIdentity.AppIconMaskable192, sizes = "192x192", type = "image/png", purpose = "maskable" },
                    new { src = BrandIdentity.AppIconMaskable512, sizes = "512x512", type = "image/png", purpose = "maskable" },
                },
                ["shortcuts"] = new object[]
                {
                    new { name = "سبد خرید", url = "/cart" },
                    new { name = "سفارش‌های من", url = "/profile/orders" },
                },
            };

            http.Response.Headers.CacheControl = "public, max-age=3600";
            return Results.Text(JsonSerializer.Serialize(manifest), "application/manifest+json; charset=utf-8");
        });

        return app;
    }

    private static string SafeColor(string? value, string fallback) =>
        value is not null && System.Text.RegularExpressions.Regex.IsMatch(value, "^#(?:[0-9a-fA-F]{3}|[0-9a-fA-F]{6})$") ? value : fallback;
}
