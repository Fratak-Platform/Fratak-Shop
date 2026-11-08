using Fratak_Shop.Core.DTOs.Products.Command;
using Fratak_Shop.Core.Services.Products;
using Fratak_Shop.Core.Services.Smart;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Categories;
using Fratak_Shop.Data.Entities.Products;
using Fratak_Shop.Data.Entities.Smart;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Fratak_Shop.Tests.Integration;

/// <summary>
/// تست یکپارچهٔ دور ۲۲ روی SQL Server واقعی:
/// <list type="bullet">
/// <item>هیچ کالای «دسته‌بندی نشده»ای منتشر نمی‌شود (ساخت، فعال‌سازی)؛ غیرفعال کردن همیشه آزاد است.</item>
/// <item>تغییر گروهی تأمین‌کننده: انتخاب‌شده‌ها، «بدون تأمین‌کننده»، تأمین‌کنندهٔ ناموجود (NotFound) و فهرست خالی (خطا).</item>
/// <item>کالای تک با مسیر دستهٔ سایت دیگر ← «دستهٔ کلی» (نه دستهٔ یک‌کالایی)؛ دستهٔ دقیق که بعداً پیدا شد ← انتقال.</item>
/// <item>۳ کالای هم‌برگ در دستهٔ کلی ← زیر‌دستهٔ اختصاصی با نام درست.</item>
/// <item>ورود هوشمند: فقط کالای «جاافتاده» (۲ دقیقه بعد از ساخت)، جست‌وجوی دسته در سایت دیگر (خطای آن کار را خراب نمی‌کند)، صف «تکمیل هوشمند» و علامت انجام.</item>
/// <item>تصویرهای پیشنهادی دسته: اولی خودکار فعال؛ انتخاب/حذف یکی/حذف همه؛ نام ناامن و تصویر بیگانه رد.</item>
/// <item>اعمال نام/نام انگلیسی/نامک: نامک فقط برای پیش‌نویس هرگز منتشرنشده؛ نام فقط اگر در این فاصله عوض نشده.</item>
/// </list>
/// همهٔ آزمون‌ها فقط با داده‌های خودشان کار می‌کنند (نام‌های تصادفی) تا با آزمون‌های دیگر تداخل نداشته باشند.
/// </summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", "Integration")]
[Trait("Area", "Smart")]
public class SmartIntakeRound22IntegrationTests(SqlServerFixture db)
{
    private const string Letters = "ابپتثجچحخدذرزژسشصضطظعغفقکگلمنوهی";

    /// <summary>واژهٔ فارسی تصادفی (حرف آخر «ژ» تا ریشه‌یاب پسوندی از آن حذف نکند). مثال: «کلیسپتخدژ».</summary>
    private static string Word(string prefix) =>
        prefix + new string(Enumerable.Range(0, 5).Select(_ => Letters[Random.Shared.Next(Letters.Length)]).ToArray()) + "ژ";

    private static string Key() => Guid.NewGuid().ToString("N")[..10];

    private static async Task<Guid> AddProductAsync(FratakShopDbContext context, Guid categoryId, string name, string? sourceCategory = null,
        bool active = false, DateTimeOffset? publishedAt = null, string? englishName = null)
    {
        var key = Key();
        var product = new Product
        {
            CategoryId = categoryId,
            Name = name,
            EnglishName = englishName,
            Slug = "r22-" + key,
            SKU = "R22-" + key,
            Price = 100_000,
            StockQuantity = 5,
            IsActive = active,
            PublishedAt = publishedAt,
            SourceCategory = sourceCategory,
        };
        context.Products.Add(product);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        return product.Id;
    }

    private static async Task<Guid> AddCategoryAsync(FratakShopDbContext context, string name)
    {
        var category = new Category { Name = name, Slug = "r22-cat-" + Key(), IsActive = true };
        context.Categories.Add(category);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        return category.Id;
    }

