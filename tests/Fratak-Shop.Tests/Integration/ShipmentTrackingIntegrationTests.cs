using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Core.Services.Orders;
using Fratak_Shop.Core.Services.Settings;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Orders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Fratak_Shop.Tests.Integration;

/// <summary>
/// تست رهگیری مرسوله روی دیتابیس واقعی (مرحلهٔ هفتم – فاز ۶) با سامانهٔ پست جعلی:
/// ثبت کد با ارقام فارسی، خواندن رویدادها بدون تکرار، تحویل خودکار سفارش با زمان واقعی پست،
/// برگشت به فرستنده (بدون تحویل خودکار)، ثبت خطای شبکه، شروع دوباره با تغییر کد و توقف/ادامه.
/// </summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", "Integration")]
public class ShipmentTrackingIntegrationTests(SqlServerFixture db)
{
    /// <summary>سامانهٔ پست جعلی: پاسخ هر کد از دیکشنری؛ کد ناشناس ← «یافت نشد».</summary>
    private sealed class FakePost : IPostTrackingClient
    {
        public Dictionary<string, PostTrackingResponse> Responses { get; } = new();
        public List<string> Calls { get; } = [];

        public Task<PostTrackingResponse> TrackAsync(string code, CancellationToken cancellationToken)
        {
            Calls.Add(code);
            return Task.FromResult(Responses.TryGetValue(code, out var response)
                ? response
                : new PostTrackingResponse(PostTrackingOutcome.NotFound, [], []));
        }
    }

    private static PostTrackingResponse Found(params (string Status, string? Location, DateTimeOffset? At)[] events) =>
        new(PostTrackingOutcome.Found,
            events.Select((e, i) => new PostTrackingEvent(i, e.Status, e.Location, e.At is { } at ? PersianDate.Format(at) : null, e.At)).ToList(),
            [new("مبدأ", "شیراز")]);

    private static ShipmentTrackingService Service(AsyncServiceScope scope, IPostTrackingClient client) => new(
        scope.ServiceProvider.GetRequiredService<FratakShopDbContext>(),
        scope.ServiceProvider.GetRequiredService<IStoreSettingService>(),
        client,
        scope.ServiceProvider.GetRequiredService<IAdminOrderService>(),
        scope.ServiceProvider.GetRequiredService<IStaffNotificationService>());

    /// <summary>سفارش «ارسال‌شده» با کد رهگیری (مثل وقتی کارمند کد را ثبت کرده).</summary>
    private async Task<Guid> CreateShippedOrderAsync(string code, DateTimeOffset shippedAt)
    {
        await using var scope = db.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
        var order = new Order
        {
            OrderNumber = "S" + Random.Shared.NextInt64(1_000_000_000, 9_999_999_999),
            Status = OrderStatus.Shipped,
            PaymentMethod = PaymentMethod.OnlineGateway,
            PaymentStatus = PaymentStatus.Paid,
            PaidAt = shippedAt.AddDays(-1),
            ShippedAt = shippedAt,
            TrackingCode = code,
            SubtotalAmount = 100_000,
            ShippingAmount = 50_000,
            PayableAmount = 150_000,
            CustomerName = "مشتری رهگیری",
            CustomerPhone = "09120000000",
            ShippingProvince = "تهران",
            ShippingCity = "تهران",
            ShippingAddress = "خیابان آزمایش",
            ShippingPostalCode = "1234567890",
            ShippingPhone = "09120000000",
        };
        order.Items.Add(new OrderItem { ProductName = "کالای آزمایشی", SKU = "T-SHIP", UnitPrice = 100_000, Quantity = 1, TotalAmount = 100_000 });
        context.Orders.Add(order);
        await context.SaveChangesAsync();
        return order.Id;
    }

    /// <summary>کد ۲۴ رقمی یکتا برای هر تست.</summary>
    private static string NewCode() => Random.Shared.NextInt64(100_000_000_000, 999_999_999_999).ToString() + Random.Shared.NextInt64(100_000_000_000, 999_999_999_999).ToString();

