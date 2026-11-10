using Fratak_Shop.Core.Services.Assistant;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Assistant;
using Fratak_Shop.Data.Entities.Categories;
using Fratak_Shop.Data.Entities.Contents;
using Fratak_Shop.Data.Entities.Products;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;

namespace Fratak_Shop.Tests.Integration;

/// <summary>
/// تست یکپارچهٔ دور ۲۴ برای «دستیار هوشمند» روی SQL Server واقعی.
/// <list type="bullet">
/// <item>گفت‌وگو با کارشناس: باز شدن با «کارشناس»، رفتن پیام‌های بعدی به کارشناس، جواب کارشناس، بستن، سقف پیام و بستن خودکار.</item>
/// <item>دانش سایت: صفحهٔ محتوایی تازه با کلمهٔ یکتایش پیدا و به آن لینک داده می‌شود.</item>
/// <item>شخصیت: «منو X صدا کن»، «رسمی صحبت کن» و «فراموش کن» در AssistantProfiles ذخیره می‌شود؛ لحن از تاریخ تولد می‌آید.</item>
/// <item>ورود: موبایل نوشته‌شده در کادر پر می‌شود ولی در گزارش پوشانده است؛ رمز هرگز پر نمی‌شود.</item>
/// <item>کالایاب مرحله‌ای و پرسش‌های پیشنهادی صفحهٔ کالا با دادهٔ واقعی.</item>
/// </list>
/// هر آزمون فقط دادهٔ خودش را می‌سازد و در finally پاک می‌کند.
/// </summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", "Integration")]
[Trait("Area", "Assistant")]
public class AssistantRound24IntegrationTests(SqlServerFixture db)
{
    private const string Letters = "ابپتثجچحخدذرزسشصضطظعغفقکگلمنوی";

    /// <summary>واژهٔ فارسی تصادفی و یکتا (با «ژ» در پایان تا با واژهٔ واقعی یکی نشود).</summary>
    private static string Word() => new string(Enumerable.Range(0, 6).Select(_ => Letters[Random.Shared.Next(Letters.Length)]).ToArray()) + "ژ";

    private static string NewSession() => "r24" + Guid.NewGuid().ToString("N")[..20];

    private static AssistantRequest Request(string text, string session, Guid? userId = null, Guid? current = null, string? flow = null, string? page = null) =>
        new(text, session, userId, current, [], null, _ => Task.FromResult<Guid?>(null)) { Flow = flow, PagePath = page };

    private static async Task CleanupHandoffsAsync(FratakShopDbContext context, params string[] sessions)
    {
        context.ChangeTracker.Clear();
        var ids = await context.AssistantHandoffs.IgnoreQueryFilters().Where(h => sessions.Contains(h.SessionKey)).Select(h => h.Id).ToListAsync();
        foreach (var id in ids)
        {
            var marker = id.ToString();
            await context.StaffNotifications.IgnoreQueryFilters().Where(n => n.Url != null && n.Url.Contains(marker)).ExecuteDeleteAsync();
        }
        await context.AssistantHandoffMessages.IgnoreQueryFilters().Where(m => ids.Contains(m.HandoffId)).ExecuteDeleteAsync();
        await context.AssistantHandoffs.IgnoreQueryFilters().Where(h => ids.Contains(h.Id)).ExecuteDeleteAsync();
        await context.AssistantMessages.IgnoreQueryFilters().Where(m => sessions.Contains(m.SessionKey)).ExecuteDeleteAsync();
    }

    // ── گفت‌وگو با کارشناس ──────────────────────────────────────

