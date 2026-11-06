using System.Text.Json;
using Fratak_Shop.Core.Services.Branding;
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
            string Icon(string path) => versions.AddFileVersionToPath(http.Request.PathBase, path);

            var settings = await settingService.GetAsync();
            if (!settings.IsPwaEnabled)
                return Results.NotFound();

            var themeColor = SafeColor(settings.ThemeColor, BrandIdentity.DefaultThemeColor);
            // مرحلهٔ دوازدهم: آیکون‌های اپ (اصلی، کوچک، maskable) و رنگ صفحهٔ شروع از «استودیوی لوگو».
            var pwa = PwaIconSet.From(settings, BrandStyleRules.Parse(settings.BrandStyleJson));
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
                ["background_color"] = pwa.Background,
                ["theme_color"] = themeColor,
                ["icons"] = Icons(pwa, Icon),
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

    /// <summary>
    /// آیکون‌های مانیفست از <see cref="PwaIconSet"/> (مرحلهٔ دوازدهم):
    /// همیشه یک آیکون ۱۹۲ و یک آیکون ۵۱۲ با purpose=any؛ آیکون maskable فقط اگر واقعاً وجود دارد (آیکون معمولی که maskable
    /// اعلام شود در اندروید بریده می‌شود). آدرس‌های محلی نسخهٔ فایل (?v=hash) می‌گیرند تا با هر تغییر، اپ نصب‌شده آیکون تازه بگیرد.
    /// مثال پیش‌فرض: app-icon-192.png، app-icon-512.png، app-icon-maskable-192.png، app-icon-maskable-512.png.
    /// </summary>
    public static object[] Icons(PwaIconSet pwa, Func<string, string> version)
    {
        string Src(string url) => url.StartsWith('/') && !url.StartsWith("//") ? version(url) : url;
        var icons = new List<object>
        {
            new { src = Src(pwa.Icon192), sizes = Size(pwa.Icon192, "192x192", pwa.IsDefault), type = MimeOf(pwa.Icon192), purpose = "any" },
            new { src = Src(pwa.Icon512), sizes = Size(pwa.Icon512, "512x512", pwa.IsDefault), type = MimeOf(pwa.Icon512), purpose = "any" },
        };
        if (pwa.Maskable192 is { } mask192)
            icons.Add(new { src = Src(mask192), sizes = "192x192", type = MimeOf(mask192), purpose = "maskable" });
        if (pwa.Maskable512 is { } mask512)
            icons.Add(new { src = Src(mask512), sizes = Size(mask512, "512x512", pwa.IsDefault), type = MimeOf(mask512), purpose = "maskable" });
        return [.. icons];
    }

    /// <summary>SVG اندازهٔ «any» دارد؛ بقیه اندازهٔ اعلام‌شده (مرورگر تصویر بزرگ‌تر/کوچک‌تر را خودش مقیاس می‌دهد).</summary>
    private static string Size(string url, string size, bool isDefault) => !isDefault && BrandResolver.IsSvg(url) ? "any" : size;

    /// <summary>نوع فایل از پسوند. مثال: «/a.webp» → image/webp؛ ناشناخته → image/png.</summary>
    public static string MimeOf(string url)
    {
        var path = url.Split('?', '#')[0];
        return path.EndsWith(".svg", StringComparison.OrdinalIgnoreCase) ? "image/svg+xml"
            : path.EndsWith(".webp", StringComparison.OrdinalIgnoreCase) ? "image/webp"
            : path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) ? "image/jpeg"
            : "image/png";
    }

    private static string SafeColor(string? value, string fallback) =>
        value is not null && System.Text.RegularExpressions.Regex.IsMatch(value, "^#(?:[0-9a-fA-F]{3}|[0-9a-fA-F]{6})$") ? value : fallback;
}
