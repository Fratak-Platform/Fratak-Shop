using Fratak_Shop.Core.Services.Products;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Products;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Fratak_Shop.Tests.Integration;

/// <summary>
/// تست ابزارهای کاتالوگ روی SQL Server واقعی (مرحلهٔ هفتم – فاز ۵):
/// تغییر گروهی قیمت (پیش‌نمایش ← اعمال ← برگرداندن محتاطانه)، رد اعمال کهنه (stale)،
/// ورود از فایل با ثبت در تاریخچهٔ قیمت و دفتر موجودی، و کپی محصول.
/// </summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", "Integration")]
public class CatalogToolsIntegrationTests(SqlServerFixture db)
{
    private static FratakShopDbContext Ctx(AsyncServiceScope scope) => scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();

    private async Task SetAsync(Guid id, Action<Product> change)
    {
        await using var scope = db.Scope();
        var context = Ctx(scope);
        var product = await context.Products.AsTracking().SingleAsync(p => p.Id == id);
        change(product);
        await context.SaveChangesAsync();
    }

    private async Task<Product> GetAsync(Guid id)
    {
        await using var scope = db.Scope();
        return await Ctx(scope).Products.AsNoTracking().Include(p => p.Images).Include(p => p.Features).SingleAsync(p => p.Id == id);
    }

    [Fact]
    public async Task Bulk_price_is_applied_and_reverted_without_overwriting_later_edits()
    {
        var a = await db.CreateProductAsync(400_000, 5);
        var b = await db.CreateProductAsync(300_000, 5);
        var scope = new BulkPriceScope(ProductIds: [a, b]);
        var op = new BulkPriceOperation(BulkPriceTarget.Price, BulkPriceMode.Percent, 10);

        string token;
        await using (var s = db.Scope())
        {
            var preview = await new BulkPriceService(Ctx(s)).PreviewAsync(scope, op);
            Assert.Equal(EnOperationResultStatus.Success, preview.Status);
            Assert.Equal(2, preview.Data.Matched);
            Assert.Equal(2, preview.Data.Changed);
            token = preview.Data.Token;
        }

        Guid batchId;
        await using (var s = db.Scope())
        {
            var applied = await new BulkPriceService(Ctx(s)).ApplyAsync(scope, op, token, null, "تست");
            Assert.Equal(EnOperationResultStatus.Success, applied.Status);
            batchId = applied.Data;
        }

        Assert.Equal(440_000, (await GetAsync(a)).Price);
        Assert.Equal(330_000, (await GetAsync(b)).Price);
        await using (var s = db.Scope())
        {
            var batch = await Ctx(s).PriceChangeBatches.Include(x => x.Items).SingleAsync(x => x.Id == batchId);
            Assert.Equal(PriceChangeSource.BulkTool, batch.Source);
            Assert.Equal(2, batch.Items.Count);
            Assert.Contains(batch.Items, i => i.ProductId == a && i.OldPrice == 400_000 && i.NewPrice == 440_000);
        }

        // بعد از اعمال، کسی قیمت A را دستی عوض می‌کند؛ برگرداندن نباید کار او را پاک کند.
        await SetAsync(a, p => p.Price = 450_000);

        await using (var s = db.Scope())
        {
            var service = new BulkPriceService(Ctx(s));
            Assert.Equal(EnOperationResultStatus.Success, (await service.RevertAsync(batchId, null, "تست")).Status);
            var rows = await service.ListBatchesAsync();
            Assert.NotNull(rows.Single(r => r.Id == batchId).RevertedAt);
        }

        Assert.Equal(450_000, (await GetAsync(a)).Price);
        Assert.Equal(300_000, (await GetAsync(b)).Price);

        await using (var s = db.Scope())
            Assert.Equal(EnOperationResultStatus.Error, (await new BulkPriceService(Ctx(s)).RevertAsync(batchId, null, "تست")).Status);
    }

    [Fact]
    public async Task Apply_is_rejected_when_data_changed_after_the_preview()
    {
        var a = await db.CreateProductAsync(200_000, 3);
        var b = await db.CreateProductAsync(100_000, 3);
        var scope = new BulkPriceScope(ProductIds: [a, b]);
        var op = new BulkPriceOperation(BulkPriceTarget.DiscountPrice, BulkPriceMode.PercentOff, 10);

        string token;
        await using (var s = db.Scope())
            token = (await new BulkPriceService(Ctx(s)).PreviewAsync(scope, op)).Data.Token;

        await SetAsync(b, p => p.Price = 120_000);

        await using (var s = db.Scope())
        {
            var result = await new BulkPriceService(Ctx(s)).ApplyAsync(scope, op, token, null, "تست");
            Assert.Equal(EnOperationResultStatus.Error, result.Status);
            Assert.Equal("stale", result.Code);
        }

        // هیچ چیزی ذخیره نشده است.
        Assert.Null((await GetAsync(a)).DiscountPrice);
        Assert.Null((await GetAsync(b)).DiscountPrice);
    }

