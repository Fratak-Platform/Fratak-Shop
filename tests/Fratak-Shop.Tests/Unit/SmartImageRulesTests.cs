using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Core.Services.Smart;
using Fratak_Shop.Data.Entities.Smart;
using Fratak_Shop.Web.Infrastructure.Smart;
using EditPage = Fratak_Shop.Web.Areas.Admin.Pages.Products;
using SmartPage = Fratak_Shop.Web.Areas.Admin.Pages.SmartContent;

namespace Fratak_Shop.Tests.Unit;

/// <summary>
/// تست‌های دور بیستم برای «عکس درست»، منابع بیشتر و اعلان نتیجه (بدون دیتابیس و بدون اینترنت):
/// <list type="bullet">
/// <item>خواندن داده از صفحه‌های فروشگاه‌ها: JSON-LD (آرایه، ImageObject، @graph، mainEntity، آدرس نسبی)، dl/dt/dd و کارت‌های بینگ با عنوان.</item>
/// <item>فهرست سیاه عکس: آدرس با شکل‌های مختلف یکی حساب می‌شود و عکس شبیه (dHash) هم بسته است.</item>
/// <item>امتیاز منبع از تصمیم‌های کارشناس (هموارسازی لاپلاس) و رتبه‌بندی نامزدها.</item>
/// <item>جدا کردن عکس‌های تأییدشده/ردشده (با دست‌کاری فرم) و بازخورد یادگیری.</item>
/// <item>حذف عکس در ویرایش محصول «بر اساس نام» (نه بر اساس جایگاه).</item>
/// <item>لینک، عنوان و متن اعلان نتیجهٔ کارهای هوشمند و محدودسازی تنظیمات تازه.</item>
/// </list>
/// </summary>
[Trait("Category", "Unit")]
[Trait("Area", "Smart")]
public class SmartImageRulesTests
{
    // ── خواندن صفحه‌ها ────────────────────────────────────────────────

    [Fact]
    public void JsonLd_images_are_read_from_arrays_image_objects_graph_and_relative_urls()
    {
        const string html = """
            <html><head>
            <script type="application/ld+json">{"@context":"https://schema.org","@graph":[{"@type":"WebPage"},
              {"@type":"Product","name":"هدفون","image":["https://cdn.shop.ir/a.jpg",{"@type":"ImageObject","url":"/media/b.webp"}]}]}</script>
            <script type='application/ld+json'>{"@type":"WebPage","mainEntity":{"@type":"Product","image":{"@type":"ImageObject","contentUrl":"//cdn.shop.ir/c.png"}}}</script>
            <script type="application/ld+json">{"@type":"Organization","logo":"https://shop.ir/logo.png","image":"https://shop.ir/not-a-product.jpg"}</script>
            <script type="application/ld+json">{"@type":"Product","image":"data:image/png;base64,AAAA"}</script>
            <script type="application/ld+json">{ broken json </script>
            </head></html>
            """;
        var images = SmartParsers.JsonLdImages(html, "https://shop.ir/p/1");

        Assert.Equal(["https://cdn.shop.ir/a.jpg", "https://shop.ir/media/b.webp", "https://cdn.shop.ir/c.png"], images);
        Assert.Empty(SmartParsers.JsonLdImages(null));
        Assert.Empty(SmartParsers.JsonLdImages("<p>بدون داده</p>"));
    }

    [Fact]
    public void Specs_are_read_from_definition_lists_and_json_ld_properties()
    {
        const string html = """
            <dl><dt>وزن:</dt><dd>45 گرم</dd><dt>نسخه بلوتوث</dt><dd><span>5.3</span></dd></dl>
            <script type="application/ld+json">{"@type":"Product","additionalProperty":[
              {"@type":"PropertyValue","name":"شارژدهی","value":"30 ساعت"},
              {"@type":"PropertyValue","name":"تعداد میکروفون","value":4},
              {"@type":"PropertyValue","name":"نویز کنسلینگ","value":true},
              {"@type":"PropertyValue","name":"","value":"بی‌نام"}]}</script>
            """;
        var specs = SmartParsers.HtmlSpecs(html);

        Assert.Contains(("وزن", "45 گرم"), specs);
        Assert.Contains(("نسخه بلوتوث", "5.3"), specs);
        Assert.Contains(("شارژدهی", "30 ساعت"), specs);
        Assert.Contains(("تعداد میکروفون", "4"), specs);
        Assert.Contains(("نویز کنسلینگ", "دارد"), specs);
        Assert.DoesNotContain(specs, s => s.Value == "بی‌نام");

        var jsonOnly = SmartParsers.JsonLdSpecs(html);
        Assert.Equal(3, jsonOnly.Count); // ردیف بی‌نام کنار گذاشته می‌شود
    }

