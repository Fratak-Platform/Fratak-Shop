using System.Net;
using Fratak_Shop.Data.Entities.Marketing;
using Fratak_Shop.Data.Entities.Orders;
using Fratak_Shop.Data.Entities.Payments;
using Fratak_Shop.Tests.E2E.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Tests.E2E.Shopping;

/// <summary>
/// خرید از اول تا آخر از طریق صفحه‌های واقعی: سبد خرید → تسویه → پرداخت (در محل / کیف پول / زرین‌پال) → جزئیات سفارش،
/// فاکتور و لغو. هر آزمون اثر واقعی را در پایگاه داده بررسی می‌کند (موجودی، مبلغ، وضعیت پرداخت، کیف پول).
/// </summary>
[Collection(E2ECollection.Name)]
[Trait("Category", "E2E")]
[Trait("Area", "Checkout")]
public class CartCheckoutE2ETests(ShopApp app)
{
    private async Task<(TestData.Customer Customer, ShopClient Client, Guid ProductId)> ShopperAsync(decimal wallet = 0, decimal price = 250_000, int stock = 10)
    {
        var customer = await app.CreateCustomerAsync(wallet);
        var (productId, _) = await app.CreateProductAsync(price, stock);
        var client = app.CreateClient();
        await client.LoginAsync(customer.Phone, customer.Password);
        return (customer, client, productId);
    }

    // ── سبد خرید ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Guest_cart_survives_login_and_is_merged_into_the_account()
    {
        var customer = await app.CreateCustomerAsync();
        var (productId, slug) = await app.CreateProductAsync();
        using var client = app.CreateClient();

        await client.AddToCartAsync(productId, 2);
        var cart = await client.GetPageAsync("/cart");
        Assert.Contains(slug, cart.Html, StringComparison.Ordinal);

        using var checkout = await client.GetAsync("/checkout");
        Assert.Equal(HttpStatusCode.Redirect, checkout.StatusCode);
        Assert.StartsWith("/login", checkout.Headers.Location?.OriginalString, StringComparison.OrdinalIgnoreCase);

        await client.LoginAsync(customer.Phone, customer.Password);

        var quantity = await app.WithDbAsync(db => db.CartItems.Where(i => i.Cart.UserId == customer.Id && i.ProductId == productId)
            .Select(i => (int?)i.Quantity).FirstOrDefaultAsync());
        Assert.Equal(2, quantity);
    }

    [Fact]
    public async Task Cart_quantity_can_be_updated_and_removed()
    {
        var (customer, client, productId) = await ShopperAsync();
        using var _ = client;

        await client.AddToCartAsync(productId);
        using (await client.PostFormAsync("/cart?handler=Update", ("productId", productId.ToString()), ("quantity", "3")))
        {
        }
        Assert.Equal(3, await CartQuantityAsync(customer.Id, productId));

        using (await client.PostFormAsync("/cart?handler=Update", ("productId", productId.ToString()), ("quantity", "0")))
        {
        }
        Assert.Null(await CartQuantityAsync(customer.Id, productId));
    }

    [Theory]
    [InlineData("11")]          // بیش از موجودی (۱۰)
    [InlineData("2147483647")]  // int.MaxValue
    [InlineData("-5")]          // منفی
    [InlineData("abc")]         // نامعتبر
    public async Task Cart_never_holds_more_than_the_stock_or_a_negative_quantity(string quantity)
    {
        var (customer, client, productId) = await ShopperAsync(stock: 10);
        using var _ = client;
        await client.AddToCartAsync(productId);

        using (await client.PostFormAsync("/cart?handler=Add", ("productId", productId.ToString()), ("quantity", quantity)))
        {
        }
        using (await client.PostFormAsync("/cart?handler=Update", ("productId", productId.ToString()), ("quantity", quantity)))
        {
        }

        var stored = await CartQuantityAsync(customer.Id, productId);
        Assert.True(stored is null or >= 1 and <= 10, $"cart quantity {stored}");
        await client.GetPageAsync("/cart");
    }

