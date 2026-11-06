using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;
using Fratak_Shop.Core.Services.Seo.Engine;

namespace Fratak_Shop.Core.Services.Seo.Autopilot;

/// <summary>
/// اطلاعات صفحه‌ای که HTML آن مرتب می‌شود (برای ساخت Alt و تشخیص لینک داخلی).
/// مثال: <c>new HtmlFixContext("هدفون بی‌سیم سونی", "shop.fratak.ir")</c>.
/// </summary>
/// <param name="PageTitle">نام صفحه (برای Alt تصویرهایی که هیچ نشانه‌ای ندارند).</param>
/// <param name="Host">دامنهٔ سایت؛ لینک مطلق به همین دامنه نسبی می‌شود. خالی ← این کار انجام نمی‌شود.</param>
public sealed record HtmlFixContext(string PageTitle, string? Host = null);

/// <summary>نتیجهٔ مرتب‌سازی: HTML تازه و فهرست کارهای انجام‌شده به فارسی (برای تاریخچهٔ سئو).</summary>
public sealed record HtmlFixResult(string Html, IReadOnlyList<string> Changes)
{
    /// <summary>آیا چیزی عوض شد؟</summary>
    public bool Changed => Changes.Count > 0;
}

/// <summary>
/// مرتب‌کنندهٔ HTML محتوایی که ادمین یا نویسنده در ویرایشگر وارد می‌کند (توضیحات محصول، متن مقاله، صفحه‌های محتوا).
/// <para>چرا؟ متنی که از Word/سایت دیگر کپی می‌شود پر از کامنت، استایل اضافه، تیتر H1 دوم، تصویر بدون Alt و لینک خالی
/// است؛ این‌ها هم برای ابزارهای سئو خطا حساب می‌شوند و هم صفحه را سنگین می‌کنند (موارد ۳۶ تا ۵۵ پیشنهاد سئو).</para>
/// <para>اصل مهم: فقط چیزهایی عوض می‌شود که «قطعاً» بهتر است؛ متن نوشته‌شده هرگز تغییر نمی‌کند. نتیجه با همان ورودی
/// همیشه یکسان است (اجرای دوباره چیزی را عوض نمی‌کند).</para>
/// <para>کجا استفاده می‌شود؟ <c>SeoAutopilotService</c> بعد از هر ذخیره و کار شبانهٔ «مرتب‌سازی HTML قدیمی‌ها».</para>
/// <example>
/// <code>
/// var result = HtmlSeoFixer.Fix("&lt;!-- x --&gt;&lt;h1&gt;معرفی&lt;/h1&gt;&lt;img src='/a.webp'&gt;", new("هدفون سونی"));
/// // result.Html == "&lt;h2&gt;معرفی&lt;/h2&gt;&lt;img src=\"/a.webp\" alt=\"هدفون سونی – معرفی\" loading=\"lazy\" decoding=\"async\"&gt;"
/// </code>
/// </example>
/// </summary>
public static class HtmlSeoFixer
{
    /// <summary>ورودی بزرگ‌تر از این دست نمی‌خورد (حفاظت از پردازنده).</summary>
    public const int MaxInputLength = 400_000;

    private static readonly string[] JunkStyleProperties =
    [
        "font-family", "font-size", "line-height", "mso-", "font-variant", "letter-spacing", "word-spacing",
        "text-indent", "margin", "padding", "tab-stops", "page-break", "white-space", "font-weight", "font-style",
    ];

