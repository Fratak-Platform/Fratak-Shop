using Fratak_Shop.Core.DTOs.Settings;
using Fratak_Shop.Core.Services.Seo.Engine;
using Fratak_Shop.Core.Services.Seo.Knowledge;
using Fratak_Shop.Core.Services.Seo.Smart;
using Fratak_Shop.Data.Entities.Pages;
using Fratak_Shop.Data.Entities.SeoEngine;
using Fratak_Shop.Web.Areas.Admin.Pages.Seo;
using Fratak_Shop.Web.Infrastructure.SeoEngine;

namespace Fratak_Shop.Tests.Unit;

/// <summary>
/// تست‌های مرحلهٔ شانزدهم بدون دیتابیس و بدون اینترنت:
/// • جدول دانش سئو (دادهٔ اولیه، اطمینان قاعده‌ها، امتیاز یادگیری کلمهٔ کلیدی).
/// • محافظ اسپم (پاک کردن عنوان، انباشت کلمه، متن پنهان، تراکم).
/// • ویراستار فارسی (املا، حشو، نشانی‌های محافظت‌شده، قاعدهٔ کم‌اطمینان فقط پیشنهاد).
/// • «پیشنهاد هوشمند» (نسخه‌های نام محصول، عنوان، متن بلند؛ حذف اسکریپت؛ فیلد خالی).
/// • صفحه‌های ثابت، الگوی عنوان آموخته، کدهای کمکی پنل.
/// </summary>
public class SmartSeoTests
{
    /// <summary>دانش کامل از فایل جاسازی‌شده (یک بار برای همهٔ تست‌های این کلاس).</summary>
    private static readonly Lazy<SeoKnowledgeBase> Seed = new(SeoKnowledgeBase.FromSeed);

    private static SeoKnowledgeBase Kb => Seed.Value;

    private static readonly StoreFacts Store = new() { SiteName = "فراتک", TitleSuffixLength = 8 };

    private static SmartTextRequest Request(SmartPurpose purpose, string text, string? keyword = null, string pageType = "product") => new()
    {
        Purpose = purpose,
        Text = text,
        Keyword = keyword,
        Subject = "گوشی موبایل سامسونگ Galaxy A55",
        Category = "گوشی موبایل",
        Brand = "سامسونگ",
        PageType = pageType,
        Store = Store,
    };

    // ── جدول دانش ────────────────────────────────────────────────────

    [Fact]
    public void Seed_file_is_large_and_covers_every_curated_kind()
    {
        Assert.True(SeoKnowledgeSeed.Rows.Count >= 150_000, $"rows: {SeoKnowledgeSeed.Rows.Count}");
        Assert.True(Kb.Total > 1_000, $"total: {Kb.Total}");
        Assert.NotEmpty(Kb.Corrections);
        Assert.NotEmpty(Kb.Styles);
        Assert.NotEmpty(Kb.Spam);
        Assert.NotEmpty(Kb.Headings);
        Assert.NotEmpty(Kb.Questions);
        Assert.NotEmpty(Kb.CallsToAction);
        Assert.NotEmpty(Kb.Benefits);
        Assert.NotEmpty(Kb.Synonyms);
        Assert.Contains(Kb.Corrections, r => r.Term == "باطری" && r.Value == "باتری");
        Assert.Contains(Kb.Spam, r => r.Term.StartsWith("ارزان", StringComparison.Ordinal));
    }

    [Fact]
    public void Seed_kind_codes_map_to_real_kinds()
    {
        Assert.NotEmpty(SeoKnowledgeSeed.KindCodes);
        Assert.All(SeoKnowledgeSeed.KindCodes.Values, k => Assert.True(Enum.IsDefined(k) && k != SeoKnowledgeKind.None, k.ToString()));
    }

    [Theory]
    [InlineData(1.0, 0, 0, true)]   // دادهٔ اولیهٔ مطمئن ← خودکار
    [InlineData(1.0, 0, 2, false)]  // دو بار رد کارشناس ← فقط پیشنهاد
    [InlineData(0.6, 0, 0, false)]  // وزن کم ← فقط پیشنهاد
    [InlineData(0.6, 8, 0, false)]  // پذیرش زیاد وزن را بیشتر از خودش نمی‌کند
    [InlineData(0.9, 6, 1, true)]   // ۰٫۹ × ۱۰ ÷ ۱۱٫۵ ≈ ۰٫۷۸
    public void Rule_confidence_decides_auto_apply(double weight, int successes, int rejections, bool auto)
    {
        var rule = new KnowledgeRule(SeoKnowledgeKind.Correction, "الف", "ب", "املا", weight, successes, rejections);
        Assert.Equal(auto, rule.IsAuto);
        Assert.InRange(rule.Confidence, 0, weight);
        Assert.Equal("7:الف", rule.Id);
    }

