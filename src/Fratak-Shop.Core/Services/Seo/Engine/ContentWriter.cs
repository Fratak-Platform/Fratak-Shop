using System.Net;
using System.Text;
using AngleSharp.Html.Parser;
using Fratak_Shop.Data.Entities.SeoEngine;

namespace Fratak_Shop.Core.Services.Seo.Engine;

// ═══════════════════════════════════════════════════════════════════════════
//  نویسندهٔ فارسی موتور سئو (تولید زبان طبیعی داخلی، بدون هوش مصنوعی خارجی)
//  روش «بساز و رتبه بده» (generate & rank):
//   ۱) برای هر بخش (عنوان، توضیحات متا، متن، پرسش‌وپاسخ) ده‌ها جملهٔ نامزد از «بانک عبارت‌ها» ساخته می‌شود؛
//      هر عبارت شرط دارد و فقط وقتی استفاده می‌شود که واقعیتش در داده‌ها باشد (گارانتی، مدت مرجوعی، ویژگی‌های فنی …).
//   ۲) نامزدها با چند معیار رتبه‌بندی می‌شوند: قواعد سئو (طول، جای کلمهٔ کلیدی، یکتا بودن)، شباهت به عنوان‌ها و
//      واژه‌های صفحه‌های برتر نتایج (یادگیری از رقبا)، روان بودن با مدل زبانی سه‌کلمه‌ای که از متن‌های خود سایت
//      آموزش دیده، و سبک درخواستی کارشناس (کوتاه‌تر/بلندتر/کمتر تبلیغاتی).
//   ۳) برای هر «نسخه» (Variant) یک دانهٔ تصادفی ثابت استفاده می‌شود؛ وقتی کارشناس پیشنهاد را رد می‌کند، نسخهٔ بعدی
//      متن متفاوتی می‌سازد و متن‌های ردشده هرگز دوباره پیشنهاد نمی‌شوند.
//   ۴) تنوع (مرحلهٔ یازدهم): الگوهایی که در صفحه‌های هم‌نوع زیاد استفاده شده‌اند امتیاز منفی می‌گیرند (PhraseUsage)،
//      متن روی تفاوت‌های واقعی محصول (SpecKnowledge و CategoryProfile) تمرکز می‌کند و نگارش متن/پرسش‌وپاسخ در
//      ContentWriter.Compose.cs انجام می‌شود.
//  قانون‌های محتوا (برای جلوگیری از جریمه و ادعای نادرست):
//   • فقط واقعیت‌های موجود در داده نوشته می‌شود؛ هیچ ادعای «اصل»، «بهترین»، «ارزان‌ترین» یا موجودی/قیمت دقیق.
//   • متن فقط برای صفحهٔ کم‌محتوا (زیر ۱۵۰ واژه) نوشته می‌شود و متن فعلی همیشه اول و دست‌نخورده می‌ماند.
//   • متن انسانی ۲۵۰ واژه یا بیشتر هرگز بازنویسی نمی‌شود؛ متن مقاله‌ها هرگز تولید نمی‌شود.
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>سبکی که کارشناس هنگام رد کردن خواسته (از روی متن دلیل رد تشخیص داده می‌شود).</summary>
[Flags]
public enum WriterStyle
{
    None = 0,
    Shorter = 1,
    Longer = 2,
    LessPromotional = 4,
}

/// <summary>ورودی نویسنده برای یک صفحه.</summary>
public sealed class WriterContext
{
    public required SeoSubject Subject { get; init; }
    public required StoreFacts Store { get; init; }
    /// <summary>کلمهٔ کلیدی نهایی (فعلی یا پیشنهادی).</summary>
    public string Keyword { get; init; } = "";
    public CompetitorInsight? Insight { get; init; }
    public TrigramLanguageModel? Language { get; init; }
    public SeoSiteIndex? Site { get; init; }
    public int Variant { get; init; }
    public WriterStyle Style { get; init; }
    /// <summary>متن‌هایی که قبلاً رد شده‌اند (نرمال‌شده)؛ دوباره پیشنهاد نمی‌شوند.</summary>
    public IReadOnlySet<string> Avoid { get; init; } = new HashSet<string>();

    /// <summary>
    /// نکته‌های تأییدشدهٔ خریداران (خلاصهٔ خرید تأییدشده) برای «توضیحات محصول» – دور بیستم.
    /// فقط وقتی پر می‌شود که خلاصهٔ خرید محصول «تأییدشده» باشد؛ null = بدون نظر خریداران (فقط از مشخصات نوشته می‌شود).
    /// محل پر شدن: <c>SeoEngineService.WriteProductReviewAsync</c>.
    /// </summary>
    public ReviewNotes? Notes { get; init; }

    private Random? _random;

    /// <summary>تصادفی ثابت برای (صفحه، نسخه): یک نسخه همیشه همان متن را می‌سازد (قابل تکرار و تست).</summary>
    public Random Random => _random ??= new Random((int)(TextModels.Fnv1a(Subject.EntityId + ":" + Variant) & 0x7FFFFFFF));

    /// <summary>نام کوتاه (محصول) یا نام کامل.</summary>
    public string ShortName => Subject.Kind == SeoEntityKind.Product ? KeywordPlanner.ShortName(Subject.Name) : PersianText.Clean(Subject.Name);

