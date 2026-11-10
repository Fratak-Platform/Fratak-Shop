using Fratak_Shop.Core.Services.Assistant;
using Fratak_Shop.Core.Services.Shopping;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Assistant;
using Fratak_Shop.Data.Entities.Categories;
using Fratak_Shop.Data.Entities.Products;
using Fratak_Shop.Data.Entities.Smart;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Fratak_Shop.Tests.Integration;

/// <summary>
/// تست یکپارچهٔ دور ۲۳ برای «دستیار فروشگاه» (<see cref="AssistantService"/>) روی SQL Server واقعی.
/// <list type="bullet">
/// <item>جست‌وجو کالای واقعی را کارت می‌کند؛ پرسش مشخصه از ویژگی‌های ثبت‌شدهٔ همان کالا جواب می‌گیرد.</item>
/// <item>پرسش بی‌جواب ← جست‌وجوی اینترنت پیشنهاد می‌شود، بی‌جواب ثبت می‌شود و از ۲ بار به بعد «دریافت مشخصات» در صف هوشمند می‌رود.</item>
/// <item>سبد: افزودن با دکمهٔ کارت، دیدن سبد و حذف با «حذفش کن».</item>
/// <item>پیگیری: کاربر فقط سفارش‌های خودش را می‌بیند (شمارهٔ سفارش دیگران ← «پیدا نشد»).</item>
/// <item>کالای ناموجود ← «کالاهای درخواستی» در گزارش؛ متن ذخیره‌شده بدون موبایل؛ پاک‌سازی پیام‌های قدیمی.</item>
/// </list>
/// هر آزمون فقط دادهٔ خودش را می‌سازد و در finally پاک می‌کند.
/// </summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", "Integration")]
[Trait("Area", "Assistant")]
public class AssistantIntegrationTests(SqlServerFixture db)
{
    private const string Letters = "ابپتثجچحخدذرزسشصضطظعغفقکگلمنوی";

    /// <summary>واژهٔ فارسی تصادفی و یکتا (حرف آخر «ژ» تا ریشه‌یاب چیزی از آن کم نکند).</summary>
    // دور ۲۵: حرف تکراری پشت‌سرهم ساخته نمی‌شود (دستیار «ببب» را به «ب» کوتاه می‌کند و واژهٔ تصادفی عوض می‌شد).
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

    private static string NewSession() => "it" + Guid.NewGuid().ToString("N")[..20];

    private static AssistantRequest Request(string text, string session, Guid? userId = null, Guid? current = null, Guid? action = null,
        Func<bool, Task<Guid?>>? cart = null) =>
        new(text, session, userId, current, [], action, cart ?? (_ => Task.FromResult<Guid?>(null)));

    private static async Task<(Guid Category, Guid Product, string Name)> SeedAsync(FratakShopDbContext context, int stock = 3, bool withFeature = true)
    {
        var key = Guid.NewGuid().ToString("N")[..10];
        var category = new Category { Name = "دستهٔ دستیار " + Word(), Slug = "assistant-cat-" + key, IsActive = true };
        context.Categories.Add(category);
        var name = $"پاوربانک آزمایشی {Word()}";
        var product = new Product
        {
            CategoryId = category.Id,
            Name = name,
            Slug = "assistant-" + key,
            SKU = "AS-" + key,
            Price = 250_000,
            StockQuantity = stock,
            IsActive = true,
            PublishedAt = DateTimeOffset.UtcNow.AddDays(-1),
        };
        context.Products.Add(product);
        if (withFeature)
            context.ProductFeatures.Add(new ProductFeature { ProductId = product.Id, Key = "ظرفیت باتری", Value = "10000 میلی‌آمپر ساعت", DisplayOrder = 1 });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        return (category.Id, product.Id, name);
    }

    private static async Task CleanupAsync(FratakShopDbContext context, Guid categoryId, Guid productId, params string[] sessions)
    {
        context.ChangeTracker.Clear();
        await context.AssistantMessages.IgnoreQueryFilters().Where(m => sessions.Contains(m.SessionKey) || m.ProductId == productId).ExecuteDeleteAsync();
        await context.SmartTasks.IgnoreQueryFilters().Where(t => t.TargetId == productId).ExecuteDeleteAsync();
        await context.CartItems.IgnoreQueryFilters().Where(i => i.ProductId == productId).ExecuteDeleteAsync();
        await context.ProductFeatures.IgnoreQueryFilters().Where(f => f.ProductId == productId).ExecuteDeleteAsync();
        await context.Products.IgnoreQueryFilters().Where(p => p.Id == productId).ExecuteDeleteAsync();
        await context.Categories.IgnoreQueryFilters().Where(c => c.Id == categoryId).ExecuteDeleteAsync();
    }

