using System.Net;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Fratak_Shop.Core.Services.Seo.Engine;
using Fratak_Shop.Core.Services.Seo.Knowledge;
using Fratak_Shop.Data.Entities.SeoEngine;

namespace Fratak_Shop.Core.Services.Seo.Smart;

/// <summary>
/// بخش «متن بلند» (توضیحات محصول، متن مقاله، صفحه‌های محتوایی) در «پیشنهاد هوشمند». با AngleSharp روی درخت HTML کار
/// می‌کند تا قالب‌بندی کاربر (لینک، تصویر، جدول) حفظ شود و فقط متن‌ها و ساختار بهتر شوند.
/// </summary>
public static partial class TextRefiner
{
    /// <summary>پاراگراف بلندتر از این (واژه) به دو یا چند پاراگراف شکسته می‌شود.</summary>
    public const int LongParagraphWords = 90;

    /// <summary>متن با این تعداد واژه یا بیشتر اگر تیتر نداشته باشد، تیتر H2 می‌گیرد.</summary>
    public const int HeadingWords = 250;

    /// <summary>حداکثر بخش‌هایی که خود موتور پررنگ می‌کند.</summary>
    public const int MaxNewBold = 5;

    private static List<SmartVariant> BodyVariants(string text, bool isHtml, SmartTextRequest request, SeoKnowledgeBase knowledge, SmartFacts facts, string keyword, int seed)
    {
        var html = isHtml ? text : PlainToHtml(text);

        // ۱) ویرایش: پاک‌سازی، نگارش، فهرست‌ها و پاراگراف‌های بلند
        var clean = NewVariant(Clean, html: true);
        var document = Parse(html);
        CleanDocument(document, clean);
        PolishDocument(document, knowledge, request, clean);
        ConvertPseudoLists(document, clean);
        SplitLongParagraphs(document, clean);
        clean.Text = Serialize(document);

        // ۲) ساختار: تیتر، پررنگ
        var structured = Derive(Structured, clean);
        document = Parse(clean.Text);
        DetectHeadings(document, structured);
        GenerateHeadings(document, request, knowledge, facts, seed, preferKeyword: false, structured);
        Emphasize(document, keyword.Length > 0 ? keyword : facts.Name(false), structured);
        CheckHeadingKeyword(document, keyword, structured);
        structured.Text = Serialize(document);

        // ۳) سئو: کلمه در پاراگراف اول و تیتر، کنترل تراکم، تنوع واژه
        var seo = Derive(Seo, clean);
        document = Parse(clean.Text);
        DetectHeadings(document, seo);
        GenerateHeadings(document, request, knowledge, facts, seed, preferKeyword: true, seo);
        if (keyword.Length > 0)
        {
            EnsureKeywordInIntro(document, keyword, request, seo);
            ControlDensity(document, keyword, request, seo);
        }
        else
            seo.Notes.Add("کلمهٔ کلیدی صفحه تعیین نشده؛ برای سئوی بهتر ابتدا کلمهٔ کلیدی را بنویسید.");
        Diversify(document, keyword, knowledge, seo);
        Emphasize(document, keyword.Length > 0 ? keyword : facts.Name(false), seo);
        CheckHeadingKeyword(document, keyword, seo);
        seo.Text = Serialize(document);

        var list = new List<SmartVariant> { clean, structured, seo };

        // ۴) ترغیبی: مزیت‌های واقعی + دعوت به اقدام (فقط صفحه‌های فروش و محتوایی)
        if (request.PageType is "product" or "category" or "brand" or "home" or "blog" or "page")
        {
            var persuasive = Derive(Persuasive, structured);
            document = Parse(structured.Text);
            if (AddBenefitsAndCta(document, request, knowledge, facts, seed, persuasive))
            {
                persuasive.Text = Serialize(document);
                list.Add(persuasive);
            }
        }
        return list;
    }

    private static SmartVariant Derive(string id, SmartVariant from)
    {
        var variant = NewVariant(id, html: true);
        foreach (var change in from.Changes) variant.Changes.Add(change);
        foreach (var note in from.Notes) variant.Notes.Add(note);
        variant.RuleIds.AddRange(from.RuleIds);
        return variant;
    }

    private static IDocument Parse(string html) => new HtmlParser().ParseDocument("<html><body>" + html + "</body></html>");

    private static string Serialize(IDocument document) => document.Body?.InnerHtml.Trim() ?? "";

    /// <summary>
    /// متن ساده (بدون HTML) → پاراگراف‌ها: هر خط غیرخالی یک پاراگراف (فهرست‌های خطی بعداً به ul/ol تبدیل می‌شوند).
    /// </summary>
    public static string PlainToHtml(string text) =>
        string.Concat(text.Replace("\r\n", "\n").Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .Select(l => "<p>" + WebUtility.HtmlEncode(l) + "</p>"));

    // ── پاک‌سازی ───────────────────────────────────────────────────