    /// <summary>
    /// عبارتی که در جمله‌های اصلی می‌آید: اگر نام کوتاه کلمهٔ کلیدی را دارد همان نام (طبیعی‌تر)، وگرنه خود کلمهٔ کلیدی
    /// (تا کلمه حتماً در متن باشد).
    /// </summary>
    public string Lead => Keyword.Length == 0 || KeywordMatcher.Contains(ShortName, Keyword) ? ShortName : Keyword;

    private IReadOnlyList<string>? _terms;
    private List<SpecFact>? _facts;
    private List<SpecFact>? _distinctive;

    /// <summary>عبارت‌های خاص این صفحه که در «اسکلت» جمله‌ها حذف می‌شوند (نام، برند، مدل …).</summary>
    public IReadOnlyList<string> Terms => _terms ??= PhraseUsage.TermsOf(Subject, Keyword);

    /// <summary>
    /// «قالبی بودن» متن نسبت به صفحه‌های هم‌نوع دیگر سایت (۰ تا ۱). بدون نمای سایت → ۰.
    /// مثال: اگر عنوان «قیمت و خرید …» در ۸۰٪ محصولات استفاده شده باشد، حدود ۰٫۸.
    /// </summary>
    public double Commonness(string text) => Site?.Usage.Commonness(Subject.Kind, text, Terms, Subject.EntityId) ?? 0;

    /// <summary>آمار مشخصات دستهٔ این محصول در فروشگاه (برای مقایسه و ویژگی‌های کمیاب). null = نامعلوم.</summary>
    public CategoryProfile? Profile => Site?.Category(Subject.Product?.CategorySlug ?? Subject.Product?.CategoryName);

    /// <summary>واژهٔ نوع کالا با املای نمایشی. مثال: «هدفون بی‌سیم هایلو …» → «هدفون».</summary>
    public string? TypeWord => Subject.Kind == SeoEntityKind.Product
        ? ShortName.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault(w => PersianText.PersianRatio(w) == 1 && !PersianText.IsStopWord(PersianText.Normalize(w)))
        : null;

    /// <summary>
    /// بخش مدل نام (واژه‌های لاتین/عددی پشت سر هم با املای اصلی). مثال: «… شیائومی مدل Redmi Buds 4» → «Redmi Buds 4».
    /// </summary>
    public string? ModelName
    {
        get
        {
            var words = ShortName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var model = new List<string>();
            foreach (var word in words)
            {
                var latin = PersianText.PersianRatio(word) == 0 && word.Any(char.IsLetterOrDigit);
                if (latin) model.Add(word);
                else if (model.Count > 0) break;
            }
            var text = string.Join(' ', model);
            return text.Length is >= 2 and <= 30 ? text : null;
        }
    }

    /// <summary>مشخصات قابل استفادهٔ محصول (بدون «ندارد» و تکراری)، حداکثر ۱۰ تا.</summary>
    public IReadOnlyList<SpecFact> Facts => _facts ??= (Subject.Product?.Features ?? [])
        .Select(SpecKnowledge.Read).OfType<SpecFact>()
        .DistinctBy(f => f.NormalizedKey, StringComparer.Ordinal)
        .Take(10).ToList();

    /// <summary>
    /// مشخصات به ترتیب «ارزش برجسته کردن» = تمایز × اهمیت موضوع (برای عنوان، معرفی و توضیحات متا).
    /// تمایز: قابلیت «دارد» = سهم هم‌دسته‌هایی که ندارند؛ عدد جهت‌دار = چقدر از میانگین دسته بهتر است؛
    /// بقیه = کمیاب بودن مقدار (حداکثر ۰٫۶). بدون آمار دسته، همه ۰٫۵ (فقط اهمیت موضوع و ترتیب اصلی مهم است).
    /// مثال: در دسته‌ای که فقط ۲ از ۱۰ هدفون حذف نویز دارند، «حذف نویز» جلوتر از «بلوتوث 5.1» می‌آید.
    /// </summary>
    public IReadOnlyList<SpecFact> Distinctive => _distinctive ??= Facts
        .Select((f, i) => (Fact: f, Index: i, Value: Distinctness(f) * (1 + SpecKnowledge.Weight(f.Topic))))
        .OrderByDescending(x => Math.Round(x.Value, 2))
        .ThenBy(x => x.Index)
        .Select(x => x.Fact).ToList();

    private double Distinctness(SpecFact fact)
    {
        var profile = Profile;
        if (profile is null || profile.Count < 3)
            return 0.5;
        if (fact.IsYes)
            return 1 - profile.Share(Subject.EntityId, fact.Key, fact.Value);
        if (fact.Direction != 0 && fact.Number is > 0)
            return profile.Average(Subject.EntityId, fact.Key) is { Average: > 0 } average
                ? Math.Clamp((fact.Number.Value / average.Average - 1) * fact.Direction * 2, 0, 1)
                : 0.3;
        return Math.Min(0.6, 1 - profile.Share(Subject.EntityId, fact.Key, fact.Value));
    }

    /// <summary>عبارت‌های برجستهٔ محصول برای عنوان/متا (حداکثر ۳). مثال: «حذف نویز فعال»، «40 ساعت شارژدهی».</summary>
    public IReadOnlyList<string> Highlights => Distinctive.Select(SpecKnowledge.Highlight).OfType<string>().Take(3).ToList();