    [Fact]
    public void Keyword_bonus_rewards_accepted_and_punishes_rejected_keywords()
    {
        var kb = new SeoKnowledgeBase(
        [
            new KnowledgeRow(SeoKnowledgeKind.Keyword, "هدفون سونی", null, "product", 1, Successes: 3),
            new KnowledgeRow(SeoKnowledgeKind.Keyword, "هدفون ارزان", null, "product", 1, Rejections: 2),
        ]);
        Assert.Equal(1.8, kb.KeywordBonus("هدفون سونی"), 2);
        Assert.Equal(-2.4, kb.KeywordBonus("هدفون ارزان"), 2);
        Assert.Equal(0.0, kb.KeywordBonus("کیبورد مکانیکی"), 2);
    }

    // ── محافظ اسپم ───────────────────────────────────────────────────

    [Fact]
    public void Clean_title_removes_hype_emoji_and_repeated_marks_but_keeps_the_product()
    {
        var removed = new List<string>();
        var cleaned = SpamGuard.CleanTitle("🔥 ارزان‌ترین هدفون سونی!!!", Kb, removed);
        Assert.Contains("هدفون سونی", cleaned);
        Assert.DoesNotContain("ارزان", cleaned);
        Assert.DoesNotContain("🔥", cleaned);
        Assert.DoesNotContain("!!", cleaned);
        Assert.NotEmpty(removed);
        Assert.Equal("", SpamGuard.CleanTitle(null, Kb));
        Assert.Equal("", SpamGuard.CleanTitle("   ", null));
    }

    [Fact]
    public void Clean_title_without_knowledge_uses_the_fallback_list()
    {
        var cleaned = SpamGuard.CleanTitle("فقط امروز هدفون سونی", null);
        Assert.DoesNotContain("فقط امروز", cleaned);
        Assert.Contains("هدفون سونی", cleaned);
    }

    [Theory]
    [InlineData("فقط امروز تخفیف هدفون", true)]
    [InlineData("هدفون ⭐ سونی", true)]
    [InlineData("قیمت هدفون سونی", false)]
    [InlineData("هدفون بی سیم بلوتوثی", false)]
    public void Spammy_research_phrases_are_detected(string phrase, bool spammy)
    {
        Assert.Equal(spammy, SpamGuard.IsSpammy(phrase, Kb));
    }

    [Fact]
    public void Replace_phrase_respects_word_boundaries_and_returns_same_instance_when_absent()
    {
        Assert.Equal("خرید هدفون", PersianNormalize(SpamGuard.ReplacePhrase("خرید ارزان‌ترین هدفون", "ارزان‌ترین", "")));
        const string text = "هدفونهای سونی";
        // «هدفون» داخل واژهٔ بزرگ‌تر است؛ نباید جایگزین شود.
        Assert.Same(text, SpamGuard.ReplacePhrase(text, "هدفون", "x"));
    }

    private static string PersianNormalize(string value) => string.Join(' ', value.Split(' ', StringSplitOptions.RemoveEmptyEntries));

    [Fact]
    public void Inspect_flags_keyword_stuffed_title_and_hidden_text()
    {
        var issues = SpamGuard.Inspect("هدفون سونی | خرید هدفون سونی | هدفون سونی ارزان", null,
            "<p style=\"display:none\">هدفون سونی هدفون سونی</p><p>متن</p>", "هدفون سونی", Kb);
        Assert.Contains(issues, i => i.Code == "keyword-title" && i.Severity == 2);
        Assert.Contains(issues, i => i.Code == "hidden-text");
    }

    [Fact]
    public void Inspect_flags_keyword_density_above_the_limit_but_not_natural_text()
    {
        var filler = string.Join(' ', Enumerable.Range(0, 90).Select(i => "واژه" + (char)('آ' + i % 20)));
        var stuffed = "<p>" + string.Join(' ', Enumerable.Repeat("هدفون سونی", 15)) + " " + filler + "</p>";
        Assert.Contains(SpamGuard.Inspect(null, null, stuffed, "هدفون سونی", Kb), i => i.Code == "density");

        var natural = "<p>هدفون سونی " + filler + "</p>";
        Assert.DoesNotContain(SpamGuard.Inspect(null, null, natural, "هدفون سونی", Kb), i => i.Code == "density");
        Assert.True(SpamGuard.KeywordDensity(PersianTextOf(stuffed), "هدفون سونی") > SpamGuard.MaxDensity);
        Assert.Equal(0, SpamGuard.KeywordDensity("", "هدفون"));
    }

