using Fratak_Shop.Core.Services.Smart;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Smart;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Fratak_Shop.Tests.Integration;

/// <summary>
/// تست‌های دور نوزدهم روی SQL Server واقعی (بدون اینترنت):
/// <list type="bullet">
/// <item>صف کارهای هوشمند: بدون کار تکراری، اولویت دستی، گرفتن اتمی، تلاش دوباره با سقف ۳، لغو/تلاش دوباره/تأیید.</item>
/// <item>کش تحقیق: ذخیره/خواندن، انقضا و پاک‌سازی.</item>
/// <item>تنظیمات: محدودسازی مقدارها، کلید دستیار (ساخت، بررسی زمان‌ثابت، ابطال).</item>
/// <item>خلاصهٔ خرید: پیش‌نویس ← تأیید؛ نسخهٔ تأییدشدهٔ انسانی هرگز با تولید خودکار بعدی بازنویسی نمی‌شود.</item>
/// <item>تقویم بلاگ: اعتبارسنجی ایده، افزودن، رد کردن.</item>
/// </list>
/// </summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", "Integration")]
public class SmartContentIntegrationTests(SqlServerFixture db)
{
    /// <summary>اولویت خیلی بالا تا کارهای همین آزمون حتماً اول برداشته شوند (حتی اگر آزمون دیگری کار در صف گذاشته باشد).</summary>
    private static int UniquePriority() => 100_000 + Random.Shared.Next(1, 1_000_000);

    [SqlFact]
    public async Task Queue_dedupes_claims_atomically_and_respects_the_attempt_limit()
    {
        var target = Guid.NewGuid();
        var priority = UniquePriority();

        await using (var scope = db.Scope())
        {
            var queue = scope.ServiceProvider.GetRequiredService<ISmartTaskQueue>();
            var first = await queue.EnqueueAsync(SmartTaskKind.ProductSpecs, SmartTarget.Product, target, "کالای آزمون", SmartPriority.Batch);
            var again = await queue.EnqueueAsync(SmartTaskKind.ProductSpecs, SmartTarget.Product, target, "کالای آزمون", priority);
            Assert.Equal(first, again); // کار تکراری ساخته نمی‌شود، فقط اولویت بالا می‌رود
            Assert.Equal(priority, (await queue.GetAsync(first))!.Priority);

            // سه بار گرفتن و شکست موقت ← بار سوم «ناموفق» نهایی.
            for (var attempt = 1; attempt <= SmartTaskQueue.MaxAttempts; attempt++)
            {
                var claimed = await queue.ClaimNextAsync("test-worker", TimeSpan.FromMinutes(5));
                Assert.NotNull(claimed);
                Assert.Equal(first, claimed!.Id);
                Assert.Equal(attempt, claimed.Attempts);
                Assert.Equal(SmartTaskStatus.Running, (await queue.GetAsync(first))!.Status);
                await queue.RetryLaterAsync(first, TimeSpan.Zero, "منبع در دسترس نبود");
            }

            var failed = await queue.GetAsync(first);
            Assert.Equal(SmartTaskStatus.Failed, failed!.Status);
            Assert.Equal("منبع در دسترس نبود", failed.Error);

            // «دوباره» ← از صفر و با اولویت دستی.
            Assert.True((await queue.RetryAsync(first)).IsSuccess());
            var retried = await queue.GetAsync(first);
            Assert.Equal(SmartTaskStatus.Queued, retried!.Status);
            Assert.Equal(0, retried.Attempts);
            Assert.True((await queue.CancelAsync(first)).IsSuccess());
            Assert.False((await queue.CancelAsync(first)).IsSuccess()); // لغوشده دوباره لغو نمی‌شود
        }
    }