    [Fact]
    public async Task Import_updates_by_sku_and_records_price_history_and_stock_ledger()
    {
        var id = await db.CreateProductAsync(250_000, 10);
        var sku = (await GetAsync(id)).SKU;
        var lines = new List<ProductImportLine>
        {
            new(2, sku.ToLowerInvariant(), 260_000, null, 25, 200_000, null),
            new(3, "NOPE-" + Guid.NewGuid().ToString("N")[..8], 1000, null, null, null, null),
        };

        await using (var s = db.Scope())
        {
            var context = Ctx(s);
            var service = new ProductImportService(context, new BulkPriceService(context));
            var preview = await service.PreviewAsync(lines);
            Assert.Equal(1, preview.Changed);
            Assert.Equal(1, preview.Failed);
            Assert.Contains(preview.Rows.Single(r => r.ProductId == id).Changes, c => c.Field == "موجودی" && c.After == "25");

            var applied = await service.ApplyAsync(lines, "prices.xlsx", null, "تست");
            Assert.Equal(EnOperationResultStatus.Success, applied.Status);
        }

        var product = await GetAsync(id);
        Assert.Equal(260_000, product.Price);
        Assert.Equal(200_000, product.PurchasePrice);
        Assert.Equal(25, product.StockQuantity);

        await using (var s = db.Scope())
        {
            var context = Ctx(s);
            Assert.True(await context.PriceChangeBatches.AnyAsync(x => x.Source == PriceChangeSource.Import && x.Items.Any(i => i.ProductId == id && i.NewPrice == 260_000)));

            var (movements, total) = await new InventoryService(context).ListMovementsAsync(new StockMovementQuery(ProductId: id));
            Assert.Equal(2, total);
            Assert.Contains(movements, m => m.Reason == StockMovementReason.Initial && m.Delta == 10);
            var import = Assert.Single(movements, m => m.Reason == StockMovementReason.Import);
            Assert.Equal(15, import.Delta);
            Assert.Equal(25, import.BalanceAfter);
            Assert.Equal("prices.xlsx", import.Reference);
        }
    }

    [Fact]
    public async Task Clone_creates_an_inactive_copy_with_unique_slug_and_sku()
    {
        var id = await db.CreateProductAsync(150_000, 7);
        await using (var s = db.Scope())
        {
            var context = Ctx(s);
            context.ProductImages.Add(new ProductImage { ProductId = id, ImageName = "orig-1.webp", AltText = "عکس", DisplayOrder = 0, IsPrimary = true });
            context.ProductImages.Add(new ProductImage { ProductId = id, ImageName = "orig-2.webp", DisplayOrder = 1 });
            context.Set<ProductFeature>().Add(new ProductFeature { ProductId = id, Key = "رنگ", Value = "مشکی", DisplayOrder = 0 });
            await context.SaveChangesAsync();
        }
        var source = await GetAsync(id);

        Guid first;
        Guid second;
        await using (var s = db.Scope())
        {
            var service = new ProductCloneService(Ctx(s));
            Assert.Equal(new[] { "orig-1.webp", "orig-2.webp" }, (await service.ImageNamesAsync(id)).Order().ToArray());
            // فقط عکس اول کپی فایل شده؛ عکسی که کپی نشده نباید به فایل محصول اصلی اشاره کند.
            var result = await service.CloneAsync(id, new Dictionary<string, string> { ["orig-1.webp"] = "copy-1.webp" });
            Assert.Equal(EnOperationResultStatus.Success, result.Status);
            first = result.Data;
        }
        await using (var s = db.Scope())
            second = (await new ProductCloneService(Ctx(s)).CloneAsync(id, new Dictionary<string, string>())).Data;

        var copy = await GetAsync(first);
        Assert.False(copy.IsActive);
        Assert.Equal(0, copy.StockQuantity);
        Assert.Equal(source.Price, copy.Price);
        Assert.Equal(source.Name + " (کپی)", copy.Name);
        Assert.Equal(source.Slug + "-copy", copy.Slug);
        Assert.Equal(source.SKU + "-C1", copy.SKU);
        var image = Assert.Single(copy.Images);
        Assert.Equal("copy-1.webp", image.ImageName);
        Assert.True(image.IsPrimary);
        Assert.Equal("رنگ", Assert.Single(copy.Features).Key);

        var copy2 = await GetAsync(second);
        Assert.Equal(source.Slug + "-copy-2", copy2.Slug);
        Assert.Equal(source.SKU + "-C2", copy2.SKU);
        Assert.Empty(copy2.Images);

        // محصول اصلی دست نخورده است.
        var after = await GetAsync(id);
        Assert.Equal(2, after.Images.Count);
        Assert.True(after.IsActive);
    }
}