    private static void CleanDocument(IDocument document, SmartVariant variant)
    {
        var body = document.Body!;
        foreach (var junk in body.QuerySelectorAll("script,style,noscript,template,iframe,object,embed,form").ToList())
            junk.Remove();

        // رویدادهای درون‌خطی (onclick، onerror …) و نشانی‌های javascript: هیچ‌وقت در خروجی نمی‌مانند
        // (متن ممکن است از سایت تأمین‌کننده وارد شده باشد).
        foreach (var element in body.QuerySelectorAll("*").ToList())
        {
            foreach (var attribute in element.Attributes.ToList())
            {
                var unsafeUrl = attribute.Name is "href" or "src" or "action" or "formaction" or "xlink:href"
                    && attribute.Value.Trim().Replace("\t", "").Replace("\n", "").StartsWith("javascript:", StringComparison.OrdinalIgnoreCase);
                if (attribute.Name.StartsWith("on", StringComparison.OrdinalIgnoreCase) || unsafeUrl)
                    element.RemoveAttribute(attribute.Name);
            }
        }

        // متن پنهان = فریب گوگل (Hidden text) → حذف
        var hidden = body.QuerySelectorAll("[style]").Where(e => HiddenStyle().IsMatch(e.GetAttribute("style") ?? "")).ToList();
        foreach (var element in hidden)
            element.Remove();
        if (hidden.Count > 0)
            AddChange(variant, $"حذف {PersianText.Digits(hidden.Count)} متن پنهان (گوگل متن پنهان را فریب می‌داند و جریمه می‌کند)");

        // صفحه خودش H1 دارد؛ H1 داخل متن → H2
        var h1s = body.QuerySelectorAll("h1").ToList();
        foreach (var h1 in h1s)
            Rename(document, h1, "h2");
        if (h1s.Count > 0)
            AddChange(variant, "تیتر H1 داخل متن به H2 تبدیل شد (هر صفحه فقط یک H1 دارد)");

        // <p> با <br><br> → چند پاراگراف
        var splitCount = 0;
        foreach (var paragraph in body.QuerySelectorAll("p").ToList())
        {
            var groups = SplitByDoubleBreak(paragraph);
            if (groups.Count < 2)
                continue;
            foreach (var group in groups)
            {
                var fresh = document.CreateElement("p");
                foreach (var node in group)
                    fresh.AppendChild(node);
                paragraph.Before(fresh);
            }
            paragraph.Remove();
            splitCount++;
        }
        if (splitCount > 0)
            AddChange(variant, "خط‌های خالی پشت سر هم به پاراگراف جدا تبدیل شد");

        // پاراگراف خالی
        var empty = body.QuerySelectorAll("p,div,span").Where(e => PersianText.Clean(e.TextContent.Replace('\u00A0', ' ')).Length == 0
            && e.QuerySelector("img,iframe,video,table,input,svg") is null).ToList();
        foreach (var element in empty)
            element.Remove();
        if (empty.Count > 0)
            AddChange(variant, $"حذف {PersianText.Digits(empty.Count)} پاراگراف خالی");
    }

    private static List<List<INode>> SplitByDoubleBreak(IElement paragraph)
    {
        var groups = new List<List<INode>> { new() };
        var nodes = paragraph.ChildNodes.ToList();
        for (var i = 0; i < nodes.Count; i++)
        {
            if (nodes[i] is IElement { LocalName: "br" })
            {
                var j = i + 1;
                while (j < nodes.Count && nodes[j] is IText t && string.IsNullOrWhiteSpace(t.Data.Replace('\u00A0', ' ')))
                    j++;
                if (j < nodes.Count && nodes[j] is IElement { LocalName: "br" })
                {
                    groups.Add([]);
                    i = j;
                    continue;
                }
            }
            groups[^1].Add(nodes[i]);
        }
        return groups.Where(g => g.Any(n => n.TextContent.Trim().Length > 0 || n is IElement)).ToList();
    }

    private static IElement Rename(IDocument document, IElement element, string name)
    {
        var fresh = document.CreateElement(name);
        foreach (var child in element.ChildNodes.ToList())
            fresh.AppendChild(child);
        element.Replace(fresh);
        return fresh;
    }

    // ── نگارش گره‌های متنی ─────────────────────────────────────────