    [SqlFact]
    public async Task Queue_delayed_retry_is_not_claimed_early_and_review_flow_marks_applied()
    {
        var target = Guid.NewGuid();
        await using var scope = db.Scope();
        var queue = scope.ServiceProvider.GetRequiredService<ISmartTaskQueue>();

        var id = await queue.EnqueueAsync(SmartTaskKind.ProductImages, SmartTarget.Product, target, "کالای عکس", UniquePriority());
        var claimed = await queue.ClaimNextAsync("w1", TimeSpan.FromMinutes(5));
        Assert.Equal(id, claimed!.Id);

        // کار «در حال اجرا» را کارگر دیگری نمی‌گیرد.
        var other = await queue.ClaimNextAsync("w2", TimeSpan.FromMinutes(5));
        Assert.NotEqual(id, other?.Id);
        if (other is not null)
            await queue.RetryLaterAsync(other.Id, TimeSpan.Zero, null);

        await queue.RetryLaterAsync(id, TimeSpan.FromHours(1), "بعداً");
        var early = await queue.ClaimNextAsync("w3", TimeSpan.FromMinutes(5));
        Assert.NotEqual(id, early?.Id); // زمانش نرسیده
        if (early is not null)
            await queue.RetryLaterAsync(early.Id, TimeSpan.Zero, null);

        // مسیر «منتظر تأیید» ← تأیید ← «انجام شد».
        await scope.ServiceProvider.GetRequiredService<FratakShopDbContext>().SmartTasks.Where(t => t.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RunAfter, DateTimeOffset.UtcNow.AddMinutes(-1)));
        var again = await queue.ClaimNextAsync("w4", TimeSpan.FromMinutes(5));
        Assert.Equal(id, again!.Id);
        await queue.CompleteAsync(id, SmartTaskStatus.NeedsReview, "۳ عکس منتظر انتخاب", "{\"steps\":[\"عکس\"]}");
        Assert.Equal("{\"steps\":[\"عکس\"]}", await queue.GetResultJsonAsync(id));
        Assert.Contains(await queue.ForTargetAsync(SmartTarget.Product, target), t => t.Id == id && t.Status == SmartTaskStatus.NeedsReview);

        await queue.MarkReviewedAsync(id, "۲ عکس اضافه شد");
        var done = await queue.GetAsync(id);
        Assert.Equal(SmartTaskStatus.Applied, done!.Status);
        Assert.Equal("۲ عکس اضافه شد", done.Summary);

