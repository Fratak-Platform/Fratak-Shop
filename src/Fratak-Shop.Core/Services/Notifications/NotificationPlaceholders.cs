using Fratak_Shop.Data.Entities.Notifications;

namespace Fratak_Shop.Core.Services.Notifications;

/// <summary>یک برچسب قابل استفاده در متن پیام؛ مثلاً کلید <c>FirstName</c> که در متن به‌صورت <c>{FirstName}</c> نوشته می‌شود.</summary>
/// <param name="Key">نام انگلیسی برچسب (بدون آکولاد).</param>
/// <param name="Title">عنوان فارسی که روی دکمهٔ برچسب نمایش داده می‌شود.</param>
/// <param name="Sample">مقدار نمونه برای پیش‌نمایش وقتی دادهٔ واقعی وجود ندارد.</param>
public sealed record NotificationPlaceholder(string Key, string Title, string Sample);

/// <summary>گروهی از برچسب‌ها (مثلاً «اطلاعات سفارش») برای نمایش مرتب در ویرایشگر قالب.</summary>
public sealed record NotificationPlaceholderGroup(string Title, IReadOnlyList<NotificationPlaceholder> Items);

/// <summary>
/// فهرست کامل برچسب‌های قالب پیام برای هر رویداد.
/// <para>
/// کاربرد: صفحهٔ «قالب‌های پیام» در پنل برای هر رویداد فقط برچسب‌های معتبر همان رویداد را نشان می‌دهد؛
/// کارمند روی برچسب کلیک می‌کند و <c>{Key}</c> در متن درج می‌شود. هنگام ارسال واقعی،
/// <see cref="NotificationService"/> مقدارها را از دادهٔ همان کاربر/سفارش پر می‌کند.
/// </para>
/// برای افزودن برچسب جدید: ۱) اینجا به گروه مناسب اضافه کنید؛ ۲) در <c>NotificationService.EnrichAsync</c>
/// (یا محل صدا زدن رویداد) مقدارش را پر کنید.
/// </summary>
public static class NotificationPlaceholders
{
    /// <summary>برچسب‌هایی که در همهٔ پیام‌ها قابل استفاده‌اند.</summary>
    public static readonly NotificationPlaceholderGroup Customer = new("اطلاعات مشتری",
    [
        new("FirstName", "نام", "علی"),
        new("LastName", "نام خانوادگی", "رضایی"),
        new("FullName", "نام کامل", "علی رضایی"),
        new("Name", "نام نمایشی", "علی"),
        new("Phone", "موبایل", "09121234567"),
        new("WalletBalance", "موجودی کیف پول", "۲۵۰٬۰۰۰ تومان"),
        new("LoyaltyPoints", "امتیاز باشگاه", "۱۲۰"),
    ]);

    /// <summary>برچسب‌های عمومی فروشگاه.</summary>
    public static readonly NotificationPlaceholderGroup Shop = new("فروشگاه و زمان",
    [
        new("SiteName", "نام فروشگاه", "فراتک"),
        new("Today", "تاریخ امروز", "1405/08/03"),
    ]);

    /// <summary>برچسب‌های سفارش (برای رویدادهای سفارش، مرجوعی و نظرسنجی).</summary>
    public static readonly NotificationPlaceholderGroup Order = new("اطلاعات سفارش",
    [
        new("OrderNumber", "شماره سفارش", "FR-140508-1024"),
        new("Amount", "مبلغ سفارش", "1,250,000"),
        new("AmountWords", "مبلغ به حروف", "یک میلیون و دویست و پنجاه هزار تومان"),
        new("OrderStatus", "وضعیت سفارش", "ارسال‌شده"),
        new("OrderDate", "تاریخ سفارش", "1405/08/01"),
        new("ItemCount", "تعداد کالا", "2"),
        new("ItemsSummary", "خلاصهٔ کالاها", "هندزفری هایلو T15، کابل شارژ"),
        new("ShippingMethod", "روش ارسال", "پست پیشتاز"),
        new("PaymentMethod", "روش پرداخت", "پرداخت آنلاین"),
        new("City", "شهر مقصد", "تهران"),
        new("TrackingCode", "کد رهگیری مرسوله", "123456789012345678901234"),
        new("RefundText", "متن بازگشت وجه", "مبلغ 250,000 تومان به کیف پول شما برگشت داده شد."),
    ]);

    private static readonly NotificationPlaceholderGroup Return = new("مرجوعی",
    [
        new("RequestNumber", "شمارهٔ درخواست مرجوعی", "RT-1024"),
        new("Amount", "مبلغ", "450,000"),
        new("Note", "توضیح کارشناس", "کالا پس از بررسی سالم تشخیص داده شد."),
    ]);