    private static void PolishDocument(IDocument document, SeoKnowledgeBase knowledge, SmartTextRequest request, SmartVariant variant)
    {
        var options = new WritingOptions { SpellingNotes = false, Vocabulary = request.Vocabulary };
        var merged = new WritingResult();
        foreach (var node in TextNodes(document.Body!).ToList())
        {
            var data = node.Data;
            if (string.IsNullOrWhiteSpace(data))
                continue;
            var leading = data.Length - data.TrimStart().Length;
            var trailing = data.Length - data.TrimEnd().Length;
            var polished = PersianWriting.Polish(data, knowledge, options);
            if (polished.Text.Length == 0)
                continue;
            var rebuilt = (leading > 0 ? " " : "") + polished.Text + (trailing > 0 ? " " : "");
            if (rebuilt != data)
                node.Data = rebuilt;
            merged.Changes.AddRange(polished.Changes);
            foreach (var note in polished.Notes)
                if (!merged.Notes.Contains(note)) merged.Notes.Add(note);
        }
        // تجمیع شمارنده‌های قاعده‌های داخلی (نیم‌فاصله، نشانه‌گذاری …) در یک ردیف
        var summary = new WritingResult();
        foreach (var group in merged.Changes.GroupBy(c => (c.RuleId, c.From, c.Category)))
        {
            var first = group.First();
            summary.Changes.Add(first.From.Length == 0 && first.To.Length > 0
                ? first with { To = PersianText.Digits(group.Sum(c => ParseDigits(c.To))) }
                : first);
        }
        summary.Notes.AddRange(merged.Notes);
        summary.Notes.AddRange(PersianWriting.SpellingNotes(PersianText.HtmlToText(document.Body!.InnerHtml), knowledge, request.Vocabulary));
        Record(variant, summary);
    }

    private static int ParseDigits(string value)
    {
        var normalized = new string(value.Select(c => c is >= '۰' and <= '۹' ? (char)('0' + (c - '۰')) : c).ToArray());
        return int.TryParse(normalized, out var number) ? number : 1;
    }

    /// <summary>گره‌های متنی قابل ویرایش (کد، pre و اسکریپت کنار گذاشته می‌شوند).</summary>
    private static IEnumerable<IText> TextNodes(INode root)
    {
        foreach (var child in root.ChildNodes)
        {
            if (child is IText text)
                yield return text;
            else if (child is IElement element && element.LocalName is not ("code" or "pre" or "script" or "style" or "kbd" or "samp"))
                foreach (var inner in TextNodes(element))
                    yield return inner;
        }
    }

    // ── فهرست و پاراگراف ──────────────────────────────────────────

    /// <summary>
    /// خط‌هایی که با «-»، «•»، «*» یا «۱-»، «1.» شروع می‌شوند و پشت سر هم آمده‌اند → فهرست واقعی (ul/ol).
    /// گوگل فهرست را بهتر می‌فهمد و گاهی همان را در نتایج نشان می‌دهد.
    /// </summary>
    private static void ConvertPseudoLists(IDocument document, SmartVariant variant)
    {
        var body = document.Body!;
        // پاراگراف‌های چندخطی (با br) که همهٔ خط‌هایشان نشانهٔ فهرست دارند → چند پاراگراف (برای مرحلهٔ بعد)
        foreach (var paragraph in body.QuerySelectorAll("p").ToList())
        {
            if (paragraph.QuerySelector("br") is null)
                continue;
            var lines = BreakSplit().Split(paragraph.InnerHtml).Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
            if (lines.Count < 2 || lines.Count(l => Bullet().IsMatch(WebUtility.HtmlDecode(StripTags(l)))) < 2)
                continue;
            foreach (var line in lines)
            {
                var fresh = document.CreateElement("p");
                fresh.InnerHtml = line;
                paragraph.Before(fresh);
            }
            paragraph.Remove();
        }

        var converted = 0;
        var children = body.Children.ToList();
        for (var i = 0; i < children.Count; i++)
        {
            if (!IsBulletParagraph(children[i], out var ordered))
                continue;
            var run = new List<IElement> { children[i] };
            while (i + run.Count < children.Count && IsBulletParagraph(children[i + run.Count], out var nextOrdered) && nextOrdered == ordered)
                run.Add(children[i + run.Count]);
            if (run.Count < 2)
                continue;
            var list = document.CreateElement(ordered ? "ol" : "ul");
            foreach (var paragraph in run)
            {
                var item = document.CreateElement("li");
                StripBullet(paragraph);
                foreach (var node in paragraph.ChildNodes.ToList())
                    item.AppendChild(node);
                list.AppendChild(item);
            }
            run[0].Before(list);
            foreach (var paragraph in run)
                paragraph.Remove();
            converted++;
            i += run.Count - 1;
        }
        if (converted > 0)
            AddChange(variant, $"{PersianText.Digits(converted)} فهرست خطی به فهرست واقعی (نقطه‌دار/شماره‌دار) تبدیل شد");
    }

    private static bool IsBulletParagraph(IElement element, out bool ordered)
    {
        ordered = false;
        if (element.LocalName != "p")
            return false;
        var match = Bullet().Match(element.TextContent);
        if (!match.Success)
            return false;
        ordered = char.IsDigit(match.Groups[1].Value[0]);
        return true;
    }

    private static void StripBullet(IElement paragraph)
    {
        var first = TextNodes(paragraph).FirstOrDefault(t => t.Data.Trim().Length > 0);
        if (first is null)
            return;
        first.Data = BulletPrefix().Replace(first.Data, "", 1);
    }

    private static string StripTags(string html) => Regex.Replace(html, "<[^>]+>", " ");

