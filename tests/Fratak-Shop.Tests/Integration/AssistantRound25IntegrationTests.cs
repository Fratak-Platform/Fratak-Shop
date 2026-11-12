using Fratak_Shop.Core.DTOs.Favorites.Command;
using Fratak_Shop.Core.Services.Assistant;
using Fratak_Shop.Core.Services.Comments;
using Fratak_Shop.Core.Services.Favorites;
using Fratak_Shop.Core.Services.Smart;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Assistant;
using Fratak_Shop.Data.Entities.Comments;
using Fratak_Shop.Data.Entities.Favorites;
using Fratak_Shop.Data.Entities.Notifications;
using Fratak_Shop.Data.Entities.Products;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;

namespace Fratak_Shop.Tests.Integration;

/// <summary>
/// تست یکپارچهٔ دور ۲۵ روی SQL Server واقعی.
/// <list type="bullet">
/// <item>آموزش دستیار: ثبت ← دستیار همان جواب را می‌دهد (با شناسهٔ پیام)؛ نظر 👍/👎 فقط برای پیام همان گفت‌وگو؛ حذف ← دیگر جواب نمی‌دهد.</item>
/// <item>مشاور خرید: «X زیر ۲ میلیون» فقط کالاهای داخل بودجه را با دکمهٔ «دیدن همه» (با سقف قیمت) نشان می‌دهد.</item>
/// <item>کارهای بعد از خرید: مهمان ← راهنمای ورود (بدون انجام هیچ کاری)؛ مشتری بی‌سفارش ← پیام درست.</item>
/// <item>پرسش و پاسخ کالا: تأیید پاسخ ← اعلان به پرسش‌کننده؛ تأیید دوباره، جواب خود پرسش‌کننده و پرسش ردشده اعلان نمی‌دهند؛ پیش‌نویس از مشخصات.</item>
/// <item>خبر ارزان شدن علاقه‌مندی: پر شدن قیمت مرجع بی‌اعلان، اعلان یک‌بار در هفته، کاهش کم، ساعت سکوت، «خبرم کن» فعال، سقف روزانه، خاموش بودن.</item>
/// </list>
/// هر آزمون فقط دادهٔ خودش را می‌سازد و در finally پاک می‌کند.
/// </summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", "Integration")]
[Trait("Area", "Assistant")]
public class AssistantRound25IntegrationTests(SqlServerFixture db)
{
    private const string Letters = "ابپتثجچحخدذرزسشصضطظعغفقکگلمنوی";

    /// <summary>ساعت ۱۲ ظهر تهران (۰۸:۳۰ UTC) – بیرون از ساعت سکوت پیش‌فرض (۲۲ تا ۹).</summary>
    private static readonly DateTimeOffset Noon = new(2026, 10, 22, 8, 30, 0, TimeSpan.Zero);

    /// <summary>واژهٔ فارسی تصادفی و یکتا (بدون حرف تکراری پشت‌سرهم، با «ژ» در پایان).</summary>
    private static string Word()
    {
        var letters = new char[6];
        for (var i = 0; i < letters.Length; i++)
        {
            do
                letters[i] = Letters[Random.Shared.Next(Letters.Length)];
            while (i > 0 && letters[i] == letters[i - 1]);
        }
        return new string(letters) + "ژ";
    }

    private static string NewSession() => "r25" + Guid.NewGuid().ToString("N")[..20];

    private static AssistantRequest Request(string text, string session, Guid? userId = null) =>
        new(text, session, userId, null, [], null, _ => Task.FromResult<Guid?>(null));

    // ── آموزش دستیار و نظر مشتری ────────────────────────────────

