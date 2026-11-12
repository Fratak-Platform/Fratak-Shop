using Fratak_Shop.Core.Services.Smart;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Categories;
using Fratak_Shop.Data.Entities.Products;
using Fratak_Shop.Data.Entities.Smart;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Fratak_Shop.Tests.Integration;

/// <summary>
/// تست یکپارچهٔ دور ۲۶ روی SQL Server واقعی:
/// <list type="bullet">
/// <item>رأی رقبا: دستهٔ اکثریت سایت‌های دیگر مقدم بر همه؛ دستهٔ نبود ← ساخته می‌شود (زیر سطح کلی مسیر)؛ دستهٔ هم‌نام موجود ← همان.</item>
/// <item>حلقهٔ آماده‌سازی: فقط پیش‌نویس واردشدهٔ ناقص؛ فاصلهٔ ۶ ساعت؛ سقف ۵ تلاش؛ کالای منتشرشده هرگز.</item>
/// <item>عکس فضاسازی دسته: بنر خودکار قدیمی (از عکس کالا) با عکس فضاسازی عوض می‌شود؛ عکس انتخاب‌شده و دسته‌ای که ادمین همه را حذف کرده دست نمی‌خورد.</item>
/// </list>
/// همه با داده‌های خودشان (نام تصادفی) و پاک‌سازی در finally.
/// </summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", "Integration")]
[Trait("Area", "Round26")]
public class SmartRound26IntegrationTests(SqlServerFixture db)
{
    private const string Letters = "ابپتثجچحخدذرزسشصضطظعغفقکگلمنوهی";

    /// <summary>واژهٔ فارسی تصادفی (حرف آخر «ژ» تا ریشه‌یاب از آن چیزی حذف نکند).</summary>
    private static string Word(string prefix) =>
        prefix + new string(Enumerable.Range(0, 5).Select(_ => Letters[Random.Shared.Next(Letters.Length)]).ToArray()) + "ژ";

    private static string Key() => Guid.NewGuid().ToString("N")[..10];

    private static async Task<Guid> AddProductAsync(FratakShopDbContext context, Guid categoryId, string name, bool active = false,
        DateTimeOffset? publishedAt = null, DateTimeOffset? intakeAt = null)
    {
        var key = Key();
        var product = new Product
        {
            CategoryId = categoryId,
            Name = name,
            Slug = "r26-" + key,
            SKU = "R26-" + key,
            Price = 100_000,
            StockQuantity = 5,
            IsActive = active,
            PublishedAt = publishedAt,
            SmartIntakeAt = intakeAt,
        };
        context.Products.Add(product);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        return product.Id;
    }

    private static async Task<Guid> AddCategoryAsync(FratakShopDbContext context, string name, string? options = null, string? image = null)
    {
        var category = new Category { Name = name, Slug = "r26-cat-" + Key(), IsActive = true, ImageOptionsJson = options, ImageName = image };
        context.Categories.Add(category);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        return category.Id;
    }

    // ───────────── رأی رقبا ─────────────

    [SqlFact]
    public async Task Market_vote_creates_the_majority_category_under_the_general_level()
    {
        await using var scope = db.Scope();
        var intake = scope.ServiceProvider.GetRequiredService<IProductIntakeService>();
        var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
        var bucket = await scope.ServiceProvider.GetRequiredService<ISmartCategoryService>().EnsureUncategorizedAsync();

        var (top, general, market) = (Word("بالا"), Word("کلی"), Word("نظر"));
        var product = await AddProductAsync(context, bucket, Word("ابزار") + " " + Word("مدل"));
        var ids = new List<Guid> { product };
        Task<MarketLookup?> Lookup(IntakeProduct p, CancellationToken _) =>
            Task.FromResult<MarketLookup?>(new MarketLookup($"{top} > {general} > {Word("برگ")}", market));

        try
        {
            var report = await intake.RunAsync(DateTimeOffset.UtcNow.AddMinutes(10), Lookup, ids);
            Assert.Equal(1, report.Looked);
            Assert.Equal(1, report.Categorized);

            var row = await context.Products.Where(p => p.Id == product)
                .Select(p => new { p.MarketCategory, p.CategorySource, CategoryName = p.Category.Name, ParentName = p.Category.ParentCategory!.Name })
                .SingleAsync();
            Assert.Equal(market, row.MarketCategory);
            Assert.Equal(market, row.CategoryName); // نام اکثریت، نه برگِ یک سایت
            Assert.Equal(general, row.ParentName);
            Assert.Equal(ProductCategorySource.Smart, row.CategorySource);
            Assert.Contains(report.Created, c => c.Name == market);
        }
        finally
        {
            await context.SmartTasks.Where(t => t.TargetId != null && ids.Contains(t.TargetId.Value)).ExecuteDeleteAsync();
        }
    }

