using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Core.Services.Settings;
using Fratak_Shop.Core.Services.Suppliers;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Products;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Fratak_Shop.Tests.Integration;

/// <summary>
/// تست بررسی تأمین‌کننده روی دیتابیس واقعی (مرحلهٔ هفتم – فاز ۵) با سایت منبع جعلی:
/// ناموجودی خودکار، پیشنهاد «موجود شد» و تغییر قیمت، عدم تکرار پیشنهاد در اجرای دوم، خواندن گروهی با شناسهٔ ذخیره‌شده،
/// تأیید با عدد دلخواه و تغییر هم‌زمان قیمت فروش، و رد شدن بدون پیشنهاد دوباره.
/// </summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", "Integration")]
public class SupplierSyncIntegrationTests(SqlServerFixture db)
{
    /// <summary>سایت ووکامرس جعلی: پاسخ هر نامک/شناسه از دیکشنری.</summary>
    private sealed class FakeStore : ISupplierHttp
    {
        public Dictionary<string, (long Id, string Json)> BySlug { get; } = new();
        public List<string> Urls { get; } = [];

        public Task<SupplierHttpResponse> GetAsync(string url, bool expectJson, CancellationToken cancellationToken)
        {
            Urls.Add(url);
            var query = new Uri(url).Query;
            foreach (var (slug, item) in BySlug)
                if (query.Contains("slug=" + Uri.EscapeDataString(slug) + "&"))
                    return Task.FromResult(new SupplierHttpResponse(200, "[" + item.Json + "]"));
            var include = System.Text.RegularExpressions.Regex.Match(query, @"include=([0-9,]+)");
            if (include.Success)
            {
                var ids = include.Groups[1].Value.Split(',').Select(long.Parse).ToHashSet();
                return Task.FromResult(new SupplierHttpResponse(200, "[" + string.Join(",", BySlug.Values.Where(v => ids.Contains(v.Id)).Select(v => v.Json)) + "]"));
            }
            return Task.FromResult(new SupplierHttpResponse(200, "[]"));
        }
    }

    private static string Item(long id, bool inStock, decimal price, int? stock)
    {
        var stockText = stock.HasValue ? stock.Value + " عدد در انبار" : "";
        var flag = inStock ? "true" : "false";
        // $$$: آکولادهای JSON (حتی «}}» پایانی) متن معمولی‌اند و فقط {{{…}}} جای‌گذاری می‌شود.
        return $$$"""{"id":{{{id}}},"name":"کالا {{{id}}}","prices":{"price":"{{{price:0}}}","currency_code":"IRT","currency_minor_unit":0},"is_in_stock":{{{flag}}},"is_purchasable":true,"stock_availability":{"text":"{{{stockText}}}"}}""";
    }

    private SupplierSyncService Service(AsyncServiceScope scope, ISupplierHttp http) => new(
        scope.ServiceProvider.GetRequiredService<FratakShopDbContext>(),
        scope.ServiceProvider.GetRequiredService<IStoreSettingService>(),
        http,
        scope.ServiceProvider.GetRequiredService<IStaffNotificationService>());

    private async Task<Guid> PrepareAsync(string key, int stock, decimal price, decimal? purchase, Guid supplierId, bool exclude = false)
    {
        var id = await db.CreateProductAsync(price, stock);
        await using var scope = db.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
        var product = await context.Products.AsTracking().SingleAsync(p => p.Id == id);
        product.PurchasePrice = purchase;
        product.SupplierId = supplierId;
        product.SupplierUrl = $"https://store.test/product/{key}/";
        product.ExcludeFromSupplierSync = exclude;
        await context.SaveChangesAsync();
        return id;
    }