    [Fact]
    public async Task Inactive_or_unknown_products_cannot_be_added()
    {
        var (customer, client, _) = await ShopperAsync();
        using var __ = client;
        var (inactive, _) = await app.CreateProductAsync(active: false);

        using (await client.PostFormAsync("/cart?handler=Add", ("productId", inactive.ToString()), ("quantity", "1")))
        {
        }
        using (await client.PostFormAsync("/cart?handler=Add", ("productId", Guid.NewGuid().ToString()), ("quantity", "1")))
        {
        }

        Assert.Equal(0, await app.WithDbAsync(db => db.CartItems.CountAsync(i => i.Cart.UserId == customer.Id)));
    }

    // ── پرداخت در محل ────────────────────────────────────────────────────

    [Fact]
    public async Task Cash_on_delivery_order_reserves_stock_clears_the_cart_and_shows_everywhere()
    {
        var (customer, client, productId) = await ShopperAsync(price: 300_000, stock: 10);
        using var _ = client;
        await client.AddToCartAsync(productId, 2);

        using var placed = await client.PlaceOrderAsync(PaymentMethod.CashOnDelivery);

        Assert.Equal(HttpStatusCode.Redirect, placed.StatusCode);
        Assert.Contains("/checkout/success/", placed.Headers.Location?.OriginalString, StringComparison.Ordinal);
        var number = TestData.OrderNumberFrom(placed);
        var order = await app.OrderAsync(number);
        Assert.NotNull(order);
        Assert.Equal(customer.Id, order!.UserId);
        Assert.Equal(PaymentMethod.CashOnDelivery, order.PaymentMethod);
        Assert.NotEqual(PaymentStatus.Paid, order.PaymentStatus);
        Assert.Equal(600_000m, order.SubtotalAmount);
        Assert.Equal(order.SubtotalAmount - order.DiscountAmount - order.CouponDiscountAmount + order.ShippingAmount, order.PayableAmount);
        Assert.Equal(8, await app.StockAsync(productId));
        Assert.Equal(0, await app.WithDbAsync(db => db.CartItems.CountAsync(i => i.Cart.UserId == customer.Id)));

        await client.GetPageAsync($"/checkout/success/{number}");
        var orders = await client.GetPageAsync("/profile/orders");
        Assert.Contains(number, orders.Html, StringComparison.OrdinalIgnoreCase);
        var detail = await client.GetPageAsync($"/profile/orders/{number}");
        Assert.Contains("لغو سفارش", detail.Html, StringComparison.Ordinal);
        var invoice = await client.GetPageAsync($"/profile/orders/{number}/invoice");
        Assert.Contains("پیش‌فاکتور", invoice.Html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cancelling_before_preparation_returns_the_stock()
    {
        var (_, client, productId) = await ShopperAsync(stock: 5);
        using var _ = client;
        await client.AddToCartAsync(productId, 2);
        using var placed = await client.PlaceOrderAsync(PaymentMethod.CashOnDelivery);
        var number = TestData.OrderNumberFrom(placed);
        Assert.Equal(3, await app.StockAsync(productId));

        using var cancel = await client.PostFormAsync($"/profile/orders/{number}?handler=Cancel");

        Assert.Equal(HttpStatusCode.Redirect, cancel.StatusCode);
        Assert.Equal(OrderStatus.Cancelled, (await app.OrderAsync(number))!.Status);
        Assert.Equal(5, await app.StockAsync(productId));

        // دوباره لغو کردن چیزی را دوباره برنمی‌گرداند.
        using (await client.PostFormAsync($"/profile/orders/{number}?handler=Cancel"))
        {
        }
        Assert.Equal(5, await app.StockAsync(productId));
    }

    [Fact]
    public async Task Order_in_preparation_cannot_be_cancelled_by_the_customer()
    {
        var (_, client, productId) = await ShopperAsync(stock: 5);
        using var _ = client;
        await client.AddToCartAsync(productId);
        using var placed = await client.PlaceOrderAsync(PaymentMethod.CashOnDelivery);
        var number = TestData.OrderNumberFrom(placed);
        await app.WithDbAsync(db => db.Orders.Where(o => o.OrderNumber == number)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.Status, OrderStatus.Processing)));

        using (await client.PostFormAsync($"/profile/orders/{number}?handler=Cancel"))
        {
        }

        Assert.Equal(OrderStatus.Processing, (await app.OrderAsync(number))!.Status);
        Assert.Equal(4, await app.StockAsync(productId));
    }

