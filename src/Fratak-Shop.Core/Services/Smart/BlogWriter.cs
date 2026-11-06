using System.Globalization;
using System.Net;
using System.Text;
using Fratak_Shop.Core.Services.Seo.Autopilot;
using Fratak_Shop.Core.Services.Seo.Engine;
using Fratak_Shop.Data.Entities.Smart;

namespace Fratak_Shop.Core.Services.Smart;

/// <summary>یک محصول واقعی فروشگاه برای استفاده در مقاله (قیمت هنگام نمایش با [[price:slug]] زنده می‌شود).</summary>
public sealed record BlogProduct(
    Guid Id, string Name, string Slug, string? BrandName, decimal FinalPrice, bool InStock, double Rating, int ReviewCount, int SalesCount,
    IReadOnlyList<SeoFeature> Features, IReadOnlyList<string> Pros, IReadOnlyList<string> Cons, IReadOnlyList<string> ForWhom);

/// <summary>
/// سفارش نوشتن یک مقاله. مثال: (BestPicks، «۵ پیشنهاد خرید هدفون بی‌سیم»، دستهٔ «هدفون»، ۵ محصول).
/// <para>دور ۲۱: <paramref name="CustomTitle"/> = عنوانی که ادمین خودش نوشته (ایدهٔ دستی)؛ همان عنوان مقاله می‌شود.</para>
/// </summary>
public sealed record BlogBrief(BlogArticleType Type, string Topic, string? Keyword, string? CategoryName, string? CategorySlug, IReadOnlyList<BlogProduct> Products, DateTimeOffset Now,
    string? CustomTitle = null);

/// <summary>مقالهٔ آماده: عنوان، خلاصه، HTML (با کدهای کوتاه محصول) و کلمهٔ کلیدی.</summary>
public sealed record BlogArticle(string Title, string Excerpt, string Html, string Keyword, IReadOnlyList<string> ProductSlugs);

/// <summary>نتیجهٔ دروازهٔ کیفیت. <c>Passed</c> = همهٔ شرط‌های سخت برقرار و امتیاز ≥ حداقل.</summary>
public sealed record QualityReport(int Score, bool Passed, IReadOnlyList<string> Problems, int Words, int H2Count, double AverageSentence, int ProductCards);

/// <summary>
/// نویسندهٔ مقالهٔ بلاگ از دادهٔ واقعی فروشگاه (B14–B16): راهنمای خرید، بهترین‌ها، مقایسه و بررسی.
/// <para>اصل «اول اطلاعات کلیدی» (درخواست مالک): هر مقاله با کادر «خلاصه در یک نگاه» (TL;DR) شروع می‌شود که پاسخ مستقیم را می‌دهد.</para>
/// <para>هیچ متنی از سایت دیگری برداشته نمی‌شود؛ متن از مشخصات، امتیاز، نکته‌های جمع‌بندی‌شدهٔ نظرها و الگوهای متنوع خود سایت ساخته می‌شود.
/// قیمت‌ها با کد کوتاه <c>[[price:slug]]</c> و کارت محصول با <c>[[product:slug]]</c> هنگام نمایش زنده می‌شوند (قیمت کهنه نمی‌ماند).</para>
/// <para>محل استفاده: SmartOrchestrator ← کار «نوشتن مقاله» (دستی یا تقویم محتوا).</para>
/// </summary>
public static class BlogWriter
{
    /// <summary>نشانهٔ کادر خلاصه در HTML (دروازهٔ کیفیت دنبالش می‌گردد).</summary>
    public const string TldrClass = "fs-tldr";

    public static BlogArticle Write(BlogBrief brief)
    {
        var products = brief.Products.Where(p => p.InStock).DistinctBy(p => p.Id).ToList();
        return brief.Type switch
        {
            BlogArticleType.Comparison when products.Count >= 2 => Comparison(brief, products[0], products[1]),
            BlogArticleType.Review when products.Count >= 1 => Review(brief, products[0]),
            BlogArticleType.BestPicks => BestPicks(brief, products),
            _ => BuyingGuide(brief, products),
        };
    }

    // ───────────────────────────── بهترین‌ها ─────────────────────────────