    [SqlFact]
    public async Task Market_vote_reuses_an_existing_category_with_the_same_name()
    {
        await using var scope = db.Scope();
        var intake = scope.ServiceProvider.GetRequiredService<IProductIntakeService>();
        var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
        var bucket = await scope.ServiceProvider.GetRequiredService<ISmartCategoryService>().EnsureUncategorizedAsync();

        var name = Word("کتری");
        var existing = await AddCategoryAsync(context, name);
        var product = await AddProductAsync(context, bucket, Word("کالا") + " " + Word("مدل"));
        var ids = new List<Guid> { product };
        // مسیر ناقص (یک بخش) + رأی ← فقط رأی ذخیره می‌شود؛ مسیر قبلی دست نمی‌خورد.
        Task<MarketLookup?> Lookup(IntakeProduct p, CancellationToken _) => Task.FromResult<MarketLookup?>(new MarketLookup("فقط یک بخش", name));

        try
        {
            await intake.RunAsync(DateTimeOffset.UtcNow.AddMinutes(10), Lookup, ids);
            var row = await context.Products.Where(p => p.Id == product).Select(p => new { p.CategoryId, p.SourceCategory, p.MarketCategory }).SingleAsync();
            Assert.Equal(existing, row.CategoryId);
            Assert.Null(row.SourceCategory);
            Assert.Equal(name, row.MarketCategory);
            Assert.Equal(1, await context.Categories.CountAsync(c => c.Name == name)); // دستهٔ تکراری ساخته نشد
        }
        finally
        {
            await context.SmartTasks.Where(t => t.TargetId != null && ids.Contains(t.TargetId.Value)).ExecuteDeleteAsync();
        }
    }

    [SqlFact]
    public async Task Hostile_market_names_are_never_stored_or_created()
    {
        await using var scope = db.Scope();
        var intake = scope.ServiceProvider.GetRequiredService<IProductIntakeService>();
        var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
        var bucket = await scope.ServiceProvider.GetRequiredService<ISmartCategoryService>().EnsureUncategorizedAsync();

        var product = await AddProductAsync(context, bucket, Word("کالا") + " " + Word("مدل"));
        var ids = new List<Guid> { product };
        Task<MarketLookup?> Lookup(IntakeProduct p, CancellationToken _) =>
            Task.FromResult<MarketLookup?>(new MarketLookup(null, "<script>alert(1)</script>"));

        try
        {
            var report = await intake.RunAsync(DateTimeOffset.UtcNow.AddMinutes(10), Lookup, ids);
            Assert.Equal(0, report.Looked);
            var row = await context.Products.Where(p => p.Id == product).Select(p => new { p.CategoryId, p.MarketCategory }).SingleAsync();
            Assert.Null(row.MarketCategory);
            Assert.Equal(bucket, row.CategoryId);
            Assert.False(await context.Categories.AnyAsync(c => c.Name.Contains("script")));
        }
        finally
        {
            await context.SmartTasks.Where(t => t.TargetId != null && ids.Contains(t.TargetId.Value)).ExecuteDeleteAsync();
        }
    }

    // ───────────── حلقهٔ آماده‌سازی ─────────────

    [SqlFact]
    public async Task Readiness_loop_claims_incomplete_drafts_with_gap_and_slow_retries()
    {
        await using var scope = db.Scope();
        var catalog = scope.ServiceProvider.GetRequiredService<ISmartCatalogService>();
        var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
        var category = await AddCategoryAsync(context, "دستهٔ آماده‌سازی " + Key());
        var now = DateTimeOffset.UtcNow;

        var draft = await AddProductAsync(context, category, "پیش‌نویس ناقص " + Key(), intakeAt: now.AddHours(-1));
        var notImported = await AddProductAsync(context, category, "هنوز وارد نشده " + Key());
        var published = await AddProductAsync(context, category, "منتشرشده " + Key(), active: true, publishedAt: now.AddDays(-1), intakeAt: now.AddHours(-1));
        var mine = new[] { draft, notImported, published };
        // قدیمی‌ترین‌ها اول برداشته می‌شوند؛ تاریخ ساخت عقب می‌رود تا کالاهای آزمون‌های دیگر جلو نیفتند.
        await context.Products.Where(p => mine.Contains(p.Id)).ExecuteUpdateAsync(s => s.SetProperty(p => p.CreatedAt, new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero)));

        var first = await catalog.ClaimReadinessCandidatesAsync(now, 50, default, mine);
        Assert.Contains(first, c => c.Id == draft);
        Assert.Contains(first, c => c.Id == notImported);       // دور ۲۸: بدون «ورود هوشمند» هم آماده‌سازی می‌شود
        Assert.DoesNotContain(first, c => c.Id == published);   // منتشرشده هرگز

