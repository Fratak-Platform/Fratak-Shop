using Fratak_Shop.Core.Services.Learning;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Orders;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Core.Services.Orders;

/// <summary>سطح ریسک سفارش.</summary>
public enum OrderRiskLevel
{
    Low = 0,
    Medium = 1,
    High = 2,
}

/// <summary>نتیجهٔ ارزیابی: احتمال (۰ تا ۱)، سطح و دلیل‌های فارسی. مثال: (۰٫۴۲، High، [«حساب تازه»، «پرداخت در محل»]).</summary>
public sealed record OrderRisk(double Probability, OrderRiskLevel Level, IReadOnlyList<string> Reasons, bool Learned)
{
    /// <summary>درصد برای نمایش. مثال: ۴۲.</summary>
    public int Percent => (int)Math.Round(Probability * 100);
}

/// <summary>
/// ویژگی‌های ریسک یک سفارش (همه از دادهٔ خود سایت).
/// </summary>
/// <param name="AccountAgeHours">سن حساب هنگام سفارش (ساعت)؛ مهمان/نامعلوم = ۰.</param>
/// <param name="PriorOrders">سفارش‌های قبلی همین مشتری.</param>
/// <param name="PriorBad">سفارش‌های قبلی مرجوع‌شده یا لغوشده پس از پرداخت/ارسال.</param>
/// <param name="Amount">مبلغ قابل پرداخت.</param>
/// <param name="TypicalAmount">میانهٔ مبلغ سفارش‌های سایت.</param>
/// <param name="UsedCoupon">کوپن استفاده شده.</param>
/// <param name="PhoneMismatch">تلفن گیرنده با تلفن مشتری فرق دارد.</param>
/// <param name="IranHour">ساعت ثبت به وقت ایران.</param>
/// <param name="CashOnDelivery">پرداخت در محل.</param>
/// <param name="SharedAddressAccounts">تعداد حساب‌های دیگری که ۹۰ روز اخیر به همین کد پستی سفارش داده‌اند.</param>
public sealed record OrderRiskSignals(double AccountAgeHours, int PriorOrders, int PriorBad, decimal Amount, decimal TypicalAmount, bool UsedCoupon,
    bool PhoneMismatch, int IranHour, bool CashOnDelivery, int SharedAddressAccounts);

/// <summary>
/// دور ۲۴ (پیشنهاد ۵۵): امتیاز ریسک سفارش (تقلب، سفارش جعلی پرداخت در محل، سوءاستفاده از کوپن با چند حساب، مرجوعی پرتکرار).
/// <para>
/// مدل: رگرسیون لجستیک داخلی (<see cref="LogisticModel"/>) که از سفارش‌های تمام‌شدهٔ خود سایت یاد می‌گیرد
/// (تحویل‌شده = خوب؛ مرجوعی یا لغو پس از پرداخت/ارسال = بد). تا وقتی دادهٔ کافی نیست (کمتر از ۲۰ سفارش بد)،
/// وزن‌های پیش‌فرض محافظه‌کار قانون‌محور استفاده می‌شوند. فقط نشان راهنما برای ادمین است؛ هیچ سفارشی خودکار رد نمی‌شود.
/// </para>
/// <para>مثال: حساب ۲ ساعته، اولین سفارش، پرداخت در محل، ۳ برابر میانهٔ خرید ← «ریسک زیاد» با دلیل‌ها.</para>
/// <para>محل استفاده: /admin/orders (نشان ردیف) و /admin/orders/{id} (نشان و دلیل‌ها).</para>
/// </summary>
public interface IOrderRiskService
{
    /// <summary>ارزیابی چند سفارش. سفارش ناموجود در خروجی نیست.</summary>
    Task<Dictionary<Guid, OrderRisk>> AssessAsync(IReadOnlyCollection<Guid> orderIds, CancellationToken cancellationToken = default);
}

/// <summary>محاسبهٔ خالص ویژگی‌ها و نتیجه (بدون دیتابیس؛ مستقیم در تست واحد).</summary>
public static class OrderRiskRules
{
    /// <summary>نام فارسی هر ویژگی (هم‌ترتیب با <see cref="Features"/>).</summary>
    public static readonly string[] Titles =
    [
        "حساب تازه (کمتر از ۲۴ ساعت)", "اولین سفارش", "سابقهٔ مرجوعی/لغو", "مبلغ بالاتر از معمول", "استفاده از کوپن",
        "تلفن گیرنده متفاوت", "ثبت در نیمه‌شب", "پرداخت در محل", "چند حساب با یک نشانی",
    ];