    private static BlogArticle BestPicks(BlogBrief brief, List<BlogProduct> products)
    {
        var cat = Category(brief);
        var year = PersianYear(brief.Now);
        // دور ۲۱: فهرست متنوع (حداکثر ۲ کالا از هر برند + حتماً یک گزینهٔ اقتصادی) و عنوان «۵ پیشنهاد خرید …» به جای «بهترین …»
        // (ادعای «بهترین» بدون آزمون مستقل نه صادقانه است نه برای گوگل امن).
        var ranked = Diverse(products, 5);
        var seed = brief.Topic + cat;
        var keyword = Keyword(brief, $"خرید {cat}");
        var title = Title(brief, $"{PersianText.Digits(ranked.Count)} پیشنهاد خرید {cat} {year}؛ مقایسه، قیمت روز و مناسب چه کسی است");
        var html = new StringBuilder();

        var best = ranked.FirstOrDefault();
        var value = ranked.Where(p => p != best).OrderBy(p => p.FinalPrice).FirstOrDefault(p => p.Rating >= 3.8 || p.ReviewCount < 3);
        Tldr(html, [
            best is null ? null : $"انتخاب اول ما: {E(Short(best))} – بالاترین امتیاز و رضایت خریداران در این فهرست.",
            value is null ? null : $"گزینهٔ اقتصادی: {E(Short(value))} – کمترین قیمت بین گزینه‌های خوش‌نام این فهرست.",
            ranked.Count > 2 ? $"اگر {E(Topic(ranked[2]))} برایتان مهم است: {E(Short(ranked[2]))}." : null,
            "قیمت‌ها هنگام باز کردن صفحه از فروشگاه خوانده می‌شوند و همیشه به‌روزند.",
        ]);

        html.Append(P(Spun(seed + "i", $"این فهرست از میان {cat}‌های موجود در فروشگاه و بر پایهٔ [سه معیار|چند معیار روشن] چیده شده: مشخصات فنی، امتیاز و نظر خریداران واقعی، و [ارزش خرید نسبت به قیمت|نسبت کیفیت به قیمت].")));
        html.Append(P(Spun(seed + "j", $"[برای هر مدل|کنار هر گزینه] نوشته‌ایم مناسب چه کسانی است و [چه ضعفی دارد|کجا کم می‌آورد] تا [سریع‌تر تصمیم بگیرید|انتخاب آسان‌تری داشته باشید]. [فقط کالاهای موجود|تنها مدل‌هایی که همین حالا موجودند] در فهرست آمده‌اند.")));

        ComparisonTable(html, ranked, $"مقایسهٔ سریع {cat}‌های منتخب");

        var index = 0;
        foreach (var product in ranked)
        {
            index++;
            var label = index == 1 ? "انتخاب اول ما" : product == value ? "گزینهٔ اقتصادی" : $"پیشنهاد {PersianText.Digits(index)}";
            html.Append(H2($"{PersianText.Digits(index)}. {E(Short(product))} – {label}"));
            ProductSection(html, product, seed);
        }

        HowWeChose(html, seed);
        BuyingTips(html, cat, products, seed, 4);
        Faq(html, cat, products, seed);
        html.Append(H2("جمع‌بندی"));
        html.Append(P(best is null
            ? $"برای انتخاب {cat} اول نیاز اصلی‌تان را مشخص کنید و بعد مشخصات کلیدی را مقایسه کنید."
            : Spun(seed + "z", $"اگر [وقت مقایسه ندارید|فرصت بررسی همه را ندارید]، {E(Short(best))} [کم‌ریسک‌ترین|مطمئن‌ترین] انتخاب این فهرست است. برای بودجهٔ محدودتر{(value is null ? "" : $" {E(Short(value))}")} را ببینید و [در نهایت|در آخر] مدلی را بردارید که [با نیاز اصلی‌تان|با نوع استفادهٔ شما] جور است.")));

        return Finish(title, keyword, html, ranked, $"{PersianText.Digits(ranked.Count)} پیشنهاد خرید {cat} با مقایسهٔ مشخصات، نقاط قوت و ضعف، قیمت روز و اینکه هر کدام مناسب چه کسی است.");
    }

    // ───────────────────────────── راهنمای خرید ─────────────────────────────