        var row = await context.Products.Where(p => p.Id == draft).Select(p => new { p.SmartReadyAttempts, p.SmartReadyCheckedAt }).SingleAsync();
        Assert.Equal(1, row.SmartReadyAttempts);
        Assert.NotNull(row.SmartReadyCheckedAt);

        // کمتر از ۶ ساعت بعد ← دوباره برداشته نمی‌شود؛ ۷ ساعت بعد ← بله.
        Assert.DoesNotContain(await catalog.ClaimReadinessCandidatesAsync(now.AddHours(1), 50, default, mine), c => c.Id == draft);
        Assert.Contains(await catalog.ClaimReadinessCandidatesAsync(now.AddHours(7), 50, default, mine), c => c.Id == draft);

        // دور ۲۸: بعد از ۵ تلاش رها نمی‌شود؛ فقط روزی یک بار (۷ ساعت بعد ← نه؛ ۲۵ ساعت بعد ← بله).
        var checkedAt = now.AddHours(7);
        await context.Products.Where(p => p.Id == draft).ExecuteUpdateAsync(s => s.SetProperty(p => p.SmartReadyAttempts, SmartCatalogService.MaxReadyAttempts)
            .SetProperty(p => p.SmartReadyCheckedAt, checkedAt));
        Assert.DoesNotContain(await catalog.ClaimReadinessCandidatesAsync(checkedAt.AddHours(7), 50, default, mine), c => c.Id == draft);
        Assert.Contains(await catalog.ClaimReadinessCandidatesAsync(checkedAt.AddHours(25), 50, default, mine), c => c.Id == draft);

        // «آماده‌سازی دوباره همه» ← شمارنده صفر و بلافاصله قابل برداشت.
        Assert.True(await catalog.ResetReadinessAsync() >= 1);
        context.ChangeTracker.Clear();
        Assert.Equal(0, await context.Products.Where(p => p.Id == draft).Select(p => p.SmartReadyAttempts).SingleAsync());
        Assert.Contains(await catalog.ClaimReadinessCandidatesAsync(checkedAt.AddHours(26), 50, default, mine), c => c.Id == draft);
    }

    // ───────────── عکس فضاسازی دسته ─────────────

    [SqlFact]
    public async Task Photo_options_replace_an_old_auto_banner_but_never_a_chosen_photo()
    {
        await using var scope = db.Scope();
        var catalog = scope.ServiceProvider.GetRequiredService<ISmartCatalogService>();
        var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();

        // بنر خودکار قدیمی (از عکس کالا) فعال است ← عکس فضاسازی تازه جایش می‌نشیند.
        var legacy = await AddCategoryAsync(context, "دستهٔ بنر قدیمی " + Key(), CategoryImages.Serialize(["old1.webp", "old2.webp"]), "old1.webp");
        Assert.True(await catalog.SaveCategoryImageOptionsAsync(legacy, ["kitchen-photo-aaaa1111.webp", "kitchen-photo-bbbb2222.webp"]));
        Assert.Equal("kitchen-photo-aaaa1111.webp", (await catalog.GetCategoryImagesAsync(legacy))!.ImageName);

        // عکس فضاسازی انتخاب‌شده می‌ماند حتی اگر فهرست تازه آن را نداشته باشد.
        var chosen = await AddCategoryAsync(context, "دستهٔ عکس انتخابی " + Key(), CategoryImages.Serialize(["a-photo-11112222.webp"]), "a-photo-11112222.webp");
        Assert.True(await catalog.SaveCategoryImageOptionsAsync(chosen, ["b-photo-33334444.webp"]));
        Assert.Equal("a-photo-11112222.webp", (await catalog.GetCategoryImagesAsync(chosen))!.ImageName);
    }

    [SqlFact]
    public async Task Photo_backfill_skips_categories_with_photos_or_cleared_by_the_admin()
    {
        await using var scope = db.Scope();
        var catalog = scope.ServiceProvider.GetRequiredService<ISmartCatalogService>();
        var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();

        var cleared = await AddCategoryAsync(context, "دستهٔ پاک‌شده " + Key(), "[]");
        // دور ۲۸: فقط عکس «سوژهٔ تأییدشده» (-photo-w-) کافی است؛ عکس فضاسازی قدیمی دوباره بازبینی می‌شود.
        var hasPhoto = await AddCategoryAsync(context, "دستهٔ عکس‌دار " + Key(), CategoryImages.Serialize(["x-photo-w-12345678.webp"]));
        var legacyPhoto = await AddCategoryAsync(context, "دستهٔ عکس قدیمی " + Key(), CategoryImages.Serialize(["x-photo-12345678.webp"]));
        var list = await catalog.ListCategoriesNeedingPhotosAsync(500);
        Assert.DoesNotContain(list, c => c.Id == cleared);
        Assert.DoesNotContain(list, c => c.Id == hasPhoto);
        _ = legacyPhoto;
        Assert.True(list.Count <= 50);
    }
}
