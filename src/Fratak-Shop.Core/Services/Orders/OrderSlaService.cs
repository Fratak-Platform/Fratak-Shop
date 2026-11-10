using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Core.Services.Settings;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Admin;
using Fratak_Shop.Data.Entities.Orders;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Core.Services.Orders;

/// <summary>خلاصهٔ زمان‌بندی سفارش‌های باز برای داشبورد و نشان منو.</summary>
/// <param name="Overdue">تعداد سفارش‌های دیرکرد.</param>
/// <param name="Soon">تعداد سفارش‌های نزدیک به پایان مهلت.</param>
/// <param name="OverdueByStage">تعداد دیرکردها به تفکیک مرحله (برای «۳ سفارش منتظر کد رهگیری»).</param>
public sealed record OrderSlaSummary(int Overdue, int Soon, IReadOnlyDictionary<OrderSlaStage, int> OverdueByStage)
{
    public static OrderSlaSummary Empty { get; } = new(0, 0, new Dictionary<OrderSlaStage, int>());
}

/// <summary>
/// سرویس زمان‌بندی سفارش‌ها روی دیتابیس (محاسبهٔ خالص در <see cref="OrderSla"/> است).
/// <para>
/// کاربردها:
/// ۱) فهرست سفارش‌های پنل: نشان زمان‌بندی هر ردیف و فیلتر «دیرکرد / نزدیک به پایان مهلت».
/// ۲) داشبورد و نشان منو: تعداد سفارش‌های دیرکرد.
/// ۳) کار پس‌زمینه (<c>OrderMaintenanceService</c>): برای هر سفارشی که از مهلت مرحله‌اش گذشته، فقط یک بار
///    به کارکنان دارای مجوز «مدیریت سفارش‌ها» اعلان می‌فرستد.
/// </para>
/// چرا محاسبه در حافظه؟ مهلت مرحلهٔ «تحویل» به روزهای تحویل روش ارسال هر سفارش بستگی دارد و یکسان نگه داشتن
/// منطق پنل، فیلتر و هشدار مهم‌تر است. فقط سفارش‌های باز (چند صد ردیف سبک) خوانده می‌شوند.
/// </summary>
public interface IOrderSlaService
{
    /// <summary>آیا زمان‌بندی در تنظیمات فعال است؟</summary>
    Task<bool> IsEnabledAsync();

    /// <summary>تعداد سفارش‌های دیرکرد و نزدیک به پایان مهلت.</summary>
    Task<OrderSlaSummary> GetSummaryAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// شناسهٔ سفارش‌هایی که دقیقاً در این وضعیت‌اند (برای فیلتر فهرست). مثال: <c>Overdue</c> → سفارش‌های دیرکرد.
    /// </summary>
    Task<List<Guid>> GetIdsAsync(OrderSlaLevel level, CancellationToken cancellationToken = default);

    /// <summary>زمان‌بندی چند سفارش مشخص (برای نشان ردیف‌های یک صفحه از فهرست).</summary>
    Task<Dictionary<Guid, OrderSlaResult>> EvaluateAsync(IReadOnlyCollection<Guid> orderIds, CancellationToken cancellationToken = default);

