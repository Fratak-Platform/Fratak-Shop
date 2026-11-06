using Fratak_Shop.Core.Services.Products;
using Fratak_Shop.Core.Services.Seo.Engine;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.SeoEngine;
using Fratak_Shop.Data.Utilities.SeoManagement;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Fratak_Shop.Tests.Integration;

/// <summary>
/// تست موتور هوشمند سئو روی SQL Server واقعی (مرحلهٔ هفتم – فاز ۸). منبع اینترنتی در این تست‌ها «آفلاین» است
/// (OfflineSeoWebSource)؛ یعنی همه چیز با داده‌های داخلی ساخته می‌شود — همان حالتی که سرور به گوگل/بینگ دسترسی ندارد.
/// • ساخت پیشنهاد ← تأیید بخشی (فقط بخش‌های تیک‌خورده اعمال می‌شوند) ← نمرهٔ تازه.
/// • رد با دلیل ← نسخهٔ تازه که متن ردشده را تکرار نمی‌کند.
/// • ویرایش صفحه بعد از پیشنهاد ← پیشنهاد کهنه و غیرقابل اعمال.
/// • تغییر نامک ← ریدایرکت ۳۰۱ از آدرس قبلی.
/// • اجرای زمان‌بندی‌شده: هر نوبت فقط یک بار.
/// • کپی محصول، پرسش‌وپاسخ سئو را کپی نمی‌کند.
/// </summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", "Integration")]
public class SeoEngineIntegrationTests(SqlServerFixture db)
{
    private static FratakShopDbContext Ctx(AsyncServiceScope scope) => scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();

    private static ISeoEngineService Engine(AsyncServiceScope scope) => scope.ServiceProvider.GetRequiredService<ISeoEngineService>();

    private async Task<SeoSuggestion> SuggestionAsync(Guid id)
    {
        await using var scope = db.Scope();
        return await Ctx(scope).SeoSuggestions.AsNoTracking().SingleAsync(s => s.Id == id);
    }

    private async Task<Guid> GenerateAsync(Guid productId)
    {
        await using var scope = db.Scope();
        var result = await Engine(scope).GenerateAsync(SeoEntityKind.Product, productId);
        Assert.True(result.Status == EnOperationResultStatus.Success, result.Message);
        return result.Data;
    }

    [SqlFact]
    public async Task Generated_suggestion_is_applied_only_for_the_approved_fields()
    {
        var productId = await db.CreateProductAsync();
        var suggestionId = await GenerateAsync(productId);

        var suggestion = await SuggestionAsync(suggestionId);
        var proposal = SeoProposal.FromJson(suggestion.ProposalJson);
        Assert.Equal(SeoSuggestionStatus.Pending, suggestion.Status);
        Assert.True(suggestion.ScoreAfter > suggestion.ScoreBefore, $"{suggestion.ScoreBefore} → {suggestion.ScoreAfter}");
        // محصول آزمایشی توضیحات متا و کلمهٔ کلیدی ندارد؛ عنوانش (نام محصول) از نظر طول و کلمه مشکلی ندارد، پس
        // موتور عنوان را عوض نمی‌کند (بخش‌های بی‌اثر هرس می‌شوند).
        Assert.True(suggestion.Fields.HasFlag(SeoFields.MetaDescription));
        Assert.True(suggestion.Fields.HasFlag(SeoFields.FocusKeyword));
        Assert.False(string.IsNullOrWhiteSpace(proposal.MetaDescription));
        Assert.NotEmpty(SeoJson.Deserialize<List<string>>(suggestion.ReasonsJson) ?? []);

        await using (var scope = db.Scope())
        {
            var approved = await Engine(scope).ApproveAsync(suggestionId, SeoFields.MetaDescription, null, "تست");
            Assert.Equal(EnOperationResultStatus.Success, approved.Status);
        }

        await using (var scope = db.Scope())
        {
            var context = Ctx(scope);
            var product = await context.Products.AsNoTracking().SingleAsync(p => p.Id == productId);
            Assert.Equal(proposal.MetaDescription, product.Seo?.MetaDescription);
            Assert.Null(product.Seo?.FocusKeyword); // تیک نخورده بود
            Assert.True(product.Seo?.IsReviewed);

            var applied = await context.SeoSuggestions.AsNoTracking().SingleAsync(s => s.Id == suggestionId);
            Assert.Equal(SeoSuggestionStatus.Applied, applied.Status);
            Assert.Equal(SeoFields.MetaDescription, applied.AppliedFields);
            Assert.Equal("تست", applied.ReviewedByName);

            var score = await context.SeoScores.AsNoTracking().SingleAsync(s => s.Kind == SeoEntityKind.Product && s.EntityId == productId);
            Assert.True(score.Score > suggestion.ScoreBefore, $"score {score.Score} should beat {suggestion.ScoreBefore}");
        }

        // دوباره تأیید کردن همان پیشنهاد مجاز نیست.
        await using (var scope = db.Scope())
            Assert.Equal(EnOperationResultStatus.Error, (await Engine(scope).ApproveAsync(suggestionId, SeoFields.All, null, "تست")).Status);
    }

