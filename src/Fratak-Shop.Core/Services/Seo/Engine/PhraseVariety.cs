using System.Globalization;
using System.Text;
using Fratak_Shop.Data.Entities.SeoEngine;

namespace Fratak_Shop.Core.Services.Seo.Engine;

// ═══════════════════════════════════════════════════════════════════════════
//  تنوع متن (مرحلهٔ یازدهم): جلوگیری از «متن قالبی» و تکراری بین صفحه‌های مشابه
//  مشکل: وقتی ۵۰ هدفون از یک دسته داریم، اگر همه با یک الگو نوشته شوند («X از محصولات برند Y است …») گوگل
//  آن‌ها را «محتوای قالبی کم‌ارزش» می‌بیند، حتی اگر نام محصول‌ها فرق کند.
//  راه‌حل (همه داخلی و بدون هوش مصنوعی خارجی):
//   • Spin           : هر جمله با چند جایگزین نوشته می‌شود («[خرید|سفارش آنلاین] …») و همهٔ ترکیب‌ها نامزد می‌شوند.
//   • PhraseUsage    : «حافظهٔ سایت»؛ اسکلت جمله‌های همهٔ صفحه‌ها (بعد از حذف نام محصول، برند، عددها …) شمرده
//                      می‌شود. نویسنده الگویی را انتخاب می‌کند که در صفحه‌های هم‌نوع کمتر استفاده شده است؛ پس
//                      صفحه‌های مشابه خودبه‌خود جمله‌بندی متفاوت می‌گیرند.
//   • CategoryProfile: آمار مشخصات محصولات هر دسته (کدام ویژگی کمیاب است، میانگین عددها) تا متن هر محصول
//                      روی «تفاوت‌های واقعی» آن با هم‌دسته‌هایش تمرکز کند.
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// گسترش الگوی جایگزین‌ها. هر «[الف|ب|ج]» یکی از گزینه‌هاست؛ «[الف|]» یعنی اختیاری.
/// مثال: «[خرید|سفارش] {L} [آنلاین|]» → «خرید {L} آنلاین»، «خرید {L}»، «سفارش {L} آنلاین»، «سفارش {L}».
/// جای‌نگهدارهای «{…}» دست نمی‌خورند (بعداً با نام و مقدار پر می‌شوند). براکت تودرتو پشتیبانی نمی‌شود.
/// </summary>
public static class Spin
{
    /// <summary>همهٔ ترکیب‌ها (حداکثر max تا، به ترتیب ثابت). فاصله‌های تکراری حاصل از گزینهٔ خالی حذف می‌شوند.</summary>
    public static List<string> Expand(string pattern, int max = 64)
    {
        var results = new List<StringBuilder> { new() };
        var i = 0;
        while (i < pattern.Length)
        {
            var ch = pattern[i];
            if (ch == '[')
            {
                var close = pattern.IndexOf(']', i + 1);
                if (close < 0)
                {
                    foreach (var r in results) r.Append(pattern, i, pattern.Length - i);
                    break;
                }
                var options = pattern[(i + 1)..close].Split('|');
                var next = new List<StringBuilder>(results.Count * options.Length);
                foreach (var option in options)
                {
                    foreach (var r in results)
                    {
                        if (next.Count >= max) break;
                        next.Add(new StringBuilder(r.ToString()).Append(option));
                    }
                }
                results = next;
                i = close + 1;
                continue;
            }
            foreach (var r in results) r.Append(ch);
            i++;
        }

        return results.Select(r => Tidy(r.ToString())).Where(s => s.Length > 0).Distinct(StringComparer.Ordinal).ToList();
    }

    /// <summary>فاصلهٔ تکراری و فاصلهٔ قبل از علامت‌ها (حاصل گزینهٔ خالی) را مرتب می‌کند.</summary>
    public static string Tidy(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            if (ch == ' ' && (builder.Length == 0 || builder[^1] == ' '))
                continue;
            if (ch is '.' or '،' or '؛' or '؟' or ':' or ',' or '!' && builder.Length > 0 && builder[^1] == ' ')
                builder.Length--;
            builder.Append(ch);
        }
        return builder.ToString().Trim();
    }
}