    private static string PersianTextOf(string html) => Fratak_Shop.Core.Utilities.PersianText.HtmlToText(html);

    [Fact]
    public void Inspect_of_a_clean_page_is_empty()
    {
        var issues = SpamGuard.Inspect("هدفون بی‌سیم سونی WH-1000XM5", "خرید هدفون بی‌سیم سونی با حذف نویز و ۳۰ ساعت شارژدهی.",
            "<p>هدفون سونی با حذف نویز فعال برای سفر و کار مناسب است.</p>", "هدفون سونی", Kb);
        Assert.Empty(issues);
    }

    // ── ویراستار فارسی ───────────────────────────────────────────────

    [Fact]
    public void Polish_fixes_spelling_and_wordiness_and_reports_changes()
    {
        var result = PersianWriting.Polish("لازم به ذکر است که باطری این گوشی قوی است", Kb);
        Assert.Contains("باتری", result.Text);
        Assert.DoesNotContain("باطری", result.Text);
        Assert.DoesNotContain("لازم به ذکر است", result.Text);
        Assert.Contains(result.Changes, c => c.ToString().Contains("باطری"));
    }

    [Fact]
    public void Polish_never_touches_urls_emails_or_empty_input()
    {
        var result = PersianWriting.Polish("راهنما: https://faratek.ir/blog/a_b-c?x=1 و info@faratek.ir", Kb);
        Assert.Contains("https://faratek.ir/blog/a_b-c?x=1", result.Text);
        Assert.Contains("info@faratek.ir", result.Text);
        Assert.Equal("", PersianWriting.Polish(null, Kb).Text);
        Assert.Equal("", PersianWriting.Polish("  ", Kb).Text);
    }

    [Fact]
    public void Polish_normalizes_arabic_letters()
    {
        Assert.Equal("کتابی", PersianWriting.Letters("كتابي"));
    }

    [Fact]
    public void Rejected_rule_is_only_suggested_not_applied()
    {
        // کارشناس‌ها این اصلاح را سه بار رد کرده‌اند ← اطمینان زیر ۰٫۷ ← فقط یادداشت.
        var kb = new SeoKnowledgeBase([new KnowledgeRow(SeoKnowledgeKind.Correction, "باطری", "باتری", "املا", 1, Rejections: 3)]);
        var result = PersianWriting.Polish("باطری گوشی", kb);
        Assert.Contains("باطری", result.Text);
        Assert.Contains(result.Notes, n => n.Contains("باتری"));
    }

    // ── پیشنهاد هوشمند ───────────────────────────────────────────────

    [Fact]
    public void Product_name_variants_are_clean_distinct_and_at_most_three()
    {
        var result = TextRefiner.Refine(Request(SmartPurpose.ProductName, "🔥 گوشی سامسونگ گلکسی A55 ارزان‌ترین!!!"), Kb);
        Assert.InRange(result.Variants.Count, 1, 3);
        Assert.Equal(result.Variants.Count, result.Variants.Select(v => v.Id).Distinct().Count());
        Assert.All(result.Variants, v =>
        {
            Assert.DoesNotContain("ارزان", v.Text);
            Assert.DoesNotContain("🔥", v.Text);
            Assert.DoesNotContain("!!", v.Text);
            Assert.Contains("A55", v.Text);
            Assert.False(v.IsHtml);
            Assert.InRange(v.Score, 0, 100);
            Assert.False(string.IsNullOrWhiteSpace(v.Title));
        });
        Assert.InRange(result.Before.Score, 0, 100);
    }

    [Fact]
    public void Seo_title_variants_fit_the_title_budget_and_contain_the_keyword()
    {
        var result = TextRefiner.Refine(Request(SmartPurpose.SeoTitle, "گوشی خوب سامسونگ مدل گلکسی A55 با دوربین عالی و باتری قوی و صفحه نمایش بزرگ", "گوشی سامسونگ A55"), Kb);
        Assert.NotEmpty(result.Variants);
        Assert.All(result.Variants, v => Assert.True(v.Text.Length <= Store.TitleBudget, $"{v.Id}: {v.Text.Length} > {Store.TitleBudget}"));
        Assert.Contains(result.Variants, v => v.Text.Contains("A55"));
    }