    [SqlFact]
    public async Task Search_and_feature_questions_answer_from_real_product_data()
    {
        await using var scope = db.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
        var assistant = scope.ServiceProvider.GetRequiredService<IAssistantService>();
        var (categoryId, productId, name) = await SeedAsync(context);
        var session = NewSession();
        try
        {
            var search = await assistant.ReplyAsync(Request(name, session));
            Assert.Equal(AssistantIntent.Search, search.Intent);
            Assert.Contains(search.Cards, c => c.Id == productId && c.Url.StartsWith("/product/", StringComparison.Ordinal));

            var battery = await assistant.ReplyAsync(Request("ظرفیت باتری این چقدره", session, current: productId));
            Assert.Equal(AssistantIntent.ProductQuestion, battery.Intent);
            Assert.Contains("10000 میلی‌آمپر", battery.Text);
            Assert.Null(battery.Lookup);

            var price = await assistant.ReplyAsync(Request("قیمتش چنده", session, current: productId));
            Assert.Contains(Fratak_Shop.Core.Services.Seo.Engine.PersianText.Money(250_000m), price.Text);
            Assert.Contains("فقط", price.Text);

            var rows = await context.AssistantMessages.AsNoTracking().Where(m => m.SessionKey == session).ToListAsync();
            Assert.Equal(3, rows.Count);
            Assert.All(rows, r => Assert.True(r.Answered));
            Assert.Contains(rows, r => r.ProductId == productId && r.Topic == "باتری");
        }
        finally
        {
            await CleanupAsync(context, categoryId, productId, session);
        }
    }

    [SqlFact]
    public async Task Unanswered_feature_questions_suggest_web_lookup_and_feed_smart_specs()
    {
        await using var scope = db.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
        var assistant = scope.ServiceProvider.GetRequiredService<IAssistantService>();
        var (categoryId, productId, _) = await SeedAsync(context, withFeature: false);
        var session = NewSession();
        try
        {
            var first = await assistant.ReplyAsync(Request("وزنش چقدره", session, current: productId));
            Assert.Equal(AssistantIntent.ProductQuestion, first.Intent);
            Assert.NotNull(first.Lookup);
            Assert.Equal(productId, first.Lookup.ProductId);
            Assert.Equal("وزن", first.Lookup.TopicTitle);
            Assert.Contains("لطفاً چند لحظه صبر کنید", first.Text);

            await assistant.ReplyAsync(Request("وزنش چقدره", session, current: productId));
            var task = await context.SmartTasks.AsNoTracking().FirstOrDefaultAsync(t => t.TargetId == productId && t.Kind == SmartTaskKind.ProductSpecs);
            Assert.NotNull(task);

            var insights = await assistant.InsightsAsync(1);
            Assert.Contains(insights.Gaps, g => g.ProductId == productId && g.Count >= 2 && g.Topic == "وزن");
            Assert.True(await assistant.FeedGapsAsync(50) >= 1);
            Assert.Equal(1, await context.SmartTasks.CountAsync(t => t.TargetId == productId && t.Kind == SmartTaskKind.ProductSpecs));

            // گارانتی از اینترنت پرسیده نمی‌شود.
            var warranty = await assistant.ReplyAsync(Request("گارانتی داره؟", session, current: productId));
            Assert.Null(warranty.Lookup);
        }
        finally
        {
            await CleanupAsync(context, categoryId, productId, session);
        }
    }

    [SqlFact]
    public async Task Cart_add_show_and_remove_work_through_the_assistant()
    {
        await using var scope = db.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
        var assistant = scope.ServiceProvider.GetRequiredService<IAssistantService>();
        var carts = scope.ServiceProvider.GetRequiredService<ICartService>();
        var (categoryId, productId, name) = await SeedAsync(context, stock: 5);
        var session = NewSession();
        var cartSession = "itcart-" + Guid.NewGuid().ToString("N");
        Guid? cartId = null;
        async Task<Guid?> Cart(bool create)
        {
            if (cartId is null && create)
                cartId = (await carts.GetOrCreateCartAsync(null, cartSession))?.Id;
            return cartId;
        }
        try
        {
            var empty = await assistant.ReplyAsync(Request("سبد خرید من", session, cart: Cart));
            Assert.Contains("خالی", empty.Text);
            Assert.Null(cartId);

            var add = await assistant.ReplyAsync(Request($"افزودن «{name}» به سبد", session, action: productId, cart: Cart));
            Assert.Equal(AssistantIntent.CartAdd, add.Intent);
            Assert.True(add.CartChanged, add.Text);
            Assert.NotNull(cartId);
            var items = await carts.GetItemsAsync(cartId.Value);
            Assert.Equal(1, Assert.Single(items, i => i.ProductId == productId).Quantity);

            var show = await assistant.ReplyAsync(Request("سبد خرید من", session, cart: Cart));
            Assert.Equal(AssistantIntent.CartShow, show.Intent);
            Assert.Contains(name, show.Text);

            var remove = await assistant.ReplyAsync(Request("حذفش کن", session, current: productId, cart: Cart));
            Assert.Equal(AssistantIntent.CartRemove, remove.Intent);
            Assert.True(remove.CartChanged, remove.Text);
            Assert.DoesNotContain(await carts.GetItemsAsync(cartId.Value), i => i.ProductId == productId);
        }
        finally
        {
            if (cartId is { } id)
            {
                await context.CartItems.IgnoreQueryFilters().Where(i => i.CartId == id).ExecuteDeleteAsync();
                await context.Carts.IgnoreQueryFilters().Where(c => c.Id == id).ExecuteDeleteAsync();
            }
            await CleanupAsync(context, categoryId, productId, session);
        }
    }