/// <summary>
/// حافظهٔ «الگوهای استفاده‌شده» در کل سایت. برای هر صفحه، متن‌هایش (عنوان، توضیحات، جمله‌های متن، پرسش‌وپاسخ)
/// به «اسکلت» تبدیل می‌شوند: نام محصول/برند/دسته و مقدارهای خاص با ◊ و عددها با # جایگزین می‌شوند.
/// مثال: «هدفون هایلو T15 دارای شارژدهی 40 ساعت است.» و «هدفون انکر P3 دارای شارژدهی 35 ساعت است.» هر دو
/// اسکلت «◊ دارای شارژدهی # ساعت است» دارند ⇒ یک الگو حساب می‌شوند.
/// کاربرد: <see cref="ContentWriter"/> برای هر جمله، نامزدی را ترجیح می‌دهد که الگویش در صفحه‌های هم‌نوع کمتر دیده شده.
/// داخل <see cref="SeoSiteIndex"/> نگهداری و با هر پیشنهاد تازه به‌روز می‌شود (پس صفحهٔ بعدی همان اجرا هم آن را می‌بیند).
/// </summary>
public sealed class PhraseUsage
{
    // کلید = نوع صفحه + «|» + سه‌واژهٔ اسکلت (یا کل اسکلت متن کوتاه). مقدار = تعداد صفحه‌هایی که دارندش.
    private readonly Dictionary<string, int> _pages = new(StringComparer.Ordinal);
    private readonly Dictionary<Guid, (SeoEntityKind Kind, HashSet<string> Keys)> _byEntity = [];
    private readonly Dictionary<SeoEntityKind, int> _kindCount = [];

    /// <summary>بیشترین واژه‌ای که از متن هر صفحه شمرده می‌شود (برای سرعت در سایت‌های بزرگ).</summary>
    public const int MaxTokensPerPage = 600;

    /// <summary>تعداد صفحه‌های ثبت‌شده از یک نوع.</summary>
    public int Pages(SeoEntityKind kind) => _kindCount.GetValueOrDefault(kind);

    /// <summary>ثبت متن‌های یک صفحه (اگر قبلاً ثبت شده بود، اول حذف می‌شود).</summary>
    public void Register(Guid id, SeoEntityKind kind, IEnumerable<string?> texts, IReadOnlyCollection<string> terms)
    {
        Unregister(id);
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var budget = MaxTokensPerPage;
        foreach (var text in texts)
        {
            if (string.IsNullOrWhiteSpace(text) || budget <= 0)
                continue;
            foreach (var sentence in PersianText.Sentences(text))
            {
                var tokens = SkeletonTokens(sentence, terms);
                budget -= tokens.Count;
                foreach (var key in Keys(tokens))
                    keys.Add(Prefix(kind) + key);
                if (budget <= 0)
                    break;
            }
        }
        foreach (var key in keys)
            _pages[key] = _pages.GetValueOrDefault(key) + 1;
        _byEntity[id] = (kind, keys);
        _kindCount[kind] = _kindCount.GetValueOrDefault(kind) + 1;
    }

    /// <summary>حذف صفحه از آمار (پیش از ثبت دوبارهٔ نسخهٔ تازه).</summary>
    public void Unregister(Guid id)
    {
        if (!_byEntity.Remove(id, out var entry))
            return;
        foreach (var key in entry.Keys)
        {
            var count = _pages.GetValueOrDefault(key) - 1;
            if (count <= 0) _pages.Remove(key);
            else _pages[key] = count;
        }
        _kindCount[entry.Kind] = Math.Max(0, _kindCount.GetValueOrDefault(entry.Kind) - 1);
    }

    /// <summary>
    /// «قالبی بودن» یک متن نسبت به صفحه‌های هم‌نوع دیگر (۰ = الگوی تازه، ۱ = همهٔ صفحه‌ها همین الگو را دارند).
    /// میانگین سهم صفحه‌هایی که هر سه‌واژهٔ اسکلت را دارند؛ خود صفحه (self) حساب نمی‌شود.
    /// مثال: اگر ۸ صفحه از ۱۰ هدفون جملهٔ «◊ دارای شارژدهی # ساعت است» را داشته باشند، نتیجه حدود ۰٫۸ است.
    /// </summary>
    public double Commonness(SeoEntityKind kind, string text, IReadOnlyCollection<string> terms, Guid self)
    {
        var others = Pages(kind) - (_byEntity.TryGetValue(self, out var mine) && mine.Kind == kind ? 1 : 0);
        if (others <= 0)
            return 0;
        var keys = Keys(SkeletonTokens(text, terms)).Distinct(StringComparer.Ordinal).ToList();
        if (keys.Count == 0)
            return 0;
        var prefix = Prefix(kind);
        var sum = 0.0;
        foreach (var key in keys)
        {
            var full = prefix + key;
            var count = _pages.GetValueOrDefault(full);
            if (mine.Keys is not null && mine.Kind == kind && mine.Keys.Contains(full))
                count--;
            sum += Math.Min(1.0, (double)Math.Max(0, count) / others);
        }
        return sum / keys.Count;
    }

