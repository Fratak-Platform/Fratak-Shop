using Fratak_Shop.Core.DTOs.Engagement;
using Fratak_Shop.Core.Services.Insights;
using Fratak_Shop.Core.Services.Marketing;
using Fratak_Shop.Core.Services.Orders;
using Fratak_Shop.Core.Services.Products;
using Fratak_Shop.Core.Services.Smart;
using Fratak_Shop.Core.Services.Wallets;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Orders;
using Fratak_Shop.Data.Entities.Products;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Fratak_Shop.Tests.Integration;

/// <summary>
/// تست یکپارچهٔ دور ۲۴ بستهٔ D روی SQL Server واقعی:
/// <list type="bullet">
/// <item>۶۶ چرخهٔ کامل مرجوعی: ثبت ← تأیید ← دریافت (برگشت موجودی) ← بازپرداخت به کیف پول؛ بازپرداخت دوباره و ثبت دوباره ممنوع.</item>
/// <item>۵۱/۶۷ پیش‌بینی موجودی و پیش‌نویس سفارش خرید به تفکیک تأمین‌کننده.</item>
/// <item>۵۵ ریسک سفارش: سفارش مشتری تازه با پرداخت در محل و مبلغ بالا از سفارش مشتری قدیمی پرخطرتر است.</item>
/// <item>۵۷ ریزش مشتری: بینش پنل و پیام برگشت (خاموش ← هیچ؛ روشن ← کوپن شخصی سقف‌دار، یک‌بار در دورهٔ استراحت).</item>
/// <item>۵۶ معیارهای روزانهٔ ناهنجاری: ۵ معیار، ۶۰ روز سابقه و روز هفته هم‌طول.</item>
/// </list>
/// هر آزمون دادهٔ خودش را می‌سازد و در finally پاک می‌کند.
/// </summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", "Integration")]
[Trait("Area", "ML")]
public class MlRound24IntegrationTests(SqlServerFixture db)
{
    private static Order NewOrder(Guid? userId, OrderStatus status, decimal amount, PaymentMethod method = PaymentMethod.OnlineGateway) => new()
    {
        UserId = userId,
        OrderNumber = "ML" + Random.Shared.NextInt64(100_000_000, 999_999_999),
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
        // کد پستی یکتا تا «چند حساب با یک نشانی» از دادهٔ آزمون‌های دیگر اثر نگیرد.
        ShippingPostalCode = Random.Shared.NextInt64(1_000_000_000, 9_999_999_999).ToString(),
        ShippingPhone = "09120000000",
    };

    private static async Task CleanupAsync(FratakShopDbContext context, List<Guid> orderIds, Guid? userId, params Guid[] productIds)
    {
        context.ChangeTracker.Clear();
        if (orderIds.Count > 0)
        {
            await context.ReturnRequestItems.IgnoreQueryFilters().Where(i => orderIds.Contains(i.ReturnRequest.OrderId)).ExecuteDeleteAsync();
            await context.ReturnRequests.IgnoreQueryFilters().Where(r => orderIds.Contains(r.OrderId)).ExecuteDeleteAsync();
            await context.OrderItems.IgnoreQueryFilters().Where(i => orderIds.Contains(i.OrderId)).ExecuteDeleteAsync();
            await context.Orders.IgnoreQueryFilters().Where(o => orderIds.Contains(o.Id)).ExecuteDeleteAsync();
        }
        if (userId is { } uid)
        {
            await context.WalletTransactions.IgnoreQueryFilters().Where(t => t.UserId == uid).ExecuteDeleteAsync();
            await context.Coupons.IgnoreQueryFilters().Where(c => c.UserId == uid).ExecuteDeleteAsync();
            await context.Notifications.IgnoreQueryFilters().Where(n => n.UserId == uid).ExecuteDeleteAsync();
        }
        if (productIds.Length > 0)
            await context.StockMovements.IgnoreQueryFilters().Where(m => productIds.Contains(m.ProductId)).ExecuteDeleteAsync();
    }