    /// <summary>زمان‌بندی و بازهٔ تحویل تخمینی یک سفارش (برای جزئیات سفارش در پنل و صفحهٔ مشتری).</summary>
    Task<(OrderSlaResult? Sla, OrderDeliveryEstimate? Estimate)> GetForOrderAsync(Guid orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// اعلان دیرکرد به کارکنان؛ هر سفارش برای هر مرحله فقط یک بار. اگر هم‌زمان تعداد زیادی دیرکرد جدید باشد
    /// (بیش از ۱۰)، یک اعلان خلاصه با لینک فیلتر دیرکردها فرستاده می‌شود تا زنگوله پر نشود.
    /// </summary>
    /// <returns>تعداد سفارش‌هایی که برایشان هشدار ثبت شد.</returns>
    Task<int> AlertOverdueAsync(DateTimeOffset now, CancellationToken cancellationToken = default);
}

public class OrderSlaService(
    FratakShopDbContext context,
    IStoreSettingService settings,
    IStaffNotificationService staff) : IOrderSlaService
{
    /// <summary>سقف سفارش‌های بازی که در یک محاسبه خوانده می‌شود (محافظ کارایی).</summary>
    private const int MaxOpenOrders = 5000;

    /// <summary>بیشتر از این تعداد دیرکرد جدید، به‌جای اعلان تکی یک اعلان خلاصه می‌رود.</summary>
    private const int SingleAlertLimit = 10;

    private sealed record OpenOrder(Guid Id, string OrderNumber, string CustomerName, int AlertedStage, OrderSlaInput Input);

    public async Task<bool> IsEnabledAsync() => (await settings.GetAsync()).IsOrderSlaEnabled;

    public async Task<OrderSlaSummary> GetSummaryAsync(CancellationToken cancellationToken = default)
    {
        var (policy, enabled) = await PolicyAsync(cancellationToken);
        if (!enabled)
            return OrderSlaSummary.Empty;

        var now = DateTimeOffset.UtcNow;
        var results = (await LoadOpenAsync(null, cancellationToken))
            .Select(o => OrderSla.Evaluate(o.Input, policy, now))
            .Where(r => r is not null)
            .Select(r => r!)
            .ToList();

        var byStage = results.Where(r => r.Level == OrderSlaLevel.Overdue)
            .GroupBy(r => r.Stage)
            .ToDictionary(g => g.Key, g => g.Count());
        return new OrderSlaSummary(results.Count(r => r.Level == OrderSlaLevel.Overdue), results.Count(r => r.Level == OrderSlaLevel.Soon), byStage);
    }

    public async Task<List<Guid>> GetIdsAsync(OrderSlaLevel level, CancellationToken cancellationToken = default)
    {
        var (policy, enabled) = await PolicyAsync(cancellationToken);
        if (!enabled)
            return [];

        var now = DateTimeOffset.UtcNow;
        return (await LoadOpenAsync(null, cancellationToken))
            .Where(o => OrderSla.Evaluate(o.Input, policy, now)?.Level == level)
            .Select(o => o.Id)
            .ToList();
    }

    public async Task<Dictionary<Guid, OrderSlaResult>> EvaluateAsync(IReadOnlyCollection<Guid> orderIds, CancellationToken cancellationToken = default)
    {
        var map = new Dictionary<Guid, OrderSlaResult>();
        if (orderIds.Count == 0)
            return map;

        var (policy, enabled) = await PolicyAsync(cancellationToken);
        if (!enabled)
            return map;

        var now = DateTimeOffset.UtcNow;
        foreach (var order in await LoadOpenAsync(orderIds, cancellationToken))
            if (OrderSla.Evaluate(order.Input, policy, now) is { } result)
                map[order.Id] = result;
        return map;
    }

    public async Task<(OrderSlaResult? Sla, OrderDeliveryEstimate? Estimate)> GetForOrderAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        var settingsDto = await settings.GetAsync();
        var policy = OrderSlaPolicy.From(settingsDto);
        var order = (await Project(context.Orders.Where(o => o.Id == orderId)).ToListAsync(cancellationToken)).FirstOrDefault();
        if (order is null)
            return (null, null);

        var now = DateTimeOffset.UtcNow;
        // بازهٔ تحویل تخمینی همیشه نشان داده می‌شود؛ نشان دیرکرد فقط وقتی زمان‌بندی فعال است.
        var sla = settingsDto.IsOrderSlaEnabled ? OrderSla.Evaluate(order.Input, policy, now) : null;

        // دور ۲۴ (پیشنهاد ۵۹): بازهٔ تحویل از تاریخچهٔ واقعی همین استان و روش (اگر دادهٔ کافی باشد)؛ مهلت SLA دست نمی‌خورد.
        var input = order.Input;
        if (input.Status is OrderStatus.Confirmed or OrderStatus.Processing or OrderStatus.Shipped or OrderStatus.Pending)
        {
            var target = await context.Orders.Where(o => o.Id == orderId).Select(o => new { o.ShippingMethodId, o.ShippingProvince })
                .FirstAsync(cancellationToken);
            if (await DeliveryEta.LearnAsync(context, target.ShippingMethodId, target.ShippingProvince, cancellationToken) is { } learned)
                input = input with { MethodMinDays = learned.Min, MethodMaxDays = learned.Max };
        }
        return (sla, OrderSla.EstimateDelivery(input, policy, now));
    }

