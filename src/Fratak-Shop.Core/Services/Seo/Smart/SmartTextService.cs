using System.Security.Cryptography;
using System.Text;
using Fratak_Shop.Core.Services.Seo.Engine;
using Fratak_Shop.Core.Services.Seo.Knowledge;
using Fratak_Shop.Core.Services.Settings;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.SeoEngine;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Fratak_Shop.Core.Services.Seo.Smart;

/// <summary>
/// ورودی دکمهٔ «پیشنهاد هوشمند» کنار هر فیلد متنی پنل. فقط Text و Purpose لازم است؛ بقیه (نام صفحه، دسته، برند، کلمهٔ
/// کلیدی) از همان فرم خوانده می‌شود تا پیشنهاد دقیق‌تر شود.
/// <para>مثال (عنوان محصول): Purpose=ProductName، Text=«گوشی سامسونگ a55 فوق العاده!!!»، CategoryId=…، BrandId=….</para>
/// </summary>
public sealed class SmartTextInput
{
    public SmartPurpose Purpose { get; init; }
    public SmartGoal Goal { get; init; }
    public string Text { get; init; } = "";
    public bool IsHtml { get; init; }

    /// <summary>نوع صفحهٔ فرم (برای سرتیترها و جمله‌های مناسب). null = عمومی.</summary>
    public SeoEntityKind? Kind { get; init; }

    /// <summary>شناسهٔ محصول ذخیره‌شده (برای خواندن مشخصات/گارانتی/موجودی). null = محصول تازه یا صفحهٔ دیگر.</summary>
    public Guid? EntityId { get; init; }

    public string? Subject { get; init; }
    public string? EnglishName { get; init; }
    public string? Keyword { get; init; }
    public Guid? CategoryId { get; init; }
    public Guid? BrandId { get; init; }
}

/// <summary>
/// بازخورد کاربر: Variant = شناسهٔ نسخهٔ انتخاب‌شده (clean، seo، …) یا «*» برای «هیچ‌کدام» (ردِ همه).
/// فقط ردِ صریح همه شمرده می‌شود؛ بستن پنجره بازخوردی نیست.
/// </summary>
public sealed class SmartTextFeedbackInput
{
    public SmartPurpose Purpose { get; init; }
    public string Variant { get; init; } = "";
    public bool Accepted { get; init; }
    public IReadOnlyList<string> RuleIds { get; init; } = [];
    public string? Input { get; init; }
    public int OutputLength { get; init; }
}

/// <summary>سرویس «پیشنهاد هوشمند»: بازنویسی متن هر فیلد با موتور داخلی (بدون هوش مصنوعی پولی) + یادگیری از انتخاب کاربر.</summary>
public interface ISmartTextService
{
    /// <summary>۲ تا ۳ نسخهٔ بهتر از متن فعلی با توضیح تغییرات. چیزی ذخیره نمی‌شود.</summary>
    Task<SmartTextResult> SuggestAsync(SmartTextInput input, CancellationToken cancellationToken = default);

    /// <summary>ثبت انتخاب/رد کاربر (فقط هش و طول متن؛ خود متن ذخیره نمی‌شود).</summary>
    Task RecordFeedbackAsync(SmartTextFeedbackInput input, Guid? userId, CancellationToken cancellationToken = default);
}

