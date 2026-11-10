using System.Text.RegularExpressions;
using Fratak_Shop.Core.Services.Seo.Engine;

namespace Fratak_Shop.Core.Services.Smart;

/// <summary>
/// دور ۲۳ – «دروازهٔ کیفیت متن»: هیچ متن هوشمندی (توضیحات، توضیح کوتاه، مقاله، پاسخ دستیار) بدون گذشتن از این بررسی ذخیره نمی‌شود.
/// <para>چرا؟ قبلاً متنی مثل «برای دیدن گزینه‌های دیگر، صفحهٔ دسته‌بندی نشده را ببینید» نوشته شد؛ آن صفحه وجود ندارد و جمله غلط است.</para>
/// <list type="bullet">
/// <item><see cref="Issues"/>: فهرست ایرادها (خالی = سالم). مثال: «اشاره به دستهٔ سیستمی دسته‌بندی نشده».</item>
/// <item><see cref="Clean"/>: جمله/بند/ردیف فهرستی که ایراد «بی‌جا» دارد حذف می‌شود و بقیهٔ متن سالم می‌ماند.</item>
/// </list>
/// استفاده: <c>SeoAutopilotService.ApplyBodyAsync</c> (ذخیرهٔ توضیحات کالا)، تعمیر شبانهٔ متن‌های قدیمی (SmartWorker) و آزمون‌ها.
/// </summary>
public static class ContentQualityGate
{
    // عبارت‌هایی که هرگز نباید در متن عمومی بیایند (دستهٔ انتظار، نشانی داخلی، کلمه‌های بی‌معنای برنامه‌نویسی).
    private static readonly string[] ForbiddenPhrases =
    [
        "دسته بندی نشده", "دسته‌بندی نشده", "دسته‌بندی‌نشده", "دستهبندی نشده", "بدون دسته‌بندی", "بدون دسته بندی",
        "uncategorized", "lorem ipsum",
    ];

