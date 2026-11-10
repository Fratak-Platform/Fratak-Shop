using System.Globalization;
using Fratak_Shop.Core.DTOs.Banners.Query;
using Fratak_Shop.Core.DTOs.Categories.Query;
using Fratak_Shop.Core.Services.Categories;
using Fratak_Shop.Core.Services.Design;
using Fratak_Shop.Core.Services.Marketing;
using Fratak_Shop.Core.Services.Smart;
using Fratak_Shop.Data.Entities.Marketing;
using Fratak_Shop.Web.Helpers;
using Fratak_Shop.Web.Infrastructure.Design;
using SkiaSharp;

namespace Fratak_Shop.Tests.Unit;

/// <summary>
/// تست‌های واحد دور ۲۳ (بدون پایگاه داده) برای قانون‌های تازه:
/// <list type="bullet">
/// <item>دروازهٔ کیفیت محتوا: «دسته‌بندی نشده»، لینک خالی/سیستمی، قالب پرنشده، مقدار خام و جملهٔ تکراری؛ پاک‌سازی فقط بخش ایرادار.</item>
/// <item>خانواده و آیکون دسته‌ها: تطبیق دقیق، آیکون برای همهٔ نوع‌های کالا و وجود آن آیکون در مجموعهٔ سایت.</item>
/// <item>منوی دسته‌ها: دستهٔ سیستمی/خالی/کم‌محتوا پنهان، ترتیب با فروش واقعی، منوی فروشگاه تازه خالی نمی‌ماند.</item>
/// <item>پرسش‌های متداول دسته از دادهٔ واقعی؛ دستهٔ بی‌کالا پرسش ندارد.</item>
/// <item>بنر: A/B منصفانه، تقویم سوگواری (قمری و شمسی)، گروه نسخه.</item>
/// <item>رنگ: کنتراست WCAG، پالت خوانا از رنگ عکس، رنگ غالب عکس و برش زمینهٔ سفید.</item>
/// <item>«ایده ← طرح»: نوع ایده، صداقت در درصد تخفیف، ۳ نسخه با یک گروه A/B.</item>
/// </list>
/// </summary>
[Trait("Category", "Unit")]
[Trait("Area", "Round23")]
public class Round23RulesTests
{
    // ── دروازهٔ کیفیت محتوا ─────────────────────────────────────

    [Fact]
    public void Quality_gate_flags_uncategorized_text_and_bad_links()
    {
        Assert.NotEmpty(ContentQualityGate.Issues("برای دیدن کالاهای دیگر صفحهٔ دسته‌بندی نشده را ببینید."));
        Assert.True(ContentQualityGate.HasForbidden("دسته بندی نشده"));
        Assert.True(ContentQualityGate.HasForbidden("Uncategorized items"));
        Assert.True(ContentQualityGate.HasForbidden("<a href=\"/category/uncategorized\">x</a>"));
        Assert.NotEmpty(ContentQualityGate.Issues("<p><a href=\"\">لینک</a> خالی</p>"));
        Assert.NotEmpty(ContentQualityGate.Issues("<p>{LINKS}</p>"));
        Assert.True(ContentQualityGate.IsClean("هدفون بی‌سیم با ۳۰ ساعت شارژ."));
        Assert.Empty(ContentQualityGate.Issues(null));
    }

    [Fact]
    public void Quality_gate_style_checks_can_be_skipped()
    {
        Assert.NotEmpty(ContentQualityGate.Issues("قیمت: null"));
        Assert.Empty(ContentQualityGate.Issues("قیمت: null", style: false));
        const string sentence = "این کالا برای استفادهٔ روزانه در سفر کاملاً مناسب است.";
        Assert.NotNull(ContentQualityGate.RepeatedSentence($"<p>{sentence}</p><p>{sentence}</p>"));
        Assert.Null(ContentQualityGate.RepeatedSentence("<p>جملهٔ اول کمی بلند و متفاوت است.</p><p>جملهٔ دوم هم کاملاً چیز دیگری می‌گوید.</p>"));
    }

    [Fact]
    public void Quality_gate_cleans_only_the_broken_parts()
    {
        Assert.Equal("<p>خوب است.</p>", ContentQualityGate.Clean("<p>خوب است.</p><p>صفحهٔ دسته‌بندی نشده را ببینید.</p>"));
        var unwrapped = ContentQualityGate.Clean("<p>این <a href=\"#\">هدفون</a> سبک است.</p>");
        Assert.Contains("هدفون", unwrapped);
        Assert.DoesNotContain("href", unwrapped);
        Assert.Equal("جملهٔ خوب اول.", ContentQualityGate.Clean("جملهٔ خوب اول. صفحهٔ دسته‌بندی نشده را ببینید."));
        Assert.Equal("", ContentQualityGate.Clean(null));
    }

