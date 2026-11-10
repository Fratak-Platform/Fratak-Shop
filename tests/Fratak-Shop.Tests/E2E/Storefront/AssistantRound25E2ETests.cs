using System.Net;
using System.Text;
using System.Text.Json;
using Fratak_Shop.Data.Entities.Comments;
using Fratak_Shop.Data.Entities.Notifications;
using Fratak_Shop.Data.Entities.Products;
using Fratak_Shop.Tests.E2E.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Tests.E2E.Storefront;

/// <summary>
/// تست سرتاسری دور ۲۵ روی برنامهٔ واقعی (TestServer + SQL Server).
/// <list type="bullet">
/// <item>نظر 👍/👎 دربارهٔ پاسخ دستیار: بدون توکن ضدجعل یا با بدنهٔ خراب ← 400؛ فقط پیام همان گفت‌وگو ثبت می‌شود.</item>
/// <item>صفحهٔ «آموزش دستیار»: مدیر می‌بیند و ثبت/حذف می‌کند؛ لینک بیرونی رد می‌شود؛ مشتری و کارمند بی‌دسترسی راه ندارند.</item>
/// <item>نظرات و پرسش‌ها: پیش‌نویس هوشمند زیر پرسش؛ پاسخ مدیر ← اعلان «به پرسش شما پاسخ داده شد» برای پرسش‌کننده.</item>
/// <item>تنظیمات هوشمند: گزینه‌های «خبر ارزان شدن علاقه‌مندی‌ها» در فرم هست.</item>
/// </list>
/// </summary>
[Collection(E2ECollection.Name)]
[Trait("Category", "E2E")]
[Trait("Area", "Assistant")]
public class AssistantRound25E2ETests(ShopApp app)
{
    private static string NewSession() => "e2e25" + Guid.NewGuid().ToString("N")[..20];