    [SqlFact]
    public async Task FullCycle_AutoOutOfStock_Proposals_Approve_Reject()
    {
        var run = Guid.NewGuid().ToString("N")[..8];
        Guid supplierId;
        await using (var scope = db.Scope())
        {
            var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
            var supplier = new Supplier { Name = "تأمین‌کنندهٔ آزمایشی " + run, SyncMode = SupplierSyncMode.WooCommerce };
            context.Suppliers.Add(supplier);
            await context.SaveChangesAsync();
            supplierId = supplier.Id;
        }

        var outKey = "out-" + run;
        var backKey = "back-" + run;
        var skipKey = "skip-" + run;
        var outId = await PrepareAsync(outKey, stock: 3, price: 500_000, purchase: 400_000, supplierId);
        var backId = await PrepareAsync(backKey, stock: 0, price: 500_000, purchase: 400_000, supplierId);
        var skipId = await PrepareAsync(skipKey, stock: 0, price: 500_000, purchase: 400_000, supplierId, exclude: true);

        var store = new FakeStore();
        var outExternal = Random.Shared.NextInt64(1_000_000, 9_000_000);
        store.BySlug[outKey] = (outExternal, Item(outExternal, false, 400_000, null));
        store.BySlug[backKey] = (outExternal + 1, Item(outExternal + 1, true, 415_000, 50));
        store.BySlug[skipKey] = (outExternal + 2, Item(outExternal + 2, true, 415_000, 50));

        // ── اجرای اول ──
        await using (var scope = db.Scope())
        {
            var result = await Service(scope, store).RunAsync(SupplierSyncTrigger.Manual, "تست", CancellationToken.None);
            Assert.NotNull(result);
        }

        await using (var scope = db.Scope())
        {
            var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
            Assert.Equal(0, (await context.Products.SingleAsync(p => p.Id == outId)).StockQuantity);
            var auto = await context.SupplierChanges.SingleAsync(c => c.ProductId == outId && c.Kind == SupplierChangeKind.OutOfStock);
            Assert.Equal(SupplierChangeStatus.AutoApplied, auto.Status);
            Assert.True((await context.SupplierProductStates.SingleAsync(s => s.ProductId == outId)).AutoOutOfStock);

            var pending = await context.SupplierChanges.Where(c => c.ProductId == backId && c.Status == SupplierChangeStatus.Pending).ToListAsync();
            Assert.Equal(10m, pending.Single(c => c.Kind == SupplierChangeKind.BackInStock).NewValue); // ۵۰ با حاشیهٔ ۲۰٪ و سقف ۱۰
            Assert.Equal(415_000m, pending.Single(c => c.Kind == SupplierChangeKind.PriceChange).NewValue);
            Assert.Equal((outExternal + 1).ToString(), (await context.SupplierProductStates.SingleAsync(s => s.ProductId == backId)).ExternalId);

            // محصول «ناموجود دستی» اصلاً خوانده نشد.
            Assert.False(await context.SupplierProductStates.AnyAsync(s => s.ProductId == skipId));
            Assert.DoesNotContain(store.Urls, u => u.Contains(Uri.EscapeDataString(skipKey)));
        }

        // ── اجرای دوم: پیشنهاد تکراری ساخته نمی‌شود و شناسه‌ها گروهی خوانده می‌شوند ──
        store.Urls.Clear();
        await using (var scope = db.Scope())
            await Service(scope, store).RunAsync(SupplierSyncTrigger.Manual, "تست", CancellationToken.None);
        Assert.Contains(store.Urls, u => u.Contains("include="));
        Assert.DoesNotContain(store.Urls, u => u.Contains("slug=" + Uri.EscapeDataString(backKey)));

        List<SupplierChange> backChanges;
        await using (var scope = db.Scope())
        {
            var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
            backChanges = await context.SupplierChanges.Where(c => c.ProductId == backId && c.Status == SupplierChangeStatus.Pending).ToListAsync();
            Assert.Equal(2, backChanges.Count);
            Assert.Equal(1, await context.SupplierChanges.CountAsync(c => c.ProductId == outId)); // ناموجودی دوباره ثبت نشد
        }

        // ── تأیید: موجودی دلخواه ۷ و افزایش قیمت فروش به همان اندازه ──
        await using (var scope = db.Scope())
        {
            var approvals = backChanges.Select(c => new SupplierApproval(c.Id, c.Kind == SupplierChangeKind.BackInStock ? 7 : null)).ToList();
            var result = await Service(scope, store).ApproveAsync(approvals, adjustSalePrice: true, Guid.NewGuid(), "کارمند تست");
            Assert.Equal(Fratak_Shop.Core.Utilities.EnOperationResultStatus.Success, result.Status);
        }

        await using (var scope = db.Scope())
        {
            var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
            var product = await context.Products.SingleAsync(p => p.Id == backId);
            Assert.Equal(7, product.StockQuantity);
            Assert.Equal(415_000m, product.PurchasePrice);
            Assert.Equal(515_000m, product.Price);
        }

        // ── رد: منبع موجود شد، پیشنهاد رد می‌شود و با همان عدد دوباره نمی‌آید ──
        store.BySlug[outKey] = (outExternal, Item(outExternal, true, 400_000, 50));
        await using (var scope = db.Scope())
            await Service(scope, store).RunAsync(SupplierSyncTrigger.Manual, "تست", CancellationToken.None);
        Guid backInStockId;
        await using (var scope = db.Scope())
        {
            var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
            backInStockId = (await context.SupplierChanges.SingleAsync(c => c.ProductId == outId && c.Kind == SupplierChangeKind.BackInStock && c.Status == SupplierChangeStatus.Pending)).Id;
            var result = await Service(scope, store).RejectAsync([backInStockId], Guid.NewGuid(), "کارمند تست");
            Assert.Equal(Fratak_Shop.Core.Utilities.EnOperationResultStatus.Success, result.Status);
        }
        await using (var scope = db.Scope())
            await Service(scope, store).RunAsync(SupplierSyncTrigger.Manual, "تست", CancellationToken.None);
        await using (var scope = db.Scope())
        {
            var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
            Assert.False(await context.SupplierChanges.AnyAsync(c => c.ProductId == outId && c.Kind == SupplierChangeKind.BackInStock && c.Status == SupplierChangeStatus.Pending));
        }
    }
}
