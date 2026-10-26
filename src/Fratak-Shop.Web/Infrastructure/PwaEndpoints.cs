using System.Text.Json;
using Fratak_Shop.Core.Services.Settings;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace Fratak_Shop.Web.Infrastructure;

/// <summary>
/// Web app manifest built from the store settings (name, short name, theme colour).
/// Returns 404 while the PWA is disabled, so browsers don't offer installation.
/// The service worker itself is the static file <c>/sw.js</c>.
/// آیکون‌ها با هش نسخه لینک می‌شوند، خود مانیفست no-cache است و آدرس‌های آیکون قدیمی فایل برند را برمی‌گردانند
/// (رفع باگ «لوگوی قدیمی بعد از نصب PWA» – docs/BRAND.md).
/// </summary>
public static class PwaEndpoints
{
    public static IEndpointRouteBuilder MapPwaEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/manifest.webmanifest", async (HttpContext http, IStoreSettingService settingService, IFileVersionProvider versions) =>
        {
            // آدرس آیکون‌ها با هش محتوای فایل (?v=...): اگر فایل آیکون عوض شود آدرسش هم عوض می‌شود؛ کروم/اندروید
            // آیکون اپ نصب‌شده را فقط وقتی دوباره می‌سازند که آدرس آیکون در مانیفست تغییر کند (و هیچ کش میانی هم
            // نسخهٔ قدیمی را برنمی‌گرداند). مثال: /assets/brand/app-icon-512.png?v=Ab3x...
            string Icon(string path) => versions.AddVersionToPath(http.Request.PathBase, path);

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
                    new { src = Icon(BrandIdentity.AppIcon192), sizes = "192x192", type = "image/png", purpose = "any" },
                    new { src = Icon(BrandIdentity.AppIcon512), sizes = "512x512", type = "image/png", purpose = "any" },
                    new { src = Icon(BrandIdentity.AppIconMaskable192), sizes = "192x192", type = "image/png", purpose = "maskable" },
                    new { src = Icon(BrandIdentity.AppIconMaskable512), sizes = "512x512", type = "image/png", purpose = "maskable" },
                },
                ["shortcuts"] = new object[]
                {
                    new { name = "سبد خرید", url = "/cart" },
                    new { name = "سفارش‌های من", url = "/profile/orders" },
                },
            };

            // no-cache: مرورگر/CDN هر بار اعتبار مانیفست را می‌پرسد؛ مانیفست کهنه (با آیکون قدیمی) دلیل اصلی
            // «هنگام نصب لوگوی جدید، بعد از نصب لوگوی قدیمی» بود.
            http.Response.Headers.CacheControl = "no-cache";
            return Results.Text(JsonSerializer.Serialize(manifest), "application/manifest+json; charset=utf-8");
        });

        // آیکون‌های قالب قدیمی: هر درخواستی به آدرس قدیمی (مانیفست کهنه در کش گوشی/CDN، اپ نصب‌شدهٔ قبلی، سافاری iOS)
        // همان فایل برند جدید را می‌گیرد، نه 404 و نه تصویر قدیمی. فایل‌های قدیمی از wwwroot حذف شده‌اند، پس
        // StaticFiles این آدرس‌ها را رد می‌کند و به این endpoint می‌رسند.
        foreach (var (legacy, current) in BrandIdentity.LegacyIcons)
        {
            app.MapMethods(legacy, [HttpMethods.Get, HttpMethods.Head], (HttpContext http, IWebHostEnvironment environment) =>
            {
                var file = environment.WebRootFileProvider.GetFileInfo(current);
                if (!file.Exists || file.PhysicalPath is null)
                    return Results.NotFound();
                // یک روز: اگر روزی دوباره آیکون عوض شد، نسخهٔ قدیمی زیاد در کش نماند.
                http.Response.Headers.CacheControl = "public, max-age=86400";
                return Results.File(file.PhysicalPath, "image/png", enableRangeProcessing: false);
            });
        }

        return app;
    }

    private static string SafeColor(string? value, string fallback) =>
        value is not null && System.Text.RegularExpressions.Regex.IsMatch(value, "^#(?:[0-9a-fA-F]{3}|[0-9a-fA-F]{6})$") ? value : fallback;
}