    private static readonly NotificationPlaceholderGroup Wallet = new("کیف پول و کارت هدیه",
    [
        new("Amount", "مبلغ", "500,000"),
        new("Balance", "موجودی جدید", "750,000"),
        new("Description", "شرح تراکنش", "شارژ آنلاین کیف پول"),
        new("Reference", "شمارهٔ پیگیری", "A1B2C3"),
    ]);

    private static readonly NotificationPlaceholderGroup Loyalty = new("باشگاه مشتریان",
    [
        new("Points", "امتیاز", "40"),
        new("Balance", "مجموع امتیاز", "320"),
        new("Description", "شرح", "خرید سفارش FR-1024"),
        new("Tier", "سطح باشگاه", "طلایی"),
        new("Reward", "جایزه", "کد تخفیف ۵۰ هزار تومانی"),
        new("FriendName", "نام دوست معرفی‌شده", "مریم"),
    ]);

    private static readonly NotificationPlaceholderGroup Security = new("امنیت",
    [
        new("Device", "دستگاه", "Chrome روی Windows"),
        new("Ip", "آی‌پی", "5.120.10.20"),
        new("Time", "زمان", "1405/08/03 - 14:30"),
    ]);

    private static readonly NotificationPlaceholderGroup Cart = new("سبد خرید رها‌شده",
    [
        new("ItemCount", "تعداد کالا", "3"),
        new("CartAmount", "مبلغ سبد", "1,800,000 تومان"),
        new("CouponCode", "کد تخفیف", "BACK10"),
        new("CouponValue", "مقدار تخفیف", "۱۰٪"),
    ]);

    private static readonly NotificationPlaceholderGroup Product = new("محصول",
    [
        new("ProductName", "نام محصول", "هندزفری بلوتوثی هایلو T15"),
        new("Price", "قیمت فعلی", "1,150,000"),
        new("OldPrice", "قیمت قبلی", "1,290,000"),
        new("Saving", "مقدار ارزان‌تر شدن", "140,000"),
    ]);

    private static readonly NotificationPlaceholderGroup Feedback = new("نظرسنجی",
    [
        new("Points", "امتیاز جایزهٔ نظرسنجی", "20"),
    ]);

    private static readonly NotificationPlaceholderGroup Ticket = new("تیکت پشتیبانی",
    [
        new("TicketNumber", "شمارهٔ تیکت", "T-1024"),
        new("Subject", "موضوع تیکت", "سؤال دربارهٔ ارسال"),
    ]);

    /// <summary>عنوان فارسی هر رویداد برای فهرست قالب‌ها.</summary>
    public static string EventTitle(NotificationEvent @event) => @event switch
    {
        NotificationEvent.Welcome => "خوش‌آمدگویی بعد از ثبت‌نام",
        NotificationEvent.OrderPlaced => "ثبت سفارش",
        NotificationEvent.OrderPaid => "پرداخت موفق سفارش",
        NotificationEvent.OrderConfirmed => "تأیید سفارش",
        NotificationEvent.OrderProcessing => "شروع آماده‌سازی سفارش",
        NotificationEvent.OrderShipped => "ارسال سفارش",
        NotificationEvent.OrderDelivered => "تحویل سفارش",
        NotificationEvent.OrderCancelled => "لغو سفارش",
        NotificationEvent.OrderReturned => "مرجوع شدن سفارش",
        NotificationEvent.PaymentFailed => "پرداخت ناموفق",
        NotificationEvent.ReturnRequested => "ثبت درخواست مرجوعی",
        NotificationEvent.ReturnApproved => "تأیید مرجوعی",
        NotificationEvent.ReturnRejected => "رد مرجوعی",
        NotificationEvent.ReturnRefunded => "بازپرداخت مرجوعی",
        NotificationEvent.OrderFeedbackRequest => "درخواست نظرسنجی سفارش",
        NotificationEvent.WalletTopUp => "شارژ کیف پول",
        NotificationEvent.WalletAdjusted => "تغییر موجودی کیف پول توسط مدیر",
        NotificationEvent.WithdrawalRequested => "ثبت درخواست برداشت",
        NotificationEvent.WithdrawalApproved => "تأیید درخواست برداشت",
        NotificationEvent.WithdrawalPaid => "واریز برداشت",
        NotificationEvent.WithdrawalRejected => "رد درخواست برداشت",
        NotificationEvent.GiftCardPurchased => "خرید کارت هدیه",
        NotificationEvent.GiftCardRedeemed => "استفاده از کارت هدیه",
        NotificationEvent.PointsEarned => "دریافت امتیاز باشگاه",
        NotificationEvent.TierUpgraded => "ارتقای سطح باشگاه",
        NotificationEvent.RewardRedeemed => "دریافت جایزهٔ باشگاه",
        NotificationEvent.ReferralJoined => "عضویت دوست معرفی‌شده",
        NotificationEvent.PasswordChanged => "تغییر رمز عبور",
        NotificationEvent.TwoFactorEnabled => "فعال شدن ورود دومرحله‌ای",
        NotificationEvent.TwoFactorDisabled => "غیرفعال شدن ورود دومرحله‌ای",
        NotificationEvent.NewDeviceLogin => "ورود از دستگاه جدید",
        NotificationEvent.AbandonedCartReminder => "یادآوری سبد خرید رها‌شده",
        NotificationEvent.BackInStock => "موجود شدن محصول",
        NotificationEvent.PriceDrop => "کاهش قیمت محصول",
        NotificationEvent.TicketCreated => "ثبت تیکت",
        NotificationEvent.TicketReplied => "پاسخ به تیکت",
        _ => @event.ToString(),
    };