    [SqlFact]
    public async Task Human_request_opens_handoff_and_messages_flow_both_ways_until_closed()
    {
        await using var scope = db.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
        var assistant = scope.ServiceProvider.GetRequiredService<IAssistantService>();
        var handoffs = scope.ServiceProvider.GetRequiredService<IAssistantHandoffService>();
        var session = NewSession();
        try
        {
            var open = await assistant.ReplyAsync(Request("میخوام با کارشناس صحبت کنم", session, page: "/cart"));
            Assert.Equal(AssistantIntent.Human, open.Intent);
            Assert.True(open.Handoff);
            Assert.True(await handoffs.IsOpenAsync(session));

            var row = await context.AssistantHandoffs.AsNoTracking().SingleAsync(h => h.SessionKey == session);
            Assert.Equal(AssistantHandoffStatus.Waiting, row.Status);
            Assert.Equal("مهمان", row.DisplayName);
            Assert.Equal("/cart", row.PageUrl);

            // دوباره خواستن کارشناس گفت‌وگوی تازه نمی‌سازد.
            var (sameId, created) = await handoffs.OpenAsync(session, null, "/");
            Assert.False(created);
            Assert.Equal(row.Id, sameId);

            // پیام بعدی به کارشناس می‌رود، نه به دستیار؛ موبایل پوشانده است.
            var forwarded = await assistant.ReplyAsync(Request("سفارشم کجاست؟ شمارم 09121234567", session));
            Assert.Equal(AssistantIntent.Handoff, forwarded.Intent);
            Assert.True(forwarded.Handoff);
            var customerText = await context.AssistantHandoffMessages.AsNoTracking()
                .Where(m => m.HandoffId == row.Id && !m.FromStaff && m.AuthorName == AssistantHandoffService.CustomerAuthor).Select(m => m.Text).SingleAsync();
            Assert.Contains("سفارشم کجاست", customerText);
            Assert.DoesNotContain("09121234567", customerText);

            var list = await handoffs.ListAsync(includeClosed: false);
            var summary = Assert.Single(list, h => h.Id == row.Id);
            Assert.True(summary.Unread >= 2);

            var before = DateTimeOffset.UtcNow.AddMinutes(-1).ToUnixTimeMilliseconds();
            Assert.True(await handoffs.ReplyAsync(row.Id, Guid.NewGuid(), "کارشناس آزمایشی", "سلام، سفارش شما در حال آماده‌سازی است."));
            var poll = await handoffs.PollAsync(session, before);
            Assert.True(poll.Open);
            Assert.Equal(AssistantHandoffStatus.Active, poll.Status);
            var staff = Assert.Single(poll.Messages);
            Assert.True(staff.FromStaff);
            Assert.Equal("کارشناس آزمایشی", staff.Author);
            Assert.Empty((await handoffs.PollAsync(session, staff.At)).Messages);

            var detail = await handoffs.GetAsync(row.Id);
            Assert.NotNull(detail);
            Assert.True(detail.Value.Messages.Count >= 3);
            Assert.Equal(0, await context.AssistantHandoffs.AsNoTracking().Where(h => h.Id == row.Id).Select(h => h.UnreadForStaff).SingleAsync());

            // بستن توسط مشتری؛ بعد از آن دستیار دوباره خودش جواب می‌دهد و کارشناس نمی‌تواند پیام بدهد.
            Assert.True(await handoffs.CloseByCustomerAsync(session));
            Assert.False(await handoffs.IsOpenAsync(session));
            Assert.False(await handoffs.ReplyAsync(row.Id, Guid.NewGuid(), "کارشناس", "سلام"));
            Assert.False((await handoffs.PollAsync(session, 0)).Open);
            var back = await assistant.ReplyAsync(Request("سلام", session));
            Assert.Equal(AssistantIntent.Greeting, back.Intent);
            Assert.False(back.Handoff);
        }
        finally
        {
            await CleanupHandoffsAsync(context, session);
        }
    }

