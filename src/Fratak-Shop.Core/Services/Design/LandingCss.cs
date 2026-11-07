using System.Text;
using System.Text.RegularExpressions;

namespace Fratak_Shop.Core.Services.Design;

/// <summary>
/// پاک‌سازی و «محدود کردن» CSS صفحه‌ساز (دور ۲۱). CSS خروجی GrapesJS قواعدی مثل <c>* { box-sizing }</c> و <c>body { margin:0 }</c> دارد
/// که اگر مستقیم در صفحه بیاید هدر/فوتر سایت را به هم می‌ریزد؛ این کلاس جلوی هر انتخاب‌گر «.fs-lp» می‌گذارد تا فقط داخل صفحهٔ کمپین اثر کند.
/// <list type="bullet">
/// <item><c>body{margin:0}</c> ← <c>.fs-lp{margin:0}</c>؛ <c>*{…}</c> ← <c>.fs-lp *{…}</c>؛ <c>#i3k .t{…}</c> ← <c>.fs-lp #i3k .t{…}</c></item>
/// <item><c>@media (max-width:480px){…}</c> نگه داشته می‌شود و داخلش هم محدود می‌شود (نسخهٔ موبایل/تبلت صفحه‌ساز).</item>
/// <item><c>@keyframes</c> نگه داشته می‌شود؛ <c>@import</c>، <c>@font-face</c>، <c>@charset</c>، <c>@namespace</c> و هر at-rule ناشناخته حذف می‌شود.</item>
/// <item>امنیت: <c>expression(</c>، <c>javascript:</c>، <c>behavior</c>، <c>-moz-binding</c> و <c>url()</c> بیرونی غیر https حذف؛ «&lt;» هرگز در خروجی نمی‌ماند
/// (بستن تگ style و تزریق HTML ممکن نیست). سقف ۲۰۰ هزار نویسه.</item>
/// </list>
/// محل استفاده: <see cref="LandingPageService"/> هنگام ذخیره (خروجی ذخیره‌شده همیشه امن است؛ صفحهٔ عمومی دوباره پردازش نمی‌کند).
/// </summary>
public static partial class LandingCss
{
    public const string Scope = ".fs-lp";
    public const int MaxLength = 200_000;

    private static readonly string[] Forbidden = ["expression(", "javascript:", "vbscript:", "behavior:", "behaviour:", "-moz-binding", "@import", "\\"];

    /// <summary>CSS امن و محدود به «.fs-lp». ورودی خالی ← رشتهٔ خالی.</summary>
    public static string Sanitize(string? css)
    {
        if (string.IsNullOrWhiteSpace(css))
            return string.Empty;
        if (css.Length > MaxLength)
            css = css[..MaxLength];
        css = CommentRegex().Replace(css, " ").Replace("<", "", StringComparison.Ordinal).Replace(">", " > ", StringComparison.Ordinal);
        var output = new StringBuilder(css.Length + 256);
        ScopeBlock(css, output, depth: 0);
        return output.ToString().Trim();
    }

    private static void ScopeBlock(string css, StringBuilder output, int depth)
    {
        var i = 0;
        while (i < css.Length)
        {
            while (i < css.Length && (char.IsWhiteSpace(css[i]) || css[i] == ';' || css[i] == '}'))
                i++;
            if (i >= css.Length)
                break;

            var open = css.IndexOf('{', i);
            if (css[i] == '@')
            {
                var semicolon = css.IndexOf(';', i);
                // at-rule بدون بلوک (@import …; @charset …;) ← حذف.
                if (open < 0 || (semicolon >= 0 && semicolon < open))
                {
                    i = semicolon < 0 ? css.Length : semicolon + 1;
                    continue;
                }
                var prelude = css[i..open].Trim();
                var close = MatchBrace(css, open);
                var inner = css[(open + 1)..close];
                var name = prelude.Split([' ', '(', '\t', '\n', '\r'], 2)[0].ToLowerInvariant();
                if ((name is "@media" or "@supports") && depth < 3 && SafePrelude(prelude))
                {
                    var nested = new StringBuilder();
                    ScopeBlock(inner, nested, depth + 1);
                    if (nested.Length > 0)
                        output.Append(Squash(prelude)).Append('{').Append(nested).Append('}').Append('\n');
                }
                else if (name is "@keyframes" or "@-webkit-keyframes" && SafePrelude(prelude) && IsSafe(inner))
                {
                    output.Append(Squash(prelude)).Append('{').Append(Squash(inner)).Append('}').Append('\n');
                }
                i = close + 1;
                continue;
            }

            if (open < 0)
                break;
            var selector = css[i..open];
            var end = MatchBrace(css, open);
            var body = css[(open + 1)..end];
            i = end + 1;
            if (body.Contains('{') || !IsSafe(body))
                continue;
            var scoped = ScopeSelectors(selector);
            var declarations = CleanUrls(body).Trim();
            if (scoped.Length == 0 || declarations.Length == 0)
                continue;
            output.Append(scoped).Append('{').Append(Squash(declarations)).Append('}').Append('\n');
        }
    }