    [SqlFact]
    public async Task Taught_answer_is_used_feedback_is_session_bound_and_delete_stops_it()
    {
        await using var scope = db.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
        var teachings = scope.ServiceProvider.GetRequiredService<IAssistantTeachingService>();
        var assistant = scope.ServiceProvider.GetRequiredService<IAssistantService>();
        var cache = scope.ServiceProvider.GetRequiredService<IMemoryCache>();
        var session = NewSession();
        const string Question = "ارسال به جزیره کیش دارید";
        const string Answer = "بله، به جزیره کیش هم با پست پیشتاز ارسال داریم.";
        try
        {
            // ورودی خراب ثبت نمی‌شود.
            var bad = await teachings.SaveAsync(null, new AssistantTeachingInput(Question, "بله", "https://evil.example", null), "مدیر آزمون");
            Assert.False(bad.IsSuccess());

            var saved = await teachings.SaveAsync(null, new AssistantTeachingInput(Question, Answer, "/faq", null), "مدیر آزمون");
            Assert.True(saved.IsSuccess(), saved.Message);
            var row = await context.AssistantTeachings.AsNoTracking().SingleAsync(t => t.Question == Question);
            Assert.Equal("مدیر آزمون", row.CreatedByName);
            Assert.Contains(await teachings.ListAsync(), r => r.Id == row.Id);

            var reply = await assistant.ReplyAsync(Request("ارسال به جزیره کیش دارید؟", session));
            Assert.Equal(Answer, reply.Text);
            Assert.Contains(reply.Actions, a => a.Url == "/faq");
            Assert.NotNull(reply.MessageId);

            context.ChangeTracker.Clear();
            var logged = await context.AssistantMessages.AsNoTracking().SingleAsync(m => m.Id == reply.MessageId);
            Assert.Equal(AssistantService.TaughtTopic, logged.Topic);
            Assert.True((await context.AssistantTeachings.AsNoTracking().SingleAsync(t => t.Id == row.Id)).HitCount >= 1);

            // نظر: گفت‌وگوی دیگر یا پیام ناموجود ← رد؛ همان گفت‌وگو ← ثبت.
            Assert.False(await assistant.FeedbackAsync(reply.MessageId!.Value, NewSession(), true));
            Assert.False(await assistant.FeedbackAsync(Guid.NewGuid(), session, true));
            Assert.False(await assistant.FeedbackAsync(Guid.Empty, session, true));
            Assert.True(await assistant.FeedbackAsync(reply.MessageId.Value, session, false));
            context.ChangeTracker.Clear();
            Assert.False((await context.AssistantMessages.AsNoTracking().SingleAsync(m => m.Id == reply.MessageId)).Helpful);

            // پیام قدیمی‌تر از ۲۴ ساعت دیگر رأی نمی‌گیرد.
            await context.AssistantMessages.Where(m => m.Id == reply.MessageId).ExecuteUpdateAsync(s => s.SetProperty(m => m.CreatedAt, DateTimeOffset.UtcNow.AddDays(-2)));
            Assert.False(await assistant.FeedbackAsync(reply.MessageId.Value, session, true));

            // حذف ← دستیار دیگر جواب آموزشی نمی‌دهد.
            Assert.True((await teachings.DeleteAsync(row.Id)).IsSuccess());
            Assert.False((await teachings.DeleteAsync(Guid.NewGuid())).IsSuccess());
            var after = await assistant.ReplyAsync(Request("ارسال به جزیره کیش دارید؟", session));
            Assert.NotEqual(Answer, after.Text);
        }
        finally
        {
            context.ChangeTracker.Clear();
            await context.AssistantTeachings.IgnoreQueryFilters().Where(t => t.Question == Question).ExecuteDeleteAsync();
            await context.AssistantMessages.IgnoreQueryFilters().Where(m => m.SessionKey == session).ExecuteDeleteAsync();
            cache.Remove(AssistantTeachingService.CacheKey);
        }
    }

    [SqlFact]
    public async Task Taught_intent_routes_to_safe_action()
    {
        await using var scope = db.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
        var teachings = scope.ServiceProvider.GetRequiredService<IAssistantTeachingService>();
        var assistant = scope.ServiceProvider.GetRequiredService<IAssistantService>();
        var cache = scope.ServiceProvider.GetRequiredService<IMemoryCache>();
        var session = NewSession();
        const string Question = "بسته پستی من کجا مونده";
        try
        {
            Assert.True((await teachings.SaveAsync(null, new AssistantTeachingInput(Question, null, null, AssistantIntent.Track), "مدیر")).IsSuccess());
            var reply = await assistant.ReplyAsync(Request(Question, session));
            Assert.Equal(AssistantIntent.Track, reply.Intent);
            Assert.Contains(await teachings.ExamplesAsync(), e => e.Text == Question && e.Intent == AssistantIntent.Track);
        }
        finally
        {
            context.ChangeTracker.Clear();
            await context.AssistantTeachings.IgnoreQueryFilters().Where(t => t.Question == Question).ExecuteDeleteAsync();
            await context.AssistantMessages.IgnoreQueryFilters().Where(m => m.SessionKey == session).ExecuteDeleteAsync();
            cache.Remove(AssistantTeachingService.CacheKey);
        }
    }

