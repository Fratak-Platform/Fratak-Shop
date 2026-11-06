using Fratak_Shop.Core.Services.Seo.Autopilot;
using Fratak_Shop.Core.Services.Seo.Engine;
using Fratak_Shop.Core.Services.Smart;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Products;
using Fratak_Shop.Data.Entities.SeoEngine;
using Fratak_Shop.Data.Entities.Smart;
using Fratak_Shop.Web.Infrastructure.Smart;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Fratak_Shop.Tests.Integration;

/// <summary>
/// تست‌های دور بیستم روی SQL Server واقعی (بدون اینترنت):
/// <list type="bullet">
/// <item>بازخورد عکس: ثبت تأیید/رد (بدون ردیف تکراری)، فهرست سیاه هر محصول جدا، امتیاز هر منبع از تصمیم‌های کارشناس.</item>
/// <item>متن «نقد و بررسی»: وضعیت متن فعلی، نوشتن با تاریخچه، «تغییری لازم نبود»، برگرداندن و احترام به برگرداندن.</item>
/// <item>نویسندهٔ نقد و بررسی روی کالای واقعی: با مشخصات کافی پیش‌نویس نشانه‌دار؛ بدون مشخصات کافی ← خطا؛ کالای ناموجود ← NotFound.</item>
/// <item>صف: شمارش کارهای باقی‌ماندهٔ یک گروه، خلاصهٔ گروهی برای اعلان و برنامهٔ تکمیل شبانه بدون کار تکراری.</item>
/// </list>
/// </summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", "Integration")]
[Trait("Area", "Smart")]
public class SmartRound20IntegrationTests(SqlServerFixture db)
{
    /// <summary>اولویت خیلی بالا و یکتا تا کارهای این آزمون با کارهای آزمون‌های دیگر قاطی نشوند.</summary>
    private static int UniquePriority() => 2_000_000 + Random.Shared.Next(1, 1_000_000);

    // ── بازخورد عکس ───────────────────────────────────────────────────

    [SqlFact]
    public async Task Image_feedback_builds_a_per_product_blacklist_and_learns_source_scores()
    {
        var productId = await db.CreateProductAsync();
        var otherProduct = await db.CreateProductAsync();
        var shop = "serp:t" + Guid.NewGuid().ToString("N")[..12] + ".ir";
        const long rejectedHash = 0x0F0F_0F0F_F0F0_F0F0;
        const long hashOnly = 0x1234_5678_9ABC_DEF0;

        await using var scope = db.Scope();
        var catalog = scope.ServiceProvider.GetRequiredService<ISmartCatalogService>();
        var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();

        var saved = await catalog.RecordImageFeedbackAsync(productId,
        [
            new ImageFeedbackItem("https://a.ir/wrong.jpg", rejectedHash, shop, false),
            new ImageFeedbackItem("https://a.ir/right-1.jpg", 11, shop, true),
            new ImageFeedbackItem("https://a.ir/right-2.jpg", 12, shop, true),
            new ImageFeedbackItem("https://a.ir/right-3.jpg?x-oss-process=image/resize,h_300", 13, shop, false),
            new ImageFeedbackItem("//a.ir/right-3.jpg", 13, shop, true),   // همان آدرس ← آخرین تصمیم می‌ماند (تأیید)
            new ImageFeedbackItem(null, hashOnly, shop, false),             // فقط اثر انگشت (آدرس منبع نامعلوم)
            new ImageFeedbackItem(null, 0, shop, false),                    // بی‌ارزش ← نادیده
            new ImageFeedbackItem("  ", 0, null, false),
        ]);
        Assert.Equal(5, saved);

        var blacklist = await catalog.GetImageBlacklistAsync(productId);
        Assert.True(blacklist.BlocksUrl("https://a.ir/wrong.jpg"));
        Assert.True(blacklist.BlocksUrl("//a.ir/wrong.jpg"));
        Assert.False(blacklist.BlocksUrl("https://a.ir/right-1.jpg"));
        Assert.False(blacklist.BlocksUrl("https://a.ir/right-3.jpg"));
        Assert.True(blacklist.BlocksHash(unchecked((ulong)rejectedHash)));
        Assert.True(blacklist.BlocksHash(unchecked((ulong)hashOnly) ^ 0b101UL)); // همان عکس با کمی تفاوت از سایت دیگر
        Assert.False(blacklist.BlocksHash(11));

        // فهرست سیاه هر محصول جداست.
        var other = await catalog.GetImageBlacklistAsync(otherProduct);
        Assert.Equal(0, other.Count);
        Assert.False(other.BlocksUrl("https://a.ir/wrong.jpg"));

        // امتیاز منبع: ۳ تأیید از ۵ ← (۳+۱)÷(۵+۲) = ۰٫۵۷۱۴.
        var scores = await catalog.ImageSourceScoresAsync();
        Assert.Equal(0.5714, scores[shop], 4);

        // تغییر نظر کارشناس: عکس ردشده تأیید شد ← ردیف تکراری ساخته نمی‌شود و دیگر بسته نیست.
        Assert.Equal(1, await catalog.RecordImageFeedbackAsync(productId, [new ImageFeedbackItem("https://a.ir/wrong.jpg", rejectedHash, shop, true)]));
        Assert.False((await catalog.GetImageBlacklistAsync(productId)).BlocksUrl("https://a.ir/wrong.jpg"));
        Assert.Equal(5, await context.SmartImageFeedbacks.CountAsync(f => f.ProductId == productId));
        Assert.Equal(0.7143, (await catalog.ImageSourceScoresAsync())[shop], 4);

        // ورودی خالی ← هیچ کاری.
        Assert.Equal(0, await catalog.RecordImageFeedbackAsync(productId, []));
    }

