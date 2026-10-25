using Fratak_Shop.Core.DTOs.Settings;
using Fratak_Shop.Data.Entities.Orders;

namespace Fratak_Shop.Core.Services.Orders;

/// <summary>
/// مرحله‌های زمان‌بندی سفارش. عدد هر مرحله در ستون <c>Order.SlaAlertedStage</c> ذخیره می‌شود،
/// پس ترتیب و عددها نباید عوض شوند (مرحلهٔ جدید را با عدد بزرگ‌تر اضافه کنید).
/// </summary>
public enum OrderSlaStage
{
    None = 0,

    /// <summary>ثبت خرید در تأمین‌کننده: از پرداخت (یا ثبت سفارش پرداخت در محل) تا «در حال آماده‌سازی».</summary>
    SupplierOrder = 1,

    /// <summary>آماده‌سازی و تحویل به پست: از «در حال آماده‌سازی» تا «ارسال‌شده».</summary>
    Preparation = 2,

    /// <summary>ثبت کد رهگیری: سفارش «ارسال‌شده» است ولی هنوز کد رهگیری ندارد.</summary>
    TrackingCode = 3,

    /// <summary>تأیید تحویل: از ارسال تا «تحویل‌شده» (بر اساس بیشترین روز تحویل روش ارسال).</summary>
    Delivery = 4,
}

/// <summary>وضعیت زمانی مرحلهٔ جاری سفارش.</summary>
public enum OrderSlaLevel
{
    /// <summary>سفارش در مرحلهٔ زمان‌دار نیست (منتظر پرداخت، تحویل‌شده، لغوشده...).</summary>
    None = 0,

    /// <summary>در مهلت است.</summary>
    OnTrack = 1,

    /// <summary>بخش زیادی از مهلت گذشته (بیش از درصد هشدار تنظیمات)؛ باید زودتر رسیدگی شود.</summary>
    Soon = 2,

    /// <summary>از مهلت گذشته است (دیرکرد).</summary>
    Overdue = 3,
}

/// <summary>
/// داده‌های لازم برای محاسبهٔ زمان‌بندی یک سفارش؛ عمداً جدا از موجودیت تا از کوئری سبک (Select) هم ساخته شود.
/// </summary>
/// <param name="MethodMinDays">کمترین روز تحویل روش ارسال انتخاب‌شده (اگر روش ارسال حذف شده باشد null).</param>
/// <param name="MethodMaxDays">بیشترین روز تحویل روش ارسال انتخاب‌شده.</param>
public sealed record OrderSlaInput(
    OrderStatus Status,
    PaymentMethod PaymentMethod,
    PaymentStatus PaymentStatus,
    DateTimeOffset CreatedAt,
    DateTimeOffset? PaidAt,
    DateTimeOffset? ProcessingAt,
    DateTimeOffset? ShippedAt,
    string? TrackingCode,
    int? MethodMinDays = null,
    int? MethodMaxDays = null);

/// <summary>
/// مهلت‌های قابل تنظیم (از تنظیمات فروشگاه، بخش «زمان‌بندی سفارش»).
/// مثال پیش‌فرض: ثبت خرید ۲۴ ساعت، آماده‌سازی ۴۸ ساعت، کد رهگیری ۱۲ ساعت، تحویل پستی ۳ تا ۴ روز.
/// </summary>
public sealed record OrderSlaPolicy(
    int SupplierOrderHours,
    int PreparationHours,
    int TrackingCodeHours,
    int DeliveryMinDays,
    int DeliveryMaxDays,
    int WarningPercent)
{
    /// <summary>مقدارهای پیش‌فرض (همان مقدارهای پیش‌فرض تنظیمات) برای تست و حالت بدون تنظیمات.</summary>
    public static OrderSlaPolicy Default { get; } = new(24, 48, 12, 3, 4, 75);

    /// <summary>ساخت سیاست زمان‌بندی از تنظیمات فروشگاه.</summary>
    public static OrderSlaPolicy From(StoreSettingDto settings) => new(
        settings.SlaSupplierOrderHours,
        settings.SlaPreparationHours,
        settings.SlaTrackingCodeHours,
        settings.SlaDeliveryMinDays,
        settings.SlaDeliveryMaxDays,
        settings.SlaWarningPercent);
}

/// <summary>نتیجهٔ زمان‌بندی مرحلهٔ جاری یک سفارش.</summary>
/// <param name="Stage">مرحلهٔ جاری.</param>
/// <param name="Level">در مهلت / نزدیک پایان / دیرکرد.</param>
/// <param name="StartedAt">شروع مرحله.</param>
/// <param name="Deadline">پایان مهلت مرحله.</param>
/// <param name="Remaining">زمان باقی‌مانده تا پایان مهلت (منفی یعنی دیرکرد).</param>
public sealed record OrderSlaResult(OrderSlaStage Stage, OrderSlaLevel Level, DateTimeOffset StartedAt, DateTimeOffset Deadline, TimeSpan Remaining)
{
    /// <summary>عنوان فارسی مرحله؛ مثلاً «ثبت خرید در تأمین‌کننده».</summary>
    public string StageTitle => OrderSla.StageTitle(Stage);