    private static CreateProductCommand Command(Guid categoryId, string key, bool active) => new()
    {
        CategoryId = categoryId,
        Name = "کالای انتشار " + key,
        Slug = "r22-pub-" + key,
        SKU = "R22P-" + key,
        Price = 150_000,
        StockQuantity = 3,
        IsActive = active,
        SupplierUrl = "https://supplier.example/p/" + key,
    };

    // ── انتشار بدون دسته ممنوع ───────────────────────────────────────

    [SqlFact]
    public async Task Products_in_the_uncategorized_bucket_can_never_be_published()
    {
        await using var scope = db.Scope();
        var products = scope.ServiceProvider.GetRequiredService<IProductService>();
        var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
        var bucket = await scope.ServiceProvider.GetRequiredService<ISmartCategoryService>().EnsureUncategorizedAsync();
        var real = await AddCategoryAsync(context, "دستهٔ واقعی " + Key());
        var key = Key();

        // ۱) ساخت کالای فعال در «دسته‌بندی نشده» ← خطا و هیچ ردیفی ساخته نمی‌شود.
        var published = await products.CreateProductAsync(Command(bucket, key + "a", active: true));
        Assert.Equal(EnOperationResultStatus.Error, published.Status);
        Assert.Equal(ProductService.PublishWithoutCategory, published.Message);
        Assert.False(await context.Products.AnyAsync(p => p.Slug == "r22-pub-" + key + "a"));

        // ۲) پیش‌نویس در «دسته‌بندی نشده» مجاز است (جای انتظار دسته‌بندی هوشمند).
        var draft = await products.CreateProductAsync(Command(bucket, key + "b", active: false));
        Assert.True(draft.IsSuccess(), draft.Message);
        var id = await context.Products.Where(p => p.Slug == "r22-pub-" + key + "b").Select(p => p.Id).SingleAsync();

        // ۳) فعال‌سازی ← خطا؛ کالا پیش‌نویس می‌ماند.
        var activate = await products.ChangeProductActivationAsync(id, true);
        Assert.Equal(EnOperationResultStatus.Error, activate.Status);
        Assert.False(await context.Products.Where(p => p.Id == id).Select(p => p.IsActive).SingleAsync());

        // ۴) غیرفعال کردن همیشه آزاد است.
        Assert.True((await products.ChangeProductActivationAsync(id, false)).IsSuccess());

        // ۵) بعد از گرفتن دستهٔ واقعی ← انتشار مجاز و تاریخ انتشار ثبت می‌شود.
        await context.Products.Where(p => p.Id == id).ExecuteUpdateAsync(s => s.SetProperty(p => p.CategoryId, real));
        context.ChangeTracker.Clear(); // ExecuteUpdate ردیف ردگیری‌شده را تازه نمی‌کند (در برنامهٔ واقعی هر درخواست زمینهٔ تازه دارد)
        Assert.True((await products.ChangeProductActivationAsync(id, true)).IsSuccess());
        var row = await context.Products.Where(p => p.Id == id).Select(p => new { p.IsActive, p.PublishedAt }).SingleAsync();
        Assert.True(row.IsActive);
        Assert.NotNull(row.PublishedAt);

        // ۶) ساخت مستقیم کالای فعال در دستهٔ واقعی مثل قبل کار می‌کند.
        Assert.True((await products.CreateProductAsync(Command(real, key + "c", active: true))).IsSuccess());
    }

    // ── تغییر گروهی تأمین‌کننده ──────────────────────────────────────