    private static BlogArticle BuyingGuide(BlogBrief brief, List<BlogProduct> products)
    {
        var cat = Category(brief);
        var year = PersianYear(brief.Now);
        var seed = brief.Topic + cat + "g";
        var keyword = Keyword(brief, $"راهنمای خرید {cat}");
        var title = Title(brief, $"{keyword} {year}؛ قبل از خرید به این نکته‌ها دقت کنید");
        var ranked = products.OrderByDescending(Score).Take(4).ToList();
        var html = new StringBuilder();

        var topics = CommonFacts(products).Take(3).Select(f => f.Key).ToList();
        Tldr(html, [
            topics.Count > 0 ? $"مهم‌ترین مشخصات برای مقایسه: {E(PersianText.JoinList(topics))}." : null,
            products.Count > 0 ? $"بازهٔ قیمت مدل‌های موجود: از {PersianText.Money(products.Min(p => p.FinalPrice))} تا {PersianText.Money(products.Max(p => p.FinalPrice))} تومان." : null,
            ranked.Count > 0 ? $"پیشنهاد سریع: {E(Short(ranked[0]))}." : null,
            "در پایان، فهرست کوتاه پیشنهادها و پاسخ پرسش‌های رایج آمده است.",
        ]);

        html.Append(P(Spun(seed + "i", $"[خرید {cat}|انتخاب {cat}] وقتی ساده می‌شود که بدانید [کدام مشخصات واقعاً فرق ایجاد می‌کند|کدام عددها مهم‌اند و کدام فقط تبلیغاتی‌اند]. در این راهنما [مشخصات کلیدی را|نکته‌های اصلی را] [به زبان ساده|ساده و کاربردی] توضیح داده‌ایم و [برای هر کدام|کنار هر نکته] مثال واقعی از کالاهای فروشگاه آورده‌ایم.")));

        html.Append(H2($"{cat} مناسب شما کدام است؟"));
        var audiences = products.SelectMany(p => p.ForWhom).GroupBy(a => a, StringComparer.Ordinal).OrderByDescending(g => g.Count()).Select(g => g.Key).Take(4).ToList();
        if (audiences.Count > 0)
        {
            html.Append(P(Spun(seed + "a", "[اول|قبل از هر چیز] ببینید [از کدام گروه هستید|کاربری شما به کدام گروه نزدیک‌تر است]؛ [مشخصاتی که باید دنبالش باشید|اولویت‌های انتخاب] برای هر گروه فرق می‌کند:")));
            html.Append(Ul(audiences.Select(E)));
        }
        else
        {
            html.Append(P(Spun(seed + "a", $"[نوع استفاده|کاربرد روزانه] را مشخص کنید: [استفادهٔ سبک روزمره|کار روزمره] به {cat} ساده و مقرون‌به‌صرفه نیاز دارد، ولی [استفادهٔ حرفه‌ای یا طولانی|کار سنگین و مداوم] مشخصات قوی‌تر و [ساخت بادوام‌تر|کیفیت ساخت بهتری] می‌خواهد.")));
        }

        BuyingTips(html, cat, products, seed, 6);

        html.Append(H2("قیمت و بودجه"));
        if (products.Count > 0)
        {
            var sorted = products.OrderBy(p => p.FinalPrice).ToList();
            var mid = sorted[sorted.Count / 2];
            html.Append(P(Spun(seed + "p", $"در حال حاضر [قیمت {cat}‌های موجود در فروشگاه|بازهٔ قیمت مدل‌های موجود] از {PersianText.Money(sorted[0].FinalPrice)} تا {PersianText.Money(sorted[^1].FinalPrice)} تومان است و [مدل‌های میان‌رده|قیمت میانه] حدود {PersianText.Money(mid.FinalPrice)} تومان است. [گران‌ترین مدل همیشه بهترین انتخاب برای شما نیست|قیمت بالاتر لزوماً یعنی انتخاب بهتر برای نیاز شما نیست]؛ [اول مشخصات ضروری را مشخص کنید|ابتدا ویژگی‌های لازم را فهرست کنید] و بعد ارزان‌ترین مدلی را بردارید که همه را دارد.")));
        }

        html.Append(H2("اشتباه‌های رایج هنگام خرید"));
        html.Append(Ul([
            Spun(seed + "m1", "[مقایسهٔ فقط قیمت|نگاه کردن فقط به قیمت] بدون دقت به [گارانتی و خدمات پس از فروش|گارانتی معتبر]"),
            Spun(seed + "m2", "[اعتماد به عددهای تبلیغاتی|تکیه بر ادعاهای تبلیغاتی] به جای [مشخصات ثبت‌شده و نظر خریداران|نظر خریداران واقعی]"),
            Spun(seed + "m3", "[خرید مدلی که برای نیازتان زیادی قوی یا ضعیف است|انتخاب مدلی که با نوع استفاده‌تان جور نیست]"),
            Spun(seed + "m4", "[نخواندن شرایط بازگشت کالا|بی‌توجهی به شرایط مرجوعی] پیش از خرید"),
        ]));

        if (ranked.Count > 0)
        {
            html.Append(H2($"پیشنهادهای ما برای خرید {cat}"));
            html.Append(P(Spun(seed + "r", "[این مدل‌ها|این گزینه‌ها] [بیشترین امتیاز و رضایت|بهترین ترکیب امتیاز، فروش و مشخصات] را بین کالاهای موجود دارند:")));
            foreach (var product in ranked)
            {
                html.Append(H3(E(Short(product))));
                html.Append(P(Line(product, seed)));
                html.Append(Card(product));
            }
        }

        Faq(html, cat, products, seed);
        html.Append(H2("جمع‌بندی"));
        html.Append(P(Spun(seed + "z", $"[برای خرید درست {cat}|برای یک انتخاب درست] [سه قدم کافی است|این سه قدم را بردارید]: نیاز اصلی را مشخص کنید، [مشخصات کلیدی بالا را مقایسه کنید|مشخصات مهم را کنار هم بگذارید] و [نظر خریداران واقعی را بخوانید|به تجربهٔ خریداران قبلی توجه کنید]. [اگر سؤالی داشتید|برای راهنمایی بیشتر]، پشتیبانی فروشگاه کمکتان می‌کند.")));

        return Finish(title, keyword, html, ranked, $"{keyword}: مشخصات مهم به زبان ساده، بازهٔ قیمت، اشتباه‌های رایج و پیشنهاد مدل‌های موجود.");
    }

    // ───────────────────────────── مقایسه ─────────────────────────────