    // ── کیف پول ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Wallet_payment_debits_exactly_the_payable_amount_and_refunds_it_on_cancel()
    {
        var (customer, client, productId) = await ShopperAsync(wallet: 2_000_000, price: 400_000);
        using var _ = client;
        await client.AddToCartAsync(productId);

        using var placed = await client.PlaceOrderAsync(PaymentMethod.Wallet);

        Assert.Contains("/checkout/success/", placed.Headers.Location?.OriginalString, StringComparison.Ordinal);
        var number = TestData.OrderNumberFrom(placed);
        var order = (await app.OrderAsync(number))!;
        Assert.Equal(PaymentStatus.Paid, order.PaymentStatus);
        Assert.Equal(2_000_000m - order.PayableAmount, await app.WalletBalanceAsync(customer.Id));
        var invoice = await client.GetPageAsync($"/profile/orders/{number}/invoice");
        Assert.Contains("INV-", invoice.Html, StringComparison.Ordinal);

        using (await client.PostFormAsync($"/profile/orders/{number}?handler=Cancel"))
        {
        }

        Assert.Equal(OrderStatus.Cancelled, (await app.OrderAsync(number))!.Status);
        Assert.Equal(2_000_000m, await app.WalletBalanceAsync(customer.Id));
    }

    [Fact]
    public async Task Wallet_payment_without_enough_balance_creates_no_order()
    {
        var (customer, client, productId) = await ShopperAsync(wallet: 10_000, price: 400_000, stock: 3);
        using var _ = client;
        await client.AddToCartAsync(productId);

        using var placed = await client.PlaceOrderAsync(PaymentMethod.Wallet);

        Assert.Equal(HttpStatusCode.OK, placed.StatusCode);
        Assert.False(await app.WithDbAsync(db => db.Orders.AnyAsync(o => o.UserId == customer.Id)));
        Assert.Equal(3, await app.StockAsync(productId));
        Assert.Equal(10_000m, await app.WalletBalanceAsync(customer.Id));
    }

    // ── درگاه زرین‌پال ───────────────────────────────────────────────────

    [Fact]
    public async Task Online_payment_goes_to_zarinpal_and_the_verified_callback_marks_the_order_paid()
    {
        var (_, client, productId) = await ShopperAsync(price: 520_000);
        using var _ = client;
        await client.AddToCartAsync(productId);

        using var placed = await client.PlaceOrderAsync(PaymentMethod.OnlineGateway);

        var location = placed.Headers.Location!.ToString();
        Assert.StartsWith("https://sandbox.zarinpal.com/pg/StartPay/", location, StringComparison.Ordinal);
        var authority = location[(location.LastIndexOf('/') + 1)..];
        var payment = app.ZarinPal.Find(authority)!;
        var transaction = await app.WithDbAsync(db => db.PaymentTransactions.FirstAsync(t => t.Authority == authority));
        var order = await app.WithDbAsync(db => db.Orders.FirstAsync(o => o.Id == transaction.OrderId));
        Assert.Equal((long)(order.PayableAmount * 10), payment.AmountRial);
        Assert.Equal("https://fratak.test/payment/callback", payment.CallbackUrl);
        Assert.NotEqual(PaymentStatus.Paid, order.PaymentStatus);

        using var callback = await client.GetAsync($"/payment/callback?Authority={authority}&Status=OK");

        Assert.Equal($"/checkout/success/{order.OrderNumber}", callback.Headers.Location?.OriginalString);
        var paid = (await app.OrderAsync(order.OrderNumber))!;
        Assert.Equal(PaymentStatus.Paid, paid.PaymentStatus);
        Assert.Equal(payment.RefId, paid.TransactionId);
        Assert.NotNull(paid.PaidAt);
    }