    // ── مشاور خرید ──────────────────────────────────────────────

    [SqlFact]
    public async Task Advisor_shows_only_products_within_budget()
    {
        var word = Word();
        var cheap = await db.CreateProductAsync(price: 900_000, stock: 5);
        var middle = await db.CreateProductAsync(price: 1_500_000, stock: 5);
        var pricey = await db.CreateProductAsync(price: 3_000_000, stock: 5);
        await using var scope = db.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
        var assistant = scope.ServiceProvider.GetRequiredService<IAssistantService>();
        var session = NewSession();
        try
        {
            var ids = new[] { cheap, middle, pricey };
            for (var i = 0; i < ids.Length; i++)
            {
                var id = ids[i];
                var name = $"{word} نمونه {i + 1}";
                await context.Products.Where(p => p.Id == id).ExecuteUpdateAsync(s => s.SetProperty(p => p.Name, name));
            }
            // واژه‌های کاتالوگ دستیار و جست‌وجو دوباره ساخته شوند.
            var cache = scope.ServiceProvider.GetRequiredService<IMemoryCache>();
            cache.Remove(CacheInvalidation.AssistantVocabularyKey);
            cache.Remove(CacheInvalidation.SearchVocabularyKey);

            var reply = await assistant.ReplyAsync(Request($"{word} زیر ۲ میلیون", session));
            Assert.Equal(AssistantIntent.Advisor, reply.Intent);
            Assert.Contains(reply.Cards, c => c.Id == cheap);
            Assert.Contains(reply.Cards, c => c.Id == middle);
            Assert.DoesNotContain(reply.Cards, c => c.Id == pricey);
            Assert.All(reply.Cards, c => Assert.True(c.FinalPrice <= 2_000_000m));
            Assert.Contains(reply.Actions, a => a.Url is { } url && url.StartsWith("/search?", StringComparison.Ordinal) && url.Contains("MaxPrice=2000000", StringComparison.Ordinal));
            Assert.NotNull(reply.MessageId);
        }
        finally
        {
            context.ChangeTracker.Clear();
            await context.AssistantMessages.IgnoreQueryFilters().Where(m => m.SessionKey == session).ExecuteDeleteAsync();
        }
    }

    // ── کارهای بعد از خرید ──────────────────────────────────────

    [SqlFact]
    public async Task Post_purchase_actions_need_login_and_handle_customers_without_orders()
    {
        var userId = await db.CreateUserAsync();
        await using var scope = db.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
        var assistant = scope.ServiceProvider.GetRequiredService<IAssistantService>();
        var session = NewSession();
        try
        {
            // مهمان: فقط راهنمای ورود؛ هیچ کاری انجام نمی‌شود.
            foreach (var (text, intent) in new[]
                     {
                         ("سفارشمو کنسل کن", AssistantIntent.OrderCancel),
                         ("میخوام مرجوع کنم", AssistantIntent.OrderReturn),
                         ("فاکتور سفارشم", AssistantIntent.Invoice),
                     })
            {
                var guest = await assistant.ReplyAsync(Request(text, session));
                Assert.Equal(intent, guest.Intent);
                Assert.Contains(guest.Actions, a => a.Url is { } url && url.StartsWith("/login?returnUrl=", StringComparison.Ordinal));
            }

            // مشتری بدون سفارش.
            var invoice = await assistant.ReplyAsync(Request("فاکتور سفارشم", session, userId));
            Assert.Equal(AssistantIntent.Invoice, invoice.Intent);
            Assert.Contains("هنوز سفارشی", invoice.Text, StringComparison.Ordinal);
            var cancel = await assistant.ReplyAsync(Request("سفارشمو کنسل کن", session, userId));
            Assert.Equal(AssistantIntent.OrderCancel, cancel.Intent);
            Assert.DoesNotContain(cancel.Actions, a => a.Say is { } say && say.StartsWith("تایید لغو", StringComparison.Ordinal));
            var ret = await assistant.ReplyAsync(Request("میخوام مرجوع کنم", session, userId));
            Assert.Equal(AssistantIntent.OrderReturn, ret.Intent);

            // شمارهٔ سفارش دیگران/ناموجود ← «پیدا نشد» (هیچ اطلاعاتی لو نمی‌رود).
            var foreign = await assistant.ReplyAsync(Request("فاکتور سفارش FR-20200101-999999", session, userId));
            Assert.Equal(AssistantIntent.Invoice, foreign.Intent);
            Assert.Contains("پیدا نشد", foreign.Text, StringComparison.Ordinal);
        }
        finally
        {
            context.ChangeTracker.Clear();
            await context.AssistantMessages.IgnoreQueryFilters().Where(m => m.SessionKey == session).ExecuteDeleteAsync();
        }
    }