    /// <summary>
    /// اسکلت متن (برای نمایش و تست): واژه‌ها با فاصله. مثال: «خرید هدفون هایلو T15 با 40 ساعت شارژ» با
    /// terms=[هدفون هایلو T15] → «خرید ◊ با # ساعت شارژ».
    /// </summary>
    public static string Skeleton(string text, IReadOnlyCollection<string> terms) => string.Join(' ', SkeletonTokens(text, terms));

    private static List<string> SkeletonTokens(string text, IReadOnlyCollection<string> terms)
    {
        var normalized = " " + PersianText.Normalize(text) + " ";
        // عبارت‌های خاص صفحه (طولانی‌ترها اول تا «هدفون هایلو T15» پیش از «هایلو» جایگزین شود).
        foreach (var raw in terms)
        {
            // عبارت‌ها هم نرمال می‌شوند تا «Z0» با «z0» متن نرمال‌شده بخواند (TermsOf خودش نرمال‌شده می‌دهد).
            var term = PersianText.Normalize(raw);
            if (term.Length < 2)
                continue;
            normalized = normalized.Replace(" " + term + " ", " ◊ ", StringComparison.Ordinal);
            // پس از علامت‌ها هم (مثل «T15،») جایگزین شود.
            normalized = normalized.Replace(" " + term + "،", " ◊،", StringComparison.Ordinal)
                .Replace(" " + term + ".", " ◊.", StringComparison.Ordinal)
                .Replace(" " + term + "؟", " ◊؟", StringComparison.Ordinal)
                .Replace(" " + term + "؛", " ◊؛", StringComparison.Ordinal);
        }

        var tokens = new List<string>();
        var current = new StringBuilder();
        void Flush()
        {
            if (current.Length == 0) return;
            var token = current.ToString();
            current.Clear();
            token = token.Any(char.IsDigit) && token.All(c => char.IsDigit(c) || c == '.') ? "#" : token;
            if (tokens.Count > 0 && tokens[^1] == token && token is "◊" or "#")
                return; // «◊ ◊» یکی می‌شود
            tokens.Add(token);
        }
        foreach (var ch in normalized)
        {
            if (ch == '◊')
            {
                Flush();
                current.Append('◊');
                Flush();
            }
            else if (char.IsLetterOrDigit(ch) || ch == '.' && current.Length > 0 && char.IsDigit(current[^1]))
                current.Append(ch);
            else
                Flush();
        }
        Flush();
        return tokens;
    }

    // سه‌واژه‌ها؛ متن کوتاه‌تر از ۳ واژه یک کلید کامل دارد. عنوان‌ها و جمله‌های کوتاه با کلید «=» هم ثبت می‌شوند
    // تا «یکسان بودن کل الگو» وزن بیشتری بگیرد.
    private static IEnumerable<string> Keys(List<string> tokens)
    {
        if (tokens.Count == 0)
            yield break;
        if (tokens.Count < 3)
        {
            yield return string.Join(' ', tokens);
            yield break;
        }
        for (var i = 0; i + 2 < tokens.Count; i++)
        {
            // سه‌واژه‌ای که فقط از ◊ و # و واژه‌های ایست ساخته شده، اطلاعاتی دربارهٔ الگو ندارد.
            var a = tokens[i]; var b = tokens[i + 1]; var c = tokens[i + 2];
            if (IsFiller(a) && IsFiller(b) && IsFiller(c))
                continue;
            yield return a + " " + b + " " + c;
        }
        if (tokens.Count <= 14)
            yield return "=" + string.Join(' ', tokens);
    }

    private static bool IsFiller(string token) => token is "◊" or "#" || PersianText.IsStopWord(token);

    private static string Prefix(SeoEntityKind kind) => ((int)kind).ToString(CultureInfo.InvariantCulture) + "|";

