using Fratak_Shop.Core.Services.Seo.Engine;
using Fratak_Shop.Data.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Fratak_Shop.Core.Services.Assistant;

// ═══════════════════════════════════════════════════════════════════════════
//  دور ۲۴ – «دانش» دستیار: همهٔ اطلاعات عمومی سایت در یک فهرست کوچک و کش‌شده.
//  منابع: دسته‌ها، برندها، مقاله‌های منتشرشده، پرسش‌های متداول و صفحه‌های محتوایی (درباره ما، قوانین، شیوهٔ ارسال، …).
//  ایمنی: فقط دادهٔ عمومی که هر بازدیدکننده هم می‌بیند خوانده می‌شود؛ دستیار هیچ‌وقت به جدول‌ها دسترسی آزاد ندارد
//  (دادهٔ شخصی فقط سبد، سفارش و آدرس‌های خود کاربر است که جداگانه و با شناسهٔ همان کاربر خوانده می‌شود).
//  سبکی: هر ۱۰ دقیقه یک بار ساخته می‌شود (حداکثر ۳۰۰۰ مورد؛ متن هر مورد حداکثر ۱۲۰۰ نویسه) و جست‌وجو در حافظه است.
//  محل استفاده: AssistantService (پرسش‌های عمومی پیش از «کالای درخواستی») و تست‌های Unit/AssistantKnowledgeTests.
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>نوع یک مورد دانش.</summary>
public enum KnowledgeKind
{
    Category = 1,
    Brand = 2,
    Post = 3,
    Faq = 4,
    Page = 5,
}

/// <summary>یک مورد دانش. Body متن ساده است (HTML نیست).</summary>
public sealed record KnowledgeEntry(KnowledgeKind Kind, string Title, string Body, string Url);

/// <summary>یک نتیجهٔ جست‌وجو در دانش: مورد، امتیاز (۰ تا ۱) و بهترین جملهٔ مرتبط.</summary>
public sealed record KnowledgeHit(KnowledgeEntry Entry, double Score, string Snippet);

/// <summary>دانش عمومی سایت برای دستیار.</summary>
public interface IAssistantKnowledge
{
    /// <summary>بهترین موردهای مرتبط با پرسش (حداکثر <paramref name="max"/>؛ امتیاز ≥ <paramref name="minScore"/>).</summary>
    Task<IReadOnlyList<KnowledgeHit>> SearchAsync(string query, int max = 3, double minScore = 0.5, CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public sealed class AssistantKnowledge(FratakShopDbContext context, IMemoryCache cache) : IAssistantKnowledge
{
    /// <summary>کلید کش (در CacheInvalidation نیست چون عمر کوتاه دارد؛ ۱۰ دقیقه).</summary>
    public const string CacheKey = "assistant:knowledge";

    /// <summary>بیشترین تعداد مورد.</summary>
    public const int MaxEntries = 3000;

    /// <inheritdoc />
    public async Task<IReadOnlyList<KnowledgeHit>> SearchAsync(string query, int max = 3, double minScore = 0.5, CancellationToken cancellationToken = default)
    {
        if (AssistantNlu.Words(query).Count == 0)
            return [];
        var entries = await cache.GetOrCreateAsync(CacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10);
            entry.Size = 1;
            return await BuildAsync(cancellationToken);
        }) ?? [];
        return KnowledgeIndex.Search(entries, query, max, minScore);
    }

    private async Task<List<KnowledgeEntry>> BuildAsync(CancellationToken cancellationToken)
    {
        var list = new List<KnowledgeEntry>();
        var categories = await context.Categories.AsNoTracking()
            .Where(c => c.IsActive && !c.IsSystem && c.Products.Any(p => p.IsActive))
            .OrderBy(c => c.DisplayOrder)
            .Select(c => new { c.Name, c.Slug, c.Description })
            .Take(800)
            .ToListAsync(cancellationToken);
        list.AddRange(categories.Select(c => new KnowledgeEntry(KnowledgeKind.Category, c.Name, KnowledgeIndex.Plain(c.Description), $"/category/{c.Slug}")));

        var brands = await context.Brands.AsNoTracking()
            .Where(b => b.IsActive && b.Products.Any(p => p.IsActive))
            .OrderBy(b => b.DisplayOrder)
            .Select(b => new { b.Name, b.EnglishName, b.Slug, b.Description })
            .Take(500)
            .ToListAsync(cancellationToken);
        list.AddRange(brands.Select(b => new KnowledgeEntry(KnowledgeKind.Brand, b.EnglishName is { Length: > 0 } en ? $"{b.Name} ({en})" : b.Name,
            KnowledgeIndex.Plain(b.Description), $"/brand/{b.Slug}")));

        var now = DateTimeOffset.UtcNow;
        var posts = await context.BlogPosts.AsNoTracking()
            .Where(p => p.IsPublished && (p.PublishedAt == null || p.PublishedAt <= now))
            .OrderByDescending(p => p.ViewCount)
            .Select(p => new { p.Title, p.Slug, p.Excerpt })
            .Take(800)
            .ToListAsync(cancellationToken);
        list.AddRange(posts.Select(p => new KnowledgeEntry(KnowledgeKind.Post, p.Title, KnowledgeIndex.Plain(p.Excerpt), $"/blog/{p.Slug}")));

        var faqs = await context.FaqItems.AsNoTracking()
            .Where(f => f.IsActive)
            .OrderBy(f => f.DisplayOrder)
            .Select(f => new { f.Question, f.Answer })
            .Take(400)
            .ToListAsync(cancellationToken);
        list.AddRange(faqs.Select(f => new KnowledgeEntry(KnowledgeKind.Faq, f.Question, KnowledgeIndex.Plain(f.Answer), "/faq")));

        var pages = await context.ContentPages.AsNoTracking()
            .Where(p => p.IsActive)
            .OrderBy(p => p.DisplayOrder)
            .Select(p => new { p.Title, p.Slug, p.Summary, p.Content })
            .Take(200)
            .ToListAsync(cancellationToken);
        list.AddRange(pages.Select(p => new KnowledgeEntry(KnowledgeKind.Page, p.Title, KnowledgeIndex.Plain((p.Summary ?? "") + " " + (p.Content ?? "")), $"/page/{p.Slug}")));

        return list.Take(MaxEntries).ToList();
    }
}

