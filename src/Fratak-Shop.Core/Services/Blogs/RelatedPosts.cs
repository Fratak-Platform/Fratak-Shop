using Fratak_Shop.Core.Utilities;

namespace Fratak_Shop.Core.Services.Blogs;

/// <summary>
/// یک مقالهٔ نامزد برای «مطالب مرتبط». فقط داده‌های سبک (بدون متن مقاله) تا رتبه‌بندی ارزان بماند.
/// </summary>
public sealed record RelatedCandidate(Guid Id, string Title, Guid CategoryId, DateTimeOffset PublishedAt);

/// <summary>
/// دور ۲۳ (مورد ۳۷): رتبه‌بندی «مطالب مرتبط» زیر/کنار هر مقاله – بدون هوش مصنوعی و بدون سرویس بیرونی.
/// <para>
/// قاعده (ساده و قابل‌پیش‌بینی):
/// • هم‌دسته بودن: ۳ امتیاز.
/// • هر واژهٔ معنادار مشترک در عنوان (حداقل ۳ حرف و نه واژهٔ عمومی مثل «بهترین»، «راهنمای»، «خرید»): ۲ امتیاز.
/// • مقالهٔ بی‌امتیاز اصلاً پیشنهاد نمی‌شود (لینک بی‌ربط = تجربهٔ بد و سیگنال بد برای گوگل).
/// • امتیاز برابر ← تازه‌تر جلوتر.
/// </para>
/// <para>مثال: برای «راهنمای خرید هدفون بی‌سیم»، «بررسی هدفون سونی» (واژهٔ مشترک «هدفون») بالاتر از یک مقالهٔ هم‌دستهٔ بی‌واژهٔ مشترک می‌آید.</para>
/// <para>استفاده: <see cref="PostService.GetRelatedPostsAsync"/> ← صفحهٔ مقاله (Pages/BlogPost).</para>
/// </summary>
public static class RelatedPosts
{
    /// <summary>واژه‌های عمومی عنوان‌ها که شباهت واقعی نمی‌سازند.</summary>
    private static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
    {
        "راهنمای", "راهنما", "خرید", "بهترین", "بررسی", "معرفی", "مقایسه", "انتخاب", "برای", "چگونه", "چطور", "نکات", "نکته",
        "کلیدی", "مشخصات", "ویژگی", "ویژگی‌های", "ویژگی ها", "برجسته", "کامل", "جدید", "سال", "قیمت", "کدام", "بخریم", "تفاوت",
        "های", "ها", "یک", "این", "آن", "با", "از", "به", "در", "که", "و", "یا", "تا",
    };

    /// <summary>واژه‌های معنادار عنوان (نرمال‌شده؛ «هدفون‌ها» و «هدفون ها» یکی‌اند). مثال: «راهنمای خرید هدفون بی‌سیم» ← {هدفون، بی، سیم}.</summary>
    public static HashSet<string> Words(string? title)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var raw in SearchText.Normalize(title).Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var word = raw.Trim('،', ',', '؛', ';', ':', '؟', '?', '!', '.', '(', ')', '«', '»', '"', '-');
            if (word.Length >= 3 && !StopWords.Contains(word) && !word.All(char.IsDigit))
                result.Add(word);
        }
        return result;
    }

    /// <summary>امتیاز یک نامزد نسبت به مقالهٔ فعلی (۰ یعنی بی‌ربط).</summary>
    public static int Score(HashSet<string> currentWords, Guid currentCategoryId, RelatedCandidate candidate)
    {
        var score = candidate.CategoryId == currentCategoryId ? 3 : 0;
        foreach (var word in Words(candidate.Title))
        {
            if (currentWords.Contains(word))
                score += 2;
        }
        return score;
    }

    /// <summary>
    /// بهترین <paramref name="take"/> نامزد مرتبط (بدون خود مقاله و بدون تکرار). مثال استفاده در تست‌ها:
    /// <c>RelatedPosts.Rank(id, "بررسی هدفون سونی", cat, candidates, 4)</c>.
    /// </summary>
    public static List<Guid> Rank(Guid currentId, string currentTitle, Guid currentCategoryId, IEnumerable<RelatedCandidate> candidates, int take)
    {
        if (take <= 0)
            return [];
        var words = Words(currentTitle);
        return candidates
            .Where(c => c.Id != currentId)
            .DistinctBy(c => c.Id)
            .Select(c => (c.Id, c.PublishedAt, Score: Score(words, currentCategoryId, c)))
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.PublishedAt)
            .Take(take)
            .Select(x => x.Id)
            .ToList();
    }
}
