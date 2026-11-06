using System.Text.Json;
using System.Text.RegularExpressions;
using Fratak_Shop.Core.Services.Seo.Engine;

namespace Fratak_Shop.Core.Services.Smart;

/// <summary>
/// یک متن خام از یک منبع برای تحقیق (دور ۲۱). Source = نام/دامنهٔ منبع («digikala»، «mobile.ir»)، Trusted = منبع معتبر
/// (دیجی‌کالا یا صفحهٔ خود تأمین‌کننده) که یک تأییدش کافی است؛ بقیه باید دست‌کم دو تا هم‌نظر باشند.
/// </summary>
public sealed record ResearchDoc(string Source, string Text, bool Trusted);

/// <summary>
/// تحقیق دربارهٔ یک کالا از روی متن «توضیحات» سایت‌های دیگر (دور ۲۱) — کاملاً داخلی، بدون هیچ سرویس یا مدل زبانی بیرونی.
/// <para>چه می‌کند؟ متن چند سایت را «می‌خواند» و فقط داده‌های قابل اتکا بیرون می‌کشد؛ <b>هیچ جمله‌ای کپی نمی‌شود</b>
/// (کپی = جریمهٔ محتوای تکراری + نقض حق نشر). خروجی‌ها:</para>
/// <list type="bullet">
/// <item>ترتیب موضوع‌ها: هر موضوعی (باتری، صفحه، دوربین …) که منبع‌های بیشتری درباره‌اش نوشته‌اند، برای خریدار مهم‌تر است ← جلوتر نوشته می‌شود.</item>
/// <item>اقلام همراه: از بخش «محتویات جعبه/اقلام همراه» هر منبع؛ فقط موردهایی که ≥۲ منبع یا یک منبع معتبر گفته‌اند.</item>
/// <item>کاربردها: عبارت‌های «مناسب برای …/ایده‌آل برای …» با همان قاعدهٔ توافق.</item>
/// <item>ویژگی‌های برجسته: از یک فهرست ثابت و دست‌نوشته (ANC، شارژ سریع، اتصال هم‌زمان …) با برچسب استاندارد خود ما.</item>
/// </list>
/// <para>مثال: سه فروشگاه که همه «کابل شارژ» و «دفترچهٔ راهنما» را در جعبه گفته‌اند و دو تا «مناسب برای ورزش» ←
/// InBox=[کابل شارژ، دفترچهٔ راهنما]، UseCases=[ورزش].</para>
/// <para>محل استفاده: SmartOrchestrator.NotesAsync ← ContentWriter.ProductReview («توضیحات محصول»).</para>
/// </summary>
public static partial class ProductResearch
{
    /// <summary>بیشترین طول متنی که از هر منبع بررسی می‌شود (کنترل حافظه و زمان روی هاست ضعیف).</summary>
    public const int MaxDocChars = 12_000;

    /// <summary>کلیدواژه‌های هر موضوع برای شمردن «چند منبع دربارهٔ این موضوع نوشته‌اند».</summary>
    private static readonly (SpecTopic Topic, string[] Words)[] TopicWords =
    [
        (SpecTopic.Battery, ["باتری", "شارژدهی", "میلی‌آمپر", "میلی آمپر", "mah"]),
        (SpecTopic.ScreenSize, ["صفحه نمایش", "صفحه‌نمایش", "نمایشگر", "اینچ"]),
        (SpecTopic.Camera, ["دوربین", "مگاپیکسل", "عکاسی", "فیلم‌برداری", "فیلمبرداری"]),
        (SpecTopic.Processor, ["پردازنده", "چیپست", "تراشه"]),
        (SpecTopic.Storage, ["حافظه داخلی", "حافظهٔ داخلی", "گیگابایت"]),
        (SpecTopic.NoiseCancel, ["نویز", "anc"]),
        (SpecTopic.Bluetooth, ["بلوتوث", "bluetooth"]),
        (SpecTopic.Water, ["ضد آب", "ضدآب", "تعریق", "ipx", "ip67", "ip68"]),
        (SpecTopic.Weight, ["وزن", "سبک"]),
        (SpecTopic.FastCharge, ["شارژ سریع", "fast charg", "quick charge"]),
        (SpecTopic.Microphones, ["میکروفون", "مکالمه"]),
        (SpecTopic.Material, ["بدنه", "طراحی", "جنس"]),
        (SpecTopic.Port, ["usb-c", "type-c", "تایپ سی", "درگاه"]),
        (SpecTopic.Power, ["توان", "وات"]),
        (SpecTopic.Compatibility, ["سازگار", "سازگاری"]),
    ];

