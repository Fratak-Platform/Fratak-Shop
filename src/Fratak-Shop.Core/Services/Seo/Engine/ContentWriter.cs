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

    /// <summary>عبارت روان‌تر طبق مدل زبانی (۰ تا ۱).</summary>
    public double Fluency(string text)
    {
        if (Language is null || Language.TokenCount < 200)
            return 0.5; // پیکرهٔ آموزشی خیلی کوچک → بی‌طرف
        return Math.Clamp((Language.Fluency(text) + 12) / 10, 0, 1);
    }

    /// <summary>نویز کوچک ثابت برای شکستن تساوی و تنوع بین نسخه‌ها.</summary>
    public double Noise() => (Random.NextDouble() - 0.5) * (Variant == 0 ? 0.05 : 0.6);
}

/// <summary>نویسندهٔ متن‌های سئو.</summary>
public static class ContentWriter
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

        var sections = s.Kind == SeoEntityKind.Product ? ProductSections(ctx) : ListingSections(ctx);
        if (sections.Count == 0)
            return null;

        var builder = new StringBuilder();
        if (existingHtml.Length > 0)
            builder.Append(existingHtml).Append('\n');
        foreach (var section in sections)
            builder.Append(section).Append('\n');
        var html = builder.ToString().Trim();

        // اگر چیزی جز متن فعلی اضافه نشد یا کمتر از ۴۰ واژه اضافه شد، ارزش پیشنهاد ندارد.
        return PersianText.AnalyzeHtml(html).Words - existingWords < 40 ? null : html;
    }

    private static List<string> ProductSections(WriterContext ctx)
    {
        var s = ctx.Subject;
        var p = s.Product;
        var lead = ctx.Lead;
        var name = ctx.ShortName;
        var sections = new List<string>();
        var features = FeaturePhrases(p).ToList();

        // ۱) معرفی: نوع، برند، دسته + مهم‌ترین ویژگی‌ها + گارانتی (فقط اگر داده موجود باشد).
        var intro = new List<string>();
        if (p?.BrandName is { } brand && p.CategoryName is { } category)
            intro.Add(Pick(ctx,
                $"{lead} از محصولات برند {brand} در دستهٔ {category} است.",
                $"{lead} یکی از محصولات دستهٔ {category} است که برند {brand} آن را عرضه کرده است.",
                $"{lead} محصولی از برند {brand} است و در گروه {category} قرار می‌گیرد."));
        else if (p?.CategoryName is { } onlyCategory)
            intro.Add(Pick(ctx, $"{lead} از محصولات دستهٔ {onlyCategory} است.", $"{lead} در گروه {onlyCategory} قرار می‌گیرد."));
        else
            intro.Add($"در این صفحه مشخصات و شرایط خرید {lead} را می‌بینید.");

        if (features.Count >= 3)
            intro.Add(Pick(ctx,
                $"از مشخصات مهم این محصول می‌توان به {features[0]}، {features[1]} و {features[2]} اشاره کرد.",
                $"{features[0]}، {features[1]} و {features[2]} از ویژگی‌های اصلی آن است.",
                $"این کالا با {features[0]}، {features[1]} و {features[2]} عرضه می‌شود."));
        else if (features.Count > 0)
            intro.Add(Pick(ctx, $"از ویژگی‌های آن {PersianText.JoinList(features)} است.", $"این کالا با {PersianText.JoinList(features)} عرضه می‌شود."));

        if (WarrantyPhrase(p) is { } warranty)
            intro.Add(Pick(ctx, $"این کالا با {warranty} عرضه می‌شود.", $"{name} همراه با {warranty} به دست شما می‌رسد."));
        sections.Add(P(string.Join(' ', intro)));

        // ۲) ویژگی‌ها به نثر (هر ویژگی یک جمله با ساختارهای گوناگون). فقط با حداقل ۴ ویژگی.
        if (features.Count >= 4 && p is not null)
        {
            var sentences = new List<string>();
            foreach (var feature in p.Features.Where(IsUsableFeature).Take(8))
            {
                var key = PersianText.Clean(feature.Key);
                var value = PersianText.Clean(feature.Value);
                if (IsYes(value))
                    sentences.Add(Pick(ctx, $"این محصول از {key} پشتیبانی می‌کند.", $"{key} یکی از قابلیت‌های آن است.", $"{name} دارای {key} است."));
                else
                    sentences.Add(Pick(ctx, $"{key} این محصول {value} است.", $"{name} دارای {key} {value} است.", $"از نظر {key}، مقدار {value} ارائه شده است.", $"{key}: {value}."));
            }
            sentences.Add("فهرست کامل مشخصات فنی در جدول مشخصات همین صفحه آمده است.");
            sections.Add(H2(Pick(ctx, $"ویژگی‌های {name}", $"مشخصات کلیدی {name}", $"نگاهی به ویژگی‌های {name}")));
            sections.Add(P(string.Join(' ', sentences)));
        }

        // ۳) شرایط خرید از فروشگاه (همه از تنظیمات) + لینک داخلی به دسته و برند.
        var buy = new List<string>
        {
            Pick(ctx,
                $"برای خرید {lead} کافی است آن را به سبد خرید اضافه کنید و سفارش را آنلاین ثبت کنید.",
                $"خرید {lead} از {ctx.Store.SiteName} به‌صورت آنلاین و در چند مرحلهٔ ساده انجام می‌شود."),
        };
        buy.AddRange(StoreSentences(ctx));
        var links = new List<string>();
        if (p?.CategorySlug is { } categorySlug && p.CategoryName is { } categoryName)
            links.Add(A("/category/" + categorySlug, categoryName));
        if (p?.BrandSlug is { } brandSlug && p.BrandName is { } brandName)
            links.Add(A("/brand/" + brandSlug, "محصولات " + brandName));
        if (links.Count > 0)
            buy.Add($"برای دیدن مدل‌های مشابه، صفحهٔ {PersianText.JoinList(links)} را ببینید.");
        sections.Add(H2(Pick(ctx, $"خرید {lead}", $"شرایط خرید {name}", $"خرید {name} از {ctx.Store.SiteName}")));
        sections.Add(P(string.Join(' ', buy)));
        return sections;
    }

    private static List<string> ListingSections(WriterContext ctx)
    {
        var s = ctx.Subject;
        var listing = s.Listing;
        if (listing is null || listing.ProductCount == 0)
            return [];
        var name = PersianText.Clean(s.Name);
        var lead = ctx.Lead;
        var site = ctx.Store.SiteName;
        var sections = new List<string>();
        var count = listing.ProductCount >= 10 ? $"بیش از {PersianText.Digits(listing.ProductCount / 10 * 10)} مدل" : "مدل‌های مختلف";
        var groups = listing.RelatedGroups.Take(4).ToList();

        var intro = new List<string>();
        if (s.Kind == SeoEntityKind.Category)
        {
            intro.Add(Pick(ctx,
                $"در این صفحه {count} از {lead} را با قیمت روز و مشخصات کامل می‌بینید.",
                $"{lead} در {site} با {count} و امکان مقایسهٔ مشخصات عرضه می‌شود.",
                $"این صفحه {count} از {lead} را یک‌جا نشان می‌دهد تا راحت‌تر مقایسه و انتخاب کنید."));
            if (groups.Count >= 2)
                intro.Add(Pick(ctx,
                    $"محصولات این دسته از برندهایی مانند {PersianText.JoinList(groups.Select(g => A(g.Path, g.Text)).ToList())} عرضه می‌شوند.",
                    $"برندهای {PersianText.JoinList(groups.Select(g => A(g.Path, g.Text)).ToList())} از جمله برندهای این دسته‌اند."));
            if (listing.ParentName is { } parent)
                intro.Add($"این دسته زیرمجموعهٔ {parent} است.");
        }
        else
        {
            intro.Add(Pick(ctx,
                $"در این صفحه {count} از {lead} را با قیمت روز و مشخصات کامل می‌بینید.",
                $"{site} {count} از محصولات {name} را با امکان مقایسهٔ مشخصات عرضه می‌کند."));
            if (groups.Count >= 1)
                intro.Add($"محصولات {name} در {site} در دسته‌های {PersianText.JoinList(groups.Select(g => A(g.Path, g.Text)).ToList())} قرار دارند.");
        }
        sections.Add(P(string.Join(' ', intro)));

        if (listing.SubCategories.Count >= 2)
        {
            sections.Add(H2(Pick(ctx, $"انواع {name}", $"زیردسته‌های {name}")));
            sections.Add(P($"برای پیدا کردن سریع‌تر محصول مورد نظر، یکی از زیردسته‌ها را انتخاب کنید: {PersianText.JoinList(listing.SubCategories.Take(8).Select(c => A(c.Path, c.Text)).ToList())}."));
        }

        if (listing.CommonFeatureKeys.Count >= 2)
        {
            var keys = listing.CommonFeatureKeys.Take(4).ToList();
            var guide = new List<string>
            {
                Pick(ctx,
                    $"هنگام انتخاب {name} به {PersianText.JoinList(keys)} توجه کنید؛ این مشخصات برای هر محصول در صفحهٔ همان محصول آمده است.",
                    $"مهم‌ترین مشخصاتی که در خرید {name} باید مقایسه کنید {PersianText.JoinList(keys)} است."),
                "با ابزار مقایسه می‌توانید چند مدل را کنار هم ببینید و تفاوت‌هایشان را بسنجید.",
            };
            if (listing.WarrantyPercent >= 60)
                guide.Add("بیشتر محصولات این بخش گارانتی دارند؛ نوع و مدت گارانتی هر کالا در صفحهٔ آن نوشته شده است.");
            sections.Add(H2(Pick(ctx, $"راهنمای خرید {name}", $"نکات انتخاب {name}")));
            sections.Add(P(string.Join(' ', guide)));
        }

        if (listing.TopProducts.Count >= 3)
        {
            sections.Add(H2(Pick(ctx, $"پرطرفدارترین {name}", $"محبوب‌ترین محصولات {name}")));
            sections.Add("<ul>" + string.Concat(listing.TopProducts.Take(6).Select(t => "<li>" + A(t.Path, t.Text) + "</li>")) + "</ul>");
        }

        var buy = new List<string>
        {
            Pick(ctx, $"خرید {lead} از {site} آنلاین انجام می‌شود.", $"برای خرید {lead} محصول مورد نظر را به سبد خرید اضافه کنید و سفارش را آنلاین ثبت کنید."),
        };
        buy.AddRange(StoreSentences(ctx));
        sections.Add(H2(Pick(ctx, $"خرید {lead}", $"شرایط خرید {name} از {site}")));
        sections.Add(P(string.Join(' ', buy)));
        return sections;
    }

    // ── پرسش‌وپاسخ ───────────────────────────────────────────────────────

    /// <summary>
    /// پرسش‌وپاسخ فقط از واقعیت‌ها (گارانتی، ویژگی‌ها، زمان ارسال، مرجوعی، پرداخت در محل، برندها/دسته‌ها) یا برای مقاله
    /// از تیترهای پرسشی خود مقاله. کمتر از ۲ مورد → خالی.
    /// </summary>
    public static List<SeoFaqItem> Faq(WriterContext ctx)
    {
        var s = ctx.Subject;
        var items = new List<SeoFaqItem>();
        var name = ctx.ShortName;
        var store = ctx.Store;

        switch (s.Kind)
        {
            case SeoEntityKind.Product:
                if (WarrantyPhrase(s.Product) is { } warranty)
                    items.Add(new($"{name} گارانتی دارد؟", $"بله، {name} با {warranty} عرضه می‌شود."));
                foreach (var feature in (s.Product?.Features ?? []).Where(f => IsUsableFeature(f) && !IsYes(f.Value)).Take(2))
                {
                    var key = PersianText.Clean(feature.Key);
                    var asks = feature.Value.Any(char.IsDigit) ? "چقدر است؟" : "چیست؟";
                    items.Add(new($"{key} {name} {asks}", $"{key} {name} {PersianText.Clean(feature.Value)} است."));
                }
                break;
            case SeoEntityKind.Category when s.Listing is not null:
                var brands = s.Listing.RelatedGroups.Select(g => g.Text).Take(5).ToList();
                if (brands.Count >= 2)
                    items.Add(new($"{s.Name} از چه برندهایی موجود است؟", $"در این دسته محصولات برندهای {PersianText.JoinList(brands)} عرضه می‌شود."));
                var subs = s.Listing.SubCategories.Select(g => g.Text).Take(6).ToList();
                if (subs.Count >= 2)
                    items.Add(new($"انواع {s.Name} کدام‌اند؟", $"این دسته شامل {PersianText.JoinList(subs)} است."));
                if (s.Listing.WarrantyPercent >= 60)
                    items.Add(new($"{s.Name} گارانتی دارند؟", "بیشتر محصولات این دسته گارانتی دارند؛ نوع و مدت گارانتی هر کالا در صفحهٔ همان محصول آمده است."));
                break;
            case SeoEntityKind.Brand when s.Listing is not null:
                var categories = s.Listing.RelatedGroups.Select(g => g.Text).Take(5).ToList();
                if (categories.Count >= 1)
                    items.Add(new($"محصولات {s.Name} در چه دسته‌هایی عرضه می‌شود؟", $"محصولات {s.Name} در {store.SiteName} در دسته‌های {PersianText.JoinList(categories)} عرضه می‌شود."));
                var top = s.Listing.TopProducts.Select(t => t.Text).Take(3).ToList();
                if (top.Count >= 2)
                    items.Add(new($"پرطرفدارترین محصولات {s.Name} کدام‌اند؟", $"در حال حاضر {PersianText.JoinList(top)} از پربازدیدترین محصولات {s.Name} در {store.SiteName} هستند."));
                break;
            case SeoEntityKind.BlogPost:
                return QuestionHeadings(s.BodyHtml);
        }

        if (s.Kind is SeoEntityKind.Product or SeoEntityKind.Category or SeoEntityKind.Brand)
        {
            var subject = s.Kind == SeoEntityKind.Product ? name : s.Kind == SeoEntityKind.Brand ? "محصولات " + s.Name : s.Name;
            if (store.DeliveryMaxDays > 0)
                items.Add(new($"ارسال {subject} چقدر طول می‌کشد؟",
                    $"پس از آماده‌سازی و ارسال سفارش، مرسوله معمولاً ظرف {PersianText.Digits(store.DeliveryMinDays)} تا {PersianText.Digits(store.DeliveryMaxDays)} روز کاری به دست شما می‌رسد و کد رهگیری آن در حساب کاربری نمایش داده می‌شود."));
            if (store.ReturnEnabled && store.ReturnDays > 0)
                items.Add(new($"امکان مرجوع کردن {subject} وجود دارد؟",
                    $"بله، تا {PersianText.Digits(store.ReturnDays)} روز پس از تحویل می‌توانید از بخش سفارش‌های حساب کاربری درخواست مرجوعی ثبت کنید."));
            if (store.CashOnDelivery)
                items.Add(new($"پرداخت در محل برای {subject} امکان‌پذیر است؟",
                    "اگر سفارش شما شرایط پرداخت در محل را داشته باشد، این گزینه هنگام تکمیل خرید نمایش داده می‌شود."));
            else if (store.FreeShippingThreshold > 0)
                items.Add(new($"ارسال {subject} رایگان است؟",
                    $"برای سفارش‌هایی که مبلغشان از {PersianText.Money(store.FreeShippingThreshold)} تومان بیشتر باشد، ارسال رایگان است."));
        }

        items = items.Take(6).ToList();
        return items.Count >= 2 ? items : [];
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

    /// <summary>انتخاب یکی از چند جملهٔ هم‌معنی: روان‌ترین طبق مدل زبانی، با تنوع بین نسخه‌ها.</summary>
    private static string Pick(WriterContext ctx, params string[] options) =>
        options.Select(o => (Text: o, Score: ctx.Fluency(o) + ctx.Noise()))
            .OrderByDescending(x => x.Score)
            .First().Text;

    /// <summary>جمله‌های شرایط خرید از تنظیمات فروشگاه.</summary>
    private static List<string> StoreSentences(WriterContext ctx)
    {
        var store = ctx.Store;
        var list = new List<string>();
        if (store.DeliveryMaxDays > 0)
            list.Add($"سفارش پس از ارسال معمولاً ظرف {PersianText.Digits(store.DeliveryMinDays)} تا {PersianText.Digits(store.DeliveryMaxDays)} روز کاری به دستتان می‌رسد و وضعیت مرسوله را می‌توانید در حساب کاربری پیگیری کنید.");
        if (store.ReturnEnabled && store.ReturnDays > 0)
            list.Add($"تا {PersianText.Digits(store.ReturnDays)} روز پس از تحویل هم امکان ثبت درخواست مرجوعی وجود دارد.");
        if (store.FreeShippingThreshold > 0)
            list.Add($"ارسال سفارش‌های بالای {PersianText.Money(store.FreeShippingThreshold)} تومان رایگان است.");
        if (store.CashOnDelivery)
            list.Add("در صورت فراهم بودن شرایط، پرداخت در محل هم ممکن است.");
        return list;
    }

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
        new("<a href=\"[^\"<>]*\">[^<]*</a>", System.Text.RegularExpressions.RegexOptions.Compiled);

    private static string H2(string text) => "<h2>" + WebUtility.HtmlEncode(text) + "</h2>";

    private static string A(string path, string text) =>
        $"<a href=\"{WebUtility.HtmlEncode(path)}\">{WebUtility.HtmlEncode(text)}</a>";
}