    private static BlogArticle Comparison(BlogBrief brief, BlogProduct a, BlogProduct b)
    {
        var seed = a.Slug + b.Slug;
        var keyword = Keyword(brief, $"مقایسه {Short(a)} و {Short(b)}");
        var title = Title(brief, $"{keyword}؛ کدام را بخریم؟");
        var html = new StringBuilder();
        var cheaper = a.FinalPrice <= b.FinalPrice ? a : b;
        var better = Score(a) >= Score(b) ? a : b;

        Tldr(html, [
            $"اگر رضایت خریداران و کیفیت کلی برایتان مهم‌تر است: {E(Short(better))}.",
            cheaper != better ? $"اگر بودجه محدود است: {E(Short(cheaper))} (ارزان‌تر)." : $"{E(Short(better))} ارزان‌تر هم هست؛ انتخاب ساده است.",
            $"تفاوت‌های کلیدی در جدول زیر آمده و برندهٔ هر ردیف مشخص شده است.",
        ]);

        html.Append(P(Spun(seed + "i", $"{E(Short(a))} و {E(Short(b))} [دو گزینهٔ پرسش‌برانگیزند|دو مدلی‌اند که زیاد با هم مقایسه می‌شوند]. در ادامه [مشخصات را ردیف‌به‌ردیف|مشخصات اصلی را کنار هم] گذاشته‌ایم، [نقاط قوت و ضعف هر کدام را|نکته‌های مثبت و منفی هر دو را] از نظر خریداران آورده‌ایم و گفته‌ایم هر کدام مناسب چه کسی است.")));

        html.Append(H2("مقایسهٔ مشخصات"));
        var rows = SideBySide(a, b);
        var table = new StringBuilder("<table><thead><tr><th>ویژگی</th>");
        table.Append($"<th>{E(Short(a))}</th><th>{E(Short(b))}</th></tr></thead><tbody>");
        table.Append($"<tr><td>قیمت فعلی</td><td>[[price:{a.Slug}]]</td><td>[[price:{b.Slug}]]</td></tr>");
        if (a.ReviewCount + b.ReviewCount > 0)
            table.Append($"<tr><td>امتیاز خریداران</td><td>{RatingText(a)}</td><td>{RatingText(b)}</td></tr>");
        foreach (var row in rows.Take(10))
            table.Append($"<tr><td>{E(row.Key)}</td><td>{E(row.A)}{(row.Winner == 1 ? " ✓" : "")}</td><td>{E(row.B)}{(row.Winner == 2 ? " ✓" : "")}</td></tr>");
        table.Append("</tbody></table>");
        html.Append(table);

        var explained = 0;
        html.Append(H2("تفاوت‌ها به زبان ساده"));
        foreach (var row in rows.Where(r => r.Winner != 0).Take(4))
        {
            var winner = row.Winner == 1 ? a : b;
            var fact = SpecKnowledge.Read(new SeoFeature(row.Key, row.Winner == 1 ? row.A : row.B));
            var explain = fact is null ? null : SpecKnowledge.Explain(fact).FirstOrDefault();
            html.Append(P(explain is null
                ? $"در «{E(row.Key)}»، {E(Short(winner))} با مقدار {E(row.Winner == 1 ? row.A : row.B)} جلوتر است."
                : $"در «{E(row.Key)}»، {E(Short(winner))} جلوتر است. {E(Fill(Spun(seed + row.Key, explain), Short(winner)))}"));
            explained++;
        }
        if (explained == 0)
            html.Append(P("مشخصات ثبت‌شدهٔ این دو مدل تفاوت عددی روشنی ندارند؛ انتخاب بیشتر به قیمت، طراحی و نظر خریداران بستگی دارد."));

        foreach (var product in new[] { a, b })
        {
            html.Append(H2($"{E(Short(product))} مناسب چه کسانی است؟"));
            ProductSection(html, product, seed);
        }

        html.Append(H2("کدام را بخریم؟"));
        html.Append(P(Spun(seed + "w", $"[جمع‌بندی ما|خلاصه اینکه]: {E(Short(better))} [برای بیشتر خریداران انتخاب مطمئن‌تری است|در مجموع رضایت بیشتری داشته]{(cheaper != better ? $"، ولی اگر [تفاوت قیمت برایتان مهم است|بودجه اولویت اول است]، {E(Short(cheaper))} [انتخاب منطقی‌تری است|گزینهٔ به‌صرفه‌تری است]" : "")}. [پیش از خرید|قبل از تصمیم نهایی] [ردیف‌هایی از جدول را که برایتان مهم‌تر است|ویژگی‌هایی را که واقعاً لازم دارید] دوباره مرور کنید.")));
        Faq(html, Category(brief), [a, b], seed);

        return Finish(title, keyword, html, [a, b], $"{keyword}: جدول مشخصات، برندهٔ هر ویژگی، نقاط قوت و ضعف و پیشنهاد خرید برای هر نوع کاربر.");
    }

    // ───────────────────────────── بررسی یک محصول ─────────────────────────────

    private static BlogArticle Review(BlogBrief brief, BlogProduct product)
    {
        var seed = product.Slug + "rv";
        var keyword = Keyword(brief, $"بررسی {Short(product)}");
        var title = $"{keyword}؛ نقاط قوت و ضعف از نگاه خریداران";
        var html = new StringBuilder();
        var facts = Facts(product).ToList();

        Tldr(html, [
            product.Pros.Count > 0 ? $"مهم‌ترین نقطهٔ قوت: {E(product.Pros[0])}." : null,
            product.Cons.Count > 0 ? $"مهم‌ترین ضعف: {E(product.Cons[0])}." : "ایراد تکرارشونده‌ای در نظرها دیده نشده است.",
            product.ForWhom.Count > 0 ? $"مناسب برای: {E(product.ForWhom[0])}." : null,
            product.ReviewCount >= 3 ? $"امتیاز خریداران: {RatingText(product)}." : null,
        ]);
        html.Append(P(Spun(seed + "i", $"در این بررسی، {E(Short(product))} را [از روی مشخصات فنی|بر پایهٔ مشخصات ثبت‌شده] و [تجربهٔ خریداران واقعی|نظر کسانی که آن را خریده‌اند] بررسی کرده‌ایم تا [بدانید به کارتان می‌آید یا نه|ببینید با نیازتان جور است یا نه].")));
        html.Append(Card(product));

        html.Append(H2("مشخصات کلیدی و معنای آن‌ها"));
        foreach (var fact in facts.Take(6))
        {
            var explain = SpecKnowledge.Explain(fact).FirstOrDefault();
            html.Append(P($"<strong>{E(fact.Key)}: {E(fact.Value)}</strong> – {E(explain is null ? "" : Fill(Spun(seed + fact.Key, explain), Short(product)))}"));
        }
        if (facts.Count == 0)
            html.Append(P("مشخصات فنی این کالا در صفحهٔ محصول به‌طور کامل آمده است."));

        html.Append(H2("نقاط قوت و ضعف"));
        Points(html, product);
        html.Append(H2("مناسب چه کسانی است؟"));
        html.Append(product.ForWhom.Count > 0 ? Ul(product.ForWhom.Select(E)) : P("برای استفادهٔ روزمره و عمومی مناسب است."));
        Faq(html, Category(brief), [product], seed);
        html.Append(H2("جمع‌بندی"));
        html.Append(P(Line(product, seed)));
        return Finish(title, keyword, html, [product], $"{keyword}: مشخصات کلیدی به زبان ساده، نقاط قوت و ضعف از نظر خریداران و اینکه مناسب چه کسی است.");
    }

