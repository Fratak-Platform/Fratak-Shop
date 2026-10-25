using Fratak_Shop.Core.DTOs.Reports;
using Fratak_Shop.Core.Services.Reports;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Analytics;
using Fratak_Shop.Data.Entities.Orders;
using Fratak_Shop.Data.Entities.Payments;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Fratak_Shop.Tests.Integration;

/// <summary>
/// تست آمار بازدید روی دیتابیس واقعی (مرحلهٔ هفتم – فاز ۷): ادغام بسته‌های پیاپی (upsert)، نسبت دادن منبع به سفارش
/// فقط یک بار، قیف فروش و گزارش کمپین در یک روز مشخص گذشته (تا با داده‌های تست‌های دیگر قاطی نشود) و پاک‌سازی داده‌های قدیمی.
/// </summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", "Integration")]
public class AnalyticsIntegrationTests(SqlServerFixture db)
{
    private static VisitorHit Hit(DateOnly day, string key, FunnelStage stages, int pageViews, TrafficTouch touch, DateTimeOffset at) =>
        new(day, key, null, (int)stages, pageViews, (byte)IranClock.ToLocal(at).Hour, "mobile", touch, "/", at, at);

    /// <summary>سفارش آزمایشی با زمان ثبت دلخواه (CreatedAt بعد از ذخیره با ExecuteUpdate تنظیم می‌شود چون DbContext آن را پر می‌کند).</summary>
    private async Task<Guid> CreateOrderAsync(DateTimeOffset createdAt, PaymentStatus payment, string? visitorKey, decimal amount, string? source = null)
    {
        await using var scope = db.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
        var order = new Order
        {
            OrderNumber = "A" + Random.Shared.NextInt64(1_000_000_000, 9_999_999_999),
            Status = OrderStatus.Confirmed,
            PaymentMethod = PaymentMethod.OnlineGateway,
            PaymentStatus = payment,
            SubtotalAmount = amount,
            PayableAmount = amount,
            CustomerName = "مشتری آزمایشی",
            CustomerPhone = "09120000000",
            ShippingProvince = "تهران",
            ShippingCity = "تهران",
            ShippingAddress = "خیابان آزمایش",
            ShippingPostalCode = "1234567890",
            ShippingPhone = "09120000000",
            VisitorKey = visitorKey,
            TrafficSource = source,
            TrafficMedium = source is null ? null : "social",
        };
        context.Orders.Add(order);
        await context.SaveChangesAsync();
        await context.Orders.Where(o => o.Id == order.Id).ExecuteUpdateAsync(s => s.SetProperty(o => o.CreatedAt, createdAt));
        return order.Id;
    }

    [Fact]
    public async Task Consecutive_batches_are_merged_into_one_row_per_visitor_and_day()
    {
        var day = new DateOnly(2021, 1, 5);
        var key = TrafficRules.NewVisitorKey();
        var product = Guid.NewGuid().ToString("N");
        var first = IranClock.StartOfDay(day).AddHours(10);
        var instagram = new TrafficTouch("instagram", "social", "yalda");

        await using (var scope = db.Scope())
        {
            var analytics = scope.ServiceProvider.GetRequiredService<IAnalyticsService>();
            await analytics.SaveAsync(new AnalyticsBatch([Hit(day, key, FunnelStage.Visit | FunnelStage.ProductView, 2, instagram, first)],
                [new CounterHit(day, AnalyticsMetrics.ProductView, product, 2)]));
            await analytics.SaveAsync(new AnalyticsBatch([Hit(day, key, FunnelStage.AddToCart, 3, TrafficTouch.Direct, first.AddMinutes(5))],
                [new CounterHit(day, AnalyticsMetrics.ProductView, product, 3), new CounterHit(day, AnalyticsMetrics.ProductView, product, 1)]));
        }

        await using var check = db.Scope();
        var context = check.ServiceProvider.GetRequiredService<FratakShopDbContext>();
        var row = await context.VisitorDays.SingleAsync(v => v.VisitorKey == key);
        Assert.Equal((int)(FunnelStage.Visit | FunnelStage.ProductView | FunnelStage.AddToCart), row.Stages);
        Assert.Equal(5, row.PageViews);
        Assert.Equal("instagram", row.Source);              // منبع اولین ذخیرهٔ روز می‌ماند
        Assert.Equal("yalda", row.Campaign);
        Assert.Equal(first.AddMinutes(5), row.LastSeenAt);
        Assert.Equal(6, (await context.AnalyticsCounters.SingleAsync(c => c.Key == product)).Count);
    }