    /// <summary>پاراگراف ساده (بدون تگ داخلی) بلندتر از ۹۰ واژه → چند پاراگراف در مرز جمله‌ها.</summary>
    private static void SplitLongParagraphs(IDocument document, SmartVariant variant)
    {
        var split = 0;
        foreach (var paragraph in document.Body!.Children.Where(c => c.LocalName == "p").ToList())
        {
            if (paragraph.Children.Length > 0)
                continue; // قالب‌بندی داخلی (لینک، پررنگ) دارد؛ شکستن خطر خراب کردنش را دارد.
            var text = PersianText.Clean(paragraph.TextContent);
            var words = PersianText.WordCount(text);
            if (words <= LongParagraphWords)
                continue;
            var sentences = SplitKeepingPunctuation(text);
            if (sentences.Count < 2)
                continue;
            var parts = new List<string>();
            var current = new List<string>();
            var target = words / (int)Math.Ceiling(words / (double)LongParagraphWords * 1.0) + 5;
            var count = 0;
            foreach (var sentence in sentences)
            {
                current.Add(sentence);
                count += PersianText.WordCount(sentence);
                if (count >= target)
                {
                    parts.Add(string.Join(" ", current));
                    current.Clear();
                    count = 0;
                }
            }
            if (current.Count > 0)
            {
                if (parts.Count > 0 && count < 15)
                    parts[^1] += " " + string.Join(" ", current);
                else
                    parts.Add(string.Join(" ", current));
            }
            if (parts.Count < 2)
                continue;
            foreach (var part in parts)
            {
                var fresh = document.CreateElement("p");
                fresh.TextContent = part;
                paragraph.Before(fresh);
            }
            paragraph.Remove();
            split++;
        }
        if (split > 0)
            AddChange(variant, $"{PersianText.Digits(split)} پاراگراف خیلی بلند به پاراگراف‌های کوتاه‌تر شکسته شد");
    }

    // ── تیترها ─────────────────────────────────────────────────────

    /// <summary>
    /// خط‌های کوتاهی که در واقع تیترند (پاراگراف کوتاه بدون نقطه، تمام‌پررنگ یا پایان‌یافته با «:» و پیش از یک پاراگراف بلندتر)
    /// → H2 (یا H3 اگر زیرتیتر به نظر برسد).
    /// </summary>
    private static void DetectHeadings(IDocument document, SmartVariant variant)
    {
        var converted = 0;
        var children = document.Body!.Children.ToList();
        var seenH2 = children.Any(c => c.LocalName == "h2");
        for (var i = 0; i < children.Count - 1; i++)
        {
            var element = children[i];
            if (element.LocalName != "p" || element.QuerySelector("img,a,br") is not null)
                continue;
            var text = PersianText.Clean(element.TextContent);
            var words = PersianText.WordCount(text);
            if (words is < 1 or > 12 || text.Length > 90)
                continue;
            var next = children[i + 1];
            if (next.LocalName is not ("p" or "ul" or "ol" or "table") || PersianText.WordCount(next.TextContent) < Math.Max(8, words * 2))
                continue;
            var fullyBold = element.Children.Length == 1 && element.Children[0].LocalName is "strong" or "b"
                            && PersianText.Clean(element.Children[0].TextContent) == text;
            var endsColon = text.EndsWith(':') || text.EndsWith('：');
            var question = text.EndsWith('؟') || text.EndsWith('?');
            var noPunctuation = !".،؛!…,".Contains(text[^1]) && !endsColon && words <= 8;
            if (!(fullyBold || endsColon || question || noPunctuation))
                continue;
            var level = endsColon && seenH2 ? "h3" : "h2";
            var heading = document.CreateElement(level);
            heading.TextContent = text.TrimEnd(':', '：', ' ');
            element.Replace(heading);
            seenH2 |= level == "h2";
            converted++;
        }
        if (converted > 0)
            AddChange(variant, $"{PersianText.Digits(converted)} خط کوتاه به تیتر واقعی (H2/H3) تبدیل شد");
    }

    /// <summary>واژه‌های نشانهٔ نقش هر بخش متن (برای انتخاب تیتر مناسب).</summary>
    private static readonly (string Role, string[] Cues)[] RoleCues =
    [
        ("specs", ["مشخصات", "ابعاد", "وزن", "ظرفیت", "توان", "پردازنده", "حافظه", "جنس", "باتری", "اینچ", "وات"]),
        ("features", ["ویژگی", "قابلیت", "امکانات", "مجهز", "طراحی", "فناوری"]),
        ("usage", ["کاربرد", "استفاده", "مناسب برای", "به کار", "مصرف"]),
        ("buying", ["خرید", "قیمت", "انتخاب", "بخرید", "هنگام خرید", "سفارش"]),
        ("care", ["نگهداری", "تمیز", "شستشو", "مراقبت", "نگه دارید", "نگه‌داری"]),
        ("compare", ["مقایسه", "تفاوت", "در مقابل", "نسبت به"]),
        ("steps", ["مرحله", "گام", "ابتدا", "سپس", "در ادامه"]),
        ("pros", ["مزایا", "نقاط قوت", "مزیت"]),
        ("cons", ["معایب", "محدودیت", "نقاط ضعف", "ایراد"]),
    ];