    // ───────────────────────────── بخش‌های مشترک ─────────────────────────────

    private static void ProductSection(StringBuilder html, BlogProduct product, string seed)
    {
        html.Append(P(Line(product, seed)));
        var highlights = Facts(product).Select(SpecKnowledge.Highlight).Where(h => h is not null).Select(h => h!).Distinct().Take(4).ToList();
        if (highlights.Count > 0)
            html.Append(P($"<strong>ویژگی‌های برجسته:</strong> {E(PersianText.JoinList(highlights))}."));
        Points(html, product);
        if (product.ForWhom.Count > 0)
            html.Append(P($"<strong>مناسب برای:</strong> {E(PersianText.JoinList(product.ForWhom.Take(2).ToList()))}."));
        html.Append(Card(product));
    }

    private static void Points(StringBuilder html, BlogProduct product)
    {
        if (product.Pros.Count > 0)
            html.Append(P("<strong>نقاط قوت از نگاه خریداران:</strong>")).Append(Ul(product.Pros.Take(4).Select(E)));
        if (product.Cons.Count > 0)
            html.Append(P("<strong>نقاط ضعف:</strong>")).Append(Ul(product.Cons.Take(3).Select(E)));
    }

    private static string Line(BlogProduct product, string seed)
    {
        var name = E(Short(product));
        var brand = string.IsNullOrWhiteSpace(product.BrandName) ? "" : $" ساخت {E(product.BrandName)}";
        var rating = product.ReviewCount >= 3
            ? Spun(seed + product.Slug, $" [و از {PersianText.Digits(product.ReviewCount)} خریدار|با {PersianText.Digits(product.ReviewCount)} نظر و] امتیاز {PersianText.Decimal1(product.Rating)} از ۵ گرفته است")
            : "";
        var highlight = Facts(product).Select(SpecKnowledge.Highlight).FirstOrDefault(h => h is not null);
        var core = highlight is null
            ? Spun(seed + product.Slug + "l", $"{name}{brand} [یکی از گزینه‌های موجود این دسته است|از مدل‌های موجود فروشگاه است]")
            : Spun(seed + product.Slug + "l", $"{name}{brand} [با {E(highlight)} شناخته می‌شود|نقطهٔ قوت فنی‌اش {E(highlight)} است]");
        return core + rating + ".";
    }

    private static void BuyingTips(StringBuilder html, string cat, List<BlogProduct> products, string seed, int max)
    {
        var facts = CommonFacts(products).Take(max).ToList();
        if (facts.Count == 0)
            return;
        html.Append(H2($"مهم‌ترین مشخصات در خرید {cat}"));
        foreach (var fact in facts)
        {
            var owner = products.First(p => p.Features.Any(f => f.Key == fact.Key && f.Value == fact.Value));
            var explain = SpecKnowledge.Explain(fact).FirstOrDefault();
            if (explain is null)
                continue;
            html.Append(H3(E(fact.Key)));
            html.Append(P(Spun(seed + fact.Key + "t", "[برای مثال|به عنوان نمونه]، ") + E(Fill(Spun(seed + fact.Key, explain), Short(owner)))));
        }
    }

    private static void HowWeChose(StringBuilder html, string seed)
    {
        html.Append(H2("این فهرست را چطور انتخاب کردیم؟"));
        html.Append(P(Spun(seed + "h", "[رتبه‌بندی|ترتیب فهرست] [بر اساس دادهٔ واقعی فروشگاه است|از روی داده‌های واقعی ساخته شده]: امتیاز و تعداد نظر خریداران، [میزان فروش|تعداد فروش]، مشخصات فنی ثبت‌شده و موجود بودن کالا. [هیچ برندی برای حضور در این فهرست هزینه‌ای نپرداخته است|حضور در فهرست تبلیغاتی و پولی نیست] و [با تغییر نظرها و موجودی|هر بار که داده‌ها عوض شود] [به‌روز می‌شود|بازبینی می‌شود].")));
    }

