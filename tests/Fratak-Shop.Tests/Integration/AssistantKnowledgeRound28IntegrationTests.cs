using Fratak_Shop.Core.Services.Assistant;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Assistant;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;

namespace Fratak_Shop.Tests.Integration;

/// <summary>
/// دور ۲۸ – جدول «دانش دستیار» روی SQL Server واقعی:
/// <list type="bullet">
/// <item>بذر تکرارپذیر است (بار دوم چیزی اضافه نمی‌کند) و دانستنی حذف‌شده دوباره ساخته نمی‌شود.</item>
/// <item>ثبت: اعتبارسنجی (کوتاه/بلند، لینک بیرونی، تکراری)؛ دانستنی تازه بلافاصله در جواب دستیار استفاده می‌شود.</item>
/// <item>یادگیری شبانه: 👎های زیاد ← دانستنی خودکار خاموش می‌شود؛ رأی کم ← دست نمی‌خورد.</item>
/// <item>دستیار: «X یعنی چی» از واژه‌نامه، گپ کوتاه انسانی، و هم‌معنی‌ها؛ پیام خرید به واژه‌نامه نمی‌رود.</item>
/// </list>
/// هر آزمون دادهٔ خودش را در finally پاک می‌کند.
/// </summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", "Integration")]
[Trait("Area", "Round28")]
public class AssistantKnowledgeRound28IntegrationTests(SqlServerFixture db)
{
    private static string NewSession() => "r28k" + Guid.NewGuid().ToString("N")[..20];

    private static AssistantRequest Request(string text, string session) =>
        new(text, session, null, null, [], null, _ => Task.FromResult<Guid?>(null));

    private static string Unique() => "واژهتست" + new string(Guid.NewGuid().ToString("N")[..6].Select(c => (char)('ا' + (c % 10))).ToArray());

    [SqlFact]
    public async Task Seeding_is_idempotent_and_keeps_deleted_rows_deleted()
    {
        await using var scope = db.Scope();
        var facts = scope.ServiceProvider.GetRequiredService<IAssistantFactService>();
        var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();

        await facts.EnsureSeededAsync();
        var count = await context.AssistantFacts.IgnoreQueryFilters().CountAsync(f => f.IsSeed);
        Assert.True(count >= AssistantBrain.Seed.Count, $"{count} < {AssistantBrain.Seed.Count}");
        Assert.Equal(0, await facts.EnsureSeededAsync());

        // فهرست پنل همه را صفحه‌بندی‌شده می‌دهد؛ جست‌وجو کار می‌کند.
        var (items, total) = await facts.ListAsync(AssistantFactKind.Guide, "پاوربانک", 1, 50);
        Assert.True(total >= 1);
        Assert.Contains(items, i => i.Term == "پاوربانک");
        Assert.All(items, i => Assert.Equal(AssistantFactKind.Guide, i.Kind));
    }

    [SqlFact]
    public async Task Save_validates_input_and_new_fact_is_used_by_the_assistant_at_once()
    {
        await using var scope = db.Scope();
        var facts = scope.ServiceProvider.GetRequiredService<IAssistantFactService>();
        var assistant = scope.ServiceProvider.GetRequiredService<IAssistantService>();
        var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
        var term = Unique();
        var session = NewSession();
        Guid? id = null;
        try
        {
            Assert.False((await facts.SaveAsync(null, new AssistantFactInput(AssistantFactKind.Glossary, "x", null, "توضیح کافی", null, null), "آزمون")).IsSuccess());
            Assert.False((await facts.SaveAsync(null, new AssistantFactInput(AssistantFactKind.Glossary, term, null, "", null, null), "آزمون")).IsSuccess());
            Assert.False((await facts.SaveAsync(null, new AssistantFactInput(AssistantFactKind.Glossary, term, null, "توضیح", "https://evil.example", null), "آزمون")).IsSuccess());
            Assert.False((await facts.SaveAsync(null, new AssistantFactInput(AssistantFactKind.Glossary, term, null, "توضیح", "//evil.example", null), "آزمون")).IsSuccess());
            Assert.False((await facts.SaveAsync(null, new AssistantFactInput((AssistantFactKind)99, term, null, "توضیح", null, null), "آزمون")).IsSuccess());
            Assert.False((await facts.SaveAsync(null, new AssistantFactInput(AssistantFactKind.Glossary, term, null, new string('ب', 1501), null, null), "آزمون")).IsSuccess());

            var saved = await facts.SaveAsync(null, new AssistantFactInput(AssistantFactKind.Glossary, term, "شکل دیگر، " + term + "ها", "این یک کلمه آزمایشی است.", "/faq", "آزمون"), "مدیر آزمون");
            Assert.True(saved.IsSuccess(), saved.Message);
            id = saved.Data;
            var row = await context.AssistantFacts.AsNoTracking().SingleAsync(f => f.Id == id);
            Assert.False(row.IsSeed);
            Assert.Equal("مدیر آزمون", row.CreatedByName);
            Assert.Equal("شکل دیگر|" + term + "ها", row.Aliases);

            // تکراری با همان نوع ← رد.
            Assert.False((await facts.SaveAsync(null, new AssistantFactInput(AssistantFactKind.Glossary, term, null, "دوباره", null, null), "آزمون")).IsSuccess());

            var reply = await assistant.ReplyAsync(Request(term + " یعنی چی", session));
            Assert.Contains("کلمه آزمایشی", reply.Text, StringComparison.Ordinal);
            Assert.Contains(reply.Actions, a => a.Url == "/faq");
            var logged = await context.AssistantMessages.AsNoTracking().Where(m => m.SessionKey == session).OrderByDescending(m => m.CreatedAt).FirstAsync();
            Assert.Equal(AssistantBrain.TopicPrefix + id, logged.Topic);
            Assert.Equal(1, await context.AssistantFacts.AsNoTracking().Where(f => f.Id == id).Select(f => f.Shown).SingleAsync());

            // خاموش ← دیگر استفاده نمی‌شود.
            Assert.True((await facts.ToggleAsync(id.Value)).IsSuccess());
            var after = await assistant.ReplyAsync(Request(term + " یعنی چی", session));
            Assert.DoesNotContain("کلمه آزمایشی", after.Text, StringComparison.Ordinal);

            Assert.True((await facts.DeleteAsync(id.Value)).IsSuccess());
            Assert.Null(await facts.GetAsync(id.Value));
            Assert.False((await facts.DeleteAsync(id.Value)).IsSuccess());
        }
        finally
        {
            await context.AssistantMessages.IgnoreQueryFilters().Where(m => m.SessionKey == session).ExecuteDeleteAsync();
            await context.AssistantFacts.IgnoreQueryFilters().Where(f => f.Term == term).ExecuteDeleteAsync();
            scope.ServiceProvider.GetRequiredService<IMemoryCache>().Remove(AssistantFactService.CacheKey);
        }
    }