    /// <summary>
    /// فهرست ثابت و دست‌نوشتهٔ ویژگی‌های برجسته: (برچسب استاندارد ما، موضوع، عبارت‌هایی که در متن منابع دنبالشان می‌گردیم).
    /// برچسب همیشه متن خود ما است؛ متن منبع هرگز مستقیم نمایش داده نمی‌شود.
    /// </summary>
    private static readonly (string Label, SpecTopic Topic, string[] Patterns)[] FeatureLexicon =
    [
        ("حذف نویز فعال (ANC)", SpecTopic.NoiseCancel, ["anc", "حذف نویز فعال", "نویز کنسلینگ فعال", "active noise"]),
        ("شارژ سریع", SpecTopic.FastCharge, ["شارژ سریع", "fast charg", "quick charge", "فست شارژ"]),
        ("شارژ بی‌سیم", SpecTopic.WirelessCharge, ["شارژ بی‌سیم", "شارژ بی سیم", "wireless charg", "شارژر وایرلس"]),
        ("مقاومت در برابر آب و تعریق", SpecTopic.Water, ["ضد آب", "ضدآب", "مقاوم در برابر آب", "مقاومت در برابر آب", "ipx"]),
        ("اتصال هم‌زمان به دو دستگاه", SpecTopic.Bluetooth, ["multipoint", "مولتی پوینت", "اتصال همزمان به دو", "اتصال هم‌زمان به دو"]),
        ("کنترل لمسی", SpecTopic.Generic, ["کنترل لمسی", "لمسی", "touch control"]),
        ("پشتیبانی از دستیار صوتی", SpecTopic.Generic, ["دستیار صوتی", "siri", "google assistant", "گوگل اسیستنت"]),
        ("حالت بازی با تأخیر کم", SpecTopic.Generic, ["حالت بازی", "game mode", "تاخیر کم", "تأخیر کم", "low latency"]),
        ("پشتیبانی از کارت حافظه", SpecTopic.Storage, ["کارت حافظه", "microsd", "micro sd"]),
        ("نمایشگر سطح شارژ", SpecTopic.Battery, ["نمایشگر شارژ", "نمایش میزان شارژ", "نشانگر شارژ", "led نمایش شارژ"]),
        ("بدنهٔ فلزی", SpecTopic.Material, ["بدنه فلزی", "بدنهٔ فلزی", "بدنه آلومینیومی", "بدنهٔ آلومینیومی"]),
        ("قابلیت تاشو و حمل آسان", SpecTopic.Weight, ["تاشو", "قابل حمل", "حمل آسان"]),
    ];

