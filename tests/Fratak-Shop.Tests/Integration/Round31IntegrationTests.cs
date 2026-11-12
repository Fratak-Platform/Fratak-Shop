using Fratak_Shop.Core.Services.Seo.Engine;
using Fratak_Shop.Core.Services.Smart;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Categories;
using Fratak_Shop.Data.Entities.Products;
using Fratak_Shop.Data.Entities.SeoEngine;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Fratak_Shop.Tests.Integration;

/// <summary>
/// دور ۳۱ روی SQL Server واقعی:
/// <list type="bullet">
/// <item>#۱ قانون پروژه: کالای بی‌دسته ← صفحهٔ تأمین‌کننده خوانده می‌شود ← مسیر دسته ذخیره و کالا دسته‌بندی می‌شود.
/// لینک خطرناک هرگز باز نمی‌شود، کالای دستی دست نمی‌خورد، هر کالا تا ۱۴ روز دوباره خوانده نمی‌شود و خطای خواننده کار را خراب نمی‌کند.
/// (خواننده در آزمون جعلی است؛ هیچ درخواست اینترنتی زده نمی‌شود.)</item>
/// <item>#۱۳ «حذف همهٔ پیشنهادهای منتظر»: فقط منتظرها حذف می‌شوند؛ اعمال‌شده‌ها و محتوای صفحه می‌ماند.</item>
/// </list>
/// </summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", "Integration")]
[Trait("Area", "Round31")]
public class Round31IntegrationTests(SqlServerFixture db)
{
    private static async Task<Guid> AddProductAsync(FratakShopDbContext context, Guid categoryId, string name, string? supplierUrl)
    {
        var key = Guid.NewGuid().ToString("N")[..10];
        var product = new Product
        {
            CategoryId = categoryId,
            Name = name,
            Slug = "r31-" + key,
            SKU = "R31-" + key,
            Price = 120_000,
            StockQuantity = 3,
            IsActive = false,
            SupplierUrl = supplierUrl,
            PublishedAt = DateTimeOffset.UtcNow.AddDays(-1),
        };
        context.Products.Add(product);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        return product.Id;
    }

