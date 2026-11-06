using System.Security.Cryptography;
using System.Text;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Fratak_Shop.Data.Entities.SeoEngine;

namespace Fratak_Shop.Core.Services.Seo.Tools;

// ═══════════════════════════════════════════════════════════════════════════
//  یابندهٔ لینک شکسته – بخش‌های بدون دیتابیس (مرحلهٔ هفتم – فاز ۸ب)
//   • SeoLinkExtractor : لینک‌ها (a href) و تصویرها (img src) را از HTML متن صفحه درمی‌آورد.
//   • SeoPaths         : یکسان‌سازی مسیرها (رمزگشایی نامک فارسی، حروف کوچک، بدون / پایانی).
//   • SeoLinkTarget    : تشخیص داخلی/بیرونی بودن لینک و ساخت مسیر کامل از لینک نسبی.
//   • InternalLinkIndex: بررسی لینک داخلی بدون هیچ درخواست اینترنتی (از روی نامک‌های دیتابیس و ریدایرکت‌ها).
//   • SeoLinkRules     : تبدیل نتیجهٔ HTTP لینک بیرونی به نوع مشکل.
//   • SeoLinkRewriter  : جایگزینی یا برداشتن یک لینک در HTML (دکمهٔ «اصلاح» صفحهٔ لینک‌های شکسته).
//  همه ایستا و بدون وابستگی‌اند تا با تست واحد بررسی شوند (tests/Unit/SeoToolsTests.cs).
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// یک لینک یا تصویر استخراج‌شده از متن. Count = تعداد تکرار همین آدرس در متن.
/// مثال: («/product/galaxy-a55»، «گوشی A55»، IsImage=false، Count=2).
/// </summary>
public sealed record ExtractedLink(string Url, string? Anchor, bool IsImage, int Count);

/// <summary>استخراج لینک‌ها و تصویرهای قابل بررسی از HTML.</summary>
public static class SeoLinkExtractor
{
    /// <summary>
    /// همهٔ a[href] و img[src] قابل بررسی (یکتا بر اساس آدرس و نوع). لنگرها (#…)، mailto:، tel:، javascript: و data: کنار گذاشته می‌شوند.
    /// <example>«&lt;a href="/blog/x"&gt;راهنما&lt;/a&gt;&lt;img src="/uploads/a.webp" alt="عکس"&gt;» → دو مورد.</example>
    /// </summary>
    public static List<ExtractedLink> Extract(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return [];
        var document = new HtmlParser().ParseDocument("<html><body>" + html + "</body></html>");
        var found = new Dictionary<(string Url, bool Image), (string? Anchor, int Count)>();

        void Add(string? url, string? anchor, bool image)
        {
            url = url?.Trim();
            if (string.IsNullOrEmpty(url) || !IsCheckable(url) || url.Length > 2048)
                return;
            var key = (url, image);
            found[key] = found.TryGetValue(key, out var existing)
                ? (existing.Anchor ?? Clean(anchor), existing.Count + 1)
                : (Clean(anchor), 1);
        }

        foreach (var link in document.QuerySelectorAll("a[href]"))
            Add(link.GetAttribute("href"), link.TextContent, image: false);
        foreach (var image in document.QuerySelectorAll("img[src]"))
            Add(image.GetAttribute("src"), image.GetAttribute("alt"), image: true);

        return found.Select(f => new ExtractedLink(f.Key.Url, f.Value.Anchor, f.Key.Image, f.Value.Count)).ToList();
    }

    /// <summary>
    /// آیا این آدرس اصلاً صفحه/فایلی است که بشود بررسی کرد؟
    /// مثال: «#faq» و «tel:021…» و «mailto:a@b.ir» → خیر؛ «/product/x»، «https://a.ir/b» و «../x» → بله.
    /// </summary>
    public static bool IsCheckable(string url)
    {
        if (url.StartsWith('#'))
            return false;
        var colon = url.IndexOf(':');
        var slash = url.IndexOf('/');
        // «scheme:» فقط وقتی قبل از اولین / بیاید (مثلاً «/a:b» نسبی است).
        if (colon > 0 && (slash < 0 || colon < slash))
        {
            var scheme = url[..colon].ToLowerInvariant();
            return scheme is "http" or "https";
        }
        return true;
    }