    [SqlFact]
    public async Task Supplier_can_be_changed_or_cleared_for_many_products_at_once()
    {
        var a = await db.CreateProductAsync();
        var b = await db.CreateProductAsync();
        var untouched = await db.CreateProductAsync();
        await using var scope = db.Scope();
        var products = scope.ServiceProvider.GetRequiredService<IProductService>();
        var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
        var supplier = new Supplier { Name = "تأمین‌کنندهٔ گروهی " + Key() };
        context.Suppliers.Add(supplier);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        Task<List<Guid?>> SuppliersOf(params Guid[] ids) =>
            context.Products.Where(p => ids.Contains(p.Id)).OrderBy(p => p.Id).Select(p => p.SupplierId).ToListAsync();

        // شناسهٔ تکراری فقط یک بار حساب می‌شود.
        var changed = await products.ChangeSupplierAsync([a, b, a], supplier.Id);
        Assert.True(changed.IsSuccess(), changed.Message);
        Assert.Equal(2, changed.Data);
        Assert.All(await SuppliersOf(a, b), s => Assert.Equal(supplier.Id, s));
        Assert.Null(Assert.Single(await SuppliersOf(untouched)));

        // دوباره همان تأمین‌کننده ← تغییری لازم نیست.
        Assert.Equal(0, (await products.ChangeSupplierAsync([a, b], supplier.Id)).Data);

        // تأمین‌کنندهٔ ناموجود ← NotFound و هیچ چیز عوض نمی‌شود.
        var unknown = await products.ChangeSupplierAsync([a], Guid.NewGuid());
        Assert.Equal(EnOperationResultStatus.NotFound, unknown.Status);
        Assert.All(await SuppliersOf(a, b), s => Assert.Equal(supplier.Id, s));

        // فهرست خالی ← خطا.
        Assert.Equal(EnOperationResultStatus.Error, (await products.ChangeSupplierAsync([], supplier.Id)).Status);

        // شناسهٔ کالای ناموجود ← بی‌اثر (۰ تغییر، بدون خطا).
        Assert.Equal(0, (await products.ChangeSupplierAsync([Guid.NewGuid()], supplier.Id)).Data);

        // «بدون تأمین‌کننده» ← پاک می‌شود.
        var cleared = await products.ChangeSupplierAsync([a, b], null);
        Assert.True(cleared.IsSuccess());
        Assert.Equal(2, cleared.Data);
        Assert.All(await SuppliersOf(a, b), s => Assert.Null(s));
    }

    // ── دستهٔ کلی برای کالای تک و زیر‌دستهٔ اختصاصی ──────────────────

    [SqlFact]
    public async Task A_lone_product_goes_to_a_general_category_and_moves_once_an_exact_one_exists()
    {
        await using var scope = db.Scope();
        var service = scope.ServiceProvider.GetRequiredService<ISmartCategoryService>();
        var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
        var bucket = await service.EnsureUncategorizedAsync();

        var (top, general, leaf) = (Word("بالا"), Word("کلی"), Word("برگ"));
        var lone = await AddProductAsync(context, bucket, Word("ابزار") + " " + Word("مدل"), $"خانه > {top} > {general} > {leaf}");

        var first = await service.RunAsync([lone], 10);
        Assert.Equal(1, first.Examined);
        Assert.Equal(1, first.Moved);
        Assert.Equal(1, first.General);
        var row = await context.Products.Where(p => p.Id == lone).Select(p => new { p.CategoryId, p.CategorySource, CategoryName = p.Category.Name }).SingleAsync();
        Assert.Equal(general, row.CategoryName); // دستهٔ کلی، نه «برگ» برای یک کالا
        Assert.Equal(ProductCategorySource.General, row.CategorySource);
        Assert.Contains(first.Created, c => c.Id == row.CategoryId && c.Products == 1);
        Assert.False(await context.Categories.AnyAsync(c => c.Name == leaf));

        // هنوز دستهٔ دقیقی نیست و کالا تنهاست ← همان‌جا می‌ماند (دستهٔ یک‌کالایی ساخته نمی‌شود).
        var second = await service.RunAsync([lone], 10);
        Assert.Equal(1, second.Examined);
        Assert.Equal(0, second.Moved);
        Assert.False(await context.Categories.AnyAsync(c => c.Name == leaf));

        // ادمین دستهٔ دقیق را ساخت ← کالا به آن می‌رود (دقیق).
        var exact = await AddCategoryAsync(context, leaf);
        var third = await service.RunAsync([lone], 10);
        Assert.Equal(1, third.Moved);
        var moved = await context.Products.Where(p => p.Id == lone).Select(p => new { p.CategoryId, p.CategorySource }).SingleAsync();
        Assert.Equal(exact, moved.CategoryId);
        Assert.Equal(ProductCategorySource.Smart, moved.CategorySource);

        // کالای دقیق‌شده دیگر نامزد نیست.
        Assert.Equal(0, (await service.RunAsync([lone], 10)).Examined);
    }