    /// <summary>
    /// متن کوتاه برای نشان پنل. مثال: «۳ ساعت مانده»، «۲ روز دیرکرد»، «۴۵ دقیقه مانده».
    /// </summary>
    public string RemainingText => OrderSla.Humanize(Remaining);
}

/// <summary>بازهٔ تحویل تخمینی که به مشتری نشان داده می‌شود (مثلاً «بین ۵ تا ۷ آبان»).</summary>
public sealed record OrderDeliveryEstimate(DateTimeOffset From, DateTimeOffset To);

/// <summary>
/// محاسبهٔ خالص (بدون دیتابیس) زمان‌بندی سفارش‌ها.
/// <para>
/// کاربردها: نشان «دیرکرد / نزدیک به پایان مهلت» در فهرست و جزئیات سفارش پنل، فیلتر «سفارش‌های دیرکرد»،
/// کار زمان‌بندی‌شدهٔ هشدار به کارکنان (<see cref="OrderSlaService"/>) و بازهٔ تحویل تخمینی در صفحهٔ سفارش مشتری.
/// چون خالص است، مستقیم در تست‌های واحد بررسی می‌شود.
/// </para>
/// مثال: سفارشی که ساعت ۱۰ صبح پرداخت شده و هنوز «تأییدشده» است، با مهلت ۲۴ ساعت تا ۱۰ صبح فردا وقت دارد؛
/// ساعت ۴ صبح فردا (۷۵٪ مهلت) «نزدیک به پایان مهلت» و بعد از ۱۰ صبح «دیرکرد» می‌شود.
/// </summary>
public static class OrderSla
{
    /// <summary>
    /// مرحلهٔ جاری، مهلت و وضعیت زمانی سفارش را برمی‌گرداند؛ اگر سفارش در مرحلهٔ زمان‌دار نباشد null.
    /// </summary>
    public static OrderSlaResult? Evaluate(OrderSlaInput order, OrderSlaPolicy policy, DateTimeOffset now)
    {
        var current = CurrentStage(order, policy);
        if (current is null)
            return null;

        var (stage, start, deadline) = current.Value;
        var total = deadline - start;
        var elapsed = now - start;
        var remaining = deadline - now;

        OrderSlaLevel level;
        if (remaining < TimeSpan.Zero)
            level = OrderSlaLevel.Overdue;
        else if (total > TimeSpan.Zero && elapsed.TotalSeconds >= total.TotalSeconds * Math.Clamp(policy.WarningPercent, 1, 99) / 100d)
            level = OrderSlaLevel.Soon;
        else
            level = OrderSlaLevel.OnTrack;

        return new OrderSlaResult(stage, level, start, deadline, remaining);
    }

    /// <summary>
    /// تعیین مرحلهٔ جاری و بازهٔ زمانی آن.
    /// <list type="bullet">
    /// <item>منتظر پرداخت آنلاین/کیف پول → بدون زمان‌بندی (هنوز کاری با ما نیست).</item>
    /// <item>پرداخت در محلِ ثبت‌شده، سفارش پرداخت‌شده (مثلاً کارت‌به‌کارت تأییدشده) یا «تأییدشده» → ثبت خرید در تأمین‌کننده (از پرداخت یا ثبت سفارش).</item>
    /// <item>«در حال آماده‌سازی» → آماده‌سازی تا ارسال.</item>
    /// <item>«ارسال‌شده» بدون کد رهگیری → ثبت کد رهگیری؛ با کد → تأیید تحویل.</item>
    /// </list>
    /// </summary>
    private static (OrderSlaStage Stage, DateTimeOffset Start, DateTimeOffset Deadline)? CurrentStage(OrderSlaInput order, OrderSlaPolicy policy)
    {
        switch (order.Status)
        {
            case OrderStatus.Pending when order.PaymentMethod == PaymentMethod.CashOnDelivery || order.PaymentStatus == PaymentStatus.Paid:
            case OrderStatus.Confirmed:
            {
                var start = order.PaidAt ?? order.CreatedAt;
                return (OrderSlaStage.SupplierOrder, start, start.AddHours(policy.SupplierOrderHours));
            }

            case OrderStatus.Processing:
            {
                var start = order.ProcessingAt ?? order.PaidAt ?? order.CreatedAt;
                return (OrderSlaStage.Preparation, start, start.AddHours(policy.PreparationHours));
            }

            case OrderStatus.Shipped:
            {
                var start = order.ShippedAt ?? order.ProcessingAt ?? order.PaidAt ?? order.CreatedAt;
                if (string.IsNullOrWhiteSpace(order.TrackingCode))
                    return (OrderSlaStage.TrackingCode, start, start.AddHours(policy.TrackingCodeHours));

                var (_, maxDays) = DeliveryDays(order, policy);
                return (OrderSlaStage.Delivery, start, start.AddDays(maxDays));
            }

            default:
                return null;
        }
    }