    private static readonly string[] SummaryCues = ["در نهایت", "در پایان", "جمع‌بندی", "جمع بندی", "خلاصه", "به طور کلی", "به‌طور کلی", "در مجموع", "سخن پایانی"];

    /// <summary>
    /// متن بلند (≥ ۲۵۰ واژه) بدون هیچ تیتر: بر اساس موضوع هر بخش (ویژگی، مشخصات، کاربرد، خرید، نگهداری، جمع‌بندی …)
    /// تیتر H2 از الگوهای جدول دانش اضافه می‌شود. پاراگراف اول (مقدمه) بی‌تیتر می‌ماند و هر نقش فقط یک بار.
    /// </summary>
    private static void GenerateHeadings(IDocument document, SmartTextRequest request, SeoKnowledgeBase knowledge, SmartFacts facts,
        int seed, bool preferKeyword, SmartVariant variant)
    {
        var body = document.Body!;
        if (body.QuerySelector("h2,h3") is not null)
            return;
        var words = PersianText.WordCount(body.TextContent);
        var blocks = body.Children.Where(c => c.LocalName is "p" or "ul" or "ol" or "table" or "blockquote").ToList();
        if (words < HeadingWords || blocks.Count < 3)
            return;

        var pageType = HeadingPageType(request.PageType, knowledge);
        var used = new HashSet<string>(StringComparer.Ordinal);
        var inserted = new List<string>();
        var sinceHeading = PersianText.WordCount(blocks[0].TextContent);

        string? Template(string role)
        {
            var picked = facts.Pick(knowledge.Headings, r => r.Value == role && r.Topic == pageType, 1, seed + role.Length, preferKeyword);
            if (picked.Count == 0)
                return null;
            variant.RuleIds.Add(picked[0].Rule.Id);
            return picked[0].Text;
        }

        void Insert(IElement before, string role)
        {
            var title = Template(role);
            if (title is null)
                return;
            var heading = document.CreateElement("h2");
            heading.TextContent = title;
            before.Before(heading);
            used.Add(role);
            inserted.Add(title);
            sinceHeading = 0;
        }

        for (var i = 1; i < blocks.Count; i++)
        {
            var block = blocks[i];
            var blockText = PersianText.Clean(block.TextContent);
            var isLast = i == blocks.Count - 1;
            string? role = null;
            if (isLast && blocks.Count >= 4 && SummaryCues.Any(c => blockText.StartsWith(c, StringComparison.Ordinal)))
                role = "summary";
            else
            {
                var head = string.Join(' ', PersianText.Tokenize(blockText).Take(30));
                var best = RoleCues
                    .Select(r => (r.Role, Hits: r.Cues.Count(c => head.Contains(PersianText.Normalize(c), StringComparison.Ordinal))))
                    .Where(r => r.Hits > 0 && !used.Contains(r.Role))
                    .OrderByDescending(r => r.Hits)
                    .FirstOrDefault();
                role = best.Role;
            }
            if (role is not null && !used.Contains(role) && (sinceHeading >= 60 || role == "summary"))
                Insert(block, role);
            sinceHeading += PersianText.WordCount(blockText);
        }

        // هیچ نقشی تشخیص داده نشد: یک تیتر عمومی پس از مقدمه و یک «جمع‌بندی» پیش از بخش آخر.
        if (inserted.Count == 0)
        {
            Insert(blocks[1], request.PageType == "product" ? "features" : "intro");
            if (blocks.Count >= 5 && !used.Contains("summary"))
                Insert(blocks[^1], "summary");
        }
        if (inserted.Count > 0)
            AddChange(variant, $"{PersianText.Digits(inserted.Count)} تیتر H2 اضافه شد: {string.Join("، ", inserted.Take(4).Select(t => "«" + t + "»"))}");
    }

    /// <summary>نوع صفحه‌ای که الگوی تیتر دارد (اگر برای این نوع الگویی نیست، page یا product).</summary>
    private static string HeadingPageType(string pageType, SeoKnowledgeBase knowledge)
    {
        if (knowledge.Headings.Any(h => h.Topic == pageType))
            return pageType;
        return knowledge.Headings.Any(h => h.Topic == "page") ? "page" : "product";
    }

    private static void CheckHeadingKeyword(IDocument document, string keyword, SmartVariant variant)
    {
        if (keyword.Length == 0)
            return;
        var headings = document.Body!.QuerySelectorAll("h2,h3").Select(h => h.TextContent).ToList();
        if (headings.Count > 0 && !headings.Any(h => KeywordMatcher.Contains(h, keyword, partial: true)))
            variant.Notes.Add($"کلمهٔ کلیدی «{keyword}» در هیچ تیتری نیست؛ اگر طبیعی است در یکی از تیترها بیاورید.");
    }

    // ── پررنگ کردن ─────────────────────────────────────────────────

