using System.Numerics;
using Fratak_Shop.Core.Services.Seo.Engine;
using Fratak_Shop.Core.Services.Seo.Knowledge;

namespace Fratak_Shop.Core.Services.Seo.Autopilot;

/// <summary>یک خطر جریمه در سطح صفحه/سایت. Blocking = متن خودکار با این خطر ذخیره نمی‌شود.</summary>
public sealed record PenaltyRisk(string Code, string Message, bool Blocking);

/// <summary>
/// نگهبان ضدجریمه (موارد ۱۶۵ تا ۱۷۶ پیشنهاد سئو). هر تغییر خودکار پیش از ذخیره از اینجا می‌گذرد.
/// <para>بررسی‌ها (همه برگرفته از Spam Policies گوگل):</para>
/// <list type="bullet">
/// <item>همهٔ قانون‌های <see cref="SpamGuard"/> (انباشت کلمهٔ کلیدی، اسپم، متن پنهان، پررنگ زیاد).</item>
/// <item>محتوای تقریباً تکراری با صفحهٔ دیگر سایت (SimHash) ← خطر Doorway/Duplicate.</item>
/// <item>لینک بیش از حد (بیش از ۳ لینک در هر ۱۰۰ واژه).</item>
/// <item>تغییر پی‌درپی (بازنویسی زودتر از دورهٔ استراحت) ← گوگل فرصت سنجش ندارد.</item>
/// </list>
/// <para>کجا استفاده می‌شود؟ <c>SeoAutopilotService</c> (پیش از هر ذخیرهٔ خودکار) و صفحهٔ «ضدجریمه» پنل.</para>
/// </summary>
public static class SeoPenaltyGuard
{
    /// <summary>فاصلهٔ همینگ SimHash برابر یا کمتر از این ← دو متن تقریباً یکسان‌اند.</summary>
    public const int NearDuplicateDistance = 3;

    /// <summary>بیشترین لینک در هر ۱۰۰ واژه.</summary>
    public const double MaxLinksPer100Words = 3;

    /// <summary>
    /// بررسی متن یک صفحه. <paramref name="otherHashes"/> = SimHash صفحه‌های دیگر (برای تکراری بودن).
    /// مثال: عنوانی که کلمهٔ کلیدی را سه بار تکرار کرده ← خطر «KeywordInTitle» با Blocking=true.
    /// </summary>
    public static List<PenaltyRisk> Check(string? title, string? meta, string? bodyHtml, string? keyword,
        SeoKnowledgeBase? knowledge, IReadOnlyCollection<long>? otherHashes = null, DateTimeOffset? lastRewriteAt = null,
        int cooldownDays = 21, DateTimeOffset? now = null)
    {
        var risks = SpamGuard.Inspect(title, meta, bodyHtml, keyword, knowledge)
            .Select(i => new PenaltyRisk(i.Code, i.Message, i.Severity >= 2))
            .ToList();

        var stats = PersianText.AnalyzeHtml(bodyHtml);
        if (stats.Words >= 80)
        {
            var links = stats.Links.Count;
            if (links * 100.0 / stats.Words > MaxLinksPer100Words)
                risks.Add(new("TooManyLinks", $"لینک‌های متن زیاد است ({PersianText.Digits(links)} لینک برای {PersianText.Digits(stats.Words)} واژه).", true));
        }

        if (otherHashes is { Count: > 0 } && stats.Words >= 60)
        {
            var hash = SimHash(stats.Text);
            if (otherHashes.Any(other => other != 0 && Distance(hash, other) <= NearDuplicateDistance))
                risks.Add(new("NearDuplicate", "این متن تقریباً با صفحهٔ دیگری از سایت یکسان است (خطر محتوای تکراری).", true));
        }

        if (lastRewriteAt is { } last && (now ?? DateTimeOffset.UtcNow) - last < TimeSpan.FromDays(Math.Max(21, cooldownDays)))
            risks.Add(new("Cooldown", "این صفحه به‌تازگی بازنویسی شده؛ تغییر دوباره پیش از سنجش نتیجه ممنوع است.", true));
        return risks;
    }

    /// <summary>آیا متن خودکار اجازهٔ ذخیره دارد؟ (هیچ خطر Blocking نباشد)</summary>
    public static bool IsSafe(IEnumerable<PenaltyRisk> risks) => !risks.Any(r => r.Blocking);

    /// <summary>
    /// اثر انگشت ۶۴ بیتی متن (SimHash روی سه‌واژه‌ای‌ها). متن‌های شبیه، عددهای نزدیک (فاصلهٔ همینگ کم) می‌گیرند.
    /// متن خیلی کوتاه ← صفر (مقایسه نمی‌شود).
    /// </summary>
    public static long SimHash(string? text)
    {
        var tokens = PersianText.Tokenize(text).Where(t => !PersianText.IsStopWord(t)).Select(PersianText.Stem).ToList();
        if (tokens.Count < 8)
            return 0;
        Span<int> weights = stackalloc int[64];
        for (var i = 0; i + 2 < tokens.Count; i++)
        {
            var hash = Fnv64(tokens[i] + " " + tokens[i + 1] + " " + tokens[i + 2]);
            for (var bit = 0; bit < 64; bit++)
                weights[bit] += (hash >> bit & 1UL) == 1UL ? 1 : -1;
        }

        ulong result = 0;
        for (var bit = 0; bit < 64; bit++)
        {
            if (weights[bit] > 0)
                result |= 1UL << bit;
        }

        return unchecked((long)result);
    }

    /// <summary>فاصلهٔ همینگ دو SimHash (تعداد بیت‌های متفاوت؛ ۰ = یکسان).</summary>
    public static int Distance(long a, long b) => BitOperations.PopCount(unchecked((ulong)(a ^ b)));

    private static ulong Fnv64(string value)
    {
        var hash = 14695981039346656037UL;
        foreach (var ch in value)
        {
            hash ^= ch;
            hash *= 1099511628211UL;
        }

        return hash;
    }
}
