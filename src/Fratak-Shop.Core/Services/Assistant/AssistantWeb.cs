using Fratak_Shop.Core.Services.Seo.Engine;

namespace Fratak_Shop.Core.Services.Assistant;

/// <summary>
/// جوابی که از نتایج جست‌وجوی اینترنت ساخته شد.
/// </summary>
/// <param name="Text">متن آمادهٔ نمایش (با بیان میزان اطمینان).</param>
/// <param name="Sources">تعداد منبع هم‌نظر (۰ = جوابی پیدا نشد).</param>
/// <param name="Hosts">نام سایت‌های منبع (حداکثر ۳).</param>
public sealed record AssistantWebAnswer(string Text, int Sources, IReadOnlyList<string> Hosts)
{
    /// <summary>جواب مطمئن‌تر: دست‌کم ۲ منبع جدا همین را گفته‌اند.</summary>
    public bool Confident => Sources >= 2;
}

/// <summary>
/// دور ۲۳ – ارزیابی نتایج جست‌وجوی اینترنت برای پرسشی که جوابش در دادهٔ فروشگاه نیست (مثلاً «باتری این پاوربانک چند میلی‌آمپر است؟»).
/// <list type="bullet">
/// <item><b>اعتبارسنجی:</b> نتیجه‌ای پذیرفته می‌شود که نام همین کالا (دست‌کم ۶۰٪ واژه‌ها و همهٔ کدهای مدل لاتین/عددی مثل A54 یا 20000) در عنوان یا متنش باشد
/// و جملهٔ مربوط به موضوع (واژهٔ موضوع) داشته باشد. کالای دیگری با نام شبیه رد می‌شود.</item>
/// <item><b>اطمینان:</b> ۲ منبع یا بیشتر ← «طبق چند منبع اینترنتی…»؛ یک منبع ← «مطمئن نیستم ولی طبق یک منبع…»؛ هیچ ← «جواب مطمئنی پیدا نکردم».</item>
/// <item>متن منبع بدون تغییر و کوتاه (حداکثر ۲۲۰ نویسه) نقل می‌شود؛ هیچ عددی ساخته نمی‌شود.</item>
/// </list>
/// محل استفاده: Web/Infrastructure/Assistant/AssistantLookup.cs (جست‌وجوی پس‌زمینه) و تست‌های Unit/AssistantTests.cs.
/// </summary>
public static class AssistantWeb
{
    /// <summary>طول بیشینهٔ جملهٔ نقل‌شده از منبع.</summary>
    public const int MaxQuote = 220;

    /// <summary>
    /// عبارت جست‌وجو. مثال: («پاوربانک شیائومی 20000»، «باتری») ← «پاوربانک شیائومی 20000 باتری مشخصات».
    /// </summary>
    public static string Query(string productName, string topicTitle) =>
        $"{productName.Trim()} {topicTitle.Trim()} مشخصات".Trim();

    /// <summary>
    /// ساخت جواب از نتایج. ورودی‌ها: نام کالا، عنوان و واژه‌های موضوع، نتایج جست‌وجو (عنوان + خلاصه).
    /// مثال: دو نتیجه که هر دو «شیائومی 20000» و «ظرفیت باتری 20000 میلی‌آمپر» دارند ← Sources = 2 و متن «طبق چند منبع اینترنتی …».
    /// </summary>
    public static AssistantWebAnswer Evaluate(string productName, string topicTitle, IReadOnlyCollection<string> topicWords, IReadOnlyList<SerpItem> results)
    {
        var nameWords = AssistantNlu.Words(productName);
        var codes = nameWords.Where(w => w.Any(char.IsDigit) || w.All(c => c is >= 'a' and <= 'z')).ToList();
        var topic = topicWords.Select(PersianText.Normalize).Where(w => w.Length >= 2).Distinct().ToList();
        var quotes = new List<(string Quote, string Host)>();
        foreach (var item in results.Take(10))
        {
            var haystack = PersianText.Normalize(item.Title + " " + item.Snippet);
            var words = AssistantNlu.Words(haystack);
            if (nameWords.Count == 0 || codes.Any(c => !words.Contains(c)))
                continue;
            var matched = nameWords.Count(words.Contains);
            if (matched < Math.Ceiling(nameWords.Count * 0.6))
                continue;
            var sentence = PersianText.Sentences(item.Snippet)
                .FirstOrDefault(s => topic.Any(t => PersianText.Normalize(s).Contains(t, StringComparison.Ordinal)));
            if (string.IsNullOrWhiteSpace(sentence))
                continue;
            var host = Host(item.Url);
            if (host is null || quotes.Any(q => q.Host == host))
                continue;
            sentence = sentence.Trim();
            quotes.Add((sentence.Length > MaxQuote ? sentence[..MaxQuote].TrimEnd() + "…" : sentence, host));
        }

        if (quotes.Count == 0)
            return new AssistantWebAnswer(
                $"متأسفانه دربارهٔ «{topicTitle}» این کالا جواب مطمئنی پیدا نکردم. پرسش شما ثبت شد تا مشخصات کالا کامل شود.", 0, []);

        var hosts = quotes.Select(q => q.Host).Take(3).ToList();
        var text = quotes.Count >= 2
            ? $"طبق چند منبع اینترنتی ({string.Join("، ", hosts)}): {quotes[0].Quote}"
            : $"مطمئن نیستم ولی طبق یک منبع اینترنتی ({hosts[0]}): {quotes[0].Quote}";
        return new AssistantWebAnswer(text + " — این اطلاعات از سایت‌های دیگر است؛ پیش از خرید با مشخصات صفحهٔ کالا تطبیق دهید.", quotes.Count, hosts);
    }

    /// <summary>نام سایت از آدرس (بدون www). آدرس نامعتبر یا غیر http(s) ← null.</summary>
    public static string? Host(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https"
            ? uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? uri.Host[4..] : uri.Host
            : null;
}
