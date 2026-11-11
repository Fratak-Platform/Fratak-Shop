using Fratak_Shop.Core.Services.Seo.Engine;

namespace Fratak_Shop.Core.Services.Searches;

/// <summary>
/// دور ۲۸ (#۷۶ – «جست‌وجوی بی‌نتیجه ← جایگزین»): وقتی عبارت کامل هیچ کالایی نداد، به‌جای صفحهٔ خالی چند راه دیگر پیشنهاد می‌شود.
/// <list type="bullet">
/// <item><see cref="RelaxedQueries"/>: عبارت‌های ساده‌تر به ترتیب نزدیکی (حذف واژهٔ آخر، بعد بلندترین واژه‌ها تک‌تک).
/// مثال: «هدفون بی سیم قرمز سامسونگ» ← «هدفون بی سیم قرمز»، «هدفون بی سیم»، «سامسونگ»، «هدفون»، «قرمز».</item>
/// <item><see cref="MatchCategories"/>: دسته‌هایی که نامشان با یکی از واژه‌های عبارت شروع می‌شود (مثلاً «کتری» ← «کتری و چای‌ساز»).</item>
/// </list>
/// خالص و بدون پایگاه داده (آزمون‌پذیر)؛ صفحهٔ جست‌وجو اولین عبارت ساده‌تری را که نتیجه دارد با پیام شفاف نشان می‌دهد.
/// </summary>
public static class SearchAlternatives
{
    // واژه‌های بی‌اثر در جست‌وجو (حرف اضافه و واژه‌های عمومی خرید).
    private static readonly HashSet<string> Stop = new HashSet<string>(StringComparer.Ordinal)
    {
        "و", "با", "برای", "از", "به", "در", "که", "را", "یک", "این", "آن", "مدل", "خرید", "قیمت", "ارزان", "اصل", "جدید", "عدد", "رنگ",
    };

    /// <summary>حداکثر عبارت ساده‌تر (هر کدام یک پرس‌وجو است؛ برای هاست ضعیف کم نگه داشته می‌شود).</summary>
    public const int MaxRelaxed = 4;

    /// <summary>واژه‌های معنادار عبارت (نرمال‌شده، بدون واژه‌های بی‌اثر، دست‌کم ۲ حرف).</summary>
    public static IReadOnlyList<string> Words(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return Array.Empty<string>();
        return PersianText.Normalize(query).Split(new[] { ' ', '\u200c', '-', '،', ',', '/' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(w => w.Length >= 2 && !Stop.Contains(w)).Distinct(StringComparer.Ordinal).Take(8).ToList();
    }

    /// <summary>
    /// عبارت‌های ساده‌تر به ترتیب نزدیکی به عبارت اصلی؛ عبارت تک‌واژه‌ای ← فهرست خالی. هیچ‌کدام برابر خود عبارت نیست.
    /// </summary>
    public static IReadOnlyList<string> RelaxedQueries(string? query)
    {
        var words = Words(query);
        var result = new List<string>();
        if (words.Count < 2)
            return result;
        // ۱) حذف واژه‌ها از آخر (معمولاً جزئیات: رنگ، مدل، ظرفیت).
        for (var n = words.Count - 1; n >= 2 && result.Count < 2; n--)
            result.Add(string.Join(' ', words.Take(n)));
        // ۲) تک‌واژه‌ها از بلند به کوتاه (واژهٔ بلندتر معمولاً نام کالا یا برند است).
        foreach (var word in words.Where(w => w.Length >= 3 && !w.All(char.IsDigit)).OrderByDescending(w => w.Length))
        {
            if (result.Count >= MaxRelaxed)
                break;
            if (!result.Contains(word, StringComparer.Ordinal))
                result.Add(word);
        }
        var normalized = string.Join(' ', words);
        return result.Where(r => !string.Equals(r, normalized, StringComparison.Ordinal)).Take(MaxRelaxed).ToList();
    }

    /// <summary>
    /// دسته‌هایی که یکی از واژه‌های نامشان با یکی از واژه‌های عبارت (≥ ۳ حرف) شروع می‌شود. حداکثر <paramref name="take"/> مورد.
    /// مثال: («کتری استیل»، [کتری و چای‌ساز، قهوه‌ساز]) ← [کتری و چای‌ساز].
    /// </summary>
    public static IReadOnlyList<(string Name, string Slug)> MatchCategories(string? query, IEnumerable<(Guid Id, string Name, string Slug)> categories, int take = 4)
    {
        var words = Words(query).Where(w => w.Length >= 3).ToList();
        if (words.Count == 0)
            return [];
        return categories
            .Select(c => (c.Name, c.Slug, Words: Words(c.Name)))
            .Where(c => c.Words.Any(cw => words.Any(w => cw.StartsWith(w, StringComparison.Ordinal) || w.StartsWith(cw, StringComparison.Ordinal) && cw.Length >= 3)))
            .DistinctBy(c => c.Slug)
            .Take(take)
            .Select(c => (c.Name, c.Slug))
            .ToList();
    }
}