    private static void Faq(StringBuilder html, string cat, IReadOnlyList<BlogProduct> products, string seed)
    {
        var items = new List<(string Q, string A)>();
        foreach (var product in products.Take(3))
        {
            foreach (var fact in Facts(product))
            {
                if (SpecKnowledge.Faq(fact) is not { } faq || faq.Questions.Length == 0 || faq.Answers.Length == 0)
                    continue;
                var name = Short(product);
                var q = Fill(Spun(seed + fact.Key + "q", SmartText.Pick(seed + fact.Key, faq.Questions)), name).Replace("{S}", name);
                var a = Fill(Spun(seed + fact.Key + "a", SmartText.Pick(seed + fact.Key + "a", faq.Answers)), name).Replace("{S}", name);
                if (items.All(i => i.Q != q))
                    items.Add((q, a));
                break;
            }
        }
        items.Add(($"قیمت {cat} در فروشگاه چقدر است؟", "قیمت‌های داخل این مقاله هنگام باز کردن صفحه مستقیم از فروشگاه خوانده می‌شوند؛ پس همیشه قیمت روز را می‌بینید."));
        items.Add(("اگر کالا مناسبم نبود چه کنم؟", "طبق شرایط بازگشت کالای فروشگاه می‌توانید آن را برگردانید؛ جزئیات در صفحهٔ «شرایط بازگشت» آمده است."));
        html.Append(H2("پرسش‌های رایج"));
        foreach (var (q, a) in items.Take(5))
        {
            html.Append(H3(E(q)));
            html.Append(P(E(a)));
        }
    }

    private static void ComparisonTable(StringBuilder html, List<BlogProduct> products, string heading)
    {
        if (products.Count == 0)
            return;
        html.Append(H2(heading));
        var builder = new StringBuilder("<table><thead><tr><th>مدل</th><th>قیمت</th><th>امتیاز</th><th>نکتهٔ برجسته</th></tr></thead><tbody>");
        foreach (var product in products)
        {
            var highlight = Facts(product).Select(SpecKnowledge.Highlight).FirstOrDefault(h => h is not null) ?? product.Pros.FirstOrDefault() ?? "—";
            builder.Append($"<tr><td><a href=\"/product/{E(product.Slug)}\">{E(Short(product))}</a></td><td>[[price:{product.Slug}]]</td><td>{RatingText(product)}</td><td>{E(highlight)}</td></tr>");
        }
        builder.Append("</tbody></table>");
        html.Append(builder);
    }

    /// <summary>ردیف‌های مقایسه: کلیدهای مشترک دو محصول؛ برنده با جهت مشخصه (باتری بیشتر بهتر، وزن کمتر بهتر).</summary>
    public static List<(string Key, string A, string B, int Winner)> SideBySide(BlogProduct a, BlogProduct b)
    {
        var rows = new List<(string, string, string, int)>();
        foreach (var fa in a.Features)
        {
            var fb = b.Features.FirstOrDefault(f => SpecMerge.NormalizeKey(f.Key) == SpecMerge.NormalizeKey(fa.Key));
            if (fb is null)
                continue;
            var winner = 0;
            var facta = SpecKnowledge.Read(fa);
            var factb = SpecKnowledge.Read(fb);
            if (facta is { Number: { } na, Direction: not 0 } && factb is { Number: { } nb } && Math.Abs(na - nb) > 0.0001)
                winner = (na > nb) == (facta.Direction > 0) ? 1 : 2;
            rows.Add((fa.Key, fa.Value, fb.Value, winner));
        }
        return rows.OrderByDescending(r => r.Item4 != 0).ToList();
    }

    private static string RatingText(BlogProduct p) =>
        p.ReviewCount >= 3 ? $"{PersianText.Decimal1(p.Rating)} از ۵ ({PersianText.Digits(p.ReviewCount)} نظر)" : "نظر کافی ثبت نشده";

    private static void Tldr(StringBuilder html, IEnumerable<string?> lines)
    {
        var items = lines.Where(l => !string.IsNullOrWhiteSpace(l)).Select(l => l!).ToList();
        html.Append($"<div class=\"{TldrClass}\"><p><strong>خلاصه در یک نگاه</strong></p><ul>");
        foreach (var item in items)
            html.Append("<li>").Append(item).Append("</li>");
        html.Append("</ul></div>");
    }

    /// <summary>عنوان دلخواه ادمین (اگر نوشته) وگرنه عنوان ساخته‌شده. مثال: CustomTitle «هدیهٔ تولد زیر ۲ میلیون» ← همان.</summary>
    private static string Title(BlogBrief brief, string generated) =>
        string.IsNullOrWhiteSpace(brief.CustomTitle) ? generated : PersianText.Truncate(PersianText.Clean(brief.CustomTitle), 200);