    /// <summary>
    /// وزن‌های پیش‌فرض (وقتی هنوز دادهٔ یادگیری کافی نیست). نرخ پایهٔ فرضی ۵٪.
    /// مثال: مشتری قدیمی با پرداخت آنلاین ≈ ۵٪ (کم)؛ حساب تازه + اولین سفارش ≈ ۱۱٪ (کم)؛ + پرداخت در محل ≈ ۲۵٪ (متوسط)؛
    /// + مبلغ ۳ برابر معمول ≈ ۴۴٪ (زیاد).
    /// </summary>
    public static LogisticModel Default { get; } = new(-3.0, [0.6, 0.3, 2.5, 0.8, 0.4, 0.5, 0.4, 1.0, 0.7], 0.05);

    /// <summary>
    /// بردار ویژگی: [حساب تازه، اولین سفارش، نرخ بد هموارشده، log نسبت مبلغ (فقط بالاتر)، کوپن، تلفن متفاوت، نیمه‌شب (۱ تا ۵)، پرداخت در محل، حساب‌های هم‌نشانی (حداکثر ۳)].
    /// </summary>
    public static double[] Features(OrderRiskSignals s)
    {
        var ratio = s.TypicalAmount > 0 && s.Amount > 0 ? Math.Log((double)(s.Amount / s.TypicalAmount)) : 0;
        return
        [
            s.AccountAgeHours < 24 ? 1 : 0,
            s.PriorOrders == 0 ? 1 : 0,
            s.PriorBad / (s.PriorOrders + 2.0),
            Math.Clamp(ratio, 0, 2.5),
            s.UsedCoupon ? 1 : 0,
            s.PhoneMismatch ? 1 : 0,
            s.IranHour is >= 1 and <= 5 ? 1 : 0,
            s.CashOnDelivery ? 1 : 0,
            Math.Min(3, Math.Max(0, s.SharedAddressAccounts)),
        ];
    }

    /// <summary>
    /// نتیجه از مدل: سطح «زیاد» وقتی احتمال ≥ max(۰٫۳۰، ۴× نرخ پایه)، «متوسط» ≥ max(۰٫۱۲، ۲× نرخ پایه).
    /// دلیل‌ها = ویژگی‌هایی که سهم مثبتشان ≥ ۰٫۳ است (پرسهم‌ترین اول).
    /// </summary>
    public static OrderRisk Assess(OrderRiskSignals signals, LogisticModel model, bool learned)
    {
        var features = Features(signals);
        var probability = model.Predict(features);
        var level = probability >= Math.Max(0.30, 4 * model.BaseRate) ? OrderRiskLevel.High
            : probability >= Math.Max(0.12, 2 * model.BaseRate) ? OrderRiskLevel.Medium
            : OrderRiskLevel.Low;
        var reasons = model.Contributions(features)
            .Select((c, i) => (Contribution: c, Index: i))
            .Where(x => x.Contribution >= 0.3 && x.Index < Titles.Length)
            .OrderByDescending(x => x.Contribution)
            .Select(x => Titles[x.Index])
            .ToList();
        return new OrderRisk(Math.Round(probability, 3), level, reasons, learned);
    }

    /// <summary>برچسب آموزش: مرجوعی، یا لغو پس از پرداخت یا ارسال (سفارش واقعاً مشکل‌دار).</summary>
    public static bool IsBad(OrderStatus status, DateTimeOffset? paidAt, DateTimeOffset? shippedAt) =>
        status == OrderStatus.Returned || (status == OrderStatus.Cancelled && (paidAt is not null || shippedAt is not null));
}

/// <inheritdoc />
public sealed class OrderRiskService(FratakShopDbContext context) : IOrderRiskService
{
    private sealed record Snapshot(LogisticModel Model, bool Learned, decimal Typical, DateTimeOffset BuiltAt);

    private static volatile Snapshot? _cache;
    private static readonly SemaphoreSlim Gate = new(1, 1);

    /// <summary>عمر مدل در حافظه.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(12);

    private static readonly TimeSpan IranOffset = TimeSpan.FromHours(3.5);

    private sealed record Row(Guid Id, DateTimeOffset CreatedAt, DateTimeOffset? UserCreatedAt, int PriorOrders, int PriorBad, decimal Amount,
        bool Coupon, string CustomerPhone, string ShippingPhone, PaymentMethod Method, int Shared, OrderStatus Status, DateTimeOffset? PaidAt,
        DateTimeOffset? ShippedAt);

