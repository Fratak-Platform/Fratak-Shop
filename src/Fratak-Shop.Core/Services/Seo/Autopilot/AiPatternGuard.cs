using System.Text.RegularExpressions;
using Fratak_Shop.Core.Services.Seo.Engine;

namespace Fratak_Shop.Core.Services.Seo.Autopilot;

/// <summary>یک الگوی «متن ماشینی/کلیشه‌ای» که در متن پیدا شد.</summary>
/// <param name="Phrase">عبارت پیدا‌شده.</param>
/// <param name="Kind">نوع: Filler (پرکننده)، Cliche (کلیشه)، Claim (ادعای اثبات‌نشده)، Repetition (تکرار شروع جمله).</param>
/// <param name="Hint">راهنمای فارسی برای نویسنده.</param>
public sealed record AiPatternFinding(string Phrase, string Kind, string Hint);

/// <summary>گزارش طبیعی بودن متن: نمرهٔ ۰ تا ۱۰۰ (بیشتر = طبیعی‌تر) و یافته‌ها.</summary>
public sealed record AiPatternReport(int Score, IReadOnlyList<AiPatternFinding> Findings, double SentenceLengthSpread);

/// <summary>
/// نگهبان «ردپای متن ماشینی» (موارد ۹۶ تا ۱۱۵ پیشنهاد سئو).
/// <para>هدف «پنهان کردن» هوش مصنوعی نیست (گوگل متن مفید را جریمه نمی‌کند)؛ هدف حذف نشانه‌هایی است که متن را کم‌ارزش و
/// تکراری نشان می‌دهند: جمله‌های پرکننده («در دنیای امروز...»)، کلیشه‌ها، ادعاهای اثبات‌نشده («۱۰۰٪ تضمینی») و جمله‌هایی
/// که همه یک طول و یک شروع دارند. همین نشانه‌ها در «Helpful Content» گوگل منفی‌اند.</para>
/// <para>کجا استفاده می‌شود؟ بررسی زندهٔ ویرایشگر (هشدار به نویسنده) و پیش از ذخیرهٔ هر متن خودکار (پاک‌سازی).</para>
/// </summary>
public static class AiPatternGuard
{
    /// <summary>جمله‌های پرکننده که بدون از دست رفتن معنا حذف می‌شوند (معمولاً اول جمله).</summary>
    public static readonly string[] Fillers =
    [
        "در دنیای امروز", "در عصر حاضر", "در دنیای پرشتاب امروز", "همان‌طور که می‌دانید", "همانطور که می‌دانید",
        "قابل ذکر است که", "لازم به ذکر است که", "شایان ذکر است که", "در این مقاله قصد داریم", "در این مطلب قصد داریم",
        "با ما همراه باشید", "تا انتهای این مقاله با ما همراه باشید", "در ادامه با ما همراه باشید", "بدون شک", "بی‌شک",
        "در نهایت می‌توان گفت", "به طور کلی می‌توان گفت", "امروزه", "بیایید نگاهی بیندازیم", "در این راهنمای جامع",
    ];

    /// <summary>کلیشه‌های پرتکرار متن‌های ماشینی (حذف نمی‌شوند؛ فقط هشدار).</summary>
    public static readonly string[] Cliches =
    [
        "نقش بسیار مهمی", "از اهمیت بالایی برخوردار", "یکی از مهم‌ترین", "حرف اول را می‌زند", "دنیای شگفت‌انگیز",
        "سفری هیجان‌انگیز", "تجربه‌ای بی‌نظیر", "انتخابی هوشمندانه", "گامی بزرگ", "در یک کلام", "جای تعجب نیست",
        "کلید موفقیت", "راز موفقیت", "به سطح بالاتری", "همراهی همیشگی", "تحولی شگرف",
    ];