    /// <summary>عبارت روان‌تر طبق مدل زبانی (۰ تا ۱).</summary>
    public double Fluency(string text)
    {
        if (Language is null || Language.TokenCount < 200)
            return 0.5; // پیکرهٔ آموزشی خیلی کوچک → بی‌طرف
        return Math.Clamp((Language.Fluency(text) + 12) / 10, 0, 1);
    }

    /// <summary>نویز کوچک ثابت برای شکستن تساوی و تنوع بین نسخه‌ها.</summary>
    public double Noise() => (Random.NextDouble() - 0.5) * (Variant == 0 ? 0.25 : 0.6);
}

/// <summary>نویسندهٔ متن‌های سئو.</summary>
public static partial class ContentWriter
{
    // ── عنوان ───────────────────────────────────────────────────────────

    /// <summary>
    /// بهترین عنوان (بدون نام سایت). null = عنوانی بهتر از فعلی پیدا نشد.
    /// مثال محصول: «قیمت و خرید هدفون بی‌سیم سونی مدل WH-1000XM5».
    /// </summary>
    public static string? BestTitle(WriterContext ctx) =>
        Rank(TitleOptions(ctx), ctx, ScoreTitle).FirstOrDefault();

    /// <summary>همهٔ عنوان‌های نامزد (قبل از رتبه‌بندی؛ برای تست و عیب‌یابی).</summary>
    public static List<string> TitleOptions(WriterContext ctx)
    {
        var s = ctx.Subject;
        var lead = ctx.Lead;
        var name = PersianText.Clean(s.Name);
        var options = new List<string>();
        switch (s.Kind)
        {
            case SeoEntityKind.Product:
                var warranty = WarrantyPhrase(s.Product);
                foreach (var l in new[] { lead, ctx.Keyword }.Where(x => x.Length > 0).Distinct())
                {
                    options.Add($"قیمت و خرید {l}");
                    options.Add($"خرید {l}");
                    options.Add($"{l}؛ قیمت و مشخصات");
                    options.Add($"{l} | مشخصات، قیمت و خرید");
                    options.Add($"مشخصات و قیمت {l}");
                    options.Add($"{l} + قیمت روز");
                    options.Add($"{l}، قیمت و خرید آنلاین");
                    if (warranty is not null)
                    {
                        options.Add($"خرید {l} با {warranty}");
                        options.Add($"{l} با {warranty}");
                    }
                    // عنوان‌های «متمایزکننده» با برجسته‌ترین ویژگی واقعی محصول (هم یکتاتر، هم نرخ کلیک بهتر).
                    foreach (var highlight in ctx.Highlights.Take(2))
                    {
                        options.Add($"{l} با {highlight}");
                        options.Add($"{l}؛ {highlight}");
                        options.Add($"خرید {l} با {highlight}");
                        options.Add($"{l}، {highlight} | مشخصات و قیمت");
                    }
                }
                options.Add(name);
                break;
            case SeoEntityKind.Category:
                var brands = s.Listing?.RelatedGroups.Select(g => g.Text).Take(2).ToList() ?? [];
                options.Add($"خرید {name} با قیمت روز");
                options.Add($"قیمت و خرید {name}");
                options.Add($"{name}؛ خرید آنلاین و مقایسهٔ قیمت");
                options.Add($"انواع {name} با قیمت روز");
                options.Add($"خرید اینترنتی {name}");
                options.Add($"{name} | قیمت، مشخصات و خرید");
                if (brands.Count == 2)
                    options.Add($"خرید {name} {brands[0]} و {brands[1]}");
                if (ctx.Keyword.Length > 0 && !KeywordMatcher.Contains(name, ctx.Keyword))
                    options.Add($"{ctx.Keyword}؛ قیمت و خرید {name}");
                break;
            case SeoEntityKind.Brand:
                var english = PersianText.Clean(s.EnglishName);
                options.Add($"محصولات {name}؛ قیمت و خرید");
                options.Add($"خرید محصولات {name} با قیمت روز");
                options.Add($"قیمت محصولات {name}");
                if (english.Length > 0)
                {
                    options.Add($"محصولات {name} ({english})؛ قیمت و خرید");
                    options.Add($"{name} {english} | خرید اینترنتی محصولات");
                }
                break;
            case SeoEntityKind.BlogPost:
                options.Add(name);
                foreach (var separator in new[] { ":", "؛", "|", " - ", " – ", "؟" })
                {
                    var index = name.IndexOf(separator, StringComparison.Ordinal);
                    if (index > 15)
                        options.Add(name[..index].Trim() + (separator == "؟" ? "؟" : ""));
                }
                if (ctx.Keyword.Length > 0 && !KeywordMatcher.Contains(name, ctx.Keyword))
                {
                    options.Add($"{ctx.Keyword}: {name}");
                    options.Add($"{ctx.Keyword}؛ {name}");
                }
                if (ctx.Keyword.Length > 0)
                {
                    options.Add($"{ctx.Keyword}: راهنمای کامل");
                    options.Add($"راهنمای {ctx.Keyword}");
                }
                break;
            case SeoEntityKind.StaticPage:
                options.AddRange(StaticTitleOptions(ctx));
                break;
            default:
                options.Add(name);
                if (!string.IsNullOrWhiteSpace(ctx.Store.Tagline))
                    options.Add($"{name} | {PersianText.Truncate(ctx.Store.Tagline!, 40).TrimEnd('…')}");
                options.Add($"{name} فروشگاه اینترنتی {ctx.Store.SiteName}");
                break;
        }

        if (ctx.Style.HasFlag(WriterStyle.LessPromotional))
            options = options.Where(o => !o.Contains("خرید") && !o.Contains("قیمت روز")).DefaultIfEmpty(name).ToList();

        return options.Select(PersianText.Clean).Where(o => o.Length > 0).Distinct(StringComparer.Ordinal).ToList();
    }