    [SqlFact]
    public async Task Three_products_with_the_same_leaf_in_a_general_category_get_their_own_subcategory()
    {
        await using var scope = db.Scope();
        var service = scope.ServiceProvider.GetRequiredService<ISmartCategoryService>();
        var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
        var bucket = await service.EnsureUncategorizedAsync();

        var (top, general, leaf) = (Word("بالا"), Word("کلی"), Word("برگ"));
        var path = $"{top} > {general} > {leaf}";
        var ids = new List<Guid>();
        for (var i = 0; i < 3; i++)
            ids.Add(await AddProductAsync(context, bucket, Word("کالا") + " " + Word("نوع"), path));

        // دور اول: نام‌ها شبیه هم نیستند (خوشه نمی‌سازند) ← همه به دستهٔ کلی.
        var first = await service.RunAsync(ids, 10);
        Assert.Equal(3, first.General);
        var generalId = await context.Categories.Where(c => c.Name == general).Select(c => c.Id).SingleAsync();
        Assert.All(await context.Products.Where(p => ids.Contains(p.Id)).Select(p => p.CategoryId).ToListAsync(), c => Assert.Equal(generalId, c));

        // دور دوم (مثل بازبینی روزانه): ۳ کالای هم‌برگ ← زیر‌دستهٔ اختصاصی زیر همان دستهٔ کلی.
        var second = await service.RunAsync(ids, 10);
        Assert.Equal(3, second.Moved);
        var sub = await context.Categories.SingleAsync(c => c.Name == leaf);
        Assert.Equal(generalId, sub.ParentId);
        Assert.True(sub.IsAutoCreated);
        Assert.True(Fratak_Shop.Data.Utilities.EnglishSlug.IsEnglish(sub.Slug), sub.Slug);
        var rows = await context.Products.Where(p => ids.Contains(p.Id)).Select(p => new { p.CategoryId, p.CategorySource }).ToListAsync();
        Assert.All(rows, r =>
        {
            Assert.Equal(sub.Id, r.CategoryId);
            Assert.Equal(ProductCategorySource.Smart, r.CategorySource);
        });
    }

    // ── ورود هوشمند ──────────────────────────────────────────────────