        var latest = await queue.LatestForAsync(SmartTaskKind.ProductImages, SmartTarget.Product, target);
        Assert.Equal(id, latest!.Value.Row.Id);
        var stats = await queue.StatsAsync();
        Assert.True(stats.AppliedToday >= 1);
    }

    [SqlFact]
    public async Task Research_cache_round_trips_expires_and_prunes()
    {
        var key = "test:" + Guid.NewGuid().ToString("N");
        await using var scope = db.Scope();
        var store = scope.ServiceProvider.GetRequiredService<ISmartResearchStore>();

        await store.SetAsync(key, "digikala", new List<string> { "وزن", "۲۵۰ گرم" }, TimeSpan.FromDays(1));
        Assert.Equal(new[] { "وزن", "۲۵۰ گرم" }, await store.GetAsync<List<string>>(key));

        await store.SetAsync(key, "digikala", new List<string> { "تازه" }, TimeSpan.FromDays(1)); // به‌روزرسانی همان کلید
        Assert.Equal(new[] { "تازه" }, await store.GetAsync<List<string>>(key));

        var expired = key + ":old";
        await store.SetAsync(expired, "bing", 42, TimeSpan.FromSeconds(-5));
        Assert.Equal(0, await store.GetAsync<int>(expired)); // منقضی ← مثل نبودن
        Assert.True(await store.PruneAsync() >= 1); // ردیف منقضی واقعاً پاک می‌شود
        Assert.Equal(new[] { "تازه" }, await store.GetAsync<List<string>>(key)); // ردیف معتبر دست نمی‌خورد
    }

    [SqlFact]
    public async Task Settings_are_clamped_and_the_helper_key_is_verified_and_revocable()
    {
        await using var scope = db.Scope();
        var config = scope.ServiceProvider.GetRequiredService<ISmartConfigService>();

        var options = await config.GetAsync();
        var original = options.BlogPostsPerWeek;
        options.BlogPostsPerWeek = 50;
        Assert.True((await config.SaveAsync(options)).IsSuccess());
        Assert.Equal(7, (await config.GetAsync()).BlogPostsPerWeek);

        var key = await config.RotateHelperKeyAsync();
        Assert.True(key.Length >= 32);
        Assert.True(await config.VerifyHelperKeyAsync(key, "لپ‌تاپ دفتر"));
        Assert.False(await config.VerifyHelperKeyAsync(key + "x", null));
        Assert.False(await config.VerifyHelperKeyAsync("", null));
        Assert.False(await config.VerifyHelperKeyAsync(null, null));
        var status = await config.GetHelperAsync();
        Assert.True(status.HasKey);
        Assert.DoesNotContain(key, status.KeyHint ?? ""); // خود کلید هیچ‌جا نمایش داده نمی‌شود

        var newer = await config.RotateHelperKeyAsync();
        Assert.False(await config.VerifyHelperKeyAsync(key, null)); // کلید قبلی باطل شد
        Assert.True(await config.VerifyHelperKeyAsync(newer, null));

        Assert.True((await config.RevokeHelperKeyAsync()).IsSuccess());
        Assert.False(await config.VerifyHelperKeyAsync(newer, null));
        Assert.False((await config.GetHelperAsync()).HasKey);

        options = await config.GetAsync();
        options.BlogPostsPerWeek = original;
        await config.SaveAsync(options);
    }

    [SqlFact]
    public async Task Human_approved_insight_is_never_overwritten_by_automation()
    {
        var productId = await db.CreateProductAsync();
        await using var scope = db.Scope();
        var insights = scope.ServiceProvider.GetRequiredService<IProductInsightService>();

        var draft = new InsightDraft
        {
            Summary = "خریداران از شارژدهی راضی‌اند.",
            Pros = [new InsightPoint("شارژدهی خوب", 4)],
            ReviewCount = 5,
            RatingTenths = 44,
        };
        Assert.True(await insights.SaveDraftAsync(productId, draft, approve: false, null));
        Assert.Null(await insights.GetApprovedAsync(productId)); // پیش‌نویس در سایت دیده نمی‌شود
        Assert.Equal(ProductInsightStatus.Draft, (await insights.GetAsync(productId))!.Status);
        Assert.Contains(await insights.PendingAsync(100), p => p.ProductId == productId);

        Assert.True((await insights.ApproveAsync(productId, "مدیر فروشگاه")).IsSuccess());
        var approved = await insights.GetApprovedAsync(productId);
        Assert.NotNull(approved);
        Assert.Equal(4.4, approved!.Rating!.Value, 3);

        draft.Summary = "متن خودکار تازه";
        Assert.False(await insights.SaveDraftAsync(productId, draft, approve: false, null));
        Assert.False(await insights.SaveDraftAsync(productId, draft, approve: true, null));
        Assert.Equal("خریداران از شارژدهی راضی‌اند.", (await insights.GetApprovedAsync(productId))!.Summary);
        Assert.DoesNotContain(await insights.PendingAsync(100), p => p.ProductId == productId);
    }

    [SqlFact]
    public async Task Blog_plan_validates_ideas_and_can_skip_them()
    {
        await using var scope = db.Scope();
        var plan = scope.ServiceProvider.GetRequiredService<IBlogPlanService>();
        var categoryId = await scope.ServiceProvider.GetRequiredService<FratakShopDbContext>().Categories.Select(c => c.Id).FirstAsync();

        Assert.False((await plan.AddAsync(BlogArticleType.BuyingGuide, "کو", null, categoryId, null, DateTimeOffset.UtcNow)).IsSuccess());
        Assert.False((await plan.AddAsync(BlogArticleType.Comparison, "مقایسهٔ دو هدفون", null, null, [Guid.NewGuid()], DateTimeOffset.UtcNow)).IsSuccess());
        Assert.False((await plan.AddAsync(BlogArticleType.BuyingGuide, "راهنمای خرید هدفون", null, null, null, DateTimeOffset.UtcNow)).IsSuccess());

        var added = await plan.AddAsync(BlogArticleType.BuyingGuide, "راهنمای خرید هدفون", "خرید هدفون", categoryId, null, DateTimeOffset.UtcNow.AddDays(3));
        Assert.True(added.IsSuccess());
        var row = await plan.GetAsync(added.Data);
        Assert.Equal(BlogPlanStatus.Planned, row!.Status);
        Assert.Contains(await plan.ListAsync(), r => r.Id == added.Data);

        Assert.True((await plan.SkipAsync(added.Data)).IsSuccess());
        Assert.Equal(BlogPlanStatus.Skipped, (await plan.GetAsync(added.Data))!.Status);
    }

    [SqlFact]
    public async Task Reminder_run_and_stats_are_safe_on_any_data()
    {
        await using var scope = db.Scope();
        var reminders = scope.ServiceProvider.GetRequiredService<ICustomerReminderService>();
        // ساعت ۲۳ تهران ← ساعت سکوت: هیچ پیامی فرستاده نمی‌شود.
        var quiet = await reminders.RunAsync(new DateTimeOffset(2026, 11, 5, 19, 30, 0, TimeSpan.Zero));
        Assert.True(quiet.QuietHours);
        Assert.Equal(0, quiet.Sent);

        var stats = await reminders.StatsAsync();
        Assert.True(stats.Pending >= 0 && stats.SentLast30Days >= 0 && stats.SkippedLast30Days >= 0);
    }
}
