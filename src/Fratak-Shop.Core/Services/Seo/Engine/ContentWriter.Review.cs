using System.Net;
using System.Text;
using Fratak_Shop.Data.Entities.SeoEngine;

namespace Fratak_Shop.Core.Services.Seo.Engine;

// ═══════════════════════════════════════════════════════════════════════════
//  «نقد و بررسی» حرفه‌ای محصول با HTML ساختاریافته (دور بیستم)
//  چرا؟ متن قبلی موتور (Body) فقط چند پاراگراف ساده بود. خریدار و گوگل هر دو متنی را بهتر می‌فهمند که:
//   • بالای صفحه «خلاصهٔ بررسی» داشته باشد (خریدار اول نکته‌های کلیدی را می‌بیند)؛
//   • تیتر H2 و زیرتیتر H3 داشته باشد (اسکن سریع + فهم موضوع برای موتور جست‌وجو)؛
//   • عبارت‌های مهم با <strong> و هشدارهای منصفانه (مثل «طبق اعلام سازنده») با <em> مشخص شوند؛
//   • فهرست نقاط قوت / «پیش از خرید بدانید»، جدول مشخصات کلیدی و جمع‌بندی داشته باشد.
//  اصل‌ها: فقط از داده‌های واقعی (مشخصات، آمار هم‌دسته‌ها در خود فروشگاه، نظرهای تأییدشده) – هیچ ادعای ساختگی؛
//  عبارت ممنوع (اصل، بهترین، تضمینی …) نمی‌آید؛ تگ <u> عمداً استفاده نمی‌شود (اثر سئویی ندارد و با لینک اشتباه گرفته می‌شود).
//  پاراگراف اول با data-fs="review" علامت می‌خورد تا سیستم بداند این متن را «دستیار هوشمند» نوشته است.
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// نکته‌های تأییدشدهٔ خریداران برای «نقد و بررسی». همه متن انسانی/تأییدشده‌اند و بدون تغییر (فقط encode) نمایش داده می‌شوند.
/// مثال: Pros = [«صدای بم قوی»]، Cons = [«قاب شارژ کمی بزرگ است»]، Verdict = «برای استفادهٔ روزمره ارزش خرید دارد».
/// </summary>
public sealed class ReviewNotes
{
    public IReadOnlyList<string> Pros { get; init; } = [];
    public IReadOnlyList<string> Cons { get; init; } = [];
    public IReadOnlyList<string> ForWhom { get; init; } = [];
    public string? Verdict { get; init; }
    public int ReviewCount { get; init; }
}

public static partial class ContentWriter
{
    /// <summary>
    /// مقدار ویژگی data-fs پاراگراف اول «نقد و بررسی» هوشمند. مثال خروجی: <c>&lt;p data-fs="review"&gt;…&lt;/p&gt;</c>.
    /// محل استفاده: تشخیص «این متن را دستیار نوشته» در SmartCatalogService.GetBodyStateAsync.
    /// </summary>
    public const string ReviewMarker = "review";

    /// <summary>عبارتی که اگر در HTML باشد یعنی بخشی از متن را «نقد و بررسی» هوشمند نوشته است.</summary>
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
    /// «نقد و بررسی» کامل یک محصول به‌صورت HTML ساختاریافته.
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
    /// بخش انسانی بالای «نقد و بررسی» هوشمند: هر چه پیش از پاراگراف نشانه‌دار آمده (برای بازنویسی بدون از دست رفتن متن کارشناس).
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