    /// <summary>عنوان‌هایی که بعدشان فهرست «اقلام داخل جعبه» می‌آید.</summary>
    [GeneratedRegex(@"(?:محتویات|اقلام|لوازم|موارد)\s*(?:داخل|همراه|درون)?\s*(?:جعبه|بسته|بسته‌بندی|کارتن)|اقلام\s*همراه|in\s*the\s*box|what'?s\s*in\s*the\s*box",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex InBoxHeading();

    /// <summary>«مناسب برای X»، «ایده‌آل برای X»، «مخصوص X»، «گزینه‌ای عالی برای X».</summary>
    [GeneratedRegex(@"(?:مناسب|ایده[‌ ]?آل|مخصوص|عالی|طراحی\s*شده)\s+(?:برای|جهت)\s+([^.،,؛;!?؟\n\r()]{3,60})", RegexOptions.CultureInvariant)]
    private static partial Regex UseCasePattern();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    /// <summary>کلمه‌هایی که «کاربرد» را به جملهٔ ادامه‌دار تبدیل می‌کنند؛ کاربرد از همان‌جا بریده می‌شود.</summary>
    private static readonly string[] UseCaseStops =
    [
        " است", " می‌باشد", " میباشد", " هست", " بوده", " خواهد", " که ", " و همچنین", " را ", " می‌شود", " میشود", " می‌کند", " است.",
    ];

    /// <summary>واژه‌های بی‌ارزش یا تبلیغاتی که نباید به عنوان کاربرد/قلم جعبه وارد متن شوند.</summary>
    private static readonly string[] Junk =
    [
        "بهترین", "ارزان", "تخفیف", "قیمت", "خرید", "فروش", "ارسال", "رایگان", "گارانتی", "تماس", "واتساپ", "تلگرام", "اینستاگرام", "http", "www", "@",
        "شما", "همه", "هر کسی", "همه افراد",
    ];

    /// <summary>
    /// تحلیل متن منابع. منبع تکراری (همان Source) و متن کوتاه‌تر از ۸۰ نویسه کنار گذاشته می‌شود.
    /// <para>هرگز استثنا نمی‌دهد؛ ورودی خالی ← <see cref="ReviewNotes"/> بدون داده‌های تحقیق (ResearchSources=0).</para>
    /// </summary>
    public static ResearchDigest Analyze(IEnumerable<ResearchDoc>? docs)
    {
        var clean = (docs ?? [])
            .Where(d => d is not null && !string.IsNullOrWhiteSpace(d.Text) && !string.IsNullOrWhiteSpace(d.Source))
            .GroupBy(d => d.Source.Trim().ToLowerInvariant())
            .Select(g => g.OrderByDescending(d => d.Text.Length).First())
            .Select(d => d with { Text = Normalize(d.Text) })
            .Where(d => d.Text.Length >= 80)
            .Take(12)
            .ToList();
        if (clean.Count == 0)
            return ResearchDigest.Empty;

        // ترتیب موضوع‌ها: تعداد منبع‌هایی که دربارهٔ هر موضوع چیزی نوشته‌اند (حداقل یک منبع).
        var topics = TopicWords
            .Select(t => (t.Topic, Count: clean.Count(d => t.Words.Any(w => Mentions(d.Text, w)))))
            .Where(t => t.Count > 0)
            .OrderByDescending(t => t.Count)
            .Select(t => t.Topic)
            .ToList();

        var inBox = Agreed(clean, InBoxItems, 8);
        var uses = Agreed(clean, UseCases, 4);

        var highlights = new List<ResearchHighlight>();
        foreach (var (label, topic, patterns) in FeatureLexicon)
        {
            var supporters = clean.Where(d => patterns.Any(p => Mentions(d.Text, p))).ToList();
            var score = supporters.Count + (supporters.Any(d => d.Trusted) ? 1 : 0);
            if (score >= 2 && !Negated(supporters, patterns))
                highlights.Add(new ResearchHighlight(label, topic, supporters.Count));
        }

        return new ResearchDigest(clean.Count, topics, inBox, uses,
            highlights.OrderByDescending(h => h.Sources).Take(4).ToList());
    }

    /// <summary>ساخت <see cref="ReviewNotes"/> از نتیجهٔ تحقیق و (اختیاری) نکته‌های تأییدشدهٔ خریداران.</summary>
    public static ReviewNotes ToNotes(ResearchDigest digest, ReviewNotes? buyers) => new()
    {
        TopicOrder = digest.TopicOrder,
        InBox = digest.InBox,
        UseCases = digest.UseCases,
        Highlights = digest.Highlights,
        ResearchSources = digest.Sources,
        Pros = buyers?.Pros ?? [],
        Cons = buyers?.Cons ?? [],
        ForWhom = buyers?.ForWhom ?? [],
        Verdict = buyers?.Verdict,
        ReviewCount = buyers?.ReviewCount ?? 0,
    };

    /// <summary>
    /// موردهای مورد توافق: هر مورد اگر در ≥۲ منبع یا در یک منبع معتبر آمده باشد پذیرفته می‌شود. ترتیب = تعداد منبع.
    /// کلید مقایسه بدون فاصله/نیم‌فاصله است تا «کابل شارژ» و «کابل‌شارژ» یکی حساب شوند.
    /// </summary>
    private static List<string> Agreed(List<ResearchDoc> docs, Func<string, IEnumerable<string>> extract, int take)
    {
        var counts = new Dictionary<string, (string Text, int Count, bool Trusted)>(StringComparer.Ordinal);
        foreach (var doc in docs)
        {
            foreach (var item in extract(doc.Text).Distinct(StringComparer.Ordinal))
            {
                var key = Key(item);
                if (key.Length < 2)
                    continue;
                counts[key] = counts.TryGetValue(key, out var old)
                    ? (old.Text.Length <= item.Length ? old.Text : item, old.Count + 1, old.Trusted || doc.Trusted)
                    : (item, 1, doc.Trusted);
            }
        }
        return counts.Values
            .Where(v => v.Count >= 2 || v.Trusted)
            .OrderByDescending(v => v.Count)
            .ThenBy(v => v.Text.Length)
            .Select(v => v.Text)
            .Take(take)
            .ToList();
    }

    /// <summary>
    /// اقلام داخل جعبه از ۳۰۰ نویسهٔ بعد از عنوان «محتویات جعبه». جداکننده‌ها: «،» «,» «؛» خط تازه، بولت و « و ».
    /// مثال: «محتویات جعبه: هدفون، کابل شارژ USB-C و دفترچه راهنما.» ← [هدفون، کابل شارژ USB-C، دفترچه راهنما].
    /// </summary>
    public static IEnumerable<string> InBoxItems(string text)
    {
        foreach (Match heading in InBoxHeading().Matches(text))
        {
            var start = heading.Index + heading.Length;
            var window = text.Substring(start, Math.Min(300, text.Length - start));
            var end = window.IndexOfAny(['.', '!', '؟', '?']);
            if (end > 0)
                window = window[..end];
            // خط تیره فقط وقتی جداکننده است که دو طرفش فاصله باشد («USB-C» نباید بشکند).
            foreach (var raw in Regex.Split(window, @"[،,؛;:\n\r•\*\|]|\s[-–]\s|\s+و\s+|\s+\+\s+"))
            {
                var item = Spaces().Replace(raw, " ").Trim(' ', '.', '«', '»', '"', '(', ')');
                foreach (var prefix in new[] { "شامل ", "عبارتند از ", "عبارت است از ", "یک عدد ", "۱ عدد ", "1 عدد " })
                {
                    if (item.StartsWith(prefix, StringComparison.Ordinal))
                        item = item[prefix.Length..].Trim();
                }
                var words = item.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
                if (item is "شامل" or "عبارتند از" or "عبارت است از" or "موارد زیر" or "این موارد")
                    continue;
                if (item.Length is < 3 or > 40 || words > 5 || IsJunk(item) || item.All(ch => char.IsDigit(ch) || ch == ' '))
                    continue;
                yield return item;
            }
        }
    }

    /// <summary>
    /// کاربردها از «مناسب برای X». X تا اولین کلمهٔ فعلی/ربطی بریده می‌شود و حداکثر ۵ کلمه است.
    /// مثال: «این هدفون مناسب برای ورزش و پیاده‌روی است» ← «ورزش و پیاده‌روی».
    /// </summary>
    public static IEnumerable<string> UseCases(string text)
    {
        foreach (Match match in UseCasePattern().Matches(text))
        {
            var phrase = " " + match.Groups[1].Value.Trim() + " ";
            foreach (var stop in UseCaseStops)
            {
                var at = phrase.IndexOf(stop, StringComparison.Ordinal);
                if (at >= 0)
                    phrase = phrase[..at];
            }
            // «ورزش و پیاده‌روی» دو کاربرد جدا است تا با «مناسب برای ورزش» منبع دیگر جمع شود.
            foreach (var part in Regex.Split(phrase, @"\s+و\s+|\s+یا\s+"))
            {
                var use = Spaces().Replace(part, " ").Trim(' ', '.', '«', '»');
                var words = use.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (words.Length is 0 or > 4 || use.Length is < 3 or > 40 || IsJunk(use))
                    continue;
                yield return use;
            }
        }
    }

    /// <summary>
    /// متن قابل خواندن «توضیحات» یک صفحهٔ فروشگاه: description در JSON-LD محصول، og/meta description و
    /// پنل توضیحات ووکامرس/قالب‌های رایج. حداکثر <see cref="MaxDocChars"/> نویسه؛ صفحهٔ بی‌توضیح ← رشتهٔ خالی.
    /// </summary>
    public static string PageText(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return "";
        var head = html.Length > 1_500_000 ? html[..1_500_000] : html;
        var parts = new List<string>();
        foreach (var block in SmartParsers.JsonLdDescriptions(head))
            parts.Add(block);
        foreach (Match m in MetaDescription().Matches(head.Length > 300_000 ? head[..300_000] : head))
            parts.Add(System.Net.WebUtility.HtmlDecode(m.Groups["v"].Value));
        foreach (Match m in DescriptionPanel().Matches(head))
        {
            var start = m.Index + m.Length;
            var chunk = head.Substring(start, Math.Min(40_000, head.Length - start));
            // تا شروع زبانهٔ بعدی (مشخصات/نظرات) — متن آن‌ها «توضیحات» نیست.
            var stop = NextPanel().Match(chunk);
            if (stop.Success)
                chunk = chunk[..stop.Index];
            parts.Add(PersianText.HtmlToText(StripScripts().Replace(chunk, " ")));
            if (parts.Sum(x => x.Length) > MaxDocChars)
                break;
        }
        var text = Normalize(string.Join("\n", parts));
        return text.Length > MaxDocChars ? text[..MaxDocChars] : text;
    }

    /// <summary>
    /// متن «معرفی/بررسی تخصصی» کالای دیجی‌کالا از پاسخ JSON نسخهٔ ۲ (data.product.expert_reviews و data.product.review).
    /// ساختار این پاسخ گاهی عوض می‌شود؛ پس به‌جای مسیر ثابت، همهٔ رشته‌های بلند (≥۴۰ نویسه) زیر این دو شاخه جمع می‌شوند.
    /// </summary>
    public static string DigikalaText(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return "";
        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 64 });
            if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object
                || !data.TryGetProperty("product", out var product) || product.ValueKind != JsonValueKind.Object)
                return "";
            var parts = new List<string>();
            foreach (var name in new[] { "expert_reviews", "review", "introduce", "description" })
            {
                if (product.TryGetProperty(name, out var branch))
                    Collect(branch, parts, 0);
            }
            var text = Normalize(string.Join("\n", parts.Select(PersianText.HtmlToText)));
            return text.Length > MaxDocChars ? text[..MaxDocChars] : text;
        }
        catch (JsonException)
        {
            return "";
        }
    }

    private static void Collect(JsonElement element, List<string> into, int depth)
    {
        if (depth > 8 || into.Sum(x => x.Length) > MaxDocChars)
            return;
        switch (element.ValueKind)
        {
            case JsonValueKind.String when element.GetString() is { Length: >= 40 } value && !value.StartsWith("http", StringComparison.OrdinalIgnoreCase):
                into.Add(value);
                break;
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (property.Name is "url" or "image" or "images" or "webp_url" or "video")
                        continue;
                    Collect(property.Value, into, depth + 1);
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray().Take(40))
                    Collect(item, into, depth + 1);
                break;
        }
    }

    /// <summary>آیا منبع‌ها ویژگی را «نفی» کرده‌اند؟ مثال: «فاقد حذف نویز فعال» یا «ANC ندارد» ← پذیرفته نمی‌شود.</summary>
    private static bool Negated(List<ResearchDoc> supporters, string[] patterns)
    {
        var negative = 0;
        foreach (var doc in supporters)
        {
            foreach (var pattern in patterns)
            {
                var at = doc.Text.IndexOf(pattern, StringComparison.OrdinalIgnoreCase);
                if (at < 0 || !Mentions(doc.Text, pattern))
                    continue;
                var before = doc.Text.Substring(Math.Max(0, at - 12), Math.Min(12, at));
                var after = doc.Text.Substring(at + pattern.Length, Math.Min(14, doc.Text.Length - at - pattern.Length));
                if (before.Contains("فاقد", StringComparison.Ordinal) || before.Contains("بدون", StringComparison.Ordinal)
                    || after.Contains("ندارد", StringComparison.Ordinal) || after.Contains("نیست", StringComparison.Ordinal))
                    negative++;
                break;
            }
        }
        return negative * 2 >= supporters.Count;
    }

    /// <summary>
    /// آیا متن به این عبارت اشاره کرده است؟ عبارت‌های کوتاه لاتین (مثل «anc») فقط به‌صورت واژهٔ کامل پذیرفته می‌شوند
    /// تا «performance» یا «balance» به اشتباه «ANC» حساب نشوند (عدد کنارش مجاز است: «IPX5» ← «ipx»، «5000mAh» ← «mah»).
    /// مثال: Mentions("ANC هم دارد", "anc") ← true؛ Mentions("high performance", "anc") ← false.
    /// </summary>
    public static bool Mentions(string text, string pattern)
    {
        if (pattern.Length <= 5 && pattern.All(ch => ch is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9'))
            return Regex.IsMatch(text, @"(?<![A-Za-z])" + Regex.Escape(pattern) + @"(?![A-Za-z])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return text.Contains(pattern, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsJunk(string text) => Junk.Any(j => text.Contains(j, StringComparison.OrdinalIgnoreCase));

    private static string Key(string text) => PersianText.Normalize(text).Replace(" ", "", StringComparison.Ordinal).Replace("\u200c", "", StringComparison.Ordinal);

    private static string Normalize(string text)
    {
        // خط‌ها حفظ می‌شوند (جداکنندهٔ اقلام فهرست‌ها)؛ فقط فاصله‌های اضافهٔ داخل هر خط و حروف عربی یکسان می‌شوند.
        var source = text.Length > MaxDocChars * 2 ? text[..(MaxDocChars * 2)] : text;
        var clean = string.Join('\n', source.Replace("\r", "", StringComparison.Ordinal).Split('\n').Select(PersianText.Clean).Where(l => l.Length > 0));
        return clean.Length > MaxDocChars ? clean[..MaxDocChars] : clean;
    }

    [GeneratedRegex("<meta[^>]+(?:name|property)=[\"'](?:og:description|description|twitter:description)[\"'][^>]*content=[\"'](?<v>[^\"']{20,1000})[\"']", RegexOptions.IgnoreCase)]
    private static partial Regex MetaDescription();

    [GeneratedRegex("<(?:div|section|article)[^>]+(?:id|class)=[\"'][^\"']*(?:tab-description|woocommerce-Tabs-panel--description|product-description|product__description|description-content|product-desc|productDescription)[^\"']*[\"'][^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex DescriptionPanel();

    [GeneratedRegex("<(?:div|section)[^>]+(?:id|class)=[\"'][^\"']*(?:tab-additional_information|tab-reviews|woocommerce-Tabs-panel--reviews|related|upsells|comments)[^\"']*[\"']", RegexOptions.IgnoreCase)]
    private static partial Regex NextPanel();

    [GeneratedRegex("<(script|style|noscript)[^>]*>.*?</\\1>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex StripScripts();
}

/// <summary>
/// نتیجهٔ تحقیق یک کالا. Sources = تعداد منبع تحلیل‌شده؛ بقیه همان خروجی‌های <see cref="ProductResearch.Analyze"/>.
/// </summary>
public sealed record ResearchDigest(
    int Sources,
    IReadOnlyList<SpecTopic> TopicOrder,
    IReadOnlyList<string> InBox,
    IReadOnlyList<string> UseCases,
    IReadOnlyList<ResearchHighlight> Highlights)
{
    public static readonly ResearchDigest Empty = new(0, [], [], [], []);

    /// <summary>خلاصهٔ یک‌خطی برای گزارش کار. مثال: «۴ منبع؛ ۳ قلم جعبه، ۲ کاربرد، ۱ ویژگی».</summary>
    public string Summary =>
        $"{PersianText.Digits(Sources)} منبع؛ {PersianText.Digits(InBox.Count)} قلم جعبه، {PersianText.Digits(UseCases.Count)} کاربرد، {PersianText.Digits(Highlights.Count)} ویژگی";
}