    /// <inheritdoc />
    public async Task<Dictionary<Guid, OrderRisk>> AssessAsync(IReadOnlyCollection<Guid> orderIds, CancellationToken cancellationToken = default)
    {
        var result = new Dictionary<Guid, OrderRisk>();
        if (orderIds.Count == 0)
            return result;
        var snapshot = await ModelAsync(cancellationToken);
        var ids = orderIds.Distinct().Take(200).ToList();
        foreach (var row in await RowsAsync(context.Orders.Where(o => ids.Contains(o.Id)), cancellationToken))
            result[row.Id] = OrderRiskRules.Assess(Signals(row, snapshot.Typical), snapshot.Model, snapshot.Learned);
        return result;
    }

    private static OrderRiskSignals Signals(Row row, decimal typical) => new(
        row.UserCreatedAt is { } created ? Math.Max(0, (row.CreatedAt - created).TotalHours) : 0,
        row.PriorOrders,
        row.PriorBad,
        row.Amount,
        typical,
        row.Coupon,
        Digits(row.ShippingPhone) != Digits(row.CustomerPhone),
        row.CreatedAt.ToOffset(IranOffset).Hour,
        row.Method == PaymentMethod.CashOnDelivery,
        row.Shared);

    private static string Digits(string? phone)
    {
        var digits = new string((phone ?? "").Where(char.IsAsciiDigit).ToArray());
        return digits.Length > 10 ? digits[^10..] : digits;
    }

    private Task<List<Row>> RowsAsync(IQueryable<Order> query, CancellationToken cancellationToken) =>
        query.AsNoTracking().Select(o => new Row(
            o.Id,
            o.CreatedAt,
            o.User != null ? (DateTimeOffset?)o.User.CreatedAt : null,
            context.Orders.Count(x => o.UserId != null && x.UserId == o.UserId && x.CreatedAt < o.CreatedAt && x.Status != OrderStatus.Cancelled),
            context.Orders.Count(x => o.UserId != null && x.UserId == o.UserId && x.CreatedAt < o.CreatedAt
                                      && (x.Status == OrderStatus.Returned || (x.Status == OrderStatus.Cancelled && (x.PaidAt != null || x.ShippedAt != null)))),
            o.PayableAmount,
            o.CouponId != null,
            o.CustomerPhone,
            o.ShippingPhone,
            o.PaymentMethod,
            context.Orders.Where(x => o.ShippingPostalCode != "" && x.ShippingPostalCode == o.ShippingPostalCode && x.UserId != null && x.UserId != o.UserId
                                      && x.CreatedAt < o.CreatedAt && x.CreatedAt > o.CreatedAt.AddDays(-90))
                .Select(x => x.UserId).Distinct().Count(),
            o.Status,
            o.PaidAt,
            o.ShippedAt)).ToListAsync(cancellationToken);

    private async Task<Snapshot> ModelAsync(CancellationToken cancellationToken)
    {
        if (_cache is { } cached && DateTimeOffset.UtcNow - cached.BuiltAt < Lifetime)
            return cached;
        await Gate.WaitAsync(cancellationToken);
        try
        {
            if (_cache is { } again && DateTimeOffset.UtcNow - again.BuiltAt < Lifetime)
                return again;
            var amounts = await context.Orders.AsNoTracking()
                .Where(o => o.Status != OrderStatus.Cancelled && o.PayableAmount > 0)
                .OrderByDescending(o => o.CreatedAt).Take(2000).Select(o => o.PayableAmount).ToListAsync(cancellationToken);
            amounts.Sort();
            var typical = amounts.Count == 0 ? 0 : amounts[amounts.Count / 2];

            var finished = await RowsAsync(context.Orders
                .Where(o => o.Status == OrderStatus.Delivered || o.Status == OrderStatus.Returned
                            || (o.Status == OrderStatus.Cancelled && (o.PaidAt != null || o.ShippedAt != null)))
                .OrderByDescending(o => o.CreatedAt).Take(3000), cancellationToken);
            var samples = finished
                .Select(r => (OrderRiskRules.Features(Signals(r, typical)), OrderRiskRules.IsBad(r.Status, r.PaidAt, r.ShippedAt)))
                .ToList();
            var learned = LogisticModel.Train(samples);
            var snapshot = new Snapshot(learned ?? OrderRiskRules.Default, learned is not null, typical, DateTimeOffset.UtcNow);
            _cache = snapshot;
            return snapshot;
        }
        finally
        {
            Gate.Release();
        }
    }
}
