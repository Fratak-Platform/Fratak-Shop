using Fratak_Shop.Core.DTOs.Shopping.Command;
using Fratak_Shop.Core.Services.Searches;
using Fratak_Shop.Core.Services.Settings;
using Fratak_Shop.Core.Services.Shopping;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Searches;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Fratak_Shop.Tests.Integration;

[Collection(SqlServerCollection.Name)]
[Trait("Category", "Integration")]
public class SaveForLaterTests(SqlServerFixture db)
{
    private async Task<(Guid CartId, Guid ProductId)> CartWithProductAsync(int quantity = 2, int stock = 10)
    {
        var user = await db.CreateUserAsync();
        var product = await db.CreateProductAsync(stock: stock);
        await using var scope = db.Scope();
        var carts = scope.ServiceProvider.GetRequiredService<ICartService>();
        var cart = await carts.GetOrCreateCartAsync(user, null);
        Assert.NotNull(cart);
        var add = await carts.AddItemAsync(new AddCartItemCommand { CartId = cart!.Id, ProductId = product, Quantity = quantity });
        Assert.True(add.IsSuccess(), add.Message);
        return (cart.Id, product);
    }

    [SqlFact]
    public async Task SavedItems_LeaveTheCartAndComeBack()
    {
        var (cartId, productId) = await CartWithProductAsync(quantity: 2);

        await using (var scope = db.Scope())
        {
            var saveForLater = scope.ServiceProvider.GetRequiredService<ISaveForLaterService>();
            var save = await saveForLater.SaveForLaterAsync(cartId, productId);
            Assert.True(save.IsSuccess(), save.Message);
        }

        await using (var scope = db.Scope())
        {
            var cart = await scope.ServiceProvider.GetRequiredService<ICartService>().GetCartByIdAsync(cartId);
            Assert.DoesNotContain(cart!.Items, i => i.ProductId == productId); // not counted in the cart total
            var saved = Assert.Single(await scope.ServiceProvider.GetRequiredService<ISaveForLaterService>().GetAsync(cartId));
            Assert.Equal(productId, saved.ProductId);
            Assert.Equal(2, saved.Quantity);
            Assert.True(saved.IsAvailable);
        }

        await using (var scope = db.Scope())
        {
            var move = await scope.ServiceProvider.GetRequiredService<ISaveForLaterService>().MoveToCartAsync(cartId, productId);
            Assert.True(move.IsSuccess(), move.Message);
        }

        await using (var scope = db.Scope())
        {
            var cart = await scope.ServiceProvider.GetRequiredService<ICartService>().GetCartByIdAsync(cartId);
            Assert.Equal(2, Assert.Single(cart!.Items, i => i.ProductId == productId).Quantity);
            Assert.Empty(await scope.ServiceProvider.GetRequiredService<ISaveForLaterService>().GetAsync(cartId));
        }
    }

    [SqlFact]
    public async Task OutOfStockItems_StayInTheSavedList()
    {
        var (cartId, productId) = await CartWithProductAsync();

        await using (var scope = db.Scope())
            Assert.True((await scope.ServiceProvider.GetRequiredService<ISaveForLaterService>().SaveForLaterAsync(cartId, productId)).IsSuccess());

        await using (var scope = db.Scope())
        {
            var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
            var product = await context.Products.AsTracking().SingleAsync(p => p.Id == productId);
            product.StockQuantity = 0;
            await context.SaveChangesAsync();
        }

        await using (var scope = db.Scope())
        {
            var saveForLater = scope.ServiceProvider.GetRequiredService<ISaveForLaterService>();
            Assert.False((await saveForLater.MoveToCartAsync(cartId, productId)).IsSuccess());
            var saved = Assert.Single(await saveForLater.GetAsync(cartId));
            Assert.False(saved.IsAvailable);
        }
    }

    [SqlFact]
    public async Task OnlyItemsOfTheCartCanBeSaved()
    {
        var (cartId, _) = await CartWithProductAsync();
        var otherProduct = await db.CreateProductAsync();

        await using var scope = db.Scope();
        Assert.False((await scope.ServiceProvider.GetRequiredService<ISaveForLaterService>().SaveForLaterAsync(cartId, otherProduct)).IsSuccess());
        Assert.False((await scope.ServiceProvider.GetRequiredService<ISaveForLaterService>().SaveForLaterAsync(Guid.NewGuid(), otherProduct)).IsSuccess());
    }
}

/// <summary>Cached data must refresh as soon as it changes (no stale settings / search data).</summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", "Integration")]
public class CacheRefreshTests(SqlServerFixture db)
{
    [SqlFact]
    public async Task StoreSettings_AreReloadedAfterAChange()
    {
        decimal original;
        await using (var scope = db.Scope())
            original = (await scope.ServiceProvider.GetRequiredService<IStoreSettingService>().GetAsync()).GiftCardMinAmount;

        await using (var scope = db.Scope())
        {
            var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
            var entity = await context.StoreSettings.AsTracking().FirstAsync();
            entity.GiftCardMinAmount = original + 1_000;
            await context.SaveChangesAsync();
        }

        try
        {
            await using var scope = db.Scope();
            Assert.Equal(original + 1_000, (await scope.ServiceProvider.GetRequiredService<IStoreSettingService>().GetAsync()).GiftCardMinAmount);
        }
        finally
        {
            await using var scope = db.Scope();
            var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
            var entity = await context.StoreSettings.AsTracking().FirstAsync();
            entity.GiftCardMinAmount = original;
            await context.SaveChangesAsync();
        }
    }

    [SqlFact]
    public async Task NewSynonyms_AreUsedImmediately()
    {
        await using (var scope = db.Scope())
        {
            // Warm the synonym cache first.
            var before = await scope.ServiceProvider.GetRequiredService<ISearchInsightsService>().ExpandAsync("zphone");
            Assert.DoesNotContain("zfone", Assert.Single(before));
        }

        await using (var scope = db.Scope())
        {
            var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
            context.SearchSynonyms.Add(new SearchSynonym { Terms = "zphone, زدفون, zfone", IsActive = true });
            await context.SaveChangesAsync();
        }

        await using (var scope = db.Scope())
        {
            var groups = await scope.ServiceProvider.GetRequiredService<ISearchInsightsService>().ExpandAsync("ZPhone");
            var group = Assert.Single(groups);
            Assert.Contains("zphone", group);
            Assert.Contains("zfone", group);
            Assert.Contains("زدفون", group);
        }
    }

    [SqlFact]
    public async Task ProductChanges_BumpTheCatalogVersion()
    {
        var productId = await db.CreateProductAsync();
        var before = CacheInvalidation.CatalogVersion;

        await using var scope = db.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
        var product = await context.Products.AsTracking().SingleAsync(p => p.Id == productId);
        product.Price += 1_000;
        await context.SaveChangesAsync();

        Assert.True(CacheInvalidation.CatalogVersion > before);
    }
}