    [SqlFact]
    public async Task Rejecting_regenerates_a_new_variant_that_avoids_the_rejected_text()
    {
        var productId = await db.CreateProductAsync();
        var firstId = await GenerateAsync(productId);
        var first = SeoProposal.FromJson((await SuggestionAsync(firstId)).ProposalJson);

        Guid? nextId;
        await using (var scope = db.Scope())
        {
            var rejected = await Engine(scope).RejectAsync(firstId, SeoFields.MetaDescription, "خیلی طولانی است", null, "تست");
            Assert.Equal(EnOperationResultStatus.Success, rejected.Status);
            nextId = rejected.Data;
        }

        var old = await SuggestionAsync(firstId);
        Assert.Equal(SeoSuggestionStatus.Rejected, old.Status);
        Assert.Equal(SeoFields.MetaDescription, old.RejectedFields);
        Assert.Equal("خیلی طولانی است", old.RejectReason);

        Assert.NotNull(nextId);
        var next = await SuggestionAsync(nextId!.Value);
        Assert.Equal(SeoSuggestionStatus.Pending, next.Status);
        Assert.Equal(old.Variant + 1, next.Variant);
        var second = SeoProposal.FromJson(next.ProposalJson);
        Assert.NotEqual(PersianText.Normalize(first.MetaDescription), PersianText.Normalize(second.MetaDescription));
        Assert.Equal(first.FocusKeyword, second.FocusKeyword); // بخش ردنشده همان نسخهٔ قبل می‌ماند
    }

    [SqlFact]
    public async Task Editing_the_page_after_generation_makes_the_suggestion_stale()
    {
        var productId = await db.CreateProductAsync();
        var suggestionId = await GenerateAsync(productId);

        await using (var scope = db.Scope())
        {
            var context = Ctx(scope);
            var product = await context.Products.AsTracking().SingleAsync(p => p.Id == productId);
            product.Seo = new SeoMetaData { Title = "عنوانی که کارشناس دستی نوشت" };
            await context.SaveChangesAsync();
        }

        await using (var scope = db.Scope())
        {
            var result = await Engine(scope).ApproveAsync(suggestionId, SeoFields.All, null, "تست");
            Assert.Equal(EnOperationResultStatus.Error, result.Status);
        }

        Assert.Equal(SeoSuggestionStatus.Stale, (await SuggestionAsync(suggestionId)).Status);
        await using (var scope = db.Scope())
        {
            var product = await Ctx(scope).Products.AsNoTracking().SingleAsync(p => p.Id == productId);
            Assert.Equal("عنوانی که کارشناس دستی نوشت", product.Seo?.Title); // متن انسانی دست نخورد
        }
    }

    [SqlFact]
    public async Task Slug_change_adds_a_permanent_redirect_from_the_old_address()
    {
        // نامک «test-<کد هگز>» بی‌معنی است و محصول تازه است → موتور نامک بهتر پیشنهاد می‌دهد.
        var productId = await db.CreateProductAsync();
        string oldSlug;
        await using (var scope = db.Scope())
            oldSlug = (await Ctx(scope).Products.AsNoTracking().SingleAsync(p => p.Id == productId)).Slug;

        var suggestionId = await GenerateAsync(productId);
        var proposal = SeoProposal.FromJson((await SuggestionAsync(suggestionId)).ProposalJson);
        Assert.False(string.IsNullOrWhiteSpace(proposal.Slug), "expected a slug proposal for a meaningless slug on a new product");
        Assert.True(Fratak_Shop.Data.Utilities.EnglishSlug.IsEnglish(proposal.Slug), proposal.Slug); // دور سئوی خودکار: نامک فقط انگلیسی
        Assert.DoesNotContain(oldSlug[5..], proposal.Slug!, StringComparison.OrdinalIgnoreCase); // کد هگز بی‌معنی تکرار نمی‌شود

        await using (var scope = db.Scope())
            Assert.Equal(EnOperationResultStatus.Success, (await Engine(scope).ApproveAsync(suggestionId, SeoFields.Slug, null, "تست")).Status);

        await using (var scope = db.Scope())
        {
            var context = Ctx(scope);
            var product = await context.Products.AsNoTracking().SingleAsync(p => p.Id == productId);
            Assert.Equal(proposal.Slug, product.Slug);

            var redirect = await context.SeoRedirects.AsNoTracking().SingleAsync(r => r.FromPath == "/product/" + oldSlug.ToLowerInvariant());
            Assert.Equal("/product/" + Uri.EscapeDataString(proposal.Slug!), redirect.ToUrl);
            Assert.True(redirect.IsActive);
            Assert.All(redirect.ToUrl, ch => Assert.True(ch < 128, "Location header must be ASCII"));
        }
    }

