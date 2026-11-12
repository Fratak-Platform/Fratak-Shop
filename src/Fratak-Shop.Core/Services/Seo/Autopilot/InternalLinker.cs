using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Fratak_Shop.Core.Services.Seo.Engine;

namespace Fratak_Shop.Core.Services.Seo.Autopilot;

/// <summary>
/// یک مقصد لینک داخلی: صفحه‌ای که اگر یکی از عبارت‌هایش در متن آمد، همان‌جا به آن لینک می‌دهیم.
/// <para>مثال: <c>new LinkTarget("/category/headphones", ["هدفون", "هندزفری"], 80)</c>.</para>
/// </summary>
/// <param name="Url">آدرس نسبی مقصد.</param>
/// <param name="Phrases">عبارت‌ها به ترتیب ترجیح (اولی دقیق‌ترین). چند عبارت = متن لینک متنوع (مورد ۱۱۸).</param>
/// <param name="Priority">اولویت (بیشتر = مهم‌تر). مثلاً دسته‌های پرفروش و محصولات موجود بالاترند.</param>
public sealed record LinkTarget(string Url, IReadOnlyList<string> Phrases, double Priority);

/// <summary>
/// لینک‌ساز داخلی خودکار (موارد ۱۱۶ تا ۱۲۷ پیشنهاد سئو).
/// <para>چه می‌کند؟ در متن HTML (توضیح محصول، مقاله) اولین جایی که نام یک دسته/محصول/مقالهٔ دیگر آمده را به همان صفحه
/// لینک می‌کند؛ مثل کاری که یک ویراستار حرفه‌ای انجام می‌دهد.</para>
/// <para>قانون‌های حرفه‌ای (برای جلوگیری از اسپم):</para>
/// <list type="bullet">
/// <item>هر مقصد فقط یک بار در صفحه؛ صفحه به خودش لینک نمی‌دهد؛ مقصدی که از قبل در متن لینک شده دوباره لینک نمی‌شود.</item>
/// <item>داخل تیتر، لینک موجود، دکمه، کد و زیرنویس لینک ساخته نمی‌شود.</item>
/// <item>سقف کل لینک‌ها (پیش‌فرض ۶) و حداکثر یک لینک خودکار در هر پاراگراف.</item>
/// <item>فقط کلمهٔ کامل (نه بخشی از کلمهٔ دیگر)؛ «کیف» درون «کیفیت» لینک نمی‌شود.</item>
/// </list>
/// <para>کجا استفاده می‌شود؟ هنگام نمایش صفحهٔ محصول و مقاله (متن ذخیره‌شده دست نمی‌خورد؛ پس با حذف مقصد، لینک هم خودکار
/// برداشته می‌شود و لینک شکسته نمی‌ماند).</para>
/// </summary>
public static class InternalLinker
{
    private static readonly HashSet<string> BlockedAncestors = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "h1", "h2", "h3", "h4", "h5", "h6", "button", "code", "pre", "figcaption", "script", "style", "label", "summary", "th",
    };

    /// <summary>
    /// افزودن لینک‌ها. متن بدون تغییر ← همان رشتهٔ ورودی برگردانده می‌شود.
    /// <example><code>
    /// InternalLinker.Link("&lt;p&gt;این هدفون برای ورزش عالی است&lt;/p&gt;", "/product/x",
    ///     [new("/category/headphones", ["هدفون"], 10)], 6)
    /// // ← "&lt;p&gt;این &lt;a href=\"/category/headphones\"&gt;هدفون&lt;/a&gt; برای ورزش عالی است&lt;/p&gt;"
    /// </code></example>
    /// </summary>
    /// <param name="html">متن HTML.</param>
    /// <param name="currentPath">آدرس همین صفحه (به خودش لینک نمی‌دهد).</param>
    /// <param name="targets">مقصدها.</param>
    /// <param name="maxLinks">سقف لینک خودکار.</param>
    public static string Link(string? html, string currentPath, IReadOnlyList<LinkTarget> targets, int maxLinks)
    {
        if (string.IsNullOrWhiteSpace(html) || targets.Count == 0 || maxLinks <= 0 || html.Length > HtmlSeoFixer.MaxInputLength)
            return html ?? "";

        var document = new HtmlParser().ParseDocument("<!DOCTYPE html><html><body>" + html + "</body></html>");
        var body = document.Body!;

        var existing = body.QuerySelectorAll("a[href]")
            .Select(a => NormalizeUrl(a.GetAttribute("href")!))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var current = NormalizeUrl(currentPath);
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var linkedBlocks = new HashSet<INode>();
        var added = 0;

        var ordered = targets
            .Where(t => !string.IsNullOrWhiteSpace(t.Url) && t.Url.StartsWith('/'))
            .Where(t => !string.Equals(NormalizeUrl(t.Url), current, StringComparison.OrdinalIgnoreCase))
            .Where(t => !existing.Contains(NormalizeUrl(t.Url)))
            .OrderByDescending(t => t.Priority)
            .ThenByDescending(t => t.Phrases.Count == 0 ? 0 : t.Phrases.Max(p => p.Length)) // عبارت بلندتر = دقیق‌تر
            .ToList();

        foreach (var target in ordered)
        {
            if (added >= maxLinks)
                break;
            if (!used.Add(NormalizeUrl(target.Url)))
                continue;

            var linked = false;
            foreach (var phrase in target.Phrases.Where(p => p.Trim().Length >= 3).Select(p => p.Trim()))
            {
                foreach (var node in TextNodes(body))
                {
                    var block = Block(node);
                    if (block is not null && linkedBlocks.Contains(block))
                        continue;
                    var index = FindWhole(node.TextContent, phrase);
                    if (index < 0)
                        continue;

                    Wrap(document, node, index, phrase.Length, target.Url);
                    if (block is not null)
                        linkedBlocks.Add(block);
                    linked = true;
                    break;
                }

                if (linked)
                    break;
            }

            if (linked)
                added++;
        }

        return added == 0 ? html : body.InnerHtml;
    }

    /// <summary>
    /// جای اولین «کلمهٔ کامل» عبارت در متن (نیم‌فاصله و فاصله یکسان دیده می‌شوند). نبود ← ۱-.
    /// مثال: <c>FindWhole("کیفیت کیف خوب", "کیف") == 6</c>.
    /// </summary>
    public static int FindWhole(string text, string phrase)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(phrase))
            return -1;
        var start = 0;
        while (start <= text.Length - phrase.Length)
        {
            var index = text.IndexOf(phrase, start, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
                return -1;
            var before = index == 0 ? ' ' : text[index - 1];
            var afterIndex = index + phrase.Length;
            var after = afterIndex >= text.Length ? ' ' : text[afterIndex];
            if (!IsWordChar(before) && !IsWordChar(after))
                return index;
            start = index + 1;
        }

        return -1;
    }

    /// <summary>حرف یا رقم یا نیم‌فاصله (نیم‌فاصله جزء کلمه است: «کیف‌ها» یک کلمه است). ویرگول/نقطه‌ویرگول/علامت سؤال فارسی جزء کلمه نیستند.</summary>
    private static bool IsWordChar(char ch) =>
        ch == '\u200C' || char.IsLetterOrDigit(ch) || PersianText.IsPersianLetter(ch) && ch is not ('\u060C' or '\u061F' or '\u061B' or '\u06D4');

    private static IEnumerable<INode> TextNodes(IElement body) =>
        body.Descendants()
            .Where(n => n.NodeType == NodeType.Text && n.TextContent.Trim().Length >= 3)
            .Where(n => !HasBlockedAncestor(n))
            .ToList();

    private static bool HasBlockedAncestor(INode node)
    {
        for (var parent = node.ParentElement; parent is not null; parent = parent.ParentElement)
        {
            if (BlockedAncestors.Contains(parent.LocalName))
                return true;
        }

        return false;
    }

    /// <summary>پاراگراف/آیتم فهرستی که متن در آن است (برای قانون «یک لینک در هر پاراگراف»).</summary>
    private static INode? Block(INode node)
    {
        for (var parent = node.ParentElement; parent is not null; parent = parent.ParentElement)
        {
            if (parent.LocalName is "p" or "li" or "td" or "blockquote" or "div" or "body")
                return parent;
        }

        return null;
    }

    private static void Wrap(IDocument document, INode textNode, int index, int length, string url)
    {
        var text = textNode.TextContent;
        var parent = textNode.Parent!;
        var before = document.CreateTextNode(text[..index]);
        var link = document.CreateElement("a");
        link.SetAttribute("href", url);
        link.TextContent = text.Substring(index, length);
        var after = document.CreateTextNode(text[(index + length)..]);
        parent.InsertBefore(before, textNode);
        parent.InsertBefore(link, textNode);
        parent.InsertBefore(after, textNode);
        parent.RemoveChild(textNode);
    }

    private static string NormalizeUrl(string url)
    {
        var value = url.Trim();
        if (Uri.TryCreate(value, UriKind.Absolute, out var absolute) && absolute.Scheme is "http" or "https")
            value = absolute.AbsolutePath;
        var cut = value.IndexOfAny(['?', '#']);
        if (cut >= 0)
            value = value[..cut];
        value = Uri.UnescapeDataString(value);
        return value.Length > 1 ? value.TrimEnd('/') : value;
    }
}