    // نشانه‌های قالب پرنشده: {LINKS}، {Name}، [الف|ب]، [[deals:8]] در متن ذخیره‌شده‌ای که شورت‌کد نیست.
    private static readonly Regex TemplateToken = new(@"\{[A-Za-z_][A-Za-z0-9_]*\}|\[[^\[\]\|]{1,40}\|[^\[\]]{1,80}\]", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200));

    // کلمه‌های خام برنامه که گاهی جای مقدار خالی می‌نشینند: «قیمت: null»، «undefined».
    private static readonly Regex RawValue = new(@"(?<![A-Za-z])(null|undefined|NaN|System\.[A-Za-z]+)(?![A-Za-z])", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200));

    // لینک به دستهٔ سیستمی یا لینک خالی.
    private static readonly Regex BadLink = new(@"href\s*=\s*[""'](\s*|#|/category/uncategorized[^""']*)[""']", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200));

    // جمله‌ها برای پیدا کردن تکرار.
    private static readonly Regex SentenceSplit = new(@"(?<=[\.!؟\?])\s+|<\s*/?\s*(p|li|h[1-6]|br|div|td|th)[^>]*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200));

    // بلوک‌هایی که Clean می‌تواند جدا حذف کند.
    private static readonly Regex Block = new(@"<(p|li)\b[^>]*>.*?</\1>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(500));

    /// <summary>
    /// همهٔ ایرادهای متن (HTML یا متن ساده). خالی = سالم.
    /// مثال: «… صفحهٔ دسته‌بندی نشده را ببینید» ← ["اشاره به «دسته‌بندی نشده» …"].
    /// <para>style = false فقط ایرادهای «قطعی» (عبارت ممنوع، لینک خراب، قالب پرنشده) را می‌سنجد؛ برای متن‌های بلند نویسندهٔ سئو
    /// که جدول/پرسش‌وپاسخ دارند و تکرار کوتاهِ طبیعی در آن‌ها ایراد نیست.</para>
    /// </summary>
    public static IReadOnlyList<string> Issues(string? content, bool style = true)
    {
        var issues = new List<string>();
        if (string.IsNullOrWhiteSpace(content))
            return issues;
        if (HasForbidden(content))
            issues.Add("اشاره به «دسته‌بندی نشده» یا عبارت ممنوع؛ این صفحه وجود ندارد و جمله بی‌جاست.");
        if (BadLink.IsMatch(content))
            issues.Add("لینک خالی یا لینک به صفحه‌ای که وجود ندارد.");
        var plain = PersianText.HtmlToText(content);
        if (TemplateToken.IsMatch(plain))
            issues.Add("قالب پرنشده در متن ({…} یا [الف|ب]).");
        if (!style)
            return issues;
        if (RawValue.IsMatch(plain))
            issues.Add("مقدار خام برنامه (null/undefined) در متن.");
        if (RepeatedSentence(content) is { } repeated)
            issues.Add($"جملهٔ تکراری: «{PersianText.Truncate(repeated, 60)}».");
        return issues;
    }

    /// <summary>متن سالم است؟ مثال: «هدفون بی‌سیم با ۳۰ ساعت شارژ.» ← true.</summary>
    public static bool IsClean(string? content) => Issues(content).Count == 0;

    /// <summary>
    /// حذف «فقط» بخش‌های ایرادار: بند/ردیفی که به دستهٔ سیستمی اشاره یا لینک خالی دارد؛ در متن ساده، جملهٔ ایرادار.
    /// لینک خراب داخل بندِ سالم به متن ساده تبدیل می‌شود (کلمه می‌ماند، لینک می‌رود).
    /// مثال: «&lt;p&gt;خوب است.&lt;/p&gt;&lt;p&gt;صفحهٔ دسته‌بندی نشده را ببینید.&lt;/p&gt;» ← «&lt;p&gt;خوب است.&lt;/p&gt;».
    /// </summary>
    public static string Clean(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return content ?? "";
        var html = content;
        if (html.Contains('<', StringComparison.Ordinal))
        {
            // اول لینک‌های خراب باز می‌شوند (متن لینک می‌ماند)، بعد بند دارای عبارت ممنوع حذف می‌شود؛
            // مثال: «<p>برای خرید <a href="/category/uncategorized">این کالاها</a> عجله نکنید.</p>» ← همان بند بدون لینک.
            html = UnwrapBadLinks(html);
            html = Block.Replace(html, m => HasForbidden(m.Value) ? "" : m.Value);
            // اگر عبارت ممنوع بیرون از p/li مانده (مثلاً داخل div)، همان جمله حذف می‌شود.
            if (HasForbidden(html))
                html = DropSentences(html);
            return html.Trim();
        }
        return DropSentences(html).Trim();
    }

    /// <summary>متن عبارت ممنوع دارد؟ (مقایسه بعد از یکسان‌سازی فارسی؛ «دسته بندی نشده» و «دسته‌بندی نشده» یکی‌اند.)</summary>
    public static bool HasForbidden(string? content)
    {
        if (string.IsNullOrEmpty(content))
            return false;
        var normal = PersianText.Normalize(content);
        foreach (var phrase in ForbiddenPhrases)
        {
            if (normal.Contains(PersianText.Normalize(phrase), StringComparison.Ordinal))
                return true;
        }
        return content.Contains("/category/uncategorized", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>اولین جملهٔ تکراری (بیش از ۲۵ نویسه، بعد از یکسان‌سازی) یا null. مثال: دو بار «این کالا مناسب سفر است.» ← همان.</summary>
    public static string? RepeatedSentence(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return null;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var part in SentenceSplit.Split(content))
        {
            var text = PersianText.HtmlToText(part).Trim();
            if (text.Length <= 25)
                continue;
            var key = PersianText.Normalize(text).TrimEnd('.', '!', '؟', '?', ' ');
            if (!seen.Add(key))
                return text;
        }
        return null;
    }

    private static string UnwrapBadLinks(string html) =>
        Regex.Replace(html, @"<a\b[^>]*href\s*=\s*[""'](\s*|#|/category/uncategorized[^""']*)[""'][^>]*>(.*?)</a>", "$2",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(500));

    private static string DropSentences(string text)
    {
        // جمله‌ها با نقطه/علامت سؤال جدا می‌شوند؛ جملهٔ ایرادار کامل حذف می‌شود.
        var parts = Regex.Split(text, @"(?<=[\.!؟\?])\s+", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200));
        return string.Join(" ", parts.Where(p => !HasForbidden(p)));
    }
}