    // ── متن «نقد و بررسی» ─────────────────────────────────────────────

    [SqlFact]
    public async Task Smart_review_is_saved_with_history_and_a_rollback_is_respected()
    {
        var productId = await db.CreateProductAsync();
        const string html = "<p data-fs=\"review\">در این نقد و بررسی، این کالای آزمایشی را از نظر مشخصات و کاربرد مرور می‌کنیم.</p>\n<h2>مشخصات کلیدی</h2>\n<p>جزئیات بیشتر دربارهٔ کالا.</p>";

        await using var scope = db.Scope();
        var catalog = scope.ServiceProvider.GetRequiredService<ISmartCatalogService>();
        var autopilot = scope.ServiceProvider.GetRequiredService<ISeoAutopilotService>();
        var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();

        Assert.Null(await catalog.GetBodyStateAsync(Guid.NewGuid()));
        var empty = await catalog.GetBodyStateAsync(productId);
        Assert.NotNull(empty);
        Assert.Equal(0, empty!.Words);
        Assert.False(empty.HasMarker || empty.WrittenBySmart || empty.RolledBack);
        Assert.Equal(ReviewAction.Write, SmartOrchestrator.DecideReview(empty).Action);

        // ورودی نامعتبر هیچ چیز ذخیره نمی‌کند.
        Assert.Equal(EnOperationResultStatus.Error, (await autopilot.ApplyBodyAsync(productId, "  ", "x", null)).Status);
        Assert.Equal(EnOperationResultStatus.NotFound, (await autopilot.ApplyBodyAsync(Guid.NewGuid(), html, "x", null)).Status);

        var applied = await autopilot.ApplyBodyAsync(productId, html, "نقد و بررسی هوشمند (آزمون)", "دستیار هوشمند");
        Assert.True(applied.IsSuccess(), applied.Message);
        Assert.True(applied.Data);

        var written = await catalog.GetBodyStateAsync(productId);
        Assert.True(written!.HasMarker);
        Assert.True(written.WrittenBySmart);
        Assert.False(written.RolledBack);
        Assert.Equal(ReviewAction.Regenerate, SmartOrchestrator.DecideReview(written).Action);

        var revision = await context.SeoRevisions.AsNoTracking()
            .Where(r => r.EntityId == productId && r.Source == SeoRevisionSource.SmartReview)
            .OrderByDescending(r => r.CreatedAt).FirstAsync();
        Assert.Equal("دستیار هوشمند", revision.ByName);

        // همان متن دوباره ← موفق ولی «تغییری لازم نبود» و تاریخچهٔ اضافه نه.
        var again = await autopilot.ApplyBodyAsync(productId, html, "دوباره", null);
        Assert.True(again.IsSuccess());
        Assert.False(again.Data);
        Assert.Equal(1, await context.SeoRevisions.CountAsync(r => r.EntityId == productId && r.Source == SeoRevisionSource.SmartReview));

        // کارشناس متن را کمی ویرایش کرد (نشانه مانده) ← دستیار دیگر به آن دست نمی‌زند.
        var edited = written.Html.Replace("جزئیات بیشتر", "توضیح کارشناس", StringComparison.Ordinal);
        await context.Products.Where(p => p.Id == productId).ExecuteUpdateAsync(s => s.SetProperty(p => p.Description, edited));
        var humanEdited = await catalog.GetBodyStateAsync(productId);
        Assert.True(humanEdited!.HasMarker);
        Assert.False(humanEdited.WrittenBySmart);
        Assert.Equal(ReviewAction.Skip, SmartOrchestrator.DecideReview(humanEdited).Action);
        await context.Products.Where(p => p.Id == productId).ExecuteUpdateAsync(s => s.SetProperty(p => p.Description, written.Html));

        // برگرداندن ← متن قبلی برمی‌گردد و دستیار دیگر برای این کالا نقد و بررسی نمی‌نویسد.
        var rollback = await autopilot.RollbackAsync(revision.Id, "مدیر آزمایشی");
        Assert.True(rollback.IsSuccess(), rollback.Message);
        var description = await context.Products.AsNoTracking().Where(p => p.Id == productId).Select(p => p.Description).SingleAsync();
        Assert.True(string.IsNullOrEmpty(description), description);
        var rolledBack = await catalog.GetBodyStateAsync(productId);
        Assert.True(rolledBack!.RolledBack);
        Assert.Equal(ReviewAction.Skip, SmartOrchestrator.DecideReview(rolledBack).Action);
    }