    [SqlFact]
    public async Task Intake_categorizes_new_products_queues_completion_and_marks_them_done()
    {
        await using var scope = db.Scope();
        var intake = scope.ServiceProvider.GetRequiredService<IProductIntakeService>();
        var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
        var bucket = await scope.ServiceProvider.GetRequiredService<ISmartCategoryService>().EnsureUncategorizedAsync();

        var (top, general, leaf) = (Word("بالا"), Word("کلی"), Word("برگ"));
        var found = await AddProductAsync(context, bucket, Word("ابزار") + " " + Word("مدل"));
        var failing = await AddProductAsync(context, bucket, Word("ابزار") + " " + Word("مدل"));
        var manualCategory = await AddCategoryAsync(context, "دستهٔ دستی " + Key());
        var manual = await AddProductAsync(context, manualCategory, Word("کالا"));
        var ids = new List<Guid> { found, failing, manual };
        var lookups = new List<Guid>();

        Task<string?> Lookup(IntakeProduct product, CancellationToken _)
        {
            lookups.Add(product.Id);
            if (product.Id == failing)
                throw new HttpRequestException("سایت در دسترس نیست");
            return Task.FromResult<string?>($"خانه > {top} > {general} > {leaf}");
        }

        try
        {
            // کالای تازه‌ساخته (کمتر از ۲ دقیقه) هنوز بررسی نمی‌شود (ادمین شاید هنوز عکس و مشخصات را کامل می‌کند).
            var early = await intake.RunAsync(DateTimeOffset.UtcNow, Lookup, ids);
            Assert.Equal(0, early.Examined);
            Assert.Empty(lookups);

            var now = DateTimeOffset.UtcNow.AddMinutes(10);
            var report = await intake.RunAsync(now, Lookup, ids);
            Assert.Equal(3, report.Examined);
            Assert.Equal(1, report.Looked); // کالای دستهٔ واقعی جست‌وجو نمی‌شود؛ خطای سایت دیگر فقط رد می‌شود
            Assert.DoesNotContain(manual, lookups);
            Assert.Equal(1, report.Categorized);
            Assert.Equal(3, report.Queued);
            Assert.Equal(failing, Assert.Single(report.Unplaced));
            Assert.Contains(report.Created, c => c.Name == general);
            Assert.False(string.IsNullOrWhiteSpace(report.Summary));

            var rows = await context.Products.Where(p => ids.Contains(p.Id))
                .Select(p => new { p.Id, p.CategoryId, CategoryName = p.Category.Name, p.CategorySource, p.SourceCategory, p.SmartIntakeAt })
                .ToDictionaryAsync(p => p.Id);
            Assert.Equal(general, rows[found].CategoryName);
            Assert.Equal(ProductCategorySource.General, rows[found].CategorySource);
            Assert.Equal($"{top} > {general} > {leaf}", rows[found].SourceCategory); // «خانه» حذف شد
            Assert.Equal(bucket, rows[failing].CategoryId);
            Assert.Equal(manualCategory, rows[manual].CategoryId);
            Assert.All(rows.Values, r => Assert.Equal(now, r.SmartIntakeAt));

            // صف: «تکمیل هوشمند» برای هر سه کالا (اولویت دسته‌ای) و «تکمیل دسته» برای دستهٔ تازه.
            var tasks = await context.SmartTasks.Where(t => t.TargetId != null && ids.Contains(t.TargetId.Value) && t.Kind == SmartTaskKind.ProductComplete).ToListAsync();
            Assert.Equal(3, tasks.Count);
            Assert.All(tasks, t =>
            {
                Assert.Equal(SmartTaskStatus.Queued, t.Status);
                Assert.Equal(SmartPriority.Batch, t.Priority);
            });
            var createdId = report.Created.Single(c => c.Name == general).Id;
            Assert.True(await context.SmartTasks.AnyAsync(t => t.TargetId == createdId && t.Kind == SmartTaskKind.CategoryComplete));

            // دور بعد: همه «انجام‌شده»اند ← دوباره سراغشان نمی‌رود.
            lookups.Clear();
            var again = await intake.RunAsync(now.AddMinutes(3), Lookup, ids);
            Assert.Equal(0, again.Examined);
            Assert.Empty(lookups);
        }
        finally
        {
            await context.SmartTasks.Where(t => t.TargetId != null && ids.Contains(t.TargetId.Value)).ExecuteDeleteAsync();
        }
    }

    // ── تصویرهای پیشنهادی دسته ───────────────────────────────────────