    /// <summary>متن لنگر کوتاه و تمیز (حداکثر ۲۰۰ نویسه).</summary>
    private static string? Clean(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        var clean = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return clean.Length <= 200 ? clean : clean[..199] + "…";
    }
}

/// <summary>یکسان‌سازی مسیرها برای مقایسه (همان قالبی که ریدایرکت‌ها و SeoScore.Path دارند).</summary>
public static class SeoPaths
{
    /// <summary>
    /// مسیر بدون query و # ، رمزگشایی‌شده، با حروف کوچک و بدون / پایانی. مسیر خالی = «/».
    /// <example>«/Product/%D8%B4%D8%A7%D9%85%D9%BE%D9%88/?ref=x» → «/product/شامپو».</example>
    /// </summary>
    public static string Normalize(string? path) => Clean(path, lowercase: true);

    /// <summary>
    /// مثل <see cref="Normalize"/> ولی بدون تغییر حروف بزرگ/کوچک؛ برای مسیر فایل روی دیسک (لینوکس به حروف حساس است).
    /// مثال: «/uploads/products/A%20B.webp» → «/uploads/products/A B.webp».
    /// </summary>
    public static string FilePath(string? path) => Clean(path, lowercase: false);

    private static string Clean(string? path, bool lowercase)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "/";
        var value = path.Trim();
        var cut = value.IndexOfAny(['?', '#']);
        if (cut >= 0)
            value = value[..cut];
        try
        {
            value = Uri.UnescapeDataString(value);
        }
        catch (UriFormatException)
        {
        }
        value = value.Replace('\\', '/');
        while (value.Contains("//", StringComparison.Ordinal))
            value = value.Replace("//", "/", StringComparison.Ordinal);
        value = (lowercase ? value.ToLowerInvariant() : value).TrimEnd('/');
        if (!value.StartsWith('/'))
            value = "/" + value;
        return value.Length == 0 ? "/" : value;
    }

    /// <summary>
    /// مسیر از یک آدرس کامل Search Console یا هر آدرس مطلق. مثال: «https://fratak.ir/blog/%D8%B1/» → «/blog/ر».
    /// آدرس نامعتبر → null.
    /// </summary>
    public static string? FromUrl(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" ? Normalize(uri.AbsolutePath) : null;

    /// <summary>دامنه بدون www و با حروف کوچک. مثال: «WWW.Fratak.ir» → «fratak.ir».</summary>
    public static string BareHost(string host)
    {
        var value = host.Trim().TrimEnd('.').ToLowerInvariant();
        return value.StartsWith("www.", StringComparison.Ordinal) ? value[4..] : value;
    }

    /// <summary>هش SHA-256 هگز کوچک (کلید ایندکس آدرس‌های بلند).</summary>
    public static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}

