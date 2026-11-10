using System.Net;
using Fratak_Shop.Data.Entities.Orders;
using Fratak_Shop.Tests.E2E.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Tests.E2E.Storefront;

/// <summary>
/// تست سرتاسری دور ۲۴ بستهٔ D روی برنامهٔ واقعی (پنل ادمین):
/// <list type="bullet">
/// <item>۶۸ داشبورد آماده‌سازی: جمع تعداد یک کالا در چند سفارش، شمارهٔ سفارش‌ها و خروجی CSV؛ مشتری و مهمان دسترسی ندارند.</item>
/// <item>۵۵ نشان ریسک روی جزئیات و فهرست سفارش (سفارش پرداخت در محلِ مشتری تازه با مبلغ بالا).</item>
/// <item>۵۷ کارت «احتمال ریزش» در جزئیات مشتری؛ ۵۱/۶۷ زبانهٔ پیش‌نویس سفارش خرید و خروجی آن؛ ۵۴ صفحهٔ دیدگاه‌ها؛ تنظیم پیام برگشت.</item>
/// </list>
/// </summary>
[Collection(E2ECollection.Name)]
[Trait("Category", "E2E")]
public class MlRound24E2ETests(ShopApp app)
{
    private static Order NewOrder(Guid? userId, OrderStatus status, decimal amount, PaymentMethod method = PaymentMethod.OnlineGateway) => new()
    {
        UserId = userId,
        OrderNumber = "MLE" + Random.Shared.NextInt64(100_000_000, 999_999_999),
        Status = status,
        PaymentMethod = method,
        PaymentStatus = method == PaymentMethod.CashOnDelivery ? PaymentStatus.Pending : PaymentStatus.Paid,
        SubtotalAmount = amount,
        PayableAmount = amount,
        CustomerName = "مشتری آزمون",
        CustomerPhone = "09120000000",
        ShippingProvince = "تهران",
        ShippingCity = "تهران",
        ShippingAddress = "خیابان آزمون",
        ShippingPostalCode = Random.Shared.NextInt64(1_000_000_000, 9_999_999_999).ToString(),
        ShippingPhone = "09120000000",
    };

    private Task DeleteOrdersAsync(List<Guid> ids) => app.WithDbAsync(async db =>
    {
        await db.OrderItems.IgnoreQueryFilters().Where(i => ids.Contains(i.OrderId)).ExecuteDeleteAsync();
        await db.Orders.IgnoreQueryFilters().Where(o => ids.Contains(o.Id)).ExecuteDeleteAsync();
    });

    [SqlFact]
    public async Task Prep_dashboard_aggregates_pick_list_and_exports()
    {
        var (productId, _) = await app.CreateProductAsync(stock: 1);
        var sku = await app.WithDbAsync(db => db.Products.Where(p => p.Id == productId).Select(p => p.SKU).SingleAsync());
        var ids = new List<Guid>();
        var numbers = new List<string>();
        try
        {
            await app.WithDbAsync(async db =>
            {
                foreach (var (status, quantity) in new[] { (OrderStatus.Confirmed, 2), (OrderStatus.Processing, 3), (OrderStatus.Delivered, 7) })
                {
                    var order = NewOrder(null, status, 100_000 * quantity);
                    order.Items.Add(new OrderItem { ProductId = productId, ProductName = "کالای برداشت", SKU = sku, UnitPrice = 100_000, Quantity = quantity, TotalAmount = 100_000 * quantity });
                    db.Orders.Add(order);
                    await db.SaveChangesAsync();
                    ids.Add(order.Id);
                    numbers.Add(order.OrderNumber);
                }
            });

            using var client = app.CreateClient();
            await client.LoginAsync(ShopApp.OwnerPhone);
            var page = await client.GetPageAsync("/admin/orders/prep");
            var row = page.Document.QuerySelectorAll("tbody tr").FirstOrDefault(r => r.TextContent.Contains(sku, StringComparison.Ordinal));
            Assert.NotNull(row);
            Assert.Contains("(2 سفارش)", row!.TextContent);
            Assert.Contains(">5<", row.InnerHtml);
            Assert.Contains(numbers[0], row.TextContent);
            Assert.Contains(numbers[1], row.TextContent);
            Assert.DoesNotContain(numbers[2], row.TextContent); // تحویل‌شده در فهرست برداشت نیست
            Assert.NotNull(page.One("[data-print]"));

            var confirmedOnly = await client.GetPageAsync("/admin/orders/prep?stage=confirmed");
            var confirmedRow = confirmedOnly.Document.QuerySelectorAll("tbody tr").First(r => r.TextContent.Contains(sku, StringComparison.Ordinal));
            Assert.Contains(">2<", confirmedRow.InnerHtml);

            // مرحلهٔ نامعتبر ← «همه» (بدون خطا).
            await client.GetPageAsync("/admin/orders/prep?stage=<script>");

            using var csv = await client.Http.GetAsync("/admin/orders/prep?handler=Export&format=csv");
            Assert.Equal(HttpStatusCode.OK, csv.StatusCode);
            var text = await csv.Content.ReadAsStringAsync();
            Assert.Contains(sku, text);
            Assert.Contains(numbers[0], text);
        }
        finally
        {
            await DeleteOrdersAsync(ids);
        }
    }