    [Fact]
    public async Task Order_source_is_written_only_once()
    {
        var orderId = await CreateOrderAsync(DateTimeOffset.UtcNow, PaymentStatus.Pending, null, 100_000);
        var key = TrafficRules.NewVisitorKey();

        await using (var scope = db.Scope())
        {
            var analytics = scope.ServiceProvider.GetRequiredService<IAnalyticsService>();
            await analytics.AttributeOrderAsync(orderId, key, new TrafficTouch("torob", "comparison", null));
            await analytics.AttributeOrderAsync(orderId, TrafficRules.NewVisitorKey(), new TrafficTouch("google", "organic", null));
        }

        await using var check = db.Scope();
        var order = await check.ServiceProvider.GetRequiredService<FratakShopDbContext>().Orders.SingleAsync(o => o.Id == orderId);
        Assert.Equal("torob", order.TrafficSource);
        Assert.Equal("comparison", order.TrafficMedium);
        Assert.Equal(key, order.VisitorKey);

        // شناسهٔ دستکاری‌شده ذخیره نمی‌شود و بدون کوکی منبع «مستقیم» ثبت می‌شود.
        var other = await CreateOrderAsync(DateTimeOffset.UtcNow, PaymentStatus.Pending, null, 100_000);
        await using (var scope = db.Scope())
            await scope.ServiceProvider.GetRequiredService<IAnalyticsService>().AttributeOrderAsync(other, "not-a-key", null);
        await using var check2 = db.Scope();
        var direct = await check2.ServiceProvider.GetRequiredService<FratakShopDbContext>().Orders.SingleAsync(o => o.Id == other);
        Assert.Equal("direct", direct.TrafficSource);
        Assert.Null(direct.VisitorKey);
    }

    [Fact]
    public async Task Funnel_and_campaign_reports_count_unique_visitors_and_realized_orders()
    {
        var day = new DateOnly(2021, 3, 10);
        var start = IranClock.StartOfDay(day);
        var period = new ReportPeriod(start, IranClock.StartOfDay(day.AddDays(1)));
        var insta = new TrafficTouch("instagram", "social", null);
        var keys = Enumerable.Range(0, 4).Select(_ => TrafficRules.NewVisitorKey()).ToArray();

        await using (var scope = db.Scope())
        {
            await scope.ServiceProvider.GetRequiredService<IAnalyticsService>().SaveAsync(new AnalyticsBatch(
            [
                Hit(day, keys[0], FunnelStage.Visit, 1, TrafficTouch.Direct, start.AddHours(9)),
                Hit(day, keys[1], FunnelStage.Visit | FunnelStage.ProductView, 3, insta, start.AddHours(9)),
                Hit(day, keys[2], FunnelStage.Visit | FunnelStage.ProductView | FunnelStage.AddToCart | FunnelStage.Checkout, 6, insta, start.AddHours(21)),
                Hit(day, keys[3], FunnelStage.Visit | FunnelStage.ProductView | FunnelStage.AddToCart | FunnelStage.Checkout, 8, insta, start.AddHours(21)),
            ], []));
        }

        await CreateOrderAsync(start.AddHours(21.5), PaymentStatus.Paid, keys[2], 2_000_000, "instagram");
        await CreateOrderAsync(start.AddHours(22), PaymentStatus.Pending, keys[3], 1_000_000, "instagram");

        await using var report = db.Scope();
        var reports = report.ServiceProvider.GetRequiredService<IAnalyticsReportService>();
        var funnel = await reports.GetFunnelAsync(period);

        Assert.Equal(4, funnel.Visitors);
        Assert.Equal(18, funnel.PageViews);
        Assert.Equal("4,3,2,2,2,1", string.Join(',', funnel.Steps.Select(s => s.Count)));
        Assert.Equal(0, funnel.UntrackedOrders);
        Assert.Equal(4, funnel.Devices.Single().Count);

        var campaigns = await reports.GetCampaignsAsync(period, groupByCampaign: false);
        var instagram = campaigns.Single(r => r.Source == "instagram");
        Assert.Equal(3, instagram.Visitors);
        Assert.Equal(1, instagram.Orders);                 // فقط سفارش پرداخت‌شده «قطعی» است
        Assert.Equal(2_000_000, instagram.Revenue);
        Assert.Equal(1, campaigns.Single(r => r.Source == "direct").Visitors);

        var heatmap = await reports.GetHeatmapAsync(period);
        var row = HeatmapReport.Row(day.DayOfWeek);
        Assert.Equal(2, heatmap.Visits[row][9]);
        Assert.Equal(2, heatmap.Visits[row][21]);
    }

    [Fact]
    public async Task Purge_removes_only_rows_older_than_retention()
    {
        var oldKey = TrafficRules.NewVisitorKey();
        var newKey = TrafficRules.NewVisitorKey();
        var today = IranClock.Today(DateTimeOffset.UtcNow);
        var oldDay = today.AddDays(-2000);

        await using (var scope = db.Scope())
        {
            var analytics = scope.ServiceProvider.GetRequiredService<IAnalyticsService>();
            await analytics.SaveAsync(new AnalyticsBatch(
                [Hit(oldDay, oldKey, FunnelStage.Visit, 1, TrafficTouch.Direct, IranClock.StartOfDay(oldDay)),
                 Hit(today, newKey, FunnelStage.Visit, 1, TrafficTouch.Direct, DateTimeOffset.UtcNow)], []));
            Assert.True(await analytics.PurgeAsync(400) >= 1);
        }

        await using var check = db.Scope();
        var context = check.ServiceProvider.GetRequiredService<FratakShopDbContext>();
        Assert.False(await context.VisitorDays.AnyAsync(v => v.VisitorKey == oldKey));
        Assert.True(await context.VisitorDays.AnyAsync(v => v.VisitorKey == newKey));
    }
}
