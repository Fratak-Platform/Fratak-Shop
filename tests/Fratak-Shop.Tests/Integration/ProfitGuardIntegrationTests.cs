using Fratak_Shop.Core.DTOs.Products.Command;
using Fratak_Shop.Core.Services.Products;
using Fratak_Shop.Core.Services.Smart;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Fratak_Shop.Tests.Integration;

/// <summary>
/// دور ۱۹ (C27) – «محافظ سود» روی سرویس واقعی محصول و SQL Server واقعی:
/// <list type="bullet">
/// <item>ساخت کالا با قیمت فروش (یا قیمت تخفیف) کمتر از قیمت خرید رد می‌شود و هیچ چیزی ذخیره نمی‌شود.</item>
/// <item>ویرایش کالای موجود به قیمت زیان‌ده رد می‌شود و قیمت قبلی در پایگاه داده دست نمی‌خورد.</item>
/// <item>با خاموش کردن محافظ در تنظیمات محتوای هوشمند (حراج عمدی)، همان درخواست از محافظ عبور می‌کند.</item>
/// </list>
/// تست‌های یک Collection پشت سر هم اجرا می‌شوند، پس تغییر موقت تنظیمات روی تست‌های دیگر اثری ندارد (در finally برگردانده می‌شود).
/// </summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", "Integration")]
public class ProfitGuardIntegrationTests(SqlServerFixture db)
{
    /// <summary>دستور ساخت معتبر (به‌جز قیمت‌ها)؛ دسته عمداً ناموجود است تا اگر محافظ رد نکند، نتیجه «یافت نشد» باشد و چیزی ساخته نشود.</summary>
    private static CreateProductCommand NewCommand(decimal price, decimal? discount, decimal? purchase)
    {
        var key = Guid.NewGuid().ToString("N")[..10];
        return new CreateProductCommand
        {
            CategoryId = Guid.NewGuid(),
            Name = "کالای محافظ سود " + key,
            Slug = "profit-guard-" + key,
            SKU = "PG-" + key,
            Price = price,
            DiscountPrice = discount,
            PurchasePrice = purchase,
            StockQuantity = 1,
            SupplierUrl = "https://supplier.example/p/" + key,
        };
    }

    /// <summary>روشن/خاموش کردن محافظ و برگرداندن مقدار قبلی.</summary>
    private async Task<bool> SetGuardAsync(bool enabled)
    {
        await using var scope = db.Scope();
        var config = scope.ServiceProvider.GetRequiredService<ISmartConfigService>();
        var options = await config.GetAsync();
        var previous = options.BlockBelowCost;
        options.BlockBelowCost = enabled;
        Assert.True((await config.SaveAsync(options)).IsSuccess());
        return previous;
    }

    [SqlFact]
    public async Task Creating_a_product_below_cost_is_blocked_and_nothing_is_saved()
    {
        var previous = await SetGuardAsync(true);
        try
        {
            await using var scope = db.Scope();
            var products = scope.ServiceProvider.GetRequiredService<IProductService>();

            // قیمت فروش ۱۰۰ هزار، خرید ۱۵۰ هزار ← زیان.
            var belowCost = NewCommand(100_000, null, 150_000);
            var result = await products.CreateProductAsync(belowCost);
            Assert.Equal(EnOperationResultStatus.Error, result.Status);
            Assert.StartsWith("زیان", result.Message);

            // قیمت عادی سودده ولی تخفیف زیر قیمت خرید ← باز هم زیان (کمترین قیمت ملاک است).
            var discounted = NewCommand(200_000, 120_000, 150_000);
            var discountResult = await products.CreateProductAsync(discounted);
            Assert.Equal(EnOperationResultStatus.Error, discountResult.Status);
            Assert.Contains("زیان", discountResult.Message);

            var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
            Assert.False(await context.Products.AnyAsync(p => p.SKU == belowCost.SKU || p.SKU == discounted.SKU));

            // قیمت سودده از محافظ عبور می‌کند (و به بررسی بعدی، یعنی دسته، می‌رسد).
            var fine = await products.CreateProductAsync(NewCommand(200_000, 160_000, 150_000));
            Assert.Equal(EnOperationResultStatus.NotFound, fine.Status);
        }
        finally
        {
            await SetGuardAsync(previous);
        }
    }