    // ── پرسش و پاسخ کالا ────────────────────────────────────────

    private static Comment Question(Guid? userId, Guid productId, string content, CommentStatus status) => new()
    {
        UserId = userId,
        ProductId = productId,
        Type = CommentType.Question,
        AuthorName = "پرسش‌کنندهٔ آزمون",
        AuthorPhone = "09120000000",
        Content = content,
        Status = status,
    };

    private static Comment Reply(Guid? userId, Guid productId, Guid parentId, string content) => new()
    {
        UserId = userId,
        ProductId = productId,
        ParentId = parentId,
        Type = CommentType.Question,
        AuthorName = "فروشگاه",
        AuthorPhone = "09120000009",
        Content = content,
        Status = CommentStatus.Pending,
    };

    [SqlFact]
    public async Task Approved_answer_notifies_the_asker_once_and_skips_self_and_rejected()
    {
        var asker = await db.CreateUserAsync();
        var staff = await db.CreateUserAsync();
        var productId = await db.CreateProductAsync();
        await using var scope = db.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
        var comments = scope.ServiceProvider.GetRequiredService<ICommentService>();
        var ids = new List<Guid>();
        try
        {
            var question = Question(asker, productId, "باتریش چقدر دوام داره؟", CommentStatus.Approved);
            var rejected = Question(asker, productId, "پرسش ردشده آزمون", CommentStatus.Rejected);
            context.Comments.AddRange(question, rejected);
            await context.SaveChangesAsync();
            ids.AddRange([question.Id, rejected.Id]);

            var answer = Reply(staff, productId, question.Id, "حدود دو روز با یک بار شارژ.");
            var self = Reply(asker, productId, question.Id, "خودم پیدا کردم، ممنون.");
            var toRejected = Reply(staff, productId, rejected.Id, "پاسخ به پرسش ردشده");
            context.Comments.AddRange(answer, self, toRejected);
            await context.SaveChangesAsync();
            ids.AddRange([answer.Id, self.Id, toRejected.Id]);
            context.ChangeTracker.Clear();

            Assert.True((await comments.ApproveCommentAsync(answer.Id)).IsSuccess());
            var slug = await context.Products.Where(p => p.Id == productId).Select(p => p.Slug).SingleAsync();
            var sent = await context.Notifications.AsNoTracking().Where(n => n.UserId == asker && n.Event == NotificationEvent.QuestionAnswered).ToListAsync();
            var notification = Assert.Single(sent);
            Assert.Equal("/product/" + slug + "#quest", notification.LinkUrl);
            Assert.Equal(NotificationCategory.Support, notification.Category);

            // تأیید دوباره، جواب خود پرسش‌کننده و پاسخ به پرسش ردشده ← اعلان تازه‌ای نیست.
            Assert.True((await comments.ApproveCommentAsync(answer.Id)).IsSuccess());
            Assert.True((await comments.ApproveCommentAsync(self.Id)).IsSuccess());
            Assert.True((await comments.ApproveCommentAsync(toRejected.Id)).IsSuccess());
            Assert.Equal(1, await context.Notifications.CountAsync(n => n.UserId == asker && n.Event == NotificationEvent.QuestionAnswered));
            Assert.False(await context.Notifications.AnyAsync(n => n.UserId == staff && n.Event == NotificationEvent.QuestionAnswered));
        }
        finally
        {
            context.ChangeTracker.Clear();
            await context.Comments.IgnoreQueryFilters().Where(c => c.ParentId != null && ids.Contains(c.Id)).ExecuteDeleteAsync();
            await context.Comments.IgnoreQueryFilters().Where(c => ids.Contains(c.Id)).ExecuteDeleteAsync();
            await context.Notifications.IgnoreQueryFilters().Where(n => n.UserId == asker || n.UserId == staff).ExecuteDeleteAsync();
        }
    }

