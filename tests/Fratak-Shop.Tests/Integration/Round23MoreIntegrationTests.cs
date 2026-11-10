using Fratak_Shop.Core.Services.Blogs;
using Fratak_Shop.Core.Services.Marketing;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Blogs;
using Fratak_Shop.Data.Entities.Categories;
using Fratak_Shop.Data.Entities.Products;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Fratak_Shop.Tests.Integration;

/// <summary>
/// تست یکپارچهٔ تکمیلی دور ۲۳ روی SQL Server واقعی:
/// <list type="bullet">
/// <item>کوپن مناسبتی: غیرفعال ساخته می‌شود، دوباره ساخته نمی‌شود، و اگر مدیر حذفش کند (رد) هم برنمی‌گردد.</item>
/// <item>مطالب مرتبط: مقالهٔ هم‌موضوع می‌آید، مقالهٔ بی‌ربط و پیش‌نویس هرگز.</item>
/// <item>یکدست‌سازی «ي/ك» عربی هنگام ذخیرهٔ نام کالا.</item>
/// </list>
/// هر آزمون دادهٔ خودش را می‌سازد و در finally پاک می‌کند.
/// </summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", "Integration")]
[Trait("Area", "Round23")]
public class Round23MoreIntegrationTests(SqlServerFixture db)
{
    private static string Key() => Guid.NewGuid().ToString("N")[..10];

    [SqlFact]
    public async Task Occasion_coupon_is_created_inactive_once_and_never_after_rejection()
    {
        await using var scope = db.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<IOccasionCouponService>();
        const string code = "YALDA2612";
        // ۱۰ روز مانده به یلدای ۱۴۰۵ (۲۱ دسامبر ۲۰۲۶)؛ مناسبت دیگری در ۱۴ روز آینده نیست.
        var now = new DateTimeOffset(2026, 12, 11, 8, 0, 0, TimeSpan.Zero);
        async Task CleanAsync()
        {
            context.ChangeTracker.Clear();
            await context.Coupons.IgnoreQueryFilters().Where(c => c.Code == code).ExecuteDeleteAsync();
        }
        await CleanAsync();
        try
        {
            Assert.Equal(1, await service.PlanAsync(now));
            var coupon = await context.Coupons.AsNoTracking().SingleAsync(c => c.Code == code);
            Assert.False(coupon.IsActive);
            Assert.True(coupon.ExcludeDiscountedProducts);
            Assert.Equal(1, coupon.UsageLimitPerUser);

            Assert.Equal(0, await service.PlanAsync(now.AddHours(13)));

            // مدیر رد کرد (حذف نرم) ← دیگر ساخته نمی‌شود.
            var tracked = await context.Coupons.AsTracking().SingleAsync(c => c.Code == code);
            context.Coupons.Remove(tracked);
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();
            Assert.Equal(0, await service.PlanAsync(now.AddDays(1)));
            Assert.False(await context.Coupons.AnyAsync(c => c.Code == code));
        }
        finally
        {
            await CleanAsync();
            await context.StaffNotifications.IgnoreQueryFilters().Where(n => n.Title.Contains("شب یلدا") && n.Body!.Contains(code)).ExecuteDeleteAsync();
        }
    }

    [SqlFact]
    public async Task Related_posts_bring_same_topic_and_never_drafts_or_unrelated()
    {
        await using var scope = db.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
        var posts = scope.ServiceProvider.GetRequiredService<IPostService>();
        var key = Key();
        var word = "زرافه" + key[..4];  // واژهٔ یکتا تا مقاله‌های دیگر پایگاه داده قاطی نشوند
        var audio = new BlogCategory { Name = "صوتی " + key, Slug = "r23-rel-a-" + key };
        var kitchen = new BlogCategory { Name = "آشپزخانه " + key, Slug = "r23-rel-k-" + key };
        context.BlogCategories.AddRange(audio, kitchen);
        BlogPost Post(string title, BlogCategory category, bool published, int daysAgo) => new()
        {
            BlogCategory = category,
            Title = title,
            Slug = "r23-rel-" + Key(),
            Content = "<p>متن آزمون</p>",
            IsPublished = published,
            PublishedAt = published ? DateTimeOffset.UtcNow.AddDays(-daysAgo) : null,
        };
        var current = Post($"راهنمای خرید {word}", audio, true, 1);
        var sameTopic = Post($"بررسی {word} سونی", kitchen, true, 3);
        var draft = Post($"پیش‌نویس {word}", audio, false, 0);
        var unrelated = Post("راهنمای خرید کتری برقی " + key, kitchen, true, 2);
        context.BlogPosts.AddRange(current, sameTopic, draft, unrelated);
        await context.SaveChangesAsync();
        try
        {
            var related = await posts.GetRelatedPostsAsync(current.Id, 4);
            var slugs = related.Select(r => r.Slug).ToList();
            Assert.Contains(sameTopic.Slug, slugs);
            Assert.DoesNotContain(draft.Slug, slugs);
            Assert.DoesNotContain(unrelated.Slug, slugs);
            Assert.DoesNotContain(current.Slug, slugs);
            Assert.Empty(await posts.GetRelatedPostsAsync(Guid.NewGuid(), 4));
        }
        finally
        {
            context.ChangeTracker.Clear();
            var ids = new[] { current.Id, sameTopic.Id, draft.Id, unrelated.Id };
            await context.BlogPosts.IgnoreQueryFilters().Where(p => ids.Contains(p.Id)).ExecuteDeleteAsync();
            await context.BlogCategories.IgnoreQueryFilters().Where(c => c.Id == audio.Id || c.Id == kitchen.Id).ExecuteDeleteAsync();
        }
    }

    [SqlFact]
    public async Task Arabic_letters_are_normalized_when_a_product_is_saved()
    {
        await using var scope = db.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
        var key = Key();
        var category = new Category { Name = "دستهٔ حروف " + key, Slug = "r23-letters-" + key };
        context.Categories.Add(category);
        var product = new Product
        {
            CategoryId = category.Id,
            Name = "\u0643\u064A\u0641 \u0639\u0644\u064A " + key,
            Slug = "r23-letters-p-" + key,
            SKU = "R23L-" + key,
            Price = 10_000,
            StockQuantity = 1,
        };
        context.Products.Add(product);
        await context.SaveChangesAsync();
        try
        {
            context.ChangeTracker.Clear();
            var name = await context.Products.Where(p => p.Id == product.Id).Select(p => p.Name).SingleAsync();
            Assert.Equal("کیف علی " + key, name);
        }
        finally
        {
            context.ChangeTracker.Clear();
            await context.Products.IgnoreQueryFilters().Where(p => p.Id == product.Id).ExecuteDeleteAsync();
            await context.Categories.IgnoreQueryFilters().Where(c => c.Id == category.Id).ExecuteDeleteAsync();
        }
    }
}