/// <summary>
/// مقصد یک لینک: داخلی (فقط مسیر) یا بیرونی (آدرس کامل).
/// مثال: «/product/a» یا «https://fratak.ir/product/a» → داخلی «/product/a»؛ «https://digikala.com/x» → بیرونی.
/// </summary>
/// <param name="FilePath">مسیر داخلی با حروف اصلی (برای بررسی فایل روی دیسک).</param>
public sealed record SeoLinkTarget(bool IsInternal, string? Path, Uri? External, bool IsHttp, string? FilePath = null)
{
    /// <summary>
    /// تشخیص مقصد. sourcePath مسیر صفحه‌ای است که لینک داخلش است (برای لینک‌های نسبی مثل «x» یا «../x»).
    /// ownHosts: دامنه‌های خود سایت بدون www (اگر خالی باشد هر آدرس مطلقی بیرونی حساب می‌شود و با HTTP بررسی می‌شود).
    /// آدرس نامعتبر → null.
    /// </summary>
    public static SeoLinkTarget? Classify(string url, string sourcePath, IReadOnlySet<string> ownHosts)
    {
        var value = url.Trim();
        if (value.StartsWith("//", StringComparison.Ordinal))
            value = "https:" + value;

        if (Uri.TryCreate(value, UriKind.Absolute, out var absolute) && absolute.Scheme is "http" or "https")
        {
            if (string.IsNullOrEmpty(absolute.Host))
                return null;
            var http = absolute.Scheme == "http";
            return ownHosts.Contains(SeoPaths.BareHost(absolute.Host))
                ? new SeoLinkTarget(true, SeoPaths.Normalize(absolute.AbsolutePath), null, http, SeoPaths.FilePath(absolute.AbsolutePath))
                : new SeoLinkTarget(false, null, absolute, http);
        }

        // نسبی: مثل مرورگر نسبت به آدرس صفحهٔ مبدأ ساخته می‌شود.
        var basePath = string.IsNullOrWhiteSpace(sourcePath) ? "/" : sourcePath;
        if (!Uri.TryCreate(new Uri("https://site.invalid"), basePath, out var baseUri)
            || !Uri.TryCreate(baseUri, value, out var resolved))
            return null;
        return new SeoLinkTarget(true, SeoPaths.Normalize(resolved.AbsolutePath), null, false, SeoPaths.FilePath(resolved.AbsolutePath));
    }
}

/// <summary>نتیجهٔ بررسی یک لینک. Problem=None یعنی سالم.</summary>
public sealed record SeoLinkCheck(SeoLinkProblem Problem, string? SuggestedUrl = null, string? Detail = null, int? StatusCode = null)
{
    public static readonly SeoLinkCheck Ok = new(SeoLinkProblem.None);

    public bool IsOk => Problem == SeoLinkProblem.None;
}

/// <summary>
/// فهرست صفحه‌های موجود سایت برای بررسی لینک داخلی بدون درخواست HTTP. همهٔ نامک‌ها با <see cref="SeoPaths.Normalize"/>
/// (حروف کوچک، رمزگشایی‌شده) ذخیره می‌شوند. ساخت: SeoLinkAuditService.BuildIndexAsync.
/// </summary>
public sealed class InternalLinkIndex
{
    /// <summary>مسیرهایی که بررسی نمی‌شوند (خصوصی یا پویا). لینک به آن‌ها همیشه سالم حساب می‌شود.</summary>
    private static readonly string[] SkippedPrefixes = ["/admin", "/api", "/profile", "/checkout", "/payment", "/pay/", "/login", "/hubs", "/_"];

    /// <summary>پوشه‌های فایل (تصویر و دارایی‌ها): وجود فایل روی دیسک بررسی می‌شود.</summary>
    private static readonly string[] FilePrefixes = ["/uploads/", "/assets/", "/images/", "/media/", "/lib/", "/fonts/"];

    public HashSet<string> Products { get; init; } = new(StringComparer.Ordinal);
    public HashSet<string> InactiveProducts { get; init; } = new(StringComparer.Ordinal);
    public HashSet<string> Categories { get; init; } = new(StringComparer.Ordinal);
    public HashSet<string> Brands { get; init; } = new(StringComparer.Ordinal);
    public HashSet<string> Posts { get; init; } = new(StringComparer.Ordinal);
    public HashSet<string> BlogCategories { get; init; } = new(StringComparer.Ordinal);
    public HashSet<string> Pages { get; init; } = new(StringComparer.Ordinal);

    /// <summary>ریدایرکت‌های فعال: مسیر مبدأ (یکسان‌شده) → آدرس مقصد همان‌طور که ثبت شده.</summary>
    public Dictionary<string, string> Redirects { get; init; } = new(StringComparer.Ordinal);

    /// <summary>مسیرهای ثابت سایت (بدون پارامتر)؛ مثل «/»، «/blog»، «/cart»، «/sitemap.xml».</summary>
    public HashSet<string> StaticPaths { get; init; } = new(StringComparer.Ordinal);

