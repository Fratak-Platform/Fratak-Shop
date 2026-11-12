using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Fratak_Shop.Core.Services.Seo.Engine;
using Fratak_Shop.Core.Services.Seo.Tools;
using Fratak_Shop.Data.Entities.SeoEngine;
using Fratak_Shop.Web.Areas.Admin.Pages.Seo;
using Fratak_Shop.Web.Infrastructure.SeoEngine;

namespace Fratak_Shop.Tests.Unit;

/// <summary>
/// تست‌های ابزارهای سئو (مرحلهٔ هفتم – فاز ۸ب) بدون دیتابیس و بدون اینترنت:
/// • یابندهٔ لینک شکسته: استخراج لینک از HTML، یکسان‌سازی مسیر، تشخیص داخلی/بیرونی، فهرست صفحه‌های سایت،
///   قانون‌های پاسخ HTTP و بازنویسی امن متن.
/// • Search Console: خواندن فایل کلید، JWT امضاشده، خواندن پاسخ‌های گوگل (نمونه‌ها ساختار واقعی API دارند)،
///   راهنمای خطاها، آدرس ویژگی، جمع‌بندی آمار و «فرصت‌ها».
/// • ویرایش گروهی: فیلترها، پاک‌سازی و جست‌وجو/جایگزینی.
/// • پیش‌نمایش اشتراک و درصد تغییر صفحهٔ Search Console.
/// </summary>
public class SeoToolsTests
{
    // ═══ یابندهٔ لینک ═══════════════════════════════════════════════════

    [Fact]
    public void Extractor_returns_unique_checkable_links_and_images_with_counts()
    {
        const string html = """
            <p>برای انتخاب <a href="/blog/rahnama">راهنمای خرید</a> را بخوانید یا <a href="/blog/rahnama">اینجا</a> بزنید.</p>
            <p><a href="#faq">پرسش‌ها</a> <a href="tel:02112345678">تماس</a> <a href="mailto:a@b.ir">ایمیل</a>
               <a href="javascript:void(0)">هیچ</a> <a href="https://digikala.com/x">دیجی‌کالا</a></p>
            <img src="/uploads/products/a.webp" alt="  عکس   محصول ">
            <img src="data:image/png;base64,AAAA">
            """;

        var links = SeoLinkExtractor.Extract(html);

        Assert.Equal(3, links.Count);
        var guide = Assert.Single(links, l => l.Url == "/blog/rahnama");
        Assert.Equal(2, guide.Count);
        Assert.Equal("راهنمای خرید", guide.Anchor); // اولین متن لنگر می‌ماند
        Assert.False(guide.IsImage);
        var image = Assert.Single(links, l => l.IsImage);
        Assert.Equal("/uploads/products/a.webp", image.Url);
        Assert.Equal("عکس محصول", image.Anchor); // فاصله‌های اضافه حذف
        Assert.Contains(links, l => l.Url == "https://digikala.com/x");
        Assert.Empty(SeoLinkExtractor.Extract(null));
        Assert.Empty(SeoLinkExtractor.Extract("<p>بدون لینک</p>"));
    }