    [SqlFact]
    public async Task Question_drafts_come_from_product_specs_and_ignore_non_questions()
    {
        var productId = await db.CreateProductAsync(stock: 4);
        await using var scope = db.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
        var advisor = scope.ServiceProvider.GetRequiredService<IQuestionAnswerAdvisor>();
        var ids = new List<Guid>();
        try
        {
            context.ProductFeatures.Add(new ProductFeature { ProductId = productId, Key = "ظرفیت باتری", Value = "10000", DisplayOrder = 1 });
            var question = Question(null, productId, "باتریش چند میلی آمپره؟", CommentStatus.Pending);
            var review = new Comment
            {
                ProductId = productId,
                Type = CommentType.Review,
                AuthorName = "خریدار",
                AuthorPhone = "09120000000",
                Content = "باتریش عالیه",
                Rating = 5,
                Status = CommentStatus.Pending,
            };
            context.Comments.AddRange(question, review);
            await context.SaveChangesAsync();
            ids.AddRange([question.Id, review.Id]);

            var drafts = await advisor.SuggestAsync([question.Id, review.Id, Guid.NewGuid()]);
            Assert.True(drafts.ContainsKey(question.Id));
            Assert.False(drafts.ContainsKey(review.Id));
            var spec = Assert.Single(drafts[question.Id], d => d.Source == "مشخصات کالا");
            Assert.Contains("ظرفیت باتری", spec.Body, StringComparison.Ordinal);
            Assert.Empty(await advisor.SuggestAsync([]));
        }
        finally
        {
            context.ChangeTracker.Clear();
            await context.Comments.IgnoreQueryFilters().Where(c => ids.Contains(c.Id)).ExecuteDeleteAsync();
            await context.ProductFeatures.IgnoreQueryFilters().Where(f => f.ProductId == productId).ExecuteDeleteAsync();
        }
    }

    // ── خبر ارزان شدن علاقه‌مندی‌ها ─────────────────────────────

    private static async Task<Guid> AddFavoriteAsync(FratakShopDbContext context, Guid userId, Guid productId, decimal? watch)
    {
        var favorite = new Favorite { UserId = userId, ProductId = productId, WatchPrice = watch };
        context.Favorites.Add(favorite);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        return favorite.Id;
    }

    private static Task SetPriceAsync(FratakShopDbContext context, Guid productId, decimal price) =>
        context.Products.Where(p => p.Id == productId).ExecuteUpdateAsync(s => s.SetProperty(p => p.Price, price));

    private static Task<int> DropNotificationsAsync(FratakShopDbContext context, Guid userId) =>
        context.Notifications.CountAsync(n => n.UserId == userId && n.Event == NotificationEvent.PriceDrop);

    private static async Task CleanupFavoritesAsync(FratakShopDbContext context, Guid userId)
    {
        context.ChangeTracker.Clear();
        await context.Favorites.IgnoreQueryFilters().Where(f => f.UserId == userId).ExecuteDeleteAsync();
        await context.ProductAlerts.IgnoreQueryFilters().Where(a => a.UserId == userId).ExecuteDeleteAsync();
        await context.Notifications.IgnoreQueryFilters().Where(n => n.UserId == userId).ExecuteDeleteAsync();
    }