    [Fact]
    public async Task Cancelled_payment_leaves_the_order_unpaid_and_cannot_be_flipped_to_ok()
    {
        var (_, client, productId) = await ShopperAsync();
        using var _ = client;
        await client.AddToCartAsync(productId);
        using var placed = await client.PlaceOrderAsync(PaymentMethod.OnlineGateway);
        var authority = placed.Headers.Location!.ToString().Split('/')[^1];

        using var cancelled = await client.GetAsync($"/payment/callback?Authority={authority}&Status=NOK");
        Assert.Contains("/checkout/failed/", cancelled.Headers.Location?.OriginalString, StringComparison.Ordinal);

        // حمله: همان authority با Status=OK دوباره فرستاده می‌شود.
        using var flipped = await client.GetAsync($"/payment/callback?Authority={authority}&Status=OK");
        Assert.DoesNotContain("/checkout/success/", flipped.Headers.Location?.OriginalString ?? "", StringComparison.Ordinal);
        var transaction = await app.WithDbAsync(db => db.PaymentTransactions.FirstAsync(t => t.Authority == authority));
        Assert.Equal(PaymentTransactionStatus.Cancelled, transaction.Status);
        Assert.NotEqual(PaymentStatus.Paid, await app.WithDbAsync(db => db.Orders.Where(o => o.Id == transaction.OrderId).Select(o => o.PaymentStatus).FirstAsync()));
    }

    [Fact]
    public async Task Gateway_rejecting_the_verification_never_marks_the_order_paid()
    {
        var (_, client, productId) = await ShopperAsync();
        using var _ = client;
        await client.AddToCartAsync(productId);
        using var placed = await client.PlaceOrderAsync(PaymentMethod.OnlineGateway);
        var authority = placed.Headers.Location!.ToString().Split('/')[^1];

        app.ZarinPal.VerifyErrorCode = -51; // پرداخت ناموفق از نظر درگاه
        try
        {
            using var callback = await client.GetAsync($"/payment/callback?Authority={authority}&Status=OK");
            Assert.Contains("/checkout/failed/", callback.Headers.Location?.OriginalString, StringComparison.Ordinal);
        }
        finally
        {
            app.ZarinPal.Reset();
        }

        var transaction = await app.WithDbAsync(db => db.PaymentTransactions.FirstAsync(t => t.Authority == authority));
        Assert.Equal(PaymentTransactionStatus.Failed, transaction.Status);
        Assert.NotEqual(PaymentStatus.Paid, await app.WithDbAsync(db => db.Orders.Where(o => o.Id == transaction.OrderId).Select(o => o.PaymentStatus).FirstAsync()));
    }