    /// <summary>
    /// بررسی یک مسیر داخلی. اگر مسیر فایل باشد و باید روی دیسک بررسی شود، null برمی‌گردد.
    /// <example>
    /// «/product/galaxy-a55» (موجود) → سالم · «/product/old» با ریدایرکت → Redirected با مقصد ·
    /// «/blog/حذف‌شده» → NotFound · «/uploads/a.webp» → null (بررسی فایل).
    /// </example>
    /// </summary>
    public SeoLinkCheck? Check(string rawPath)
    {
        var path = SeoPaths.Normalize(rawPath);
        if (StaticPaths.Contains(path))
            return SeoLinkCheck.Ok;
        if (SkippedPrefixes.Any(p => path.StartsWith(p, StringComparison.Ordinal)))
            return SeoLinkCheck.Ok;

        if (Redirects.ContainsKey(path))
        {
            // زنجیرهٔ ریدایرکت تا مقصد نهایی (حداکثر ۵ قدم، بدون حلقه).
            var target = Redirects[path];
            var seen = new HashSet<string>(StringComparer.Ordinal) { path };
            for (var i = 0; i < 5; i++)
            {
                // مقصد بیرونی (آدرس کامل) پایان زنجیره است. «/…» روی لینوکس هم Uri مطلق file:// می‌شود؛ پس طرح بررسی می‌شود.
                if (Uri.TryCreate(target, UriKind.Absolute, out var absolute) && absolute.Scheme is "http" or "https")
                    break;
                var next = SeoPaths.Normalize(target);
                if (!seen.Add(next) || !Redirects.TryGetValue(next, out var further))
                    break;
                target = further;
            }
            return new SeoLinkCheck(SeoLinkProblem.Redirected, target, "این آدرس ریدایرکت دارد؛ لینک مستقیم به آدرس نهایی بهتر است");
        }

        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 2)
        {
            var slug = segments[1];
            switch (segments[0])
            {
                case "product":
                    if (Products.Contains(slug)) return SeoLinkCheck.Ok;
                    return new SeoLinkCheck(SeoLinkProblem.NotFound, null,
                        InactiveProducts.Contains(slug) ? "محصول مقصد غیرفعال است (صفحه‌اش ۴۰۴ می‌دهد)" : "محصولی با این نامک وجود ندارد");
                case "category":
                    return Categories.Contains(slug) ? SeoLinkCheck.Ok : new SeoLinkCheck(SeoLinkProblem.NotFound, null, "دسته‌ای با این نامک وجود ندارد یا غیرفعال است");
                case "brand":
                    return Brands.Contains(slug) ? SeoLinkCheck.Ok : new SeoLinkCheck(SeoLinkProblem.NotFound, null, "برندی با این نامک وجود ندارد یا غیرفعال است");
                case "blog":
                    return Posts.Contains(slug) ? SeoLinkCheck.Ok : new SeoLinkCheck(SeoLinkProblem.NotFound, null, "مقالهٔ منتشرشده‌ای با این نامک وجود ندارد");
                case "page":
                    return Pages.Contains(slug) ? SeoLinkCheck.Ok : new SeoLinkCheck(SeoLinkProblem.NotFound, null, "صفحه‌ای با این نامک وجود ندارد یا غیرفعال است");
            }
        }
        if (segments.Length == 3 && segments[0] == "blog" && segments[1] == "category")
            return BlogCategories.Contains(segments[2]) ? SeoLinkCheck.Ok : new SeoLinkCheck(SeoLinkProblem.NotFound, null, "دستهٔ وبلاگی با این نامک وجود ندارد");

        if (FilePrefixes.Any(p => path.StartsWith(p, StringComparison.Ordinal)) || (segments.Length > 0 && segments[^1].Contains('.')))
            return null;

        return new SeoLinkCheck(SeoLinkProblem.NotFound, null, "این آدرس در سایت وجود ندارد");
    }
}

/// <summary>نتیجهٔ خام بررسی HTTP یک لینک بیرونی (از ISeoLinkProbe).</summary>
/// <param name="StatusCode">کد پاسخ نهایی؛ null = اتصال برقرار نشد.</param>
/// <param name="FinalUrl">آدرس نهایی بعد از ریدایرکت‌ها (اگر ریدایرکتی بود).</param>
/// <param name="PermanentRedirect">همهٔ ریدایرکت‌ها دائمی (۳۰۱/۳۰۸) بودند؟</param>
/// <param name="Error">توضیح خطای اتصال. مثال: «مهلت تمام شد».</param>
public sealed record LinkProbeResult(int? StatusCode, string? FinalUrl = null, bool PermanentRedirect = false, string? Error = null);

