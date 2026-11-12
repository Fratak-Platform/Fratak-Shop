using Fratak_Shop.Core.Services.Seo.Engine;
using Fratak_Shop.Data.Entities.SeoEngine;

namespace Fratak_Shop.Tests.Unit;

/// <summary>
/// تست‌های «تنوع و یکتایی» متن سئو (مرحلهٔ یازدهم) بدون دیتابیس و اینترنت.
/// سناریوی اصلی: ۳۰ محصول هم‌دسته و شبیه به هم (هدفون بی‌سیم با برند و مشخصات متفاوت) پشت سر هم نوشته می‌شوند،
/// درست مثل یک اجرای واقعی موتور (بعد از هر محصول نمای سایت به‌روز می‌شود). انتظار: عنوان‌ها و توضیحات متا یکتا،
/// الگوهای متنوع، متن‌ها از هم دور (SimHash)، تراکم کلمهٔ کلیدی سالم، بدون ادعای ممنوع و بدون جای‌نگهدار جامانده.
/// </summary>
[Trait("Category", "Unit")]
public class SeoVarietyTests
{
    private static readonly StoreFacts Store = new()
    {
        SiteName = "فراتک شاپ",
        TitleSuffixLength = 12,
        ReturnEnabled = true,
        ReturnDays = 7,
        DeliveryMinDays = 2,
        DeliveryMaxDays = 5,
        FreeShippingThreshold = 2_000_000,
        CashOnDelivery = true,
    };

    private static readonly string[] Brands = ["هایلو", "شیائومی", "انکر", "سامسونگ", "کیو سی وای", "اپل", "سونی", "جی بی ال", "لنوو", "باسئوس"];

    /// <summary>
    /// ۳۰ هدفون هم‌دسته با مشخصات قطعی (بدون تصادف) ولی متفاوت؛ عمداً بسیاری از مقدارها بین محصولات تکراری‌اند.
    /// </summary>
    private static List<SeoSubject> Siblings(int count = 30)
    {
        var list = new List<SeoSubject>();
        for (var i = 0; i < count; i++)
        {
            var brand = Brands[i % Brands.Length];
            var features = new List<SeoFeature>
            {
                new("نسخه بلوتوث", $"5.{i % 4}"),
                new("شارژدهی", $"{18 + i % 7 * 4} ساعت"),
            };
            if (i % 2 == 0) features.Add(new("نویز کنسلینگ", "دارد"));
            if (i % 3 != 1) features.Add(new("مقاومت در برابر آب", i % 3 == 0 ? "IPX5" : "IP54"));
            if (i % 4 == 1) features.Add(new("درگاه شارژ", "USB-C"));
            if (i % 5 == 2) features.Add(new("شارژ بی‌سیم", "دارد"));
            if (i % 6 == 3) features.Add(new("وزن", $"{38 + i % 9} گرم"));
            if (i % 7 == 4) features.Add(new("تعداد میکروفون", $"{2 + i % 5}"));

            var name = $"هدفون بی‌سیم {brand} مدل X{100 + i}";
            list.Add(new SeoSubject
            {
                Kind = SeoEntityKind.Product,
                EntityId = Guid.Parse($"00000000-0000-4000-8000-{i + 1:000000000000}"),
                Name = name,
                Slug = $"x{100 + i}",
                PathPrefix = "/product/",
                DefaultTitle = name,
                CreatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
                Images = [new SeoImageInfo(Guid.Parse($"00000000-0000-4000-9000-{i + 1:000000000000}"), null, true)],
                Product = new ProductFacts
                {
                    CategoryName = "هدفون بی‌سیم",
                    CategorySlug = "wireless-headphones",
                    BrandName = brand,
                    BrandSlug = "brand-" + (i % Brands.Length),
                    WarrantyTitle = i % 3 == 2 ? "" : "۱۸ ماهه",
                    InStock = true,
                    Features = features,
                },
            });
        }
        return list;
    }

    private sealed record Written(SeoSubject Subject, string Keyword, string Title, string Meta, string Body, List<SeoFaqItem> Faq, SeoSiteIndex Site);