    [SqlFact]
    public async Task Handoff_rejects_empty_spam_unknown_sessions_and_external_pages()
    {
        await using var scope = db.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
        var handoffs = scope.ServiceProvider.GetRequiredService<IAssistantHandoffService>();
        var session = NewSession();
        var stranger = NewSession();
        try
        {
            Assert.False(await handoffs.PostCustomerAsync(stranger, "سلام"));
            Assert.False((await handoffs.PollAsync(stranger, 0)).Open);
            Assert.False(await handoffs.CloseByCustomerAsync(stranger));

            var (id, created) = await handoffs.OpenAsync(session, null, "https://evil.example/phish");
            Assert.True(created);
            Assert.Null(await context.AssistantHandoffs.AsNoTracking().Where(h => h.Id == id).Select(h => h.PageUrl).SingleAsync());

            Assert.False(await handoffs.PostCustomerAsync(session, "   "));
            Assert.False(await handoffs.ReplyAsync(id, Guid.NewGuid(), "کارشناس", ""));
            Assert.False(await handoffs.ReplyAsync(Guid.NewGuid(), Guid.NewGuid(), "کارشناس", "سلام"));

            // پیام خیلی بلند بریده می‌شود.
            Assert.True(await handoffs.PostCustomerAsync(session, new string('ا', 5000)));
            var longest = await context.AssistantHandoffMessages.AsNoTracking().Where(m => m.HandoffId == id).MaxAsync(m => m.Text.Length);
            Assert.Equal(AssistantHandoffService.MaxText, longest);

            // سقف پیام در ساعت (ضد اسپم).
            for (var i = 1; i < AssistantHandoffService.MaxCustomerPerHour; i++)
                Assert.True(await handoffs.PostCustomerAsync(session, "پیام " + i));
            Assert.False(await handoffs.PostCustomerAsync(session, "یکی بیشتر از سقف"));
        }
        finally
        {
            await CleanupHandoffsAsync(context, session, stranger);
        }
    }

    [SqlFact]
    public async Task Idle_handoffs_are_closed_automatically()
    {
        await using var scope = db.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
        var handoffs = scope.ServiceProvider.GetRequiredService<IAssistantHandoffService>();
        var idle = NewSession();
        var fresh = NewSession();
        try
        {
            var (idleId, _) = await handoffs.OpenAsync(idle, null, null);
            var (freshId, _) = await handoffs.OpenAsync(fresh, null, null);
            await context.AssistantHandoffs.Where(h => h.Id == idleId).ExecuteUpdateAsync(s => s.SetProperty(h => h.LastMessageAt, DateTimeOffset.UtcNow.AddHours(-30)));
            context.ChangeTracker.Clear();

            Assert.True(await handoffs.WaitingCountAsync() >= 2);
            Assert.True(await handoffs.AutoCloseAsync() >= 1);
            Assert.Equal(AssistantHandoffStatus.Closed, await context.AssistantHandoffs.AsNoTracking().Where(h => h.Id == idleId).Select(h => h.Status).SingleAsync());
            Assert.Equal(AssistantHandoffStatus.Waiting, await context.AssistantHandoffs.AsNoTracking().Where(h => h.Id == freshId).Select(h => h.Status).SingleAsync());
            Assert.DoesNotContain(await handoffs.ListAsync(includeClosed: false), h => h.Id == idleId);
            Assert.Contains(await handoffs.ListAsync(includeClosed: true), h => h.Id == idleId);
        }
        finally
        {
            await CleanupHandoffsAsync(context, idle, fresh);
        }
    }

    // ── دانش سایت ───────────────────────────────────────────────