    [SqlFact]
    public async Task Prep_dashboard_is_staff_only()
    {
        using var customer = app.CreateClient();
        await customer.LoginAsync(ShopApp.DemoCustomerPhone);
        using var asCustomer = await customer.Http.GetAsync("/admin/orders/prep?handler=Export&format=csv");
        Assert.NotEqual(HttpStatusCode.OK, asCustomer.StatusCode);
        using var guest = app.CreateClient();
        using var asGuest = await guest.Http.GetAsync("/admin/orders/prep");
        Assert.NotEqual(HttpStatusCode.OK, asGuest.StatusCode);
    }

    [SqlFact]
    public async Task Risky_order_shows_risk_badge_on_detail_and_list()
    {
        var customer = await app.CreateCustomerAsync();
        var ids = new List<Guid>();
        try
        {
            var orderId = await app.WithDbAsync(async db =>
            {
                var order = NewOrder(customer.Id, OrderStatus.Pending, 9_000_000, PaymentMethod.CashOnDelivery);
                db.Orders.Add(order);
                await db.SaveChangesAsync();
                return order.Id;
            });
            ids.Add(orderId);

            using var client = app.CreateClient();
            await client.LoginAsync(ShopApp.OwnerPhone);
            var detail = await client.GetPageAsync($"/admin/orders/{orderId}");
            Assert.Contains("ریسک", detail.Html);
            Assert.Contains("پرداخت در محل", detail.Html);

            var list = await client.GetPageAsync("/admin/orders");
            Assert.Contains("ریسک", list.Html);
        }
        finally
        {
            await DeleteOrdersAsync(ids);
        }
    }

    [SqlFact]
    public async Task Customer_detail_shows_churn_and_ml_admin_pages_load()
    {
        var customer = await app.CreateCustomerAsync();
        var ids = new List<Guid>();
        try
        {
            await app.WithDbAsync(async db =>
            {
                var now = DateTimeOffset.UtcNow;
                for (var i = 0; i < 3; i++)
                {
                    var order = NewOrder(customer.Id, OrderStatus.Delivered, 500_000);
                    db.Orders.Add(order);
                    await db.SaveChangesAsync();
                    ids.Add(order.Id);
                    var at = now.AddDays(-150 - 30 * i);
                    await db.Orders.Where(o => o.Id == order.Id).ExecuteUpdateAsync(s => s.SetProperty(o => o.CreatedAt, at));
                }
            });

            using var client = app.CreateClient();
            await client.LoginAsync(ShopApp.OwnerPhone);
            var detail = await client.GetPageAsync($"/admin/customers/{customer.Id}");
            Assert.Contains("احتمال ریزش", detail.Html);

            var po = await client.GetPageAsync("/admin/products/inventory?tab=po");
            Assert.Contains("پیش‌نویس سفارش خرید", po.Html);
            using var poExport = await client.Http.GetAsync("/admin/products/inventory?handler=Purchase&format=csv");
            Assert.Equal(HttpStatusCode.OK, poExport.StatusCode);

            await client.GetPageAsync("/admin/products/inventory");
            await client.GetPageAsync("/admin/comments");
            var settings = await client.GetPageAsync("/admin/smart/settings");
            Assert.NotNull(settings.One("input[name='Options.WinBackEnabled']"));
        }
        finally
        {
            await DeleteOrdersAsync(ids);
        }
    }
}