    [Fact]
    public void Attack_markup_in_json_ld_specs_stays_plain_text()
    {
        const string html = """<script type="application/ld+json">{"@type":"Product","additionalProperty":[{"name":"رنگ","value":"<img src=x onerror=alert(1)>مشکی"}]}</script>""";
        foreach (var (key, value) in SmartParsers.HtmlSpecs(html))
        {
            Assert.DoesNotContain("<", key + value);
            Assert.DoesNotContain(">", key + value);
        }
    }

    [Fact]
    public void Bing_cards_carry_title_and_source_page_and_unsafe_urls_are_dropped()
    {
        const string html = """
            <a class="iusc" m="{&quot;murl&quot;:&quot;https://img.shop.ir/t15.jpg?w=300&quot;,&quot;purl&quot;:&quot;https://www.shop.ir/p/t15&quot;,&quot;t&quot;:&quot;هدفون &lt;b&gt;هایلو&lt;/b&gt; T15&quot;}" href="#">x</a>
            <a class="iusc" m="{&quot;murl&quot;:&quot;https://img.other.ir/a.svg&quot;,&quot;t&quot;:&quot;svg&quot;}" href="#">x</a>
            <a class="iusc" m="{&quot;murl&quot;:&quot;javascript:alert(1)&quot;,&quot;t&quot;:&quot;x&quot;}" href="#">x</a>
            <a class="iusc" m="{&quot;murl&quot;:&quot;https://img.shop.ir/t15.jpg?w=300&quot;,&quot;t&quot;:&quot;تکراری&quot;}" href="#">x</a>
            <a class="iusc" m="{&quot;murl&quot;:&quot;https://img.x.ir/n.jpg&quot;,&quot;purl&quot;:&quot;ftp://x.ir/&quot;}" href="#">x</a>
            """;
        var hits = SmartParsers.BingImageResults(html);

        Assert.Equal(2, hits.Count);
        Assert.Equal("https://img.shop.ir/t15.jpg?w=300", hits[0].Url);
        Assert.Equal("https://www.shop.ir/p/t15", hits[0].PageUrl);
        Assert.Equal("هدفون هایلو T15", hits[0].Title);
        Assert.Null(hits[1].Title);
        Assert.Null(hits[1].PageUrl); // فقط http/https
        Assert.Equal(hits.Select(h => h.Url), SmartParsers.BingImages(html));
        Assert.Empty(SmartParsers.BingImageResults(null));
    }

    // ── فهرست سیاه و امتیاز منبع ──────────────────────────────────────

    [Fact]
    public void Url_key_treats_equivalent_urls_as_one_image()
    {
        var key = SmartImageRules.UrlKey("https://dkstatics-public.digikala.com/a.jpg");
        Assert.Equal(key, SmartImageRules.UrlKey("//dkstatics-public.digikala.com/a.jpg"));
        Assert.Equal(key, SmartImageRules.UrlKey("https://DKSTATICS-public.digikala.com/a.jpg?x-oss-process=image/resize,h_300"));
        Assert.Equal(key, SmartImageRules.UrlKey("  https://dkstatics-public.digikala.com/a.jpg  "));
        Assert.NotEqual(key, SmartImageRules.UrlKey("https://dkstatics-public.digikala.com/b.jpg"));
        Assert.Equal(64, key.Length);
        Assert.Equal(64, SmartImageRules.UrlKey("not a url").Length); // آدرس نامعتبر هم کلید ثابت دارد
    }