    [SqlFact]
    public async Task Favorite_watch_backfills_silently_then_notifies_once_per_week()
    {
        var userId = await db.CreateUserAsync();
        var productId = await db.CreateProductAsync(price: 1_000_000, stock: 5);
        await using var scope = db.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
        var watch = scope.ServiceProvider.GetRequiredService<IFavoritePriceWatch>();
        try
        {
            var favoriteId = await AddFavoriteAsync(context, userId, productId, null);

            // ۱. قیمت مرجع خالی ← پر می‌شود، بدون اعلان.
            var first = await watch.ProcessAsync(Noon);
            Assert.False(first.Quiet);
            Assert.True(first.Backfilled >= 1);
            Assert.Equal(1_000_000m, (await context.Favorites.AsNoTracking().SingleAsync(f => f.Id == favoriteId)).WatchPrice);
            Assert.Equal(0, await DropNotificationsAsync(context, userId));

            // ۲. کاهش ۶٪ ← اعلان؛ قیمت مرجع و زمان اعلان به‌روز می‌شود.
            await SetPriceAsync(context, productId, 940_000);
            await watch.ProcessAsync(Noon);
            context.ChangeTracker.Clear();
            var favorite = await context.Favorites.AsNoTracking().SingleAsync(f => f.Id == favoriteId);
            Assert.Equal(940_000m, favorite.WatchPrice);
            Assert.Equal(Noon, favorite.PriceAlertedAt);
            Assert.Equal(1, await DropNotificationsAsync(context, userId));
            var slug = await context.Products.Where(p => p.Id == productId).Select(p => p.Slug).SingleAsync();
            var notification = await context.Notifications.AsNoTracking().SingleAsync(n => n.UserId == userId && n.Event == NotificationEvent.PriceDrop);
            Assert.Equal("/product/" + slug, notification.LinkUrl);
            Assert.Equal(NotificationCategory.Promotion, notification.Category);

            // ۳. کاهش بیشتر فردا ← هنوز در فاصلهٔ ۷ روزه؛ اعلان تکراری نیست.
            await SetPriceAsync(context, productId, 800_000);
            await watch.ProcessAsync(Noon.AddDays(1));
            Assert.Equal(1, await DropNotificationsAsync(context, userId));

            // ۴. بعد از ۸ روز ← اعلان دوم (۸۰۰ هزار ≤ ۹۴۰ هزار × ۰٫۹۵).
            await watch.ProcessAsync(Noon.AddDays(8));
            Assert.Equal(2, await DropNotificationsAsync(context, userId));
        }
        finally
        {
            await CleanupFavoritesAsync(context, userId);
        }
    }

    [SqlFact]
    public async Task Favorite_watch_ignores_small_drops_and_quiet_hours()
    {
        var userId = await db.CreateUserAsync();
        var small = await db.CreateProductAsync(price: 970_000, stock: 5);
        var big = await db.CreateProductAsync(price: 500_000, stock: 5);
        var outOfStock = await db.CreateProductAsync(price: 100_000, stock: 0);
        await using var scope = db.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
        var watch = scope.ServiceProvider.GetRequiredService<IFavoritePriceWatch>();
        try
        {
            var smallId = await AddFavoriteAsync(context, userId, small, 1_000_000);
            await AddFavoriteAsync(context, userId, big, 1_000_000);
            await AddFavoriteAsync(context, userId, outOfStock, 1_000_000);

            // ساعت ۲۳:۳۰ تهران ← سکوت: هیچ اعلانی.
            var quiet = await watch.ProcessAsync(new DateTimeOffset(2026, 10, 22, 20, 0, 0, TimeSpan.Zero));
            Assert.True(quiet.Quiet);
            Assert.Equal(0, quiet.Notified);
            Assert.Equal(0, await DropNotificationsAsync(context, userId));

            // ظهر: فقط کالای موجودِ به‌اندازهٔ کافی ارزان‌شده (۵۰٪)؛ کاهش ۳٪ و کالای ناموجود نه.
            await watch.ProcessAsync(Noon);
            Assert.Equal(1, await DropNotificationsAsync(context, userId));
            var untouched = await context.Favorites.AsNoTracking().SingleAsync(f => f.Id == smallId);
            Assert.Equal(1_000_000m, untouched.WatchPrice);
            Assert.Null(untouched.PriceAlertedAt);
        }
        finally
        {
            await CleanupFavoritesAsync(context, userId);
        }
    }