    private static double ScoreTitle(string title, WriterContext ctx)
    {
        var full = title.Length + ctx.Store.TitleSuffixLength;
        var score = 0.0;
        if (full is >= SeoLimits.TitleMin and <= SeoLimits.TitleMax) score += 3;
        else if (full is > SeoLimits.TitleMax and <= 70 or >= 20 and < SeoLimits.TitleMin) score += 1;
        else score -= 3;

        // نزدیکی به طول میانگین عنوان رقبا (در محدودهٔ مجاز ما).
        var target = Math.Clamp(ctx.Insight?.AverageTitleLength > 0 ? ctx.Insight.AverageTitleLength : 50, 38, ctx.Store.TitleBudget);
        score += 1 - Math.Min(1, Math.Abs(title.Length - target) / 30.0);

        if (ctx.Keyword.Length > 0)
        {
            var position = KeywordMatcher.FirstPosition(title, ctx.Keyword);
            score += position switch { < 0 => -3, <= 0.34 => 3, _ => 1.5 };
        }

        if (ctx.Site is not null && SeoSiteIndex.Others(ctx.Site.Titles, title, ctx.Subject.EntityId) > 0)
            score -= 5;
        // الگوی عنوانی که صفحه‌های هم‌نوع زیاد دارند («قیمت و خرید …» روی همهٔ محصولات) امتیاز کمتری می‌گیرد.
        score -= 2.5 * ctx.Commonness(title);

        score += TermBonus(title, ctx.Insight?.TitleTerms, ctx.Insight?.SampleTitles.Count ?? 0, 0.3, 1.2);
        score += StyleBonus(title.Length, ctx.Style, 45);
        return score;
    }

    // ── توضیحات متا ──────────────────────────────────────────────────────

    /// <summary>بهترین توضیحات متا (۱۲۰ تا ۱۶۰ نویسه اگر ممکن باشد).</summary>
    public static string? BestMeta(WriterContext ctx) =>
        Rank(MetaOptions(ctx), ctx, ScoreMeta).FirstOrDefault();