    [SqlFact]
    public async Task Category_image_options_auto_approve_the_first_and_can_be_chosen_or_removed()
    {
        await using var scope = db.Scope();
        var catalog = scope.ServiceProvider.GetRequiredService<ISmartCatalogService>();
        var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
        var id = await AddCategoryAsync(context, "دستهٔ تصویر " + Key());

        var fresh = await catalog.GetCategoryImagesAsync(id);
        Assert.NotNull(fresh);
        Assert.Null(fresh.Options); // هنوز پیشنهادی ساخته نشده
        Assert.Null(await catalog.GetCategoryImagesAsync(Guid.NewGuid()));
        Assert.False(await catalog.SaveCategoryImageOptionsAsync(Guid.NewGuid(), ["a.webp"]));

        // ذخیره: نام ناامن و تکراری حذف، حداکثر ۴، اولی خودکار تصویر دسته.
        Assert.True(await catalog.SaveCategoryImageOptionsAsync(id, ["a1.webp", "../evil.webp", "a2.webp", "a1.webp", "a3.webp", "a4.webp", "a5.webp"]));
        var state = await catalog.GetCategoryImagesAsync(id);
        Assert.Equal(new[] { "a1.webp", "a2.webp", "a3.webp", "a4.webp" }, state!.Options!);
        Assert.Equal("a1.webp", state.ImageName);
        Assert.True((await catalog.ListCategoryImagesAsync()).ContainsKey(id) || await context.Categories.CountAsync() > 500);

        // انتخاب یکی دیگر از همان پیشنهادها.
        Assert.True(await catalog.ChooseCategoryImageAsync(id, "a3.webp"));
        Assert.Equal("a3.webp", (await catalog.GetCategoryImagesAsync(id))!.ImageName);

        // حمله/دست‌کاری فرم: نام بیرون از فهرست، مسیر «../» یا دستهٔ دیگر ← رد و بدون تغییر.
        Assert.False(await catalog.ChooseCategoryImageAsync(id, "../../etc/passwd"));
        Assert.False(await catalog.ChooseCategoryImageAsync(id, "other.webp"));
        Assert.False(await catalog.ChooseCategoryImageAsync(Guid.NewGuid(), "a1.webp"));
        Assert.Equal("a3.webp", (await catalog.GetCategoryImagesAsync(id))!.ImageName);

        // حذف تصویر فعال ← اولین پیشنهاد باقی‌مانده فعال می‌شود.
        Assert.Equal(new[] { "a3.webp" }, await catalog.RemoveCategoryImagesAsync(id, "a3.webp"));
        state = await catalog.GetCategoryImagesAsync(id);
        Assert.Equal(new[] { "a1.webp", "a2.webp", "a4.webp" }, state!.Options!);
        Assert.Equal("a1.webp", state.ImageName);

        // حذف چیزی که نیست ← هیچ.
        Assert.Empty(await catalog.RemoveCategoryImagesAsync(id, "nope.webp"));
        Assert.Empty(await catalog.RemoveCategoryImagesAsync(Guid.NewGuid(), null));

        // حذف همه ← فهرست خالی (نه null؛ یعنی دیگر خودکار ساخته نشود) و تصویر دسته خالی.
        Assert.Equal(3, (await catalog.RemoveCategoryImagesAsync(id, null)).Count);
        state = await catalog.GetCategoryImagesAsync(id);
        Assert.NotNull(state!.Options);
        Assert.Empty(state.Options);
        Assert.Null(state.ImageName);
        Assert.Empty(await catalog.RemoveCategoryImagesAsync(id, null));
    }

    [SqlFact]
    public async Task Saving_options_never_replaces_an_image_the_admin_uploaded()
    {
        await using var scope = db.Scope();
        var catalog = scope.ServiceProvider.GetRequiredService<ISmartCatalogService>();
        var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
        var id = await AddCategoryAsync(context, "دستهٔ عکس دستی " + Key());
        await context.Categories.Where(c => c.Id == id).ExecuteUpdateAsync(s => s.SetProperty(c => c.ImageName, "manual.webp"));

        Assert.True(await catalog.SaveCategoryImageOptionsAsync(id, ["s1.webp", "s2.webp"]));
        var state = await catalog.GetCategoryImagesAsync(id);
        Assert.Equal("manual.webp", state!.ImageName);

        // حذف همهٔ پیشنهادها تصویر دستی را پاک نمی‌کند.
        Assert.Equal(2, (await catalog.RemoveCategoryImagesAsync(id, null)).Count);
        Assert.Equal("manual.webp", (await catalog.GetCategoryImagesAsync(id))!.ImageName);
    }

