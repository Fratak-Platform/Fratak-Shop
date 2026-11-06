using Fratak_Shop.Core.Services.Seo.Engine;
using Fratak_Shop.Core.Services.Smart;
using Fratak_Shop.Data.Entities.SeoEngine;
using Fratak_Shop.Web.Infrastructure.Smart;

namespace Fratak_Shop.Tests.Unit;

/// <summary>
/// تست‌های دور بیستم برای «نقد و بررسی» حرفه‌ای محصول (بدون دیتابیس و بدون اینترنت).
/// <para>سناریو مثل اجرای واقعی است: ۳۰ هدفون هم‌دسته (برند و مشخصات متفاوت ولی شبیه) پشت سر هم نوشته می‌شوند و
/// بعد از هر محصول نمای سایت به‌روز می‌شود. انتظار:</para>
/// <list type="bullet">
/// <item>ساختار HTML درست: پاراگراف نشانه‌دار اول، H2/H3، strong، فهرست، جدول مشخصات؛ تگ &lt;u&gt; هرگز.</item>
/// <item>سئوی سالم: کلمهٔ کلیدی در مقدمه و تیتر، تراکم ۰٫۵ تا ۲٫۵ درصد، بدون «محتوای تکراری» و حداقل ۲۰۰ واژه.</item>
/// <item>بدون ادعای ممنوع (اصل/بهترین/تضمینی …) و بدون جای‌نگهدار جامانده ({ } [ |]).</item>
/// <item>قطعی (دو بار اجرا = همان متن) و متن هم‌دسته‌ها از هم دور (SimHash).</item>
/// <item>امنیت: مقدار مشخصه یا نظر خریدار با HTML مخرب، در خروجی فقط به‌صورت متن (encode) می‌آید.</item>
/// <item>تصمیم دستیار دربارهٔ متن فعلی (DecideReview) و جدا کردن بخش انسانی (HumanPart).</item>
/// </list>
/// </summary>
[Trait("Category", "Unit")]
[Trait("Area", "Smart")]
public class SmartReviewTests
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

    /// <summary>یک هدفون با مشخصات قطعی؛ i متفاوت ← برند و مقدارهای متفاوت (بسیاری عمداً تکراری).</summary>
    private static SeoSubject Headphone(int i, List<SeoFeature>? features = null)
    {
        var brand = Brands[i % Brands.Length];
        features ??=
        [
            new("نسخه بلوتوث", $"5.{i % 4}"),
            new("شارژدهی", $"{18 + i % 7 * 4} ساعت"),
        ];
        if (features.Count == 2)
        {
            if (i % 2 == 0) features.Add(new("نویز کنسلینگ", "دارد"));
            if (i % 3 != 1) features.Add(new("مقاومت در برابر آب", i % 3 == 0 ? "IPX5" : "IP54"));
            if (i % 4 == 1) features.Add(new("درگاه شارژ", "USB-C"));
            if (i % 5 == 2) features.Add(new("شارژ بی‌سیم", "دارد"));
            if (i % 6 == 3) features.Add(new("وزن", $"{38 + i % 9} گرم"));
            if (i % 7 == 4) features.Add(new("تعداد میکروفون", $"{2 + i % 5}"));
        }

        var name = $"هدفون بی‌سیم {brand} مدل X{100 + i}";
        return new SeoSubject
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
        };
    }

    private sealed record Written(SeoSubject Subject, string Keyword, string Html, SeoSiteIndex Site);

    /// <summary>نوشتن پشت سر هم مثل اجرای واقعی: بعد از هر صفحه، متن تازه در نمای سایت می‌نشیند (تا یکتایی سنجیده شود).</summary>
    private static List<Written> WriteAll(int count)
    {
        var subjects = Enumerable.Range(0, count).Select(i => Headphone(i)).ToList();
        var site = SeoSiteIndex.Build(subjects);
        var result = new List<Written>();
        foreach (var subject in subjects)
        {
            var keyword = KeywordPlanner.ShortName(subject.Name);
            var ctx = new WriterContext { Subject = subject, Store = Store, Keyword = keyword, Site = site };
            var html = ContentWriter.ProductReview(ctx) ?? "";
            var after = subject.With(new SeoProposal { DescriptionHtml = html, FocusKeyword = keyword }, SeoFields.FocusKeyword | SeoFields.Description);
            site.Replace(subject, after);
            result.Add(new Written(after, keyword, html, site));
        }
        return result;
    }

    private static readonly Lazy<List<Written>> Output = new(() => WriteAll(30));

    // ── ساختار ────────────────────────────────────────────────────────

    [Fact]
    public void Review_is_structured_html_with_headings_lists_table_and_strong_keyword()
    {
        foreach (var w in Output.Value)
        {
            Assert.False(string.IsNullOrWhiteSpace(w.Html), w.Subject.Name);
            Assert.StartsWith("<p " + ContentWriter.ReviewMarkerAttribute + ">", w.Html, StringComparison.Ordinal);
            Assert.Contains("<h2>", w.Html, StringComparison.Ordinal);
            Assert.Contains("<h3>", w.Html, StringComparison.Ordinal);
            Assert.Contains("<strong>", w.Html, StringComparison.Ordinal);
            Assert.Contains("<ul>", w.Html, StringComparison.Ordinal);
            Assert.Contains("<table class=\"fs-spec-table\">", w.Html, StringComparison.Ordinal);
            Assert.Contains("fs-review-box", w.Html, StringComparison.Ordinal);
            Assert.Contains("fs-proscons", w.Html, StringComparison.Ordinal);
            // <u> عمداً هرگز (با لینک اشتباه گرفته می‌شود و اثر سئویی ندارد)؛ تگ‌های قدیمی b/i هم نه.
            Assert.DoesNotContain("<u>", w.Html, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("<b>", w.Html, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("<i>", w.Html, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("<h1", w.Html, StringComparison.OrdinalIgnoreCase); // H1 مال عنوان صفحه است
            // تگ‌های باز و بسته برابرند (HTML خراب به صفحه نمی‌رود).
            foreach (var tag in new[] { "p", "h2", "h3", "ul", "li", "table", "tr", "strong", "em", "div" })
                Assert.Equal(Count(w.Html, "<" + tag + ">") + Count(w.Html, "<" + tag + " "), Count(w.Html, "</" + tag + ">"));
            // هیچ نشانهٔ داخلی (نویسه‌های استفادهٔ خصوصی یونیکد) جا نمانده است.
            Assert.False(w.Html.Any(ch => ch is >= '\uE000' and <= '\uF8FF'), "private-use marker left in output");
        }
    }

    [Fact]
    public void Review_has_healthy_seo_signals_and_no_duplicate_content()
    {
        foreach (var w in Output.Value)
        {
            var report = SeoAnalyzer.Analyze(w.Subject, w.Site, Store);
            Assert.True(report.Words >= 200, $"{w.Subject.Name}: {report.Words} words");
            Assert.InRange(report.Density, 0.5, 2.5);
            Assert.DoesNotContain(report.Checks, c => c.Level == SeoCheckLevel.Bad
                && c.Id is "keyword-density" or "keyword-intro" or "keyword-heading" or "subheadings" or "duplicate-content");
        }
    }

    [Fact]
    public void Review_has_no_placeholders_or_banned_claims()
    {
        string[] banned = ["اصل", "بهترین", "ارزان‌ترین", "ارزانترین", "تضمینی", "اورجینال"];
        foreach (var w in Output.Value)
        {
            var text = PersianText.HtmlToText(w.Html);
            Assert.DoesNotContain("{", text);
            Assert.DoesNotContain("}", text);
            Assert.DoesNotContain("[", text);
            Assert.DoesNotContain("|]", text);
            Assert.DoesNotContain("  ", text);
            var tokens = PersianText.Tokenize(text).ToHashSet(StringComparer.Ordinal);
            foreach (var word in banned)
                Assert.DoesNotContain(PersianText.Normalize(word), tokens);
        }
    }

    [Fact]
    public void Reviews_of_siblings_are_far_apart()
    {
        var prints = Output.Value.Select(w => TextModels.SimHash(PersianText.HtmlToText(w.Html))).ToList();
        var distances = new List<int>();
        for (var a = 0; a < prints.Count; a++)
        for (var b = a + 1; b < prints.Count; b++)
            distances.Add(TextModels.Hamming(prints[a], prints[b]));
        // آستانهٔ «تکراری» تحلیل‌گر فاصلهٔ ۳ است؛ بررسی‌ها با وجود ساختار مشترک باید دورتر باشند.
        Assert.True(distances.Min() > 3, $"min distance {distances.Min()}");
        Assert.True(distances.Average() >= 10, $"average distance {distances.Average():0.0}");
        Assert.Equal(30, Output.Value.Select(w => w.Html).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Review_writing_is_deterministic()
    {
        var first = WriteAll(6);
        var second = WriteAll(6);
        Assert.Equal(first.Select(w => w.Html), second.Select(w => w.Html));
    }

    // ── شرط‌ها و ورودی‌های ویژه ───────────────────────────────────────

    [Fact]
    public void Review_is_refused_without_enough_data_or_for_non_products()
    {
        // کمتر از ۲ مشخصه ← متن بدون داده نوشته نمی‌شود (محتوای کم‌ارزش = ریسک جریمه).
        var thin = Headphone(1, [new SeoFeature("رنگ", "مشکی")]);
        Assert.Null(ContentWriter.ProductReview(new WriterContext { Subject = thin, Store = Store, Keyword = KeywordPlanner.ShortName(thin.Name) }));

        var category = new SeoSubject
        {
            Kind = SeoEntityKind.Category,
            EntityId = Guid.NewGuid(),
            Name = "هدفون بی‌سیم",
            Slug = "wireless-headphones",
            PathPrefix = "/category/",
            DefaultTitle = "هدفون بی‌سیم",
            CreatedAt = DateTimeOffset.UnixEpoch,
        };
        Assert.Null(ContentWriter.ProductReview(new WriterContext { Subject = category, Store = Store, Keyword = "هدفون بی‌سیم" }));
    }

    [Fact]
    public void Existing_text_stays_on_top_and_buyer_notes_are_used()
    {
        var subject = Headphone(4);
        var notes = new ReviewNotes
        {
            Pros = ["صدای بم قوی و شفاف", "اتصال سریع به گوشی"],
            Cons = ["قاب شارژ کمی بزرگ است"],
            Verdict = "برای استفادهٔ روزمره و ورزش ارزش خرید دارد",
            ReviewCount = 12,
        };
        const string human = "<p>متن کارشناس فروشگاه دربارهٔ این هدفون.</p>";
        var ctx = new WriterContext { Subject = subject, Store = Store, Keyword = KeywordPlanner.ShortName(subject.Name), Notes = notes };
        var html = ContentWriter.ProductReview(ctx, human)!;

        Assert.StartsWith(human, html, StringComparison.Ordinal);
        Assert.Contains(ContentWriter.ReviewMarkerAttribute, html, StringComparison.Ordinal);
        Assert.Contains("صدای بم قوی و شفاف", html, StringComparison.Ordinal);
        Assert.Contains("قاب شارژ کمی بزرگ است", html, StringComparison.Ordinal);
        // بخش انسانی دقیقاً قابل جدا کردن است (برای بازنویسی بعدی بدون از دست رفتن متن کارشناس).
        Assert.Equal(human, ContentWriter.HumanPart(html));
    }

    [Fact]
    public void Attack_html_in_specs_or_buyer_notes_is_never_rendered_as_markup()
    {
        var subject = Headphone(2,
        [
            new("نسخه بلوتوث", "5.3<script>alert(1)</script>"),
            new("شارژدهی", "30 ساعت\"><img src=x onerror=alert(1)>"),
            new("نویز کنسلینگ", "دارد"),
        ]);
        var notes = new ReviewNotes
        {
            Pros = ["<img src=x onerror=alert(2)> صدای خوب"],
            Cons = ["<a href=\"javascript:alert(3)\">لینک</a> قاب بزرگ"],
            Verdict = "<script>steal()</script> خرید خوبی بود و راضی هستم",
            ReviewCount = 5,
        };
        var html = ContentWriter.ProductReview(new WriterContext { Subject = subject, Store = Store, Keyword = KeywordPlanner.ShortName(subject.Name), Notes = notes });

        Assert.NotNull(html);
        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<img", html, StringComparison.OrdinalIgnoreCase);
        // فقط لینک‌های داخلی فروشگاه (دسته/برند) در متن هست.
        foreach (var part in html.Split("<a ", StringSplitOptions.None).Skip(1))
            Assert.StartsWith("href=\"/", part, StringComparison.Ordinal);
    }

    // ── بخش انسانی و تصمیم دستیار ─────────────────────────────────────

    [Theory]
    [InlineData(null, null)]
    [InlineData("  ", null)]
    [InlineData("<p>فقط متن کارشناس</p>", "<p>فقط متن کارشناس</p>")]
    [InlineData("<p data-fs=\"review\">نوشتهٔ دستیار</p><h2>x</h2>", null)]
    [InlineData("<p>متن کارشناس</p>\n<p data-fs=\"review\">نوشتهٔ دستیار</p>", "<p>متن کارشناس</p>")]
    public void HumanPart_keeps_only_the_text_above_the_smart_review(string? html, string? expected) =>
        Assert.Equal(expected, ContentWriter.HumanPart(html));

    [Theory]
    [InlineData(true, true, true, 500, ReviewAction.Skip)]       // کارشناس برگردانده ← هرگز دوباره
    [InlineData(false, true, true, 500, ReviewAction.Regenerate)] // متن خود دستیار ← به‌روزرسانی
    [InlineData(false, false, true, 500, ReviewAction.Skip)]      // نشانه هست ولی ویرایش شده ← دست نزن
    [InlineData(false, false, false, 20, ReviewAction.Write)]     // متن کم ← بررسی تازه
    [InlineData(false, false, false, 149, ReviewAction.Write)]
    [InlineData(false, false, false, 150, ReviewAction.Propose)]  // متن انسانی بلند ← فقط پیشنهاد
    public void DecideReview_respects_human_decisions_first(bool rolledBack, bool writtenBySmart, bool hasMarker, int words, ReviewAction expected)
    {
        var (action, reason) = SmartOrchestrator.DecideReview(new ProductBodyState("<p>x</p>", words, writtenBySmart, hasMarker, rolledBack));
        Assert.Equal(expected, action);
        Assert.False(string.IsNullOrWhiteSpace(reason));
    }

    [Theory]
    [InlineData("{\"round\":2}", 2)]
    [InlineData("{\"round\":\"2\"}", 0)]
    [InlineData("{\"other\":5}", 0)]
    [InlineData("[1,2]", 0)]
    [InlineData("not json", 0)]
    [InlineData("", 0)]
    [InlineData(null, 0)]
    [InlineData("{\"round\":99999999999}", 0)]
    public void PayloadInt_reads_numbers_safely(string? json, int expected) =>
        Assert.Equal(expected, SmartOrchestrator.PayloadInt(json, "round"));

    private static int Count(string text, string part)
    {
        var count = 0;
        for (var at = text.IndexOf(part, StringComparison.Ordinal); at >= 0; at = text.IndexOf(part, at + part.Length, StringComparison.Ordinal))
            count++;
        return count;
    }
}