    private static readonly Regex HiddenStyle = new(
        @"display\s*:\s*none|visibility\s*:\s*hidden|font-size\s*:\s*0(px|pt|em|rem|%)?\s*(;|$)|opacity\s*:\s*0(\.0+)?\s*(;|$)|text-indent\s*:\s*-\d{3,}",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200));

    private static readonly HashSet<string> LinkFreeAncestors = new(StringComparer.OrdinalIgnoreCase) { "A", "BUTTON" };

    /// <summary>
    /// مرتب‌سازی کامل. ورودی خالی/خیلی بزرگ ← همان ورودی بدون تغییر.
    /// </summary>
    public static HtmlFixResult Fix(string? html, HtmlFixContext context)
    {
        if (string.IsNullOrWhiteSpace(html) || html.Length > MaxInputLength)
            return new HtmlFixResult(html ?? "", []);

        var document = new HtmlParser().ParseDocument("<!DOCTYPE html><html><body>" + html + "</body></html>");
        var body = document.Body!;
        var changes = new List<string>();

        Count(changes, RemoveComments(body), "کامنت کد حذف شد");
        Count(changes, RemoveHiddenText(body), "متن پنهان (ریسک جریمهٔ گوگل) حذف شد");
        Count(changes, RemoveDangerous(body), "اسکریپت/استایل درون متن حذف شد");
        Count(changes, CleanStyles(body), "استایل اضافهٔ کپی‌شده پاک شد");
        Count(changes, UnwrapWordJunk(body), "برچسب اضافهٔ Word/فونت حذف شد");
        Count(changes, SemanticTags(body), "برچسب b/i به strong/em تبدیل شد");
        Count(changes, DemoteH1(body), "تیتر H1 دوم به H2 تبدیل شد (هر صفحه یک H1)");
        Count(changes, FixHeadingOrder(body), "ترتیب تیترها درست شد (پرش از H2 به H4 و ...)");
        Count(changes, RemoveEmptyHeadings(body), "تیتر خالی حذف شد");
        Count(changes, FixImages(body, context.PageTitle), "Alt و بارگذاری تنبل تصویر اضافه شد");
        Count(changes, FixLinks(body, context.Host), "لینک خالی/ناامن درست شد");
        Count(changes, FixFrames(body, context.PageTitle), "ویدئوی جاسازی‌شده عنوان و بارگذاری تنبل گرفت");
        Count(changes, FixTables(body), "سرستون جدول مشخص شد");
        Count(changes, RemoveEmptyParagraphs(body), "پاراگراف خالی حذف شد");
        Count(changes, CollapseBreaks(body), "خط خالی پشت‌سرهم کم شد");
        Count(changes, NormalizeLetters(body), "حروف عربی (ي/ك) به فارسی تبدیل شد");

        if (changes.Count == 0)
            return new HtmlFixResult(html, []);

        var output = body.InnerHtml.Trim();
        return new HtmlFixResult(output, changes);
    }

    /// <summary>فقط حذف کامنت‌ها (برای HTML خروجی صفحه و کدهای سفارشی سربرگ/پابرگ).</summary>
    public static string StripComments(string? html)
    {
        if (string.IsNullOrEmpty(html) || !html.Contains("<!--", StringComparison.Ordinal))
            return html ?? "";
        var document = new HtmlParser().ParseDocument("<!DOCTYPE html><html><body>" + html + "</body></html>");
        return RemoveComments(document.Body!) > 0 ? document.Body!.InnerHtml : html;
    }

    private static void Count(List<string> changes, int count, string message)
    {
        if (count > 0)
            changes.Add(count == 1 ? message : $"{message} ({PersianText.Digits(count)} مورد)");
    }

    // ── کامنت و متن پنهان ───────────────────────────────────────────────

    private static int RemoveComments(IElement root)
    {
        var comments = root.Descendants().Where(n => n.NodeType == NodeType.Comment).ToList();
        foreach (var comment in comments)
            comment.Parent?.RemoveChild(comment);
        return comments.Count;
    }

    private static int RemoveHiddenText(IElement root)
    {
        var count = 0;
        foreach (var element in root.QuerySelectorAll("[style],[hidden]").ToList())
        {
            if (element.Parent is null)
                continue;
            var hidden = element.HasAttribute("hidden");
            if (!hidden)
            {
                try
                {
                    hidden = HiddenStyle.IsMatch(element.GetAttribute("style") ?? "");
                }
                catch (RegexMatchTimeoutException)
                {
                    hidden = false;
                }
            }

            if (!hidden)
                continue;
            element.Remove();
            count++;
        }

        return count;
    }

    private static int RemoveDangerous(IElement root)
    {
        var count = 0;
        foreach (var element in root.QuerySelectorAll("script,style,link,meta,base,noscript").ToList())
        {
            // داده‌های ساختاریافتهٔ دستی (JSON-LD) دست نمی‌خورد؛ بقیه در متن محتوا جایی ندارند.
            if (element.LocalName == "script" && string.Equals(element.GetAttribute("type"), "application/ld+json", StringComparison.OrdinalIgnoreCase))
                continue;
            element.Remove();
            count++;
        }

        return count;
    }

    // ── زباله‌های Word و استایل ─────────────────────────────────────────

    private static int UnwrapWordJunk(IElement root)
    {
        var count = 0;
        foreach (var element in root.Descendants<IElement>().ToList())
        {
            var name = element.LocalName;
            if (name.Contains(':') || name == "font")
            {
                Unwrap(element);
                count++;
                continue;
            }

            var cls = element.GetAttribute("class");
            if (cls is not null && cls.Contains("Mso", StringComparison.Ordinal))
            {
                element.RemoveAttribute("class");
                count++;
            }

            if (name == "span")
            {
                foreach (var attribute in element.Attributes.Where(a => a.Name.StartsWith("lang", StringComparison.OrdinalIgnoreCase)).ToList())
                    element.RemoveAttribute(attribute.Name);
            }

            // span بدون هیچ ویژگی ← فقط شلوغی
            if (name == "span" && element.Attributes.Length == 0)
            {
                Unwrap(element);
                count++;
                continue;
            }
        }

        return count;
    }

    private static int CleanStyles(IElement root)
    {
        var count = 0;
        foreach (var element in root.QuerySelectorAll("[style]").ToList())
        {
            var style = element.GetAttribute("style") ?? "";
            var kept = style.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(rule =>
                {
                    var property = rule.Split(':', 2)[0].Trim().ToLowerInvariant();
                    if (JunkStyleProperties.Any(junk => property.StartsWith(junk, StringComparison.Ordinal)))
                        return false;
                    var value = rule.Contains(':') ? rule.Split(':', 2)[1].Trim().ToLowerInvariant() : "";
                    // رنگ متن سیاه/پس‌زمینهٔ سفید یا شفاف = همان پیش‌فرض قالب؛ فقط با حالت تیره تداخل دارد.
                    if (property == "color" && value is "black" or "#000" or "#000000" or "rgb(0, 0, 0)" or "windowtext")
                        return false;
                    if (property is "background" or "background-color" && value is "white" or "#fff" or "#ffffff" or "transparent" or "rgb(255, 255, 255)")
                        return false;
                    return true;
                })
                .ToList();

            var cleaned = string.Join("; ", kept);
            var original = string.Join("; ", style.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            if (cleaned == original)
                continue;
            if (cleaned.Length == 0)
                element.RemoveAttribute("style");
            else
                element.SetAttribute("style", cleaned);
            count++;
        }

        return count;
    }

    private static int SemanticTags(IElement root)
    {
        var count = 0;
        foreach (var element in root.QuerySelectorAll("b,i").ToList())
        {
            // <i class="ph ..."> آیکون است، نه تأکید.
            if (element.LocalName == "i" && element.HasAttribute("class"))
                continue;
            Rename(element, element.LocalName == "b" ? "strong" : "em");
            count++;
        }

        return count;
    }

    // ── تیترها ───────────────────────────────────────────────────────────

    private static int DemoteH1(IElement root)
    {
        var count = 0;
        foreach (var h1 in root.QuerySelectorAll("h1").ToList())
        {
            Rename(h1, "h2");
            count++;
        }

        return count;
    }

    /// <summary>
    /// تیترها پشت‌سرهم و بدون پرش: H2 → H4 می‌شود H2 → H3. اولین تیتر متن هم حداقل H2 است (H1 مال قالب صفحه است).
    /// </summary>
    private static int FixHeadingOrder(IElement root)
    {
        var count = 0;
        var previous = 1;
        foreach (var heading in root.QuerySelectorAll("h2,h3,h4,h5,h6").ToList())
        {
            var level = heading.LocalName[1] - '0';
            var allowed = Math.Min(level, previous + 1);
            if (allowed != level)
            {
                heading.Replace(Renamed(heading, "h" + allowed));
                count++;
            }

            previous = allowed;
        }

        return count;
    }

    private static int RemoveEmptyHeadings(IElement root)
    {
        var count = 0;
        foreach (var heading in root.QuerySelectorAll("h1,h2,h3,h4,h5,h6").ToList())
        {
            if (!string.IsNullOrWhiteSpace(heading.TextContent.Replace('\u00A0', ' ')) || heading.QuerySelector("img") is not null)
                continue;
            heading.Remove();
            count++;
        }

        return count;
    }

    // ── تصویر، لینک، ویدئو، جدول ────────────────────────────────────────

    /// <summary>
    /// Alt خالی ← از (به ترتیب) زیرنویس figure، ویژگی title، نزدیک‌ترین تیتر قبلی، نام صفحه + شماره.
    /// همهٔ تصویرهای متن lazy می‌شوند (تصویر اصلی صفحه بیرون از متن است و lazy نیست).
    /// </summary>
    private static int FixImages(IElement root, string pageTitle)
    {
        var count = 0;
        var index = 0;
        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var img in root.QuerySelectorAll("img").ToList())
        {
            index++;
            var changed = false;
            var src = img.GetAttribute("src");
            if (string.IsNullOrWhiteSpace(src) && string.IsNullOrWhiteSpace(img.GetAttribute("srcset")))
            {
                img.Remove(); // تصویر بدون آدرس فقط خطای ۴۰۴ می‌سازد
                count++;
                continue;
            }

            var alt = img.GetAttribute("alt");
            if (string.IsNullOrWhiteSpace(alt) || LooksLikeFileName(alt))
            {
                var made = MakeAlt(img, pageTitle, index);
                // دو تصویر با Alt یکسان ← شماره می‌گیرند تا تکراری نباشند.
                if (!used.Add(made))
                    made = $"{made} – تصویر {PersianText.Digits(index)}";
                used.Add(made);
                img.SetAttribute("alt", made);
                changed = true;
            }
            else
            {
                used.Add(alt.Trim());
            }

            if (!img.HasAttribute("loading"))
            {
                img.SetAttribute("loading", "lazy");
                changed = true;
            }

            if (!img.HasAttribute("decoding"))
            {
                img.SetAttribute("decoding", "async");
                changed = true;
            }

            if (changed)
                count++;
        }

        return count;
    }

    private static bool LooksLikeFileName(string alt) =>
        Regex.IsMatch(alt.Trim(), @"^(img|image|dsc|photo|screenshot)?[-_ ]?\d*\.?(jpe?g|png|webp|gif)?$|\.(jpe?g|png|webp|gif|avif)$",
            RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100));

    private static string MakeAlt(IElement img, string pageTitle, int index)
    {
        var caption = img.Closest("figure")?.QuerySelector("figcaption")?.TextContent;
        if (Clean(caption) is { Length: > 2 } fromCaption)
            return Cut(fromCaption);
        if (Clean(img.GetAttribute("title")) is { Length: > 2 } fromTitle)
            return Cut(fromTitle);

        var heading = PreviousHeading(img);
        var title = Clean(pageTitle);
        if (heading is { Length: > 2 })
            return Cut(title.Length > 0 && !heading.Contains(title, StringComparison.Ordinal) ? $"{title} – {heading}" : heading);
        return title.Length > 0
            ? (index == 1 ? Cut(title) : Cut($"{title} – تصویر {PersianText.Digits(index)}"))
            : $"تصویر {PersianText.Digits(index)}";
    }

    private static string? PreviousHeading(IElement element)
    {
        // از خود عنصر به عقب در ترتیب سند می‌رویم تا اولین تیتر.
        var all = element.Owner!.Body!.QuerySelectorAll("h2,h3,h4,h5,h6,img").ToList();
        var position = all.IndexOf(element);
        for (var i = position - 1; i >= 0; i--)
        {
            if (all[i].LocalName != "img")
                return Clean(all[i].TextContent);
        }

        return null;
    }

    private static int FixLinks(IElement root, string? host)
    {
        var count = 0;
        foreach (var link in root.QuerySelectorAll("a").ToList())
        {
            var href = (link.GetAttribute("href") ?? "").Trim();
            if (href.Length == 0 || href == "#" || href.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase))
            {
                Unwrap(link); // لینک خالی برای ربات «لینک شکسته» است
                count++;
                continue;
            }

            var changed = false;
            if (!string.IsNullOrWhiteSpace(host) && Uri.TryCreate(href, UriKind.Absolute, out var uri)
                && uri.Scheme is "http" or "https"
                && (string.Equals(uri.Host, host, StringComparison.OrdinalIgnoreCase) || string.Equals(uri.Host, "www." + host, StringComparison.OrdinalIgnoreCase)))
            {
                // لینک مطلق به خود سایت ← نسبی (با تغییر دامنه/https نمی‌شکند و ریدایرکت اضافه نمی‌سازد)
                link.SetAttribute("href", uri.PathAndQuery + uri.Fragment);
                href = uri.PathAndQuery + uri.Fragment;
                changed = true;
            }

            if (string.Equals(link.GetAttribute("target"), "_blank", StringComparison.OrdinalIgnoreCase))
            {
                var rel = (link.GetAttribute("rel") ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
                var before = rel.Count;
                if (!rel.Contains("noopener", StringComparer.OrdinalIgnoreCase)) rel.Add("noopener");
                if (rel.Count != before)
                {
                    link.SetAttribute("rel", string.Join(' ', rel));
                    changed = true;
                }
            }

            // لینک تودرتو (a داخل a) در HTML نامعتبر است
            if (link.ParentElement?.Closest("a") is not null)
            {
                Unwrap(link);
                changed = true;
            }

            if (changed)
                count++;
        }

        return count;
    }

    private static int FixFrames(IElement root, string pageTitle)
    {
        var count = 0;
        foreach (var frame in root.QuerySelectorAll("iframe").ToList())
        {
            var changed = false;
            if (string.IsNullOrWhiteSpace(frame.GetAttribute("title")))
            {
                frame.SetAttribute("title", Cut("ویدئوی " + (Clean(pageTitle) is { Length: > 0 } t ? t : "معرفی")));
                changed = true;
            }

            if (!frame.HasAttribute("loading"))
            {
                frame.SetAttribute("loading", "lazy");
                changed = true;
            }

            if (changed)
                count++;
        }

        return count;
    }

    private static int FixTables(IElement root)
    {
        var count = 0;
        foreach (var table in root.QuerySelectorAll("table").OfType<IHtmlTableElement>().ToList())
        {
            if (table.Head is not null)
                continue;
            var first = table.Rows.FirstOrDefault();
            if (first is null || first.Cells.Length == 0 || first.Cells.Any(c => c.LocalName != "th"))
                continue;
            var head = table.Owner!.CreateElement("thead");
            first.Parent!.RemoveChild(first);
            head.AppendChild(first);
            table.Prepend(head);
            count++;
        }

        return count;
    }

    private static int RemoveEmptyParagraphs(IElement root)
    {
        var count = 0;
        foreach (var paragraph in root.QuerySelectorAll("p,div").ToList())
        {
            if (paragraph.Children.Any(c => c.LocalName is not "br"))
                continue;
            if (!string.IsNullOrWhiteSpace(paragraph.TextContent.Replace('\u00A0', ' ')))
                continue;
            paragraph.Remove();
            count++;
        }

        return count;
    }

    private static int CollapseBreaks(IElement root)
    {
        var count = 0;
        foreach (var br in root.QuerySelectorAll("br").ToList())
        {
            if (br.Parent is null)
                continue;
            // بیش از دو <br> پشت‌سرهم (با فاصلهٔ خالی بینشان) ← دو تا
            var run = new List<INode> { br };
            var next = br.NextSibling;
            while (next is not null && (next is IElement { LocalName: "br" } || next.NodeType == NodeType.Text && string.IsNullOrWhiteSpace(next.TextContent)))
            {
                if (next is IElement)
                    run.Add(next);
                next = next.NextSibling;
            }

            if (run.Count <= 2)
                continue;
            foreach (var extra in run.Skip(2))
                extra.Parent?.RemoveChild(extra);
            count++;
        }

        return count;
    }

    private static int NormalizeLetters(IElement root)
    {
        var count = 0;
        foreach (var text in root.Descendants().Where(n => n.NodeType == NodeType.Text).ToList())
        {
            if (text.Parent is IElement { LocalName: "script" or "code" or "pre" })
                continue;
            var value = text.TextContent;
            if (value.IndexOf('ي') < 0 && value.IndexOf('ك') < 0)
                continue;
            text.TextContent = value.Replace('ي', 'ی').Replace('ك', 'ک');
            count++;
        }

        return count;
    }

    // ── ابزارهای کوچک ─────────────────────────────────────────────────

    private static void Unwrap(IElement element)
    {
        var parent = element.Parent;
        if (parent is null)
            return;
        while (element.FirstChild is { } child)
            parent.InsertBefore(child, element);
        parent.RemoveChild(element);
    }

    private static void Rename(IElement element, string name) => element.Replace(Renamed(element, name));

    private static IElement Renamed(IElement element, string name)
    {
        var copy = element.Owner!.CreateElement(name);
        foreach (var attribute in element.Attributes)
            copy.SetAttribute(attribute.Name, attribute.Value);
        while (element.FirstChild is { } child)
            copy.AppendChild(child);
        return copy;
    }

    private static string Clean(string? text) =>
        Regex.Replace((text ?? "").Replace('\u00A0', ' '), @"\s+", " ", RegexOptions.None, TimeSpan.FromMilliseconds(100)).Trim();

    private static string Cut(string text) => PersianText.Truncate(text, 120);
}