    [SqlFact]
    public async Task Return_flow_create_approve_receive_refund_to_wallet()
    {
        var userId = await db.CreateUserAsync();
        var productId = await db.CreateProductAsync(300_000, 4);
        var orders = new List<Guid>();
        await using var scope = db.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
        var returns = scope.ServiceProvider.GetRequiredService<IReturnService>();
        var wallet = scope.ServiceProvider.GetRequiredService<IWalletService>();
        try
        {
            var order = NewOrder(userId, OrderStatus.Delivered, 600_000);
            order.Items.Add(new OrderItem { ProductId = productId, ProductName = "کالای مرجوعی", SKU = "R-1", UnitPrice = 300_000, Quantity = 2, TotalAmount = 600_000 });
            context.Orders.Add(order);
            await context.SaveChangesAsync();
            orders.Add(order.Id);
            var now = DateTimeOffset.UtcNow;
            await context.Orders.Where(o => o.Id == order.Id).ExecuteUpdateAsync(s => s.SetProperty(o => o.DeliveredAt, now.AddDays(-1)).SetProperty(o => o.PaidAt, now.AddDays(-3)));
            context.ChangeTracker.Clear();

            var eligibility = await returns.GetEligibilityAsync(userId, order.OrderNumber.ToLowerInvariant());
            Assert.True(eligibility.IsEligible, eligibility.Reason);
            var item = Assert.Single(eligibility.Items);
            Assert.Equal(300_000m, item.UnitRefundAmount);

            var reasons = await returns.GetReasonsAsync();
            var noImage = reasons.First(r => !r.RequiresImage);
            var withImage = reasons.FirstOrDefault(r => r.RequiresImage);

            // منفی: بیش از تعداد خرید، بدون کالا، علت نامعتبر، علت نیازمند عکس بدون عکس، سفارش کاربر دیگر.
            Assert.False((await returns.CreateAsync(new CreateReturnCommand { UserId = userId, OrderNumber = order.OrderNumber, ReasonId = noImage.Id, Quantities = { [item.OrderItemId] = 3 } })).IsSuccess());
            Assert.False((await returns.CreateAsync(new CreateReturnCommand { UserId = userId, OrderNumber = order.OrderNumber, ReasonId = noImage.Id })).IsSuccess());
            Assert.False((await returns.CreateAsync(new CreateReturnCommand { UserId = userId, OrderNumber = order.OrderNumber, ReasonId = Guid.NewGuid(), Quantities = { [item.OrderItemId] = 1 } })).IsSuccess());
            if (withImage is not null)
                Assert.False((await returns.CreateAsync(new CreateReturnCommand { UserId = userId, OrderNumber = order.OrderNumber, ReasonId = withImage.Id, Quantities = { [item.OrderItemId] = 1 } })).IsSuccess());
            Assert.False((await returns.CreateAsync(new CreateReturnCommand { UserId = Guid.NewGuid(), OrderNumber = order.OrderNumber, ReasonId = noImage.Id, Quantities = { [item.OrderItemId] = 1 } })).IsSuccess());

            var created = await returns.CreateAsync(new CreateReturnCommand
            {
                UserId = userId, OrderNumber = order.OrderNumber, ReasonId = noImage.Id, Description = "<b>ناقص</b> بود", Quantities = { [item.OrderItemId] = 2 },
            });
            Assert.True(created.IsSuccess(), created.Message);
            var request = await context.ReturnRequests.AsNoTracking().SingleAsync(r => r.OrderId == order.Id);
            Assert.Equal(600_000m, request.RequestedAmount);

            // همه اقلام در درخواست باز ← درخواست دوم ممکن نیست.
            Assert.False((await returns.GetEligibilityAsync(userId, order.OrderNumber)).IsEligible);
            // بازپرداخت قبل از تأیید ممنوع.
            Assert.False((await returns.RefundAsync(request.Id)).IsSuccess());

            Assert.False((await returns.ApproveAsync(request.Id, 700_000, null)).IsSuccess());
            Assert.True((await returns.ApproveAsync(request.Id, null, "تأیید آزمون")).IsSuccess());
            Assert.False((await returns.ApproveAsync(request.Id, null, null)).IsSuccess());
            Assert.True((await returns.MarkReceivedAsync(request.Id, restock: true)).IsSuccess());
            context.ChangeTracker.Clear();
            Assert.Equal(6, await context.Products.Where(p => p.Id == productId).Select(p => p.StockQuantity).SingleAsync());

            var refund = await returns.RefundAsync(request.Id);
            Assert.True(refund.IsSuccess(), refund.Message);
            Assert.False((await returns.RefundAsync(request.Id)).IsSuccess());
            Assert.Equal(600_000m, await wallet.GetBalanceAsync(userId));

            context.ChangeTracker.Clear();
            var after = await context.Orders.AsNoTracking().SingleAsync(o => o.Id == order.Id);
            Assert.Equal(OrderStatus.Returned, after.Status);
            Assert.Equal(600_000m, after.RefundedAmount);
            Assert.Equal(ReturnStatus.Refunded, (await context.ReturnRequests.AsNoTracking().SingleAsync(r => r.Id == request.Id)).Status);
        }
        finally
        {
            await CleanupAsync(context, orders, userId, productId);
        }
    }