    /// <summary>
    /// کمترین و بیشترین روز تحویل: اولویت با روش ارسال سفارش است (همانی که هنگام خرید به مشتری گفته شد)،
    /// وگرنه مقدار تنظیمات. مثال: پست پیشتاز ۲ تا ۴ روز.
    /// </summary>
    public static (int Min, int Max) DeliveryDays(OrderSlaInput order, OrderSlaPolicy policy)
    {
        var min = order.MethodMinDays is > 0 ? order.MethodMinDays.Value : policy.DeliveryMinDays;
        var max = order.MethodMaxDays is > 0 ? order.MethodMaxDays.Value : policy.DeliveryMaxDays;
        return (Math.Max(0, Math.Min(min, max)), Math.Max(1, Math.Max(min, max)));
    }

    /// <summary>
    /// بازهٔ تحویل تخمینی برای مشتری. اگر ارسال شده: زمان ارسال + روزهای تحویل.
    /// اگر هنوز ارسال نشده: زمان ارسال پیش‌بینی‌شده (پایان مهلت مرحله‌های قبل) + روزهای تحویل.
    /// برای سفارش منتظر پرداخت، تحویل‌شده یا لغوشده null برمی‌گرداند.
    /// <para>مثال: پرداخت امروز، ثبت خرید ۲۴ساعت + آماده‌سازی ۴۸ساعت → ارسال حداکثر ۳ روز بعد؛ با پست ۳ تا ۴ روزه = ۶ تا ۷ روز دیگر.</para>
    /// </summary>
    public static OrderDeliveryEstimate? EstimateDelivery(OrderSlaInput order, OrderSlaPolicy policy, DateTimeOffset now)
    {
        DateTimeOffset dispatch;
        switch (order.Status)
        {
            case OrderStatus.Shipped:
                dispatch = order.ShippedAt ?? now;
                break;
            case OrderStatus.Processing:
                dispatch = (order.ProcessingAt ?? now).AddHours(policy.PreparationHours);
                break;
            case OrderStatus.Pending when order.PaymentMethod == PaymentMethod.CashOnDelivery || order.PaymentStatus == PaymentStatus.Paid:
            case OrderStatus.Confirmed:
                dispatch = (order.PaidAt ?? order.CreatedAt).AddHours(policy.SupplierOrderHours + policy.PreparationHours);
                break;
            default:
                return null;
        }

        // اگر مهلت‌ها گذشته باشد، تخمین را از «اکنون» حساب می‌کنیم تا تاریخ گذشته به مشتری نشان داده نشود.
        if (order.Status != OrderStatus.Shipped && dispatch < now)
            dispatch = now;

        var (min, max) = DeliveryDays(order, policy);
        var from = dispatch.AddDays(min);
        var to = dispatch.AddDays(max);
        if (to < now)
            to = now;
        if (from > to)
            from = to;
        return new OrderDeliveryEstimate(from, to);
    }

    /// <summary>عنوان فارسی هر مرحله (برای نشان‌ها، فیلترها و متن هشدار).</summary>
    public static string StageTitle(OrderSlaStage stage) => stage switch
    {
        OrderSlaStage.SupplierOrder => "ثبت خرید در تأمین‌کننده",
        OrderSlaStage.Preparation => "آماده‌سازی و تحویل به پست",
        OrderSlaStage.TrackingCode => "ثبت کد رهگیری",
        OrderSlaStage.Delivery => "تأیید تحویل",
        _ => "—",
    };

    /// <summary>
    /// متن خوانای زمان باقی‌مانده یا دیرکرد.
    /// مثال‌ها: ۹۰ دقیقه → «۱ ساعت و ۳۰ دقیقه مانده»؛ ۵۰- ساعت → «۲ روز و ۲ ساعت دیرکرد».
    /// اعداد با ارقام فارسی برگردانده می‌شوند.
    /// </summary>
    public static string Humanize(TimeSpan remaining)
    {
        var late = remaining < TimeSpan.Zero;
        var span = late ? remaining.Negate() : remaining;
        string text;
        if (span.TotalMinutes < 1)
            text = "کمتر از ۱ دقیقه";
        else if (span.TotalHours < 1)
            text = $"{(int)span.TotalMinutes} دقیقه";
        else if (span.TotalDays < 1)
            text = span.Minutes > 0 ? $"{(int)span.TotalHours} ساعت و {span.Minutes} دقیقه" : $"{(int)span.TotalHours} ساعت";
        else
            text = span.Hours > 0 ? $"{(int)span.TotalDays} روز و {span.Hours} ساعت" : $"{(int)span.TotalDays} روز";

        return ToPersianDigits(text) + (late ? " دیرکرد" : " مانده");
    }

    private static string ToPersianDigits(string value)
    {
        var chars = value.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
            if (chars[i] is >= '0' and <= '9')
                chars[i] = (char)('۰' + (chars[i] - '0'));
        return new string(chars);
    }
}
