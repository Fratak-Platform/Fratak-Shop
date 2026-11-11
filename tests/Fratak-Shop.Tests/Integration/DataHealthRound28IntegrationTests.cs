using Fratak_Shop.Core.Services.Smart;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Categories;
using Fratak_Shop.Data.Entities.Products;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Fratak_Shop.Tests.Integration;

/// <summary>
/// دور ۲۸ (#۵۱–۵۹) – «سلامت داده» روی SQL Server واقعی:
/// گزارش ایرادها و عکس‌های مشکوک را پیدا می‌کند؛ درست کردن خودکار فقط پیش‌نویس‌ها را عوض می‌کند (کالای منتشرشده دست نمی‌خورد)؛
/// بررسی دوبارهٔ عکس امتیاز را پاک می‌کند؛ آماده‌سازی دوباره فقط برای پیش‌نویس است. پاک‌سازی در finally.
/// </summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", "Integration")]
[Trait("Area", "Round28")]
public class DataHealthRound28IntegrationTests(SqlServerFixture db)
{
    private static string Key() => Guid.NewGuid().ToString("N")[..10];

    private static async Task<Guid> AddCategoryAsync(FratakShopDbContext context)
    {
        var category = new Category { Name = "دسته سلامت " + Key(), Slug = "r28-dh-" + Key(), IsActive = true };
        context.Categories.Add(category);
        await context.SaveChangesAsync();
        return category.Id;
    }

    private static async Task<Guid> AddProductAsync(FratakShopDbContext context, Guid categoryId, bool published, string? english, decimal? discount, int stock,
        byte? imageScore = 80, byte? imageFlags = 3)
    {
        var key = Key();
        var product = new Product
        {
            CategoryId = categoryId,
            Name = "کالای سلامت داده " + key,
            EnglishName = english,
            Slug = "r28-dh-" + key,
            SKU = "R28DH-" + key,
            Price = 500_000,
            DiscountPrice = discount,
            StockQuantity = stock,
            IsActive = published,
            PublishedAt = published ? DateTimeOffset.UtcNow.AddDays(-1) : null,
            SmartReadyAttempts = 5,
            SmartReadyCheckedAt = DateTimeOffset.UtcNow,
            Images = [new ProductImage { ImageName = "r28-dh-" + key + ".webp", IsPrimary = true, SubjectScore = imageScore, SubjectFlags = imageFlags }],
        };
        context.Products.Add(product);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        return product.Id;
    }

    private static async Task CleanupAsync(FratakShopDbContext context, Guid categoryId)
    {
        context.ChangeTracker.Clear();
        await context.ProductPriceHistories.IgnoreQueryFilters().Where(h => h.Product.CategoryId == categoryId).ExecuteDeleteAsync();
        await context.ProductImages.IgnoreQueryFilters().Where(i => i.Product.CategoryId == categoryId).ExecuteDeleteAsync();
        await context.Products.IgnoreQueryFilters().Where(p => p.CategoryId == categoryId).ExecuteDeleteAsync();
        await context.Categories.IgnoreQueryFilters().Where(c => c.Id == categoryId).ExecuteDeleteAsync();
    }

