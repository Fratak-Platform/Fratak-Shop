using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Auditing;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Products;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Fratak_Shop.Tests.Integration;

/// <summary>
/// تست‌های مرحلهٔ هفتم – فاز ۳ روی دیتابیس واقعی: پیش‌نویس خودکار، سطل بازیافت (حذف نرم و بازگرداندن)،
/// تاریخچهٔ تغییرات با بازگردانی امن و «نسخهٔ رکورد» برای تشخیص ویرایش هم‌زمان.
/// </summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", "Integration")]
public class AdminTablesIntegrationTests(SqlServerFixture db)
{
    private static AuditActor Staff(string name = "کارمند آزمایشی") => new(Guid.NewGuid(), name, "127.0.0.1", "/admin/test");

    /// <summary>تغییر یک محصول «از پنل» (با بازیگر گزارش فعالیت) در یک scope جدا، مثل یک درخواست واقعی.</summary>
    private async Task EditProductAsync(Guid id, Action<Product> change, string staff = "کارمند آزمایشی")
    {
        using var _ = AuditContext.Begin(Staff(staff));
        await using var scope = db.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
        var product = await context.Products.AsTracking().FirstAsync(p => p.Id == id);
        change(product);
        await context.SaveChangesAsync();
    }

    private async Task<Product> LoadAsync(Guid id)
    {
        await using var scope = db.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
        return await context.Products.IgnoreQueryFilters().AsNoTracking().FirstAsync(p => p.Id == id);
    }

    // ── پیش‌نویس خودکار ──────────────────────────────────────────────────

    [SqlFact]
    public async Task Drafts_SaveReplaceListDelete_PerUser()
    {
        var userId = await db.CreateUserAsync();
        var otherId = await db.CreateUserAsync();
        await using var scope = db.Scope();
        var drafts = scope.ServiceProvider.GetRequiredService<IEditDraftService>();
        const string key = "product:new";
        const string data = """{"v":1,"base":{"values":{},"checks":{}},"mine":{"values":{"Input.Name":["گوشی"]},"checks":{}}}""";

        Assert.Null(await drafts.SaveAsync(userId, key, 0, "محصول جدید", "/admin/products/edit", data));
        Assert.Null(await drafts.SaveAsync(userId, key, 123, "محصول جدید ۲", "/admin/products/edit", data.Replace("گوشی", "تبلت")));

        var draft = await drafts.GetAsync(userId, key);
        Assert.NotNull(draft);
        Assert.Equal(123L, draft!.BaseStamp);
        Assert.Equal("محصول جدید ۲", draft.Title);
        Assert.Contains("تبلت", draft.Data);

        // پیش‌نویس هر کارمند فقط برای خودش است.
        Assert.Null(await drafts.GetAsync(otherId, key));
        Assert.Single(await drafts.ListAsync(userId));

        await drafts.DeleteAsync(userId, key);
        Assert.Null(await drafts.GetAsync(userId, key));
        Assert.Empty(await drafts.ListAsync(userId));
    }

    [SqlFact]
    public async Task Drafts_RejectBadInput_AndDropExternalUrls()
    {
        var userId = await db.CreateUserAsync();
        await using var scope = db.Scope();
        var drafts = scope.ServiceProvider.GetRequiredService<IEditDraftService>();

        Assert.NotNull(await drafts.SaveAsync(userId, "BAD KEY", 0, null, null, "{}"));
        Assert.NotNull(await drafts.SaveAsync(userId, "blog:new", 0, null, null, "{not json"));
        Assert.NotNull(await drafts.SaveAsync(userId, "blog:new", 0, null, null, ""));
        Assert.Null(await drafts.GetAsync(userId, "blog:new"));

        // آدرس خارجی ذخیره نمی‌شود (فهرست پیش‌نویس‌ها لینک قابل کلیک دارد).
        Assert.Null(await drafts.SaveAsync(userId, "blog:new", 0, "<b>مقاله</b>", "https://evil.example/admin", "{}"));
        var summary = Assert.Single(await drafts.ListAsync(userId));
        Assert.Null(summary.Url);
    }

    [SqlFact]
    public async Task Drafts_PurgeRemovesOldOnes()
    {
        var userId = await db.CreateUserAsync();
        await using (var scope = db.Scope())
        {
            var drafts = scope.ServiceProvider.GetRequiredService<IEditDraftService>();
            Assert.Null(await drafts.SaveAsync(userId, "blog:old", 0, null, null, "{}"));
            Assert.Null(await drafts.SaveAsync(userId, "blog:fresh", 0, null, null, "{}"));
            var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
            await context.EditDrafts.Where(d => d.UserId == userId && d.FormKey == "blog:old")
                .ExecuteUpdateAsync(s => s.SetProperty(d => d.UpdatedAt, DateTimeOffset.UtcNow.AddDays(-40)));
        }

        await using (var scope = db.Scope())
        {
            var drafts = scope.ServiceProvider.GetRequiredService<IEditDraftService>();
            Assert.True(await drafts.PurgeAsync(TimeSpan.FromDays(30)) >= 1);
            var left = await drafts.ListAsync(userId);
            Assert.Equal("blog:fresh", Assert.Single(left).FormKey);
        }
    }

    // ── سطل بازیافت ───────────────────────────────────────────────────────