    // ── نام، نام انگلیسی، نامک ───────────────────────────────────────

    [SqlFact]
    public async Task Identity_is_applied_only_where_it_is_safe()
    {
        await using var scope = db.Scope();
        var catalog = scope.ServiceProvider.GetRequiredService<ISmartCatalogService>();
        var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
        var category = await AddCategoryAsync(context, "دستهٔ نام " + Key());
        var key = Key();

        // پیش‌نویس هرگز منتشرنشده: نام، نام انگلیسی و نامک؛ متن جایگزین عکس که همان نام قبلی بود هم به‌روز می‌شود.
        const string oldName = "خرید هدفون تست اورجینال";
        var draft = await AddProductAsync(context, category, oldName);
        context.ProductImages.Add(new ProductImage { ProductId = draft, ImageName = "x-" + key + ".webp", AltText = oldName, IsPrimary = true });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var applied = await catalog.ApplyIdentityAsync(draft, "هدفون تست", "Test Headphone", "test-headphone-" + key, oldName);
        Assert.True(applied.Name);
        Assert.True(applied.EnglishName);
        Assert.Equal("test-headphone-" + key, applied.Slug);
        Assert.True(applied.Any);
        var row = await context.Products.Where(p => p.Id == draft).Select(p => new { p.Name, p.EnglishName, p.Slug }).SingleAsync();
        Assert.Equal(("هدفون تست", "Test Headphone", "test-headphone-" + key), (row.Name, row.EnglishName, row.Slug));
        Assert.Equal("هدفون تست", await context.ProductImages.Where(i => i.ProductId == draft).Select(i => i.AltText).SingleAsync());

        // نام در این فاصله عوض شده (expectedName قدیمی) ← نام دست نمی‌خورد. نام انگلیسی موجود بدون اجازه بازنویسی نمی‌شود.
        var stale = await catalog.ApplyIdentityAsync(draft, "نام دیگر", "Other", null, oldName);
        Assert.False(stale.Name);
        Assert.False(stale.EnglishName);
        Assert.False(stale.Any);

        // نامک تکراری ← نسخهٔ یکتا («-2»)؛ نامک غیرانگلیسی ← نادیده.
        var other = await AddProductAsync(context, category, "کالای دیگر " + key);
        var taken = "taken-" + key;
        await context.Products.Where(p => p.Id == other).ExecuteUpdateAsync(s => s.SetProperty(p => p.Slug, taken));
        Assert.Equal(taken + "-2", (await catalog.ApplyIdentityAsync(draft, null, null, taken, "هدفون تست")).Slug);
        Assert.Null((await catalog.ApplyIdentityAsync(draft, null, null, "نامک فارسی", "هدفون تست")).Slug);
        Assert.Null((await catalog.ApplyIdentityAsync(draft, null, null, "Bad Slug!", "هدفون تست")).Slug);

        // کالای منتشرشده: نامک (آدرس ثبت‌شده در گوگل) هرگز عوض نمی‌شود.
        var live = await AddProductAsync(context, category, "کالای منتشرشده " + key, active: true, publishedAt: DateTimeOffset.UtcNow.AddDays(-3));
        var liveSlug = await context.Products.Where(p => p.Id == live).Select(p => p.Slug).SingleAsync();
        var liveResult = await catalog.ApplyIdentityAsync(live, null, "Live Product", "live-product-" + key, "کالای منتشرشده " + key);
        Assert.Null(liveResult.Slug);
        Assert.True(liveResult.EnglishName); // نام انگلیسی خالی بی‌خطر پر می‌شود
        Assert.Equal(liveSlug, await context.Products.Where(p => p.Id == live).Select(p => p.Slug).SingleAsync());

        // کالای ناموجود ← هیچ.
        Assert.False((await catalog.ApplyIdentityAsync(Guid.NewGuid(), "x y z", "X", "x-y-z", "x")).Any);
    }
}