    // ── خانواده و آیکون دسته ────────────────────────────────────

    [Fact]
    public void Families_match_exact_names_only()
    {
        Assert.Equal("mobile-accessories", CategoryFamilies.Match("لوازم جانبی گوشی")?.Key);
        Assert.Equal("toys", CategoryFamilies.Match(null, "toys")?.Key);
        Assert.Null(CategoryFamilies.Match("لوازم جانبی خودرو"));
        Assert.Null(CategoryFamilies.Match(""));
        Assert.Null(CategoryTypes.Strict("موبایل و تبلت"));
        Assert.Equal(CategoryFamilies.All.Count, CategoryFamilies.All.Select(f => f.Key).Distinct().Count());
    }

    [Fact]
    public void Every_category_icon_exists_in_the_site_icon_set()
    {
        foreach (var family in CategoryFamilies.All)
            Assert.True(Icons.Has(family.Icon), family.Icon);
        foreach (var type in ProductTypes.All)
        {
            var icon = CategoryIcons.For(type.Name);
            Assert.True(Icons.Has(icon), $"{type.Name} → {icon}");
        }
        Assert.Equal("Home", CategoryIcons.For("لوازم خانگی"));
        Assert.Equal(CategoryIcons.Default, CategoryIcons.For("زژقلمبژ"));
        Assert.True(Icons.Has(CategoryIcons.Default));
    }

    [Fact]
    public void Duplicate_keys_join_plural_forms_but_not_different_scopes()
    {
        Assert.Equal(CategoryTreeService.DuplicateKey("ماساژورها", null), CategoryTreeService.DuplicateKey("ماساژور", null));
        Assert.NotEqual(CategoryTreeService.DuplicateKey("لوازم جانبی", null), CategoryTreeService.DuplicateKey("لوازم جانبی موبایل", null));
    }

    [Fact]
    public void Ancestor_check_walks_up_and_survives_broken_cycles()
    {
        var a = new Fratak_Shop.Data.Entities.Categories.Category { Id = Guid.NewGuid(), Name = "A", Slug = "a" };
        var b = new Fratak_Shop.Data.Entities.Categories.Category { Id = Guid.NewGuid(), Name = "B", Slug = "b", ParentId = a.Id };
        var c = new Fratak_Shop.Data.Entities.Categories.Category { Id = Guid.NewGuid(), Name = "C", Slug = "c", ParentId = b.Id };
        var x = new Fratak_Shop.Data.Entities.Categories.Category { Id = Guid.NewGuid(), Name = "X", Slug = "x" };
        var y = new Fratak_Shop.Data.Entities.Categories.Category { Id = Guid.NewGuid(), Name = "Y", Slug = "y", ParentId = x.Id };
        x.ParentId = y.Id; // دادهٔ خراب: حلقه
        var list = new List<Fratak_Shop.Data.Entities.Categories.Category> { a, b, c, x, y };

        Assert.True(CategoryTreeService.IsAncestor(list, a.Id, c.Id));
        Assert.True(CategoryTreeService.IsAncestor(list, b.Id, c.Id));
        Assert.False(CategoryTreeService.IsAncestor(list, c.Id, a.Id));
        Assert.False(CategoryTreeService.IsAncestor(list, a.Id, y.Id));
    }