    [Fact]
    public void Blacklist_blocks_rejected_urls_and_near_identical_images_only()
    {
        const ulong rejectedHash = 0xF0F0_F0F0_0F0F_0F0FUL;
        var blacklist = new ImageBlacklist([SmartImageRules.UrlKey("https://a.ir/x.jpg"), "", " "], [unchecked((long)rejectedHash), 0]);

        Assert.Equal(2, blacklist.Count); // کلید خالی و هش صفر شمرده نمی‌شوند
        Assert.True(blacklist.BlocksUrl("//a.ir/x.jpg"));
        Assert.False(blacklist.BlocksUrl("https://a.ir/y.jpg"));
        Assert.False(blacklist.BlocksUrl(null));
        Assert.True(blacklist.BlocksHash(rejectedHash));
        Assert.True(blacklist.BlocksHash(rejectedHash ^ 0b11_1111UL));          // ۶ بیت تفاوت = همان عکس (کمی فشرده/کوچک‌شده)
        Assert.False(blacklist.BlocksHash(rejectedHash ^ 0b111_1111_1111UL));  // ۱۱ بیت = عکس دیگر
        Assert.False(blacklist.BlocksHash(0));                                   // نامعلوم ← بسته نیست
        Assert.Equal(0, ImageBlacklist.Empty.Count);
    }

    [Theory]
    [InlineData(0, 0, 0.5)]
    [InlineData(1, 1, 0.6667)]
    [InlineData(0, 1, 0.3333)]
    [InlineData(9, 10, 0.8333)]
    [InlineData(0, 8, 0.1)]
    [InlineData(50, 10, 0.9167)] // دست‌کاری: تأیید بیشتر از کل ← سقف کل
    [InlineData(-5, 10, 0.0833)]
    public void Source_score_uses_laplace_smoothing(int approved, int total, double expected) =>
        Assert.Equal(expected, SmartImageRules.SourceScore(approved, total), 4);

    [Theory]
    [InlineData("serp", "https://www.Mobile.ir/p/1", "serp:mobile.ir")]
    [InlineData("bing", "https://img.shop.ir/a.jpg", "bing:img.shop.ir")]
    [InlineData("bing", "not a url", "bing")]
    [InlineData("Digikala", "https://digikala.com/x", "digikala")]
    [InlineData("serp:already.ir", "https://other.ir", "serp:already.ir")]
    [InlineData("", null, "unknown")]
    public void Source_key_separates_each_shop(string source, string? url, string expected) =>
        Assert.Equal(expected, SmartImageRules.SourceKey(source, url));

    [Fact]
    public void Learned_scores_override_defaults_and_rank_orders_candidates()
    {
        Assert.Equal(0.7, SmartImageRules.ScoreOf("digikala", null));
        Assert.Equal(0.55, SmartImageRules.ScoreOf("serp:mobile.ir", null));
        Assert.Equal(0.45, SmartImageRules.ScoreOf("bing:img.ir", null));
        var learned = new Dictionary<string, double> { ["digikala"] = 0.2, ["bing:good.ir"] = 0.95 };
        Assert.Equal(0.2, SmartImageRules.ScoreOf("digikala", learned));

        // امتیاز = ۶۰٪ سابقهٔ منبع + ۴۰٪ شباهت عنوان + ۰٫۱ برای «همان کالا».
        Assert.Equal(0.98, new ImageCandidate("https://d.ir/1.jpg", "digikala", true, 1).Score(new Dictionary<string, double> { ["digikala"] = 0.8 }));
        Assert.Equal(0.47, new ImageCandidate("https://b.ir/1.jpg", "bing:b.ir", false, 0.5).Score(null));
        Assert.Equal(0.67, new ImageCandidate("https://b.ir/1.jpg", "bing:b.ir", false, 5).Score(null)); // شباهت بیش از ۱ ← ۱

        var blacklist = new ImageBlacklist([SmartImageRules.UrlKey("https://bad.ir/rejected.jpg")], []);
        var ranked = SmartImageFinder.Rank(
        [
            new ImageCandidate("https://bing.ir/1.jpg", "bing:bing.ir", false, 0.6),
            new ImageCandidate("https://good.ir/1.jpg", "bing:good.ir", false, 0.6),
            new ImageCandidate("https://bad.ir/rejected.jpg", "digikala", true, 1),            // کارشناس رد کرده ← هرگز
            new ImageCandidate("https://d.ir/1.jpg?x-oss-process=image/resize,h_300", "digikala", true, 0.9),
            new ImageCandidate("//d.ir/1.jpg", "bing:d.ir", false, 0.5),                       // همان آدرس با امتیاز کمتر ← حذف
            new ImageCandidate("javascript:alert(1)", "digikala", true, 1),                   // آدرس ناامن ← حذف
        ], blacklist, learned, 10);

        Assert.Equal(["https://good.ir/1.jpg", "https://d.ir/1.jpg?x-oss-process=image/resize,h_300", "https://bing.ir/1.jpg"], ranked.Select(c => c.Url));
        Assert.Single(SmartImageFinder.Rank(ranked, ImageBlacklist.Empty, learned, 1));
        Assert.Single(SmartImageFinder.Rank(ranked, ImageBlacklist.Empty, learned, 0)); // سقف حداقل ۱
    }