    /// <summary>
    /// جلوی هر انتخاب‌گر «.fs-lp» می‌گذارد. مثال: «body, .a:hover» ← «.fs-lp, .fs-lp .a:hover»؛ «html body .x» ← «.fs-lp .x».
    /// انتخاب‌گر خالی یا دارای نویسهٔ غیرمجاز ← حذف.
    /// </summary>
    public static string ScopeSelectors(string selectors)
    {
        var parts = new List<string>();
        foreach (var raw in SplitTopLevel(selectors))
        {
            var s = Squash(raw).Trim();
            if (s.Length == 0 || s.Length > 300 || s.Contains('@'))
                continue;
            s = RootRegex().Replace(s, Scope + " ");
            s = s.Trim();
            if (s.StartsWith(Scope, StringComparison.Ordinal))
            {
                // «.fs-lp» یا «.fs-lp .x» (از body/html آمده)؛ «.fs-lp *» هم درست است.
                parts.Add(s);
                continue;
            }
            parts.Add(Scope + " " + s);
        }
        return string.Join(", ", parts.Distinct(StringComparer.Ordinal));
    }

    /// <summary>آیا متن اعلان‌ها خطرناک است؟ (expression، javascript:، behavior، @import، بک‌اسلش برای دور زدن فیلتر).</summary>
    private static bool IsSafe(string text)
    {
        var lower = text.ToLowerInvariant();
        return !Forbidden.Any(f => lower.Contains(f, StringComparison.Ordinal));
    }

    private static bool SafePrelude(string prelude) => prelude.Length <= 300 && IsSafe(prelude) && !prelude.Contains('{');

    /// <summary>url() فقط مسیر داخلی («/…»)، https یا تصویر data؛ بقیه ← none.</summary>
    private static string CleanUrls(string body) => UrlRegex().Replace(body, m =>
    {
        var url = m.Groups["u"].Value.Trim();
        var ok = (url.StartsWith('/') && !url.StartsWith("//", StringComparison.Ordinal))
                 || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                 || DataImageRegex().IsMatch(url);
        return ok && !url.Contains('(') && !url.Contains(')') && !url.Contains('"') && !url.Contains('\'') ? $"url(\"{url}\")" : "none";
    });

    private static IEnumerable<string> SplitTopLevel(string text)
    {
        var depth = 0;
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            switch (text[i])
            {
                case '(' or '[':
                    depth++;
                    break;
                case ')' or ']':
                    depth = Math.Max(0, depth - 1);
                    break;
                case ',' when depth == 0:
                    yield return text[start..i];
                    start = i + 1;
                    break;
            }
        }
        yield return text[start..];
    }

    private static int MatchBrace(string css, int open)
    {
        var depth = 0;
        for (var i = open; i < css.Length; i++)
        {
            if (css[i] == '{')
                depth++;
            else if (css[i] == '}' && --depth == 0)
                return i;
        }
        return css.Length - 1 < open ? open : css.Length - 1;
    }

    private static string Squash(string text) => WhitespaceRegex().Replace(text, " ").Trim();

    [GeneratedRegex(@"/\*.*?\*/", RegexOptions.Singleline, 500)]
    private static partial Regex CommentRegex();

    [GeneratedRegex(@"\s+", RegexOptions.None, 500)]
    private static partial Regex WhitespaceRegex();

    // html / body / :root در ابتدای انتخاب‌گر (با یا بدون فاصله بعدش) ← .fs-lp
    [GeneratedRegex(@"^(?:(?:html|body|:root)(?![\w-])\s*)+", RegexOptions.IgnoreCase, 500)]
    private static partial Regex RootRegex();

    [GeneratedRegex(@"url\(\s*(?<q>['""]?)(?<u>[^'""\)]*)\k<q>\s*\)", RegexOptions.IgnoreCase, 500)]
    private static partial Regex UrlRegex();

    [GeneratedRegex(@"^data:image/(png|jpeg|jpg|gif|webp|svg\+xml);base64,[A-Za-z0-9+/=]+$", RegexOptions.IgnoreCase, 500)]
    private static partial Regex DataImageRegex();
}