    /// <summary>
    /// پررنگ کردن هدفمند (حداکثر ۵ مورد): اولین آمدن کلمهٔ کلیدی در پاراگراف اول، و برچسب‌های «نکته:»، «توجه:» و
    /// «عنوان: توضیح» ابتدای بندها و فهرست‌ها. اگر متن از قبل پررنگ زیاد دارد کاری نمی‌کند.
    /// </summary>
    private static void Emphasize(IDocument document, string term, SmartVariant variant)
    {
        var body = document.Body!;
        var existing = body.QuerySelectorAll("strong,b").Length;
        if (existing >= 8)
            return;
        var budget = Math.Min(MaxNewBold, SpamGuard.MaxBold - existing - 2);
        var added = 0;

        var first = body.Children.FirstOrDefault(c => c.LocalName == "p");
        if (first is not null && term.Length > 1 && first.QuerySelector("strong,b") is null && budget > 0)
        {
            var pattern = SpamGuard.PhrasePattern(term);
            if (pattern is not null && WrapFirst(document, first, new Regex(pattern, RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100))))
                added++;
        }

        foreach (var block in body.QuerySelectorAll("p,li").ToList())
        {
            if (added >= budget)
                break;
            if (block.QuerySelector("strong,b") is not null)
                continue;
            var firstText = TextNodes(block).FirstOrDefault(t => t.Data.Trim().Length > 0);
            if (firstText is null || firstText.Parent != block)
                continue;
            var match = Label().Match(firstText.Data);
            if (!match.Success || PersianText.WordCount(match.Groups[1].Value) > 5)
                continue;
            if (WrapRange(document, firstText, match.Index, match.Length))
                added++;
        }
        if (added > 0)
            AddChange(variant, $"{PersianText.Digits(added)} بخش مهم پررنگ شد (کلمهٔ کلیدی و برچسب نکته‌ها)");
    }

    private static bool WrapFirst(IDocument document, IElement scope, Regex regex)
    {
        foreach (var node in TextNodes(scope).ToList())
        {
            if (node.Parent is IElement { LocalName: "a" or "strong" or "b" })
                continue;
            Match match;
            try { match = regex.Match(node.Data); }
            catch (RegexMatchTimeoutException) { return false; }
            if (match.Success)
                return WrapRange(document, node, match.Index, match.Length);
        }
        return false;
    }

    private static bool WrapRange(IDocument document, IText node, int start, int length)
    {
        var data = node.Data;
        if (start < 0 || length <= 0 || start + length > data.Length || node.Parent is null)
            return false;
        var strong = document.CreateElement("strong");
        strong.TextContent = data.Substring(start, length);
        var after = document.CreateTextNode(data[(start + length)..]);
        node.Data = data[..start];
        node.After(strong);
        strong.After(after);
        return true;
    }

    // ── سئو ────────────────────────────────────────────────────────

    /// <summary>کلمهٔ کلیدی در پاراگراف اول نیست → یک جملهٔ معرفی کوتاه و طبیعی ابتدای پاراگراف اول.</summary>
    private static void EnsureKeywordInIntro(IDocument document, string keyword, SmartTextRequest request, SmartVariant variant)
    {
        var body = document.Body!;
        var first = body.Children.FirstOrDefault(c => c.LocalName == "p");
        if (first is not null && KeywordMatcher.Contains(first.TextContent, keyword))
            return;
        var sentence = request.PageType switch
        {
            "product" => $"در این بخش {keyword} را از نزدیک بشناسید.",
            "blog" => $"در این مقاله دربارهٔ {keyword} می‌خوانید.",
            "category" or "brand" => $"در این صفحه انواع {keyword} را می‌بینید.",
            _ => $"در این صفحه با {keyword} آشنا می‌شوید.",
        };
        if (first is null)
        {
            var paragraph = document.CreateElement("p");
            paragraph.TextContent = sentence;
            body.Prepend(paragraph);
        }
        else
            first.Prepend(document.CreateTextNode(sentence + " "));
        AddChange(variant, "کلمهٔ کلیدی در پاراگراف اول آمد (گوگل ابتدای متن را مهم‌تر می‌داند)");
    }

    /// <summary>
    /// تراکم کلمهٔ کلیدی بیش از ۲٫۵٪ → تکرارهای اضافه (بعد از سومین) با عبارت جایگزین طبیعی («این محصول»، «این موضوع» …)
    /// عوض می‌شوند تا انباشت کلمه (Keyword stuffing) پیش نیاید.
    /// </summary>
    private static void ControlDensity(IDocument document, string keyword, SmartTextRequest request, SmartVariant variant)
    {
        var plain = PersianText.HtmlToText(document.Body!.InnerHtml);
        var density = SpamGuard.KeywordDensity(plain, keyword);
        if (density <= SpamGuard.TargetDensity)
            return;
        var pattern = SpamGuard.PhrasePattern(keyword);
        if (pattern is null)
            return;
        var regex = new Regex(pattern, RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(200));
        var substitute = request.PageType switch
        {
            "product" => "این محصول",
            "category" => "این دسته",
            "brand" => "این برند",
            "blog" => "این موضوع",
            _ => "آن",
        };
        var words = Math.Max(1, PersianText.WordCount(plain));
        var keywordWords = Math.Max(1, PersianText.ContentStems(keyword).Count);
        var occurrences = KeywordMatcher.Count(plain, keyword);
        var allowed = Math.Max(3, (int)(words * 2.0 / 100 / keywordWords));
        var seen = 0;
        var replaced = 0;
        foreach (var node in TextNodes(document.Body!).ToList())
        {
            if (node.Parent is IElement { LocalName: "h2" or "h3" or "a" or "strong" or "b" })
            {
                seen += regex.Matches(node.Data).Count;
                continue;
            }
            try
            {
                node.Data = regex.Replace(node.Data, m =>
                {
                    seen++;
                    if (seen <= allowed || occurrences - replaced <= allowed)
                        return m.Value;
                    replaced++;
                    return substitute;
                });
            }
            catch (RegexMatchTimeoutException)
            {
                break;
            }
        }
        if (replaced > 0)
            AddChange(variant, $"تراکم کلمهٔ کلیدی {PersianText.Decimal1(density)}٪ بود؛ {PersianText.Digits(replaced)} تکرار اضافه با «{substitute}» جایگزین شد تا انباشت کلمه پیش نیاید");
    }

    /// <summary>
    /// تنوع واژه: پرتکرارترین واژهٔ غیرکلیدی (بیش از ۴٪ متن) که هم‌معنی امن در جدول دانش دارد، در هر سومین تکرار با هم‌معنی عوض می‌شود.
    /// </summary>
    private static void Diversify(IDocument document, string keyword, SeoKnowledgeBase knowledge, SmartVariant variant)
    {
        var plain = PersianText.HtmlToText(document.Body!.InnerHtml);
        var words = PersianText.WordCount(plain);
        if (words < 150 || knowledge.Synonyms.Count == 0)
            return;
        var keywordTokens = PersianText.Tokenize(keyword).ToHashSet(StringComparer.Ordinal);
        var counts = Regex.Matches(plain, @"[\p{L}\u200C]+").Select(m => m.Value)
            .Where(w => w.Length > 2 && !keywordTokens.Contains(PersianText.Normalize(w)) && knowledge.Synonyms.ContainsKey(w))
            .GroupBy(w => w).Select(g => (Word: g.Key, Count: g.Count())).OrderByDescending(x => x.Count).FirstOrDefault();
        if (counts.Word is null || counts.Count * 100.0 / words < 4 || counts.Count < 4)
            return;
        var alternatives = knowledge.Synonyms[counts.Word].Where(a => a != counts.Word).ToList();
        if (alternatives.Count == 0)
            return;
        var pattern = SpamGuard.PhrasePattern(counts.Word);
        if (pattern is null)
            return;
        var regex = new Regex(pattern, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200));
        var index = 0;
        var swapped = 0;
        foreach (var node in TextNodes(document.Body!).ToList())
        {
            if (node.Parent is IElement { LocalName: "h2" or "h3" or "a" })
                continue;
            try
            {
                node.Data = regex.Replace(node.Data, m =>
                {
                    index++;
                    if (index % 3 != 0)
                        return m.Value;
                    swapped++;
                    return alternatives[(index / 3 - 1) % alternatives.Count];
                });
            }
            catch (RegexMatchTimeoutException)
            {
                break;
            }
        }
        if (swapped > 0)
        {
            variant.RuleIds.Add((int)SeoKnowledgeKind.Synonym + ":" + counts.Word);
            AddChange(variant, $"واژهٔ «{counts.Word}» {PersianText.Digits(counts.Count)} بار تکرار شده بود؛ {PersianText.Digits(swapped)} مورد با هم‌معنی عوض شد");
        }
    }

    // ── ترغیبی ─────────────────────────────────────────────────────

    /// <summary>
    /// افزودن «چرا از ما بخرید» (فهرست مزیت‌های واقعی) و یک جملهٔ دعوت به اقدام مناسب نوع صفحه، اگر متن از قبل ندارد.
    /// خروجی false = چیزی اضافه نشد (این نسخه نمایش داده نمی‌شود).
    /// </summary>
    private static bool AddBenefitsAndCta(IDocument document, SmartTextRequest request, SeoKnowledgeBase knowledge, SmartFacts facts, int seed, SmartVariant variant)
    {
        var body = document.Body!;
        var plain = PersianText.Normalize(body.TextContent);
        var added = false;

        if (request.PageType is "product" or "category" or "brand" or "home")
        {
            var benefits = facts.Benefits(knowledge, "long", 4, seed)
                .Where(b => !plain.Contains(PersianText.Normalize(b.Text), StringComparison.Ordinal))
                .ToList();
            var headingType = HeadingPageType(request.PageType, knowledge);
            var heading = facts.Pick(knowledge.Headings, r => r.Value == "why" && r.Topic == headingType, 1, seed);
            if (benefits.Count >= 2 && heading.Count > 0 && !plain.Contains(PersianText.Normalize(heading[0].Text), StringComparison.Ordinal))
            {
                var h2 = document.CreateElement("h2");
                h2.TextContent = heading[0].Text;
                var list = document.CreateElement("ul");
                foreach (var (rule, benefit) in benefits)
                {
                    var item = document.CreateElement("li");
                    item.TextContent = benefit;
                    list.AppendChild(item);
                    variant.RuleIds.Add(rule.Id);
                }
                body.AppendChild(h2);
                body.AppendChild(list);
                variant.RuleIds.Add(heading[0].Rule.Id);
                AddChange(variant, $"بخش «{heading[0].Text}» با {PersianText.Digits(benefits.Count)} مزیت واقعی فروشگاه اضافه شد");
                added = true;
            }
        }

        var cta = facts.Pick(knowledge.CallsToAction, r => r.Topic == request.PageType && facts.Holds(r.Value), 1, seed);
        if (cta.Count > 0 && !plain.Contains(PersianText.Normalize(cta[0].Text), StringComparison.Ordinal))
        {
            var paragraph = document.CreateElement("p");
            paragraph.TextContent = cta[0].Text;
            body.AppendChild(paragraph);
            variant.RuleIds.Add(cta[0].Rule.Id);
            AddChange(variant, "جملهٔ دعوت به اقدام اضافه شد: «" + cta[0].Text + "»");
            added = true;
        }
        return added;
    }

    // ── امتیاز متن بلند ───────────────────────────────────────────

    private static double BodyPenalty(string text, bool isHtml, string plain, int words, string keyword, SmartTextRequest request, List<string> issues)
    {
        var penalty = 0.0;
        var minimum = request.PageType switch { "blog" => 400, "category" or "brand" => 120, "product" => 150, _ => 100 };
        if (words < minimum)
        {
            penalty += 8;
            issues.Add($"متن کوتاه است ({PersianText.Digits(words)} واژه؛ حداقل حدود {PersianText.Digits(minimum)} واژه پیشنهاد می‌شود).");
        }
        var stats = isHtml ? PersianText.AnalyzeHtml(text) : null;
        var paragraphs = stats?.Paragraphs ?? plain.Split('\n').Select(PersianText.Clean).Where(p => p.Length > 0).ToList();
        var headings = stats?.Headings ?? [];
        if (words >= 300 && headings.Count == 0)
        {
            penalty += 15;
            issues.Add("متن بلند تیتر (H2) ندارد؛ تیتر خواندن و فهم متن را برای کاربر و گوگل آسان می‌کند.");
        }
        var longCount = paragraphs.Count(p => PersianText.WordCount(p) > 120);
        if (longCount > 0)
        {
            penalty += Math.Min(15, longCount * 5);
            issues.Add($"{PersianText.Digits(longCount)} پاراگراف خیلی بلند (بیش از ۱۲۰ واژه).");
        }
        if (keyword.Length > 0 && words >= 50)
        {
            var first = paragraphs.FirstOrDefault() ?? "";
            if (!KeywordMatcher.Contains(first, keyword))
            {
                penalty += 8;
                issues.Add("کلمهٔ کلیدی در پاراگراف اول نیست.");
            }
            if (headings.Count > 0 && !headings.Any(h => KeywordMatcher.Contains(h.Text, keyword, partial: true)))
            {
                penalty += 4;
                issues.Add("کلمهٔ کلیدی در هیچ تیتری نیست.");
            }
        }
        if (words >= 400 && !(isHtml && text.Contains("<li", StringComparison.OrdinalIgnoreCase)))
        {
            penalty += 4;
            issues.Add("متن بلند فهرست (نقطه‌دار یا شماره‌دار) ندارد.");
        }
        return penalty;
    }

    [GeneratedRegex(@"display\s*:\s*none|visibility\s*:\s*hidden|font-size\s*:\s*0(?:px|pt|em|rem)?\s*(?:;|$)", RegexOptions.IgnoreCase)]
    private static partial Regex HiddenStyle();

    [GeneratedRegex(@"<br\s*/?>", RegexOptions.IgnoreCase)]
    private static partial Regex BreakSplit();

    /// <summary>نشانهٔ فهرست ابتدای خط: «-»، «•»، «*»، «–»، «▪»، «●» یا عدد + «-»/«.»/«)».</summary>
    [GeneratedRegex(@"^\s*([-•*–▪●]|[0-9۰-۹]{1,2}\s*[-.)])\s+\S")]
    private static partial Regex Bullet();

    [GeneratedRegex(@"^\s*(?:[-•*–▪●]|[0-9۰-۹]{1,2}\s*[-.)])\s+")]
    private static partial Regex BulletPrefix();

    /// <summary>برچسب ابتدای بند: «نکته:»، «مشخصات فنی:» (حداکثر حدود ۴۰ نویسه پیش از «:»).</summary>
    [GeneratedRegex(@"^\s*([^:：.،؛!?؟\n]{2,40})[:：]")]
    private static partial Regex Label();
}