/// <summary>قانون‌های تبدیل پاسخ HTTP به نوع مشکل لینک.</summary>
public static class SeoLinkRules
{
    /// <summary>
    /// <example>
    /// 404 → NotFound · 410 → Gone · 403 → Blocked (ربات را بسته؛ احتمالاً سالم) · 503 → ServerError ·
    /// null → Unreachable · 200 بعد از ریدایرکت دائمی به آدرس دیگر → Redirected (پیشنهاد: آدرس نهایی) ·
    /// ریدایرکت یک صفحهٔ داخلی به صفحهٔ اصلی همان سایت → NotFound («احتمالاً صفحه حذف شده»).
    /// </example>
    /// </summary>
    public static SeoLinkCheck FromProbe(LinkProbeResult result, Uri original)
    {
        switch (result.StatusCode)
        {
            case null:
                return new SeoLinkCheck(SeoLinkProblem.Unreachable, null, result.Error ?? "اتصال برقرار نشد");
            case 404:
                return new SeoLinkCheck(SeoLinkProblem.NotFound, null, "صفحه وجود ندارد (۴۰۴)", 404);
            case 410:
                return new SeoLinkCheck(SeoLinkProblem.Gone, null, "صفحه برای همیشه حذف شده (۴۱۰)", 410);
            case 401 or 403 or 429 or 999:
                return new SeoLinkCheck(SeoLinkProblem.Blocked, null, "سایت مقصد به ربات‌ها اجازهٔ بررسی نداد؛ یک بار دستی باز کنید", result.StatusCode);
            case >= 500:
                return new SeoLinkCheck(SeoLinkProblem.ServerError, null, $"سرور مقصد خطا داد ({result.StatusCode})", result.StatusCode);
            case >= 400:
                return new SeoLinkCheck(SeoLinkProblem.NotFound, null, $"پاسخ نامعتبر ({result.StatusCode})", result.StatusCode);
        }

        if (result.FinalUrl is not null && Uri.TryCreate(result.FinalUrl, UriKind.Absolute, out var final) && !SameAddress(original, final))
        {
            // «soft 404»: لینک عمیق به صفحهٔ اصلی همان سایت ریدایرکت شده؛ یعنی صفحهٔ اصلی دیگر وجود ندارد.
            if (SeoPaths.Normalize(final.AbsolutePath) == "/" && SeoPaths.Normalize(original.AbsolutePath) != "/"
                && SeoPaths.BareHost(final.Host) == SeoPaths.BareHost(original.Host))
                return new SeoLinkCheck(SeoLinkProblem.NotFound, null, "به صفحهٔ اصلی سایت مقصد ریدایرکت شد (احتمالاً صفحه حذف شده)", result.StatusCode);
            if (result.PermanentRedirect)
                return new SeoLinkCheck(SeoLinkProblem.Redirected, final.AbsoluteUri, "آدرس برای همیشه منتقل شده؛ لینک را به آدرس تازه عوض کنید", result.StatusCode);
        }
        return SeoLinkCheck.Ok;
    }

    /// <summary>دو آدرس یکی‌اند؟ (فرق فقط در http/https، www یا / پایانی مهم نیست.)</summary>
    public static bool SameAddress(Uri a, Uri b) =>
        SeoPaths.BareHost(a.Host) == SeoPaths.BareHost(b.Host)
        && SeoPaths.Normalize(a.AbsolutePath) == SeoPaths.Normalize(b.AbsolutePath)
        && string.Equals(a.Query.TrimStart('?'), b.Query.TrimStart('?'), StringComparison.Ordinal);

