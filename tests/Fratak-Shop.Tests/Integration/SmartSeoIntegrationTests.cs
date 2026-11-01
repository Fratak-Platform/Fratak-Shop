using Fratak_Shop.Core.Services.Seo.Engine;
using Fratak_Shop.Core.Services.Seo.Knowledge;
using Fratak_Shop.Core.Services.Seo.Smart;
using Fratak_Shop.Core.Services.Seo.Tools;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Pages;
using Fratak_Shop.Data.Entities.SeoEngine;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Fratak_Shop.Tests.Integration;

/// <summary>
/// تست‌های مرحلهٔ شانزدهم روی SQL Server واقعی (منبع اینترنتی آفلاین):
/// • جدول دانش: افزودن دستی، فهرست/جست‌وجو، غیرفعال کردن، اثر فوری روی ویراستار، یادگیری از تأیید/رد، عبارت‌های تحقیق.
/// • بذر دادهٔ اولیه: یک بار و بدون تکرار.
/// • سئوی هوشمند درجا: محصول تازه‌ای که هنوز ذخیره نشده (هیچ چیز ذخیره نمی‌شود) و صفحهٔ اصلی.
/// • صفحه‌های ثابت در موتور (پیشنهاد ← تأیید ← PageSeo) و در ویرایش گروهی.
/// • «پیشنهاد هوشمند» متن و ثبت بازخورد بدون ذخیرهٔ خود متن.
/// </summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", "Integration")]
public class SmartSeoIntegrationTests(SqlServerFixture db)
{
    private const string Letters = "ابپتثجچحخدذرزژسشصضطظعغفقکگلمنوهی";

    /// <summary>واژهٔ فارسی تصادفی (برای اینکه ردیف‌های هر اجرا با هم قاطی نشوند).</summary>
    /// «ژ» در دو سر واژه تا هیچ قاعدهٔ پیشوند/پسوند («می‌»، «ـها») رویش اثر نگذارد.
    private static string Word(int length = 6) =>
        "ژ" + new string(Enumerable.Range(0, length).Select(_ => Letters[Random.Shared.Next(Letters.Length)]).ToArray()) + "ژ";

    private ISeoKnowledgeService Knowledge => db.Services.GetRequiredService<ISeoKnowledgeService>();

    private static FratakShopDbContext Ctx(AsyncServiceScope scope) => scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();

    // ── جدول دانش ────────────────────────────────────────────────────

    [SqlFact]
    public async Task Manual_rows_are_saved_listed_disabled_and_used_immediately()
    {
        var term = Word();
        Assert.Null(await Knowledge.SaveManualAsync(SeoKnowledgeKind.Correction, term, "درست", "املا", 1));

        var kb = await Knowledge.GetAsync();
        Assert.Contains(kb.Corrections, r => r.Term == term && r.Value == "درست");
        Assert.Contains("درست", PersianWriting.Polish(term + " است", kb).Text);

        var list = await Knowledge.ListAsync(new SeoKnowledgeQuery(SeoKnowledgeKind.Correction, SeoKnowledgeSource.Manual, term, null));
        var item = Assert.Single(list.Items);
        Assert.Equal(1, list.Total);
        Assert.True(item.IsActive);
        Assert.Equal("املا", item.Topic);

        // ذخیرهٔ دوباره همان عبارت = به‌روزرسانی، نه ردیف تکراری
        Assert.Null(await Knowledge.SaveManualAsync(SeoKnowledgeKind.Correction, term, "صحیح", "املا", 5));
        list = await Knowledge.ListAsync(new SeoKnowledgeQuery(SeoKnowledgeKind.Correction, null, term, null));
        item = Assert.Single(list.Items);
        Assert.Equal("صحیح", item.Value);
        Assert.Equal(1.5, item.Weight, 3); // وزن به سقف ۱٫۵ محدود می‌شود

        // غیرفعال ← دیگر استفاده نمی‌شود
        Assert.True(await Knowledge.SetActiveAsync(item.Id, false));
        kb = await Knowledge.GetAsync();
        Assert.DoesNotContain(kb.Corrections, r => r.Term == term);
        Assert.Contains(term, PersianWriting.Polish(term + " است", kb).Text);
        var inactive = await Knowledge.ListAsync(new SeoKnowledgeQuery(null, null, term, false));
        Assert.Contains(inactive.Items, i => i.Id == item.Id);
        Assert.DoesNotContain((await Knowledge.ListAsync(new SeoKnowledgeQuery(null, null, term, true))).Items, i => i.Id == item.Id);

        Assert.False(await Knowledge.SetActiveAsync(Guid.NewGuid(), true));
    }