    [SqlFact]
    public async Task Review_writer_needs_real_specs_and_marks_its_output()
    {
        var withSpecs = await db.CreateProductAsync();
        var withoutSpecs = await db.CreateProductAsync();
        await using (var setup = db.Scope())
        {
            var context = setup.ServiceProvider.GetRequiredService<FratakShopDbContext>();
            var order = 0;
            foreach (var (key, value) in new[] { ("نسخه بلوتوث", "5.3"), ("شارژدهی", "30 ساعت"), ("نویز کنسلینگ", "دارد"), ("مقاومت در برابر آب", "IPX5") })
                context.ProductFeatures.Add(new ProductFeature { ProductId = withSpecs, Key = key, Value = value, ValueType = FeatureValueType.Text, DisplayOrder = ++order });
            context.ProductFeatures.Add(new ProductFeature { ProductId = withoutSpecs, Key = "رنگ", Value = "مشکی", ValueType = FeatureValueType.Text, DisplayOrder = 1 });
            await context.SaveChangesAsync();
        }

        await using var scope = db.Scope();
        var engine = scope.ServiceProvider.GetRequiredService<ISeoEngineService>();

        var draft = await engine.WriteProductReviewAsync(withSpecs, null, new ReviewNotes { Pros = ["<script>x()</script>صدای شفاف"] });
        Assert.True(draft.IsSuccess(), draft.Message);
        Assert.Contains(ContentWriter.ReviewMarkerAttribute, draft.Data.Html, StringComparison.Ordinal);
        Assert.Contains("<h2>", draft.Data.Html, StringComparison.Ordinal);
        Assert.DoesNotContain("<script", draft.Data.Html, StringComparison.OrdinalIgnoreCase);
        Assert.True(draft.Data.Words > 0);
        Assert.NotNull(draft.Data.Problems);

        // متن کارشناس که باید بماند، بالای پیش‌نویس است.
        var kept = await engine.WriteProductReviewAsync(withSpecs, "<p>متن کارشناس</p>", null);
        Assert.True(kept.IsSuccess());
        Assert.StartsWith("<p>متن کارشناس</p>", kept.Data.Html, StringComparison.Ordinal);

        Assert.Equal(EnOperationResultStatus.Error, (await engine.WriteProductReviewAsync(withoutSpecs, null, null)).Status);
        Assert.Equal(EnOperationResultStatus.NotFound, (await engine.WriteProductReviewAsync(Guid.NewGuid(), null, null)).Status);
    }

    // ── صف و اعلان گروهی ──────────────────────────────────────────────