    [SqlFact]
    public async Task Auto_fix_repairs_drafts_only_and_requeues_them()
    {
        await using var scope = db.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
        var health = scope.ServiceProvider.GetRequiredService<IDataHealthService>();
        var categoryId = await AddCategoryAsync(context);
        try
        {
            var draft = await AddProductAsync(context, categoryId, published: false, english: "پاوربانک Anker", discount: 600_000, stock: 0);
            var live = await AddProductAsync(context, categoryId, published: true, english: "پاوربانک Anker", discount: 600_000, stock: 0);
            var healthyDraft = await AddProductAsync(context, categoryId, published: false, english: "Anker Power Bank", discount: 450_000, stock: 2);

            Assert.True(await health.FixAsync() >= 1);
            context.ChangeTracker.Clear();

            var rows = await context.Products.Where(p => p.CategoryId == categoryId)
                .Select(p => new { p.Id, p.EnglishName, p.DiscountPrice, p.StockQuantity, p.SmartReadyAttempts }).ToListAsync();
            var d = rows.Single(r => r.Id == draft);
            Assert.Null(d.EnglishName);
            Assert.Null(d.DiscountPrice);
            Assert.Equal(0, d.SmartReadyAttempts);

            // کالای منتشرشده بی‌خبر عوض نمی‌شود.
            var l = rows.Single(r => r.Id == live);
            Assert.Equal("پاوربانک Anker", l.EnglishName);
            Assert.Equal(600_000m, l.DiscountPrice);

            // کالای سالم دست نمی‌خورد.
            var h = rows.Single(r => r.Id == healthyDraft);
            Assert.Equal("Anker Power Bank", h.EnglishName);
            Assert.Equal(450_000m, h.DiscountPrice);
            Assert.Equal(5, h.SmartReadyAttempts);
        }
        finally
        {
            await CleanupAsync(context, categoryId);
        }
    }

    [SqlFact]
    public async Task Report_lists_issues_and_suspicious_images()
    {
        await using var scope = db.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
        var health = scope.ServiceProvider.GetRequiredService<IDataHealthService>();
        var categoryId = await AddCategoryAsync(context);
        try
        {
            var people = await AddProductAsync(context, categoryId, published: true, english: "Test Item", discount: 700_000, stock: 1, imageScore: 30, imageFlags: 4);

            var report = await health.ReportAsync("discount-invalid");
            Assert.True(report.Products >= 1);
            Assert.Contains(report.Issues, i => i.Code == "discount-invalid" && i.Count >= 1);
            var row = Assert.Single(report.Worst, w => w.Id == people);
            Assert.Contains(row.Result.Issues, i => i.Code == "image-people");
            Assert.All(report.Worst, w => Assert.Contains(w.Result.Issues, i => i.Code == "discount-invalid"));
            Assert.Contains(report.BadImages, i => i.ProductId == people && (i.Flags & 4) != 0);
        }
        finally
        {
            await CleanupAsync(context, categoryId);
        }
    }

    [SqlFact]
    public async Task Recheck_clears_scores_and_reprepare_accepts_drafts_only()
    {
        await using var scope = db.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
        var health = scope.ServiceProvider.GetRequiredService<IDataHealthService>();
        var categoryId = await AddCategoryAsync(context);
        try
        {
            var draft = await AddProductAsync(context, categoryId, published: false, english: "Test Item", discount: null, stock: 1);
            var live = await AddProductAsync(context, categoryId, published: true, english: "Test Item", discount: null, stock: 1);

            Assert.Equal(1, await health.RecheckImagesAsync(draft));
            context.ChangeTracker.Clear();
            var image = await context.ProductImages.Where(i => i.ProductId == draft).Select(i => new { i.SubjectScore, i.SubjectFlags }).SingleAsync();
            Assert.Null(image.SubjectScore);
            Assert.Null(image.SubjectFlags);
            Assert.NotNull(await context.ProductImages.Where(i => i.ProductId == live).Select(i => i.SubjectScore).SingleAsync());

            Assert.True((await health.ReprepareAsync(draft)).IsSuccess());
            context.ChangeTracker.Clear();
            var attempts = await context.Products.Where(p => p.Id == draft).Select(p => new { p.SmartReadyAttempts, p.SmartReadyCheckedAt }).SingleAsync();
            Assert.Equal(0, attempts.SmartReadyAttempts);
            Assert.Null(attempts.SmartReadyCheckedAt);

            Assert.False((await health.ReprepareAsync(live)).IsSuccess());
            Assert.Equal(EnOperationResultStatus.NotFound, (await health.ReprepareAsync(Guid.NewGuid())).Status);
        }
        finally
        {
            await CleanupAsync(context, categoryId);
        }
    }
}