    private static async Task<HttpResponseMessage> PostJsonAsync(ShopClient client, string url, object body, string? token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
        };
        if (token is not null)
            request.Headers.Add("RequestVerificationToken", token);
        return await client.Http.SendAsync(request);
    }

    // ── نظر دربارهٔ پاسخ ────────────────────────────────────────

    [SqlFact]
    public async Task Feedback_needs_token_and_valid_body_and_is_bound_to_session()
    {
        using var client = app.CreateClient();
        var session = NewSession();
        try
        {
            var token = await client.AntiforgeryTokenAsync();
            using var asked = await PostJsonAsync(client, "/api/assistant/message", new { text = "سلام", session, last = Array.Empty<Guid>() }, token);
            Assert.Equal(HttpStatusCode.OK, asked.StatusCode);
            var messageId = JsonDocument.Parse(await asked.Content.ReadAsStringAsync()).RootElement.GetProperty("messageId").GetGuid();

            // حمله‌ها: بدون توکن، توکن جعلی، بدنهٔ ناقص، شناسهٔ گفت‌وگوی خراب.
            using (var noToken = await PostJsonAsync(client, "/api/assistant/feedback", new { id = messageId, session, helpful = true }, null))
                Assert.Equal(HttpStatusCode.BadRequest, noToken.StatusCode);
            using (var forged = await PostJsonAsync(client, "/api/assistant/feedback", new { id = messageId, session, helpful = true }, "forged"))
                Assert.Equal(HttpStatusCode.BadRequest, forged.StatusCode);
            using (var missing = await PostJsonAsync(client, "/api/assistant/feedback", new { session, helpful = true }, token))
                Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
            using (var badSession = await PostJsonAsync(client, "/api/assistant/feedback", new { id = messageId, session = "<x>", helpful = true }, token))
                Assert.Equal(HttpStatusCode.BadRequest, badSession.StatusCode);

            // گفت‌وگوی دیگر ← ثبت نمی‌شود.
            using (var other = await PostJsonAsync(client, "/api/assistant/feedback", new { id = messageId, session = NewSession(), helpful = false }, token))
            {
                Assert.Equal(HttpStatusCode.OK, other.StatusCode);
                Assert.False(JsonDocument.Parse(await other.Content.ReadAsStringAsync()).RootElement.GetProperty("saved").GetBoolean());
            }

            using (var ok = await PostJsonAsync(client, "/api/assistant/feedback", new { id = messageId, session, helpful = true }, token))
            {
                Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
                Assert.True(JsonDocument.Parse(await ok.Content.ReadAsStringAsync()).RootElement.GetProperty("saved").GetBoolean());
                Assert.Contains("no-store", ok.Headers.CacheControl?.ToString() ?? "");
            }
            var helpful = await app.WithDbAsync(db => db.AssistantMessages.AsNoTracking().Where(m => m.Id == messageId).Select(m => m.Helpful).SingleAsync());
            Assert.True(helpful);
        }
        finally
        {
            await app.WithDbAsync(db => db.AssistantMessages.IgnoreQueryFilters().Where(m => m.SessionKey == session).ExecuteDeleteAsync());
        }
    }

    // ── آموزش دستیار ────────────────────────────────────────────

    [SqlFact]
    public async Task Teach_page_saves_rejects_external_links_and_is_admin_only()
    {
        var question = "آزمون آموزش ارسال به قشم " + Guid.NewGuid().ToString("N")[..6];
        using var owner = app.CreateClient();
        await owner.LoginAsync(ShopApp.OwnerPhone);
        try
        {
            var page = await owner.GetPageAsync("/admin/smart/assistant/teach");
            Assert.NotNull(page.One("form"));

            // لینک بیرونی ← ثبت نمی‌شود و به فرم برمی‌گردد.
            using (var bad = await owner.PostFormAsync("/admin/smart/assistant/teach?handler=Save",
                       ("question", question), ("answer", "بله"), ("url", "https://evil.example"), ("intent", ""), ("isActive", "true")))
            {
                Assert.Equal(HttpStatusCode.Redirect, bad.StatusCode);
                Assert.Contains("#form", bad.Headers.Location?.ToString() ?? "");
            }
            Assert.False(await app.WithDbAsync(db => db.AssistantTeachings.AnyAsync(t => t.Question == question)));

            using (var saved = await owner.PostFormAsync("/admin/smart/assistant/teach?handler=Save",
                       ("question", question), ("answer", "بله، به قشم هم ارسال داریم."), ("url", "/faq"), ("intent", ""), ("isActive", "true")))
            {
                Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);
                Assert.Equal("/admin/smart/assistant/teach", saved.Headers.Location?.ToString());
            }
            var id = await app.WithDbAsync(db => db.AssistantTeachings.AsNoTracking().Where(t => t.Question == question).Select(t => t.Id).SingleAsync());
            var listed = await owner.GetPageAsync("/admin/smart/assistant/teach");
            Assert.Contains(question, listed.Text, StringComparison.Ordinal);

            // بدون توکن ضدجعل ← 400.
            using (var csrf = await owner.PostRawFormAsync("/admin/smart/assistant/teach?handler=Delete", ("id", id.ToString())))
                Assert.Equal(HttpStatusCode.BadRequest, csrf.StatusCode);

            using (var deleted = await owner.PostFormAsync("/admin/smart/assistant/teach?handler=Delete", ("id", id.ToString())))
                Assert.Equal(HttpStatusCode.Redirect, deleted.StatusCode);
            Assert.False(await app.WithDbAsync(db => db.AssistantTeachings.AnyAsync(t => t.Id == id)));

            // مشتری ← 404؛ کارمند بی‌دسترسی ← نه 200.
            using var customer = app.CreateClient();
            await customer.LoginAsync(ShopApp.DemoCustomerPhone);
            using (var denied = await customer.GetAsync("/admin/smart/assistant/teach"))
                Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
            using var accountant = app.CreateClient();
            await accountant.LoginAsync(ShopApp.AccountantPhone);
            using (var staff = await accountant.GetAsync("/admin/smart/assistant/teach"))
                Assert.NotEqual(HttpStatusCode.OK, staff.StatusCode);
        }
        finally
        {
            await app.WithDbAsync(db => db.AssistantTeachings.IgnoreQueryFilters().Where(t => t.Question == question).ExecuteDeleteAsync());
        }
    }

    // ── پرسش و پاسخ کالا ────────────────────────────────────────

    [SqlFact]
    public async Task Admin_sees_question_drafts_and_reply_notifies_the_asker()
    {
        var customer = await app.CreateCustomerAsync();
        var (productId, _) = await app.CreateProductAsync(stock: 3);
        var questionId = await app.WithDbAsync(async db =>
        {
            db.ProductFeatures.Add(new ProductFeature { ProductId = productId, Key = "ظرفیت باتری", Value = "20000", DisplayOrder = 1 });
            var question = new Comment
            {
                UserId = customer.Id,
                ProductId = productId,
                Type = CommentType.Question,
                AuthorName = "مشتری آزمون",
                AuthorPhone = customer.Phone,
                Content = "ظرفیت باتری این کالا چقدره؟",
                Status = CommentStatus.Pending,
            };
            db.Comments.Add(question);
            await db.SaveChangesAsync();
            return question.Id;
        });
        try
        {
            using var owner = app.CreateClient();
            await owner.LoginAsync(ShopApp.OwnerPhone);
            var page = await owner.GetPageAsync("/admin/comments");
            var drafts = page.One($"[data-qa-drafts] button[data-fill-target='#reply-{questionId:N}']");
            Assert.NotNull(drafts);
            Assert.Contains("ظرفیت باتری", drafts!.GetAttribute("data-fill-text") ?? "", StringComparison.Ordinal);

            using (var reply = await owner.PostFormAsync("/admin/comments?handler=Reply", ("id", questionId.ToString()), ("content", "سلام، ظرفیت باتری ۲۰ هزار میلی‌آمپر است.")))
                Assert.Equal(HttpStatusCode.Redirect, reply.StatusCode);

            var notified = await app.WithDbAsync(db => db.Notifications.AsNoTracking()
                .Where(n => n.UserId == customer.Id && n.Event == NotificationEvent.QuestionAnswered).Select(n => n.LinkUrl).ToListAsync());
            var link = Assert.Single(notified);
            Assert.EndsWith("#quest", link, StringComparison.Ordinal);
        }
        finally
        {
            await app.WithDbAsync(async db =>
            {
                await db.Comments.IgnoreQueryFilters().Where(c => c.ParentId == questionId).ExecuteDeleteAsync();
                await db.Comments.IgnoreQueryFilters().Where(c => c.Id == questionId).ExecuteDeleteAsync();
                await db.Notifications.IgnoreQueryFilters().Where(n => n.UserId == customer.Id).ExecuteDeleteAsync();
            });
        }
    }

    // ── تنظیمات هوشمند ──────────────────────────────────────────

    [SqlFact]
    public async Task Smart_settings_show_favorite_price_alert_options()
    {
        using var owner = app.CreateClient();
        await owner.LoginAsync(ShopApp.OwnerPhone);
        var page = await owner.GetPageAsync("/admin/smart/settings");
        Assert.NotNull(page.One("input[type='checkbox'][name='Options.FavoritePriceAlert']"));
        var percent = page.One("input[name='Options.FavoritePriceDropPercent']");
        Assert.NotNull(percent);
        Assert.Equal("3", percent!.GetAttribute("min"));
        Assert.Equal("50", percent.GetAttribute("max"));
    }
}