    /// <summary>ادعاهای اثبات‌نشده و جایگزین امن آن‌ها (در متن خودکار جایگزین می‌شوند).</summary>
    public static readonly IReadOnlyDictionary<string, string> Claims = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["۱۰۰٪ تضمینی"] = "با ضمانت",
        ["صد در صد تضمینی"] = "با ضمانت",
        ["تضمینی"] = "با ضمانت",
        ["بی‌رقیب"] = "خوب",
        ["بی‌نظیرترین"] = "از گزینه‌های خوب",
        ["بی‌نظیر"] = "کم‌نظیر",
        ["شماره یک بازار"] = "پرطرفدار",
        ["معجزه‌آسا"] = "مؤثر",
        ["انقلابی"] = "تازه",
        ["فوق‌العاده‌ترین"] = "از بهترین‌ها",
    };

    /// <summary>
    /// بررسی متن ساده (نه HTML). مثال: «در دنیای امروز، هدفون بی‌نظیر است.» ← یافته‌های Filler و Claim و نمرهٔ کمتر.
    /// </summary>
    public static AiPatternReport Analyze(string? text)
    {
        var findings = new List<AiPatternFinding>();
        var value = text ?? "";
        if (value.Trim().Length == 0)
            return new AiPatternReport(100, findings, 0);

        foreach (var filler in Fillers.Where(f => Contains(value, f)))
            findings.Add(new(filler, "Filler", $"«{filler}» چیزی به متن اضافه نمی‌کند؛ مستقیم سر اصل مطلب بروید."));
        foreach (var cliche in Cliches.Where(c => Contains(value, c)))
            findings.Add(new(cliche, "Cliche", $"«{cliche}» کلیشه است؛ به جایش یک واقعیت مشخص (عدد، تجربه، مقایسه) بنویسید."));
        foreach (var claim in Claims.Keys.Where(c => Contains(value, c)))
            findings.Add(new(claim, "Claim", $"«{claim}» ادعای اثبات‌نشده است (ریسک در گوگل و مرچنت سنتر)."));

        var sentences = PersianText.Sentences(value).Where(s => s.Trim().Length > 0).ToList();
        var openings = sentences
            .Select(s => PersianText.Tokenize(s).FirstOrDefault())
            .Where(w => !string.IsNullOrEmpty(w))
            .GroupBy(w => w!, StringComparer.Ordinal)
            .Where(g => g.Count() >= 3 && g.Count() * 3 >= sentences.Count)
            .Select(g => g.Key);
        foreach (var opening in openings)
            findings.Add(new(opening, "Repetition", $"بیشتر جمله‌ها با «{opening}» شروع می‌شوند؛ شروع جمله‌ها را متنوع کنید."));

        var spread = Spread(sentences);
        var score = 100 - findings.Count(f => f.Kind == "Filler") * 6 - findings.Count(f => f.Kind == "Cliche") * 4
                    - findings.Count(f => f.Kind == "Claim") * 8 - findings.Count(f => f.Kind == "Repetition") * 8;
        // جمله‌های هم‌طول (پراکندگی کم) نشانهٔ متن قالبی است؛ فقط وقتی جمله به اندازهٔ کافی هست.
        if (sentences.Count >= 6 && spread < 0.25)
            score -= 10;
        return new AiPatternReport(Math.Clamp(score, 0, 100), findings, Math.Round(spread, 2));
    }

    /// <summary>
    /// پاک‌سازی متن خودکار: حذف پرکننده‌ها و جایگزینی ادعاها. متن انسانی را با این متد عوض نکنید (فقط هشدار بدهید).
    /// مثال: «در دنیای امروز، این هدفون تضمینی است.» ← «این هدفون با ضمانت است.»
    /// </summary>
    public static string Clean(string? text, IReadOnlyList<string>? blockedWords = null)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text ?? "";
        var result = text;
        foreach (var filler in Fillers.OrderByDescending(f => f.Length))
            result = Replace(result, filler + "،", "");
        foreach (var filler in Fillers.OrderByDescending(f => f.Length))
            result = Replace(result, filler, "");
        foreach (var (claim, safe) in Claims.OrderByDescending(c => c.Key.Length))
            result = Replace(result, claim, safe);
        foreach (var word in blockedWords ?? [])
            result = Replace(result, word, "");
        result = Regex.Replace(result, @"[ \t]{2,}", " ", RegexOptions.None, TimeSpan.FromMilliseconds(200));
        result = Regex.Replace(result, @"(^|[.!؟?]\s*)[،,]\s*", "$1", RegexOptions.None, TimeSpan.FromMilliseconds(200));
        result = Regex.Replace(result, @"\s+([.،؟!])", "$1", RegexOptions.None, TimeSpan.FromMilliseconds(200));
        return result.Trim();
    }

    /// <summary>ضریب پراکندگی طول جمله‌ها (انحراف معیار ÷ میانگین). متن انسانی معمولاً بالای ۰٫۳ است.</summary>
    public static double Spread(IReadOnlyList<string> sentences)
    {
        var lengths = sentences.Select(s => (double)PersianText.WordCount(s)).Where(l => l > 0).ToList();
        if (lengths.Count < 2)
            return 1;
        var mean = lengths.Average();
        var variance = lengths.Sum(l => (l - mean) * (l - mean)) / lengths.Count;
        return mean <= 0 ? 0 : Math.Sqrt(variance) / mean;
    }

    private static bool Contains(string text, string phrase) => InternalLinker.FindWhole(text, phrase) >= 0;

    private static string Replace(string text, string phrase, string replacement)
    {
        var guard = 0;
        int index;
        while ((index = InternalLinker.FindWhole(text, phrase)) >= 0 && guard++ < 50)
            text = text[..index] + replacement + text[(index + phrase.Length)..];
        return text;
    }
}