    [Fact]
    public void Category_synonyms_are_clean_disjoint_groups()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var set in Fratak_Shop.Core.Services.Searches.CategorySynonyms.Sets)
        {
            var members = set.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            Assert.InRange(members.Length, 2, 6);
            foreach (var member in members)
                Assert.True(seen.Add(Fratak_Shop.Core.Utilities.SearchText.Normalize(member)), $"«{member}» در دو گروه آمده است.");
        }

        var map = Fratak_Shop.Core.Services.Searches.SearchInsightsService.BuildSynonymMap(Fratak_Shop.Core.Services.Searches.CategorySynonyms.Sets);
        Assert.Contains("ماوس", map["موس"]);
        Assert.Contains("تلویزیون", map["تلوزیون"]);
        Assert.Contains("لپ تاپ", map["لپتاپ"]);
        Assert.False(map.ContainsKey("آیفون")); // برند/مدل هم‌معنی دسته نیست
    }

    [Fact]
    public void Phrase_alternatives_also_match_half_space_names()
    {
        Assert.Equal(new[] { "لپ تاپ", "لپ\u200Cتاپ", "ماوس" }, Fratak_Shop.Core.Utilities.ProductSearch.WithHalfSpace(new[] { "لپ تاپ", "ماوس" }).ToArray());
    }

    private static List<Fratak_Shop.Core.Services.Products.SpecFeatureRow> Rows(string key, params string[] values) =>
        values.Select(v => new Fratak_Shop.Core.Services.Products.SpecFeatureRow(Guid.NewGuid(), key, v)).ToList();

    [Fact]
    public void Spec_facets_are_planned_only_when_they_help_buyers()
    {
        var none = new HashSet<string>();
        var memory = Rows("حافظه داخلی", "64", "128", "256", "128", "64", "256", "128", "64");
        var plan = Fratak_Shop.Core.Services.Products.SpecFacetPlanner.Plan(10, memory, none);
        var only = Assert.Single(plan);
        Assert.Equal("حافظه داخلی", only.Key);
        Assert.Equal(8, only.Coverage);
        Assert.Equal(3, only.DistinctValues);

        // دستهٔ کوچک، پوشش کم، همه یکسان، مقدار زیاد/بلند، کلید شناسه، فیلتر موجود ← هیچ.
        Assert.Empty(Fratak_Shop.Core.Services.Products.SpecFacetPlanner.Plan(5, memory, none));
        Assert.Empty(Fratak_Shop.Core.Services.Products.SpecFacetPlanner.Plan(20, memory, none));
        Assert.Empty(Fratak_Shop.Core.Services.Products.SpecFacetPlanner.Plan(8, Rows("رنگ", Enumerable.Repeat("مشکی", 11).Append("سفید").ToArray()), none));
        Assert.Empty(Fratak_Shop.Core.Services.Products.SpecFacetPlanner.Plan(20, Rows("رنگ", Enumerable.Range(0, 20).Select(i => "رنگ " + i).ToArray()), none));
        Assert.Empty(Fratak_Shop.Core.Services.Products.SpecFacetPlanner.Plan(6, Rows("توضیحات", "a", "b", "a", "b", "a", "b"), none));
        Assert.Empty(Fratak_Shop.Core.Services.Products.SpecFacetPlanner.Plan(6, Rows("جنس", new string('x', 60), "چرم", "پارچه", "چرم", "پارچه", "چرم"), none));
        Assert.Empty(Fratak_Shop.Core.Services.Products.SpecFacetPlanner.Plan(10, memory, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "حافظه داخلی" }));
        Assert.True(Fratak_Shop.Core.Services.Products.SpecFacetPlanner.IsStopKey("مدل"));
        Assert.True(Fratak_Shop.Core.Services.Products.SpecFacetPlanner.IsStopKey("ابعاد بسته‌بندی"));
        Assert.False(Fratak_Shop.Core.Services.Products.SpecFacetPlanner.IsStopKey("حافظه داخلی"));
    }

    [Fact]
    public void Spec_facet_slug_is_stable_latin_and_short()
    {
        var category = Guid.NewGuid();
        var slug = Fratak_Shop.Core.Services.Products.SpecFacetPlanner.Slug("حافظه داخلی", category);
        Assert.Equal(slug, Fratak_Shop.Core.Services.Products.SpecFacetPlanner.Slug(" حافظه داخلی ", category));
        Assert.NotEqual(slug, Fratak_Shop.Core.Services.Products.SpecFacetPlanner.Slug("حافظه داخلی", Guid.NewGuid()));
        Assert.Matches("^spec-[0-9a-f]{12}$", slug);
    }

    [Fact]
    public void Related_posts_prefer_same_topic_then_same_category_and_skip_unrelated()
    {
        var current = Guid.NewGuid();
        var audio = Guid.NewGuid();
        var kitchen = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var sony = new Fratak_Shop.Core.Services.Blogs.RelatedCandidate(Guid.NewGuid(), "بررسی هدفون سونی", kitchen, now.AddDays(-30));
        var speaker = new Fratak_Shop.Core.Services.Blogs.RelatedCandidate(Guid.NewGuid(), "راهنمای خرید اسپیکر", audio, now);
        var kettle = new Fratak_Shop.Core.Services.Blogs.RelatedCandidate(Guid.NewGuid(), "راهنمای خرید کتری برقی", kitchen, now);
        var self = new Fratak_Shop.Core.Services.Blogs.RelatedCandidate(current, "راهنمای خرید هدفون", audio, now);

        var ids = Fratak_Shop.Core.Services.Blogs.RelatedPosts.Rank(current, "راهنمای خرید هدفون بی‌سیم", audio,
            new[] { kettle, speaker, sony, self, sony }, 4);

        // «هدفون» مشترک (۲) + هم‌دسته نبودن = ۲ ؛ اسپیکر هم‌دسته = ۳ ؛ کتری: فقط واژه‌های عمومی مشترک ← حذف.
        Assert.Equal(new[] { speaker.Id, sony.Id }, ids.ToArray());
        Assert.Empty(Fratak_Shop.Core.Services.Blogs.RelatedPosts.Rank(current, "x", audio, new[] { speaker }, 0));
    }

    [Fact]
    public void Related_post_words_ignore_generic_title_words()
    {
        var words = Fratak_Shop.Core.Services.Blogs.RelatedPosts.Words("راهنمای خرید بهترین هدفون‌های ۲۰۲۶");
        Assert.Contains("هدفون", words);
        Assert.DoesNotContain("راهنمای", words);
        Assert.DoesNotContain("بهترین", words);
        Assert.DoesNotContain("2026", words);
    }

    [Fact]
    public void Slow_query_log_groups_repeats_and_keeps_only_sql_text()
    {
        var marker = "SELECT x FROM T_" + Guid.NewGuid().ToString("N");
        var now = DateTimeOffset.UtcNow;
        Fratak_Shop.Data.Diagnostics.SlowQueryLog.Record(marker + "\n   WHERE  a = @p0", 900, now);
        Fratak_Shop.Data.Diagnostics.SlowQueryLog.Record(marker + " WHERE a = @p0", 2500, now.AddSeconds(1));
        Fratak_Shop.Data.Diagnostics.SlowQueryLog.Record("   ", 5000, now);

        var entry = Assert.Single(Fratak_Shop.Data.Diagnostics.SlowQueryLog.Recent(), e => e.Sql.StartsWith(marker, StringComparison.Ordinal));
        Assert.Equal(marker + " WHERE a = @p0", entry.Sql);
        Assert.Equal(2, entry.Count);
        Assert.Equal(2500, entry.MaxMilliseconds);
        Assert.DoesNotContain(Fratak_Shop.Data.Diagnostics.SlowQueryLog.Recent(), e => e.Sql.Length == 0);
        Assert.True(Fratak_Shop.Data.Diagnostics.SlowQueryLog.Shorten(new string('a', 5000)).Length <= Fratak_Shop.Data.Diagnostics.SlowQueryLog.MaxSqlLength + 1);
    }

    [Fact]
    public void Ops_alert_fires_once_per_spike_and_respects_cooldown_and_daily_cap()
    {
        var rules = new Fratak_Shop.Web.Infrastructure.Ops.OpsAlertRules();
        var t = new DateTimeOffset(2026, 10, 22, 8, 0, 0, TimeSpan.Zero);
        var fired = 0;
        for (var i = 0; i < 29; i++)
            fired += rules.ObserveError(t.AddSeconds(i)) ? 1 : 0;
        Assert.Equal(0, fired);                                   // زیر آستانه
        Assert.True(rules.ObserveError(t.AddSeconds(30)));         // سی‌امین خطا ← هشدار
        for (var i = 0; i < 200; i++)
            Assert.False(rules.ObserveError(t.AddSeconds(31 + i))); // همان جهش ← پیامک تکراری نه

        // خطاهای پراکنده (هر ۱ دقیقه یکی) هرگز جهش نیستند.
        var calm = new Fratak_Shop.Web.Infrastructure.Ops.OpsAlertRules();
        for (var i = 0; i < 100; i++)
            Assert.False(calm.ObserveError(t.AddMinutes(i * 1)));

        // فاصلهٔ ۲ ساعته برای هر نوع و سقف ۶ پیامک در روز.
        var capped = new Fratak_Shop.Web.Infrastructure.Ops.OpsAlertRules();
        Assert.True(capped.TryFire(Fratak_Shop.Web.Infrastructure.Ops.OpsAlertKind.DatabaseDown, t));
        Assert.False(capped.TryFire(Fratak_Shop.Web.Infrastructure.Ops.OpsAlertKind.DatabaseDown, t.AddHours(1)));
        var sent = 1;
        for (var h = 2; h < 24; h += 2)
            sent += capped.TryFire(Fratak_Shop.Web.Infrastructure.Ops.OpsAlertKind.DatabaseDown, t.AddHours(h)) ? 1 : 0;
        Assert.Equal(Fratak_Shop.Web.Infrastructure.Ops.OpsAlertRules.DailyMax, sent);
        Assert.True(capped.TryFire(Fratak_Shop.Web.Infrastructure.Ops.OpsAlertKind.DatabaseDown, t.AddHours(30)));
    }

    [Fact]
    public void Ops_alert_text_never_carries_otp_like_numbers()
    {
        foreach (var kind in Enum.GetValues<Fratak_Shop.Web.Infrastructure.Ops.OpsAlertKind>())
        {
            var text = Fratak_Shop.Web.Infrastructure.Ops.OpsAlertRules.Message(kind, "فراتک");
            Assert.Contains("فراتک", text);
            Assert.DoesNotMatch(@"\d{4,}", text);
        }
    }

    [Fact]
    public void Duplicate_images_of_one_product_are_found_but_primary_and_other_products_are_safe()
    {
        var product = Guid.NewGuid();
        var other = Guid.NewGuid();
        const long a = 0x0F0F_0F0F_0F0F_0F0FL;
        const long aResized = a ^ 0b101;          // ۲ بیت فرق ← همان عکس
        const long b = 0x7070_7070_7070_7070L;    // کاملاً متفاوت
        var primary = new Fratak_Shop.Core.Services.Smart.ImageFingerprint(Guid.NewGuid(), product, true, 5, aResized);
        var copy = new Fratak_Shop.Core.Services.Smart.ImageFingerprint(Guid.NewGuid(), product, false, 0, a);
        var distinct = new Fratak_Shop.Core.Services.Smart.ImageFingerprint(Guid.NewGuid(), product, false, 1, b);
        var unknown = new Fratak_Shop.Core.Services.Smart.ImageFingerprint(Guid.NewGuid(), product, false, 2, 0);
        var otherProduct = new Fratak_Shop.Core.Services.Smart.ImageFingerprint(Guid.NewGuid(), other, false, 0, a);

        var redundant = Fratak_Shop.Core.Services.Smart.ImageDuplicates.Redundant(new[] { copy, distinct, unknown, otherProduct, primary });

        Assert.Equal(new[] { copy.Id }, redundant.ToArray());
    }

    [Fact]
    public void Arabic_letters_in_names_become_persian()
    {
        Assert.Equal("کیف علی", Fratak_Shop.Data.Contexts.FratakShopDbContext.PersianLetters("\u0643\u064Aف عل\u0649"));
        const string clean = "هدفون بی‌سیم";
        Assert.Same(clean, Fratak_Shop.Data.Contexts.FratakShopDbContext.PersianLetters(clean));
        Assert.Equal("", Fratak_Shop.Data.Contexts.FratakShopDbContext.PersianLetters(""));
    }

    // ── منوی دسته‌ها ────────────────────────────────────────────

    private static CategoryDto Root(string name, int order = 0, bool system = false, params SubCategoryDto[] subs) =>
        new() { Id = Guid.NewGuid(), Name = name, Slug = name, DisplayOrder = order, IsSystem = system, SubCategories = subs.ToList() };

    private static SubCategoryDto Sub(string name) => new() { Id = Guid.NewGuid(), Name = name, Slug = name };

    [Fact]
    public void Menu_hides_system_empty_and_thin_categories_and_orders_by_sales()
    {
        var richSub = Sub("a1");
        var thinSub = Sub("a2");
        var a = Root("A", 0, false, richSub, thinSub);
        var b = Root("B", 1);
        var c = Root("C", 2);
        var system = Root("S", 3, true);
        var empty = Root("E", 4);
        var f = Root("F", 5);
        var stats = new Dictionary<Guid, (int Count, int Sales)>
        {
            [richSub.Id] = (4, 10), [thinSub.Id] = (1, 0),
            [b.Id] = (4, 50),
            [c.Id] = (1, 99),
            [system.Id] = (40, 500),
            [f.Id] = (3, 0),
        };

        var menu = CategoryService.BuildMenu([a, b, c, system, empty, f], stats);

        Assert.Equal(new[] { "B", "A", "F" }, menu.Select(m => m.Name).ToArray());
        Assert.Equal(5, menu[1].ProductCount);
        Assert.Equal("a1", Assert.Single(menu[1].SubCategories).Name);
        Assert.True(CategoryService.IsThin(2));
        Assert.False(CategoryService.IsThin(3));
    }

    [Fact]
    public void Menu_of_a_new_shop_keeps_categories_that_have_products()
    {
        var a = Root("A");
        var b = Root("B");
        var empty = Root("E");
        var stats = new Dictionary<Guid, (int Count, int Sales)> { [a.Id] = (1, 0), [b.Id] = (2, 1) };
        var menu = CategoryService.BuildMenu([a, b, empty], stats);
        Assert.Equal(new[] { "B", "A" }, menu.Select(m => m.Name).ToArray());
    }

    // ── پرسش‌های متداول دسته ─────────────────────────────────────

    [Fact]
    public void Category_faq_uses_real_numbers_and_stays_clean()
    {
        var facts = new CategoryFacts(10, 8, 450_000m, 8_900_000m, 3, [("سونی", 4), ("شیائومی", 2)]);
        var questions = CategoryFaq.Build("هدفون", facts, "فراتک");
        Assert.Equal(4, questions.Count);
        Assert.Contains("۴۵۰", questions[0].Answer);
        Assert.Contains("۸", questions[1].Answer);
        Assert.Contains("سونی", questions[2].Answer);
        Assert.All(questions, q => Assert.True(ContentQualityGate.IsClean(q.Question + " " + q.Answer), q.Answer));

        Assert.Empty(CategoryFaq.Build("هدفون", CategoryFacts.Empty, "فراتک"));
        Assert.Empty(CategoryFaq.Build(" ", facts, "فراتک"));
    }

    // ── بنر: A/B و سوگواری ─────────────────────────────────────

    private static BannerDto Banner(string? group, int views = 0, int clicks = 0, int order = 0, BannerPosition position = BannerPosition.HeroSlider) =>
        new() { Id = Guid.NewGuid(), VariantGroup = group, Views = views, Clicks = clicks, DisplayOrder = order, Position = position, IsActive = true };

    [Fact]
    public void Ctr_is_smoothed_and_readable()
    {
        Assert.Equal(0.5, BannerRules.Ctr(0, 0));
        Assert.InRange(BannerRules.Ctr(1000, 30), 0.029, 0.032);
        Assert.Equal("2.5%", BannerRules.CtrText(1000, 25));
        Assert.Equal("—", BannerRules.CtrText(0, 5));
        Assert.Equal("100%", BannerRules.CtrText(10, 50));
    }

    [Fact]
    public void Rotation_keeps_one_variant_per_group_and_all_ungrouped()
    {
        var plain = Banner(null);
        var a1 = Banner("yalda", 10);
        var a2 = Banner("Yalda ", 5);
        var other = Banner("yalda", 0, position: BannerPosition.WideBanner);
        var result = BannerRules.Rotate([plain, a1, a2, other], () => 0.5);
        Assert.Equal(3, result.Count);
        Assert.Contains(plain, result);
        Assert.Contains(a2, result); // کم‌نمایش‌تر، تا دادهٔ منصفانه جمع شود
        Assert.Contains(other, result); // جایگاه دیگر، گروه جدا
    }

    [Fact]
    public void Winner_gets_most_traffic_after_enough_views()
    {
        var good = Banner("g", 1000, 50);
        var weak = Banner("g", 1000, 5);
        Assert.Same(good, BannerRules.Choose([weak, good], () => 0.1));
        Assert.Same(weak, BannerRules.Choose([weak, good], () => 0.95));
        Assert.Same(good, BannerRules.Choose([good], () => 0.99));
    }

    [Fact]
    public void Mourning_shows_only_mourning_group_and_category_headers()
    {
        var mourning = Banner("Mourning");
        var header = Banner(null, position: BannerPosition.CategoryHeader);
        var festive = Banner("yalda");
        var result = BannerRules.ForMourning([mourning, header, festive]);
        Assert.Equal(2, result.Count);
        Assert.DoesNotContain(festive, result);
    }

    [Theory]
    [InlineData(" Yalda 1405 ", "yalda-1405")]
    [InlineData("A_b-C", "a_b-c")]
    [InlineData("!!!", null)]
    [InlineData("<script>", "script")]
    [InlineData(null, null)]
    public void Variant_group_is_normalized(string? input, string? expected)
    {
        Assert.Equal(expected, BannerRules.NormalizeGroup(input));
    }

    [Fact]
    public void Variant_group_is_capped_at_sixty_characters()
    {
        Assert.Equal(60, BannerRules.NormalizeGroup(new string('a', 100))!.Length);
    }

    [Fact]
    public void Mourning_calendar_knows_lunar_and_solar_days()
    {
        var tehran = TimeSpan.FromHours(3.5);
        var hijri = new UmAlQuraCalendar();
        var ashura = new DateTimeOffset(hijri.ToDateTime(1447, 1, 10, 12, 0, 0, 0), tehran);
        Assert.Equal("عاشورای حسینی", MourningCalendar.Today(ashura));
        Assert.True(MourningCalendar.IsMourning(ashura));

        var persian = new PersianCalendar();
        var khordad14 = new DateTimeOffset(persian.ToDateTime(1404, 3, 14, 12, 0, 0, 0), tehran);
        Assert.Equal("رحلت امام خمینی", MourningCalendar.Today(khordad14));

        var normal = new DateTimeOffset(hijri.ToDateTime(1447, 4, 15, 12, 0, 0, 0), tehran);
        Assert.Null(MourningCalendar.Today(normal));
        Assert.False(MourningCalendar.IsMourning(normal));
    }

    [Fact]
    public void Banner_tracking_counts_each_visitor_once_and_rejects_junk()
    {
        using var cache = new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions());
        var stats = new Fratak_Shop.Web.Infrastructure.Marketing.BannerStats(cache);
        var id = Guid.NewGuid();
        var body = $"{{\"v\":[\"{id}\"],\"c\":[\"{id}\"]}}";

        Assert.Equal(2, Fratak_Shop.Web.Infrastructure.Marketing.BannerEndpoints.Accept(body, stats, "1.2.3.4"));
        Assert.Equal(0, Fratak_Shop.Web.Infrastructure.Marketing.BannerEndpoints.Accept(body, stats, "1.2.3.4")); // تکراری در ۳۰ دقیقه
        Assert.Equal(2, Fratak_Shop.Web.Infrastructure.Marketing.BannerEndpoints.Accept(body, stats, "5.6.7.8"));
        Assert.Equal(0, Fratak_Shop.Web.Infrastructure.Marketing.BannerEndpoints.Accept("{bad", stats, "x"));
        Assert.Equal(0, Fratak_Shop.Web.Infrastructure.Marketing.BannerEndpoints.Accept("[1]", stats, "x"));
        Assert.Equal(0, Fratak_Shop.Web.Infrastructure.Marketing.BannerEndpoints.Accept($"{{\"v\":[\"{Guid.Empty}\", 5, null]}}", stats, "x"));

        var many = "{\"v\":[" + string.Join(",", Enumerable.Range(0, 13).Select(_ => $"\"{Guid.NewGuid()}\"")) + "]}";
        Assert.Equal(Fratak_Shop.Web.Infrastructure.Marketing.BannerEndpoints.MaxIds,
            Fratak_Shop.Web.Infrastructure.Marketing.BannerEndpoints.Accept(many, stats, "y"));

        var drained = stats.Drain();
        Assert.Equal((2, 2), drained[id]);
        Assert.Equal(0, stats.Pending);
    }

    // ── رنگ و کنتراست ───────────────────────────────────────────

    [Fact]
    public void Contrast_follows_wcag()
    {
        Assert.InRange(DesignColors.Contrast("#ffffff", "#000000"), 20.9, 21.1);
        Assert.Equal(1, DesignColors.Contrast("#fff", "#ffffff"));
        Assert.Equal(1, DesignColors.Contrast("red", "#000"));
        Assert.Equal(DesignColors.DarkInk, DesignColors.BestInk("#facc15"));
        Assert.Equal(DesignColors.LightInk, DesignColors.BestInk("#1e3a8a"));
        Assert.Equal("#808080", DesignColors.Mix("#000000", "#ffffff", 0.5));
        Assert.True(DesignColors.TryParse("#ABC", out var rgb));
        Assert.Equal((170, 187, 204), rgb);
        Assert.False(DesignColors.TryParse("#12345", out _));
    }

    [Theory]
    [InlineData("#ffff00")]
    [InlineData("#1e3a8a")]
    [InlineData("#808080")]
    [InlineData("#ff0000")]
    [InlineData("#00ff00")]
    [InlineData("#ffffff")]
    [InlineData("#000000")]
    public void Palette_from_any_color_is_readable(string color)
    {
        var palette = DesignColors.FromColor(color);
        Assert.True(DesignColors.MinTextContrast(palette) >= DesignColors.MinContrast, $"{color}: {DesignColors.MinTextContrast(palette)}");
    }

    [Fact]
    public void Invalid_color_falls_back_to_default_palette()
    {
        Assert.Equal(DesignPalettes.All[0], DesignColors.FromColor("javascript:alert(1)"));
    }

    private static SKBitmap ProductPhoto()
    {
        var bitmap = new SKBitmap(200, 200);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);
        using var paint = new SKPaint { Color = new SKColor(200, 30, 40), IsAntialias = false };
        canvas.DrawRect(50, 50, 100, 100, paint);
        return bitmap;
    }

    [Fact]
    public void Dominant_color_ignores_white_background()
    {
        using var photo = ProductPhoto();
        var hex = ImageColors.Dominant(photo);
        Assert.True(DesignColors.TryParse(hex, out var rgb), hex);
        Assert.True(rgb.R > 150 && rgb.G < 80 && rgb.B < 90, hex);
        Assert.True(ImageColors.HasWhiteBackground(photo));

        using var gray = new SKBitmap(50, 50);
        using (var canvas = new SKCanvas(gray))
            canvas.Clear(new SKColor(128, 128, 128));
        Assert.Null(ImageColors.Dominant(gray));
        Assert.False(ImageColors.HasWhiteBackground(gray));
    }

    [Fact]
    public void Cutout_makes_only_the_background_transparent()
    {
        using var photo = ProductPhoto();
        var png = ImageColors.CutoutWhite(photo);
        Assert.NotNull(png);
        using var decoded = SKBitmap.Decode(png);
        Assert.Equal(0, decoded.GetPixel(2, 2).Alpha);
        Assert.Equal(255, decoded.GetPixel(100, 100).Alpha);
    }

    // ── ایده ← طرح ──────────────────────────────────────────────

    private static IdeaProduct Idea(string name, decimal price, decimal final, int stock = 5, string? image = "/uploads/products/a.webp") =>
        new(Guid.NewGuid(), name, "p-" + Guid.NewGuid().ToString("N")[..6], image, 800, 800, price, final, stock, 10, 3, "هدفون", "headphone", DateTimeOffset.UtcNow);

    [Theory]
    [InlineData("کارت هدیه یلدا", BriefKind.GiftCard)]
    [InlineData("باشگاه مشتریان", BriefKind.Club)]
    [InlineData("مقاله راهنمای خرید هدفون", BriefKind.Blog)]
    [InlineData("حراج لپ‌تاپ", BriefKind.Deal)]
    [InlineData("جشنواره پاییزه", BriefKind.Campaign)]
    [InlineData("هدفون", BriefKind.General)]
    public void Brief_kind_is_read_from_short_text(string text, BriefKind kind)
    {
        Assert.Equal(kind, DesignBrief.Parse(text).Kind);
    }

    [Fact]
    public void Brief_reads_occasion_and_percent()
    {
        var intent = DesignBrief.Parse("تخفیف هدفون یلدا ۲۰٪");
        Assert.Equal(BriefKind.Occasion, intent.Kind);
        Assert.Equal("yalda", intent.Occasion);
        Assert.Equal(20, intent.Percent);
        Assert.True(intent.Deal);
    }

    [Fact]
    public void Brief_never_reads_a_slice_of_a_bigger_number_as_percent()
    {
        // «۱۵۰٪» معنا ندارد؛ نباید «۵۰٪» خوانده شود.
        Assert.Null(DesignBrief.Parse("تخفیف هدفون ۱۵۰٪").Percent);
        Assert.Equal(15, DesignBrief.Parse("تخفیف هدفون 15 درصد").Percent);
    }

    [Fact]
    public void Brief_never_claims_a_bigger_discount_than_real()
    {
        var products = new List<IdeaProduct> { Idea("هدفون سونی", 100_000, 80_000), Idea("هدفون شیائومی", 100_000, 90_000) };
        var text = "حراج هدفون ۵۰٪";
        var plan = DesignBrief.Plan(text, DesignBrief.Parse(text), new BriefData(products, null, null, "فراتک"), DateTimeOffset.UtcNow);
        Assert.Equal(BriefKind.Deal, plan.Kind);
        Assert.NotNull(plan.Badge);
        Assert.Contains(DesignTemplates.FaDigits("20"), plan.Badge);
        Assert.DoesNotContain(DesignTemplates.FaDigits("50"), plan.Badge);
        Assert.NotEmpty(plan.Warnings);
        Assert.StartsWith("/", plan.LinkUrl, StringComparison.Ordinal);

        var none = DesignBrief.Plan(text, DesignBrief.Parse(text), new BriefData([Idea("هدفون", 100_000, 100_000)], null, null, "فراتک"), DateTimeOffset.UtcNow);
        Assert.Null(none.Badge);
        Assert.NotEmpty(none.Warnings);
    }

    [Fact]
    public void Gift_card_brief_links_to_gift_cards_without_product()
    {
        var plan = DesignBrief.Plan("کارت هدیه", DesignBrief.Parse("کارت هدیه"), new BriefData([Idea("هدفون", 100, 90)], null, null, "فراتک"), DateTimeOffset.UtcNow);
        Assert.Equal(BriefKind.GiftCard, plan.Kind);
        Assert.Equal("/profile/gift-cards", plan.LinkUrl);
        Assert.Null(plan.Hero);
    }

    [Fact]
    public void Brief_makes_three_variants_in_one_ab_group()
    {
        var products = new List<IdeaProduct> { Idea("هدفون سونی", 100_000, 80_000) };
        var plan = DesignBrief.Plan("حراج هدفون", DesignBrief.Parse("حراج هدفون"), new BriefData(products, null, null, "فراتک"), DateTimeOffset.UtcNow);
        var ideas = DesignBrief.Variants(plan, "#1e3a8a", new DesignImage("/uploads/products/a.webp", 800, 800));
        Assert.Equal(3, ideas.Count);
        Assert.Equal(3, ideas.Select(i => i.Key).Distinct().Count());
        var groups = ideas.Select(i => DesignBrief.VariantGroup(i.Key)).Distinct().ToList();
        Assert.NotNull(Assert.Single(groups));
        Assert.Equal("#1e3a8a", ideas[0].Spec.Palette);
        Assert.Null(DesignBrief.VariantGroup("occasion:yalda"));
        Assert.Null(DesignBrief.VariantGroup(null));
    }
}