    /// <summary>نوشتن پشت سر هم مثل اجرای واقعی: بعد از هر صفحه، نمای سایت با خروجی جدید جایگزین می‌شود.</summary>
    private static List<Written> WriteAll(List<SeoSubject> subjects)
    {
        var site = SeoSiteIndex.Build(subjects);
        var result = new List<Written>();
        foreach (var subject in subjects)
        {
            var keyword = KeywordPlanner.ShortName(subject.Name);
            var ctx = new WriterContext { Subject = subject, Store = Store, Keyword = keyword, Site = site };
            var title = ContentWriter.BestTitle(ctx);
            var meta = ContentWriter.BestMeta(ctx);
            var body = ContentWriter.Body(ctx) ?? "";
            var faq = ContentWriter.Faq(ctx);
            var after = subject.With(
                new SeoProposal { Title = title, MetaDescription = meta, DescriptionHtml = body, Faq = faq, FocusKeyword = keyword },
                SeoFields.FocusKeyword | SeoFields.Title | SeoFields.MetaDescription | SeoFields.Description | SeoFields.Faq);
            site.Replace(subject, after);
            result.Add(new Written(after, keyword, title, meta, body, faq, site));
        }
        return result;
    }

    private static readonly Lazy<List<Written>> Output = new(() => WriteAll(Siblings()));

    private static IReadOnlyList<string> Terms(Written w) => PhraseUsage.TermsOf(w.Subject, w.Keyword);

    // ── یکتایی و تنوع ────────────────────────────────────────────────