    /// <summary>عنوان فارسی هر نوع مشکل (برای پنل و اعلان).</summary>
    public static string Title(SeoLinkProblem problem) => problem switch
    {
        SeoLinkProblem.NotFound => "پیدا نشد (۴۰۴)",
        SeoLinkProblem.Gone => "حذف دائمی (۴۱۰)",
        SeoLinkProblem.Redirected => "ریدایرکت",
        SeoLinkProblem.ServerError => "خطای سرور مقصد",
        SeoLinkProblem.Unreachable => "در دسترس نیست",
        SeoLinkProblem.Insecure => "تصویر ناامن (http)",
        SeoLinkProblem.Blocked => "ربات مسدود شد",
        _ => "سالم",
    };

    /// <summary>
    /// شدت مشکل برای مرتب‌سازی و رنگ: ۳ = جدی (لینک واقعاً خراب)، ۲ = متوسط، ۱ = کم‌اهمیت (احتمالاً سالم).
    /// </summary>
    public static int Severity(SeoLinkProblem problem) => problem switch
    {
        SeoLinkProblem.NotFound or SeoLinkProblem.Gone => 3,
        SeoLinkProblem.ServerError or SeoLinkProblem.Unreachable or SeoLinkProblem.Insecure => 2,
        SeoLinkProblem.Redirected or SeoLinkProblem.Blocked => 1,
        _ => 0,
    };
}

/// <summary>ویرایش لینک‌ها داخل HTML متن صفحه.</summary>
public static class SeoLinkRewriter
{
    /// <summary>
    /// آدرس تازه امن است؟ فقط مسیر داخلی («/…» ولی نه «//») یا آدرس کامل http/https، حداکثر ۲۰۴۸ نویسه، بدون فاصله و کوتیشن.
    /// مثال: «/product/a» و «https://a.ir/b» → بله؛ «javascript:alert(1)» و «//evil.com» → خیر.
    /// </summary>
    public static bool IsSafeUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || url.Length > 2048 || url.Any(c => char.IsWhiteSpace(c) || c is '"' or '\'' or '<' or '>'))
            return false;
        if (url.StartsWith('/'))
            return !url.StartsWith("//", StringComparison.Ordinal) && !url.StartsWith("/\\", StringComparison.Ordinal);
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" && !string.IsNullOrEmpty(uri.Host);
    }

    /// <summary>
    /// همهٔ لینک‌ها (یا تصویرهای) دقیقاً با آدرس <paramref name="url"/> را عوض می‌کند.
    /// newUrl != null → آدرس عوض می‌شود. newUrl == null → لینک برداشته می‌شود ولی متنش می‌ماند (برای تصویر: خود تصویر حذف می‌شود).
    /// خروجی: HTML تازه و تعداد مواردی که عوض شد (۰ = لینک دیگر در متن نیست؛ HTML دست نمی‌خورد).
    /// <example>
    /// Replace("&lt;p&gt;&lt;a href="/old"&gt;متن&lt;/a&gt;&lt;/p&gt;", "/old", false, null) → («&lt;p&gt;متن&lt;/p&gt;», 1)
    /// </example>
    /// </summary>
    public static (string Html, int Changed) Replace(string? html, string url, bool isImage, string? newUrl)
    {
        if (string.IsNullOrEmpty(html))
            return (html ?? "", 0);
        var document = new HtmlParser().ParseDocument("<html><body>" + html + "</body></html>");
        var body = document.Body!;
        var attribute = isImage ? "src" : "href";
        var matches = body.QuerySelectorAll(isImage ? "img[src]" : "a[href]")
            .Where(e => string.Equals(e.GetAttribute(attribute)?.Trim(), url.Trim(), StringComparison.Ordinal))
            .ToList();
        if (matches.Count == 0)
            return (html, 0);

        foreach (var element in matches)
        {
            if (newUrl is not null)
            {
                element.SetAttribute(attribute, newUrl);
            }
            else if (isImage)
            {
                element.Remove();
            }
            else
            {
                // باز کردن لینک: فرزندان (متن، تگ‌های داخلی) جای خود لینک می‌نشینند.
                var parent = element.Parent;
                if (parent is null)
                    continue;
                foreach (var child in element.ChildNodes.ToList())
                    parent.InsertBefore(child, element);
                element.Remove();
            }
        }
        return (body.InnerHtml, matches.Count);
    }
}