    [SqlFact]
    public async Task Trash_SoftDeleteThenRestore()
    {
        var productId = await db.CreateProductAsync();
        await using (var scope = db.Scope())
        {
            var data = scope.ServiceProvider.GetRequiredService<IAdminDataService>();
            using var _ = AuditContext.Begin(Staff());
            var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
            var product = await context.Products.AsTracking().FirstAsync(p => p.Id == productId);
            context.Products.Remove(product); // حذف نرم (DbContext)
            await context.SaveChangesAsync();

            Assert.False(await data.Query<Product>().AnyAsync(p => p.Id == productId));
            Assert.True(await data.QueryDeleted<Product>().AnyAsync(p => p.Id == productId));
        }

        await using (var scope = db.Scope())
        {
            var data = scope.ServiceProvider.GetRequiredService<IAdminDataService>();
            using var _ = AuditContext.Begin(Staff("بازیابی‌کننده"));
            var result = await data.RestoreAsync<Product>(productId);
            Assert.True(result.IsSuccess(), result.Message);

            Assert.True(await data.Query<Product>().AnyAsync(p => p.Id == productId));
            Assert.False(await data.QueryDeleted<Product>().AnyAsync(p => p.Id == productId));

            // بازگرداندن دوباره: «در سطل نیست»
            Assert.False((await data.RestoreAsync<Product>(productId)).IsSuccess());

            // در گزارش فعالیت «حذف» و «بازیابی» ثبت شده است.
            var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
            var actions = await context.AuditLogs.Where(l => l.EntityType == nameof(Product) && l.EntityId == productId.ToString())
                .Select(l => l.Action).ToListAsync();
            Assert.Contains("delete", actions);
            Assert.Contains("restore", actions);
        }
    }

    [SqlFact]
    public async Task Trash_RestoreUnknownId_IsNotFound()
    {
        await using var scope = db.Scope();
        var data = scope.ServiceProvider.GetRequiredService<IAdminDataService>();
        Assert.False((await data.RestoreAsync<Product>(Guid.NewGuid())).IsSuccess());
    }

    // ── نسخهٔ رکورد و تاریخچه ─────────────────────────────────────────────

    [SqlFact]
    public async Task Stamp_ChangesOnlyWhenStaffSaves()
    {
        var productId = await db.CreateProductAsync();
        await using var scope = db.Scope();
        var history = scope.ServiceProvider.GetRequiredService<IAuditHistoryService>();

        // ساخت محصول در تست بدون بازیگر پنل بوده ← نسخه صفر.
        Assert.Equal(0L, await history.StampAsync(nameof(Product), productId));

        await EditProductAsync(productId, p => p.Price = 300_000);
        var first = await history.StampAsync(nameof(Product), productId);
        Assert.True(first > 0);

        // تغییرات خودکار (بدون بازیگر، مثل کم شدن موجودی بعد از خرید) نسخه را عوض نمی‌کند ← هشدار بیخود نمی‌دهد.
        await using (var other = db.Scope())
        {
            var context = other.ServiceProvider.GetRequiredService<FratakShopDbContext>();
            var product = await context.Products.AsTracking().FirstAsync(p => p.Id == productId);
            product.StockQuantity--;
            await context.SaveChangesAsync();
        }
        Assert.Equal(first, await history.StampAsync(nameof(Product), productId));

        await Task.Delay(20);
        await EditProductAsync(productId, p => p.Price = 310_000, "همکار");
        Assert.NotEqual(first, await history.StampAsync(nameof(Product), productId));

        var items = await history.ListAsync(nameof(Product), productId);
        Assert.Equal(2, items.Count);
        Assert.Equal("همکار", items[0].UserName);
        Assert.Contains(items[0].Changes, c => c.Field == nameof(Product.Price) && c.Before == "300000" && c.After == "310000");
    }

    [SqlFact]
    public async Task Revert_RestoresOldValues_ButKeepsLaterEdits()
    {
        var productId = await db.CreateProductAsync(price: 400_000);
        var original = await LoadAsync(productId);

        // ویرایش ۱: قیمت و نام؛ ویرایش ۲: فقط نام دوباره.
        await EditProductAsync(productId, p => { p.Price = 415_000; p.Name = original.Name + " ویرایش ۱"; });
        await Task.Delay(20);
        await EditProductAsync(productId, p => p.Name = original.Name + " ویرایش ۲");

        await using var scope = db.Scope();
        var history = scope.ServiceProvider.GetRequiredService<IAuditHistoryService>();
        var items = await history.ListAsync(nameof(Product), productId);
        var firstEdit = items.Last(i => i.Action == "update");

        using (AuditContext.Begin(Staff("بازگرداننده")))
        {
            var result = await history.RevertAsync(firstEdit.Id, nameof(Product), productId);
            Assert.True(result.IsSuccess(), result.Message);
            Assert.Contains(nameof(Product.Price), result.Data!.Reverted);
            // نام بعد از ویرایش ۱ دوباره عوض شده ← دست نخورد.
            Assert.Contains(result.Data.Skipped, s => s.StartsWith(nameof(Product.Name) + ":", StringComparison.Ordinal));
        }

        var product = await LoadAsync(productId);
        Assert.Equal(400_000m, product.Price);
        Assert.Equal(original.Name + " ویرایش ۲", product.Name);
    }

    [SqlFact]
    public async Task Revert_RejectsWrongEntity_AndNonUpdates()
    {
        var productId = await db.CreateProductAsync();
        var otherId = await db.CreateProductAsync();
        await EditProductAsync(productId, p => p.Price = 999_000);

        await using var scope = db.Scope();
        var history = scope.ServiceProvider.GetRequiredService<IAuditHistoryService>();
        var edit = (await history.ListAsync(nameof(Product), productId)).First();

        // شناسهٔ گزارش متعلق به محصول دیگری است ← رد می‌شود (جلوگیری از دست‌کاری فرم).
        Assert.False((await history.RevertAsync(edit.Id, nameof(Product), otherId)).IsSuccess());
        Assert.False((await history.RevertAsync(edit.Id, "BlogPost", productId)).IsSuccess());
        Assert.False((await history.RevertAsync(Guid.NewGuid(), nameof(Product), productId)).IsSuccess());
    }
}