    [SqlFact]
    public async Task Out_of_stock_products_are_not_added_to_cart()
    {
        await using var scope = db.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
        var assistant = scope.ServiceProvider.GetRequiredService<IAssistantService>();
        var (categoryId, productId, _) = await SeedAsync(context, stock: 0);
        var session = NewSession();
        var called = false;
        try
        {
            var reply = await assistant.ReplyAsync(Request("اضافه کن", session, action: productId, cart: _ =>
            {
                called = true;
                return Task.FromResult<Guid?>(Guid.NewGuid());
            }));
            Assert.False(reply.CartChanged);
            Assert.Contains("ناموجود", reply.Text);
            Assert.False(called);
        }
        finally
        {
            await CleanupAsync(context, categoryId, productId, session);
        }
    }

    [SqlFact]
    public async Task Tracking_only_shows_the_users_own_orders()
    {
        await using var scope = db.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
        var assistant = scope.ServiceProvider.GetRequiredService<IAssistantService>();
        var session = NewSession();
        try
        {
            var guest = await assistant.ReplyAsync(Request("سفارشم کجاست", session));
            Assert.Contains(guest.Actions, a => a.Url?.StartsWith("/login", StringComparison.Ordinal) == true);

            var stranger = Guid.NewGuid();
            var none = await assistant.ReplyAsync(Request("سفارشم کجاست", session, userId: stranger));
            Assert.Contains("هنوز سفارشی", none.Text);

            var foreign = await assistant.ReplyAsync(Request("سفارش FR-20260101-1234 کجاست", session, userId: stranger));
            Assert.Equal(AssistantIntent.Track, foreign.Intent);
            Assert.Contains("پیدا نشد", foreign.Text);
        }
        finally
        {
            await context.AssistantMessages.Where(m => m.SessionKey == session).ExecuteDeleteAsync();
        }
    }

    [SqlFact]
    public async Task Missing_products_are_logged_as_demand_masked_and_pruned_later()
    {
        await using var scope = db.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
        var assistant = scope.ServiceProvider.GetRequiredService<IAssistantService>();
        var session = NewSession();
        var word = Word() + Word();
        try
        {
            var reply = await assistant.ReplyAsync(Request($"{word} میخوام شمارم 09121234567", session));
            Assert.Equal(AssistantIntent.Demand, reply.Intent);
            Assert.StartsWith(AssistantService.NotCarried, reply.Text, StringComparison.Ordinal);

            var row = await context.AssistantMessages.AsNoTracking().SingleAsync(m => m.SessionKey == session);
            Assert.DoesNotContain("09121234567", row.Text);
            Assert.DoesNotContain("09121234567", row.Query ?? "");
            Assert.Contains(word, row.Query);

            var insights = await assistant.InsightsAsync(1);
            Assert.Contains(insights.Demands, d => d.Query.Contains(word, StringComparison.Ordinal));
            Assert.True(insights.Total >= 1);

            await context.AssistantMessages.Where(m => m.SessionKey == session).ExecuteUpdateAsync(s => s.SetProperty(m => m.CreatedAt, DateTimeOffset.UtcNow.AddDays(-400)));
            context.ChangeTracker.Clear();
            Assert.True(await assistant.PruneAsync(180) >= 1);
            Assert.False(await context.AssistantMessages.AnyAsync(m => m.SessionKey == session));
        }
        finally
        {
            await context.AssistantMessages.IgnoreQueryFilters().Where(m => m.SessionKey == session).ExecuteDeleteAsync();
        }
    }
}