    [SqlFact]
    public async Task Editing_to_a_losing_price_is_blocked_unless_the_guard_is_turned_off()
    {
        var productId = await db.CreateProductAsync(price: 250_000);
        var previous = await SetGuardAsync(true);
        try
        {
            EditProductCommand command;
            await using (var scope = db.Scope())
            {
                var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
                var product = await context.Products.FirstAsync(p => p.Id == productId);
                command = new EditProductCommand
                {
                    Id = product.Id,
                    CategoryId = product.CategoryId,
                    Name = product.Name,
                    Slug = product.Slug,
                    SKU = product.SKU,
                    Price = 90_000,
                    PurchasePrice = 120_000,
                    StockQuantity = product.StockQuantity,
                    IsActive = product.IsActive,
                    PublishedAt = product.PublishedAt,
                    SupplierUrl = "https://supplier.example/p/" + product.SKU, // لینک منبع خرید در ویرایش الزامی است
                };

                var result = await scope.ServiceProvider.GetRequiredService<IProductService>().EditProductAsync(command);
                Assert.Equal(EnOperationResultStatus.Error, result.Status);
                Assert.StartsWith("زیان", result.Message);
            }

            await using (var scope = db.Scope())
            {
                var price = await scope.ServiceProvider.GetRequiredService<FratakShopDbContext>().Products
                    .Where(p => p.Id == productId).Select(p => p.Price).FirstAsync();
                Assert.Equal(250_000m, price); // قیمت قبلی دست نخورده
            }

            // حراج عمدی: محافظ خاموش ← همان ویرایش ذخیره می‌شود.
            await SetGuardAsync(false);
            await using (var scope = db.Scope())
            {
                var result = await scope.ServiceProvider.GetRequiredService<IProductService>().EditProductAsync(command);
                Assert.True(result.IsSuccess(), result.Message);
                var price = await scope.ServiceProvider.GetRequiredService<FratakShopDbContext>().Products
                    .Where(p => p.Id == productId).Select(p => p.Price).FirstAsync();
                Assert.Equal(90_000m, price);
            }
        }
        finally
        {
            await SetGuardAsync(previous);
        }
    }

    [SqlFact]
    public async Task Break_even_or_losing_discount_is_blocked_even_when_the_guard_is_off()
    {
        // دور ۲۹ (#۱۳ – «همیشه سود»): خاموش بودن محافظ فقط قیمت پایهٔ حراج عمدی را آزاد می‌کند؛ تخفیف بدون سود هرگز.
        var previous = await SetGuardAsync(false);
        try
        {
            await using var scope = db.Scope();
            var products = scope.ServiceProvider.GetRequiredService<IProductService>();

            var breakEven = await products.CreateProductAsync(NewCommand(200_000, 150_000, 150_000));
            Assert.Equal(EnOperationResultStatus.Error, breakEven.Status);
            Assert.StartsWith("زیان یا بدون سود", breakEven.Message, StringComparison.Ordinal);

            var losing = await products.CreateProductAsync(NewCommand(200_000, 140_000, 150_000));
            Assert.Equal(EnOperationResultStatus.Error, losing.Status);
            Assert.StartsWith("زیان", losing.Message, StringComparison.Ordinal);

            // تخفیف سودده از محافظ عبور می‌کند (به بررسی دسته می‌رسد ← «یافت نشد»).
            var fine = await products.CreateProductAsync(NewCommand(200_000, 151_000, 150_000));
            Assert.Equal(EnOperationResultStatus.NotFound, fine.Status);
        }
        finally
        {
            await SetGuardAsync(previous);
        }
    }
}