    // ── انتخاب/رد عکس‌ها در صفحهٔ بازبینی ───────────────────────────────

    private static FoundImage Found(string name, string? url = null, long hash = 0, string source = "digikala") =>
        new(name, 800, 800, source, true, url, hash);

    [Fact]
    public void Split_images_ignores_tampered_names_and_null_means_reject_all()
    {
        var pending = new List<FoundImage> { Found("a.webp"), Found("b.webp"), Found("c.webp"), Found("b.webp") };

        var (approved, rejected) = SmartPage.ProductModel.SplitImages(pending, ["b.webp", "../../etc/passwd", "x.webp", "b.webp"]);
        Assert.Equal(["b.webp"], approved.Select(i => i.FileName));
        Assert.Equal(["a.webp", "c.webp"], rejected.Select(i => i.FileName));

        var (none, all) = SmartPage.ProductModel.SplitImages(pending, null);
        Assert.Empty(none);
        Assert.Equal(3, all.Count);

        var (everything, nothing) = SmartPage.ProductModel.SplitImages(pending, ["a.webp", "b.webp", "c.webp"]);
        Assert.Equal(3, everything.Count);
        Assert.Empty(nothing);
    }

    [Fact]
    public void Feedback_keeps_only_images_with_a_url_or_fingerprint()
    {
        var feedback = SmartPage.ProductModel.Feedback(
            [Found("a.webp", "https://a.ir/1.jpg", 11, "serp:a.ir")],
            [Found("b.webp", null, 22), Found("c.webp")]);

        Assert.Equal(2, feedback.Count);
        Assert.Contains(feedback, f => f is { Url: "https://a.ir/1.jpg", Hash: 11, Source: "serp:a.ir", Approved: true });
        Assert.Contains(feedback, f => f is { Url: null, Hash: 22, Approved: false });
    }

    // ── حذف عکس در ویرایش محصول ───────────────────────────────────────

    [Fact]
    public void Removed_names_merge_the_new_list_and_the_legacy_checkbox()
    {
        var input = new EditPage.ProductInput
        {
            Images =
            [
                new EditPage.ProductImageInput { ImageName = "a.webp", Remove = true },
                new EditPage.ProductImageInput { ImageName = "b.webp" },
                new EditPage.ProductImageInput { ImageName = "", Remove = true },
            ],
            RemovedImages = ["c.webp", " c.webp ", "", "  "],
        };
        var removed = EditPage.EditModel.RemovedNames(input);

        Assert.Equal(2, removed.Count);
        Assert.Contains("a.webp", removed);
        Assert.Contains("c.webp", removed);
        Assert.DoesNotContain("b.webp", removed);
        Assert.Empty(EditPage.EditModel.RemovedNames(new EditPage.ProductInput()));
    }

    // ── اعلان نتیجه ───────────────────────────────────────────────────

    private static ClaimedSmartTask Claimed(SmartTaskKind kind, SmartTarget target, Guid? targetId, string? title = "هدفون X") =>
        new(Guid.Parse("11111111-1111-4111-8111-111111111111"), kind, target, targetId, title, null, 1, SmartPriority.Manual);