    /// <summary>بخش‌های HTML نقد و بررسی به ترتیب نمایش.</summary>
    private static List<string> ReviewSections(Composer c)
    {
        var ctx = c.Ctx;
        var p = ctx.Subject.Product;
        var facts = ctx.Distinctive;
        var notes = ctx.Notes;
        var sections = new List<string>();

        // ۱) معرفی (پاراگراف نشانه‌دار؛ کلمهٔ کلیدی پررنگ). همیشه اولین بلوک متنی تا «کلمه در مقدمه» برقرار باشد.
        var intro = new List<string>();
        if (p?.BrandName is not null && p.CategoryName is not null)
            intro.Add(Said(c,
                "در این [نقد و بررسی|بررسی]، {L} از برند {B} را از نظر [مشخصات فنی|ویژگی‌های فنی]، کاربرد روزمره و شرایط خرید [مرور می‌کنیم|بررسی می‌کنیم].",
                "{L} [مدلی|محصولی] از برند {B} در دستهٔ {C} است و [در این بررسی|اینجا] [نقاط قوت|ویژگی‌های مهم] و نکته‌های پیش از خرید آن را [می‌بینیم|مرور می‌کنیم].",
                "[اگر|وقتی] به خرید {L} فکر می‌کنید، این بررسی [مهم‌ترین مشخصات|مشخصات کلیدی] و کاربردهای این [مدل|محصول] از برند {B} را [یک‌جا|کنار هم] [نشان می‌دهد|جمع کرده است].",
                "{L} را [در این صفحه|اینجا] [با جزئیات|دقیق] بررسی کرده‌ایم تا [آگاهانه‌تر|راحت‌تر] [تصمیم بگیرید|انتخاب کنید]؛ [محصولی|مدلی] از برند {B} در گروه {C}."));
        else if (p?.CategoryName is not null)
            intro.Add(Said(c,
                "در این [نقد و بررسی|بررسی]، {L} از دستهٔ {C} را از نظر مشخصات فنی، کاربرد روزمره و شرایط خرید [مرور می‌کنیم|بررسی می‌کنیم].",
                "[اگر|وقتی] به خرید {L} فکر می‌کنید، این بررسی [مهم‌ترین مشخصات|مشخصات کلیدی] و کاربردهای آن را [یک‌جا|کنار هم] [نشان می‌دهد|جمع کرده است].",
                "{L} را [در این صفحه|اینجا] [با جزئیات|دقیق] بررسی کرده‌ایم تا [آگاهانه‌تر|راحت‌تر] [تصمیم بگیرید|انتخاب کنید]."));
        else
            intro.Add(Said(c,
                "در این [نقد و بررسی|بررسی]، {L} را از نظر مشخصات فنی، کاربرد روزمره و شرایط خرید [مرور می‌کنیم|بررسی می‌کنیم].",
                "{L} را [در این صفحه|اینجا] [با جزئیات|دقیق] بررسی کرده‌ایم تا [آگاهانه‌تر|راحت‌تر] [تصمیم بگیرید|انتخاب کنید]."));
        intro[0] = Mark(intro[0], ctx.Lead, StrongOpen, StrongClose);

        var top = facts.Take(3).Select(FactPhrase).ToList();
        if (top.Count >= 2)
        {
            var joined = PersianText.JoinList(top);
            intro.Add(Said(c,
                $"[مهم‌ترین|برجسته‌ترین] ویژگی‌های {{N}} {joined} است.",
                $"{joined} از [نکات|ویژگی‌های] [اصلی|قابل توجه] {{N}} [به شمار می‌رود|است].",
                $"[به‌طور خلاصه|در نگاه اول]، {{N}} با {joined} [شناخته می‌شود|جلب توجه می‌کند]."));
        }
        sections.Add("<p " + ReviewMarkerAttribute + ">" + RichInline(string.Join(' ', intro)) + "</p>");

        // ۲) خلاصهٔ بررسی (اول کار برای خریدار: نکته‌های کلیدی در یک نگاه).
        var summary = new List<string>();
        foreach (var fact in facts.Take(3))
        {
            var phrase = SpecKnowledge.Highlight(fact) ?? FactPhrase(fact);
            var why = Why(fact);
            summary.Add(StrongOpen + phrase + StrongClose + (why is null ? "." : "؛ " + why + "."));
        }
        if (WarrantyPhrase(p) is { } warrantyItem)
            summary.Add(StrongOpen + Strip(SpecKnowledge.Safe(warrantyItem)) + StrongClose + (c.Next(2) == 0 ? "؛ برای پشتیبانی پس از خرید." : "؛ همراه با خدمات پس از خرید."));
        if (notes is { ReviewCount: >= 3 })
            summary.Add($"جمع‌بندی {StrongOpen}{PersianText.Digits(notes.ReviewCount)} نظر خریداران{StrongClose} در همین بررسی آمده است.");
        if (summary.Count >= 2)
            sections.Add("<div class=\"fs-review-box\"><p class=\"fs-review-box-title\"><strong>خلاصهٔ بررسی</strong></p>" + BulletList(summary) + "</div>");

        // ۳) مشخصات: H2 + برای هر موضوع شناخته‌شده یک H3 با توضیح واقعی آن مشخصه.
        sections.Add(H2(c.Say(
            "بررسی مشخصات {S}", "مشخصات و ویژگی‌های {S}", "{S} از نظر فنی", "نگاهی دقیق به ویژگی‌های {S}", "{S} چه ویژگی‌هایی دارد؟")));
        var groups = facts.Where(f => TopicTitle(f.Topic) is not null)
            .GroupBy(f => TopicTitle(f.Topic)!, StringComparer.Ordinal)
            .Take(4)
            .ToList();
        if (groups.Count >= 2)
        {
            foreach (var group in groups)
            {
                sections.Add(H3(c.Say(group.Key, group.Key + " {N}")));
                var sentences = group.Take(3).Select(f => Explained(c, f)).ToList();
                sections.Add(Paragraph(sentences));
            }
        }
        else
        {
            var sentences = facts.OrderBy(f => f.Topic == SpecTopic.Generic ? 1 : 0).Take(6)
                .Select(f => Explained(c, f)).ToList();
            for (var i = 0; i < sentences.Count; i += 3)
                sections.Add(Paragraph(sentences.Skip(i).Take(3).ToList()));
        }

        // جدول مشخصات کلیدی (با سرستون؛ HtmlSeoFixer هم همین ساختار را می‌خواهد).
        var rows = ctx.Facts.Take(8).ToList();
        if (rows.Count >= 3)
        {
            sections.Add(H3(c.Say("مشخصات کلیدی در یک نگاه", "جدول مشخصات [اصلی|کلیدی] {S}", "خلاصهٔ مشخصات {N}")));
            var table = new StringBuilder("<table class=\"fs-spec-table\"><thead><tr><th>ویژگی</th> <th>مقدار</th></tr></thead><tbody>");
            foreach (var row in rows)
                table.Append("<tr><td>").Append(WebUtility.HtmlEncode(row.Key)).Append("</td> <td>")
                    .Append(WebUtility.HtmlEncode(row.IsYes ? "دارد" : row.Value)).Append("</td></tr>");
            table.Append("</tbody></table>");
            sections.Add(table.ToString());
        }

        // ۴) نقاط قوت و «پیش از خرید بدانید» (منصفانه و فقط از داده).
        var pros = Pros(c);
        var cautions = Cautions(c);
        sections.Add(H2(c.Say("نقاط قوت و نکات قابل توجه {S}", "[مزایا|نقاط قوت] و [محدودیت‌های|نکته‌های] {N}", "{S}: [نقاط قوت|مزایا] و نکته‌های پیش از خرید")));
        var box = new StringBuilder("<div class=\"fs-proscons\">");
        if (pros.Count > 0)
            box.Append("<div class=\"fs-pros\"><h3>نقاط قوت</h3>").Append(BulletList(pros)).Append("</div>");
        box.Append("<div class=\"fs-cons\"><h3>پیش از خرید بدانید</h3>").Append(BulletList(cautions)).Append("</div></div>");
        sections.Add(box.ToString());

        // ۵) مقایسه با هم‌دسته‌ها در خود فروشگاه (فقط اختلاف معنادار).
        var compare = CompareSentences(c);
        if (compare.Count > 0)
        {
            sections.Add(H2(c.Say("{S} در مقایسه با مدل‌های مشابه", "مقایسهٔ {S} با [سایر|دیگر] [مدل‌های|محصولات] {C}", "{S} [در کنار|در برابر] [هم‌رده‌ها|مدل‌های هم‌رده]")));
            sections.Add(Paragraph(compare));
        }

        // ۶) مناسب چه کسانی است (از مشخصات + «مناسب برای» تأییدشدهٔ خلاصهٔ خرید).
        var audience = facts.Select(SpecKnowledge.Audience).OfType<string>()
            .Concat((notes?.ForWhom ?? []).Select(Note).Where(n => n.Length > 0))
            .Distinct(StringComparer.Ordinal).Take(4).ToList();
        if (audience.Count > 0)
        {
            sections.Add(H2(c.Say("{S} مناسب چه کسانی است؟", "این {T} برای چه کسانی مناسب است؟", "[چه کسانی|چه کاربرانی] از {S} [بیشتر لذت می‌برند|بیشترین بهره را می‌برند]؟")));
            sections.Add("<p>" + RichInline(Said(c,
                "{N} [به‌ویژه|بیشتر] برای این [گروه‌ها|افراد] [مناسب است|پیشنهاد می‌شود]:",
                "[با توجه به|بر اساس] مشخصات، {N} [برای|به کار] این [افراد|گروه‌ها] [می‌آید|مناسب است]:")) + "</p>");
            sections.Add(BulletList(audience.Select(a => a + ".").ToList()));
        }

        // ۷) جمع‌بندی و خرید (آخرین H2؛ کلمهٔ کلیدی در تیتر) + شرایط فروشگاه + لینک داخلی.
        var closing = new List<string>();
        if (notes?.Verdict is { } verdict && Note(verdict).Length >= 15)
            closing.Add(EmOpen + "جمع‌بندی خریداران:" + EmClose + " " + Note(verdict).TrimEnd('.', '!', '؟') + ".");
        else
        {
            var two = facts.Take(2).Select(FactPhrase).ToList();
            var forWhom = audience.FirstOrDefault() ?? "استفادهٔ روزمره";
            closing.Add(Said(c,
                $"[در مجموع|به‌طور خلاصه]، {{N}} با {PersianText.JoinList(two)} [گزینه‌ای منطقی|انتخابی سنجیده] برای {forWhom} است.",
                $"[اگر|چنانچه] {PersianText.JoinList(two)} برایتان مهم است، {{N}} [ارزش بررسی دارد|گزینهٔ مناسبی است]؛ به‌ویژه برای {forWhom}."));
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
        sections.Add(H2(c.Say("جمع‌بندی و خرید {L}", "خرید {L}: جمع‌بندی نهایی", "[نتیجه‌گیری|جمع‌بندی] [نهایی|] دربارهٔ خرید {L}")));
        // پاراگراف‌های ≤ ۳ جمله (خوانایی و قانون طول پاراگراف).
        for (var i = 0; i < closing.Count; i += 3)
            sections.Add(Paragraph(closing.Skip(i).Take(3).ToList()));
        return sections;
    }

    /// <summary>
    /// نقاط قوت از داده: عبارت برجستهٔ مشخصات، برتری معنادار نسبت به میانگین هم‌دسته‌ها، گارانتی و نقاط قوت تأییدشدهٔ خریداران.
    /// مثال: «<strong>حذف نویز فعال</strong> برای تمرکز در محیط‌های شلوغ.»
    /// </summary>
    private static List<string> Pros(Composer c)
    {
        var ctx = c.Ctx;
        var list = new List<string>();
        foreach (var fact in ctx.Distinctive)
        {
            if (list.Count >= 4)
                break;
            if (SpecKnowledge.Highlight(fact) is not { } highlight)
                continue;
            var better = BetterThanAverage(ctx, fact);
            list.Add(StrongOpen + highlight + StrongClose + (better ? " بالاتر از میانگین هم‌رده‌ها در همین فروشگاه." : Why(fact) is { } why ? "؛ " + why + "." : "."));
        }
        if (list.Count == 0)
            list.AddRange(ctx.Distinctive.Take(2).Select(f => StrongOpen + FactPhrase(f) + StrongClose + "."));
        foreach (var pro in (ctx.Notes?.Pros ?? []).Select(Note).Where(n => n.Length > 0).Take(3))
            list.Add(EmOpen + "از نظر خریداران:" + EmClose + " " + pro.TrimEnd('.') + ".");
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
        if (WarrantyPhrase(ctx.Subject.Product) is null && list.Count < 5)
            list.Add("در مشخصات این کالا گارانتی ثبت نشده است؛ اگر برایتان مهم است، پیش از خرید از پشتیبانی بپرسید.");
        if (list.Count == 0)
            list.Add(Said(c,
                "[پیش از خرید|قبل از ثبت سفارش]، [جدول مشخصات|مشخصات کامل] را با نیاز خود مقایسه کنید.",
                "[برای انتخاب درست|برای خرید آگاهانه]، سازگاری {N} با [دستگاه‌ها|وسایل] و نیازتان را در [جدول مشخصات|مشخصات کامل] بررسی کنید."));
        return list;
    }

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