    [SqlFact]
    public async Task Inventory_forecast_and_purchase_draft_group_by_supplier()
    {
        var productId = await db.CreateProductAsync(200_000, 3);
        var orders = new List<Guid>();
        await using var scope = db.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
        var inventory = scope.ServiceProvider.GetRequiredService<IInventoryService>();
        var supplier = new Supplier { Name = "تأمین‌کنندهٔ آزمون " + Guid.NewGuid().ToString("N")[..6], Phone = "02100000000" };
        try
        {
            context.Suppliers.Add(supplier);
            await context.SaveChangesAsync();
            await context.Products.Where(p => p.Id == productId).ExecuteUpdateAsync(s => s.SetProperty(p => p.SupplierId, supplier.Id).SetProperty(p => p.PurchasePrice, 150_000m));

            // ۲۸ روز، هر روز ۲ عدد.
            var now = DateTimeOffset.UtcNow;
            for (var day = 0; day < 28; day++)
            {
                var order = NewOrder(null, OrderStatus.Delivered, 400_000);
                order.Items.Add(new OrderItem { ProductId = productId, ProductName = "کالای پرفروش", SKU = "F-1", UnitPrice = 200_000, Quantity = 2, TotalAmount = 400_000 });
                context.Orders.Add(order);
                await context.SaveChangesAsync();
                orders.Add(order.Id);
                var at = now.AddDays(-day).AddHours(-1);
                await context.Orders.Where(o => o.Id == order.Id).ExecuteUpdateAsync(s => s.SetProperty(o => o.CreatedAt, at));
            }
            context.ChangeTracker.Clear();

            var forecast = await inventory.ForecastProductAsync(productId, 30);
            Assert.InRange(forecast.DailyRate, 1.5m, 2.5m);
            Assert.Equal(StockForecastLevel.Critical, forecast.Level);
            Assert.True(forecast.SuggestedReorder >= 30);
            Assert.NotEqual("simple", forecast.Method);

            var draft = await inventory.PurchaseDraftAsync(30);
            var group = Assert.Single(draft, g => g.SupplierId == supplier.Id);
            var line = Assert.Single(group.Lines, l => l.ProductId == productId);
            Assert.Equal(150_000m, line.UnitCost);
            Assert.Equal(line.Quantity * 150_000m, group.TotalCost);
            Assert.Equal("02100000000", group.Phone);

            Assert.Equal(StockForecastLevel.NoSales, (await inventory.ForecastProductAsync(Guid.NewGuid())).Level);
        }
        finally
        {
            await CleanupAsync(context, orders, null, productId);
            await context.Products.IgnoreQueryFilters().Where(p => p.Id == productId).ExecuteUpdateAsync(s => s.SetProperty(p => p.SupplierId, (Guid?)null));
            await context.Suppliers.IgnoreQueryFilters().Where(s => s.Id == supplier.Id).ExecuteDeleteAsync();
        }
    }

    [SqlFact]
    public async Task Order_risk_new_cod_big_order_is_riskier_than_loyal_customer()
    {
        var loyal = await db.CreateUserAsync();
        var fresh = await db.CreateUserAsync();
        var productId = await db.CreateProductAsync(100_000, 50);
        var orders = new List<Guid>();
        await using var scope = db.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
        var risk = scope.ServiceProvider.GetRequiredService<IOrderRiskService>();
        try
        {
            var now = DateTimeOffset.UtcNow;
            await context.Users.Where(u => u.Id == loyal).ExecuteUpdateAsync(s => s.SetProperty(u => u.CreatedAt, now.AddDays(-400)));
            for (var i = 0; i < 4; i++)
            {
                var past = NewOrder(loyal, OrderStatus.Delivered, 300_000);
                context.Orders.Add(past);
                await context.SaveChangesAsync();
                orders.Add(past.Id);
                var at = now.AddDays(-30 * (i + 1));
                await context.Orders.Where(o => o.Id == past.Id).ExecuteUpdateAsync(s => s.SetProperty(o => o.CreatedAt, at));
            }
            // مبلغ کم (زیر مبلغ معمول) تا ریسک مشتری وفادار فقط از سابقه‌اش بیاید.
            var loyalOrder = NewOrder(loyal, OrderStatus.Pending, 10_000);
            var freshOrder = NewOrder(fresh, OrderStatus.Pending, 3_000_000, PaymentMethod.CashOnDelivery);
            freshOrder.Items.Add(new OrderItem { ProductId = productId, ProductName = "گران", SKU = "K-1", UnitPrice = 100_000, Quantity = 30, TotalAmount = 3_000_000 });
            context.Orders.AddRange(loyalOrder, freshOrder);
            await context.SaveChangesAsync();
            orders.Add(loyalOrder.Id);
            orders.Add(freshOrder.Id);
            context.ChangeTracker.Clear();

            var map = await risk.AssessAsync([loyalOrder.Id, freshOrder.Id]);
            Assert.True(map[freshOrder.Id].Probability > map[loyalOrder.Id].Probability);
            Assert.Equal(OrderRiskLevel.Low, map[loyalOrder.Id].Level);
            Assert.NotEqual(OrderRiskLevel.Low, map[freshOrder.Id].Level);
            Assert.Contains("پرداخت در محل", map[freshOrder.Id].Reasons);

            Assert.Empty(await risk.AssessAsync([]));
            Assert.Empty(await risk.AssessAsync([Guid.NewGuid()]));
        }
        finally
        {
            await CleanupAsync(context, orders, null, productId);
        }
    }