    [Fact]
    public void Notification_link_opens_the_exact_review_section()
    {
        var id = Guid.Parse("22222222-2222-4222-8222-222222222222");
        Assert.Equal($"/admin/smart/product/{id}#task-11111111-1111-4111-8111-111111111111",
            SmartNotifier.LinkFor(Claimed(SmartTaskKind.ProductReview, SmartTarget.Product, id)));
        Assert.Equal("/admin/smart", SmartNotifier.LinkFor(Claimed(SmartTaskKind.ProductReview, SmartTarget.Product, null)));
        Assert.Equal("/admin/smart/categories", SmartNotifier.LinkFor(Claimed(SmartTaskKind.CategoryComplete, SmartTarget.Category, id)));
        Assert.Equal("/admin/smart/blog", SmartNotifier.LinkFor(Claimed(SmartTaskKind.BlogArticle, SmartTarget.BlogPlan, id)));
        Assert.Equal($"/admin/blog/edit/{id}", SmartNotifier.LinkFor(Claimed(SmartTaskKind.BlogImage, SmartTarget.BlogPost, id)));
        // همهٔ لینک‌ها داخلی‌اند (هیچ‌وقت آدرس بیرونی یا «//» در اعلان).
        foreach (var target in Enum.GetValues<SmartTarget>())
        {
            var link = SmartNotifier.LinkFor(Claimed(SmartTaskKind.ProductComplete, target, id));
            Assert.StartsWith("/admin/", link, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Notification_title_and_batch_summary_are_short_persian_text()
    {
        Assert.Equal("نقد و بررسی هوشمند: هدفون X – منتظر تأیید شما",
            SmartNotifier.TaskTitle(Claimed(SmartTaskKind.ProductReview, SmartTarget.Product, Guid.NewGuid()), SmartTaskStatus.NeedsReview));
        Assert.Equal("عکس هوشمند – اعمال شد",
            SmartNotifier.TaskTitle(Claimed(SmartTaskKind.ProductImages, SmartTarget.Product, Guid.NewGuid(), " "), SmartTaskStatus.Applied));
        var longTitle = SmartNotifier.TaskTitle(Claimed(SmartTaskKind.ProductComplete, SmartTarget.Product, Guid.NewGuid(), new string('ب', 500)), SmartTaskStatus.Failed);
        Assert.True(longTitle.Length <= 200);

        var mixed = new SmartBatchSummary(20, 14, 5, 1, 0, 0);
        Assert.Equal("۲۰ کار: ۱۴ اعمال شد، ۵ منتظر تأیید شما، ۱ ناموفق", SmartNotifier.BatchBody(mixed));
        Assert.Equal("/admin/smart?Status=NeedsReview", SmartNotifier.BatchLink(mixed));
        Assert.Equal("/admin/smart?Status=Failed", SmartNotifier.BatchLink(new SmartBatchSummary(3, 2, 0, 1, 0, 0)));
        Assert.Equal("/admin/smart", SmartNotifier.BatchLink(new SmartBatchSummary(3, 3, 0, 0, 0, 0)));
        Assert.Equal("۲ کار: ۲ بدون تغییر", SmartNotifier.BatchBody(new SmartBatchSummary(2, 0, 0, 0, 2, 0)));
        Assert.Equal("۰ کار: انجام شد", SmartNotifier.BatchBody(new SmartBatchSummary(0, 0, 0, 0, 0, 0)));
    }

    [Fact]
    public void Notification_goes_to_staff_who_can_act_on_it()
    {
        Assert.Equal(AdminPermissions.CatalogManage, SmartNotifier.PermissionFor(SmartTarget.Category));
        Assert.Equal(AdminPermissions.BlogPublish, SmartNotifier.PermissionFor(SmartTarget.BlogPlan));
        Assert.Equal(AdminPermissions.BlogPublish, SmartNotifier.PermissionFor(SmartTarget.BlogPost));
        Assert.Equal(AdminPermissions.ProductsManage, SmartNotifier.PermissionFor(SmartTarget.Product));
    }

    // ── تنظیمات ───────────────────────────────────────────────────────

    [Fact]
    public void New_smart_options_are_clamped_to_safe_ranges()
    {
        var low = new SmartOptions { ImageCandidates = 1, NightlyCompletePerDay = -5, AutoImagesWhenFewerThan = -1, AutoImagesMax = -3, MaxImageCandidates = 1 }.Normalize();
        Assert.Equal(6, low.ImageCandidates);
        Assert.Equal(0, low.NightlyCompletePerDay);
        Assert.Equal(0, low.AutoImagesWhenFewerThan);
        Assert.Equal(0, low.AutoImagesMax);
        Assert.Equal(6, low.MaxImageCandidates);

        var high = new SmartOptions { ImageCandidates = 99, NightlyCompletePerDay = 100_000, AutoImagesWhenFewerThan = 50, AutoImagesMax = 50, MaxImageCandidates = 500 }.Normalize();
        Assert.Equal(15, high.ImageCandidates);
        Assert.Equal(200, high.NightlyCompletePerDay);
        Assert.Equal(12, high.AutoImagesWhenFewerThan);
        Assert.Equal(8, high.AutoImagesMax);
        Assert.Equal(60, high.MaxImageCandidates);

        var defaults = new SmartOptions();
        Assert.InRange(defaults.ImageCandidates, 10, 15); // ۱۰ تا ۱۵ گزینه برای انتخاب کارشناس
        Assert.True(defaults.NotifyResults);
    }
}