    [SqlFact]
    public async Task Batch_counts_and_summary_cover_only_one_requester_and_priority()
    {
        var userId = await db.CreateUserAsync();
        var priority = UniquePriority();
        await using var scope = db.Scope();
        var queue = scope.ServiceProvider.GetRequiredService<ISmartTaskQueue>();
        var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();

        var ids = new List<Guid>();
        for (var i = 0; i < 5; i++)
            ids.Add(await queue.EnqueueAsync(SmartTaskKind.ProductComplete, SmartTarget.Product, Guid.NewGuid(), "کالای گروهی " + i, priority, userId: userId, userName: "مدیر آزمون"));
        // کار همان کاربر با اولویت دیگر جزو این گروه نیست.
        var outsider = await queue.EnqueueAsync(SmartTaskKind.ProductComplete, SmartTarget.Product, Guid.NewGuid(), "بیرون از گروه", priority - 1, userId: userId);

        try
        {
            Assert.Equal(5, await queue.PendingCountAsync(userId, priority));
            Assert.Equal(0, await queue.PendingCountAsync(Guid.NewGuid(), priority));

            await SetStatusAsync(context, ids[0], SmartTaskStatus.Applied);
            await SetStatusAsync(context, ids[1], SmartTaskStatus.Applied);
            await SetStatusAsync(context, ids[2], SmartTaskStatus.NeedsReview);
            await SetStatusAsync(context, ids[3], SmartTaskStatus.Failed);
            Assert.Equal(1, await queue.PendingCountAsync(userId, priority));

            var summary = await queue.BatchSummaryAsync(userId, priority, DateTimeOffset.UtcNow);
            Assert.Equal(new SmartBatchSummary(5, 2, 1, 1, 0, 1), summary);
            Assert.Equal("۵ کار: ۲ اعمال شد، ۱ منتظر تأیید شما، ۱ ناموفق", SmartNotifier.BatchBody(summary));
            Assert.Equal("/admin/smart?Status=NeedsReview", SmartNotifier.BatchLink(summary));

            // گروه دیگری (یک ساعت بعد) این کارها را نمی‌شمارد.
            Assert.Equal(0, (await queue.BatchSummaryAsync(userId, priority, DateTimeOffset.UtcNow.AddHours(1))).Total);

            var recent = await queue.RecentTargetsAsync(SmartTaskKind.ProductComplete, DateTimeOffset.UtcNow.AddMinutes(-5), priority);
            Assert.Equal(5, recent.Count);
            Assert.Empty(await queue.RecentTargetsAsync(SmartTaskKind.ProductComplete, DateTimeOffset.UtcNow.AddMinutes(5), priority));
        }
        finally
        {
            var all = ids.Append(outsider).ToList();
            await context.SmartTasks.Where(t => all.Contains(t.Id)).ExecuteDeleteAsync();
        }
    }

    [SqlFact]
    public async Task Nightly_completion_queues_incomplete_products_once_within_the_daily_cap()
    {
        await db.CreateProductAsync(); // دست‌کم یک کالای ناقص تازه (بدون عکس/مشخصات/خلاصه)
        var startedAt = DateTimeOffset.UtcNow.AddSeconds(-1);
        await using var scope = db.Scope();
        var queue = scope.ServiceProvider.GetRequiredService<ISmartTaskQueue>();
        var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();

        try
        {
            Assert.Equal(0, await SmartWorker.PlanNightlyCompleteAsync(scope.ServiceProvider, queue, 0, DateTimeOffset.UtcNow, CancellationToken.None));

            var first = await SmartWorker.PlanNightlyCompleteAsync(scope.ServiceProvider, queue, 3, DateTimeOffset.UtcNow, CancellationToken.None);
            Assert.InRange(first, 1, 3);
            var queued = await context.SmartTasks.AsNoTracking()
                .Where(t => t.Kind == SmartTaskKind.ProductComplete && t.Priority == SmartPriority.Nightly && t.CreatedAt >= startedAt)
                .ToListAsync();
            Assert.Equal(first, queued.Count);
            Assert.All(queued, t => Assert.Equal(SmartOrchestrator.Actor, t.RequestedByName));
            Assert.Equal(first, queued.Select(t => t.TargetId).Distinct().Count());

            // اجرای دوباره در همان روز: سقف روزانه پر شده یا کالای تازه‌ای نمانده ← هیچ کار تکراری.
            Assert.Equal(0, await SmartWorker.PlanNightlyCompleteAsync(scope.ServiceProvider, queue, 3, DateTimeOffset.UtcNow, CancellationToken.None));
        }
        finally
        {
            await context.SmartTasks
                .Where(t => t.Kind == SmartTaskKind.ProductComplete && t.Priority == SmartPriority.Nightly && t.CreatedAt >= startedAt)
                .ExecuteDeleteAsync();
        }
    }

    private static Task<int> SetStatusAsync(FratakShopDbContext context, Guid id, SmartTaskStatus status) =>
        context.SmartTasks.Where(t => t.Id == id).ExecuteUpdateAsync(s => s.SetProperty(t => t.Status, status));
}