/// <summary>جست‌وجوی خالص در موردهای دانش (بدون پایگاه داده؛ تست‌پذیر).</summary>
public static class KnowledgeIndex
{
    /// <summary>بیشترین طول متن هر مورد.</summary>
    public const int MaxBody = 1200;

    /// <summary>HTML ← متن ساده و کوتاه‌شده. مثال: «&lt;p&gt;ارسال &lt;b&gt;رایگان&lt;/b&gt;&lt;/p&gt;» ← «ارسال رایگان».</summary>
    public static string Plain(string? html)
    {
        var text = PersianText.HtmlToText(html ?? "").Trim();
        return text.Length > MaxBody ? text[..MaxBody] : text;
    }

    /// <summary>
    /// امتیاز هر مورد: واژه‌های پرسش که در عنوان هستند (وزن ۲) یا در متن (وزن ۱)، نسبت به حداکثر ممکن. واژهٔ ۴ حرف به بالا با پیشوند هم منطبق است
    /// («ارسال» ≈ «ارسالی»). عنوانی که کل پرسش را دارد ۰٫۱ امتیاز اضافه می‌گیرد.
    /// <para>مثال: پرسش «شرایط مرجوعی کالا» و صفحهٔ «شرایط مرجوعی» ← ۱؛ مقاله‌ای که فقط در متنش «مرجوعی» دارد ← ۰٫۲۵.</para>
    /// </summary>
    public static IReadOnlyList<KnowledgeHit> Search(IReadOnlyList<KnowledgeEntry> entries, string query, int max = 3, double minScore = 0.5)
    {
        var words = AssistantNlu.Words(query).Where(w => w.Length >= 2).Take(8).ToList();
        if (words.Count == 0 || entries.Count == 0)
            return [];
        var normalizedQuery = PersianText.Normalize(query);
        var hits = new List<KnowledgeHit>();
        foreach (var entry in entries)
        {
            var title = AssistantNlu.Words(entry.Title);
            var body = AssistantNlu.Words(entry.Body);
            double gained = 0;
            foreach (var word in words)
            {
                if (Matches(title, word))
                    gained += 2;
                else if (Matches(body, word))
                    gained += 1;
            }
            if (gained == 0)
                continue;
            var score = gained / (words.Count * 2.0);
            if (normalizedQuery.Length >= 4 && PersianText.Normalize(entry.Title).Contains(normalizedQuery, StringComparison.Ordinal))
                score += 0.1;
            score = Math.Min(1, score);
            if (score >= minScore)
                hits.Add(new KnowledgeHit(entry, score, Snippet(entry.Body, words)));
        }
        return hits
            .OrderByDescending(h => h.Score)
            .ThenBy(h => h.Entry.Kind == KnowledgeKind.Faq ? 0 : h.Entry.Kind == KnowledgeKind.Page ? 1 : 2)
            .ThenBy(h => h.Entry.Title.Length)
            .Take(Math.Clamp(max, 1, 10))
            .ToList();
    }

    private static bool Matches(HashSet<string> set, string word) =>
        set.Contains(word) || word.Length >= 4 && set.Any(x => x.Length >= 4 && (x.StartsWith(word, StringComparison.Ordinal) || word.StartsWith(x, StringComparison.Ordinal)));

    /// <summary>
    /// بهترین جمله (یا دو جمله) از متن که بیشترین واژهٔ پرسش را دارد؛ حداکثر ۳۰۰ نویسه. متن خالی ← «».
    /// </summary>
    public static string Snippet(string body, IReadOnlyCollection<string> words)
    {
        if (string.IsNullOrWhiteSpace(body))
            return "";
        var sentences = body.Split(['.', '!', '؟', '?', '\n', '؛'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(s => s.Length >= 8)
            .ToList();
        if (sentences.Count == 0)
            return Cut(body, 300);
        var best = sentences
            .Select((s, i) => (Text: s, Index: i, Score: words.Count(w => PersianText.Normalize(s).Contains(w, StringComparison.Ordinal))))
            .OrderByDescending(s => s.Score).ThenBy(s => s.Index)
            .First();
        var text = best.Text;
        if (text.Length < 120 && best.Index + 1 < sentences.Count)
            text += ". " + sentences[best.Index + 1];
        return Cut(text.TrimEnd('.') + ".", 300);
    }

    private static string Cut(string text, int max) => text.Length <= max ? text : text[..max].TrimEnd() + "…";
}