    [SqlFact]
    public async Task Scheduled_slot_runs_only_once_and_scores_every_page()
    {
        var productId = await db.CreateProductAsync();
        var slot = "test-" + Guid.NewGuid().ToString("N");

        await using (var scope = db.Scope())
        {
            var engine = Engine(scope);
            Assert.False(await engine.HasRunAsync(slot));
            var first = await engine.RunAsync(SeoRunTrigger.Scheduled, slot);
            Assert.True(first.Ran, first.Message);
            Assert.True(first.Analyzed > 0);
        }

        await using (var scope = db.Scope())
        {
            var engine = Engine(scope);
            Assert.True(await engine.HasRunAsync(slot));
            var again = await engine.RunAsync(SeoRunTrigger.Scheduled, slot);
            Assert.False(again.Ran);

            var context = Ctx(scope);
            Assert.True(await context.SeoScores.AnyAsync(s => s.Kind == SeoEntityKind.Product && s.EntityId == productId));
            var run = await context.SeoEngineRuns.AsNoTracking().SingleAsync(r => r.SlotKey == slot);
            Assert.NotNull(run.FinishedAt);
            Assert.Equal(SeoRunTrigger.Scheduled, run.Trigger);

            var dashboard = await engine.GetDashboardAsync();
            Assert.True(dashboard.Pages > 0);
            Assert.Contains(dashboard.Runs, r => r.SlotKey == slot);
        }
    }

    [SqlFact]
    public async Task Clone_does_not_copy_the_seo_faq_or_review_state()
    {
        var productId = await db.CreateProductAsync();
        await using (var scope = db.Scope())
        {
            var context = Ctx(scope);
            var product = await context.Products.AsTracking().SingleAsync(p => p.Id == productId);
            product.Seo = new SeoMetaData
            {
                Title = "عنوان سئو",
                FaqJson = SeoFaq.Serialize([new SeoFaqItem("گارانتی دارد؟", "بله، ۱۸ ماه.")]),
                IsReviewed = true,
                ReviewedAt = DateTimeOffset.UtcNow,
            };
            await context.SaveChangesAsync();
        }

        Guid copyId;
        await using (var scope = db.Scope())
        {
            var result = await new ProductCloneService(Ctx(scope)).CloneAsync(productId, new Dictionary<string, string>());
            Assert.Equal(EnOperationResultStatus.Success, result.Status);
            copyId = result.Data;
        }

        await using (var scope = db.Scope())
        {
            var copy = await Ctx(scope).Products.AsNoTracking().SingleAsync(p => p.Id == copyId);
            Assert.Null(copy.Seo?.FaqJson);
            Assert.False(copy.Seo?.IsReviewed ?? false);
        }
    }

    [SqlFact]
    public async Task Analyze_returns_report_and_history_for_a_page()
    {
        var productId = await db.CreateProductAsync();
        await GenerateAsync(productId);

        await using var scope = db.Scope();
        var view = await Engine(scope).AnalyzeAsync(SeoEntityKind.Product, productId);
        Assert.NotNull(view);
        Assert.InRange(view!.Report.Score, 0, 100);
        Assert.NotEmpty(view.Report.Checks);
        Assert.Single(view.History);
        Assert.Equal($"/admin/products/edit/{productId}#seo", view.EditUrl);
        Assert.Null(await Engine(scope).AnalyzeAsync(SeoEntityKind.Product, Guid.NewGuid()));
    }
}
