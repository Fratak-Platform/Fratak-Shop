using System.Net;
using System.Text;
using Fratak_Shop.Data.Entities.SeoEngine;

namespace Fratak_Shop.Core.Services.Seo.Engine;

// ═══════════════════════════════════════════════════════════════════════════
//  «توضیحات محصول» حرفه‌ای با HTML ساختاریافته (دور بیستم؛ دور ۲۱: از «نقد و بررسی» به «توضیحات فروشگاهی»)
//  چرا؟ خریدار در فروشگاه «توضیحات محصول» می‌خواند، نه مقالهٔ نقد. ساختار مثل فروشگاه‌های حرفه‌ای:
//   • معرفی کوتاه و فروشنده + کادر «ویژگی‌های کلیدی» بالای متن (خریدار اول نکته‌های مهم را می‌بیند)؛
//   • H2 «معرفی» و برای هر موضوع یک H3 (ترتیب موضوع‌ها از تحقیق در سایت‌های دیگر)؛
//   • مناسب چه کسانی است، اقلام همراه، نکته‌های خرید و نگه‌داری و «خرید از فروشگاه»؛
//   • عبارت‌های مهم با <strong> و هشدارهای منصفانه (مثل «طبق اعلام سازنده») با <em>.
//  تحقیق: متن سایت‌های دیگر فقط «خوانده» می‌شود (Core/Services/Smart/ProductResearch.cs) و هرگز کپی نمی‌شود.
//  اصل‌ها: فقط از داده‌های واقعی (مشخصات، آمار هم‌دسته‌ها در خود فروشگاه، نظرهای تأییدشده) – هیچ ادعای ساختگی؛
//  عبارت ممنوع (اصل، بهترین، تضمینی …) نمی‌آید؛ تگ <u> عمداً استفاده نمی‌شود (اثر سئویی ندارد و با لینک اشتباه گرفته می‌شود).
//  پاراگراف اول با data-fs="review" علامت می‌خورد تا سیستم بداند این متن را «دستیار هوشمند» نوشته است.
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// یک ویژگی برجسته که چند منبع دربارهٔ کالا گفته‌اند. Label برچسب استاندارد خود ما است. Sources = تعداد منبع موافق.
/// </summary>
public sealed record ResearchHighlight(string Label, SpecTopic Topic, int Sources);

/// <summary>
/// داده‌های کمکی «توضیحات محصول»: نکته‌های تأییدشدهٔ خریداران + نتیجهٔ تحقیق در سایت‌های دیگر (دور ۲۱). همه متن انسانی/تأییدشده‌اند و بدون تغییر (فقط encode) نمایش داده می‌شوند.
/// مثال: Pros = [«صدای بم قوی»]، Cons = [«قاب شارژ کمی بزرگ است»]، Verdict = «برای استفادهٔ روزمره ارزش خرید دارد».
/// </summary>
public sealed class ReviewNotes
{
    /// <summary>
    /// دور ۲۱: نتیجهٔ «تحقیق» دربارهٔ همین کالا در سایت‌های دیگر (<see cref="Fratak_Shop.Core.Services.Smart.ProductResearch"/>). متن هیچ سایتی کپی نمی‌شود؛
    /// فقط این‌ها استفاده می‌شوند: ترتیب اهمیت موضوع‌ها، اقلام همراه، کاربردها و ویژگی‌هایی که چند منبع روی آن توافق دارند.
    /// </summary>
    public IReadOnlyList<SpecTopic> TopicOrder { get; init; } = [];

    /// <summary>اقلام داخل جعبه (مورد توافق ≥۲ منبع یا یک منبع معتبر). مثال: [«کابل شارژ USB-C»، «دفترچهٔ راهنما»].</summary>
    public IReadOnlyList<string> InBox { get; init; } = [];

    /// <summary>کاربردهای مورد توافق. مثال: [«ورزش»، «مکالمه‌های کاری»].</summary>
    public IReadOnlyList<string> UseCases { get; init; } = [];

    /// <summary>ویژگی‌هایی که چند منبع گفته‌اند (برچسب استاندارد ما، نه متن آن‌ها). مثال: («اتصال هم‌زمان به دو دستگاه»، Bluetooth).</summary>
    public IReadOnlyList<ResearchHighlight> Highlights { get; init; } = [];

    /// <summary>تعداد منبع‌هایی که متنشان تحلیل شد (برای گزارش به کارشناس).</summary>
    public int ResearchSources { get; init; }

    public IReadOnlyList<string> Pros { get; init; } = [];
    public IReadOnlyList<string> Cons { get; init; } = [];
    public IReadOnlyList<string> ForWhom { get; init; } = [];
    public string? Verdict { get; init; }
    public int ReviewCount { get; init; }
}

public static partial class ContentWriter
{
    /// <summary>
    /// مقدار ویژگی data-fs پاراگراف اول «توضیحات محصول» هوشمند. مثال خروجی: <c>&lt;p data-fs="review"&gt;…&lt;/p&gt;</c>.
    /// محل استفاده: تشخیص «این متن را دستیار نوشته» در SmartCatalogService.GetBodyStateAsync.
    /// </summary>
    public const string ReviewMarker = "review";

    /// <summary>عبارتی که اگر در HTML باشد یعنی بخشی از متن را «توضیحات محصول» هوشمند نوشته است.</summary>
    public const string ReviewMarkerAttribute = "data-fs=\"review\"";