    [SqlFact]
    public async Task WinBack_insight_and_campaign_respect_switch_and_cooldown()
    {
        var userId = await db.CreateUserAsync();
        var orders = new List<Guid>();
        await using var scope = db.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
        var winBack = scope.ServiceProvider.GetRequiredService<IWinBackService>();
        var config = scope.ServiceProvider.GetRequiredService<ISmartConfigService>();
        try
        {
            Assert.Null(await winBack.InsightAsync(userId));

            // سه خرید ماهانهٔ پرمبلغ، آخرین ۱۲۰ روز پیش ← در خطر ریزش.
            var now = DateTimeOffset.UtcNow;
            for (var i = 0; i < 3; i++)
            {
                var order = NewOrder(userId, OrderStatus.Delivered, 20_000_000);
                context.Orders.Add(order);
                await context.SaveChangesAsync();
                orders.Add(order.Id);
                var at = now.AddDays(-120 - 30 * i);
                await context.Orders.Where(o => o.Id == order.Id).ExecuteUpdateAsync(s => s.SetProperty(o => o.CreatedAt, at));
            }
            context.ChangeTracker.Clear();

            var insight = await winBack.InsightAsync(userId, CancellationToken.None);
            Assert.NotNull(insight);
            Assert.True(insight!.AtRisk, $"p={insight.Probability} gap={insight.ExpectedGapDays}");
            Assert.InRange(insight.DaysSinceLastOrder, 119, 121);

            // ساعت ۱۴ ایران (۱۰:۳۰ UTC).
            var runAt = new DateTimeOffset(now.UtcDateTime.Date.AddHours(10).AddMinutes(30), TimeSpan.Zero);
            Assert.Equal(0, await winBack.RunAsync(runAt)); // خاموش (پیش‌فرض)

            var options = await config.GetAsync();
            options.WinBackEnabled = true;
            Assert.True((await config.SaveAsync(options)).IsSuccess());
            // ساعت ۲۳ ایران ← هیچ.
            Assert.Equal(0, await winBack.RunAsync(runAt.AddHours(9)));

            var sent = await winBack.RunAsync(runAt.AddMinutes(1));
            Assert.True(sent >= 1);
            context.ChangeTracker.Clear();
            var coupon = await context.Coupons.AsNoTracking().SingleAsync(c => c.UserId == userId && c.Code.StartsWith(WinBackService.CodePrefix));
            Assert.False(coupon.IsPublic);
            Assert.Equal(1, coupon.UsageLimit);
            Assert.True(coupon.MaxDiscountAmount <= options.WinBackMaxAmount);
            Assert.True(await context.Notifications.AnyAsync(n => n.UserId == userId));

            // همان ساعت دوباره ← قفل ساعتی؛ ساعت بعد ← دورهٔ استراحت ۱۲۰ روزه (کوپن دوم ساخته نمی‌شود).
            Assert.Equal(0, await winBack.RunAsync(runAt.AddMinutes(2)));
            await winBack.RunAsync(runAt.AddHours(2));
            Assert.Equal(1, await context.Coupons.CountAsync(c => c.UserId == userId && c.Code.StartsWith(WinBackService.CodePrefix)));
        }
        finally
        {
            var options = await config.GetAsync();
            options.WinBackEnabled = false;
            await config.SaveAsync(options);
            await context.Coupons.IgnoreQueryFilters().Where(c => c.Code.StartsWith(WinBackService.CodePrefix)).ExecuteDeleteAsync();
            await CleanupAsync(context, orders, userId);
        }
    }

    [SqlFact]
    public async Task Anomaly_metrics_cover_sixty_days_with_weekdays()
    {
        await using var scope = db.Scope();
        var anomalies = scope.ServiceProvider.GetRequiredService<IAnomalyService>();
        var metrics = await anomalies.MetricsAsync(DateTimeOffset.UtcNow);
        Assert.Equal(new[] { "orders", "revenue", "cancelled", "returns", "signups" }, metrics.Select(m => m.Key).ToArray());
        Assert.All(metrics, m =>
        {
            Assert.Equal(AnomalyService.HistoryDays, m.History.Length);
            Assert.Equal(m.History.Length + 1, m.Weekdays.Length);
            Assert.StartsWith("/admin/", m.Url);
        });
    }
}