    /// <summary>
    /// دور ۲۱: انتخاب متنوع برای فهرست پیشنهاد خرید. به ترتیب امتیاز، حداکثر ۲ کالا از هر برند؛ اگر ارزان‌ترین کالای خوش‌نام
    /// بیرون ماند جای آخرین را می‌گیرد تا هر بودجه‌ای گزینه داشته باشد.
    /// مثال: ۴ سامسونگ پرامتیاز + ۲ شیائومی + ۱ انکر ← ۲ سامسونگ، ۲ شیائومی، ۱ انکر.
    /// </summary>
    public static List<BlogProduct> Diverse(IReadOnlyList<BlogProduct> products, int count)
    {
        var picked = new List<BlogProduct>();
        var perBrand = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var product in products.OrderByDescending(Score))
        {
            var brand = product.BrandName ?? "";
            if (brand.Length > 0 && perBrand.GetValueOrDefault(brand) >= 2)
                continue;
            picked.Add(product);
            if (brand.Length > 0)
                perBrand[brand] = perBrand.GetValueOrDefault(brand) + 1;
            if (picked.Count >= count)
                break;
        }
        // کمبود (برندها کم بودند) ← از باقی‌مانده‌ها پر می‌شود.
        foreach (var product in products.OrderByDescending(Score).Where(p => !picked.Contains(p)))
        {
            if (picked.Count >= count)
                break;
            picked.Add(product);
        }
        var budget = products.Where(p => p.Rating >= 3.8 || p.ReviewCount < 3).OrderBy(p => p.FinalPrice).FirstOrDefault();
        if (budget is not null && !picked.Contains(budget) && picked.Count >= 2)
            picked[^1] = budget;
        return picked;
    }

    /// <summary>
    /// دور ۲۱: فهرست مطالب. هر H2 یک شناسه (s1، s2 …) می‌گیرد و فهرست لینک‌دار درست بعد از کادر «خلاصه در یک نگاه» می‌آید
    /// (خواننده مستقیم به بخش دلخواه می‌رود؛ گوگل هم لینک‌های پرش را در نتیجه نشان می‌دهد). کمتر از ۳ تیتر ← بدون فهرست.
    /// </summary>
    public static string WithToc(string html)
    {
        var titles = new List<string>();
        var index = 0;
        var withIds = System.Text.RegularExpressions.Regex.Replace(html, "<h2>(.*?)</h2>", m =>
        {
            index++;
            titles.Add(m.Groups[1].Value);
            return $"<h2 id=\"s{index}\">{m.Groups[1].Value}</h2>";
        }, System.Text.RegularExpressions.RegexOptions.Singleline, TimeSpan.FromSeconds(1));
        if (titles.Count < 3)
            return html;
        var toc = new StringBuilder("<nav class=\"fs-toc\" aria-label=\"فهرست مطالب\"><p><strong>فهرست مطالب</strong></p><ol>");
        for (var i = 0; i < titles.Count; i++)
            toc.Append($"<li><a href=\"#s{i + 1}\">{titles[i]}</a></li>");
        toc.Append("</ol></nav>");
        var marker = $"<div class=\"{TldrClass}\">";
        var start = withIds.IndexOf(marker, StringComparison.Ordinal);
        var end = start < 0 ? -1 : withIds.IndexOf("</div>", start, StringComparison.Ordinal);
        return end < 0 ? toc + withIds : withIds.Insert(end + "</div>".Length, toc.ToString());
    }

    private static BlogArticle Finish(string title, string keyword, StringBuilder html, IEnumerable<BlogProduct> products, string excerpt)
    {
        var text = WithToc(AiPatternGuard.Clean(html.ToString()));
        return new BlogArticle(PersianText.Clean(title), PersianText.Truncate(PersianText.Clean(excerpt), 300), text, keyword, products.Select(p => p.Slug).ToList());
    }

    // ───────────────────────────── دروازهٔ کیفیت (B17) ─────────────────────────────

    /// <summary>
    /// دروازهٔ کیفیت مقالهٔ خودکار. شرط‌های سخت (هر کدام نباشد ← انتشار خودکار ممنوع، فقط پیش‌نویس):
    /// ≥۷۰۰ کلمه، ≥۳ تیتر H2، کادر خلاصه، ≥۲ کارت محصول، بدون کپی ۶‌کلمه‌ای از منبع‌ها، میانگین جمله ≤۲۵ کلمه،
    /// عنوان یکتا و شامل کلمهٔ کلیدی، بدون خطر جریمهٔ مسدودکننده، نمرهٔ طبیعی بودن متن ≥۷۰. سپس امتیاز ≥ حداقل تنظیم‌شده.
    /// </summary>
    public static QualityReport Check(BlogArticle article, IEnumerable<string> existingTitles, IEnumerable<string?> sources,
        IReadOnlyCollection<long>? otherHashes = null, int minScore = 75)
    {
        var problems = new List<string>();
        var stats = PersianText.AnalyzeHtml(article.Html);
        var words = stats.Words;
        var h2 = CountTag(article.Html, "h2");
        var cards = CountOccurrences(article.Html, "[[product:");
        var avg = SmartText.AverageSentenceWords(stats.Text);
        var score = 100;
        var hard = false;

        void Fail(string message, int penalty, bool blocking = true)
        {
            problems.Add(message);
            score -= penalty;
            hard |= blocking;
        }

        if (words < 700) Fail($"متن کوتاه است ({PersianText.Digits(words)} کلمه؛ حداقل ۷۰۰).", 30);
        else if (words < 900) score -= 5;
        if (h2 < 3) Fail($"تیترهای اصلی کم است ({PersianText.Digits(h2)}؛ حداقل ۳).", 15);
        if (!article.Html.Contains($"class=\"{TldrClass}\"", StringComparison.Ordinal)) Fail("کادر «خلاصه در یک نگاه» ندارد.", 15);
        if (cards < 2 && article.ProductSlugs.Count >= 2) Fail("کمتر از ۲ کارت محصول دارد.", 10);
        if (cards < 1) Fail("هیچ کارت محصولی ندارد.", 15);
        if (avg > 25) Fail($"جمله‌ها طولانی‌اند (میانگین {PersianText.Decimal1(avg)} کلمه؛ حداکثر ۲۵).", 10);
        if (SmartText.IsCopied(stats.Text, sources)) Fail("بخشی از متن عیناً با یکی از منبع‌ها یکسان است.", 40);

        var normalizedTitle = PersianText.Normalize(article.Title);
        if (existingTitles.Any(t => PersianText.Normalize(t) == normalizedTitle)) Fail("مقاله‌ای با همین عنوان وجود دارد.", 30);
        if (!string.IsNullOrWhiteSpace(article.Keyword) && !normalizedTitle.Contains(PersianText.Normalize(article.Keyword), StringComparison.Ordinal))
            Fail("کلمهٔ کلیدی در عنوان نیست.", 10);

        foreach (var risk in SeoPenaltyGuard.Check(article.Title, article.Excerpt, article.Html, article.Keyword, null, otherHashes).Where(r => r.Blocking))
            Fail(risk.Message, 20);

        var natural = AiPatternGuard.Analyze(stats.Text);
        if (natural.Score < 70) Fail($"متن قالبی به نظر می‌رسد (نمرهٔ طبیعی بودن {PersianText.Digits(natural.Score)}).", 15);
        else score -= (100 - natural.Score) / 4;

        score = Math.Clamp(score, 0, 100);
        if (!hard && score < minScore)
            problems.Add($"امتیاز کیفیت {PersianText.Digits(score)} کمتر از حداقل {PersianText.Digits(minScore)} است.");
        return new QualityReport(score, !hard && score >= minScore, problems, words, h2, Math.Round(avg, 1), cards);
    }

    private static int CountOccurrences(string text, string token)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(token, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += token.Length;
        }
        return count;
    }

    private static int CountTag(string html, string tag)
    {
        var count = 0;
        var index = 0;
        var open = "<" + tag;
        while ((index = html.IndexOf(open, index, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            var next = index + open.Length < html.Length ? html[index + open.Length] : ' ';
            if (next is '>' or ' ')
                count++;
            index += open.Length;
        }
        return count;
    }

    // ───────────────────────────── کمکی‌ها ─────────────────────────────

    /// <summary>امتیاز رتبه‌بندی: امتیاز (با وزن تعداد نظر) + فروش. کالای بدون نظر امتیاز میانه می‌گیرد، نه صفر.</summary>
    public static double Score(BlogProduct p)
    {
        var confidence = Math.Min(1, p.ReviewCount / 10.0);
        var rating = p.ReviewCount > 0 ? p.Rating * confidence + 3.5 * (1 - confidence) : 3.5;
        return rating + Math.Log10(1 + p.SalesCount) * 0.6 + Math.Log10(1 + p.ReviewCount) * 0.3;
    }

    private static IEnumerable<SpecFact> Facts(BlogProduct product) =>
        product.Features.Select(SpecKnowledge.Read).Where(f => f is not null).Select(f => f!)
            .OrderByDescending(f => SpecKnowledge.Weight(f.Topic));

    /// <summary>مشخصاتی که در بیش از یک محصول آمده (یا مهم‌اند) – پایهٔ «نکته‌های خرید».</summary>
    private static IEnumerable<SpecFact> CommonFacts(List<BlogProduct> products) =>
        products.SelectMany(Facts)
            .Where(f => SpecKnowledge.Weight(f.Topic) > 0)
            .GroupBy(f => f.Topic)
            .OrderByDescending(g => g.Select(f => f.Key).Count())
            .ThenByDescending(g => SpecKnowledge.Weight(g.Key))
            .Select(g => g.First());

    private static string Topic(BlogProduct product) =>
        Facts(product).Select(SpecKnowledge.Highlight).FirstOrDefault(h => h is not null) ?? product.Pros.FirstOrDefault() ?? "قیمت مناسب";

    private static string Category(BlogBrief brief) => PersianText.Clean(brief.CategoryName ?? brief.Topic);

    private static string Keyword(BlogBrief brief, string fallback) =>
        PersianText.Clean(string.IsNullOrWhiteSpace(brief.Keyword) ? fallback : brief.Keyword);

    private static string PersianYear(DateTimeOffset now) =>
        PersianText.Digits(new PersianCalendar().GetYear(IranClockLocal(now)));

    private static DateTime IranClockLocal(DateTimeOffset now) => Fratak_Shop.Core.Utilities.IranClock.ToLocal(now).DateTime;

    private static string Short(BlogProduct product)
    {
        // نویسه‌های الگوی تنوع ([ | ] { }) از نام حذف می‌شوند تا الگو را نشکنند.
        var safe = new string(product.Name.Select(c => c is '[' or ']' or '|' or '{' or '}' ? ' ' : c).ToArray());
        var words = PersianText.Clean(safe).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return words.Length > 7 ? string.Join(' ', words.Take(7)) : string.Join(' ', words);
    }

    private static string Spun(string seed, string pattern)
    {
        var options = Spin.Expand(pattern, 32);
        return Spin.Tidy(options.Count == 0 ? pattern : SmartText.Pick(seed, options.ToArray()));
    }

    private static string Fill(string text, string name) => text.Replace("{N}", name, StringComparison.Ordinal).Replace("{S}", name, StringComparison.Ordinal);

    private static string E(string? text) => WebUtility.HtmlEncode(text ?? string.Empty);

    private static string P(string inner) => $"<p>{inner}</p>";

    private static string H2(string inner) => $"<h2>{inner}</h2>";

    private static string H3(string inner) => $"<h3>{inner}</h3>";

    private static string Ul(IEnumerable<string> items) => "<ul>" + string.Concat(items.Select(i => $"<li>{i}</li>")) + "</ul>";

    /// <summary>کارت محصول (در صفحهٔ مقاله با تصویر، قیمت زنده و دکمه جایگزین می‌شود).</summary>
    public static string Card(BlogProduct product) => $"<p>[[product:{product.Slug}]]</p>";

}