    [Fact]
    public async Task Unreachable_gateway_fails_gracefully_and_keeps_the_order_payable_later()
    {
        var (customer, client, productId) = await ShopperAsync();
        using var _ = client;
        await client.AddToCartAsync(productId);

        app.ZarinPal.Offline = true;
        HttpResponseMessage placed;
        try
        {
            placed = await client.PlaceOrderAsync(PaymentMethod.OnlineGateway);
        }
        finally
        {
            app.ZarinPal.Reset();
        }

        using (placed)
        {
            Assert.Contains("/checkout/failed/", placed.Headers.Location?.OriginalString, StringComparison.Ordinal);
            var number = TestData.OrderNumberFrom(placed);
            await client.GetPageAsync($"/checkout/failed/{number}");
            var order = (await app.OrderAsync(number))!;
            Assert.Equal(customer.Id, order.UserId);
            Assert.NotEqual(PaymentStatus.Paid, order.PaymentStatus);

            // درگاه برگشت: پرداخت دوباره از صفحهٔ سفارش به زرین‌پال می‌رود.
            using var retry = await client.PostFormAsync($"/payment/start/{number}");
            Assert.Equal(HttpStatusCode.Redirect, retry.StatusCode);
            Assert.StartsWith("https://sandbox.zarinpal.com/pg/StartPay/", retry.Headers.Location?.ToString(), StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Forged_callback_with_an_unknown_authority_changes_nothing()
    {
        using var client = app.CreateClient();
        var paidBefore = await app.WithDbAsync(db => db.Orders.CountAsync(o => o.PaymentStatus == PaymentStatus.Paid));

        using var forged = await client.GetAsync("/payment/callback?Authority=A000000000000000000000000000forged&Status=OK");

        Assert.Equal(HttpStatusCode.Redirect, forged.StatusCode);
        Assert.DoesNotContain("/checkout/success/", forged.Headers.Location?.OriginalString ?? "", StringComparison.Ordinal);
        Assert.Equal(paidBefore, await app.WithDbAsync(db => db.Orders.CountAsync(o => o.PaymentStatus == PaymentStatus.Paid)));
    }

    [Fact]
    public async Task Replaying_a_successful_callback_does_not_verify_or_credit_twice()
    {
        var (customer, client, productId) = await ShopperAsync();
        using var _ = client;
        await client.AddToCartAsync(productId);
        using var placed = await client.PlaceOrderAsync(PaymentMethod.OnlineGateway);
        var authority = placed.Headers.Location!.ToString().Split('/')[^1];
        using (await client.GetAsync($"/payment/callback?Authority={authority}&Status=OK"))
        {
        }
        var verifies = app.ZarinPal.VerifyCalls;
        var wallet = await app.WalletBalanceAsync(customer.Id);

        for (var i = 0; i < 3; i++)
        {
            using var replay = await client.GetAsync($"/payment/callback?Authority={authority}&Status=OK");
            Assert.Contains("/checkout/success/", replay.Headers.Location?.OriginalString, StringComparison.Ordinal);
        }

        Assert.Equal(verifies, app.ZarinPal.VerifyCalls);
        Assert.Equal(wallet, await app.WalletBalanceAsync(customer.Id));
        Assert.Equal(1, await app.WithDbAsync(db => db.PaymentTransactions.CountAsync(t => t.Authority == authority && t.Status == PaymentTransactionStatus.Succeeded)));
    }

    [Fact]
    public async Task Wallet_top_up_through_the_gateway_credits_the_wallet_once()
    {
        var customer = await app.CreateCustomerAsync();
        using var client = app.CreateClient();
        await client.LoginAsync(customer.Phone, customer.Password);

        using var start = await client.PostFormAsync("/profile/wallet?handler=TopUp", ("TopUp.Amount", "150000"));
        var location = start.Headers.Location?.ToString() ?? "";
        Assert.StartsWith("https://sandbox.zarinpal.com/pg/StartPay/", location, StringComparison.Ordinal);
        var authority = location.Split('/')[^1];
        Assert.Equal(1_500_000, app.ZarinPal.Find(authority)!.AmountRial);

        using (await client.GetAsync($"/payment/callback?Authority={authority}&Status=OK"))
        {
        }
        using (await client.GetAsync($"/payment/callback?Authority={authority}&Status=OK"))
        {
        }

        Assert.Equal(150_000m, await app.WalletBalanceAsync(customer.Id));
    }

    // ── دستکاری فرم تسویه ────────────────────────────────────────────────

    [Fact]
    public async Task Posted_prices_are_ignored_and_the_server_recomputes_the_total()
    {
        var (_, client, productId) = await ShopperAsync(price: 700_000);
        using var _ = client;
        await client.AddToCartAsync(productId);

        using var placed = await client.PlaceOrderAsync(PaymentMethod.CashOnDelivery,
            ("Input.PayableAmount", "1"), ("PayableAmount", "1"), ("Input.ShippingCost", "0"), ("Input.Discount", "699999"), ("Price", "1"));

        var order = (await app.OrderAsync(TestData.OrderNumberFrom(placed)))!;
        Assert.Equal(700_000m, order.SubtotalAmount);
        Assert.True(order.PayableAmount >= 700_000m);
    }

    [Fact]
    public async Task Another_customers_address_cannot_be_used()
    {
        var victim = await app.CreateCustomerAsync();
        var (customer, client, productId) = await ShopperAsync();
        using var _ = client;
        await client.AddToCartAsync(productId);

        using var placed = await client.PlaceOrderAsync(PaymentMethod.CashOnDelivery, ("Input.AddressId", victim.AddressId.ToString()));

        Assert.Equal(HttpStatusCode.OK, placed.StatusCode);
        Assert.False(await app.WithDbAsync(db => db.Orders.AnyAsync(o => o.UserId == customer.Id)));
    }

    [Theory]
    [InlineData("99")]
    [InlineData("-1")]
    [InlineData("None")]
    [InlineData("Free")]
    public async Task Undefined_payment_methods_are_refused(string method)
    {
        var (customer, client, productId) = await ShopperAsync();
        using var _ = client;
        await client.AddToCartAsync(productId);

        using var placed = await client.PlaceOrderAsync(PaymentMethod.CashOnDelivery, ("Input.PaymentMethod", method));

        Assert.NotEqual(HttpStatusCode.InternalServerError, placed.StatusCode);
        Assert.False(await app.WithDbAsync(db => db.Orders.AnyAsync(o => o.UserId == customer.Id)));
    }

    [Fact]
    public async Task Disabled_online_payment_cannot_be_forced_through_the_form()
    {
        var (customer, client, productId) = await ShopperAsync();
        using var _ = client;
        await client.AddToCartAsync(productId);
        var page = await client.GetPageAsync("/checkout");
        var fields = TestData.CheckoutFields(page, PaymentMethod.CashOnDelivery);
        var restore = await app.ChangeSettingsAsync(s => s.IsOnlinePaymentEnabled = false);
        try
        {
            using var placed = await client.PostFormAsync("/checkout?handler=PlaceOrder",
                fields.Where(f => f.Key != "Input.PaymentMethod").Append(("Input.PaymentMethod", "OnlineGateway")).ToArray());

            Assert.False(await app.WithDbAsync(db => db.Orders.AnyAsync(o => o.UserId == customer.Id && o.PaymentMethod == PaymentMethod.OnlineGateway)));
        }
        finally
        {
            await restore();
        }
    }

    [Fact]
    public async Task Stock_that_ran_out_after_adding_to_the_cart_blocks_the_order()
    {
        var (customer, client, productId) = await ShopperAsync(stock: 2);
        using var _ = client;
        await client.AddToCartAsync(productId, 2);
        var page = await client.GetPageAsync("/checkout");
        var fields = TestData.CheckoutFields(page, PaymentMethod.CashOnDelivery);

        // در همین فاصله کالا توسط دیگران خریده شد.
        await app.WithDbAsync(db => db.Products.Where(p => p.Id == productId).ExecuteUpdateAsync(s => s.SetProperty(p => p.StockQuantity, 1)));
        using var placed = await client.PostFormAsync("/checkout?handler=PlaceOrder", fields.ToArray());

        Assert.False(await app.WithDbAsync(db => db.Orders.AnyAsync(o => o.UserId == customer.Id)));
        Assert.Equal(1, await app.StockAsync(productId));
    }

    [Fact]
    public async Task Empty_cart_cannot_place_an_order()
    {
        var customer = await app.CreateCustomerAsync();
        using var client = app.CreateClient();
        await client.LoginAsync(customer.Phone, customer.Password);

        using var checkout = await client.GetAsync("/checkout");
        Assert.Equal(HttpStatusCode.Redirect, checkout.StatusCode);
        using var placed = await client.PostFormAsync("/checkout?handler=PlaceOrder",
            ("Input.AddressId", customer.AddressId.ToString()), ("Input.ShippingMethodId", Guid.NewGuid().ToString()), ("Input.PaymentMethod", "CashOnDelivery"));

        Assert.Equal(HttpStatusCode.Redirect, placed.StatusCode);
        Assert.False(await app.WithDbAsync(db => db.Orders.AnyAsync(o => o.UserId == customer.Id)));
    }

    // ── کد تخفیف ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Coupon_discount_is_applied_once_and_expired_coupons_are_refused()
    {
        var code = "E2E" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        await app.WithDbAsync(async db =>
        {
            db.Coupons.Add(new Coupon { Code = code, Title = "تخفیف آزمون", Type = CouponType.FixedAmount, Value = 50_000, IsActive = true, UsageLimitPerUser = 1 });
            db.Coupons.Add(new Coupon { Code = code + "X", Title = "منقضی", Type = CouponType.FixedAmount, Value = 50_000, IsActive = true, EndsAt = DateTimeOffset.UtcNow.AddDays(-1) });
            await db.SaveChangesAsync();
        });
        var (customer, client, productId) = await ShopperAsync(price: 500_000, stock: 10);
        using var _ = client;
        await client.AddToCartAsync(productId);

        using (await client.PostFormAsync("/cart?handler=Coupon", ("code", code + "X"), ("returnUrl", "/cart")))
        {
        }
        Assert.Null(await app.WithDbAsync(db => db.Carts.Where(c => c.UserId == customer.Id).Select(c => c.CouponCode).FirstOrDefaultAsync()));

        using (await client.PostFormAsync("/cart?handler=Coupon", ("code", code.ToLowerInvariant()), ("returnUrl", "/cart")))
        {
        }
        using var placed = await client.PlaceOrderAsync(PaymentMethod.CashOnDelivery);
        var order = (await app.OrderAsync(TestData.OrderNumberFrom(placed)))!;
        Assert.Equal(code, order.CouponCode);
        Assert.Equal(50_000m, order.CouponDiscountAmount);

        // بار دوم (محدودیت یک بار برای هر کاربر) تخفیفی داده نمی‌شود.
        await client.AddToCartAsync(productId);
        using (await client.PostFormAsync("/cart?handler=Coupon", ("code", code), ("returnUrl", "/cart")))
        {
        }
        using var second = await client.PlaceOrderAsync(PaymentMethod.CashOnDelivery);
        var orders = await app.WithDbAsync(db => db.Orders.Where(o => o.UserId == customer.Id && o.CouponCode == code).CountAsync());
        Assert.Equal(1, orders);
    }

    // ── پیگیری سفارش مهمان ────────────────────────────────────────────────

    [Fact]
    public async Task Order_tracking_needs_the_matching_phone_number()
    {
        var (customer, client, productId) = await ShopperAsync();
        using var _ = client;
        await client.AddToCartAsync(productId);
        using var placed = await client.PlaceOrderAsync(PaymentMethod.CashOnDelivery);
        var number = TestData.OrderNumberFrom(placed);

        using var guest = app.CreateClient();
        using var right = await guest.PostFormAsync("/track-order", ("Input.OrderNumber", number.ToUpperInvariant()), ("Input.PhoneNumber", customer.Phone));
        Assert.Contains(number, await right.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);

        using var stranger = app.CreateClient();
        using var wrong = await stranger.PostFormAsync("/track-order", ("Input.OrderNumber", number.ToUpperInvariant()), ("Input.PhoneNumber", ShopApp.NewPhone()));
        var html = await wrong.Content.ReadAsStringAsync();
        Assert.DoesNotContain(customer.Phone, html, StringComparison.Ordinal);
        Assert.DoesNotContain("خیابان آزادی", html, StringComparison.Ordinal);
    }

    private Task<int?> CartQuantityAsync(Guid userId, Guid productId) =>
        app.WithDbAsync(db => db.CartItems.Where(i => i.Cart.UserId == userId && i.ProductId == productId).Select(i => (int?)i.Quantity).FirstOrDefaultAsync());
}
