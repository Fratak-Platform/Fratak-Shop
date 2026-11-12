using Fratak_Shop.Core.Services.Categories;
using Fratak_Shop.Core.Services.Smart;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Categories;
using Fratak_Shop.Data.Entities.Products;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Fratak_Shop.Tests.Integration;

/// <summary>
/// تست یکپارچهٔ دور ۲۱ برای دسته‌بندی هوشمند روی SQL Server واقعی (<see cref="SmartCategoryService"/>).
/// <list type="bullet">
/// <item>دستهٔ سیستمی «دسته‌بندی نشده»: یک بار ساخته می‌شود، پنهان است و حذف نمی‌شود.</item>
/// <item>کالای مهم (پاوربانک) فوراً دستهٔ خودش را می‌گیرد؛ ۳ کالای مشابه دستهٔ تازه با نامک انگلیسی می‌سازند؛ کالای تک می‌ماند.</item>
/// <item>کالایی که ادمین در دستهٔ واقعی گذاشته هرگز جابه‌جا نمی‌شود.</item>
/// <item>دستهٔ ساخته‌شده تا کالای فعال ندارد پنهان است و با فعال شدن کالا خودکار فعال می‌شود.</item>
/// </list>
/// همهٔ آزمون‌ها فقط با شناسهٔ کالاهای خودشان کار می‌کنند تا دادهٔ آزمون‌های دیگر دست نخورد.
/// </summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", "Integration")]
[Trait("Area", "Smart")]
public class SmartCategoryIntegrationTests(SqlServerFixture db)
{
    private const string Letters = "ابپتثجچحخدذرزژسشصضطظعغفقکگلمنوهی";

    /// <summary>
    /// واژهٔ فارسی تصادفی (برای نام یکتای خوشه در هر اجرا). مثال: «زیرلیوانیپکمسدژ». حرف آخر همیشه «ژ» است تا ریشه‌یاب
    /// پسوندی («ها»، «ات» …) از آن حذف نکند.
    /// </summary>
    private static string Word(string prefix) =>
        prefix + new string(Enumerable.Range(0, 5).Select(_ => Letters[Random.Shared.Next(Letters.Length)]).ToArray()) + "ژ";

    private static async Task<Guid> AddProductAsync(FratakShopDbContext context, Guid categoryId, string name, bool active = false)
    {
        var key = Guid.NewGuid().ToString("N")[..10];
        var product = new Product
        {
            CategoryId = categoryId,
            Name = name,
            Slug = "smartcat-" + key,
            SKU = "SC-" + key,
            Price = 100_000,
            StockQuantity = 5,
            IsActive = active,
            PublishedAt = DateTimeOffset.UtcNow.AddDays(-1),
        };
        context.Products.Add(product);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        return product.Id;
    }

    [SqlFact]
    public async Task Uncategorized_bucket_is_created_once_hidden_and_undeletable()
    {
        await using var scope = db.Scope();
        var service = scope.ServiceProvider.GetRequiredService<ISmartCategoryService>();
        var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();

        var first = await service.EnsureUncategorizedAsync();
        var second = await service.EnsureUncategorizedAsync();
        Assert.Equal(first, second);
        var bucket = await context.Categories.SingleAsync(c => c.Id == first);
        Assert.True(bucket.IsSystem);
        Assert.False(bucket.IsActive);
        Assert.Equal(SmartCategoryService.UncategorizedSlug, bucket.Slug);
        Assert.Equal(1, await context.Categories.CountAsync(c => c.Slug == SmartCategoryService.UncategorizedSlug));

        var delete = await scope.ServiceProvider.GetRequiredService<ICategoryService>().DeleteCategoryAsync(first);
        Assert.Equal(EnOperationResultStatus.Error, delete.Status);
        Assert.True(await context.Categories.AnyAsync(c => c.Id == first));
    }