    [SqlTheory]
    [InlineData(SeoKnowledgeKind.Meta, "نسخه", null)]
    [InlineData(SeoKnowledgeKind.Correction, "ب", null)]
    [InlineData(SeoKnowledgeKind.Correction, "   ", null)]
    [InlineData((SeoKnowledgeKind)250, "عبارت", null)]
    public async Task Invalid_manual_rows_are_rejected(SeoKnowledgeKind kind, string term, string? value)
    {
        var error = await Knowledge.SaveManualAsync(kind, term, value, null, 1);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [SqlFact]
    public async Task Too_long_manual_value_is_rejected()
    {
        Assert.NotNull(await Knowledge.SaveManualAsync(SeoKnowledgeKind.Synonym, Word(), new string('ا', 1001), null, 1));
    }

    [SqlFact]
    public async Task Feedback_updates_rule_counters_and_learns_keywords_and_patterns()
    {
        var term = Word();
        var keyword = "هدفون " + Word();
        Assert.Null(await Knowledge.SaveManualAsync(SeoKnowledgeKind.Correction, term, "درست", "املا", 1));

        await Knowledge.RecordOutcomeAsync(new SeoKnowledgeFeedback
        {
            Accepted = false,
            RuleIds = ["7:" + term, "bad-id", "250:x"],
            Keyword = keyword,
            KeywordGroup = "product",
            Patterns = ["خرید {name} با ارسال سریع"],
            PatternTopic = "product",
        });
        await Knowledge.RecordOutcomeAsync(new SeoKnowledgeFeedback { Accepted = true, Keyword = keyword, KeywordGroup = "product" });

        await using (var scope = db.Scope())
        {
            var context = Ctx(scope);
            var rule = await context.SeoKnowledgeEntries.AsNoTracking().SingleAsync(e => e.Kind == SeoKnowledgeKind.Correction && e.Term == term);
            Assert.Equal(1, rule.Rejections);
            Assert.Equal(1, rule.Uses);

            var learned = await context.SeoKnowledgeEntries.AsNoTracking().SingleAsync(e => e.Kind == SeoKnowledgeKind.Keyword && e.Term == keyword);
            Assert.Equal(SeoKnowledgeSource.Learned, learned.Source);
            Assert.Equal("product", learned.Topic);
            Assert.Equal(1, learned.Successes);
            Assert.Equal(1, learned.Rejections);

            Assert.True(await context.SeoKnowledgeEntries.AnyAsync(e => e.Kind == SeoKnowledgeKind.Pattern && e.Term == "خرید {name} با ارسال سریع"));
        }

        Knowledge.Invalidate();
        var kb = await Knowledge.GetAsync();
        Assert.Equal(Math.Round(0.9 * Math.Log2(2) - 1.2, 2), kb.KeywordBonus(keyword), 2);

        var stats = await Knowledge.GetStatsAsync();
        Assert.True(stats.Learned >= 1);
        Assert.True(stats.Manual >= 1);
        Assert.NotNull(stats.LastLearnedAt);
    }

    [SqlFact]
    public async Task Research_phrases_are_stored_without_spam_and_capped_per_topic()
    {
        var topic = "موضوع " + Word();
        var topicKey = PersianText.Normalize(topic);
        await Knowledge.RecordResearchAsync(topic, ["قیمت هدفون", "قیمت هدفون", "فقط امروز تخفیف", "ب", "هدفون 🔥 سونی"]);

        await using (var scope = db.Scope())
        {
            var rows = await Ctx(scope).SeoKnowledgeEntries.AsNoTracking()
                .Where(e => e.Kind == SeoKnowledgeKind.Phrase && e.Topic == topicKey).ToListAsync();
            var row = Assert.Single(rows);
            Assert.Equal("قیمت هدفون", row.Term);
            Assert.Equal(SeoKnowledgeSource.Research, row.Source);
        }

        await Knowledge.RecordResearchAsync(topic, ["قیمت هدفون"]);
        await Knowledge.RecordResearchAsync(topic, Enumerable.Range(0, 40).Select(_ => "هدفون " + Word(6)).ToList());
        await Knowledge.RecordResearchAsync(topic, Enumerable.Range(0, 40).Select(_ => "کیبورد " + Word(6)).ToList());

        await using (var scope = db.Scope())
        {
            var rows = await Ctx(scope).SeoKnowledgeEntries.AsNoTracking()
                .Where(e => e.Kind == SeoKnowledgeKind.Phrase && e.Topic == topicKey).ToListAsync();
            Assert.True(rows.Count <= SeoKnowledgeService.MaxPhrasesPerTopic, $"{rows.Count}");
            // پرتکرارترین عبارت (وزن ۲) کنار نمی‌رود.
            Assert.Contains(rows, r => r.Term == "قیمت هدفون" && r.Weight >= 2);
        }
    }

    [SqlFact]
    public async Task Seeder_loads_the_embedded_dataset_once()
    {
        await using (var scope = db.Scope())
        {
            await SeoKnowledgeSeeder.EnsureSeededAsync(Ctx(scope), NullLogger.Instance);
        }
        await using (var scope = db.Scope())
        {
            Assert.Equal(0, await SeoKnowledgeSeeder.EnsureSeededAsync(Ctx(scope), NullLogger.Instance));
            Assert.Equal(SeoKnowledgeSeed.SeedVersion, await SeoKnowledgeSeeder.CurrentVersionAsync(Ctx(scope), default));
        }

        var stats = await Knowledge.GetStatsAsync();
        Assert.True(stats.Seeded);
        // واژه‌نامهٔ عمومی (ردیف‌های Known) فقط در حافظه است؛ در جدول، قاعده‌ها و واژه‌های ویژه ذخیره می‌شوند.
        var stored = SeoKnowledgeSeed.StoredRows.Count();
        Assert.True(stats.Total >= stored * 0.95, $"{stats.Total} < {stored}");
        Assert.Contains(stats.ByKind, k => k.Kind == SeoKnowledgeKind.Correction && k.Total > 0);

        Knowledge.Invalidate();
        var kb = await Knowledge.GetAsync();
        Assert.Contains(kb.Corrections, r => r.Term == "باطری");
    }

    // ── سئوی هوشمند درجا ───────────────────────────────────────────────

    [SqlFact]
    public async Task Draft_for_an_unsaved_product_suggests_seo_and_saves_nothing()
    {
        int products, suggestions;
        await using (var scope = db.Scope())
        {
            products = await Ctx(scope).Products.IgnoreQueryFilters().CountAsync();
            suggestions = await Ctx(scope).SeoSuggestions.IgnoreQueryFilters().CountAsync();
        }

        SeoDraft draft;
        await using (var scope = db.Scope())
        {
            draft = await scope.ServiceProvider.GetRequiredService<ISeoEngineService>().DraftAsync(new SeoDraftInput
            {
                Kind = SeoEntityKind.Product,
                Name = "هدفون بی‌سیم سونی مدل WH-1000XM5",
                EnglishName = "Sony WH-1000XM5",
                ShortText = "هدفون رو گوشی با حذف نویز فعال و ۳۰ ساعت شارژدهی",
                WarrantyTitle = "گارانتی ۱۸ ماهه",
                WarrantyMonths = 18,
            });
        }

        Assert.Empty(draft.Errors);
        Assert.NotEmpty(draft.Titles);
        Assert.NotEmpty(draft.Metas);
        Assert.False(string.IsNullOrWhiteSpace(draft.Keyword));
        Assert.True(draft.ScoreAfter >= draft.ScoreBefore, $"{draft.ScoreBefore} → {draft.ScoreAfter}");
        Assert.All(draft.Titles, t => Assert.InRange(t.Text.Length, 1, 70));
        Assert.All(draft.Metas, m => Assert.InRange(m.Text.Length, 1, 170));

        await using (var scope = db.Scope())
        {
            Assert.Equal(products, await Ctx(scope).Products.IgnoreQueryFilters().CountAsync());
            Assert.Equal(suggestions, await Ctx(scope).SeoSuggestions.IgnoreQueryFilters().CountAsync());
        }
    }

    [SqlFact]
    public async Task Draft_without_a_name_or_static_page_returns_a_clear_error()
    {
        await using var scope = db.Scope();
        var engine = scope.ServiceProvider.GetRequiredService<ISeoEngineService>();
        Assert.NotEmpty((await engine.DraftAsync(new SeoDraftInput { Kind = SeoEntityKind.Product, Name = "  " })).Errors);
        Assert.NotEmpty((await engine.DraftAsync(new SeoDraftInput { Kind = SeoEntityKind.StaticPage })).Errors);
    }

    [SqlFact]
    public async Task Draft_for_the_home_page_uses_the_fixed_page()
    {
        await using var scope = db.Scope();
        var draft = await scope.ServiceProvider.GetRequiredService<ISeoEngineService>()
            .DraftAsync(new SeoDraftInput { Kind = SeoEntityKind.StaticPage, Page = SitePage.Home });
        Assert.Empty(draft.Errors);
        Assert.Equal("/", draft.Path);
        Assert.NotEmpty(draft.Titles);
        Assert.NotEmpty(draft.Metas);
    }

    // ── صفحه‌های ثابت در موتور و ویرایش گروهی ───────────────────────────

    [SqlFact]
    public async Task Engine_generates_and_applies_seo_for_a_fixed_page_and_learns_the_keyword()
    {
        var id = StaticSeoPages.IdOf(SitePage.CustomerClub);
        await using (var scope = db.Scope())
        {
            var context = Ctx(scope);
            var row = await context.PageSeos.AsTracking().FirstOrDefaultAsync(p => p.Page == SitePage.CustomerClub);
            if (row is not null)
            {
                row.Seo = null;
                await context.SaveChangesAsync();
            }
        }

        Guid suggestionId;
        await using (var scope = db.Scope())
        {
            var engine = scope.ServiceProvider.GetRequiredService<ISeoEngineService>();
            Assert.NotNull(await engine.AnalyzeAsync(SeoEntityKind.StaticPage, id));
            var result = await engine.GenerateAsync(SeoEntityKind.StaticPage, id);
            Assert.True(result.Status == EnOperationResultStatus.Success, result.Message);
            suggestionId = result.Data;
        }

        SeoProposal proposal;
        SeoFields fields;
        await using (var scope = db.Scope())
        {
            var suggestion = await Ctx(scope).SeoSuggestions.AsNoTracking().SingleAsync(s => s.Id == suggestionId);
            proposal = SeoProposal.FromJson(suggestion.ProposalJson);
            fields = suggestion.Fields & (SeoFields.Title | SeoFields.MetaDescription | SeoFields.FocusKeyword);
            Assert.NotEqual(SeoFields.None, fields);
            var approved = await scope.ServiceProvider.GetRequiredService<ISeoEngineService>().ApproveAsync(suggestionId, fields, null, "تست");
            Assert.True(approved.Status == EnOperationResultStatus.Success, approved.Message);
        }

        await using (var scope = db.Scope())
        {
            var context = Ctx(scope);
            var page = await context.PageSeos.AsNoTracking().SingleAsync(p => p.Page == SitePage.CustomerClub);
            if (fields.HasFlag(SeoFields.MetaDescription))
                Assert.Equal(proposal.MetaDescription, page.Seo?.MetaDescription);
            if (fields.HasFlag(SeoFields.Title))
                Assert.Equal(proposal.Title, page.Seo?.Title);
            if (fields.HasFlag(SeoFields.FocusKeyword))
            {
                Assert.Equal(proposal.FocusKeyword, page.Seo?.FocusKeyword);
                var keyword = PersianText.Clean(proposal.FocusKeyword);
                Assert.True(await context.SeoKnowledgeEntries.AnyAsync(e =>
                    e.Kind == SeoKnowledgeKind.Keyword && e.Term == keyword && e.Topic == "home" && e.Successes >= 1));
            }
            Assert.True(page.Seo?.IsReviewed);
        }
    }

    [SqlFact]
    public async Task Bulk_edit_lists_every_fixed_page_and_saves_one()
    {
        await using (var scope = db.Scope())
        {
            var bulk = scope.ServiceProvider.GetRequiredService<ISeoBulkEditService>();
            var list = await bulk.ListAsync(new SeoBulkQuery(SeoEntityKind.StaticPage, "all", null, 1, 50));
            Assert.Equal(StaticSeoPages.All.Count, list.Total);
            Assert.Contains(list.Items, r => r.Path == "/" && r.Id == StaticSeoPages.IdOf(SitePage.Home));

            var faq = list.Items.Single(r => r.Id == StaticSeoPages.IdOf(SitePage.Faq));
            var saved = await bulk.SaveAsync(SeoEntityKind.StaticPage,
                [new SeoBulkChange(faq.Id, faq.SeoTitle, "پرسش‌های پرتکرار خرید و ارسال", faq.MetaDescription, faq.MetaDescription, faq.FocusKeyword, faq.FocusKeyword)],
                null);
            Assert.True(saved.Status == EnOperationResultStatus.Success, saved.Message);
            Assert.Equal(1, saved.Data!.Saved);
        }

        await using (var scope = db.Scope())
        {
            var row = await Ctx(scope).PageSeos.AsNoTracking().SingleAsync(p => p.Page == SitePage.Faq);
            Assert.Equal("پرسش‌های پرتکرار خرید و ارسال", row.Seo?.Title);
            Assert.True(await Ctx(scope).SeoScores.AnyAsync(s => s.Kind == SeoEntityKind.StaticPage && s.EntityId == StaticSeoPages.IdOf(SitePage.Faq)));
        }
    }

    // ── پیشنهاد هوشمند متن ───────────────────────────────────────────

    [SqlFact]
    public async Task Smart_text_suggests_variants_and_feedback_stores_only_a_hash()
    {
        SmartTextResult result;
        await using (var scope = db.Scope())
        {
            result = await scope.ServiceProvider.GetRequiredService<ISmartTextService>().SuggestAsync(new SmartTextInput
            {
                Purpose = SmartPurpose.ProductName,
                Text = "🔥 گوشی سامسونگ گلکسی A55 ارزان‌ترین!!!",
                Kind = SeoEntityKind.Product,
            });
        }
        Assert.InRange(result.Variants.Count, 1, 3);
        Assert.All(result.Variants, v => Assert.DoesNotContain("ارزان", v.Text));

        var marker = "v-" + Guid.NewGuid().ToString("N")[..12];
        const string secret = "متن خصوصی کاربر که نباید ذخیره شود";
        await using (var scope = db.Scope())
        {
            await scope.ServiceProvider.GetRequiredService<ISmartTextService>().RecordFeedbackAsync(new SmartTextFeedbackInput
            {
                Purpose = SmartPurpose.ProductName,
                Variant = marker,
                Accepted = true,
                RuleIds = result.Variants[0].RuleIds,
                Input = secret,
                OutputLength = result.Variants[0].Text.Length,
            }, null);
        }

        await using (var scope = db.Scope())
        {
            var row = await Ctx(scope).SmartTextFeedbacks.AsNoTracking().SingleAsync(f => f.Variant == marker);
            Assert.Equal("product-name", row.Purpose);
            Assert.True(row.Accepted);
            Assert.Equal(secret.Length, row.InputLength);
            Assert.NotNull(row.InputHash);
            Assert.DoesNotContain("متن", row.InputHash);
            Assert.Equal(32, row.InputHash!.Length);
        }
    }
}