/// <summary>
/// پیاده‌سازی «پیشنهاد هوشمند». مراحل:
/// ۱) واقعیت‌های فروشگاه (تنظیمات) و محصول (مشخصات، گارانتی، موجودی) جمع می‌شود تا جملهٔ ترغیبی فقط ادعای درست بسازد؛
/// ۲) TextRefiner با جدول دانش (غلط‌های املایی، سبک، مترادف، سرتیتر، عبارت‌های جست‌وجوی واقعی) نسخه‌ها را می‌سازد؛
/// ۳) نسخه‌ای که کارمندان برای همین نوع فیلد بیشتر پذیرفته‌اند، بالاتر نمایش داده می‌شود (آمار SmartTextFeedback).
/// استفاده: صفحهٔ /admin/seo/smart (هندلرهای Text و Feedback) که اسکریپت smart-text.js صدا می‌زند.
/// </summary>
public sealed class SmartTextService(
    FratakShopDbContext context,
    IStoreSettingService settings,
    ISeoKnowledgeService knowledge,
    IMemoryCache cache,
    ILogger<SmartTextService> logger) : ISmartTextService
{
    private const string VocabularyKey = "smart-text:vocabulary";
    private const string StatsKey = "smart-text:stats";

    public async Task<SmartTextResult> SuggestAsync(SmartTextInput input, CancellationToken cancellationToken = default)
    {
        var current = await settings.GetAsync(cancellationToken);
        var kb = await knowledge.GetAsync(cancellationToken);

        string? category = null, brand = null;
        if (input.CategoryId is { } categoryId)
            category = await context.Categories.AsNoTracking().Where(c => c.Id == categoryId).Select(c => c.Name).FirstOrDefaultAsync(cancellationToken);
        if (input.BrandId is { } brandId)
            brand = await context.Brands.AsNoTracking().Where(b => b.Id == brandId).Select(b => b.Name).FirstOrDefaultAsync(cancellationToken);
        var product = input.Kind == SeoEntityKind.Product ? await ProductFactsAsync(input.EntityId, category, brand, cancellationToken) : null;

        var keyword = PersianText.Clean(input.Keyword);
        var subject = PersianText.Clean(input.Subject);
        var topic = keyword.Length > 0 ? keyword : subject.Length > 0 ? KeywordPlanner.ShortName(subject) : "";
        var request = new SmartTextRequest
        {
            Purpose = input.Purpose,
            Goal = input.Goal,
            Text = input.Text ?? "",
            IsHtml = input.IsHtml,
            Keyword = keyword.Length > 0 ? keyword : null,
            Subject = subject.Length > 0 ? subject : null,
            EnglishName = input.EnglishName,
            Category = category,
            Brand = brand,
            PageType = SeoEngineService.PageTypeOf(input.Kind ?? SeoEntityKind.None),
            Store = SeoEngineService.StoreFactsFrom(current),
            Product = product,
            Phrases = topic.Length > 0 ? kb.PhrasesFor(topic, 30).Select(r => r.Term).ToList() : [],
            Vocabulary = await VocabularyAsync(cancellationToken),
        };

        var result = TextRefiner.Refine(request, kb);
        await OrderByPreferenceAsync(result, input.Purpose, input.Goal, cancellationToken);
        return result;
    }

    public async Task RecordFeedbackAsync(SmartTextFeedbackInput input, Guid? userId, CancellationToken cancellationToken = default)
    {
        var variant = Cut(input.Variant, 40);
        if (variant.Length == 0)
            return;
        var rules = input.RuleIds.Where(r => r.Contains(':') && r.Length <= 210).Distinct(StringComparer.Ordinal).Take(60).ToList();
        var text = input.Input ?? "";
        context.SmartTextFeedbacks.Add(new SmartTextFeedback
        {
            Purpose = PurposeKey(input.Purpose),
            Variant = variant,
            Accepted = input.Accepted,
            Rules = rules.Count == 0 ? null : Cut(string.Join(",", rules), 2000),
            InputHash = text.Length == 0 ? null : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..32],
            InputLength = Math.Min(text.Length, 1_000_000),
            OutputLength = Math.Clamp(input.OutputLength, 0, 1_000_000),
            UserId = userId,
        });
        await context.SaveChangesAsync(cancellationToken);
        cache.Remove(StatsKey);

        // قاعده‌های جدول دانش (اصلاح املا، حذف عبارت کلیشه‌ای، مترادف …): پذیرش = اعتماد بیشتر، ردِ همه = اعتماد کمتر.
        if (rules.Count > 0)
            await knowledge.RecordOutcomeAsync(new SeoKnowledgeFeedback { Accepted = input.Accepted, RuleIds = rules }, cancellationToken);
    }

    /// <summary>
    /// مرتب‌سازی نسخه‌ها با آمار پذیرش همین نوع فیلد (لاپلاس: (پذیرش+۱)/(نمایش+۲)) به‌علاوهٔ ترتیب پیش‌فرض هدف.
    /// تا کمتر از ۵ بازخورد، ترتیب پیش‌فرض دست نمی‌خورد.
    /// </summary>
    private async Task OrderByPreferenceAsync(SmartTextResult result, SmartPurpose purpose, SmartGoal goal, CancellationToken cancellationToken)
    {
        if (result.Variants.Count < 2)
            return;
        var stats = await StatsAsync(cancellationToken);
        var key = PurposeKey(purpose);
        var rows = stats.Where(s => s.Purpose == key).ToList();
        if (rows.Sum(r => r.Total) < 5)
            return;
        var rejectAll = rows.Where(r => r.Variant == "*").Sum(r => r.Total);
        var order = TextRefiner.Order(purpose, goal);
        double Score(SmartVariant v)
        {
            var row = rows.FirstOrDefault(r => r.Variant == v.Id);
            var accepted = row?.Accepted ?? 0;
            var rate = (accepted + 1.0) / (accepted + rejectAll * 0.3 + 2.0);
            var index = order.ToList().IndexOf(v.Id);
            return rate - (index < 0 ? 0.5 : index * 0.08);
        }
        var sorted = result.Variants.OrderByDescending(Score).ToList();
        result.Variants.Clear();
        result.Variants.AddRange(sorted);
    }

    private sealed record VariantStat(string Purpose, string Variant, int Total, int Accepted);

    private async Task<IReadOnlyList<VariantStat>> StatsAsync(CancellationToken cancellationToken)
    {
        if (cache.TryGetValue(StatsKey, out IReadOnlyList<VariantStat>? cached) && cached is not null)
            return cached;
        var since = DateTimeOffset.UtcNow.AddDays(-180);
        var rows = await context.SmartTextFeedbacks.AsNoTracking()
            .Where(f => f.CreatedAt >= since)
            .GroupBy(f => new { f.Purpose, f.Variant })
            .Select(g => new { g.Key.Purpose, g.Key.Variant, Total = g.Count(), Accepted = g.Count(f => f.Accepted) })
            .ToListAsync(cancellationToken);
        IReadOnlyList<VariantStat> stats = rows.Select(r => new VariantStat(r.Purpose, r.Variant, r.Total, r.Accepted)).ToList();
        cache.Set(StatsKey, stats, TimeSpan.FromMinutes(10));
        return stats;
    }

    /// <summary>
    /// واژه‌های خاص فروشگاه (نام برندها، دسته‌ها و محصولات) تا «غلط املایی» حساب نشوند. مثال: «شیائومی»، «ایرپاد».
    /// </summary>
    private async Task<IReadOnlySet<string>> VocabularyAsync(CancellationToken cancellationToken)
    {
        if (cache.TryGetValue(VocabularyKey, out IReadOnlySet<string>? cached) && cached is not null)
            return cached;
        var names = new List<string?>();
        names.AddRange(await context.Brands.AsNoTracking().Select(b => b.Name).ToListAsync(cancellationToken));
        names.AddRange(await context.Categories.AsNoTracking().Select(c => c.Name).ToListAsync(cancellationToken));
        names.AddRange(await context.Products.AsNoTracking().OrderByDescending(p => p.CreatedAt).Select(p => p.Name).Take(5000).ToListAsync(cancellationToken));
        var words = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in names.Where(n => !string.IsNullOrWhiteSpace(n)))
            foreach (var word in PersianText.Normalize(name).Split(' ', StringSplitOptions.RemoveEmptyEntries))
                if (word.Length >= 2)
                    words.Add(word);
        cache.Set(VocabularyKey, (IReadOnlySet<string>)words, TimeSpan.FromMinutes(30));
        return words;
    }

    /// <summary>واقعیت‌های محصول ذخیره‌شده (مشخصات، گارانتی، موجودی). محصول تازه → فقط دسته و برند فرم.</summary>
    private async Task<ProductFacts> ProductFactsAsync(Guid? id, string? category, string? brand, CancellationToken cancellationToken)
    {
        if (id is { } productId && productId != Guid.Empty)
        {
            try
            {
                var row = await context.Products.AsNoTracking()
                    .Where(p => p.Id == productId)
                    .Select(p => new
                    {
                        p.WarrantyTitle, p.WarrantyMonths, p.StockQuantity,
                        Category = p.Category!.IsSystem ? null : p.Category.Name, Brand = p.Brand != null ? p.Brand.Name : null,
                        Features = p.Features.OrderBy(f => f.DisplayOrder).Select(f => new { f.Key, f.Value }).Take(30).ToList(),
                    })
                    .FirstOrDefaultAsync(cancellationToken);
                if (row is not null)
                    return new ProductFacts
                    {
                        CategoryName = category ?? row.Category,
                        BrandName = brand ?? row.Brand,
                        Features = row.Features.Select(f => new SeoFeature(f.Key, f.Value)).ToList(),
                        WarrantyTitle = row.WarrantyTitle,
                        WarrantyMonths = row.WarrantyMonths,
                        InStock = row.StockQuantity > 0,
                    };
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogDebug(ex, "Smart text product facts unavailable for {Id}", productId);
            }
        }
        return new ProductFacts { CategoryName = category, BrandName = brand, InStock = true };
    }

    /// <summary>کد نوع فیلد در جدول بازخورد. مثال: SeoTitle → «seo-title».</summary>
    public static string PurposeKey(SmartPurpose purpose) => purpose switch
    {
        SmartPurpose.ProductName => "product-name",
        SmartPurpose.SeoTitle => "seo-title",
        SmartPurpose.MetaDescription => "meta",
        SmartPurpose.ShortText => "short",
        SmartPurpose.Body => "body",
        _ => "plain",
    };

    private static string Cut(string? value, int max) => (value ?? "").Trim() is var v && v.Length > max ? v[..max] : v;
}