    /// <summary>برداشتن محدودیت «هر ۲ دقیقه یک بار» بین دو بررسی پشت سر هم در تست.</summary>
    private async Task ResetLastCheckAsync(Guid orderId)
    {
        await using var scope = db.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
        await context.ShipmentTrackings.Where(t => t.OrderId == orderId)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.LastCheckedAt, DateTimeOffset.UtcNow.AddHours(-1)));
    }

    [Fact]
    public async Task Events_are_merged_without_duplicates_and_delivery_completes_the_order()
    {
        var code = NewCode();
        var shippedAt = DateTimeOffset.UtcNow.AddDays(-3);
        var orderId = await CreateShippedOrderAsync(code, shippedAt);
        var post = new FakePost();

        // ثبت با ارقام فارسی و فاصله → کد انگلیسی ذخیره می‌شود.
        var persian = string.Concat(code.Select(c => (char)('۰' + (c - '0'))));
        await using (var scope = db.Scope())
        {
            var result = await Service(scope, post).RegisterAsync(orderId, ShippingCarrier.IranPost, persian[..12] + " " + persian[12..]);
            Assert.True(result.IsSuccess(), result.Message);
            var timeline = await Service(scope, post).GetTimelineAsync(orderId);
            Assert.NotNull(timeline);
            Assert.Equal(code, timeline!.Code);
            Assert.Equal(ShipmentTrackingState.Pending, timeline.State);
        }

        var accepted = DateTimeOffset.UtcNow.AddDays(-3).AddHours(2);
        var transit = DateTimeOffset.UtcNow.AddDays(-2);
        post.Responses[code] = Found(("قبول مرسوله", "باجه شیراز", accepted), ("ورود به مرکز مبادلات", "تهران", transit));
        await using (var scope = db.Scope())
        {
            var check = await Service(scope, post).CheckNowAsync(orderId);
            Assert.True(check.IsSuccess(), check.Message);
            var timeline = await Service(scope, post).GetTimelineAsync(orderId);
            Assert.Equal(ShipmentTrackingState.InTransit, timeline!.State);
            Assert.Equal(2, timeline.Events.Count);
            Assert.Equal("ورود به مرکز مبادلات", timeline.Events[0].Status);   // جدیدترین اول
            Assert.Equal("ورود به مرکز مبادلات", timeline.LastStatus);
            Assert.Single(timeline.Info);

            // بررسی دوباره در کمتر از ۲ دقیقه پذیرفته نمی‌شود.
            Assert.False((await Service(scope, post).CheckNowAsync(orderId)).IsSuccess());
        }

        await ResetLastCheckAsync(orderId);
        var deliveredAt = DateTimeOffset.UtcNow.AddHours(-5);
        post.Responses[code] = Found(("قبول مرسوله", "باجه شیراز", accepted), ("ورود به مرکز مبادلات", "تهران", transit),
            ("تحویل به نامه‌رسان", "تهران", deliveredAt.AddHours(-3)), ("تحویل مرسوله به گیرنده", "تهران", deliveredAt));
        await using (var scope = db.Scope())
        {
            var check = await Service(scope, post).CheckNowAsync(orderId);
            Assert.True(check.IsSuccess(), check.Message);
        }

        await using (var scope = db.Scope())
        {
            var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
            var tracking = await context.ShipmentTrackings.Include(t => t.Events).SingleAsync(t => t.OrderId == orderId);
            Assert.Equal(ShipmentTrackingState.Delivered, tracking.State);
            Assert.True(tracking.AutoDelivered);
            Assert.Equal(4, tracking.Events.Count);                                   // دو رویداد قبلی تکراری ذخیره نشدند
            Assert.Equal(4, tracking.Events.Select(e => e.Fingerprint).Distinct().Count());
            Assert.Equal(0, tracking.FailureCount);

            var order = await context.Orders.SingleAsync(o => o.Id == orderId);
            Assert.Equal(OrderStatus.Delivered, order.Status);
            Assert.NotNull(order.DeliveredAt);
            // زمان تحویل سفارش = زمان واقعی پست (برای مهلت مرجوعی).
            Assert.True(Math.Abs((order.DeliveredAt!.Value - deliveredAt).TotalSeconds) < 2);
            Assert.True(await context.OrderNotes.AnyAsync(n => n.OrderId == orderId && n.AuthorName == ShipmentTrackingService.SystemName));
        }

        // سفارش تحویل‌شده دیگر بررسی نمی‌شود.
        await ResetLastCheckAsync(orderId);
        await using (var scope = db.Scope())
        {
            post.Calls.Clear();
            await Service(scope, post).RunDueAsync(CancellationToken.None, force: true);
            Assert.DoesNotContain(code, post.Calls);
        }
    }

    [Fact]
    public async Task Returned_parcel_is_not_auto_delivered_and_failures_are_recorded()
    {
        var returnedCode = NewCode();
        var failingCode = NewCode();
        var returnedOrder = await CreateShippedOrderAsync(returnedCode, DateTimeOffset.UtcNow.AddDays(-6));
        var failingOrder = await CreateShippedOrderAsync(failingCode, DateTimeOffset.UtcNow.AddDays(-1));
        var post = new FakePost();
        post.Responses[returnedCode] = Found(("قبول مرسوله", "شیراز", DateTimeOffset.UtcNow.AddDays(-6)),
            ("عدم حضور گیرنده", "تهران", DateTimeOffset.UtcNow.AddDays(-3)),
            ("برگشت به فرستنده", "تهران", DateTimeOffset.UtcNow.AddDays(-2)),
            ("تحویل مرسوله به گیرنده", "شیراز", DateTimeOffset.UtcNow.AddDays(-1)));
        post.Responses[failingCode] = PostTrackingResponse.Fail("اتصال به سامانهٔ پست ممکن نشد");

        await using (var scope = db.Scope())
        {
            var service = Service(scope, post);
            Assert.True((await service.RegisterAsync(returnedOrder, ShippingCarrier.IranPost, returnedCode)).IsSuccess());
            Assert.True((await service.RegisterAsync(failingOrder, ShippingCarrier.IranPost, failingCode)).IsSuccess());
            Assert.True((await service.CheckNowAsync(returnedOrder)).IsSuccess());
            var failed = await service.CheckNowAsync(failingOrder);
            Assert.False(failed.IsSuccess());
            Assert.Contains("اتصال", failed.Message);
        }

        await using (var scope = db.Scope())
        {
            var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
            var returned = await context.ShipmentTrackings.SingleAsync(t => t.OrderId == returnedOrder);
            Assert.Equal(ShipmentTrackingState.Returned, returned.State);
            Assert.False(returned.AutoDelivered);
            Assert.Equal(OrderStatus.Shipped, (await context.Orders.SingleAsync(o => o.Id == returnedOrder)).Status);

            var failing = await context.ShipmentTrackings.SingleAsync(t => t.OrderId == failingOrder);
            Assert.Equal(ShipmentTrackingState.Pending, failing.State);
            Assert.Equal(1, failing.FailureCount);
            Assert.Equal("اتصال به سامانهٔ پست ممکن نشد", failing.LastError);

            // برگشتی در زبانهٔ «نیازمند توجه» دیده می‌شود.
            var problems = await Service(scope, post).ListAsync(new ShipmentQuery(ShipmentTab.Problems, Search: returnedCode));
            Assert.Contains(problems.Items, r => r.OrderId == returnedOrder);
        }
    }

    [Fact]
    public async Task Changing_the_code_restarts_tracking_and_pause_resume_work()
    {
        var code = NewCode();
        var orderId = await CreateShippedOrderAsync(code, DateTimeOffset.UtcNow.AddDays(-1));
        var post = new FakePost();
        post.Responses[code] = Found(("قبول مرسوله", "شیراز", DateTimeOffset.UtcNow.AddHours(-20)));

        await using (var scope = db.Scope())
        {
            var service = Service(scope, post);
            Assert.True((await service.RegisterAsync(orderId, ShippingCarrier.IranPost, code)).IsSuccess());
            Assert.True((await service.CheckNowAsync(orderId)).IsSuccess());
            Assert.Single((await service.GetTimelineAsync(orderId))!.Events);

            // ثبت دوبارهٔ همان کد چیزی را عوض نمی‌کند.
            Assert.True((await service.RegisterAsync(orderId, ShippingCarrier.IranPost, code)).IsSuccess());
            Assert.Single((await service.GetTimelineAsync(orderId))!.Events);
        }

        var newCode = NewCode();
        await using (var scope = db.Scope())
        {
            var service = Service(scope, post);
            Assert.True((await service.RegisterAsync(orderId, ShippingCarrier.IranPost, newCode)).IsSuccess());
            var timeline = await service.GetTimelineAsync(orderId);
            Assert.Equal(newCode, timeline!.Code);
            Assert.Empty(timeline.Events);
            Assert.Equal(ShipmentTrackingState.Pending, timeline.State);
            Assert.Null(timeline.LastStatus);

            Assert.True((await service.SetPausedAsync(orderId, true)).IsSuccess());
            Assert.Equal(ShipmentTrackingState.Stopped, (await service.GetTimelineAsync(orderId))!.State);
            Assert.False((await service.SetPausedAsync(orderId, true)).IsSuccess());
            Assert.True((await service.SetPausedAsync(orderId, false)).IsSuccess());
            Assert.Equal(ShipmentTrackingState.Pending, (await service.GetTimelineAsync(orderId))!.State);
        }

        // شرکت بدون رهگیری خودکار: فقط ذخیره، بدون بررسی.
        await using (var scope = db.Scope())
        {
            var service = Service(scope, post);
            Assert.True((await service.RegisterAsync(orderId, ShippingCarrier.Tipax, "TPX-778899")).IsSuccess());
            var timeline = await service.GetTimelineAsync(orderId);
            Assert.Equal(ShipmentTrackingState.Stopped, timeline!.State);
            Assert.False(timeline.AutoTracking);
            Assert.Null(timeline.PublicUrl);
            Assert.False((await service.CheckNowAsync(orderId)).IsSuccess());
        }
    }
}
