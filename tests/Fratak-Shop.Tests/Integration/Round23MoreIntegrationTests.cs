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
        const string code = "TEACHER2705";
        // ۸ روز مانده به روز معلم ۱۴۰۶ (۱۲ اردیبهشت = ۲ مه ۲۰۲۷)؛ مناسبت دیگری در ۱۴ روز آینده نیست.
        // (یلدای ۲۰۲۶ مناسب نیست: روز پدر قمری ۲۲ دسامبر ۲۰۲۶ درست یک روز بعد از آن است.)
        var now = new DateTimeOffset(2027, 4, 24, 8, 0, 0, TimeSpan.Zero);
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
            await context.StaffNotifications.IgnoreQueryFilters().Where(n => n.Title.Contains("روز معلم") && n.Body!.Contains(code)).ExecuteDeleteAsync();
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

    [SqlFact]
    public async Task Old_post_with_gone_products_gets_a_real_in_stock_update_once()
    {
        await using var scope = db.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<IBlogRefreshService>();
        var key = Key();
        var category = new Category { Name = "هدفون آزمون " + key, Slug = "r23-ref-c-" + key };
        context.Categories.Add(category);
        Product Make(string name, bool active, int stock, int sales) => new()
        {
            CategoryId = category.Id,
            Name = name + " " + key,
            Slug = $"r23-ref-{name}-{key}",
            SKU = $"R23R-{name}-{key}",
            Price = 100_000,
            StockQuantity = stock,
            IsActive = active,
            PublishedAt = active ? DateTimeOffset.UtcNow.AddDays(-300) : null,
            SalesCount = sales,
        };
        var gone1 = Make("gone1", false, 0, 0);
        var gone2 = Make("gone2", true, 0, 9);
        var still = Make("still", true, 4, 1);
        var fresh = Make("fresh", true, 6, 50);
        var hidden = Make("hidden", false, 6, 99); // پرفروش ولی غیرفعال ← هرگز پیشنهاد نمی‌شود
        context.Products.AddRange(gone1, gone2, still, fresh, hidden);
        var blogCategory = new BlogCategory { Name = "بلاگ آزمون " + key, Slug = "r23-ref-b-" + key };
        context.BlogCategories.Add(blogCategory);
        BlogPost Post(params Product[] products) => new()
        {
            BlogCategory = blogCategory,
            Title = "راهنمای آزمون " + Key(),
            Slug = "r23-ref-p-" + Key(),
            Content = "<p>متن اصلی</p>" + string.Concat(products.Select(p => $"<p>[[product:{p.Slug}]]</p>")),
            IsPublished = true,
            PublishedAt = DateTimeOffset.UtcNow.AddDays(-400),
        };
        var stale = Post(gone1, gone2, still);      // ۲ از ۳ ناموجود ← به‌روزرسانی
        var healthy = Post(still, fresh, gone1);    // ۱ از ۳ ناموجود ← دست نمی‌خورد
        context.BlogPosts.AddRange(stale, healthy);
        await context.SaveChangesAsync();
        var productIds = new[] { gone1.Id, gone2.Id, still.Id, fresh.Id, hidden.Id };
        try
        {
            // قدیمی‌ترین ویرایش ← اول صف بررسی (مقاله‌های دیگر پایگاه داده دست نمی‌خورند چون حداکثر ۱ به‌روزرسانی).
            await context.BlogPosts.Where(p => p.Id == healthy.Id).ExecuteUpdateAsync(u => u.SetProperty(p => p.UpdatedAt, new DateTimeOffset(1999, 1, 1, 0, 0, 0, TimeSpan.Zero)));
            await context.BlogPosts.Where(p => p.Id == stale.Id).ExecuteUpdateAsync(u => u.SetProperty(p => p.UpdatedAt, new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero)));
            context.ChangeTracker.Clear();
            var now = DateTimeOffset.UtcNow;

            Assert.Equal(1, await service.RefreshAsync(now, 1));
            var content = await context.BlogPosts.Where(p => p.Id == stale.Id).Select(p => p.Content).SingleAsync();
            Assert.StartsWith("<p>متن اصلی</p>", content);
            Assert.Contains(BlogRefresh.Marker, content);
            Assert.Contains($"[[product:{fresh.Slug}]]", content);
            Assert.DoesNotContain(hidden.Slug, content);
            Assert.Contains(category.Name, content);
            var healthyContent = await context.BlogPosts.Where(p => p.Id == healthy.Id).Select(p => p.Content).SingleAsync();
            Assert.DoesNotContain(BlogRefresh.Marker, healthyContent);

            // تازه به‌روز شد ← تا ۹۰ روز دوباره دست نمی‌خورد (بخش تکراری نمی‌شود).
            await service.RefreshAsync(now.AddDays(1), 1);
            var again = await context.BlogPosts.Where(p => p.Id == stale.Id).Select(p => p.Content).SingleAsync();
            Assert.Equal(content, again);
            Assert.Equal(0, await service.RefreshAsync(now, 0));
        }
        finally
        {
            context.ChangeTracker.Clear();
            await context.BlogPosts.IgnoreQueryFilters().Where(p => p.Id == stale.Id || p.Id == healthy.Id).ExecuteDeleteAsync();
            await context.BlogCategories.IgnoreQueryFilters().Where(c => c.Id == blogCategory.Id).ExecuteDeleteAsync();
            await context.Products.IgnoreQueryFilters().Where(p => productIds.Contains(p.Id)).ExecuteDeleteAsync();
            await context.Categories.IgnoreQueryFilters().Where(c => c.Id == category.Id).ExecuteDeleteAsync();
        }
    }

    [SqlFact]
    public async Task Category_min_margin_is_inherited_from_the_parent_in_the_database()
    {
        await using var scope = db.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
        var key = Key();
        var parent = new Category { Name = "والد سود " + key, Slug = "r23-margin-p-" + key, MinMarginPercent = 30 };
        var child = new Category { Name = "فرزند سود " + key, Slug = "r23-margin-c-" + key, ParentCategory = parent };
        var plain = new Category { Name = "بی‌کف " + key, Slug = "r23-margin-x-" + key };
        context.Categories.AddRange(parent, child, plain);
        await context.SaveChangesAsync();
        try
        {
            context.ChangeTracker.Clear();
            Assert.Equal(30, await Fratak_Shop.Core.Services.Pricing.CategoryMargins.ForCategoryAsync(context, 5, child.Id));
            Assert.Equal(30, await Fratak_Shop.Core.Services.Pricing.CategoryMargins.ForCategoryAsync(context, 5, parent.Id));
            Assert.Equal(40, await Fratak_Shop.Core.Services.Pricing.CategoryMargins.ForCategoryAsync(context, 40, child.Id));
            Assert.Equal(5, await Fratak_Shop.Core.Services.Pricing.CategoryMargins.ForCategoryAsync(context, 5, plain.Id));
            Assert.Equal(5, await Fratak_Shop.Core.Services.Pricing.CategoryMargins.ForCategoryAsync(context, 5, null));
        }
        finally
        {
            context.ChangeTracker.Clear();
            await context.Categories.IgnoreQueryFilters().Where(c => c.Id == child.Id).ExecuteDeleteAsync();
            await context.Categories.IgnoreQueryFilters().Where(c => c.Id == parent.Id || c.Id == plain.Id).ExecuteDeleteAsync();
        }
    }
}