    [SqlFact]
    public async Task Site_knowledge_answers_from_content_pages()
    {
        await using var scope = db.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
        var assistant = scope.ServiceProvider.GetRequiredService<IAssistantService>();
        var knowledge = scope.ServiceProvider.GetRequiredService<IAssistantKnowledge>();
        var cache = scope.ServiceProvider.GetRequiredService<IMemoryCache>();
        var word = Word();
        var slug = "assistant-kb-" + Guid.NewGuid().ToString("N")[..10];
        var page = new ContentPage
        {
            Title = "راهنمای " + word,
            Slug = slug,
            Summary = "توضیح کامل دربارهٔ " + word + " برای مشتریان فروشگاه.",
            Content = "<p>این صفحه همه چیز را دربارهٔ " + word + " توضیح می‌دهد. پاسخ پرسش‌های رایج هم اینجاست.</p>",
            IsActive = true,
            ShowInFooter = false,
        };
        context.ContentPages.Add(page);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var session = NewSession();
        try
        {
            cache.Remove(AssistantKnowledge.CacheKey);
            var hits = await knowledge.SearchAsync(word);
            var top = Assert.Single(hits);
            Assert.Equal($"/page/{slug}", top.Entry.Url);
            Assert.Equal(KnowledgeKind.Page, top.Entry.Kind);
            Assert.Contains(word, top.Snippet);
            Assert.Empty(await knowledge.SearchAsync("باشه"));

            var reply = await assistant.ReplyAsync(Request(word, session));
            Assert.Contains(reply.Actions, a => a.Url == $"/page/{slug}");
            Assert.Contains(word, reply.Text);
            Assert.DoesNotContain(AssistantService.NotCarried, reply.Text);
        }
        finally
        {
            context.ChangeTracker.Clear();
            await context.ContentPages.IgnoreQueryFilters().Where(p => p.Id == page.Id).ExecuteDeleteAsync();
            await context.AssistantMessages.IgnoreQueryFilters().Where(m => m.SessionKey == session).ExecuteDeleteAsync();
            await context.SearchQueryLogs.IgnoreQueryFilters().Where(l => l.Query.Contains(word)).ExecuteDeleteAsync();
            cache.Remove(AssistantKnowledge.CacheKey);
        }
    }

    // ── شخصیت و لحن ─────────────────────────────────────────────

    [SqlFact]
    public async Task Preferences_and_birth_date_shape_how_the_assistant_addresses_a_member()
    {
        await using var scope = db.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
        var assistant = scope.ServiceProvider.GetRequiredService<IAssistantService>();
        var userId = await db.CreateUserAsync(); // نام: «کاربر آزمایشی»
        var session = NewSession();
        try
        {
            // سالمند ← رسمی با نام و نام خانوادگی.
            await context.Users.Where(u => u.Id == userId).ExecuteUpdateAsync(s => s.SetProperty(u => u.BirthDate, new DateOnly(1950, 3, 1)));
            context.ChangeTracker.Clear();
            var formal = await assistant.ReplyAsync(Request("سلام", session, userId));
            Assert.Equal(AssistantIntent.Greeting, formal.Intent);
            Assert.Contains("کاربر آزمایشی گرامی", formal.Text);
            Assert.Contains("آوا", formal.Text);

            // جوان ← خودمانی با نام کوچک.
            await context.Users.Where(u => u.Id == userId).ExecuteUpdateAsync(s => s.SetProperty(u => u.BirthDate, new DateOnly(2004, 3, 1)));
            context.ChangeTracker.Clear();
            var casual = await assistant.ReplyAsync(Request("سلام", session, userId));
            Assert.StartsWith("سلام کاربر!", casual.Text, StringComparison.Ordinal);
            Assert.DoesNotContain("گرامی", casual.Text);

            // «منو امین صدا کن» ← ذخیره در پروفایل و استفاده در پیام‌های بعد.
            var call = await assistant.ReplyAsync(Request("منو امین صدا کن", session, userId));
            Assert.Equal(AssistantIntent.Preference, call.Intent);
            Assert.Equal("امین", call.Style?.CallMe);
            Assert.Equal("امین", await context.AssistantProfiles.AsNoTracking().Where(p => p.UserId == userId).Select(p => p.CallMe).SingleAsync());
            Assert.Contains("امین", (await assistant.ReplyAsync(Request("سلام", session, userId))).Text);

            // «رسمی صحبت کن» بر نسل غلبه می‌کند.
            var tone = await assistant.ReplyAsync(Request("رسمی صحبت کن", session, userId));
            Assert.Equal(AssistantTone.Formal, tone.Style?.Tone);
            Assert.Contains("امین گرامی", (await assistant.ReplyAsync(Request("سلام", session, userId))).Text);

            // «اسمم رو فراموش کن» ← برگشت به نام حساب.
            await assistant.ReplyAsync(Request("اسمم رو فراموش کن", session, userId));
            var profile = await context.AssistantProfiles.AsNoTracking().SingleAsync(p => p.UserId == userId);
            Assert.Null(profile.CallMe);
            Assert.Equal(AssistantTone.Formal, profile.Tone);
            Assert.Contains("کاربر آزمایشی گرامی", (await assistant.ReplyAsync(Request("سلام", session, userId))).Text);

            // «اسم من چیه» اسم تازه نیست.
            await assistant.ReplyAsync(Request("اسم من چیه", session, userId));
            Assert.Null(await context.AssistantProfiles.AsNoTracking().Where(p => p.UserId == userId).Select(p => p.CallMe).SingleAsync());

            // طرز نوشتن خودمانی ذخیره می‌شود.
            await assistant.ReplyAsync(Request("داداش اینو داری؟", session, userId));
            Assert.True(await context.AssistantProfiles.AsNoTracking().Where(p => p.UserId == userId).Select(p => p.Register).SingleAsync() >= 1);
        }
        finally
        {
            context.ChangeTracker.Clear();
            await context.AssistantProfiles.IgnoreQueryFilters().Where(p => p.UserId == userId).ExecuteDeleteAsync();
            await context.AssistantMessages.IgnoreQueryFilters().Where(m => m.SessionKey == session).ExecuteDeleteAsync();
        }
    }

