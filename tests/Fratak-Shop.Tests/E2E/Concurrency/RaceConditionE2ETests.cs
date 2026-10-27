using Fratak_Shop.Data.Entities.Orders;
using Fratak_Shop.Tests.E2E.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Tests.E2E.Concurrency;

/// <summary>
/// حمله‌های هم‌زمانی (Race condition): مهاجم یک درخواست را چند بار در یک لحظه می‌فرستد تا از فاصلهٔ بین «بررسی» و
/// «ثبت» سوءاستفاده کند. انتظار: آخرین کالا فقط یک بار فروخته می‌شود، کیف پول هرگز منفی یا دو بار شارژ نمی‌شود و
/// تأیید پرداخت فقط یک بار انجام می‌شود — حتی با ۸ درخواست موازی روی SQL Server واقعی.
/// </summary>
[Collection(E2ECollection.Name)]
[Trait("Category", "E2E")]
[Trait("Area", "Concurrency")]
public class RaceConditionE2ETests(ShopApp app)
{
    private const int Parallel = 8;

    [SqlFact]
    public async Task Parallel_gateway_callbacks_credit_a_top_up_exactly_once()
    {
        var customer = await app.CreateCustomerAsync();
        using var client = app.CreateClient();
        await client.LoginAsync(customer.Phone, customer.Password);
        using var start = await client.PostFormAsync("/profile/wallet?handler=TopUp", ("TopUp.Amount", "200000"));
        var authority = start.Headers.Location!.ToString().Split('/')[^1];
        var verifiesBefore = app.ZarinPal.VerifyCalls;

        var callbacks = Enumerable.Range(0, Parallel).Select(async i =>
        {
            using var attacker = app.CreateClient(ip: $"10.240.0.{i + 10}");
            using var response = await attacker.GetAsync($"/payment/callback?Authority={authority}&Status=OK");
            return (int)response.StatusCode;
        });
        var statuses = await Task.WhenAll(callbacks);

        Assert.All(statuses, s => Assert.True(s < 500, $"status {s}"));
        Assert.Equal(200_000m, await app.WalletBalanceAsync(customer.Id));
        Assert.Equal(1, app.ZarinPal.VerifyCalls - verifiesBefore);
    }

    [SqlFact]
    public async Task Parallel_callbacks_mark_an_order_paid_once_with_one_reference()
    {
        var customer = await app.CreateCustomerAsync();
        var (productId, _) = await app.CreateProductAsync(price: 310_000);
        using var client = app.CreateClient();
        await client.LoginAsync(customer.Phone, customer.Password);
        await client.AddToCartAsync(productId);
        using var placed = await client.PlaceOrderAsync(PaymentMethod.OnlineGateway);
        var authority = placed.Headers.Location!.ToString().Split('/')[^1];
        var verifiesBefore = app.ZarinPal.VerifyCalls;

        await Task.WhenAll(Enumerable.Range(0, Parallel).Select(async _ =>
        {
            using var response = await client.GetAsync($"/payment/callback?Authority={authority}&Status=OK");
        }));

        var payment = app.ZarinPal.Find(authority)!;
        Assert.Equal(1, app.ZarinPal.VerifyCalls - verifiesBefore);
        var orderId = await app.WithDbAsync(db => db.PaymentTransactions.Where(t => t.Authority == authority).Select(t => t.OrderId).FirstAsync());
        var order = await app.WithDbAsync(db => db.Orders.FirstAsync(o => o.Id == orderId));
        Assert.Equal(PaymentStatus.Paid, order.PaymentStatus);
        Assert.Equal(payment.RefId, order.TransactionId);
    }

    [SqlFact]
    public async Task The_last_item_is_sold_only_once_when_buyers_race()
    {
        var (productId, _) = await app.CreateProductAsync(price: 120_000, stock: 1);
        var buyers = new List<(TestData.Customer Customer, ShopClient Client)>();
        for (var i = 0; i < 4; i++)
        {
            var customer = await app.CreateCustomerAsync(wallet: 1_000_000);
            var client = app.CreateClient();
            await client.LoginAsync(customer.Phone, customer.Password);
            await client.AddToCartAsync(productId);
            buyers.Add((customer, client));
        }

        try
        {
            await Task.WhenAll(buyers.Select(async b =>
            {
                using var response = await b.Client.PlaceOrderAsync(PaymentMethod.Wallet);
            }));

            Assert.Equal(0, await app.StockAsync(productId));
            var orders = await app.WithDbAsync(db => db.Orders.Where(o => o.Items.Any(i => i.ProductId == productId) && o.Status != OrderStatus.Cancelled).CountAsync());
            Assert.Equal(1, orders);
            var balances = new List<decimal>();
            foreach (var (customer, _) in buyers)
                balances.Add(await app.WalletBalanceAsync(customer.Id));
            Assert.Equal(3, balances.Count(b => b == 1_000_000m));
            Assert.All(balances, b => Assert.True(b >= 0));
        }
        finally
        {
            foreach (var (_, client) in buyers)
                client.Dispose();
        }
    }

    [SqlFact]
    public async Task Parallel_wallet_orders_never_overdraw_the_wallet()
    {
        var customer = await app.CreateCustomerAsync(wallet: 500_000);
        var (productId, _) = await app.CreateProductAsync(price: 300_000, stock: 20);
        var clients = new List<ShopClient>();
        for (var i = 0; i < 4; i++)
        {
            var client = app.CreateClient(ip: $"10.241.0.{i + 10}");
            await client.LoginAsync(customer.Phone, customer.Password);
            clients.Add(client);
        }
        await clients[0].AddToCartAsync(productId);

        try
        {
            await Task.WhenAll(clients.Select(async c =>
            {
                using var response = await c.PlaceOrderAsync(PaymentMethod.Wallet);
            }));

            var balance = await app.WalletBalanceAsync(customer.Id);
            Assert.True(balance >= 0, $"wallet went negative: {balance}");
            var paid = await app.WithDbAsync(db => db.Orders.Where(o => o.UserId == customer.Id && o.PaymentStatus == PaymentStatus.Paid).SumAsync(o => o.PayableAmount));
            Assert.Equal(500_000m - balance, paid);
            Assert.True(await app.StockAsync(productId) >= 19);
        }
        finally
        {
            foreach (var client in clients)
                client.Dispose();
        }
    }

    [SqlFact]
    public async Task Parallel_cart_additions_never_exceed_the_stock()
    {
        var (productId, _) = await app.CreateProductAsync(stock: 3);
        var customer = await app.CreateCustomerAsync();
        using var client = app.CreateClient();
        await client.LoginAsync(customer.Phone, customer.Password);
        await client.AddToCartAsync(productId);

        await Task.WhenAll(Enumerable.Range(0, Parallel).Select(async _ =>
        {
            using var response = await client.PostFormAsync("/cart?handler=Add", ("productId", productId.ToString()), ("quantity", "1"));
        }));

        var quantity = await app.WithDbAsync(db => db.CartItems.Where(i => i.ProductId == productId).SumAsync(i => (int?)i.Quantity)) ?? 0;
        Assert.True(quantity <= 3, $"cart holds {quantity} of a product with stock 3");
    }
}