    [Fact]
    public void Siblings_get_unique_titles_and_metas()
    {
        var output = Output.Value;
        Assert.Equal(30, output.Select(w => w.Title).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(30, output.Select(w => w.Meta).Distinct(StringComparer.Ordinal).Count());
        Assert.All(output, w => Assert.InRange(w.Title.Length, SeoLimits.TitleMin, SeoLimits.TitleMax));
        Assert.All(output, w => Assert.InRange(w.Meta.Length, 100, SeoLimits.MetaMax));
    }

    [Fact]
    public void Title_and_meta_patterns_are_diverse()
    {
        var output = Output.Value;
        var titleSkeletons = output.GroupBy(w => PhraseUsage.Skeleton(w.Title, Terms(w))).ToList();
        var metaSkeletons = output.GroupBy(w => PhraseUsage.Skeleton(w.Meta, Terms(w))).ToList();

        // هیچ الگوی عنوانی روی بیش از ۳۰٪ محصولات تکرار نمی‌شود و حداقل ۸ الگوی متفاوت هست.
        Assert.True(titleSkeletons.Count >= 8, $"title patterns: {titleSkeletons.Count}");
        Assert.True(titleSkeletons.Max(g => g.Count()) <= 9, $"most common title pattern: {titleSkeletons.Max(g => g.Count())}");
        Assert.True(metaSkeletons.Count >= 12, $"meta patterns: {metaSkeletons.Count}");
        Assert.True(metaSkeletons.Max(g => g.Count()) <= 6, $"most common meta pattern: {metaSkeletons.Max(g => g.Count())}");
    }

    [Fact]
    public void Bodies_are_far_apart_and_openers_vary()
    {
        var output = Output.Value;
        var prints = output.Select(w => TextModels.SimHash(PersianText.HtmlToText(w.Body))).ToList();
        var distances = new List<int>();
        for (var a = 0; a < prints.Count; a++)
        for (var b = a + 1; b < prints.Count; b++)
            distances.Add(TextModels.Hamming(prints[a], prints[b]));

        // آستانهٔ «تکراری» در تحلیل‌گر فاصلهٔ ۳ است؛ متن‌ها باید خیلی دورتر باشند.
        Assert.True(distances.Min() >= 8, $"min distance {distances.Min()}");
        Assert.True(distances.Average() >= 18, $"average distance {distances.Average():0.0}");

        // جملهٔ اول متن‌ها با الگوهای مختلف شروع می‌شود (نه یک قالب ثابت برای همه).
        var openers = output
            .Select(w => PersianText.Sentences(PersianText.HtmlToText(w.Body)).First())
            .Select((sentence, i) => string.Join(' ', PhraseUsage.Skeleton(sentence, Terms(output[i])).Split(' ').Take(4)))
            .Distinct(StringComparer.Ordinal).Count();
        Assert.True(openers >= 5, $"distinct openers: {openers}");

        // پاراگراف «خرید» همه یکسان نیست.
        var buy = output.Select(w => PhraseUsage.Skeleton(PersianText.HtmlToText(w.Body.Split("<h2>").Last()), Terms(w)))
            .Distinct(StringComparer.Ordinal).Count();
        Assert.True(buy >= 15, $"distinct buy paragraphs: {buy}");
    }

    [Fact]
    public void Faq_is_not_the_same_for_every_sibling()
    {
        var output = Output.Value;
        Assert.All(output, w => Assert.InRange(w.Faq.Count, 2, 6));
        var sets = output.Select(w => string.Join("\n", w.Faq.Select(f => PhraseUsage.Skeleton(f.Question + " " + f.Answer, Terms(w)))))
            .Distinct(StringComparer.Ordinal).Count();
        Assert.True(sets >= 25, $"distinct FAQ sets: {sets}");
        // پرسش‌ها در یک صفحه تکراری نیستند.
        Assert.All(output, w => Assert.Equal(w.Faq.Count, w.Faq.Select(f => f.Question).Distinct().Count()));
    }

    // ── کیفیت سئو ─────────────────────────────────────────────────────

    [Fact]
    public void Generated_pages_have_healthy_density_and_readability()
    {
        foreach (var w in Output.Value)
        {
            var report = SeoAnalyzer.Analyze(w.Subject, w.Site, Store);
            Assert.InRange(report.Density, 0.5, 2.5);
            Assert.DoesNotContain(report.Checks, c => c.Level == SeoCheckLevel.Bad && c.Id is "keyword-density" or "sentence-length" or "paragraph-length" or "keyword-intro" or "keyword-heading" or "subheadings" or "duplicate-content" or "title-unique" or "meta-unique");
            Assert.True(report.Words >= 200, $"{w.Subject.Name}: {report.Words} words");
        }
    }

    [Fact]
    public void Output_has_no_leftover_placeholders_or_banned_claims()
    {
        string[] banned = ["اصل", "بهترین", "ارزان‌ترین", "ارزانترین", "تضمینی", "اورجینال"];
        foreach (var w in Output.Value)
        {
            var all = string.Join("\n", new[] { w.Title, w.Meta, PersianText.HtmlToText(w.Body) }.Concat(w.Faq.Select(f => f.Question + " " + f.Answer)));
            Assert.DoesNotContain("{", all);
            Assert.DoesNotContain("}", all);
            Assert.DoesNotContain("[", all);
            Assert.DoesNotContain("|]", all);
            Assert.DoesNotContain("  ", all);
            var tokens = PersianText.Tokenize(all).ToHashSet(StringComparer.Ordinal);
            foreach (var word in banned)
                Assert.DoesNotContain(PersianText.Normalize(word), tokens);
        }
    }

    [Fact]
    public void Writing_is_deterministic()
    {
        var first = WriteAll(Siblings(8));
        var second = WriteAll(Siblings(8));
        for (var i = 0; i < first.Count; i++)
        {
            Assert.Equal(first[i].Title, second[i].Title);
            Assert.Equal(first[i].Meta, second[i].Meta);
            Assert.Equal(first[i].Body, second[i].Body);
            Assert.Equal(first[i].Faq.Select(f => f.Answer), second[i].Faq.Select(f => f.Answer));
        }
    }

    [Fact]
    public void Brackets_in_feature_values_do_not_break_templates()
    {
        var subject = new SeoSubject
        {
            Kind = SeoEntityKind.Product,
            EntityId = Guid.Parse("00000000-0000-4000-8000-000000000999"),
            Name = "هدفون بی‌سیم تست مدل B1",
            Slug = "b1",
            PathPrefix = "/product/",
            DefaultTitle = "هدفون بی‌سیم تست مدل B1",
            CreatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            Product = new ProductFacts
            {
                CategoryName = "هدفون بی‌سیم",
                BrandName = "تست",
                InStock = true,
                Features = [new("رنگ", "مشکی [ویژه|محدود]"), new("نسخه بلوتوث", "{5.3}"), new("شارژدهی", "30 ساعت")],
            },
        };
        var ctx = new WriterContext { Subject = subject, Store = Store, Keyword = KeywordPlanner.ShortName(subject.Name) };
        var all = ContentWriter.BestMeta(ctx) + ContentWriter.Body(ctx) + string.Concat(ContentWriter.Faq(ctx).Select(f => f.Answer));
        Assert.DoesNotContain("[", all);
        Assert.DoesNotContain("{", all);
        Assert.Contains("30 ساعت", all);
    }

    // ── اجزای سازنده ──────────────────────────────────────────────────

    [Fact]
    public void Spin_expands_alternatives_and_optional_parts()
    {
        var all = Spin.Expand("[خرید|سفارش] {L} [آنلاین|]");
        Assert.Equal(4, all.Count);
        Assert.Contains("خرید {L} آنلاین", all);
        Assert.Contains("سفارش {L}", all);

        Assert.Equal(["متن ساده"], Spin.Expand("متن ساده"));
        Assert.Equal(["باز [نشده"], Spin.Expand("باز [نشده"));
        Assert.Equal(64, Spin.Expand("[a|b] [c|d] [e|f] [g|h] [i|j] [k|l] [m|n] [o|p]").Count);
        Assert.Equal("سلام، دنیا.", Spin.Tidy("سلام ،  دنیا ."));
    }

    [Fact]
    public void SpecKnowledge_reads_and_explains_common_specs()
    {
        var battery = SpecKnowledge.Read(new SeoFeature("ظرفیت باتری", "5000 میلی‌آمپر ساعت"));
        Assert.NotNull(battery);
        Assert.Equal(SpecTopic.Battery, battery!.Topic);
        Assert.Equal(5000, battery.Number);
        Assert.NotNull(SpecKnowledge.Audience(battery));

        var ip = SpecKnowledge.IpMeaning("IP68");
        Assert.NotNull(ip);
        Assert.Equal("IP68", ip!.Value.Code);
        Assert.Contains("غوطه‌وری", ip.Value.Meaning);

        Assert.Null(SpecKnowledge.Read(new SeoFeature("حذف نویز", "ندارد")));
        Assert.Equal(5.3, SpecKnowledge.Number("۵٫۳"));
        Assert.Equal("a (b) c", SpecKnowledge.Safe("a [b] c"));

        var anc = SpecKnowledge.Read(new SeoFeature("نویز کنسلینگ", "دارد"))!;
        Assert.Equal(SpecTopic.NoiseCancel, anc.Topic);
        Assert.NotEmpty(SpecKnowledge.Explain(anc));
        Assert.NotNull(SpecKnowledge.Highlight(anc));
    }

    [Fact]
    public void Phrase_usage_marks_templates_used_by_many_pages()
    {
        var usage = new PhraseUsage();
        var others = Enumerable.Range(0, 10).Select(i => Guid.Parse($"00000000-0000-4000-8000-{i + 1:000000000000}")).ToList();
        for (var i = 0; i < others.Count; i++)
        {
            var name = $"هدفون مدل Z{i}";
            usage.Register(others[i], SeoEntityKind.Product, [$"قیمت و خرید {name} با ارسال سریع"], [name]);
        }

        var self = Guid.NewGuid();
        var common = usage.Commonness(SeoEntityKind.Product, "قیمت و خرید هدفون مدل Q1 با ارسال سریع", ["هدفون مدل Q1"], self);
        var fresh = usage.Commonness(SeoEntityKind.Product, "هدفون مدل Q1 با حذف نویز فعال و شارژدهی طولانی", ["هدفون مدل Q1"], self);
        Assert.True(common > 0.6, $"common = {common}");
        Assert.True(fresh < common, $"fresh = {fresh}, common = {common}");

        // حذف صفحه‌ها اثرشان را برمی‌دارد.
        foreach (var id in others)
            usage.Unregister(id);
        Assert.Equal(0, usage.Commonness(SeoEntityKind.Product, "قیمت و خرید هدفون مدل Q1 با ارسال سریع", ["هدفون مدل Q1"], self));
    }
}