    [SqlFact]
    public async Task Guest_preferences_round_trip_through_style_without_database_rows()
    {
        await using var scope = db.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
        var assistant = scope.ServiceProvider.GetRequiredService<IAssistantService>();
        var session = NewSession();
        try
        {
            var call = await assistant.ReplyAsync(Request("منو سارا صدا کن", session));
            Assert.Equal("سارا", call.Style?.CallMe);
            var hello = await assistant.ReplyAsync(Request("سلام", session) with { Style = call.Style });
            Assert.Contains("سارا", hello.Text);
            Assert.Equal(0, await context.AssistantProfiles.CountAsync(p => p.CallMe == "سارا" && p.CreatedAt > DateTimeOffset.UtcNow.AddMinutes(-5)));
        }
        finally
        {
            await context.AssistantMessages.IgnoreQueryFilters().Where(m => m.SessionKey == session).ExecuteDeleteAsync();
        }
    }

    // ── ورود ────────────────────────────────────────────────────

    [SqlFact]
    public async Task Login_help_fills_phone_but_logs_it_masked_and_never_touches_password()
    {
        await using var scope = db.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
        var assistant = scope.ServiceProvider.GetRequiredService<IAssistantService>();
        var session = NewSession();
        try
        {
            var reply = await assistant.ReplyAsync(Request("میخوام با ۰۹۱۲۱۲۳۴۵۶۷ وارد بشم", session, page: "/cart"));
            Assert.Equal(AssistantIntent.Login, reply.Intent);
            Assert.StartsWith("/login?mode=otp&returnUrl=%2Fcart", reply.Steps[0].Url, StringComparison.Ordinal);
            Assert.Contains(reply.Steps, s => s.Kind == "fill" && s.Target == "otp-phone" && s.Value == "09121234567");
            Assert.DoesNotContain(reply.Steps, s => s.Target == "login-password" && s.Kind == "fill");

            var password = await assistant.ReplyAsync(Request("با رمز عبور وارد بشم", session));
            Assert.Contains(password.Steps, s => s.Kind == "focus" && s.Target == "login-password");
            Assert.DoesNotContain(password.Steps, s => s.Kind == "fill");

            var logged = await context.AssistantMessages.AsNoTracking().Where(m => m.SessionKey == session).Select(m => m.Text).ToListAsync();
            Assert.Equal(2, logged.Count);
            Assert.All(logged, t => Assert.DoesNotContain("09121234567", t));
            Assert.All(logged, t => Assert.DoesNotContain("۰۹۱۲۱۲۳۴۵۶۷", t));
        }
        finally
        {
            await context.AssistantMessages.IgnoreQueryFilters().Where(m => m.SessionKey == session).ExecuteDeleteAsync();
        }
    }