    public async Task<int> AlertOverdueAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var (policy, enabled) = await PolicyAsync(cancellationToken);
        if (!enabled)
            return 0;

        var fresh = (await LoadOpenAsync(null, cancellationToken))
            .Select(o => (Order: o, Result: OrderSla.Evaluate(o.Input, policy, now)))
            .Where(x => x.Result is { Level: OrderSlaLevel.Overdue } && (int)x.Result.Stage > x.Order.AlertedStage)
            .ToList();
        if (fresh.Count == 0)
            return 0;

        if (fresh.Count > SingleAlertLimit)
        {
            await staff.NotifyAsync(new StaffAlert(
                StaffNotificationKind.Alert,
                $"{fresh.Count:N0} سفارش از مهلت زمان‌بندی گذشته‌اند",
                string.Join("، ", fresh.GroupBy(x => x.Result!.Stage).Select(g => $"{OrderSla.StageTitle(g.Key)}: {g.Count():N0}")),
                "/admin/orders?sla=overdue",
                [AdminPermissions.OrdersManage]));
        }
        else
        {
            foreach (var (order, result) in fresh)
                await staff.NotifyAsync(new StaffAlert(
                    StaffNotificationKind.Alert,
                    $"دیرکرد سفارش {order.OrderNumber}",
                    $"{result!.StageTitle} – {result.RemainingText} ({order.CustomerName})",
                    $"/admin/orders/{order.Id}",
                    [AdminPermissions.OrdersManage]));
        }

        // ثبت مرحلهٔ هشدارداده‌شده تا در اجرای بعدی تکرار نشود (فقط رو به جلو).
        foreach (var group in fresh.GroupBy(x => (int)x.Result!.Stage))
        {
            var stage = group.Key;
            var ids = group.Select(x => x.Order.Id).ToList();
            await context.Orders
                .Where(o => ids.Contains(o.Id) && o.SlaAlertedStage < stage)
                .ExecuteUpdateAsync(s => s.SetProperty(o => o.SlaAlertedStage, stage), cancellationToken);
        }

        return fresh.Count;
    }

    private async Task<(OrderSlaPolicy Policy, bool Enabled)> PolicyAsync(CancellationToken cancellationToken)
    {
        var dto = await settings.GetAsync(cancellationToken);
        return (OrderSlaPolicy.From(dto), dto.IsOrderSlaEnabled);
    }

    /// <summary>
    /// سفارش‌های باز (زمان‌دار) را به‌صورت سبک می‌خواند. سفارش‌های منتظر پرداخت آنلاین، تحویل‌شده و لغوشده
    /// اصلاً خوانده نمی‌شوند.
    /// </summary>
    private async Task<List<OpenOrder>> LoadOpenAsync(IReadOnlyCollection<Guid>? ids, CancellationToken cancellationToken)
    {
        var query = context.Orders.Where(o =>
            o.Status == OrderStatus.Confirmed || o.Status == OrderStatus.Processing || o.Status == OrderStatus.Shipped
            || (o.Status == OrderStatus.Pending && (o.PaymentMethod == PaymentMethod.CashOnDelivery || o.PaymentStatus == PaymentStatus.Paid)));
        if (ids is not null)
            query = query.Where(o => ids.Contains(o.Id));

        return await Project(query.OrderBy(o => o.CreatedAt).Take(MaxOpenOrders)).ToListAsync(cancellationToken);
    }

    private IQueryable<OpenOrder> Project(IQueryable<Order> query) => query.Select(o => new OpenOrder(
        o.Id,
        o.OrderNumber,
        o.CustomerName,
        o.SlaAlertedStage,
        new OrderSlaInput(
            o.Status, o.PaymentMethod, o.PaymentStatus, o.CreatedAt, o.PaidAt, o.ProcessingAt, o.ShippedAt, o.TrackingCode,
            context.ShippingMethods.Where(m => m.Id == o.ShippingMethodId).Select(m => (int?)m.MinDeliveryDays).FirstOrDefault(),
            context.ShippingMethods.Where(m => m.Id == o.ShippingMethodId).Select(m => (int?)m.MaxDeliveryDays).FirstOrDefault())));
}