    // نشانه‌های داخلی برای strong/em (نویسه‌های «استفادهٔ خصوصی» یونیکد که در متن واقعی نمی‌آیند).
    // جمله‌ها اول ساده ساخته می‌شوند، بعد این نشانه‌ها دور عبارت مهم گذاشته و هنگام ساخت HTML به تگ تبدیل می‌شوند.
    private const char StrongOpen = '\uE000';
    private const char StrongClose = '\uE001';
    private const char EmOpen = '\uE002';
    private const char EmClose = '\uE003';

    /// <summary>عبارت‌های «احتیاط منصفانه» که در متن ایتالیک (em) می‌شوند.</summary>
    private static readonly string[] CaveatPhrases =
    [
        "طبق اعلام سازنده", "بر اساس مشخصات سازنده", "طبق مشخصات سازنده", "به شیوهٔ استفاده بستگی دارد", "به نوع استفاده بستگی دارد",
    ];

    /// <summary>
    /// «توضیحات محصول» کامل یک محصول به‌صورت HTML ساختاریافته.
    /// <para>keepHtml: متن فعلی صفحه که باید دست‌نخورده بالای متن بماند (برای صفحهٔ کم‌محتوا)؛ null/خالی = فقط بررسی تازه.</para>
    /// <para>null = نوشتن مجاز نیست: صفحه محصول نیست یا کمتر از ۲ مشخصهٔ قابل استفاده دارد (متن بدون داده = محتوای کم‌ارزش).</para>
    /// <para>مثال: هدفون با «شارژدهی 30 ساعت»، «نویز کنسلینگ: دارد» و «IPX5» ← خلاصهٔ بررسی، H2 مشخصات با H3 «باتری و شارژدهی»،
    /// «حذف نویز» و «مقاومت در برابر آب»، جدول، نقاط قوت/پیش از خرید بدانید، مناسب چه کسانی است و جمع‌بندی و خرید.</para>
    /// <para>محل استفاده: SeoEngineService.WriteProductReviewAsync (دستیار هوشمند ← تکمیل محصول).</para>
    /// </summary>
    public static string? ProductReview(WriterContext ctx, string? keepHtml = null)
    {
        if (ctx.Subject.Kind != SeoEntityKind.Product || ctx.Facts.Count < 2)
            return null;
        var existing = keepHtml?.Trim() ?? "";

        // مثل Body: تا ۵ بار با دانهٔ متفاوت؛ نسخه‌ای با تراکم سالم و دورترین فاصله از متن صفحه‌های دیگر انتخاب می‌شود.
        string? best = null;
        var bestDistance = -1;
        var bestHealthy = false;
        for (var salt = 0; salt < 5; salt++)
        {
            var composer = new Composer(ctx, 100 + salt);
            var sections = ReviewSections(composer);
            if (sections.Count == 0)
                return null;
            var builder = new StringBuilder();
            if (existing.Length > 0)
                builder.Append(existing).Append('\n');
            foreach (var section in sections)
                builder.Append(section).Append('\n');
            var html = builder.ToString().Trim();
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
    /// بخش انسانی بالای «توضیحات محصول» هوشمند: هر چه پیش از پاراگراف نشانه‌دار آمده (برای بازنویسی بدون از دست رفتن متن کارشناس).
    /// مثال: «&lt;p&gt;متن کارشناس&lt;/p&gt;\n&lt;p data-fs="review"&gt;…» ← «&lt;p&gt;متن کارشناس&lt;/p&gt;»؛ بدون نشانه ← کل متن؛ نشانه در ابتدا ← null.
    /// </summary>
    public static string? HumanPart(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return null;
        var marker = html.IndexOf(ReviewMarkerAttribute, StringComparison.Ordinal);
        if (marker < 0)
            return html.Trim();
        var start = html.LastIndexOf("<p", marker, StringComparison.OrdinalIgnoreCase);
        var human = html[..(start < 0 ? marker : start)].Trim();
        return human.Length == 0 ? null : human;
    }

    /// <summary>
    /// بخش‌های HTML «توضیحات محصول» (دور ۲۱) به ترتیب نمایش؛ ساختاری که فروشگاه‌های حرفه‌ای می‌نویسند:
    /// <list type="number">
    /// <item>معرفی کوتاه و فروشنده (پاراگراف نشانه‌دار؛ کلمهٔ کلیدی پررنگ).</item>
    /// <item>کادر «ویژگی‌های کلیدی» (چند نکتهٔ فایده‌محور برای خریدار عجول).</item>
    /// <item>H2 «معرفی/ویژگی‌ها» با یک H3 برای هر موضوع (ترتیب موضوع‌ها از تحقیق: هر چه سایت‌های دیگر بیشتر دربارهٔ آن نوشته‌اند جلوتر).</item>
    /// <item>جایگاه میان مدل‌های مشابه همین فروشگاه (فقط اختلاف معنادار).</item>
    /// <item>مناسب چه کسانی است (از مشخصات + کاربردهای مورد توافق چند منبع).</item>
    /// <item>اقلام همراه (فقط اگر چند منبع یا منبع معتبر گفته باشند).</item>
    /// <item>نکته‌های خرید و استفاده (منصفانه + نگه‌داری).</item>
    /// <item>خرید از فروشگاه (شرایط واقعی فروشگاه + لینک داخلی).</item>
    /// </list>
    /// <para>جدول مشخصات عمداً تکرار نمی‌شود: صفحهٔ محصول زبانهٔ جدا «مشخصات محصول» دارد.</para>
    /// </summary>
    private static List<string> ReviewSections(Composer c)
    {
        var ctx = c.Ctx;
        var p = ctx.Subject.Product;
        var facts = ctx.Distinctive;
        var notes = ctx.Notes;
        var sections = new List<string>();
        var uses = (notes?.UseCases ?? []).Select(Note).Where(n => n.Length is > 2 and <= 60).Distinct(StringComparer.Ordinal).Take(4).ToList();

        // ۱) معرفی فروشنده (نه «در این بررسی …»). همیشه اولین بلوک متنی تا «کلمه در مقدمه» برقرار باشد.
        var intro = new List<string>();
        if (p?.BrandName is not null && p.CategoryName is not null)
            intro.Add(Said(c,
                "{L} [محصولی|مدلی] از برند {B} در دستهٔ {C} است که [برای استفادهٔ روزمره|برای کاربرد روزانه] [طراحی شده|ساخته شده] و [امکانات کاربردی|ویژگی‌های کاربردی] را [در اختیار شما می‌گذارد|یک‌جا ارائه می‌دهد].",
                "[اگر دنبال|اگر به دنبال] [محصولی|انتخابی] [مطمئن|کاربردی] در دستهٔ {C} هستید، {L} از برند {B} [گزینه‌ای است که ارزش آشنایی دارد|انتخابی است که ارزش بررسی دارد].",
                "{L} [عضوی از|بخشی از] [محصولات|خانوادهٔ] {C} برند {B} است؛ [محصولی|مدلی] که روی [نیازهای واقعی|کاربرد روزمرهٔ] [کاربر|خریدار] تمرکز دارد.",
                "برند {B} در {L} [ترکیبی از|مجموعه‌ای از] [ویژگی‌های کاربردی|امکانات مهم] را برای [علاقه‌مندان|خریداران] {C} [فراهم کرده است|کنار هم گذاشته است]."));
        else if (p?.CategoryName is not null)
            intro.Add(Said(c,
                "{L} [محصولی|مدلی] در دستهٔ {C} است که [برای استفادهٔ روزمره|برای کاربرد روزانه] [طراحی شده|ساخته شده] و [امکانات کاربردی|ویژگی‌های کاربردی] را [در اختیار شما می‌گذارد|یک‌جا ارائه می‌دهد].",
                "[اگر دنبال|اگر به دنبال] [محصولی|انتخابی] [مطمئن|کاربردی] در دستهٔ {C} هستید، {L} [گزینه‌ای است که ارزش آشنایی دارد|انتخابی است که ارزش بررسی دارد]."));
        else
            intro.Add(Said(c,
                "{L} [محصولی|مدلی] است که [برای استفادهٔ روزمره|برای کاربرد روزانه] [طراحی شده|ساخته شده] و [امکانات کاربردی|ویژگی‌های کاربردی] را [در اختیار شما می‌گذارد|یک‌جا ارائه می‌دهد].",
                "{L} [گزینه‌ای است که|انتخابی است که] [ارزش آشنایی بیشتر دارد|ارزش بررسی دارد]."));
        intro[0] = Mark(intro[0], ctx.Lead, StrongOpen, StrongClose);

        var top = facts.Take(3).Select(FactPhrase).ToList();
        if (top.Count >= 2)
        {
            var joined = PersianText.JoinList(top);
            intro.Add(Said(c,
                $"[مهم‌ترین|برجسته‌ترین] ویژگی‌های {{N}} {joined} است.",
                $"{joined} از [نکات|ویژگی‌های] [اصلی|قابل توجه] {{N}} [به شمار می‌رود|است].",
                $"{{N}} با {joined} [پاسخ خوبی به نیاز روزمره می‌دهد|برای استفادهٔ روزمره آماده است]."));
        }
        if (uses.Count > 0)
            intro.Add(Said(c,
                $"این [محصول|مدل] [به‌ویژه|بیشتر] برای {PersianText.JoinList(uses)} [مناسب است|کاربرد دارد].",
                $"[کاربرد اصلی|بیشترین کاربرد] {{N}} [در|برای] {PersianText.JoinList(uses)} است."));
        sections.Add("<p " + ReviewMarkerAttribute + ">" + RichInline(string.Join(' ', intro)) + "</p>");

        // ۲) ویژگی‌های کلیدی (خریدار عجول در یک نگاه می‌بیند چه می‌خرد).
        var key = KeyFeatures(c);
        if (key.Count >= 2)
            sections.Add("<div class=\"fs-review-box\"><p class=\"fs-review-box-title\"><strong>ویژگی‌های کلیدی</strong></p>" + BulletList(key) + "</div>");

        // ۳) معرفی ویژگی‌ها: H2 + H3 برای هر موضوع. ترتیب = اول موضوع‌هایی که سایت‌های دیگر بیشتر درباره‌اش نوشته‌اند.
        sections.Add(H2(c.Say(
            "معرفی {S}", "آشنایی با ویژگی‌های {S}", "{S} چه ویژگی‌هایی دارد؟", "ویژگی‌ها و امکانات {S}", "نگاهی به امکانات {S}")));
        var order = notes?.TopicOrder ?? [];
        int Rank(SpecTopic topic)
        {
            for (var i = 0; i < order.Count; i++)
                if (order[i] == topic)
                    return i;
            return order.Count + (int)topic;
        }
        var groups = facts.Where(f => TopicTitle(f.Topic) is not null)
            .GroupBy(f => TopicTitle(f.Topic)!, StringComparer.Ordinal)
            .OrderBy(g => g.Min(f => Rank(f.Topic)))
            .Take(5)
            .ToList();
        if (groups.Count >= 2)
        {
            foreach (var group in groups)
            {
                sections.Add(H3(c.Say(group.Key, group.Key + " {N}")));
                var sentences = group.Take(3).Select(f => Explained(c, f)).ToList();
                sections.Add(Paragraph(sentences));
            }
            var rest = facts.Where(f => TopicTitle(f.Topic) is null).Take(3).Select(f => Explained(c, f)).ToList();
            if (rest.Count >= 2)
            {
                sections.Add(H3(c.Say("سایر ویژگی‌ها", "امکانات دیگر {N}", "ویژگی‌های [تکمیلی|دیگر]")));
                sections.Add(Paragraph(rest));
            }
        }
        else
        {
            var sentences = facts.OrderBy(f => f.Topic == SpecTopic.Generic ? 1 : 0).Take(6)
                .Select(f => Explained(c, f)).ToList();
            for (var i = 0; i < sentences.Count; i += 3)
                sections.Add(Paragraph(sentences.Skip(i).Take(3).ToList()));
        }

        // ۴) جایگاه میان مدل‌های مشابه همین فروشگاه (فقط اختلاف معنادار).
        var compare = CompareSentences(c);
        if (compare.Count > 0)
        {
            sections.Add(H2(c.Say("{S} در کنار مدل‌های مشابه", "جایگاه {S} میان [مدل‌های|محصولات] {C}", "{S} [در برابر|در کنار] [هم‌رده‌ها|مدل‌های هم‌رده]")));
            sections.Add(Paragraph(compare));
        }

        // ۵) مناسب چه کسانی است (از مشخصات + کاربردهای مورد توافق چند منبع + «مناسب برای» تأییدشدهٔ خلاصهٔ خرید).
        var audience = facts.Select(SpecKnowledge.Audience).OfType<string>()
            .Concat(uses.Select(u => "مناسب برای " + u))
            .Concat((notes?.ForWhom ?? []).Select(Note).Where(n => n.Length > 0))
            .Distinct(StringComparer.Ordinal).Take(5).ToList();
        if (audience.Count > 0)
        {
            sections.Add(H2(c.Say("{S} مناسب چه کسانی است؟", "این {T} برای چه کسانی مناسب است؟", "[چه کسانی|چه کاربرانی] از {S} [بیشتر لذت می‌برند|بیشترین بهره را می‌برند]؟")));
            sections.Add("<p>" + RichInline(Said(c,
                "{N} [به‌ویژه|بیشتر] برای این [گروه‌ها|افراد] [مناسب است|پیشنهاد می‌شود]:",
                "[با توجه به|بر اساس] مشخصات، {N} [برای|به کار] این [افراد|گروه‌ها] [می‌آید|مناسب است]:")) + "</p>");
            sections.Add(BulletList(audience.Select(a => a.TrimEnd('.') + ".").ToList()));
        }

        // ۶) اقلام همراه (فقط از تحقیق مورد توافق؛ با احتیاط منصفانه).
        var inBox = (notes?.InBox ?? []).Select(Note).Where(n => n.Length is > 1 and <= 60).Distinct(StringComparer.Ordinal).Take(8).ToList();
        if (inBox.Count >= 2)
        {
            sections.Add(H3(c.Say("اقلام همراه {N}", "[داخل جعبهٔ|محتویات بستهٔ] {N} چه چیزهایی هست؟", "محتویات جعبه")));
            sections.Add("<p>" + RichInline(Said(c,
                "[طبق اعلام فروشندگان|بر اساس اطلاعات فروشندگان]، این اقلام همراه {N} [ارائه می‌شود|در جعبه قرار دارد]:",
                "[در جعبهٔ|داخل بستهٔ] {N} [معمولاً|] این [موارد|اقلام] [قرار دارد|دیده می‌شود] (طبق اعلام فروشندگان):")) + "</p>");
            sections.Add("<ul class=\"fs-inbox\">" + string.Concat(inBox.Select(i => "<li>" + WebUtility.HtmlEncode(i) + "</li>")) + "</ul>");
        }

        // ۷) نکته‌های خرید و استفاده (منصفانه، از داده + نکتهٔ نگه‌داری هر موضوع).
        var tips = Cautions(c);
        sections.Add(H2(c.Say("نکته‌های خرید و استفادهٔ {S}", "پیش از خرید {S} بدانید", "[نکته‌های مهم|توصیه‌های] [خرید و نگه‌داری|استفاده و نگه‌داری] {N}")));
        sections.Add("<div class=\"fs-tips\">" + BulletList(tips) + "</div>");

        // ۸) خرید (آخرین H2؛ کلمهٔ کلیدی در تیتر) + شرایط فروشگاه + لینک داخلی.
        var closing = new List<string>();
        if (notes?.Verdict is { } verdict && Note(verdict).Length >= 15)
            closing.Add(EmOpen + "جمع‌بندی خریداران:" + EmClose + " " + Note(verdict).TrimEnd('.', '!', '؟') + ".");
        else
        {
            var two = facts.Take(2).Select(FactPhrase).ToList();
            var forWhom = uses.FirstOrDefault() ?? audience.FirstOrDefault() ?? "استفادهٔ روزمره";
            if (forWhom.StartsWith("مناسب برای ", StringComparison.Ordinal))
                forWhom = forWhom["مناسب برای ".Length..];
            closing.Add(Said(c,
                $"[در مجموع|به‌طور خلاصه]، {{N}} با {PersianText.JoinList(two)} [گزینه‌ای منطقی|انتخابی سنجیده] برای {forWhom.TrimEnd('.')} است.",
                $"[اگر|چنانچه] {PersianText.JoinList(two)} برایتان مهم است، {{N}} [ارزش بررسی دارد|گزینهٔ مناسبی است]؛ به‌ویژه برای {forWhom.TrimEnd('.')}."));
        }
        closing.Add(Said(c,
            "برای خرید {L} [کافی است|فقط کافی است] آن را به سبد خرید اضافه کنید و سفارش را آنلاین ثبت کنید.",
            "خرید {L} از {SITE} [به‌صورت آنلاین و|] در چند مرحلهٔ ساده انجام می‌شود.",
            "{L} را می‌توانید [همین حالا|] [به سبد خرید اضافه کنید|آنلاین سفارش دهید] و [روند|مراحل] [سفارش|ارسال] را از حساب کاربری [دنبال کنید|پیگیری کنید]."));
        closing.AddRange(StoreSentences(c, 2).Select(Strip));
        var links = new List<string>();
        if (p?.CategorySlug is { } categorySlug && p.CategoryName is { } categoryName)
            links.Add(A("/category/" + categorySlug, categoryName));
        if (p?.BrandSlug is { } brandSlug && p.BrandName is { } brandName)
            links.Add(A("/brand/" + brandSlug, "محصولات " + brandName));
        if (links.Count > 0)
            closing.Add(c.Say(
                [
                    "برای دیدن [مدل‌های مشابه|گزینه‌های دیگر]، [صفحهٔ|به صفحهٔ] {LINKS} [را ببینید|سر بزنید].",
                    "[اگر می‌خواهید مقایسه کنید|برای مقایسه]، [سایر محصولات|مدل‌های دیگر] {LINKS} را هم ببینید.",
                ],
                new Dictionary<string, string> { ["{LINKS}"] = PersianText.JoinList(links) }));
        sections.Add(H2(c.Say("خرید {L}", "خرید {L} از {SITE}", "[سفارش|خرید] آنلاین {L}", "چرا {L} را از {SITE} بخریم؟")));
        // پاراگراف‌های ≤ ۳ جمله (خوانایی و قانون طول پاراگراف).
        for (var i = 0; i < closing.Count; i += 3)
            sections.Add(Paragraph(closing.Skip(i).Take(3).ToList()));
        return sections;
    }

    /// <summary>
    /// «ویژگی‌های کلیدی» (دور ۲۱): نکته‌های فایده‌محور از مشخصات متمایز (+ برتری معنادار نسبت به میانگین هم‌دسته‌ها)،
    /// ویژگی‌های مورد توافق چند منبع که در مشخصات ما نیامده‌اند، گارانتی و نکته‌های تأییدشدهٔ خریداران. حداکثر ۷ مورد.
    /// <para>مثال: «⟦حذف نویز فعال⟧؛ برای تمرکز در محیط‌های شلوغ.» ، «⟦اتصال هم‌زمان به دو دستگاه⟧ (به گفتهٔ چند فروشنده).»</para>
    /// </summary>
    private static List<string> KeyFeatures(Composer c)
    {
        var ctx = c.Ctx;
        var list = new List<string>();
        foreach (var fact in ctx.Distinctive)
        {
            if (list.Count >= 4)
                break;
            var phrase = SpecKnowledge.Highlight(fact) ?? FactPhrase(fact);
            var better = BetterThanAverage(ctx, fact);
            list.Add(StrongOpen + phrase + StrongClose + (better ? "؛ بالاتر از میانگین هم‌رده‌ها در همین فروشگاه." : Why(fact) is { } why ? "؛ " + why + "." : "."));
        }
        var covered = ctx.Facts.Select(f => f.Topic).Where(t => t != SpecTopic.Generic).ToHashSet();
        foreach (var highlight in ctx.Notes?.Highlights ?? [])
        {
            if (list.Count >= 5)
                break;
            if (highlight.Topic != SpecTopic.Generic && covered.Contains(highlight.Topic))
                continue; // مشخصات خود ما دقیق‌تر است؛ ادعای منابع دیگر تکرار نمی‌شود
            var label = Note(highlight.Label);
            if (label.Length > 0)
                list.Add(StrongOpen + label + StrongClose + (c.Next(2) == 0 ? " (طبق اعلام فروشندگان)." : " (به گفتهٔ چند فروشنده)."));
        }
        if (WarrantyPhrase(ctx.Subject.Product) is { } warrantyItem && list.Count < 6)
            list.Add(StrongOpen + Strip(SpecKnowledge.Safe(warrantyItem)) + StrongClose + (c.Next(2) == 0 ? "؛ برای پشتیبانی پس از خرید." : "؛ همراه با خدمات پس از خرید."));
        // نظر خریداران همیشه دست‌کم یک جا دارد (سقف کل ۷ مورد).
        foreach (var pro in (ctx.Notes?.Pros ?? []).Select(Note).Where(n => n.Length > 0).Take(2))
        {
            if (list.Count >= 7)
                break;
            list.Add(EmOpen + "از نظر خریداران:" + EmClose + " " + pro.TrimEnd('.') + ".");
        }
        return list;
    }

    /// <summary>
    /// «پیش از خرید بدانید»: نکته‌های منصفانه و واقعی (نه عیب ساختگی). مثال‌ها: شارژدهی ادعای سازنده است؛ IPX4 برای شنا نیست؛
    /// مقدار کمتر از میانگین هم‌دسته‌ها؛ گارانتی ثبت نشده؛ ضعف‌های تأییدشدهٔ خریداران. همیشه حداقل یک مورد دارد.
    /// </summary>
    private static List<string> Cautions(Composer c)
    {
        var ctx = c.Ctx;
        var list = new List<string>();
        foreach (var con in (ctx.Notes?.Cons ?? []).Select(Note).Where(n => n.Length > 0).Take(3))
            list.Add(EmOpen + "از نظر خریداران:" + EmClose + " " + con.TrimEnd('.') + ".");
        foreach (var fact in ctx.Facts)
        {
            if (list.Count >= 5)
                break;
            switch (fact.Topic)
            {
                case SpecTopic.Battery when !fact.IsYes && fact.Value.Contains("ساعت", StringComparison.Ordinal):
                    list.Add($"عدد {StrongOpen}{fact.Value}{StrongClose} {EmOpen}طبق اعلام سازنده{EmClose} است و با صدای بلندتر یا روشن بودن قابلیت‌های جانبی کمتر می‌شود.");
                    break;
                case SpecTopic.Water when SpecKnowledge.IpMeaning(fact.Value + " " + fact.Key) is { } ip && ip.WaterLevel is > 0 and < 7:
                    var splash = c.Next(2) == 0 ? "تعریق و پاشش آب" : "قطره و پاشش آب";
                    var swim = c.Next(2) == 0 ? "شنا" : "غوطه‌وری در آب";
                    list.Add($"استاندارد {StrongOpen}{ip.Code}{StrongClose} برای {splash} است، نه {swim}.");
                    break;
                case SpecTopic.NoiseCancel when SpecKnowledge.IsPassive(fact.Value):
                    list.Add($"حذف نویز این مدل {StrongOpen}غیرفعال{StrongClose} است؛ یعنی صدای محیط فقط با طراحی فیزیکی کم می‌شود.");
                    break;
                default:
                    if (WorseThanAverage(ctx, fact) is { } average)
                        list.Add($"{fact.Key} ({StrongOpen}{fact.Value}{StrongClose}) از میانگین هم‌رده‌ها در {Strip(SpecKnowledge.Safe(ctx.Store.SiteName))} (حدود {average}) {(fact.Direction < 0 ? "بیشتر" : "کمتر")} است.");
                    break;
            }
        }
        // نکتهٔ نگه‌داری هر موضوع (دانش عمومی درست، نه ادعا دربارهٔ این مدل). حداکثر دو مورد تا فهرست شلوغ نشود.
        foreach (var care in ctx.Facts.Select(f => CareTip(c, f)).OfType<string>().Distinct(StringComparer.Ordinal).Take(2))
        {
            if (list.Count >= 5)
                break;
            list.Add(care);
        }
        if (WarrantyPhrase(ctx.Subject.Product) is null && list.Count < 5)
            list.Add("در مشخصات این کالا گارانتی ثبت نشده است؛ اگر برایتان مهم است، پیش از خرید از پشتیبانی بپرسید.");
        if (list.Count == 0)
            list.Add(Said(c,
                "[پیش از خرید|قبل از ثبت سفارش]، [جدول مشخصات|مشخصات کامل] را با نیاز خود مقایسه کنید.",
                "[برای انتخاب درست|برای خرید آگاهانه]، سازگاری {N} با [دستگاه‌ها|وسایل] و نیازتان را در [جدول مشخصات|مشخصات کامل] بررسی کنید."));
        return list;
    }

    /// <summary>
    /// نکتهٔ نگه‌داری/استفادهٔ درست برای یک موضوع مشخصات (دور ۲۱). فقط توصیهٔ عمومی و درست؛ هیچ ادعایی دربارهٔ خود کالا نمی‌سازد.
    /// مثال: باتری ← «برای عمر بیشتر باتری، با شارژر استاندارد شارژ کنید و دستگاه را در گرمای زیاد شارژ نکنید.»؛ موضوع ناشناخته ← null.
    /// </summary>
    private static string? CareTip(Composer c, SpecFact f) => f.Topic switch
    {
        SpecTopic.Battery => c.Next(2) == 0
            ? "برای عمر بیشتر باتری، با شارژر و کابل استاندارد شارژ کنید و دستگاه را در گرمای زیاد شارژ نکنید."
            : "اگر مدتی از دستگاه استفاده نمی‌کنید، باتری را نیمه‌شارژ نگه دارید تا عمر آن کوتاه نشود.",
        SpecTopic.Water =>
            "پس از تماس با آب یا عرق، دستگاه را با پارچهٔ نرم خشک کنید و درگاه شارژ را تا خشک شدن کامل به برق نزنید.",
        SpecTopic.ScreenSize or SpecTopic.Resolution or SpecTopic.Refresh =>
            "برای محافظت از صفحه‌نمایش در برابر خط و ضربه، استفاده از محافظ صفحه و کاور توصیه می‌شود.",
        SpecTopic.Material when f.Value.Contains("چرم", StringComparison.Ordinal) =>
            "سطح چرمی را دور از رطوبت و نور مستقیم نگه دارید و با دستمال خشک تمیز کنید.",
        _ => null,
    };

    /// <summary>آیا عدد این مشخصه به‌طور معنادار (≥۱۵٪) از میانگین هم‌دسته‌ها بهتر است؟</summary>
    private static bool BetterThanAverage(WriterContext ctx, SpecFact fact)
    {
        var profile = ctx.Profile;
        if (profile is null || profile.Count < 4 || fact.Direction == 0 || fact.Number is not > 0)
            return false;
        if (profile.Average(ctx.Subject.EntityId, fact.Key) is not { Average: > 0 } average)
            return false;
        var ratio = fact.Number.Value / average.Average;
        return fact.Direction > 0 ? ratio >= 1.15 : ratio <= 0.85;
    }

    /// <summary>اگر عدد این مشخصه به‌طور معنادار از میانگین هم‌دسته‌ها بدتر باشد، متن میانگین (مثلاً «30 ساعت»)؛ وگرنه null.</summary>
    private static string? WorseThanAverage(WriterContext ctx, SpecFact fact)
    {
        var profile = ctx.Profile;
        if (profile is null || profile.Count < 4 || fact.Direction == 0 || fact.Number is not > 0)
            return null;
        if (profile.Average(ctx.Subject.EntityId, fact.Key) is not { Average: > 0 } average)
            return null;
        var ratio = fact.Number.Value / average.Average;
        var worse = fact.Direction > 0 ? ratio <= 0.85 : ratio >= 1.15;
        if (!worse)
            return null;
        var unit = Unit(fact.Value);
        return SpecKnowledge.Display(average.Average) + (unit.Length > 0 ? " " + unit : "");
    }

    /// <summary>
    /// عنوان H3 هر موضوع مشخصات. مثال: Battery → «باتری و شارژدهی». Generic → null (زیرتیتر جدا نمی‌گیرد).
    /// </summary>
    public static string? TopicTitle(SpecTopic topic) => topic switch
    {
        SpecTopic.Battery => "باتری و شارژدهی",
        SpecTopic.Bluetooth => "اتصال بی‌سیم",
        SpecTopic.NoiseCancel => "حذف نویز",
        SpecTopic.Water => "مقاومت در برابر آب و تعریق",
        SpecTopic.Weight => "وزن و راحتی",
        SpecTopic.Port or SpecTopic.WirelessCharge or SpecTopic.FastCharge => "شارژ و درگاه",
        SpecTopic.Storage or SpecTopic.Ram => "حافظه",
        SpecTopic.ScreenSize or SpecTopic.Refresh or SpecTopic.Resolution => "صفحه‌نمایش",
        SpecTopic.Camera => "دوربین",
        SpecTopic.Processor or SpecTopic.Os => "پردازنده و سیستم‌عامل",
        SpecTopic.Microphones => "میکروفون و مکالمه",
        SpecTopic.Power => "توان و مصرف",
        SpecTopic.Material or SpecTopic.Color => "طراحی و ساخت",
        SpecTopic.Compatibility => "سازگاری",
        _ => null,
    };

    /// <summary>کاربرد کوتاه هر مشخصه برای «خلاصهٔ بررسی» (فقط وقتی داده واقعاً این را نشان می‌دهد). مثال: حذف نویز → «برای تمرکز در محیط‌های شلوغ».</summary>
    private static string? Why(SpecFact f) => f.Topic switch
    {
        SpecTopic.Battery when SpecKnowledge.IsCapacity(f) => "برای استفادهٔ طولانی‌تر در طول روز",
        SpecTopic.Battery when f.Number is >= 20 => "برای روزهای طولانی بدون شارژ مکرر",
        SpecTopic.NoiseCancel when !SpecKnowledge.IsPassive(f.Value) => "برای تمرکز در محیط‌های شلوغ",
        SpecTopic.Water when SpecKnowledge.IpMeaning(f.Value + " " + f.Key) is { WaterLevel: >= 4 } => "مناسب ورزش و روزهای پرتعریق",
        SpecTopic.Water => "در برابر پاشش آب",
        SpecTopic.Bluetooth when f.Number is >= 5 => "برای اتصال پایدارتر به گوشی",
        SpecTopic.WirelessCharge when f.IsYes => "برای شارژ بدون کابل",
        SpecTopic.FastCharge => "برای شارژ سریع‌تر",
        SpecTopic.Port when SpecKnowledge.IsUsbC(f.Value) => "با کابل رایج USB-C",
        SpecTopic.Storage when f.Number is > 0 => "برای نگه‌داری فایل‌های بیشتر",
        SpecTopic.Ram when f.Number is > 0 => "برای اجرای هم‌زمان برنامه‌ها",
        SpecTopic.Refresh when f.Number is >= 90 => "برای تصویر روان‌تر",
        SpecTopic.Microphones when f.Number is >= 2 => "برای مکالمهٔ واضح‌تر",
        _ => null,
    };

    // ── ابزارهای ساخت HTML غنی ───────────────────────────────────────

    /// <summary>
    /// جملهٔ توضیح یک مشخصه (از SpecKnowledge.Explain) با مقدار پررنگ؛ برای «دارد/بله» نام ویژگی پررنگ می‌شود.
    /// مثال: «شارژدهی این هدفون ⟦30 ساعت⟧ است؛ …».
    /// </summary>
    private static string Explained(Composer c, SpecFact f) =>
        Mark(Said(c, SpecKnowledge.Explain(f)), f.IsYes ? f.Key : f.Value, StrongOpen, StrongClose);

    /// <summary>جملهٔ ساخته‌شده با نشانه‌های پاک (اگر دادهٔ ورودی نویسهٔ نشانه داشت حذف می‌شود) + ایتالیک کردن احتیاط‌ها.</summary>
    private static string Said(Composer c, params string[] templates) => Caveats(Strip(c.Say(templates)));

    private static string Said(Composer c, IEnumerable<string> templates) => Caveats(Strip(c.Say(templates)));

    /// <summary>حذف نویسه‌های نشانه از متن (دادهٔ بیرونی هرگز نمی‌تواند تگ بسازد).</summary>
    private static string Strip(string text) =>
        text.Replace(StrongOpen.ToString(), "", StringComparison.Ordinal).Replace(StrongClose.ToString(), "", StringComparison.Ordinal)
            .Replace(EmOpen.ToString(), "", StringComparison.Ordinal).Replace(EmClose.ToString(), "", StringComparison.Ordinal);

    /// <summary>
    /// متن تأییدشدهٔ خریداران: فقط متن (هر تگ HTML حذف می‌شود)، تمیز، بدون نشانه، بدون نویسه‌های الگو و حداکثر ۲۰۰ نویسه.
    /// مثال: «&lt;a href="javascript:…"&gt;لینک&lt;/a&gt; قاب بزرگ» ← «لینک قاب بزرگ».
    /// </summary>
    private static string Note(string? text) =>
        PersianText.Truncate(SpecKnowledge.Safe(Strip(PersianText.Clean(PersianText.HtmlToText(text)))), 200).Trim();

    /// <summary>اولین «عبارت احتیاط» جمله ایتالیک می‌شود. مثال: «… طبق اعلام سازنده …» → «… &lt;em&gt;طبق اعلام سازنده&lt;/em&gt; …».</summary>
    private static string Caveats(string text)
    {
        foreach (var phrase in CaveatPhrases)
            if (text.Contains(phrase, StringComparison.Ordinal))
                return Mark(text, phrase, EmOpen, EmClose);
        return text;
    }

    /// <summary>
    /// اولین رخداد عبارت (اگر بیرون از نشانهٔ دیگری باشد) با نشانهٔ باز/بسته پوشانده می‌شود. عبارت کوتاه‌تر از ۲ نویسه یا پیدا نشده ← بدون تغییر.
    /// مثال: Mark(«شارژدهی 30 ساعت است»، «30 ساعت»، strong) → «شارژدهی ⟦30 ساعت⟧ است».
    /// </summary>
    private static string Mark(string text, string phrase, char open, char close)
    {
        if (string.IsNullOrWhiteSpace(phrase) || phrase.Length < 2)
            return text;
        var index = text.IndexOf(phrase, StringComparison.Ordinal);
        if (index < 0)
            return text;
        var before = text[..index];
        var depth = before.Count(ch => ch is StrongOpen or EmOpen) - before.Count(ch => ch is StrongClose or EmClose);
        if (depth != 0)
            return text;
        return before + open + phrase + close + text[(index + phrase.Length)..];
    }

    /// <summary>
    /// متن با نشانه‌ها و لینک‌های ساختهٔ نویسنده ← HTML امن. همهٔ متن encode می‌شود؛ فقط نشانه‌ها به strong/em و لینک‌های
    /// <see cref="A"/> دست‌نخورده می‌مانند. تگ‌ها همیشه متوازن‌اند (تگ بازِ بسته‌نشده در پایان بسته می‌شود).
    /// </summary>
    private static string RichInline(string text)
    {
        var builder = new StringBuilder();
        var last = 0;
        foreach (System.Text.RegularExpressions.Match match in AnchorPattern.Matches(text))
        {
            AppendMarked(builder, text[last..match.Index]);
            builder.Append(match.Value);
            last = match.Index + match.Length;
        }
        AppendMarked(builder, text[last..]);
        return builder.ToString();

        static void AppendMarked(StringBuilder into, string part)
        {
            var strong = false;
            var em = false;
            var segment = new StringBuilder();
            foreach (var ch in part)
            {
                if (ch is StrongOpen or StrongClose or EmOpen or EmClose)
                {
                    into.Append(WebUtility.HtmlEncode(segment.ToString()));
                    segment.Clear();
                    switch (ch)
                    {
                        case StrongOpen when !strong: into.Append("<strong>"); strong = true; break;
                        case StrongClose when strong: into.Append("</strong>"); strong = false; break;
                        case EmOpen when !em: into.Append("<em>"); em = true; break;
                        case EmClose when em: into.Append("</em>"); em = false; break;
                    }
                    continue;
                }
                segment.Append(ch);
            }
            into.Append(WebUtility.HtmlEncode(segment.ToString()));
            if (em) into.Append("</em>");
            if (strong) into.Append("</strong>");
        }
    }

    private static string Paragraph(IReadOnlyList<string> sentences) => "<p>" + RichInline(string.Join(' ', sentences)) + "</p>";

    private static string BulletList(IReadOnlyList<string> items) =>
        "<ul>" + string.Concat(items.Select(i => "<li>" + RichInline(i) + "</li>")) + "</ul>";

    private static string H3(string text) => "<h3>" + WebUtility.HtmlEncode(Strip(text)) + "</h3>";
}