    [SqlFact]
    public async Task Nightly_learning_retires_facts_with_many_unhelpful_votes_only()
    {
        await using var scope = db.Scope();
        var facts = scope.ServiceProvider.GetRequiredService<IAssistantFactService>();
        var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
        var bad = Unique();
        var good = Unique();
        var session = NewSession();
        try
        {
            var badId = (await facts.SaveAsync(null, new AssistantFactInput(AssistantFactKind.Learned, bad, null, "جواب نامفید", null, null), "آزمون")).Data;
            var goodId = (await facts.SaveAsync(null, new AssistantFactInput(AssistantFactKind.Learned, good, null, "جواب مفید", null, null), "آزمون")).Data;
            for (var i = 0; i < 8; i++)
                context.AssistantMessages.Add(new AssistantMessage { SessionKey = session, Text = bad, Topic = AssistantBrain.TopicPrefix + badId, Helpful = false, Answered = true });
            for (var i = 0; i < 3; i++)
                context.AssistantMessages.Add(new AssistantMessage { SessionKey = session, Text = good, Topic = AssistantBrain.TopicPrefix + goodId, Helpful = i > 0, Answered = true });
            // موضوع خراب نادیده گرفته می‌شود.
            context.AssistantMessages.Add(new AssistantMessage { SessionKey = session, Text = "x", Topic = AssistantBrain.TopicPrefix + "not-a-guid", Helpful = false, Answered = true });
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();

            Assert.True(await facts.LearnAsync() >= 2);
            var badRow = await context.AssistantFacts.AsNoTracking().SingleAsync(f => f.Id == badId);
            var goodRow = await context.AssistantFacts.AsNoTracking().SingleAsync(f => f.Id == goodId);
            Assert.False(badRow.IsActive);
            Assert.Equal(8, badRow.Unhelpful);
            Assert.True(goodRow.IsActive);
            Assert.Equal(2, goodRow.Helpful);
            Assert.Equal(1, goodRow.Unhelpful);

            // بار دوم بدون رأی تازه چیزی را تغییر نمی‌دهد.
            Assert.Equal(0, await facts.LearnAsync());
        }
        finally
        {
            await context.AssistantMessages.IgnoreQueryFilters().Where(m => m.SessionKey == session).ExecuteDeleteAsync();
            await context.AssistantFacts.IgnoreQueryFilters().Where(f => f.Term == bad || f.Term == good).ExecuteDeleteAsync();
            scope.ServiceProvider.GetRequiredService<IMemoryCache>().Remove(AssistantFactService.CacheKey);
        }
    }

    [SqlFact]
    public async Task Assistant_answers_guides_glossary_and_small_talk_from_the_seed()
    {
        await using var scope = db.Scope();
        var assistant = scope.ServiceProvider.GetRequiredService<IAssistantService>();
        var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
        var session = NewSession();
        try
        {
            var guide = await assistant.ReplyAsync(Request("برای خرید پاوربانک به چی دقت کنم", session));
            Assert.Contains("پاوربانک", guide.Text, StringComparison.Ordinal);
            Assert.Contains("۱.", guide.Text, StringComparison.Ordinal);
            Assert.Contains(guide.Actions, a => a.Say == "پاوربانک");

            var glossary = await assistant.ReplyAsync(Request("ANC یعنی چی", session));
            Assert.Contains("«ANC»", glossary.Text, StringComparison.OrdinalIgnoreCase);

            var robot = await assistant.ReplyAsync(Request("ربات هستی؟", session));
            Assert.True(robot.Text.Length > 5);

            // پرسش خرید به واژه‌نامه نمی‌رود.
            var shopping = await assistant.ReplyAsync(Request("قیمت پاوربانک چیه", session));
            Assert.DoesNotContain("«پاوربانک»:", shopping.Text, StringComparison.Ordinal);

            var topics = await context.AssistantMessages.AsNoTracking().Where(m => m.SessionKey == session && m.Topic != null).Select(m => m.Topic!).ToListAsync();
            Assert.True(topics.Count(t => t.StartsWith(AssistantBrain.TopicPrefix, StringComparison.Ordinal)) >= 2);
        }
        finally
        {
            await context.AssistantMessages.IgnoreQueryFilters().Where(m => m.SessionKey == session).ExecuteDeleteAsync();
        }
    }
}