    /// <summary>گروه‌بندی رویدادها برای صفحهٔ فهرست قالب‌ها.</summary>
    public static string EventGroup(NotificationEvent @event) => (int)@event switch
    {
        >= 10 and < 20 or 24 => "سفارش",
        >= 20 and < 30 => "مرجوعی",
        >= 30 and < 50 => "کیف پول و کارت هدیه",
        >= 50 and < 70 => "باشگاه مشتریان",
        >= 70 and < 90 => "امنیت حساب",
        >= 90 and < 110 => "بازاریابی",
        >= 110 and < 120 => "پشتیبانی",
        _ => "عمومی",
    };

    /// <summary>همهٔ رویدادهای قابل ویرایش (به‌جز None) به ترتیب عددی.</summary>
    public static IReadOnlyList<NotificationEvent> Events { get; } =
        Enum.GetValues<NotificationEvent>().Where(e => e != NotificationEvent.None).ToArray();

    /// <summary>آیا رویداد به یک سفارش مربوط است؟ (برای پر کردن خودکار برچسب‌های سفارش)</summary>
    public static bool IsOrderEvent(NotificationEvent @event) =>
        @event is >= NotificationEvent.OrderPlaced and <= NotificationEvent.PaymentFailed
            or >= NotificationEvent.ReturnRequested and <= NotificationEvent.OrderFeedbackRequest;

    /// <summary>
    /// گروه‌های برچسب مجاز برای یک رویداد. همیشه «مشتری» و «فروشگاه» هست و بقیه بسته به رویداد.
    /// مثال: برای <see cref="NotificationEvent.OrderShipped"/> گروه «اطلاعات سفارش» (شامل کد رهگیری) هم برمی‌گردد.
    /// </summary>
    public static IReadOnlyList<NotificationPlaceholderGroup> For(NotificationEvent @event)
    {
        var groups = new List<NotificationPlaceholderGroup> { Customer, Shop };
        if (IsOrderEvent(@event))
            groups.Add(Order);

        var extra = @event switch
        {
            >= NotificationEvent.ReturnRequested and <= NotificationEvent.ReturnRefunded => Return,
            >= NotificationEvent.WalletTopUp and <= NotificationEvent.GiftCardRedeemed => Wallet,
            >= NotificationEvent.PointsEarned and <= NotificationEvent.ReferralJoined => Loyalty,
            NotificationEvent.NewDeviceLogin => Security,
            NotificationEvent.AbandonedCartReminder => Cart,
            NotificationEvent.BackInStock or NotificationEvent.PriceDrop => Product,
            NotificationEvent.TicketCreated or NotificationEvent.TicketReplied => Ticket,
            NotificationEvent.OrderFeedbackRequest => Feedback,
            _ => null,
        };
        if (extra is not null)
            groups.Add(extra);

        return groups;
    }

    /// <summary>برچسب‌های مناسب پیام‌های آزاد (اعلان تکی/همگانی و پاسخ آماده): مشتری + فروشگاه.</summary>
    public static IReadOnlyList<NotificationPlaceholderGroup> General { get; } = [Customer, Shop];

    /// <summary>برچسب‌های پیام سفارش (پیام‌های آماده در صفحهٔ سفارش): مشتری + فروشگاه + سفارش.</summary>
    public static IReadOnlyList<NotificationPlaceholderGroup> ForOrderMessage { get; } = [Customer, Shop, Order];

    /// <summary>مقدارهای نمونهٔ همهٔ برچسب‌های یک رویداد (برای پیش‌نمایش وقتی دادهٔ واقعی نیست).</summary>
    public static Dictionary<string, string?> Samples(IEnumerable<NotificationPlaceholderGroup> groups)
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in groups.SelectMany(g => g.Items))
            values.TryAdd(item.Key, item.Sample);
        return values;
    }
}
