using Fratak_Shop.Core.DTOs.Settings;
using Fratak_Shop.Core.Services.Orders;
using Fratak_Shop.Data.Entities.Orders;
using Fratak_Shop.Web.Areas.Admin.Pages.Orders;

namespace Fratak_Shop.Tests.Unit;

/// <summary>
/// تست‌های منطق خالص مرحلهٔ هفتم – فاز ۴ (سفارش‌ها):
/// زمان‌بندی سفارش (SLA)، بازهٔ تحویل تخمینی، قوانین پرداخت در محل، توکن لینک پرداخت،
/// نرمال‌سازی موبایل و قانون «یک قدم جلو» در کانبان.
/// </summary>
public class OrdersRound7Tests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 20, 8, 0, 0, TimeSpan.Zero);

    private static OrderSlaInput Input(OrderStatus status, PaymentStatus payment = PaymentStatus.Paid,
        PaymentMethod method = PaymentMethod.OnlineGateway, DateTimeOffset? processingAt = null, DateTimeOffset? shippedAt = null,
        string? tracking = null, int? minDays = null, int? maxDays = null) =>
        new(status, method, payment, T0, payment == PaymentStatus.Paid ? T0 : null, processingAt, shippedAt, tracking, minDays, maxDays);

    // ── زمان‌بندی (SLA) ────────────────────────────────────────────────

    [Fact]
    public void Sla_UnpaidOnlineOrder_HasNoDeadline()
    {
        var result = OrderSla.Evaluate(Input(OrderStatus.Pending, PaymentStatus.Pending), OrderSlaPolicy.Default, T0.AddDays(3));
        Assert.Null(result);
    }

    [Theory]
    [InlineData(10, OrderSlaLevel.OnTrack)]
    [InlineData(20, OrderSlaLevel.Soon)]     // ۲۰ از ۲۴ ساعت = ۸۳٪ ≥ ۷۵٪
    [InlineData(25, OrderSlaLevel.Overdue)]
    public void Sla_SupplierOrderStage_LevelsFollowElapsedTime(int hours, OrderSlaLevel expected)
    {
        var result = OrderSla.Evaluate(Input(OrderStatus.Confirmed), OrderSlaPolicy.Default, T0.AddHours(hours));

        Assert.NotNull(result);
        Assert.Equal(OrderSlaStage.SupplierOrder, result!.Stage);
        Assert.Equal(expected, result.Level);
        Assert.Equal(T0.AddHours(24), result.Deadline);
    }

    [Fact]
    public void Sla_CashOnDeliveryPending_StartsAtCreation()
    {
        var result = OrderSla.Evaluate(Input(OrderStatus.Pending, PaymentStatus.Pending, PaymentMethod.CashOnDelivery), OrderSlaPolicy.Default, T0.AddHours(1));
        Assert.NotNull(result);
        Assert.Equal(OrderSlaStage.SupplierOrder, result!.Stage);
        Assert.Equal(T0, result.StartedAt);
    }

    [Fact]
    public void Sla_Processing_UsesProcessingTime()
    {
        var processing = T0.AddHours(5);
        var result = OrderSla.Evaluate(Input(OrderStatus.Processing, processingAt: processing), OrderSlaPolicy.Default, processing.AddHours(49));

        Assert.NotNull(result);
        Assert.Equal(OrderSlaStage.Preparation, result!.Stage);
        Assert.Equal(OrderSlaLevel.Overdue, result.Level);
        Assert.Equal(processing.AddHours(48), result.Deadline);
    }

    [Fact]
    public void Sla_Shipped_WithoutTrackingCode_ThenDelivery()
    {
        var shipped = T0.AddDays(2);
        var noCode = OrderSla.Evaluate(Input(OrderStatus.Shipped, shippedAt: shipped), OrderSlaPolicy.Default, shipped.AddHours(1));
        Assert.Equal(OrderSlaStage.TrackingCode, noCode!.Stage);
        Assert.Equal(shipped.AddHours(12), noCode.Deadline);

        // روش ارسال سفارش ۲ تا ۵ روز است → همان ملاک است، نه مقدار تنظیمات (۳ تا ۴).
        var withCode = OrderSla.Evaluate(Input(OrderStatus.Shipped, shippedAt: shipped, tracking: "123456789012345678901234", minDays: 2, maxDays: 5),
            OrderSlaPolicy.Default, shipped.AddDays(1));
        Assert.Equal(OrderSlaStage.Delivery, withCode!.Stage);
        Assert.Equal(shipped.AddDays(5), withCode.Deadline);
    }

    [Theory]
    [InlineData(OrderStatus.Delivered)]
    [InlineData(OrderStatus.Cancelled)]
    [InlineData(OrderStatus.Returned)]
    public void Sla_ClosedOrders_HaveNoDeadline(OrderStatus status)
    {
        Assert.Null(OrderSla.Evaluate(Input(status), OrderSlaPolicy.Default, T0.AddDays(10)));
        Assert.Null(OrderSla.EstimateDelivery(Input(status), OrderSlaPolicy.Default, T0.AddDays(10)));
    }

    [Fact]
    public void Sla_DeliveryDays_PreferMethodAndStayOrdered()
    {
        Assert.Equal((2, 4), OrderSla.DeliveryDays(Input(OrderStatus.Shipped, minDays: 2, maxDays: 4), OrderSlaPolicy.Default));
        Assert.Equal((3, 4), OrderSla.DeliveryDays(Input(OrderStatus.Shipped), OrderSlaPolicy.Default));
        Assert.Equal((2, 5), OrderSla.DeliveryDays(Input(OrderStatus.Shipped, minDays: 5, maxDays: 2), OrderSlaPolicy.Default));
    }

    [Fact]
    public void Sla_EstimateDelivery_AddsRemainingStagesBeforeShipping()
    {
        // پرداخت در T0 → ارسال حداکثر بعد از ۲۴ + ۴۸ ساعت → تحویل ۳ تا ۴ روز بعد از آن.
        var estimate = OrderSla.EstimateDelivery(Input(OrderStatus.Confirmed), OrderSlaPolicy.Default, T0);
        Assert.NotNull(estimate);
        Assert.Equal(T0.AddHours(72).AddDays(3), estimate!.From);
        Assert.Equal(T0.AddHours(72).AddDays(4), estimate.To);

        var shipped = T0.AddDays(1);
        var afterShip = OrderSla.EstimateDelivery(Input(OrderStatus.Shipped, shippedAt: shipped, tracking: "1234567890123"), OrderSlaPolicy.Default, shipped.AddHours(2));
        Assert.Equal(shipped.AddDays(3), afterShip!.From);
        Assert.Equal(shipped.AddDays(4), afterShip.To);
    }

    [Fact]
    public void Sla_EstimateDelivery_NeverShowsPastDates()
    {
        // مهلت‌ها گذشته ولی هنوز آماده‌سازی تمام نشده: تخمین از «اکنون» حساب می‌شود.
        var now = T0.AddDays(10);
        var estimate = OrderSla.EstimateDelivery(Input(OrderStatus.Processing, processingAt: T0), OrderSlaPolicy.Default, now);
        Assert.NotNull(estimate);
        Assert.True(estimate!.From >= now);
        Assert.True(estimate.To >= estimate.From);
    }

    [Fact]
    public void Sla_Humanize_UsesPersianDigits()
    {
        Assert.Equal("۱ ساعت و ۳۰ دقیقه مانده", OrderSla.Humanize(TimeSpan.FromMinutes(90)));
        Assert.Equal("۲ روز و ۲ ساعت دیرکرد", OrderSla.Humanize(TimeSpan.FromHours(-50)));
        Assert.Equal("۴۵ دقیقه مانده", OrderSla.Humanize(TimeSpan.FromMinutes(45)));
        Assert.Equal("کمتر از ۱ دقیقه مانده", OrderSla.Humanize(TimeSpan.FromSeconds(20)));
    }

    [Fact]
    public void Sla_PolicyFromSettings_CopiesEveryValue()
    {
        var settings = new StoreSettingDto
        {
            SlaSupplierOrderHours = 6, SlaPreparationHours = 30, SlaTrackingCodeHours = 3,
            SlaDeliveryMinDays = 2, SlaDeliveryMaxDays = 7, SlaWarningPercent = 60,
        };
        Assert.Equal(new OrderSlaPolicy(6, 30, 3, 2, 7, 60), OrderSlaPolicy.From(settings));
    }

    // ── پرداخت در محل ───────────────────────────────────────────────────

    private static StoreSettingDto Cod(Action<StoreSettingDto>? change = null)
    {
        var settings = new StoreSettingDto { IsCashOnDeliveryEnabled = true };
        change?.Invoke(settings);
        return settings;
    }

    [Fact]
    public void Cod_Disabled_IsRejected()
    {
        Assert.NotNull(CashOnDeliveryRules.Check(Cod(s => s.IsCashOnDeliveryEnabled = false), null, null, null));
        Assert.Null(CashOnDeliveryRules.Check(Cod(), null, null, 5_000_000m));
    }

    [Fact]
    public void Cod_MaxAmount_IsEnforced()
    {
        var settings = Cod(s => s.CashOnDeliveryMaxAmount = 1_000_000m);
        Assert.Null(CashOnDeliveryRules.Check(settings, null, null, 1_000_000m));
        Assert.Contains("بالای", CashOnDeliveryRules.Check(settings, null, null, 1_000_001m));
        Assert.Null(CashOnDeliveryRules.Check(settings, null, null, null)); // مبلغ هنوز معلوم نیست
    }

    [Fact]
    public void Cod_Provinces_AreNormalized()
    {
        var settings = Cod(s => s.CodAllowedProvinces = "تهران، البرز\nقم");
        Assert.Equal(new[] { "تهران", "البرز", "قم" }, settings.CodProvinces);
        Assert.True(CashOnDeliveryRules.IsProvinceAllowed(settings, "استان تهران"));
        Assert.True(CashOnDeliveryRules.IsProvinceAllowed(settings, "قم"));
        Assert.False(CashOnDeliveryRules.IsProvinceAllowed(settings, "اصفهان"));
        Assert.Null(CashOnDeliveryRules.Check(settings, null, null, null)); // استان هنوز انتخاب نشده
        Assert.NotNull(CashOnDeliveryRules.Check(settings, null, "اصفهان", null));
        Assert.True(CashOnDeliveryRules.IsProvinceAllowed(Cod(), "هر استانی")); // فهرست خالی = همه
    }

    [Fact]
    public void Cod_History_Rules()
    {
        var firstPurchase = Cod(s => s.CodMinDeliveredOrders = 1);
        Assert.Contains("اولین خرید", CashOnDeliveryRules.Check(firstPurchase, new CustomerCodHistory(0, 0), null, null));
        Assert.Null(CashOnDeliveryRules.Check(firstPurchase, new CustomerCodHistory(1, 0), null, null));

        var returns = Cod(s => s.CodMaxReturnedOrders = 2);
        Assert.Null(CashOnDeliveryRules.Check(returns, new CustomerCodHistory(5, 1), null, null));
        Assert.NotNull(CashOnDeliveryRules.Check(returns, new CustomerCodHistory(5, 2), null, null));
        Assert.Null(CashOnDeliveryRules.Check(Cod(), new CustomerCodHistory(0, 50), null, null)); // ۰ = بدون محدودیت
    }

    // ── لینک پرداخت و ثبت سفارش پنل ─────────────────────────────────────

    [Fact]
    public void PaymentToken_IsRandomUrlSafeAndValidated()
    {
        var a = StaffOrderService.NewToken();
        var b = StaffOrderService.NewToken();
        Assert.Equal(32, a.Length);
        Assert.NotEqual(a, b);
        Assert.True(StaffOrderService.IsWellFormedToken(a));
        Assert.DoesNotContain('+', a);
        Assert.DoesNotContain('/', a);
        Assert.DoesNotContain('=', a);
        Assert.Equal("/pay/" + a, StaffOrderService.LinkPath(a));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("abcdefghijklmnopqrstuvwxyz/../../etc")]
    [InlineData("abcdefghijklmnopqrstuvwxyz<script>")]
    public void PaymentToken_RejectsMalformedInput(string? token) =>
        Assert.False(StaffOrderService.IsWellFormedToken(token));

    [Theory]
    [InlineData("09121234567", "09121234567")]
    [InlineData("۰۹۱۲ ۱۲۳ ۴۵۶۷", "09121234567")]
    [InlineData("+989121234567", "09121234567")]
    [InlineData("989121234567", "09121234567")]
    [InlineData("0912-123-4567", "09121234567")]
    [InlineData("0912123", null)]
    [InlineData("02112345678", null)]
    [InlineData(null, null)]
    public void Phone_IsNormalized(string? input, string? expected) =>
        Assert.Equal(expected, StaffOrderService.NormalizePhone(input));

    // ── کانبان ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData(OrderStatus.Pending, OrderStatus.Confirmed, true)]
    [InlineData(OrderStatus.Confirmed, OrderStatus.Processing, true)]
    [InlineData(OrderStatus.Processing, OrderStatus.Shipped, true)]
    [InlineData(OrderStatus.Shipped, OrderStatus.Delivered, true)]
    [InlineData(OrderStatus.Pending, OrderStatus.Processing, false)]
    [InlineData(OrderStatus.Processing, OrderStatus.Confirmed, false)]
    [InlineData(OrderStatus.Confirmed, OrderStatus.Cancelled, false)]
    [InlineData(OrderStatus.Delivered, OrderStatus.Returned, false)]
    public void Board_AllowsOnlyOneStepForward(OrderStatus from, OrderStatus to, bool expected) =>
        Assert.Equal(expected, BoardModel.IsForwardStep(from, to));

    [Fact]
    public void OrderMessage_OpenQuestion_OnlyWhilePending()
    {
        OrderMessageDto Make(OrderMessageKind kind, OrderMessageDecision decision) =>
            new(Guid.NewGuid(), true, "پشتیبانی", "متن", kind, Guid.NewGuid(), "کالا", decision, null, T0, null, null);

        Assert.True(Make(OrderMessageKind.ItemUnavailable, OrderMessageDecision.Pending).IsOpenQuestion);
        Assert.False(Make(OrderMessageKind.ItemUnavailable, OrderMessageDecision.RemoveItem).IsOpenQuestion);
        Assert.False(Make(OrderMessageKind.ItemUnavailable, OrderMessageDecision.Withdrawn).IsOpenQuestion);
        Assert.False(Make(OrderMessageKind.Message, OrderMessageDecision.None).IsOpenQuestion);
    }
}