    // ── کالایاب و پرسش‌های صفحه ─────────────────────────────────

    [SqlFact]
    public async Task Finder_walks_from_category_to_priority_to_real_products_and_suggest_reads_the_product_page()
    {
        await using var scope = db.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
        var assistant = scope.ServiceProvider.GetRequiredService<IAssistantService>();
        var key = Guid.NewGuid().ToString("N")[..10];
        var category = new Category { Name = "دستهٔ کالایاب " + Word(), Slug = "finder-cat-" + key, IsActive = true };
        context.Categories.Add(category);
        var product = new Product
        {
            CategoryId = category.Id,
            Name = "کالای کالایاب " + Word(),
            Slug = "finder-" + key,
            SKU = "FD-" + key,
            Price = 390_000,
            StockQuantity = 7,
            IsActive = true,
            PublishedAt = DateTimeOffset.UtcNow.AddDays(-1),
        };
        context.Products.Add(product);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var session = NewSession();
        try
        {
            var start = await assistant.ReplyAsync(Request("کمکم کن انتخاب کنم", session, flow: AssistantFinder.Build(category.Slug)));
            Assert.Equal(AssistantIntent.Finder, start.Intent);
            // با یک کالا بازهٔ قیمت معنا ندارد ← مستقیم پرسش اولویت.
            Assert.Equal(AssistantFinder.Priorities.Count, start.Actions.Count);
            Assert.All(start.Actions, a => Assert.NotNull(AssistantFinder.Parse(a.Flow)));
            var best = Assert.Single(start.Actions, a => a.Flow == AssistantFinder.Build(category.Slug, 0, "best"));

            var result = await assistant.ReplyAsync(Request(best.Say ?? best.Label, session, flow: best.Flow));
            Assert.Equal(AssistantIntent.Finder, result.Intent);
            Assert.Contains(result.Cards, c => c.Id == product.Id);
            Assert.Contains(result.Actions, a => a.Url != null && a.Url.StartsWith($"/category/{category.Slug}?", StringComparison.Ordinal));

            var tampered = await assistant.ReplyAsync(Request("x", session, flow: "finder:<script>"));
            Assert.NotEqual(AssistantIntent.Finder, tampered.Intent);

            var suggest = await assistant.SuggestAsync($"/product/{product.Slug}", product.Id, null, null);
            Assert.Equal("آوا", suggest.Name);
            Assert.StartsWith("سلام", suggest.Intro, StringComparison.Ordinal);
            Assert.Contains(product.Name, suggest.Intro);
            Assert.Contains(suggest.Chips, c => c.Say == "در مورد این کالا توضیح بده");
            Assert.Contains(suggest.Chips, c => c.Say == "اینو به سبد اضافه کن");

            var about = await assistant.ReplyAsync(Request("در مورد این کالا توضیح بده", session, current: product.Id));
            Assert.Equal(AssistantIntent.ProductQuestion, about.Intent);
            Assert.Contains(product.Name, about.Text);
            Assert.DoesNotContain(AssistantService.NotCarried, about.Text);
        }
        finally
        {
            context.ChangeTracker.Clear();
            await context.AssistantMessages.IgnoreQueryFilters().Where(m => m.SessionKey == session || m.ProductId == product.Id).ExecuteDeleteAsync();
            await context.SmartTasks.IgnoreQueryFilters().Where(t => t.TargetId == product.Id).ExecuteDeleteAsync();
            await context.Products.IgnoreQueryFilters().Where(p => p.Id == product.Id).ExecuteDeleteAsync();
            await context.Categories.IgnoreQueryFilters().Where(c => c.Id == category.Id).ExecuteDeleteAsync();
        }
    }
}