    /// <summary>توضیحات نامزد. برای مقاله و صفحه استخراجی (از جمله‌های خود متن با TextRank) است.</summary>
    public static List<string> MetaOptions(WriterContext ctx)
    {
        var s = ctx.Subject;
        var options = new List<string>();
        var site = ctx.Store.SiteName;
        var facts = StoreFactPhrases(ctx);

        switch (s.Kind)
        {
            case SeoEntityKind.Product:
            {
                var lead = ctx.Lead;
                var productFacts = new List<string>();
                if (WarrantyPhrase(s.Product) is { } warranty) productFacts.Add(warranty);
                productFacts.AddRange(facts);
                var features = FeaturePhrases(s.Product).Take(3).ToList();
                var seconds = new List<string> { "" , "مشخصات فنی و تصاویر را ببینید." };
                if (features.Count >= 2) seconds.Add($"دارای {features[0]} و {features[1]}.");
                if (features.Count >= 1) seconds.Add($"دارای {features[0]}.");
                if (ctx.Store.FreeShippingThreshold > 0)
                    seconds.Add($"ارسال رایگان برای خریدهای بالای {PersianText.Money(ctx.Store.FreeShippingThreshold)} تومان.");

                // توضیحات متمایز با ویژگی‌های برجستهٔ واقعی (هر محصول ویژگی‌های خودش را دارد).
                var highlights = ctx.Highlights;
                if (highlights.Count >= 1)
                {
                    var h = highlights.Select(SpecKnowledge.Safe).ToList();
                    var type = ctx.TypeWord ?? "محصولی";
                    var list = PersianText.JoinList(h);
                    var patterns = new List<string>
                    {
                        $"{lead} با {list}؛ [مشخصات کامل|مشخصات فنی]، تصاویر و شرایط خرید را در {site} ببینید.",
                        $"[به دنبال|دنبال] {type} با {h[0]} هستید؟ {lead} را در {site} [ببینید|بررسی کنید] و آنلاین سفارش دهید.",
                        $"{lead}: {list}. [مشخصات|جزئیات] را ببینید و [آنلاین سفارش دهید|از {site} آنلاین بخرید].",
                    };
                    foreach (var fact in productFacts.Take(2))
                        patterns.Add($"{lead} با {list} و {fact}؛ [خرید آنلاین|سفارش آنلاین] از {site}.");
                    foreach (var pattern in patterns)
                        options.AddRange(Spin.Expand(pattern));
                }

                foreach (var subset in Subsets(productFacts, 3))
                {
                    var with = subset.Count == 0 ? "" : " با " + PersianText.JoinList(subset);
                    var firsts = new List<string>
                    {
                        $"خرید {lead}{with} از {site}.",
                        $"قیمت روز و مشخصات کامل {lead} را در {site} ببینید" + (subset.Count == 0 ? "." : $" و{with} آنلاین سفارش دهید."),
                        $"{lead} را{with} از {site} آنلاین بخرید.",
                    };
                    foreach (var first in firsts)
                        foreach (var second in seconds)
                            options.Add((first + " " + second).Trim());
                }
                break;
            }
            case SeoEntityKind.Category:
            case SeoEntityKind.Brand:
            {
                var name = PersianText.Clean(s.Name);
                var groups = s.Listing?.RelatedGroups.Select(g => g.Text).Take(3).ToList() ?? [];
                var subs = s.Listing?.SubCategories.Select(g => g.Text).Take(3).ToList() ?? [];
                var seconds = new List<string> { "" };
                foreach (var subset in Subsets(facts, 3).Where(x => x.Count > 0))
                    seconds.Add(PersianText.JoinList(subset) + ".");

                var firsts = new List<string>();
                if (s.Kind == SeoEntityKind.Category)
                {
                    firsts.Add($"خرید {name} با قیمت روز از {site}.");
                    firsts.Add($"قیمت روز و مشخصات انواع {name} را در {site} مقایسه کنید و آنلاین سفارش دهید.");
                    if (groups.Count >= 2) firsts.Add($"خرید {name} از برندهای {PersianText.JoinList(groups)} با قیمت روز از {site}.");
                    if (subs.Count >= 2) firsts.Add($"انواع {name} شامل {PersianText.JoinList(subs)} را با مقایسهٔ قیمت و مشخصات در {site} ببینید.");
                }
                else
                {
                    var english = string.IsNullOrWhiteSpace(s.EnglishName) ? "" : $" ({PersianText.Clean(s.EnglishName)})";
                    firsts.Add($"خرید محصولات {name}{english} با قیمت روز از {site}.");
                    firsts.Add($"قیمت و مشخصات انواع محصولات {name} را در {site} مقایسه کنید و آنلاین سفارش دهید.");
                    if (groups.Count >= 2) firsts.Add($"خرید محصولات {name}{english} در دسته‌های {PersianText.JoinList(groups)} با قیمت روز از {site}.");
                }
                foreach (var first in firsts)
                    foreach (var second in seconds)
                        options.Add((first + " " + second).Trim());
                break;
            }
            case SeoEntityKind.StaticPage:
                options.AddRange(StaticMetaOptions(ctx));
                break;
            default:
                options.AddRange(ExtractiveMetas(ctx));
                break;
        }

        if (ctx.Style.HasFlag(WriterStyle.LessPromotional))
        {
            var calm = options.Where(o => !o.StartsWith("خرید", StringComparison.Ordinal)).ToList();
            if (calm.Count > 0) options = calm;
        }

        return options.Select(PersianText.Clean).Where(o => o.Length >= 50).Distinct(StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// توضیحات استخراجی برای مقاله/صفحه: مهم‌ترین جمله‌ها (TextRank) که کلمهٔ کلیدی را دارند، در ۱۲۰ تا ۱۶۰ نویسه.
    /// اگر هیچ جمله‌ای کلمه را نداشت، جمله با «در این مقاله دربارهٔ … می‌خوانید:» شروع می‌شود.
    /// </summary>
    private static IEnumerable<string> ExtractiveMetas(WriterContext ctx)
    {
        var text = (PersianText.HtmlToText(ctx.Subject.ShortText) + "\n" + PersianText.HtmlToText(ctx.Subject.BodyHtml)).Trim();
        var sentences = PersianText.Sentences(text).Where(x => PersianText.WordCount(x) is >= 6 and <= 45).Take(80).ToList();
        if (sentences.Count == 0)
            yield break;

        var ranked = TextModels.TextRank(sentences).Take(8).ToList();
        foreach (var (sentence, _, position) in ranked)
        {
            var clean = PersianText.Clean(sentence);
            var hasKeyword = ctx.Keyword.Length == 0 || KeywordMatcher.Contains(clean, ctx.Keyword);
            var baseText = hasKeyword ? clean : $"در این مقاله دربارهٔ {ctx.Keyword} می‌خوانید: {clean}";
            if (ctx.Subject.Kind == SeoEntityKind.ContentPage && !hasKeyword)
                baseText = $"{ctx.Keyword}: {clean}";
            yield return Fit(baseText);

            // جملهٔ بعدی متن را هم اضافه کن اگر جا هست (خلاصهٔ دوجمله‌ای طبیعی‌تر است).
            if (baseText.Length < SeoLimits.MetaMin && position + 1 < sentences.Count)
                yield return Fit(baseText + " " + PersianText.Clean(sentences[position + 1]));
        }

        static string Fit(string value) => value.Length <= SeoLimits.MetaMax ? value : PersianText.Truncate(value, SeoLimits.MetaMax - 2);
    }

    private static double ScoreMeta(string meta, WriterContext ctx)
    {
        var score = meta.Length switch
        {
            >= SeoLimits.MetaMin and <= SeoLimits.MetaMax => 3.0,
            >= 100 and < SeoLimits.MetaMin => 1.5,
            > SeoLimits.MetaMax and <= 175 => 0.5,
            _ => -2.0,
        };
        if (ctx.Keyword.Length > 0)
        {
            var position = KeywordMatcher.FirstPosition(meta, ctx.Keyword);
            score += position < 0 ? -3 : 2 + (1 - position);
        }
        if (ctx.Site is not null && SeoSiteIndex.Others(ctx.Site.Descriptions, meta, ctx.Subject.EntityId) > 0)
            score -= 5;
        score -= 2.5 * ctx.Commonness(meta);
        score += TermBonus(meta, ctx.Insight?.CommonTerms, Math.Max(1, ctx.Insight?.PagesAnalyzed ?? 1), 0.15, 0.9);
        score += StyleBonus(meta.Length, ctx.Style, 140);
        return score;
    }

    // ── متن صفحه ─────────────────────────────────────────────────────────

    /// <summary>
    /// متن تکمیلی برای صفحهٔ کم‌محتوا (محصول، دسته، برند). متن فعلی اول می‌آید و دست‌نخورده می‌ماند.
    /// null = نوشتن متن مجاز نیست (متن کافی است، مقاله است، یا واقعیت کافی برای نوشتن نداریم).
    /// </summary>
    public static string? Body(WriterContext ctx)
    {
        var s = ctx.Subject;
        if (s.Kind is not (SeoEntityKind.Product or SeoEntityKind.Category or SeoEntityKind.Brand))
            return null;
        var existingHtml = s.BodyHtml?.Trim() ?? "";
        var existingWords = PersianText.AnalyzeHtml(existingHtml).Words;
        if (existingWords >= SeoLimits.ThinContentWords)
            return null;

        // تا ۵ بار با دانه‌های متفاوت نوشته می‌شود؛ از نسخه‌هایی که تراکم کلمهٔ کلیدی سالم دارند (۰٫۵ تا ۲٫۴ درصد)،
        // نسخه‌ای که از متن بقیهٔ صفحه‌ها دورتر است (SimHash) انتخاب می‌شود. اگر هیچ‌کدام تراکم سالم نداشت، دورترین.
        string? best = null;
        var bestDistance = -1;
        var bestHealthy = false;
        for (var salt = 0; salt < 5; salt++)
        {
            var composer = new Composer(ctx, salt);
            var sections = s.Kind == SeoEntityKind.Product ? ProductSections(composer) : ListingSections(composer);
            if (sections.Count == 0)
                return null;

            var builder = new StringBuilder();
            if (existingHtml.Length > 0)
                builder.Append(existingHtml).Append('\n');
            foreach (var section in sections)
                builder.Append(section).Append('\n');
            var html = builder.ToString().Trim();

            // اگر چیزی جز متن فعلی اضافه نشد یا کمتر از ۴۰ واژه اضافه شد، ارزش پیشنهاد ندارد.
            if (PersianText.AnalyzeHtml(html).Words - existingWords < 40)
                return null;
            var distance = NearestDistance(ctx, html);
            var healthy = HealthyDensity(ctx, html);
            if (healthy && !bestHealthy || healthy == bestHealthy && distance > bestDistance)
            {
                best = html;
                bestDistance = distance;
                bestHealthy = healthy;
            }
            if (bestHealthy && bestDistance >= 10)
                break;
        }
        return best;
    }

    /// <summary>
    /// تراکم کلمهٔ کلیدی در محدودهٔ امن (۰٫۵ تا ۲٫۴ درصد، کمی سخت‌گیرانه‌تر از مرز ۲٫۵ تحلیل‌گر)؟
    /// بدون کلمهٔ کلیدی یا متن کوتاه‌تر از ۱۰۰ واژه (که تحلیل‌گر تراکمش را نمی‌سنجد) → true.
    /// </summary>
    private static bool HealthyDensity(WriterContext ctx, string html)
    {
        if (string.IsNullOrWhiteSpace(ctx.Keyword))
            return true;
        var text = PersianText.HtmlToText(html);
        var words = PersianText.WordCount(text);
        if (words < 100)
            return true;
        var density = 100.0 * KeywordMatcher.Count(text, ctx.Keyword) / words;
        return density is >= 0.5 and <= 2.4;
    }

    // ── پرسش‌وپاسخ ───────────────────────────────────────────────────────

    /// <summary>
    /// پرسش‌وپاسخ فقط از واقعیت‌ها (گارانتی، ویژگی‌ها، زمان ارسال، مرجوعی، پرداخت در محل، برندها/دسته‌ها) یا برای مقاله
    /// از تیترهای پرسشی خود مقاله. کمتر از ۲ مورد → خالی.
    /// </summary>
    public static List<SeoFaqItem> Faq(WriterContext ctx)
    {
        var s = ctx.Subject;
        if (s.Kind == SeoEntityKind.BlogPost)
            return QuestionHeadings(s.BodyHtml);
        if (s.Kind is SeoEntityKind.Product or SeoEntityKind.Category or SeoEntityKind.Brand)
            return CommercialFaq(ctx);
        return [];
    }

    /// <summary>
    /// تیترهای پرسشی مقاله (پایان با «؟» یا شروع با چرا/چگونه/آیا/چه/کدام …) و اولین پاراگراف بعد از هرکدام.
    /// پاسخ حداکثر ۳۰۰ نویسه. مثال: «&lt;h2&gt;چرا باتری زود خالی می‌شود؟&lt;/h2&gt;&lt;p&gt;…&lt;/p&gt;».
    /// </summary>
    public static List<SeoFaqItem> QuestionHeadings(string? html)
    {
        var items = new List<SeoFaqItem>();
        if (string.IsNullOrWhiteSpace(html))
            return items;
        var document = new HtmlParser().ParseDocument("<html><body>" + html + "</body></html>");
        string[] starters = ["چرا", "چگونه", "چطور", "آیا", "چه", "کدام", "چقدر", "چند", "کی", "کجا"];
        foreach (var heading in document.QuerySelectorAll("h2,h3"))
        {
            var question = PersianText.Clean(heading.TextContent);
            var first = PersianText.Tokenize(question).FirstOrDefault() ?? "";
            if (!(question.EndsWith('؟') || question.EndsWith('?') || starters.Contains(first)))
                continue;
            var next = heading.NextElementSibling;
            while (next is not null && next.LocalName is not ("p" or "ul" or "ol" or "h2" or "h3"))
                next = next.NextElementSibling;
            if (next is null || next.LocalName is "h2" or "h3")
                continue;
            var answer = PersianText.Clean(next.TextContent);
            if (PersianText.WordCount(answer) < 6)
                continue;
            if (!question.EndsWith('؟') && !question.EndsWith('?'))
                question += "؟";
            items.Add(new(question, PersianText.Truncate(answer, 300)));
            if (items.Count >= 6)
                break;
        }
        return items.Count >= 2 ? items : [];
    }

    // ── متن جایگزین تصویر ─────────────────────────────────────────────────

    /// <summary>
    /// فقط برای تصویرهای «بدون alt» متن جایگزین می‌سازد (alt انسانی دست نمی‌خورد).
    /// تصویر اصلی: کلمهٔ کلیدی/نام؛ بقیه: «نام – تصویر ۲».
    /// </summary>
    public static List<SeoImageAlt> ImageAlts(WriterContext ctx)
    {
        var result = new List<SeoImageAlt>();
        var images = ctx.Subject.Images;
        var name = ctx.Subject.Kind == SeoEntityKind.BlogPost ? PersianText.Truncate(ctx.Subject.Name, 90) : ctx.ShortName;
        var anyKeyword = ctx.Keyword.Length > 0 && images.Any(i => !string.IsNullOrWhiteSpace(i.Alt) && KeywordMatcher.Contains(i.Alt!, ctx.Keyword, partial: true));
        for (var i = 0; i < images.Count; i++)
        {
            if (!string.IsNullOrWhiteSpace(images[i].Alt))
                continue;
            var primary = images[i].IsPrimary || i == 0 && !images.Any(x => x.IsPrimary);
            var alt = primary
                ? (!anyKeyword && ctx.Keyword.Length > 0 && !KeywordMatcher.Contains(name, ctx.Keyword) ? ctx.Keyword : name)
                : $"{name} – تصویر {PersianText.Digits(i + 1)}";
            result.Add(new SeoImageAlt(images[i].Id, i, PersianText.Truncate(alt, 140)));
        }
        return result;
    }

    // ── سبک از روی دلیل رد ───────────────────────────────────────────────

    /// <summary>
    /// تشخیص سبک از متن دلیل رد. مثال: «عنوان خیلی طولانی و تبلیغاتی است» → Shorter | LessPromotional.
    /// </summary>
    public static WriterStyle StyleFromReason(string? reason)
    {
        var text = PersianText.Normalize(reason);
        if (text.Length == 0)
            return WriterStyle.None;
        var style = WriterStyle.None;
        if (text.Contains("طولانی") || text.Contains("بلند") || text.Contains("کوتاه تر") || text.Contains("کوتاهتر") || text.Contains("خلاصه"))
            style |= WriterStyle.Shorter;
        else if (text.Contains("کوتاه") || text.Contains("کم است") || text.Contains("بیشتر بنویس"))
            style |= WriterStyle.Longer;
        if (text.Contains("تبلیغ") || text.Contains("اسپم") || text.Contains("فروش") || text.Contains("بازاری") || text.Contains("خرید"))
            style |= WriterStyle.LessPromotional;
        return style;
    }

    // ── ابزارهای داخلی ───────────────────────────────────────────────────

    /// <summary>
    /// رتبه‌بندی نامزدها: امتیاز قاعده‌ای + روانی (مدل زبانی) + نویز نسخه. نامزدهای ردشدهٔ قبلی حذف می‌شوند.
    /// </summary>
    private static List<string> Rank(IEnumerable<string> options, WriterContext ctx, Func<string, WriterContext, double> score) =>
        options
            .Where(o => !ctx.Avoid.Contains(PersianText.Normalize(o)))
            .Select(o => (Text: o, Score: score(o, ctx) + 0.6 * ctx.Fluency(o) + ctx.Noise()))
            .OrderByDescending(x => x.Score)
            .Select(x => x.Text)
            .ToList();

    /// <summary>عبارت‌های اسمی شرایط فروشگاه (برای «با …»). مثال: «امکان مرجوعی ۷ روزه».</summary>
    private static List<string> StoreFactPhrases(WriterContext ctx)
    {
        var list = new List<string>();
        if (ctx.Store.ReturnEnabled && ctx.Store.ReturnDays > 0)
            list.Add($"امکان مرجوعی {PersianText.Digits(ctx.Store.ReturnDays)} روزه");
        list.Add("ارسال به سراسر کشور");
        if (ctx.Store.CashOnDelivery)
            list.Add("امکان پرداخت در محل");
        return list;
    }

    /// <summary>«گارانتی ۱۸ ماهه سازگار» از عنوان/مدت گارانتی. null = گارانتی ثبت نشده.</summary>
    public static string? WarrantyPhrase(ProductFacts? facts)
    {
        if (facts is null)
            return null;
        var title = PersianText.Clean(facts.WarrantyTitle);
        if (title.Length > 0)
        {
            if (title.Contains("ندارد") || title.Contains("بدون"))
                return null;
            return title.Contains("گارانتی") ? title : "گارانتی " + title;
        }
        return facts.WarrantyMonths is > 0 ? $"گارانتی {PersianText.Digits(facts.WarrantyMonths.Value)} ماهه" : null;
    }

    /// <summary>عبارت ویژگی‌ها: «حافظهٔ داخلی ۲۵۶ گیگابایت»؛ ویژگی‌های «دارد» فقط با نام؛ «ندارد» حذف.</summary>
    private static IEnumerable<string> FeaturePhrases(ProductFacts? facts) =>
        (facts?.Features ?? []).Where(IsUsableFeature)
            .Select(f => IsYes(f.Value) ? PersianText.Clean(f.Key) : $"{PersianText.Clean(f.Key)} {PersianText.Clean(f.Value)}");

    private static bool IsUsableFeature(SeoFeature feature)
    {
        var value = PersianText.Normalize(feature.Value);
        return feature.Key.Length is > 1 and <= 40 && value.Length is > 0 and <= 40
            && value is not ("ندارد" or "خیر" or "no" or "false" or "-" or "0");
    }

    private static bool IsYes(string value) => PersianText.Normalize(value) is "دارد" or "بله" or "yes" or "true" or "✓";

    /// <summary>زیرمجموعه‌های مرتب (حفظ ترتیب اولویت) تا اندازهٔ max، شامل مجموعهٔ خالی.</summary>
    private static IEnumerable<List<string>> Subsets(IReadOnlyList<string> items, int max)
    {
        var limited = items.Take(5).ToList();
        var count = 1 << limited.Count;
        for (var mask = 0; mask < count; mask++)
        {
            var subset = new List<string>();
            for (var i = 0; i < limited.Count; i++)
                if ((mask & 1 << i) != 0)
                    subset.Add(limited[i]);
            if (subset.Count <= max)
                yield return subset;
        }
    }

    private static double TermBonus(string text, IReadOnlyList<CompetitorTerm>? terms, int documents, double each, double max)
    {
        if (terms is null || terms.Count == 0 || documents == 0)
            return 0;
        var stems = PersianText.ContentStems(text).ToHashSet(StringComparer.Ordinal);
        var bonus = terms.Where(t => stems.Contains(t.Stem)).Sum(t => each * Math.Min(1.0, (double)t.Pages / documents));
        return Math.Min(max, bonus);
    }

    private static double StyleBonus(int length, WriterStyle style, int pivot) =>
        style.HasFlag(WriterStyle.Shorter) ? (pivot - length) / 30.0
        : style.HasFlag(WriterStyle.Longer) ? (length - pivot) / 30.0
        : 0;

    /// <summary>
    /// پاراگراف HTML: همهٔ متن encode می‌شود به‌جز لینک‌هایی که خود نویسنده با <see cref="A"/> ساخته (و قبلاً encode شده‌اند).
    /// تا اگر نام کالا یا ویژگی علامت &lt; یا &amp; داشت، HTML خراب یا ناامن نشود.
    /// </summary>
    private static string P(string text)
    {
        var builder = new StringBuilder("<p>");
        var last = 0;
        foreach (System.Text.RegularExpressions.Match match in AnchorPattern.Matches(text))
        {
            builder.Append(WebUtility.HtmlEncode(text[last..match.Index])).Append(match.Value);
            last = match.Index + match.Length;
        }
        builder.Append(WebUtility.HtmlEncode(text[last..])).Append("</p>");
        return builder.ToString();
    }

    private static readonly System.Text.RegularExpressions.Regex AnchorPattern =
        // فقط لینک داخلی («/…» و نه «//…») که خود نویسنده با A() ساخته؛ لینکی که از داده (مشخصه/نظر) با javascript: یا
        // آدرس بیرونی بیاید با این الگو جور نمی‌شود و مثل متن ساده encode می‌شود (دور بیستم – آزمون حملهٔ توضیحات محصول).
        new("<a href=\"/(?!/)[^\"<>]*\">[^<]*</a>", System.Text.RegularExpressions.RegexOptions.Compiled);

    private static string H2(string text) => "<h2>" + WebUtility.HtmlEncode(text) + "</h2>";

    private static string A(string path, string text) =>
        $"<a href=\"{WebUtility.HtmlEncode(path)}\">{WebUtility.HtmlEncode(text)}</a>";
}