    [SqlFact]
    public async Task Uncategorized_products_get_their_category_from_the_supplier_page()
    {
        await using var scope = db.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<ISupplierCategoryService>();
        var bucket = await scope.ServiceProvider.GetRequiredService<ISmartCategoryService>().EnsureUncategorizedAsync();

        var manualCategory = new Category { Name = "دستهٔ دستی " + Guid.NewGuid().ToString("N")[..6], Slug = "r31-manual-" + Guid.NewGuid().ToString("N")[..8], IsActive = true };
        context.Categories.Add(manualCategory);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var kettle = await AddProductAsync(context, bucket, "کتری برقی پارس خزر مدل KT-2000", "https://supplier.example.ir/p/kettle-" + Guid.NewGuid().ToString("N")[..6]);
        var evil = await AddProductAsync(context, bucket, "کالای لینک خراب", "javascript:alert(1)");
        var broken = await AddProductAsync(context, bucket, "کالای صفحهٔ خراب", "https://supplier.example.ir/p/broken");
        var manual = await AddProductAsync(context, manualCategory.Id, "میز تحریر دستی", "https://supplier.example.ir/p/desk");
        var ids = new[] { kettle, evil, broken, manual };

        var calls = new List<string>();
        Task<IReadOnlyList<string>?> Reader(string url, string name, CancellationToken _)
        {
            calls.Add(url);
            if (url.EndsWith("/broken", StringComparison.Ordinal))
                throw new HttpRequestException("supplier down"); // خطای شبکه نباید کار را خراب کند
            return Task.FromResult<IReadOnlyList<string>?>(["لوازم خانگی", "لوازم آشپزخانه", "کتری برقی"]);
        }

        try
        {
            var now = DateTimeOffset.UtcNow;
            var report = await service.RunAsync(now, Reader, ids, take: 20, force: false);

            Assert.Equal(3, report.Examined);           // کالای دستی نامزد نیست
            Assert.Equal(1, report.Found);
            Assert.True(report.Moved >= 1, report.Summary);
            Assert.DoesNotContain(calls, c => c.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(calls, c => c.EndsWith("/desk", StringComparison.Ordinal));
            Assert.Contains(evil, report.Unplaced);
            Assert.Contains(broken, report.Unplaced);

            context.ChangeTracker.Clear();
            var rows = await context.Products.AsNoTracking().Where(p => ids.Contains(p.Id))
                .Select(p => new { p.Id, p.CategoryId, p.SupplierCategory, p.SupplierCategoryAt, p.SourceCategory }).ToListAsync();
            var placed = rows.Single(r => r.Id == kettle);
            Assert.NotEqual(bucket, placed.CategoryId);
            Assert.Equal("لوازم خانگی > لوازم آشپزخانه > کتری برقی", placed.SupplierCategory);
            Assert.Equal(placed.SupplierCategory, placed.SourceCategory);
            Assert.NotNull(placed.SupplierCategoryAt);
            // لینک خراب و صفحهٔ خراب هم «خوانده شد» می‌گیرند تا هر ساعت دوباره امتحان نشوند؛ در «بی‌دسته» می‌مانند.
            Assert.All(rows.Where(r => r.Id == evil || r.Id == broken), r =>
            {
                Assert.Equal(bucket, r.CategoryId);
                Assert.NotNull(r.SupplierCategoryAt);
                Assert.Null(r.SupplierCategory);
            });
            Assert.Equal(manualCategory.Id, rows.Single(r => r.Id == manual).CategoryId);
            Assert.Null(rows.Single(r => r.Id == manual).SupplierCategoryAt);

            // تا ۱۴ روز دوباره خوانده نمی‌شود؛ با force (دکمهٔ دستی) خوانده می‌شود.
            calls.Clear();
            var again = await service.RunAsync(now.AddHours(1), Reader, ids, take: 20);
            Assert.Equal(0, again.Examined);
            Assert.Empty(calls);
            var forced = await service.RunAsync(now.AddHours(1), Reader, [broken], take: 20, force: true);
            Assert.Equal(1, forced.Examined);
            Assert.Single(calls);

            // بعد از ۱۴ روز دوباره نامزد است.
            calls.Clear();
            var later = await service.RunAsync(now + SupplierCategoryService.RecheckAfter + TimeSpan.FromHours(2), Reader, [evil, broken], take: 20);
            Assert.Equal(2, later.Examined);
        }
        finally
        {
            // پاک‌سازی بهترین‌تلاش: اگر جدول دیگری به این کالاها اشاره کرده باشد، کالاهای غیرفعال آزمایشی می‌مانند (بی‌ضرر)
            // و خطای پاک‌سازی نتیجهٔ آزمون را پنهان نمی‌کند.
            context.ChangeTracker.Clear();
            try
            {
                await context.ProductPriceHistories.IgnoreQueryFilters().Where(h => ids.Contains(h.ProductId)).ExecuteDeleteAsync();
                await context.Products.IgnoreQueryFilters().Where(p => ids.Contains(p.Id)).ExecuteDeleteAsync();
                await context.Categories.IgnoreQueryFilters().Where(c => c.Id == manualCategory.Id).ExecuteDeleteAsync();
            }
            catch (Exception exception) when (exception is DbUpdateException or Microsoft.Data.SqlClient.SqlException or InvalidOperationException)
            {
                // عمداً نادیده گرفته می‌شود.
            }
        }
    }

    [SqlFact]
    public async Task Supplier_pass_with_no_candidates_or_empty_filter_does_nothing()
    {
        await using var scope = db.Scope();
        var service = scope.ServiceProvider.GetRequiredService<ISupplierCategoryService>();
        var called = false;
        Task<IReadOnlyList<string>?> Reader(string url, string name, CancellationToken _)
        {
            called = true;
            return Task.FromResult<IReadOnlyList<string>?>(null);
        }

        var empty = await service.RunAsync(DateTimeOffset.UtcNow, Reader, Array.Empty<Guid>());
        var unknown = await service.RunAsync(DateTimeOffset.UtcNow, Reader, [Guid.NewGuid()]);

        Assert.Equal(0, empty.Examined);
        Assert.Equal(0, unknown.Examined);
        Assert.False(called);
        await Assert.ThrowsAsync<ArgumentNullException>(() => service.RunAsync(DateTimeOffset.UtcNow, null!));
    }

    [SqlFact]
    public async Task Delete_all_pending_removes_only_pending_suggestions()
    {
        var applyProduct = await db.CreateProductAsync();
        var pendingProduct = await db.CreateProductAsync();
        Guid applied;
        Guid pending;
        await using (var scope = db.Scope())
        {
            var engine = scope.ServiceProvider.GetRequiredService<ISeoEngineService>();
            var first = await engine.GenerateAsync(SeoEntityKind.Product, applyProduct);
            var second = await engine.GenerateAsync(SeoEntityKind.Product, pendingProduct);
            Assert.True(first.Status == EnOperationResultStatus.Success, first.Message);
            Assert.True(second.Status == EnOperationResultStatus.Success, second.Message);
            applied = first.Data;
            pending = second.Data;
            var approve = await engine.ApproveAsync(applied, SeoFields.MetaDescription, null, "تست");
            Assert.Equal(EnOperationResultStatus.Success, approve.Status);
        }

        await using (var scope = db.Scope())
        {
            var engine = scope.ServiceProvider.GetRequiredService<ISeoEngineService>();
            var bad = await engine.DeletePendingAsync(SeoEntityKind.None, null, "تست");
            Assert.Equal(EnOperationResultStatus.Error, bad.Status);
            var badEnum = await engine.DeletePendingAsync((SeoEntityKind)200, null, "تست");
            Assert.Equal(EnOperationResultStatus.Error, badEnum.Status);

            var result = await engine.DeletePendingAsync(SeoEntityKind.Product, null, "تست");
            Assert.Equal(EnOperationResultStatus.Success, result.Status);
            Assert.True(result.Data >= 1);
        }

        await using (var scope = db.Scope())
        {
            var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
            Assert.False(await context.SeoSuggestions.IgnoreQueryFilters().AnyAsync(s => s.Id == pending));
            Assert.False(await context.SeoSuggestions.AnyAsync(s => s.Kind == SeoEntityKind.Product && s.Status == SeoSuggestionStatus.Pending));
            var kept = await context.SeoSuggestions.AsNoTracking().SingleAsync(s => s.Id == applied);
            Assert.Equal(SeoSuggestionStatus.Applied, kept.Status);
            // محتوای اعمال‌شده روی خود صفحه دست نخورده است.
            var product = await context.Products.AsNoTracking().SingleAsync(p => p.Id == applyProduct);
            Assert.False(string.IsNullOrWhiteSpace(product.Seo?.MetaDescription));

            // دوباره زدن دکمه: چیزی برای حذف نیست ولی خطا هم نیست.
            var engine = scope.ServiceProvider.GetRequiredService<ISeoEngineService>();
            var none = await engine.DeletePendingAsync(SeoEntityKind.Product, null, "تست");
            Assert.Equal(EnOperationResultStatus.Success, none.Status);
            Assert.Equal(0, none.Data);

            // پیشنهاد تازه برای همان صفحه دوباره ساخته می‌شود.
            var fresh = await engine.GenerateAsync(SeoEntityKind.Product, pendingProduct);
            Assert.True(fresh.Status == EnOperationResultStatus.Success, fresh.Message);
        }
    }
}
