using System.Text.RegularExpressions;
using System.Xml.Linq;
using Fratak_Shop.Web.Infrastructure;

namespace Fratak_Shop.Tests.Unit;

/// <summary>
/// تست‌های هویت بصری فراتک شاپ (docs/BRAND.md).
/// <para>
/// هدف: اگر کسی فایل برند را جابه‌جا/حذف کرد، رنگی را در کد عوض کرد که با tokens.css هویت بصری نمی‌خواند،
/// یا ارجاعی به فایل‌های قدیمی قالب (لوگوی «هایپر استار»، فونت YekanBakh، آیکون‌های pwa) جا ماند، تست شکست بخورد.
/// این تست‌ها فایل‌ها را مستقیم از مخزن می‌خوانند (ریشه = پوشه‌ای که Fratak-Shop.slnx دارد).
/// </para>
/// </summary>
public class BrandAssetsTests
{
    /// <summary>ریشهٔ مخزن: از پوشهٔ خروجی تست به بالا می‌رود تا به Fratak-Shop.slnx برسد.</summary>
    private static string RepoRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Fratak-Shop.slnx")))
                dir = dir.Parent;
            Assert.NotNull(dir);
            return dir!.FullName;
        }
    }

    private static string WebRoot => Path.Combine(RepoRoot, "src", "Fratak-Shop.Web", "wwwroot");

    /// <summary>مسیر سایت (مثل "/assets/brand/logo.svg") → مسیر فایل در wwwroot.</summary>
    private static string Physical(string sitePath) =>
        Path.Combine(WebRoot, sitePath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));

    /// <summary>عرض و ارتفاع PNG از هدر IHDR (بایت ۱۶ تا ۲۴، big-endian).</summary>
    private static (int Width, int Height) PngSize(string sitePath)
    {
        var bytes = File.ReadAllBytes(Physical(sitePath));
        Assert.True(bytes.Length > 24 && bytes[1] == (byte)'P' && bytes[2] == (byte)'N' && bytes[3] == (byte)'G', $"{sitePath} is not a PNG");
        static int BigEndian(byte[] b, int i) => (b[i] << 24) | (b[i + 1] << 16) | (b[i + 2] << 8) | b[i + 3];
        return (BigEndian(bytes, 16), BigEndian(bytes, 20));
    }

    [Fact]
    public void Every_brand_asset_exists_in_wwwroot()
    {
        foreach (var asset in BrandIdentity.AllAssets)
        {
            var path = Physical(asset);
            Assert.True(File.Exists(path), $"brand asset missing: {asset}");
            Assert.True(new FileInfo(path).Length > 100, $"brand asset looks empty: {asset}");
        }
    }

    [Theory]
    [InlineData(BrandIdentity.AppIcon192, 192, 192)]
    [InlineData(BrandIdentity.AppIcon512, 512, 512)]
    [InlineData(BrandIdentity.AppIconMaskable192, 192, 192)]
    [InlineData(BrandIdentity.AppIconMaskable512, 512, 512)]
    [InlineData(BrandIdentity.AppleTouchIcon, 180, 180)]
    [InlineData(BrandIdentity.NotificationBadge, 96, 96)]
    [InlineData(BrandIdentity.OgImage, 1200, 630)]
    public void Icons_have_the_sizes_the_manifest_and_meta_tags_promise(string asset, int width, int height)
    {
        Assert.Equal((width, height), PngSize(asset));
    }

    [Fact]
    public void Svg_logos_are_valid_and_the_dark_variants_are_transparent()
    {
        foreach (var asset in new[] { BrandIdentity.Logo, BrandIdentity.LogoOnDark, BrandIdentity.Mark, BrandIdentity.MarkOnDark, BrandIdentity.FaviconSvg })
        {
            var root = XDocument.Load(Physical(asset)).Root!;
            Assert.Equal("svg", root.Name.LocalName);
            Assert.NotNull(root.Attribute("viewBox"));
        }

        // نسخهٔ تیره روی پس‌زمینهٔ خود سایت می‌نشیند؛ مستطیل سرمه‌ای فایل اصلی هویت بصری باید حذف شده باشد.
        foreach (var asset in new[] { BrandIdentity.LogoOnDark, BrandIdentity.MarkOnDark })
        {
            var svg = File.ReadAllText(Physical(asset));
            Assert.DoesNotContain("<rect", svg);
            Assert.Contains("#FFFFFF", svg);
        }
    }

    [Fact]
    public void Code_colours_match_the_visual_identity_tokens()
    {
        var tokens = File.ReadAllText(Path.Combine(RepoRoot, "Visual-Identities", "Fratak-Shop", "tokens.css"));
        string Token(string name)
        {
            var match = Regex.Match(tokens, $@"--{name}:\s*(#[0-9A-Fa-f]{{6}})");
            Assert.True(match.Success, $"token --{name} not found");
            return match.Groups[1].Value.ToUpperInvariant();
        }

        Assert.Equal(Token("fr-navy"), BrandIdentity.Navy.ToUpperInvariant());
        Assert.Equal(Token("fr-blue"), BrandIdentity.Blue.ToUpperInvariant());
        Assert.Equal(Token("fr-gradient-start"), BrandIdentity.GradientStart.ToUpperInvariant());
        Assert.Equal(Token("fr-gradient-end"), BrandIdentity.GradientEnd.ToUpperInvariant());

        // Tailwind: طیف آبی برند (۴۰۰/۵۰۰/۶۰۰) و جوهر (zinc-900) همان رنگ‌های هویت بصری‌اند.
        var tailwind = File.ReadAllText(Path.Combine(RepoRoot, "src", "Fratak-Shop.Web", "tailwind.config.js"));
        Assert.Contains($"400: \"{BrandIdentity.GradientStart}\"", tailwind);
        Assert.Contains($"500: \"{BrandIdentity.Blue}\"", tailwind);
        Assert.Contains($"600: \"{BrandIdentity.GradientEnd}\"", tailwind);
        Assert.Contains($"900: \"{BrandIdentity.Navy}\"", tailwind);

        // خروجی ساخته‌شدهٔ Tailwind هم واقعاً با پالت برند ساخته شده باشد (npm run build:css فراموش نشده باشد).
        var output = File.ReadAllText(Path.Combine(WebRoot, "assets", "css", "output.css"));
        Assert.Contains("rgb(0 105 253", output); // #0069FD
        Assert.DoesNotContain("rgb(14 165 233", output); // sky-500 قدیمی قالب
    }

    [Fact]
    public void Font_css_points_to_existing_brand_fonts()
    {
        foreach (var css in new[] { "font.css", "invoice.css" })
        {
            var text = File.ReadAllText(Path.Combine(WebRoot, "assets", "css", css));
            var urls = Regex.Matches(text, @"url\(""\.\./font/([^""]+)""\)").Select(m => m.Groups[1].Value).ToList();
            Assert.NotEmpty(urls);
            foreach (var file in urls)
                Assert.True(File.Exists(Path.Combine(WebRoot, "assets", "font", file)), $"{css} → missing font {file}");
            Assert.Contains("Vazirmatn", text);
        }

        // مجوز هر دو فونت کنار فایل‌ها (شرط مجوز SIL OFL).
        Assert.True(File.Exists(Path.Combine(WebRoot, "assets", "font", "OFL-Vazirmatn.txt")));
        Assert.True(File.Exists(Path.Combine(WebRoot, "assets", "font", "OFL-SpaceGrotesk.txt")));
    }

    [Fact]
    public void No_reference_to_the_old_template_brand_is_left()
    {
        var webProject = Path.Combine(RepoRoot, "src", "Fratak-Shop.Web");
        var skip = new[] { $"{Path.DirectorySeparatorChar}node_modules{Path.DirectorySeparatorChar}", $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}" };
        var extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".cs", ".cshtml", ".js", ".css", ".html" };
        var forbidden = new[] { "/assets/image/logo.png", "/assets/image/pwa/", "YekanBakh" };

        foreach (var file in Directory.EnumerateFiles(webProject, "*", SearchOption.AllDirectories))
        {
            if (!extensions.Contains(Path.GetExtension(file)) || skip.Any(file.Contains) || file.Contains($"{Path.DirectorySeparatorChar}dependencies{Path.DirectorySeparatorChar}"))
                continue;
            // BrandIdentity.LegacyIcons عمداً آدرس‌های قدیمی را نام می‌برد تا به فایل برند جدید هدایت شوند (رفع آیکون کهنهٔ PWA).
            if (Path.GetFileName(file) == "BrandIdentity.cs")
                continue;
            var text = File.ReadAllText(file);
            foreach (var needle in forbidden)
                Assert.False(text.Contains(needle, StringComparison.OrdinalIgnoreCase), $"{Path.GetRelativePath(RepoRoot, file)} still references {needle}");
        }
    }

    [Theory]
    [InlineData(40, 50)]
    [InlineData(44, 55)]
    [InlineData(30, 37)]
    public void Lockup_reserves_the_mark_aspect_ratio(int height, int width)
    {
        var config = new Fratak_Shop.Core.Services.Branding.BrandStyleConfig();
        config.Places["Header"] = new Fratak_Shop.Core.Services.Branding.BrandPlaceStyle { IconHeight = height };
        var view = Fratak_Shop.Core.Services.Branding.BrandResolver.Resolve(Fratak_Shop.Core.Services.Branding.BrandPlace.Header,
            new Fratak_Shop.Core.DTOs.Settings.StoreSettingDto(), config, BrandIdentity.Defaults);
        Assert.Equal(width, view.IconWidth(view.IconHeight));
    }

    [Fact]
    public void Default_pwa_icons_are_black_without_text()
    {
        // پیکسل وسط لبهٔ بالای آیکون maskable (زمینهٔ تمام‌صفحه) باید مشکی باشد؛ PNG فشرده است پس فقط هدر و اندازه بررسی
        // می‌شود و رنگ از فایل منبع SVG که آیکون از آن ساخته شده.
        var svg = File.ReadAllText(Path.Combine(RepoRoot, "Visual-Identities", "Fratak-Shop", "svg", "app-icon-black.svg"));
        Assert.Contains($"fill=\"{BrandIdentity.AppIconBackground}\"", svg);
        Assert.DoesNotContain("<text", svg);
        Assert.Equal((512, 512), PngSize(BrandIdentity.AppIconMaskable512));
    }
}