    [Fact]
    public void Body_variants_are_html_and_strip_scripts()
    {
        var html = "<p>این گوشی خیلی خوبه و باطری اش قوی است.</p><script>alert(1)</script><p onclick=\"x()\">لازم به ذکر است که گارانتی دارد.</p>";
        var result = TextRefiner.Refine(Request(SmartPurpose.Body, html, "گوشی سامسونگ"), Kb);
        Assert.InRange(result.Variants.Count, 1, 3);
        Assert.All(result.Variants, v =>
        {
            Assert.True(v.IsHtml);
            Assert.DoesNotContain("<script", v.Text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("onclick", v.Text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("باطری", v.Text);
        });
    }

    [Fact]
    public void Long_plain_body_becomes_paragraphs()
    {
        var lines = Enumerable.Range(1, 12).Select(i =>
            $"بخش {i}: این هدفون برای استفادهٔ روزانه طراحی شده و صدای شفاف، باس متعادل و اتصال پایدار دارد و در سفر هم همراه خوبی است.");
        var result = TextRefiner.Refine(Request(SmartPurpose.Body, string.Join("\n", lines), "هدفون سونی", "blog"), Kb);
        Assert.NotEmpty(result.Variants);
        Assert.All(result.Variants, v => Assert.Contains("<p", v.Text));
    }

    [Fact]
    public void Empty_plain_field_returns_a_warning_instead_of_inventing_text()
    {
        var result = TextRefiner.Refine(new SmartTextRequest { Purpose = SmartPurpose.Plain, Text = "  ", Store = Store }, Kb);
        Assert.Empty(result.Variants);
        Assert.NotEmpty(result.Warnings);
    }

    [Fact]
    public void Lead_with_keyword_and_plain_to_html_helpers()
    {
        Assert.Equal("هدفون سونی برای سفر عالی است", TextRefiner.LeadWithKeyword("این محصول برای سفر عالی است", "هدفون سونی"));
        Assert.Equal("هدفون سونی: برای سفر عالی است", TextRefiner.LeadWithKeyword("برای سفر عالی است", "هدفون سونی"));
        Assert.Equal("هدفون سونی خوب است", TextRefiner.LeadWithKeyword("هدفون سونی خوب است", "هدفون سونی"));
        Assert.Equal("<p>الف</p><p>ب &lt;</p>", TextRefiner.PlainToHtml("الف\r\n\r\n  ب <"));
        Assert.True(TextRefiner.LooksLikeHtml("<p>x</p>"));
        Assert.False(TextRefiner.LooksLikeHtml("۲ < ۳"));
    }

    [Theory]
    [InlineData(SmartPurpose.ProductName, "product-name")]
    [InlineData(SmartPurpose.SeoTitle, "seo-title")]
    [InlineData(SmartPurpose.MetaDescription, "meta")]
    [InlineData(SmartPurpose.ShortText, "short")]
    [InlineData(SmartPurpose.Body, "body")]
    [InlineData(SmartPurpose.Plain, "plain")]
    public void Purpose_keys_are_stable(SmartPurpose purpose, string key)
    {
        Assert.Equal(key, SmartTextService.PurposeKey(purpose));
    }

    // ── صفحه‌های ثابت و الگوها ─────────────────────────────────────────

    [Fact]
    public void Static_pages_have_unique_paths_and_stable_ids()
    {
        Assert.Contains(StaticSeoPages.All, p => p.Page == SitePage.Home && p.Path == "/");
        Assert.Equal(StaticSeoPages.All.Count, StaticSeoPages.All.Select(p => p.Path).Distinct().Count());
        foreach (var page in StaticSeoPages.All)
        {
            var id = StaticSeoPages.IdOf(page.Page);
            Assert.Same(page, StaticSeoPages.Find(id));
            Assert.Same(page, StaticSeoPages.Find(page.Page));
            Assert.NotEqual(page.Page.ToString(), StaticSeoPages.Label(page.Page));
        }
        Assert.Equal(Guid.Parse("5e0a0000-0000-0000-0000-000000000001"), StaticSeoPages.IdOf((SitePage)1));
        Assert.Null(StaticSeoPages.Find(Guid.NewGuid()));
    }

    [Fact]
    public void Static_page_defaults_come_from_store_settings()
    {
        var settings = new StoreSettingDto { SiteName = "فراتک", Tagline = "فروشگاه لوازم دیجیتال" };
        var home = StaticSeoPages.Defaults(StaticSeoPages.Find(SitePage.Home)!, settings);
        Assert.Equal("فروشگاه لوازم دیجیتال", home.Name);
        var blog = StaticSeoPages.Defaults(StaticSeoPages.Find(SitePage.Blog)!, settings);
        Assert.Contains("فراتک", blog.Name);
        Assert.All(StaticSeoPages.All, p => Assert.False(string.IsNullOrWhiteSpace(StaticSeoPages.Defaults(p, settings).Title)));
    }

    [Fact]
    public void Learned_title_patterns_are_generic_only()
    {
        Assert.Equal("قیمت و خرید {name}", SeoEngineService.PatternOf("قیمت و خرید هدفون سونی XM5", "هدفون سونی XM5"));
        Assert.Null(SeoEngineService.PatternOf("قیمت و خرید هدفون", "کیبورد"));
        Assert.Null(SeoEngineService.PatternOf("عنوان", ""));

        var subject = new SeoSubject
        {
            Kind = SeoEntityKind.Product,
            EntityId = Guid.NewGuid(),
            Name = "هدفون سونی XM5 مشکی",
            Slug = "xm5",
            PathPrefix = "/product/",
            DefaultTitle = "هدفون سونی XM5 مشکی",
        };
        Assert.True(SeoEngineService.IsGenericPattern("خرید {name} با ارسال سریع", subject));
        Assert.False(SeoEngineService.IsGenericPattern("{name} مشکی | قیمت", subject));
        Assert.False(SeoEngineService.IsGenericPattern("خرید {name} 2025", subject));
        Assert.False(SeoEngineService.IsGenericPattern("{name} و {name}", subject));
    }

    [Theory]
    [InlineData(SeoEntityKind.Product, "product")]
    [InlineData(SeoEntityKind.Category, "category")]
    [InlineData(SeoEntityKind.Brand, "brand")]
    [InlineData(SeoEntityKind.BlogPost, "blog")]
    [InlineData(SeoEntityKind.StaticPage, "home")]
    [InlineData(SeoEntityKind.ContentPage, "page")]
    public void Page_type_codes_match_seed_topics(SeoEntityKind kind, string code)
    {
        Assert.Equal(code, SeoEngineService.PageTypeOf(kind));
    }

    // ── کدهای کمکی پنل ─────────────────────────────────────────────

    [Fact]
    public void Smart_context_json_carries_kind_id_and_selectors()
    {
        var id = Guid.NewGuid();
        var json = SmartUi.Context("Product", id, new { name = "#Name" });
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        Assert.Equal("Product", doc.RootElement.GetProperty("kind").GetString());
        Assert.Equal(id, doc.RootElement.GetProperty("id").GetGuid());
        Assert.Equal("#Name", doc.RootElement.GetProperty("name").GetString());

        using var fresh = System.Text.Json.JsonDocument.Parse(SmartUi.Context("BlogPost", null));
        Assert.Equal(System.Text.Json.JsonValueKind.Null, fresh.RootElement.GetProperty("id").ValueKind);
    }

    [Theory]
    [InlineData("categories", "Category")]
    [InlineData("brands", "Brand")]
    [InlineData("content-pages", "ContentPage")]
    [InlineData("page-seo", "StaticPage")]
    [InlineData("faqs", null)]
    public void Manage_tables_map_to_seo_kinds(string key, string? kind)
    {
        Assert.Equal(kind, SmartUi.ManageKind(key));
        if (kind is not null)
            Assert.True(Enum.TryParse<SeoEntityKind>(kind, out _));
    }

    [Fact]
    public void Smart_permissions_are_known_and_match_the_navigation()
    {
        Assert.All(SmartUi.Permissions, p => Assert.True(Fratak_Shop.Core.Services.Admin.AdminPermissions.IsKnown(p), p));
        var nav = Fratak_Shop.Web.Infrastructure.Admin.AdminNav.Groups.SelectMany(g => g.Items).Single(i => i.Url == "/admin/seo/smart");
        Assert.Equal(SmartUi.Permissions.OrderBy(p => p), nav.AnyOf!.OrderBy(p => p));
    }

    [Fact]
    public void Knowledge_page_has_a_persian_title_for_every_kind_and_source()
    {
        foreach (var kind in Enum.GetValues<SeoKnowledgeKind>().Where(k => k is not (SeoKnowledgeKind.None or SeoKnowledgeKind.Meta)))
            Assert.NotEqual(kind.ToString(), KnowledgeModel.KindTitle(kind));
        foreach (var source in Enum.GetValues<SeoKnowledgeSource>().Where(s => s != SeoKnowledgeSource.None))
            Assert.NotEqual("—", KnowledgeModel.SourceTitle(source));
        Assert.DoesNotContain(SeoKnowledgeKind.Meta, KnowledgeModel.ManualKinds);
        Assert.DoesNotContain(SeoKnowledgeKind.Pattern, KnowledgeModel.ManualKinds);
    }
}