    [SqlFact]
    public async Task Favorite_watch_defers_to_active_price_alert_and_caps_per_day()
    {
        var watcher = await db.CreateUserAsync();
        var busy = await db.CreateUserAsync();
        var alerted = await db.CreateProductAsync(price: 800_000, stock: 5);
        var p1 = await db.CreateProductAsync(price: 800_000, stock: 5);
        var p2 = await db.CreateProductAsync(price: 800_000, stock: 5);
        var p3 = await db.CreateProductAsync(price: 800_000, stock: 5);
        await using var scope = db.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
        var watch = scope.ServiceProvider.GetRequiredService<IFavoritePriceWatch>();
        try
        {
            // «خبرم کن – کاهش قیمت» فعال ← همان سیستم خبر می‌دهد؛ اینجا بی‌صدا فقط قیمت مرجع به‌روز می‌شود.
            var alertedFavorite = await AddFavoriteAsync(context, watcher, alerted, 1_000_000);
            context.ProductAlerts.Add(new ProductAlert { UserId = watcher, ProductId = alerted, Type = ProductAlertType.PriceDrop, PriceAtSubscribe = 1_000_000, IsActive = true });
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();

            // سه کالای ارزان‌شده برای یک مشتری ← حداکثر ۲ اعلان در روز.
            foreach (var product in new[] { p1, p2, p3 })
                await AddFavoriteAsync(context, busy, product, 1_000_000);

            await watch.ProcessAsync(Noon);
            context.ChangeTracker.Clear();

            Assert.Equal(0, await DropNotificationsAsync(context, watcher));
            var silent = await context.Favorites.AsNoTracking().SingleAsync(f => f.Id == alertedFavorite);
            Assert.Equal(800_000m, silent.WatchPrice);
            Assert.Null(silent.PriceAlertedAt);

            Assert.Equal(FavoritePriceWatch.PerUserDaily, await DropNotificationsAsync(context, busy));
            var waiting = await context.Favorites.AsNoTracking().Where(f => f.UserId == busy && f.PriceAlertedAt == null).ToListAsync();
            var left = Assert.Single(waiting);
            Assert.Equal(1_000_000m, left.WatchPrice);

            // فردا (بعد از ۲۴ ساعت) نوبت سومی هم می‌رسد.
            await watch.ProcessAsync(Noon.AddDays(1).AddHours(1));
            Assert.Equal(3, await DropNotificationsAsync(context, busy));
        }
        finally
        {
            await CleanupFavoritesAsync(context, watcher);
            await CleanupFavoritesAsync(context, busy);
        }
    }

    [SqlFact]
    public async Task Favorite_watch_does_nothing_when_disabled()
    {
        var userId = await db.CreateUserAsync();
        var productId = await db.CreateProductAsync(price: 500_000, stock: 5);
        await using var scope = db.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
        var watch = scope.ServiceProvider.GetRequiredService<IFavoritePriceWatch>();
        var smart = scope.ServiceProvider.GetRequiredService<ISmartConfigService>();
        var original = await smart.GetAsync();
        try
        {
            await AddFavoriteAsync(context, userId, productId, 1_000_000);
            var off = await smart.GetAsync();
            off.FavoritePriceAlert = false;
            Assert.True((await smart.SaveAsync(off)).IsSuccess());

            var result = await watch.ProcessAsync(Noon);
            Assert.Equal(new FavoriteWatchResult(0, 0, 0, false), result);
            Assert.Equal(0, await DropNotificationsAsync(context, userId));
        }
        finally
        {
            original.FavoritePriceAlert = true;
            await smart.SaveAsync(original);
            await CleanupFavoritesAsync(context, userId);
        }
    }

    [SqlFact]
    public async Task Adding_a_favorite_records_the_current_price()
    {
        var userId = await db.CreateUserAsync();
        var productId = await db.CreateProductAsync(price: 750_000, stock: 5);
        await using var scope = db.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
        var favorites = scope.ServiceProvider.GetRequiredService<IFavoriteService>();
        try
        {
            var added = await favorites.AddFavoriteAsync(new CreateFavoriteCommand { UserId = userId, ProductId = productId });
            Assert.True(added.IsSuccess(), added.Message);
            var favorite = await context.Favorites.AsNoTracking().SingleAsync(f => f.UserId == userId && f.ProductId == productId);
            Assert.Equal(750_000m, favorite.WatchPrice);
            Assert.Null(favorite.PriceAlertedAt);
        }
        finally
        {
            await CleanupFavoritesAsync(context, userId);
        }
    }
}
