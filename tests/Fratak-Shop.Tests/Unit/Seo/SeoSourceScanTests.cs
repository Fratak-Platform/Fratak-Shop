using System.Text.RegularExpressions;

namespace Fratak_Shop.Tests.Unit.Seo;

/// <summary>
/// اسکن ایستای کد سایت برای قواعد سئوی خودکار (بدون اجرای برنامه):
/// • هیچ تگ img در قالب‌ها بدون alt نیست؛ • هیچ کامنت HTML («&lt;!--») در فایل‌های cshtml نیست (از Razor «@* *@» استفاده شود)؛
/// • در wwwroot هیچ JPG/PNG/GIF/BMP نیست جز پوشه‌های مجاز (نشان تجاری/آیکون‌های PWA که مرورگرها PNG می‌خواهند، و کتابخانهٔ نقشه).
/// <para>اگر این تست شکست خورد: کامنت را به Razor تبدیل کنید، alt بدهید یا تصویر را WebP کنید.</para>
/// </summary>
[Trait("Category", "Unit")]
[Trait("Area", "SeoSource")]
public class SeoSourceScanTests
{
    /// <summary>پوشه‌هایی که PNG در آن‌ها عمداً می‌ماند (مسیر نسبی wwwroot با «/»).</summary>
    public static readonly string[] AllowedRasterFolders = ["assets/brand/", "assets/js/dependencies/"];

    private static string RepoRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Fratak-Shop.slnx")))
                dir = dir.Parent;
            return dir?.FullName ?? throw new InvalidOperationException("Repository root (Fratak-Shop.slnx) not found.");
        }
    }

    private static string WebProject => Path.Combine(RepoRoot, "src", "Fratak-Shop.Web");

    private static IEnumerable<string> Views() =>
        Directory.EnumerateFiles(WebProject, "*.cshtml", SearchOption.AllDirectories)
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                        && !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal));

    private static string Relative(string file) => Path.GetRelativePath(RepoRoot, file).Replace('\\', '/');

    [Fact]
    public void Views_contain_no_html_comments()
    {
        var offenders = Views().Where(f => File.ReadAllText(f).Contains("<!--", StringComparison.Ordinal)).Select(Relative).ToList();
        Assert.True(offenders.Count == 0, "HTML comments found in: " + string.Join(", ", offenders));
    }

    [Fact]
    public void Every_img_tag_in_views_has_an_alt()
    {
        var offenders = new List<string>();
        foreach (var file in Views())
        {
            var text = File.ReadAllText(file);
            foreach (Match tag in Regex.Matches(text, @"<img\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Singleline, TimeSpan.FromSeconds(5)))
            {
                if (!Regex.IsMatch(tag.Value, @"\salt\s*=", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1)))
                    offenders.Add($"{Relative(file)}: {tag.Value[..Math.Min(tag.Value.Length, 120)]}");
            }
        }

        Assert.True(offenders.Count == 0, "img without alt:\n" + string.Join("\n", offenders));
    }

    [Fact]
    public void Wwwroot_has_no_jpg_png_or_gif_outside_the_allowed_folders()
    {
        var root = Path.Combine(WebProject, "wwwroot");
        var offenders = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(root, f).Replace('\\', '/'))
            .Where(f => !f.StartsWith("uploads/", StringComparison.OrdinalIgnoreCase))
            .Where(f => Path.GetExtension(f).ToLowerInvariant() is ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp")
            .Where(f => !AllowedRasterFolders.Any(a => f.StartsWith(a, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        Assert.True(offenders.Count == 0, "Convert to WebP: " + string.Join(", ", offenders));
    }

    [Fact]
    public void Views_do_not_reference_legacy_raster_images()
    {
        var pattern = new Regex(@"/(?:assets/image|uploads)/[A-Za-z0-9_\-/]+\.(?:jpe?g|png|gif)\b", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(5));
        var offenders = Views()
            .SelectMany(f => pattern.Matches(File.ReadAllText(f)).Select(m => $"{Relative(f)}: {m.Value}"))
            .Where(m => !m.Contains("/assets/image/pwa/", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.True(offenders.Count == 0, "Legacy image references: " + string.Join(", ", offenders));
    }

    [Fact]
    public void Live_seo_script_never_injects_html()
    {
        var script = File.ReadAllText(Path.Combine(WebProject, "wwwroot", "assets", "js", "seo-live.js"));
        Assert.DoesNotContain("innerHTML", script, StringComparison.Ordinal);
        Assert.DoesNotContain("insertAdjacentHTML", script, StringComparison.Ordinal);
        Assert.DoesNotContain("eval(", script, StringComparison.Ordinal);
        Assert.DoesNotContain("alert(", script, StringComparison.Ordinal);
        Assert.DoesNotContain("confirm(", script.Replace("FratakModal.confirm(", "", StringComparison.Ordinal), StringComparison.Ordinal);
    }
}