    [Theory]
    [InlineData("#faq", false)]
    [InlineData("tel:021123", false)]
    [InlineData("mailto:a@b.ir", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("JavaScript:alert(1)", false)]
    [InlineData("data:image/png;base64,AA", false)]
    [InlineData("ftp://files.ir/a", false)]
    [InlineData("/product/x", true)]
    [InlineData("https://a.ir/b", true)]
    [InlineData("HTTP://a.ir/b", true)]
    [InlineData("../x", true)]
    [InlineData("x/y", true)]
    [InlineData("/a:b", true)] // «:» بعد از اولین / یعنی مسیر نسبی، نه طرح
    public void Extractor_knows_which_urls_are_checkable(string url, bool expected) =>
        Assert.Equal(expected, SeoLinkExtractor.IsCheckable(url));

    [Theory]
    [InlineData("/Product/%D8%B4%D8%A7%D9%85%D9%BE%D9%88/?ref=x", "/product/شامپو")]
    [InlineData("/blog//x/", "/blog/x")]
    [InlineData("blog/x#part", "/blog/x")]
    [InlineData("", "/")]
    [InlineData(null, "/")]
    [InlineData("/", "/")]
    [InlineData("\\uploads\\a.webp", "/uploads/a.webp")]
    public void Paths_are_normalized_for_comparison(string? input, string expected) =>
        Assert.Equal(expected, SeoPaths.Normalize(input));

    [Fact]
    public void File_path_keeps_letter_case_for_linux_disk_checks()
    {
        Assert.Equal("/uploads/Photo.WEBP", SeoPaths.FilePath("/uploads/Photo.WEBP?v=2"));
        Assert.Equal("/uploads/photo.webp", SeoPaths.Normalize("/uploads/Photo.WEBP?v=2"));
    }

    [Fact]
    public void Path_from_url_accepts_only_web_addresses()
    {
        Assert.Equal("/blog/ر", SeoPaths.FromUrl("https://fratak.ir/blog/%D8%B1/"));
        Assert.Equal("/", SeoPaths.FromUrl("https://fratak.ir"));
        Assert.Null(SeoPaths.FromUrl("/blog/x")); // روی لینوکس «/x» یک Uri مطلق file:// است؛ نباید پذیرفته شود
        Assert.Null(SeoPaths.FromUrl("not a url"));
        Assert.Null(SeoPaths.FromUrl(null));
        Assert.Equal("fratak.ir", SeoPaths.BareHost(" WWW.Fratak.ir. "));
        Assert.Equal(64, SeoPaths.Hash("https://a.ir/x").Length);
    }

    private static readonly IReadOnlySet<string> OwnHosts = new HashSet<string>(StringComparer.Ordinal) { "fratak.ir" };

    [Fact]
    public void Link_target_separates_own_site_from_external_sites()
    {
        var own = SeoLinkTarget.Classify("https://www.fratak.ir/Product/A?x=1", "/blog/post", OwnHosts)!;
        Assert.True(own.IsInternal);
        Assert.Equal("/product/a", own.Path);
        Assert.Equal("/Product/A", own.FilePath);
        Assert.False(own.IsHttp);

        var external = SeoLinkTarget.Classify("https://digikala.com/x", "/blog/post", OwnHosts)!;
        Assert.False(external.IsInternal);
        Assert.Equal("digikala.com", external.External!.Host);

        var insecure = SeoLinkTarget.Classify("http://cdn.example.com/a.jpg", "/", OwnHosts)!;
        Assert.True(insecure.IsHttp);
        Assert.False(insecure.IsInternal);

        var protocolRelative = SeoLinkTarget.Classify("//cdn.example.com/x.js", "/", OwnHosts)!;
        Assert.Equal("https", protocolRelative.External!.Scheme);

        // لینک نسبی مثل مرورگر نسبت به صفحهٔ مبدأ.
        Assert.Equal("/blog/x", SeoLinkTarget.Classify("x", "/blog/post", OwnHosts)!.Path);
        Assert.Equal("/uploads/a.webp", SeoLinkTarget.Classify("../uploads/a.webp", "/blog/post", OwnHosts)!.Path);
        Assert.Equal("/product/b", SeoLinkTarget.Classify("/product/b", "/blog/post", OwnHosts)!.Path);

        // بدون دامنهٔ ثبت‌شده، آدرس مطلق بیرونی حساب می‌شود.
        Assert.False(SeoLinkTarget.Classify("https://fratak.ir/a", "/", new HashSet<string>())!.IsInternal);
    }

    private static InternalLinkIndex Index() => new()
    {
        Products = new(StringComparer.Ordinal) { "galaxy-a55" },
        InactiveProducts = new(StringComparer.Ordinal) { "old-phone" },
        Categories = new(StringComparer.Ordinal) { "mobile" },
        Brands = new(StringComparer.Ordinal) { "samsung" },
        Posts = new(StringComparer.Ordinal) { "راهنما" },
        BlogCategories = new(StringComparer.Ordinal) { "news" },
        Pages = new(StringComparer.Ordinal) { "about" },
        StaticPaths = new(StringComparer.Ordinal) { "/", "/blog", "/faq" },
        Redirects = new(StringComparer.Ordinal)
        {
            ["/product/old"] = "/product/mid",
            ["/product/mid"] = "/product/galaxy-a55",
            ["/loop-a"] = "/loop-b",
            ["/loop-b"] = "/loop-a",
            ["/go"] = "https://other.ir/x",
        },
    };

    [Fact]
    public void Internal_index_checks_pages_without_http_requests()
    {
        var index = Index();

        Assert.True(index.Check("/product/Galaxy-A55")!.IsOk);
        Assert.True(index.Check("/category/mobile/")!.IsOk);
        Assert.True(index.Check("/brand/samsung")!.IsOk);
        Assert.True(index.Check("/blog/%D8%B1%D8%A7%D9%87%D9%86%D9%85%D8%A7")!.IsOk); // نامک فارسی رمزشده
        Assert.True(index.Check("/blog/category/news")!.IsOk);
        Assert.True(index.Check("/page/about")!.IsOk);
        Assert.True(index.Check("/faq")!.IsOk);
        Assert.True(index.Check("/admin/products")!.IsOk); // مسیرهای خصوصی بررسی نمی‌شوند

        var inactive = index.Check("/product/old-phone")!;
        Assert.Equal(SeoLinkProblem.NotFound, inactive.Problem);
        Assert.Contains("غیرفعال", inactive.Detail);
        Assert.Equal(SeoLinkProblem.NotFound, index.Check("/product/nothing")!.Problem);
        Assert.Equal(SeoLinkProblem.NotFound, index.Check("/blog/category/x")!.Problem);
        Assert.Equal(SeoLinkProblem.NotFound, index.Check("/nowhere")!.Problem);

        // فایل‌ها روی دیسک بررسی می‌شوند.
        Assert.Null(index.Check("/uploads/a.webp"));
        Assert.Null(index.Check("/robots.txt"));
    }

    [Fact]
    public void Internal_index_follows_redirect_chains_to_the_final_address()
    {
        var index = Index();

        var chain = index.Check("/product/old")!;
        Assert.Equal(SeoLinkProblem.Redirected, chain.Problem);
        Assert.Equal("/product/galaxy-a55", chain.SuggestedUrl);

        Assert.Equal("https://other.ir/x", index.Check("/go")!.SuggestedUrl);

        // حلقه بی‌پایان نمی‌چرخد.
        Assert.Equal(SeoLinkProblem.Redirected, index.Check("/loop-a")!.Problem);
    }

    [Theory]
    [InlineData(null, SeoLinkProblem.Unreachable)]
    [InlineData(200, SeoLinkProblem.None)]
    [InlineData(404, SeoLinkProblem.NotFound)]
    [InlineData(410, SeoLinkProblem.Gone)]
    [InlineData(400, SeoLinkProblem.NotFound)]
    [InlineData(401, SeoLinkProblem.Blocked)]
    [InlineData(403, SeoLinkProblem.Blocked)]
    [InlineData(429, SeoLinkProblem.Blocked)]
    [InlineData(999, SeoLinkProblem.Blocked)]
    [InlineData(500, SeoLinkProblem.ServerError)]
    [InlineData(503, SeoLinkProblem.ServerError)]
    public void Probe_status_codes_map_to_link_problems(int? status, SeoLinkProblem expected) =>
        Assert.Equal(expected, SeoLinkRules.FromProbe(new LinkProbeResult(status), new Uri("https://a.ir/x")).Problem);

    [Fact]
    public void Probe_redirects_are_classified()
    {
        var original = new Uri("https://shop.ir/product/a");

        var moved = SeoLinkRules.FromProbe(new LinkProbeResult(200, "https://new.ir/a", PermanentRedirect: true), original);
        Assert.Equal(SeoLinkProblem.Redirected, moved.Problem);
        Assert.Equal("https://new.ir/a", moved.SuggestedUrl);

        // ریدایرکت موقت مشکل نیست.
        Assert.True(SeoLinkRules.FromProbe(new LinkProbeResult(200, "https://new.ir/a", PermanentRedirect: false), original).IsOk);

        // فقط http → https یا www یا / پایانی: همان آدرس است.
        Assert.True(SeoLinkRules.FromProbe(new LinkProbeResult(200, "https://www.shop.ir/product/a/", PermanentRedirect: true), original).IsOk);

        // «soft 404»: لینک عمیق به صفحهٔ اصلی همان سایت.
        Assert.Equal(SeoLinkProblem.NotFound, SeoLinkRules.FromProbe(new LinkProbeResult(200, "https://www.shop.ir/", PermanentRedirect: false), original).Problem);
    }

    [Fact]
    public void Severity_orders_problems()
    {
        Assert.Equal(3, SeoLinkRules.Severity(SeoLinkProblem.NotFound));
        Assert.Equal(3, SeoLinkRules.Severity(SeoLinkProblem.Gone));
        Assert.Equal(2, SeoLinkRules.Severity(SeoLinkProblem.ServerError));
        Assert.Equal(2, SeoLinkRules.Severity(SeoLinkProblem.Insecure));
        Assert.Equal(1, SeoLinkRules.Severity(SeoLinkProblem.Redirected));
        Assert.Equal(1, SeoLinkRules.Severity(SeoLinkProblem.Blocked));
        Assert.Equal(0, SeoLinkRules.Severity(SeoLinkProblem.None));
        Assert.All(Enum.GetValues<SeoLinkProblem>(), p => Assert.False(string.IsNullOrWhiteSpace(SeoLinkRules.Title(p))));
    }

    [Theory]
    [InlineData("/product/a", true)]
    [InlineData("https://a.ir/b?c=1", true)]
    [InlineData("http://a.ir", true)]
    [InlineData("//evil.com", false)]
    [InlineData("/\\evil.com", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("data:text/html,x", false)]
    [InlineData("/a b", false)]
    [InlineData("/a\"onmouseover=\"x", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Only_safe_urls_may_be_written_into_content(string? url, bool expected) =>
        Assert.Equal(expected, SeoLinkRewriter.IsSafeUrl(url));

    [Fact]
    public void Rewriter_changes_or_unlinks_every_occurrence()
    {
        const string html = "<p>متن <a href=\"/old\">لینک <b>پررنگ</b></a> و <a href=\"/old\">دوباره</a> و <a href=\"/keep\">بماند</a></p>";

        var (changedHtml, changed) = SeoLinkRewriter.Replace(html, "/old", false, "/new");
        Assert.Equal(2, changed);
        Assert.DoesNotContain("/old", changedHtml);
        Assert.Contains("href=\"/new\"", changedHtml);
        Assert.Contains("href=\"/keep\"", changedHtml);

        var (unlinked, removed) = SeoLinkRewriter.Replace(html, "/old", false, null);
        Assert.Equal(2, removed);
        Assert.Equal("<p>متن لینک <b>پررنگ</b> و دوباره و <a href=\"/keep\">بماند</a></p>", unlinked);

        var (noImage, images) = SeoLinkRewriter.Replace("<p>الف<img src=\"/x.webp\" alt=\"\">ب</p>", "/x.webp", true, null);
        Assert.Equal(1, images);
        Assert.Equal("<p>الفب</p>", noImage);

        var (same, none) = SeoLinkRewriter.Replace(html, "/missing", false, "/new");
        Assert.Equal(0, none);
        Assert.Same(html, same); // متن دست نمی‌خورد
    }

    // ═══ Search Console ═════════════════════════════════════════════════

    private static (string Json, RSA Key) ServiceAccountJson(string? tokenUri = "https://oauth2.googleapis.com/token", string type = "service_account")
    {
        var rsa = RSA.Create(2048);
        var json = JsonSerializer.Serialize(new Dictionary<string, string?>
        {
            ["type"] = type,
            ["project_id"] = "fratak-seo",
            ["private_key_id"] = "kid123",
            ["private_key"] = rsa.ExportPkcs8PrivateKeyPem(),
            ["client_email"] = "seo-bot@fratak-seo.iam.gserviceaccount.com",
            ["token_uri"] = tokenUri,
        });
        return (json, rsa);
    }

    [Fact]
    public void Service_account_file_is_parsed_and_validated()
    {
        var (json, rsa) = ServiceAccountJson();
        using (rsa)
        {
            Assert.True(GscServiceAccount.TryParse(json, out var account, out var error), error);
            Assert.Equal("seo-bot@fratak-seo.iam.gserviceaccount.com", account!.ClientEmail);
            Assert.Equal("kid123", account.PrivateKeyId);
            Assert.Equal(GscServiceAccount.DefaultTokenUri, account.TokenUri);
        }

        // فایل دست‌کاری‌شده نمی‌تواند کلید امضاشده را به سرور دیگری بفرستد.
        var (evil, evilKey) = ServiceAccountJson("https://evil.example.com/token");
        using (evilKey)
        {
            Assert.True(GscServiceAccount.TryParse(evil, out var account, out _));
            Assert.Equal(GscServiceAccount.DefaultTokenUri, account!.TokenUri);
        }

        var (user, userKey) = ServiceAccountJson(type: "authorized_user");
        using (userKey)
        {
            Assert.False(GscServiceAccount.TryParse(user, out _, out var error));
            Assert.Contains("حساب سرویس", error);
        }

        Assert.False(GscServiceAccount.TryParse("{not json", out _, out var bad));
        Assert.Contains("JSON", bad);
        Assert.False(GscServiceAccount.TryParse("", out _, out _));
        Assert.False(GscServiceAccount.TryParse("""{"type":"service_account","client_email":"a@b.iam","private_key":"-----BEGIN PRIVATE KEY-----\nAAAA\n-----END PRIVATE KEY-----\n"}""", out _, out var broken));
        Assert.Contains("کلید خصوصی", broken);
        Assert.False(GscServiceAccount.TryParse("""{"type":"service_account","private_key":"x"}""", out _, out var noEmail));
        Assert.Contains("client_email", noEmail);
    }

    [Fact]
    public void Jwt_is_signed_with_the_service_account_key()
    {
        var (json, rsa) = ServiceAccountJson();
        using (rsa)
        {
            Assert.True(GscServiceAccount.TryParse(json, out var account, out _));
            var now = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);

            var jwt = GscJwt.Create(account!, GscJwt.ReadOnlyScope, now);

            var parts = jwt.Split('.');
            Assert.Equal(3, parts.Length);
            using var header = JsonDocument.Parse(FromBase64Url(parts[0]));
            Assert.Equal("RS256", header.RootElement.GetProperty("alg").GetString());
            Assert.Equal("kid123", header.RootElement.GetProperty("kid").GetString());
            using var claims = JsonDocument.Parse(FromBase64Url(parts[1]));
            Assert.Equal(account!.ClientEmail, claims.RootElement.GetProperty("iss").GetString());
            Assert.Equal(GscJwt.ReadOnlyScope, claims.RootElement.GetProperty("scope").GetString());
            Assert.Equal(GscServiceAccount.DefaultTokenUri, claims.RootElement.GetProperty("aud").GetString());
            Assert.Equal(1_800_000_000, claims.RootElement.GetProperty("iat").GetInt64());
            Assert.Equal(1_800_003_600, claims.RootElement.GetProperty("exp").GetInt64());

            Assert.True(rsa.VerifyData(Encoding.ASCII.GetBytes(parts[0] + "." + parts[1]), FromBase64Url(parts[2]),
                HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
            Assert.DoesNotContain("=", jwt);
            Assert.DoesNotContain("+", jwt);
            Assert.DoesNotContain("/", jwt);
        }
    }

    private static byte[] FromBase64Url(string value)
    {
        var base64 = value.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(base64.PadRight(base64.Length + (4 - base64.Length % 4) % 4, '='));
    }

    [Fact]
    public void Google_responses_are_parsed_safely()
    {
        Assert.Equal(("ya29.abc", 3599), GscParsers.Token("""{"access_token":"ya29.abc","expires_in":3599,"token_type":"Bearer"}"""));
        Assert.Null(GscParsers.Token("""{"error":"invalid_grant"}""").Token);
        Assert.Null(GscParsers.Token("<html>proxy error</html>").Token);

        var sites = GscParsers.Sites("""{"siteEntry":[{"siteUrl":"sc-domain:fratak.ir","permissionLevel":"siteOwner"},{"siteUrl":"https://blog.ir/","permissionLevel":"siteRestrictedUser"}]}""");
        Assert.Equal(2, sites.Count);
        Assert.Equal(("sc-domain:fratak.ir", "siteOwner"), sites[0]);
        Assert.Empty(GscParsers.Sites("{}"));

        var rows = GscParsers.Rows("""{"rows":[{"keys":["https://fratak.ir/product/a","قیمت a55"],"clicks":3,"impressions":120,"ctr":0.025,"position":7.4},{"clicks":1}],"responseAggregationType":"byPage"}""");
        var row = Assert.Single(rows); // ردیف بدون keys کنار گذاشته می‌شود
        Assert.Equal("قیمت a55", row.Keys[1]);
        Assert.Equal(120, row.Impressions);
        Assert.Equal(7.4, row.Position);
        Assert.Empty(GscParsers.Rows("""{"responseAggregationType":"auto"}""")); // دوره بدون داده
        Assert.Empty(GscParsers.Rows("not json"));

        Assert.Equal("User does not have sufficient permission", GscParsers.Error("""{"error":{"code":403,"message":"User does not have sufficient permission","status":"PERMISSION_DENIED"}}"""));
        Assert.Equal("invalid_grant: Invalid JWT Signature.", GscParsers.Error("""{"error":"invalid_grant","error_description":"Invalid JWT Signature."}"""));
        Assert.Null(GscParsers.Error("""{"rows":[]}"""));
    }

    [Fact]
    public void Google_errors_become_actionable_persian_guidance()
    {
        const string email = "seo-bot@fratak-seo.iam.gserviceaccount.com";

        Assert.Contains("پروکسی", GscErrors.Explain(null, null, "اتصال برقرار نشد", email));
        Assert.Contains(email, GscErrors.Explain(403, """{"error":{"code":403,"message":"User does not have sufficient permission for site"}}""", null, email));
        Assert.Contains("Enable", GscErrors.Explain(403, """{"error":{"code":403,"message":"Google Search Console API has not been used in project 1 before or it is disabled."}}""", null, email));
        Assert.Contains("ساعت سرور", GscErrors.Explain(400, """{"error":"invalid_grant","error_description":"Invalid JWT"}""", null, email));
        Assert.Contains("sc-domain:", GscErrors.Explain(404, null, null, email));
        Assert.Contains("سقف", GscErrors.Explain(429, null, null, email));
        Assert.Contains("موقت", GscErrors.Explain(503, null, null, email));
        Assert.Contains("418", GscErrors.Explain(418, null, null, email));
    }

    [Theory]
    [InlineData("fratak.ir", "sc-domain:fratak.ir")]
    [InlineData("Fratak.ir/", "sc-domain:fratak.ir")]
    [InlineData("sc-domain:Fratak.IR", "sc-domain:fratak.ir")]
    [InlineData("https://Fratak.ir", "https://fratak.ir/")]
    [InlineData("https://www.fratak.ir/shop", "https://www.fratak.ir/shop/")]
    [InlineData("localhost", null)]
    [InlineData("javascript:alert(1)", null)]
    [InlineData("sc-domain:", null)]
    [InlineData("", null)]
    public void Search_console_property_is_normalized(string input, string? expected) =>
        Assert.Equal(expected, SearchConsoleSites.Normalize(input));

    [Fact]
    public void Search_console_property_helpers()
    {
        Assert.Equal("fratak.ir", SearchConsoleSites.Host("sc-domain:fratak.ir"));
        Assert.Equal("fratak.ir", SearchConsoleSites.Host("https://www.fratak.ir/"));
        Assert.Null(SearchConsoleSites.Host(null));
        Assert.Equal("https://fratak.ir/sitemap.xml", SearchConsoleSites.SitemapUrl("sc-domain:fratak.ir"));
        Assert.Equal("https://fratak.ir/sitemap.xml", SearchConsoleSites.SitemapUrl("https://fratak.ir/shop/"));
        Assert.True(SearchConsoleSites.IsValidProxy("socks5://127.0.0.1:1080"));
        Assert.True(SearchConsoleSites.IsValidProxy("http://proxy.local:10809"));
        Assert.False(SearchConsoleSites.IsValidProxy("ftp://proxy.local"));
        Assert.False(SearchConsoleSites.IsValidProxy("127.0.0.1:1080"));
        Assert.False(SearchConsoleSites.IsValidProxy(null));
    }

    [Theory]
    [InlineData(1d, 0.28d)]
    [InlineData(0.4d, 0.28d)]
    [InlineData(2.6d, 0.10d)]
    [InlineData(5d, 0.05d)]
    [InlineData(10d, 0.018d)]
    [InlineData(15d, 0.01d)]
    public void Expected_ctr_drops_with_position(double position, double expected) =>
        Assert.Equal(expected, GscCtr.Expected(position), 6);

    private static GscRow Row(double clicks, double impressions, double position, params string[] keys) =>
        new(keys, clicks, impressions, impressions > 0 ? clicks / impressions : 0, position);

    [Fact]
    public void Daily_rows_are_sorted_and_invalid_dates_skipped()
    {
        var daily = SearchConsoleService.BuildDaily(
        [
            Row(5, 100, 8.123, "2026-10-20"),
            Row(3, 80, 9, "2026-10-18"),
            Row(1, 1, 1, "not-a-date"),
            Row(1, 1, 1),
        ]);

        Assert.Equal(2, daily.Count);
        Assert.Equal(new DateOnly(2026, 10, 18), daily[0].Date);
        Assert.Equal(new DateOnly(2026, 10, 20), daily[1].Date);
        Assert.Equal(100, daily[1].Impressions);
        Assert.Equal(8.12, daily[1].Position);
    }

    [Fact]
    public void Query_stats_merge_urls_of_the_same_page()
    {
        var start = new DateOnly(2026, 9, 26);
        var end = new DateOnly(2026, 10, 23);

        var stats = SearchConsoleService.BuildQueryStats(
        [
            Row(1, 10, 4, "https://fratak.ir/a?x=1", "q"),
            Row(0, 30, 6, "https://www.fratak.ir/a/", "q"),
            Row(2, 20, 3, "https://fratak.ir/a", "  گوشي  "),
            Row(9, 9, 1, "/relative", "q"),
            Row(9, 9, 1, "https://fratak.ir/b", "   "),
        ], start, end);

        Assert.Equal(2, stats.Count);
        var merged = Assert.Single(stats, s => s.Query == "q");
        Assert.Equal("/a", merged.Path);
        Assert.Equal(1, merged.Clicks);
        Assert.Equal(40, merged.Impressions);
        Assert.Equal(5.5, merged.Position); // میانگین وزنی بر اساس نمایش
        Assert.Equal(0.025, merged.Ctr);
        Assert.Equal(start, merged.PeriodStart);
        Assert.Equal(end, merged.PeriodEnd);
        Assert.Contains(stats, s => s.Query == "گوشی"); // ي عربی یکسان شد
    }

    private static SearchConsoleQueryStat Stat(string path, string query, int clicks, int impressions, double position) => new()
    {
        Path = path,
        Query = query,
        Clicks = clicks,
        Impressions = impressions,
        Ctr = impressions > 0 ? (double)clicks / impressions : 0,
        Position = position,
    };

    [Fact]
    public void Opportunities_find_pages_that_can_win_more_clicks()
    {
        var shampooId = Guid.NewGuid();
        var creamId = Guid.NewGuid();
        var entities = new Dictionary<string, SearchConsoleService.PageEntity>(StringComparer.Ordinal)
        {
            ["/product/a"] = new(SeoEntityKind.Product, shampooId, "شامپو گیاهی", "شامپو"),
            ["/product/b"] = new(SeoEntityKind.Product, creamId, "کرم دست", "کرم"),
        };

        var list = SearchConsoleService.FindOpportunities(
        [
            Stat("/product/a", "شامپو گیاهی", 2, 400, 8),   // نزدیک صفحهٔ اول + نرخ کلیک کم
            Stat("/product/b", "لوسیون بدن", 20, 100, 2),   // جایگاه و نرخ کلیک خوب، ولی کلمهٔ کانونی ناهمخوان
            Stat("/product/c", "چیزی", 0, 40, 9),            // نمایش کم: نادیده
        ], entities);

        Assert.Equal(3, list.Count);
        var striking = list[0]; // بیشترین کلیک قابل‌به‌دست‌آوردن اول
        Assert.Equal(GscOpportunityKind.StrikingDistance, striking.Kind);
        Assert.Equal(38, striking.Potential); // 400 × (0.10 − 0.005)
        Assert.Equal(shampooId, striking.EntityId);
        Assert.Equal(SeoEntityKind.Product, striking.EntityKind);
        Assert.Contains("شامپو گیاهی", striking.Advice);

        var lowCtr = Assert.Single(list, o => o.Kind == GscOpportunityKind.LowCtr);
        Assert.Equal(8, lowCtr.Potential); // 400 × (0.025 − 0.005)

        var mismatch = Assert.Single(list, o => o.Kind == GscOpportunityKind.KeywordMismatch);
        Assert.Equal("/product/b", mismatch.Path);
        Assert.Equal("لوسیون بدن", mismatch.Query);
        Assert.Equal(2, mismatch.Potential);
        Assert.DoesNotContain(list, o => o.Path == "/product/c");
    }

    [Fact]
    public void Matching_focus_keyword_is_not_reported_as_mismatch()
    {
        var entities = new Dictionary<string, SearchConsoleService.PageEntity>(StringComparer.Ordinal)
        {
            ["/product/a"] = new(SeoEntityKind.Product, Guid.NewGuid(), "گوشی سامسونگ A55", "گوشی A55"),
        };

        var list = SearchConsoleService.FindOpportunities([Stat("/product/a", "قیمت گوشی a55", 30, 200, 2)], entities);

        Assert.DoesNotContain(list, o => o.Kind == GscOpportunityKind.KeywordMismatch);
    }

    [Fact]
    public void Numbers_are_shown_in_persian()
    {
        Assert.Equal("۳٫۴٪", SearchConsoleService.Percent(0.034));
        Assert.Equal("۰٪", SearchConsoleService.Percent(0));
        Assert.Equal("۷٫۵", PersianText.Decimal1(7.46));
        Assert.Equal("۳", PersianText.Decimal1(3.0));
        Assert.Equal("—", SearchConsoleModel.Position(0));
    }

    [Fact]
    public void Period_change_respects_metric_direction()
    {
        Assert.Equal<(string Text, bool Good)?>(("+۲۰٪", true), SearchConsoleModel.Change(120, 100));
        Assert.Equal<(string Text, bool Good)?>(("−۵۰٪", false), SearchConsoleModel.Change(50, 100));
        Assert.Equal<(string Text, bool Good)?>(("−۱۶٫۷٪", true), SearchConsoleModel.Change(5, 6, lowerIsBetter: true)); // جایگاه کمتر = بهتر
        Assert.Null(SearchConsoleModel.Change(10, 0));
    }

    // ═══ ویرایش گروهی ═══════════════════════════════════════════════════

    private static SeoBulkRow Bulk(string name, string? title = null, string? meta = null, string? focus = null, int? score = null, bool duplicate = false) =>
        new(Guid.NewGuid(), name, "/product/x", title, meta, focus, score, duplicate);

    [Fact]
    public void Bulk_filters_select_the_problem_rows()
    {
        var rows = new List<SeoBulkRow>
        {
            Bulk("کامل", "عنوان", "توضیح", "کلمه", 90),
            Bulk("بی‌عنوان", null, "توضیح", "کلمه", 75),
            Bulk("بی‌توضیح", "عنوان", " ", "کلمه", 60),
            Bulk("بی‌کلمه", "عنوان", "توضیح", null, null, duplicate: true),
        };

        Assert.Equal(4, SeoBulkEditService.Apply(rows, "all").Count());
        Assert.Equal(4, SeoBulkEditService.Apply(rows, null).Count());
        Assert.Equal("بی‌عنوان", Assert.Single(SeoBulkEditService.Apply(rows, "no-title")).Name);
        Assert.Equal("بی‌توضیح", Assert.Single(SeoBulkEditService.Apply(rows, "no-meta")).Name);
        Assert.Equal("بی‌کلمه", Assert.Single(SeoBulkEditService.Apply(rows, "no-keyword")).Name);
        Assert.Equal("بی‌کلمه", Assert.Single(SeoBulkEditService.Apply(rows, "duplicate")).Name);
        // نمرهٔ کمتر از ۷۰ یا هنوز نمره‌دهی‌نشده.
        Assert.Equal(new[] { "بی‌توضیح", "بی‌کلمه" }, SeoBulkEditService.Apply(rows, "low").Select(r => r.Name).ToArray());
        Assert.All(SeoBulkEditService.Filters, f => Assert.NotNull(SeoBulkEditService.Apply(rows, f)));
    }

    [Fact]
    public void Bulk_values_are_cleaned_before_saving()
    {
        Assert.Equal("خرید آنلاین شامپو", SeoBulkEditService.Clean("  خرید   آنلاین\nشامپو "));
        Assert.Null(SeoBulkEditService.Clean("   "));
        Assert.Null(SeoBulkEditService.Clean(null));
    }

    [Fact]
    public void Bulk_replace_is_case_insensitive_and_supports_the_name_placeholder()
    {
        Assert.Equal("خرید آنلاین شامپو", SeoBulkEditService.ReplaceValue("خرید شامپو", "شامپو", "خرید", "خرید آنلاین"));
        Assert.Equal("Order Galaxy A55", SeoBulkEditService.ReplaceValue("Buy Galaxy A55", "A55", "buy", "Order"));
        Assert.Equal("بهترین قیمت شامپو X", SeoBulkEditService.ReplaceValue("بهترین قیمت", "شامپو X", "قیمت", "قیمت {name}"));
        Assert.Equal("شامپو", SeoBulkEditService.ReplaceValue("شامپو | فروشگاه", "x", " | فروشگاه", ""));
        Assert.Null(SeoBulkEditService.ReplaceValue("خرید شامپو", "x", "کرم", "y")); // پیدا نشد
        Assert.Null(SeoBulkEditService.ReplaceValue("خرید شامپو", "x", "خرید", "خرید")); // تغییری ندارد
        Assert.Null(SeoBulkEditService.ReplaceValue(null, "x", "a", "b"));
        Assert.Null(SeoBulkEditService.ReplaceValue("abc", "x", "", "b"));
    }

    // ═══ پیش‌نمایش اشتراک ═══════════════════════════════════════════════

    [Fact]
    public void Share_preview_cuts_long_texts_like_messengers()
    {
        var model = new SharePreviewModel(new string('ا', 80), "توضیح کوتاه", "/assets/brand/og-default.png", "fratak.ir", "/product/a");

        Assert.True(model.TitleTooLong);
        Assert.False(model.DescriptionTooLong);
        var cut = SharePreviewModel.Cut(model.Title, SharePreviewModel.TitleLimit);
        Assert.Equal(SharePreviewModel.TitleLimit, cut.Length);
        Assert.EndsWith("…", cut);
        Assert.Equal("کوتاه", SharePreviewModel.Cut("کوتاه", 10));
    }
}
