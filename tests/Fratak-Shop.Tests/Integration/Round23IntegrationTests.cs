using Fratak_Shop.Core.Services.Categories;
using Fratak_Shop.Core.Services.Marketing;
using Fratak_Shop.Core.Services.Smart;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Blogs;
using Fratak_Shop.Data.Entities.Categories;
using Fratak_Shop.Data.Entities.Marketing;
using Fratak_Shop.Data.Entities.Products;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Fratak_Shop.Tests.Integration;

/// <summary>
/// تست یکپارچهٔ دور ۲۳ روی SQL Server واقعی:
/// <list type="bullet">
/// <item>ترمیم متن: جملهٔ «دسته‌بندی نشده» از توضیح کالا حذف و لینک به صفحهٔ ناموجود در مقاله باز می‌شود (متن می‌ماند).</item>
/// <item>آمار بنر: افزایش اتمی نمایش/کلیک؛ بنر ناموجود و مقدار صفر نادیده.</item>
/// <item>بنر کالای ناموجود پنهان می‌شود و با موجود شدن دوباره برمی‌گردد (حذف نمی‌شود).</item>
/// <item>آمار واقعی دسته (برای پرسش‌های متداول): کالای غیرفعال حساب نمی‌شود، زیردسته حساب می‌شود.</item>
/// </list>
/// هر آزمون دادهٔ خودش را می‌سازد و در finally پاک می‌کند.
/// </summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", "Integration")]
[Trait("Area", "Round23")]
public class Round23IntegrationTests(SqlServerFixture db)
{
    private static string Key() => Guid.NewGuid().ToString("N")[..10];

    private static Product NewProduct(Guid categoryId, string key, decimal price, decimal? discount = null, int stock = 5, bool active = true, string? description = null) => new()
    {
        CategoryId = categoryId,
        Name = "کالای آزمون " + key,
        Slug = "r23-" + key,
        SKU = "R23-" + key,
        Price = price,
        DiscountPrice = discount,
        StockQuantity = stock,
        IsActive = active,
        PublishedAt = active ? DateTimeOffset.UtcNow.AddDays(-1) : null,
        Description = description,
    };

    private static async Task CleanupAsync(FratakShopDbContext context, IEnumerable<Guid> categories, IEnumerable<Guid> products)
    {
        context.ChangeTracker.Clear();
        var productIds = products.ToList();
        var categoryIds = categories.ToList();
        await context.Banners.IgnoreQueryFilters().Where(b => b.ProductId != null && productIds.Contains(b.ProductId.Value)).ExecuteDeleteAsync();
        await context.Banners.IgnoreQueryFilters().Where(b => b.CategoryId != null && categoryIds.Contains(b.CategoryId.Value)).ExecuteDeleteAsync();
        await context.Products.IgnoreQueryFilters().Where(p => productIds.Contains(p.Id)).ExecuteDeleteAsync();
        await context.Categories.IgnoreQueryFilters().Where(c => c.ParentId != null && categoryIds.Contains(c.ParentId.Value)).ExecuteDeleteAsync();
        await context.Categories.IgnoreQueryFilters().Where(c => categoryIds.Contains(c.Id)).ExecuteDeleteAsync();
    }

    [SqlFact]
    public async Task Forbidden_text_is_repaired_without_losing_good_content()
    {
        await using var scope = db.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
        var catalog = scope.ServiceProvider.GetRequiredService<ISmartCatalogService>();
        var key = Key();
        var category = new Category { Name = "دستهٔ ترمیم " + key, Slug = "r23-fix-" + key };
        context.Categories.Add(category);
        var product = NewProduct(category.Id, key, 100_000, description: "<p>این هدفون سبک و خوش‌صداست.</p><p>کالاهای مشابه را در دسته‌بندی نشده ببینید.</p>");
        context.Products.Add(product);
        var blogCategory = new BlogCategory { Name = "وبلاگ ترمیم " + key, Slug = "r23-blog-" + key };
        context.BlogCategories.Add(blogCategory);
        var post = new BlogPost
        {
            BlogCategory = blogCategory,
            Title = "مقالهٔ ترمیم " + key,
            Slug = "r23-post-" + key,
            Content = "<p>برای خرید <a href=\"/category/uncategorized\">این کالاها</a> عجله نکنید.</p>",
            IsPublished = true,
            PublishedAt = DateTimeOffset.UtcNow.AddDays(-1),
        };
        context.BlogPosts.Add(post);
        await context.SaveChangesAsync();
        try
        {
            var result = await catalog.RepairForbiddenTextAsync(500);
            Assert.Contains(product.Id, result.Products);
            Assert.True(result.Posts >= 1);

            context.ChangeTracker.Clear();
            var description = await context.Products.Where(p => p.Id == product.Id).Select(p => p.Description).FirstAsync();
            Assert.Equal("<p>این هدفون سبک و خوش‌صداست.</p>", description);
            var content = await context.BlogPosts.Where(p => p.Id == post.Id).Select(p => p.Content).FirstAsync();
            Assert.Contains("این کالاها", content);
            Assert.DoesNotContain("uncategorized", content);

            // دور دوم چیزی برای ترمیم ندارد (ثابت می‌ماند).
            var again = await catalog.RepairForbiddenTextAsync(500);
            Assert.DoesNotContain(product.Id, again.Products);
        }
        finally
        {
            await context.BlogPosts.IgnoreQueryFilters().Where(p => p.Id == post.Id).ExecuteDeleteAsync();
            await context.BlogCategories.IgnoreQueryFilters().Where(c => c.Id == blogCategory.Id).ExecuteDeleteAsync();
            await CleanupAsync(context, [category.Id], [product.Id]);
        }
    }