    [SqlFact]
    public async Task Imported_products_get_categories_and_new_ones_are_created_safely()
    {
        await using var scope = db.Scope();
        var service = scope.ServiceProvider.GetRequiredService<ISmartCategoryService>();
        var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
        var bucket = await service.EnsureUncategorizedAsync();

        // همهٔ واژه‌ها تصادفی‌اند تا دسته‌بند آماری با دادهٔ آزمون‌های دیگر «مطمئن» نشود و خوشه‌بندی واقعاً آزموده شود.
        var head = Word("زیرلیوانی");
        var second = Word("جنس");
        var cluster = new List<Guid>
        {
            await AddProductAsync(context, bucket, $"{head} {second} {Word("ط")}"),
            await AddProductAsync(context, bucket, $"{head} {second} {Word("ط")}"),
            await AddProductAsync(context, bucket, $"{head} {second} {Word("ط")}"),
        };
        var powerBank = await AddProductAsync(context, bucket, "پاوربانک آزمایشی " + Guid.NewGuid().ToString("N")[..6]);
        var lone = await AddProductAsync(context, bucket, Word("گلدان") + " شیشه‌ای");

        // کالایی که ادمین خودش در دستهٔ واقعی گذاشته (حتی اگر نامش شبیه باشد) نباید جابه‌جا شود.
        var manualCategory = new Category { Name = "دستهٔ دستی " + Guid.NewGuid().ToString("N")[..6], Slug = "manual-" + Guid.NewGuid().ToString("N")[..8], IsActive = true };
        context.Categories.Add(manualCategory);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var manual = await AddProductAsync(context, manualCategory.Id, "میز تحریر آزمایشی");

        var ids = cluster.Append(powerBank).Append(lone).Append(manual).ToList();
        var report = await service.RunAsync(ids, 50);

        Assert.Equal(5, report.Examined); // کالای دستهٔ دستی اصلاً بررسی نمی‌شود
        var rows = await context.Products.Where(p => ids.Contains(p.Id)).Select(p => new { p.Id, p.CategoryId }).ToListAsync();
        var categoryOf = rows.ToDictionary(r => r.Id, r => r.CategoryId);

        // خوشهٔ ۳تایی ← یک دستهٔ مشترک (تازه یا هم‌نام قبلی) با نام دو واژه‌ای و نامک انگلیسی.
        var clusterCategory = Assert.Single(cluster.Select(id => categoryOf[id]).Distinct());
        Assert.NotEqual(bucket, clusterCategory);
        var created = await context.Categories.SingleAsync(c => c.Id == clusterCategory);
        Assert.Equal(head + " " + second, created.Name);
        Assert.True(Fratak_Shop.Data.Utilities.EnglishSlug.IsEnglish(created.Slug), created.Slug);
        Assert.True(created.IsAutoCreated);
        Assert.False(created.IsActive); // کالاهایش هنوز پیش‌نویس‌اند ← در منوی سایت نمی‌آید
        Assert.Contains(report.Created, c => c.Id == clusterCategory && c.Products == 3);

        // پاوربانک (نوع مهم) ← دستهٔ پاوربانک (موجود یا تازه).
        var bankCategory = await context.Categories.SingleAsync(c => c.Id == categoryOf[powerBank]);
        Assert.True(ProductTypes.NameMatches(ProductTypes.Find("power-bank")!, bankCategory.Name, bankCategory.Slug), bankCategory.Name);

        // کالای تک ← فعلاً در «دسته‌بندی نشده».
        Assert.Equal(bucket, categoryOf[lone]);
        Assert.True(report.Waiting >= 1);
        Assert.Equal(manualCategory.Id, categoryOf[manual]);
        Assert.False(string.IsNullOrWhiteSpace(report.Summary));

        // فعال شدن یکی از کالاها ← دور بعد دستهٔ خودکار هم فعال می‌شود.
        await context.Products.Where(p => p.Id == cluster[0]).ExecuteUpdateAsync(s => s.SetProperty(p => p.IsActive, true));
        var again = await service.RunAsync(ids, 50);
        Assert.Equal(1, again.Examined); // فقط کالای تک باقی مانده
        Assert.True(await context.Categories.Where(c => c.Id == clusterCategory).Select(c => c.IsActive).SingleAsync());

        // اجرای دوباره چیزی را خراب نمی‌کند و دستهٔ تکراری نمی‌سازد.
        Assert.Equal(1, await context.Categories.CountAsync(c => c.Name == head + " " + second));
    }

    [SqlFact]
    public async Task Nothing_to_do_returns_an_empty_report()
    {
        await using var scope = db.Scope();
        var service = scope.ServiceProvider.GetRequiredService<ISmartCategoryService>();
        var report = await service.RunAsync([Guid.NewGuid()], 10);
        Assert.Equal(0, report.Examined);
        Assert.Empty(report.Created);
    }
}
