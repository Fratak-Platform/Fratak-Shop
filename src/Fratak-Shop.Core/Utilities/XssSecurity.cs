using System.Net;
using System.Text.RegularExpressions;
using Ganss.Xss;

namespace Fratak_Shop.Core.Utilities;

public static partial class XssSecurity
{
    // Sanitizing is read-only after construction, so a single configured instance is
    // shared (building an HtmlSanitizer per call is expensive).
    private static readonly HtmlSanitizer Sanitizer = CreateSanitizer();

    // محتوای متنی (بلاگ، توضیح محصول، صفحه‌ها) هیچ‌وقت به فرم نیاز ندارد؛ فرمِ جاسازی‌شده فقط راه فیشینگ است
    // (مثلاً فرم «ورود» جعلی وسط یک نوشته). برای همین عناصر فرم از فهرست مجاز پیش‌فرض حذف می‌شوند.
    private static HtmlSanitizer CreateSanitizer()
    {
        var sanitizer = new HtmlSanitizer
        {
            KeepChildNodes = true,
            AllowDataAttributes = true,
        };
        foreach (var tag in new[] { "form", "input", "button", "select", "option", "optgroup", "textarea", "datalist", "keygen", "output", "fieldset", "legend" })
            sanitizer.AllowedTags.Remove(tag);
        sanitizer.AllowedAttributes.Remove("formaction");
        return sanitizer;
    }

    // دور ۲۱ – صفحه‌ساز کمپین: ظاهر صفحه با کلاس‌ها (fs-lp-…) و شناسهٔ خودکار GrapesJS («#i3k9{…}») ساخته می‌شود؛ پاک‌ساز عمومی
    // class و id را حذف می‌کند، پس این نمونهٔ جدا آن دو را نگه می‌دارد (بقیهٔ قواعد همان است: بدون اسکریپت، فرم، iframe و رویداد).
    private static readonly HtmlSanitizer LandingSanitizer = CreateLandingSanitizer();

    private static HtmlSanitizer CreateLandingSanitizer()
    {
        var sanitizer = CreateSanitizer();
        sanitizer.AllowedAttributes.Add("class");
        sanitizer.AllowedAttributes.Add("id");
        // ضد «DOM clobbering»: شناسه‌ای مثل id="FratakModal" می‌تواند متغیر سراسری اسکریپت‌های سایت را بپوشاند؛ فقط الگوی خودکار
        // GrapesJS («i» + حروف کوچک/رقم، مثل i3k9 یا ixp2-2) یا پیشوند «fs-» (لنگر دستی مثل fs-faq) پذیرفته می‌شود.
        sanitizer.PostProcessDom += (_, e) =>
        {
            foreach (var element in e.Document.QuerySelectorAll("[id]").ToList())
            {
                if (!LandingIdRegex().IsMatch(element.Id ?? ""))
                    element.RemoveAttribute("id");
            }
        };
        return sanitizer;
    }

    /// <summary>
    /// پاک‌سازی HTML صفحهٔ کمپین (دور ۲۱): مثل <see cref="SanitizeText"/> ولی class و id امن می‌مانند.
    /// مثال: «&lt;section id="i3k9" class="fs-lp-hero" onclick="…"&gt;» ← «&lt;section id="i3k9" class="fs-lp-hero"&gt;»؛ id="FratakModal" ← حذف.
    /// محل استفاده: Core/Services/Design/LandingPageService.CleanHtml.
    /// </summary>
    public static string SanitizeLanding(this string html) =>
        string.IsNullOrEmpty(html) ? string.Empty : LandingSanitizer.Sanitize(html);

    /// <summary>Removes dangerous markup (scripts, event handlers, javascript: urls) and keeps safe HTML.</summary>
    public static string SanitizeText(this string text)
    {
        return string.IsNullOrEmpty(text) ? string.Empty : Sanitizer.Sanitize(text);
    }

    /// <summary><see cref="SanitizeText"/> for optional values: empty input becomes null.</summary>
    public static string? SanitizeOrNull(this string? text)
    {
        return string.IsNullOrWhiteSpace(text) ? null : Sanitizer.Sanitize(text.Trim());
    }

    /// <summary>Plain text version of HTML (for meta descriptions, JSON-LD, search snippets).</summary>
    public static string ToPlainText(this string? html, int maxLength = 0)
    {
        if (string.IsNullOrWhiteSpace(html))
            return string.Empty;

        var text = WebUtility.HtmlDecode(TagRegex().Replace(html, " "));
        text = WhitespaceRegex().Replace(text, " ").Trim();

        if (maxLength > 0 && text.Length > maxLength)
        {
            var cut = text.LastIndexOf(' ', maxLength - 1);
            text = (cut > maxLength / 2 ? text[..cut] : text[..(maxLength - 1)]).TrimEnd('،', ',', '.', ' ') + "…";
        }

        return text;
    }

    [GeneratedRegex("<[^>]*>", RegexOptions.Compiled, 250)]
    private static partial Regex TagRegex();

    [GeneratedRegex(@"\s+", RegexOptions.Compiled, 250)]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex(@"^(?:i[a-z0-9]{2,16}(?:-[0-9]{1,4})?|fs-[a-z0-9][a-z0-9-]{0,40})$", RegexOptions.CultureInvariant, 250)]
    private static partial Regex LandingIdRegex();
}