    [SqlFact]
    public async Task Banner_stats_are_added_atomically()
    {
        await using var scope = db.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
        var banners = scope.ServiceProvider.GetRequiredService<IBannerService>();
        var key = Key();
        var category = new Category { Name = "دستهٔ بنر " + key, Slug = "r23-bn-" + key };
        context.Categories.Add(category);
        var banner = new Banner { Title = "آزمون", ImageName = "r23.webp", CategoryId = category.Id, Position = BannerPosition.CategoryHeader, EndAt = DateTimeOffset.UtcNow.AddDays(-1) };
        context.Banners.Add(banner);
        await context.SaveChangesAsync();
        try
        {
            var updated = await banners.AddStatsAsync(new Dictionary<Guid, (int Views, int Clicks)>
            {
                [banner.Id] = (5, 2),
                [Guid.NewGuid()] = (9, 9),
            });
            Assert.Equal(1, updated);
            Assert.Equal(0, await banners.AddStatsAsync(new Dictionary<Guid, (int Views, int Clicks)> { [banner.Id] = (0, 0) }));
            await banners.AddStatsAsync(new Dictionary<Guid, (int Views, int Clicks)> { [banner.Id] = (3, -4) });

            context.ChangeTracker.Clear();
            var row = await context.Banners.Where(b => b.Id == banner.Id).Select(b => new { b.Views, b.Clicks }).FirstAsync();
            Assert.Equal(8, row.Views);
            Assert.Equal(2, row.Clicks);
        }
        finally
        {
            await CleanupAsync(context, [category.Id], []);
        }
    }

    [SqlFact]
    public async Task Banner_of_out_of_stock_product_hides_and_returns()
    {
        await using var scope = db.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
        var banners = scope.ServiceProvider.GetRequiredService<IBannerService>();
        var key = Key();
        var category = new Category { Name = "دستهٔ سربرگ " + key, Slug = "r23-hd-" + key };
        context.Categories.Add(category);
        var product = NewProduct(category.Id, key, 100_000, stock: 0);
        context.Products.Add(product);
        context.Banners.Add(new Banner { Title = "سربرگ", ImageName = "r23.webp", CategoryId = category.Id, ProductId = product.Id, Position = BannerPosition.CategoryHeader });
        await context.SaveChangesAsync();
        try
        {
            Assert.Null(await banners.GetCategoryHeaderAsync(category.Id));

            await context.Products.Where(p => p.Id == product.Id).ExecuteUpdateAsync(s => s.SetProperty(p => p.StockQuantity, 3));
            context.ChangeTracker.Clear();
            var header = await banners.GetCategoryHeaderAsync(category.Id);
            Assert.NotNull(header);
            Assert.Equal(product.Id, header.ProductId);
        }
        finally
        {
            await CleanupAsync(context, [category.Id], [product.Id]);
        }
    }

    [SqlFact]
    public async Task Category_facts_count_only_active_products_including_subcategories()
    {
        await using var scope = db.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
        var categories = scope.ServiceProvider.GetRequiredService<ICategoryService>();
        var key = Key();
        var root = new Category { Name = "دستهٔ آمار " + key, Slug = "r23-fx-" + key };
        context.Categories.Add(root);
        var sub = new Category { Name = "زیردستهٔ آمار " + key, Slug = "r23-fxs-" + key, ParentId = root.Id };
        context.Categories.Add(sub);
        var products = new[]
        {
            NewProduct(root.Id, key + "a", 200_000),
            NewProduct(root.Id, key + "b", 500_000, discount: 400_000),
            NewProduct(root.Id, key + "c", 900_000, stock: 0),
            NewProduct(root.Id, key + "d", 50_000, active: false),
            NewProduct(sub.Id, key + "e", 150_000, discount: 300_000), // تخفیف نامعتبر (بیشتر از قیمت) ← حساب نمی‌شود
        };
        context.Products.AddRange(products);
        await context.SaveChangesAsync();
        try
        {
            var facts = await categories.GetCategoryFactsAsync(root.Id);
            Assert.Equal(4, facts.Count);
            Assert.Equal(3, facts.InStock);
            Assert.Equal(150_000m, facts.MinPrice);
            Assert.Equal(400_000m, facts.MaxPrice);
            Assert.Equal(1, facts.Discounted);

            var questions = CategoryFaq.Build(root.Name, facts, "فراتک");
            Assert.True(questions.Count >= 3);
            Assert.All(questions, q => Assert.True(ContentQualityGate.IsClean(q.Answer), q.Answer));

            Assert.Same(CategoryFacts.Empty, await categories.GetCategoryFactsAsync(Guid.NewGuid()));
        }
        finally
        {
            await CleanupAsync(context, [root.Id, sub.Id], products.Select(p => p.Id));
        }
    }
}