    /// <summary>
    /// عبارت‌های خاص یک صفحه که در اسکلت حذف می‌شوند (نام، نام کوتاه، کلمهٔ کلیدی، برند، دسته، واژه‌های مدل،
    /// مقدارهای متنی ویژگی‌ها، نام زیردسته‌ها …). خروجی نرمال‌شده و به ترتیب طول (بلندتر اول) است.
    /// </summary>
    public static IReadOnlyList<string> TermsOf(SeoSubject subject, string? keyword = null)
    {
        var raw = new List<string?>
        {
            subject.Name, subject.EnglishName, subject.FocusKeyword, keyword,
        };
        if (subject.Kind == SeoEntityKind.Product)
        {
            raw.Add(KeywordPlanner.ShortName(subject.Name));
            raw.AddRange(KeywordPlanner.ModelTokens(subject.Name));
        }
        if (subject.Product is { } p)
        {
            raw.Add(p.BrandName);
            raw.Add(p.BrandEnglishName);
            raw.Add(p.CategoryName);
            raw.AddRange(p.Features.Select(f => f.Value).Where(v => !v.Any(char.IsDigit) && v.Length <= 30));
        }
        if (subject.Listing is { } l)
        {
            raw.Add(l.ParentName);
            raw.AddRange(l.RelatedGroups.Select(g => g.Text));
            raw.AddRange(l.SubCategories.Select(g => g.Text));
            raw.AddRange(l.TopProducts.Select(g => g.Text));
        }
        return raw.Select(PersianText.Normalize)
            .Where(t => t.Length >= 2 && !PersianText.IsStopWord(t) && t is not ("دارد" or "بله" or "ندارد" or "yes" or "no"))
            .Distinct(StringComparer.Ordinal)
            .OrderByDescending(t => t.Length)
            .ToList();
    }
}

/// <summary>
/// آمار مشخصات محصولات یک دسته (از داده‌های خود فروشگاه). برای اینکه متن هر محصول روی تفاوت‌های واقعی‌اش با
/// هم‌دسته‌ها تمرکز کند: ویژگی کمیاب (مثلاً «حذف نویز» وقتی فقط ۲ مدل از ۱۲ مدل دارند) و مقایسه با میانگین
/// (مثلاً «شارژدهی ۴۰ ساعت، بیشتر از میانگین حدود ۳۰ ساعتِ این دسته»).
/// </summary>
public sealed class CategoryProfile
{
    // کلید ویژگی نرمال‌شده → (شناسهٔ محصول → مقدار نرمال‌شده، عدد)
    private readonly Dictionary<string, Dictionary<Guid, (string Value, double? Number)>> _features = new(StringComparer.Ordinal);
    private readonly HashSet<Guid> _products = [];

    /// <summary>تعداد محصولات ثبت‌شدهٔ دسته.</summary>
    public int Count => _products.Count;

    public void Add(Guid id, IEnumerable<SeoFeature> features)
    {
        Remove(id);
        _products.Add(id);
        foreach (var feature in features)
        {
            var key = PersianText.Normalize(feature.Key);
            var value = PersianText.Normalize(feature.Value);
            if (key.Length == 0 || value.Length == 0)
                continue;
            if (!_features.TryGetValue(key, out var map))
                _features[key] = map = [];
            map[id] = (value, SpecKnowledge.Number(feature.Value));
        }
    }

    public void Remove(Guid id)
    {
        if (!_products.Remove(id))
            return;
        foreach (var map in _features.Values)
            map.Remove(id);
    }

    /// <summary>
    /// سهم محصولات دیگرِ دسته که همین مقدار را برای همین ویژگی دارند (۰ = فقط همین محصول، ۱ = همه).
    /// محصولاتی که این ویژگی را اصلاً ثبت نکرده‌اند هم «ندارند» حساب می‌شوند (ویژگی ثبت‌نشده = کمیاب‌تر).
    /// </summary>
    public double Share(Guid self, string key, string value)
    {
        var others = _products.Count - (_products.Contains(self) ? 1 : 0);
        if (others <= 0 || !_features.TryGetValue(PersianText.Normalize(key), out var map))
            return 0;
        var normalized = PersianText.Normalize(value);
        return (double)map.Count(p => p.Key != self && p.Value.Value == normalized) / others;
    }

    /// <summary>
    /// میانگین عددی ویژگی در بقیهٔ محصولات دسته. null = کمتر از minOthers محصول دیگر عدد این ویژگی را دارند.
    /// </summary>
    public (double Average, int Count)? Average(Guid self, string key, int minOthers = 3)
    {
        if (!_features.TryGetValue(PersianText.Normalize(key), out var map))
            return null;
        var numbers = map.Where(p => p.Key != self && p.Value.Number is > 0).Select(p => p.Value.Number!.Value).ToList();
        return numbers.Count < minOthers ? null : (numbers.Average(), numbers.Count);
    }
}
