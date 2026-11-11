namespace Fratak_Shop.Core.Services.Assistant;

/// <summary>نوع پرسش/فرمان «دستیار مدیریت» (دور ۲۸).</summary>
public enum StaffCommandKind
{
    Unknown = 0,
    Help,
    Today,
    Sales,
    PendingOrders,
    OrderLookup,
    OutOfStock,
    LowStock,
    ReadyList,
    SupplierPending,
    ProductLookup,
    Publish,
    Unpublish,
}

/// <summary>بازهٔ گزارش فروش.</summary>
public enum StaffPeriod
{
    Today,
    Yesterday,
    Week,
    Month,
}

/// <summary>
/// یک فرمان فهمیده‌شده. Term = نام کالا (برای جست‌وجو/انتشار)، OrderNumber = شمارهٔ سفارش، Period = بازهٔ فروش.
/// مثال: «موجودی هدفون بی سیم» ← (ProductLookup, «هدفون بی سیم»).
/// </summary>
public sealed record StaffCommand(StaffCommandKind Kind, string? Term = null, string? OrderNumber = null, StaffPeriod Period = StaffPeriod.Today)
{
    /// <summary>این فرمان چیزی را تغییر می‌دهد (پیش از انجام تأیید صریح کارمند لازم است).</summary>
    public bool Changes => Kind is StaffCommandKind.Publish or StaffCommandKind.Unpublish;
}

/// <summary>
/// دور ۲۸ – فهم پرسش و فرمان کارکنان به زبان ساده (بدون هوش مصنوعی بیرونی؛ الگوهای فارسی + نرمال‌سازی <see cref="AssistantBrain.Normalize"/>).
/// <para>اصل ایمنی: هر فرمانی که تغییر می‌دهد (<see cref="StaffCommand.Changes"/>) فقط پیش‌نمایش می‌سازد و با دکمهٔ «تأیید» انجام می‌شود؛
/// دسترسی هر بخش هم در صفحهٔ «دستیار مدیریت» جداگانه بررسی می‌شود.</para>
/// <para>مثال‌ها: «کارهای امروز» ← Today؛ «فروش این هفته» ← Sales/Week؛ «سفارش 1405-120» ← OrderLookup؛ «منتشر کن پاوربانک انکر» ← Publish.</para>
/// </summary>
public static class StaffCommands
{
    private static readonly string[] PublishCues = ["منتشر کن", "منتشرش کن", "انتشار بده", "فعال کن", "فعالش کن"];
    private static readonly string[] UnpublishCues = ["غیرفعال کن", "غیر فعال کن", "غیرفعالش کن", "از انتشار خارج کن", "پنهان کن", "مخفی کن"];
    private static readonly string[] LookupCues = ["موجودی", "قیمت", "وضعیت کالای", "وضعیت کالا", "کالای", "محصول", "چندتا داریم", "چند تا داریم", "داریم"];

    /// <summary>فهم یک پیام. پیام خالی یا خیلی بلند ← Unknown.</summary>
    public static StaffCommand Parse(string? text)
    {
        var n = AssistantBrain.Normalize(text);
        if (n.Length == 0 || n.Length > 200)
            return new StaffCommand(StaffCommandKind.Unknown);

        // فرمان‌های تغییردهنده اول (تا «فعال کن» با «غیرفعال کن» اشتباه نشود، غیرفعال پیش از فعال بررسی می‌شود).
        if (Rest(n, UnpublishCues) is { } off)
            return off.Length >= 2 ? new StaffCommand(StaffCommandKind.Unpublish, off) : new StaffCommand(StaffCommandKind.Help);
        if (Rest(n, PublishCues) is { } on)
            return on.Length >= 2 ? new StaffCommand(StaffCommandKind.Publish, on) : new StaffCommand(StaffCommandKind.Help);

        if (Has(n, "راهنما", "کمک", "چه کارهایی", "چه کاری بلدی", "help", "دستورها"))
            return new StaffCommand(StaffCommandKind.Help);
        if (Has(n, "کارهای امروز", "کار امروز", "کارای امروز", "امروز چه کنم", "امروز چیکار کنم", "برنامه امروز", "اولویت"))
            return new StaffCommand(StaffCommandKind.Today);
        if (Has(n, "فروش", "درامد", "گردش"))
            return new StaffCommand(StaffCommandKind.Sales, Period: Period(n));
        if (OrderNumberIn(n) is { } number && Has(n, "سفارش", "فاکتور", "order"))
            return new StaffCommand(StaffCommandKind.OrderLookup, OrderNumber: number);
        if (Has(n, "سفارش") && Has(n, "منتظر", "در انتظار", "جدید", "تایید", "ارسال", "معطل", "مونده"))
            return new StaffCommand(StaffCommandKind.PendingOrders);
        if (Has(n, "اماده انتشار", "اماده ی انتشار", "امادهی انتشار", "اماده برای انتشار"))
            return new StaffCommand(StaffCommandKind.ReadyList);
        if (Has(n, "تامین کننده", "تامین‌کننده", "تغییرات تامین", "قیمت تامین"))
            return new StaffCommand(StaffCommandKind.SupplierPending);
        if (Has(n, "ناموجود", "تمام شده", "تموم شده"))
            return new StaffCommand(StaffCommandKind.OutOfStock);
        if (Has(n, "موجودی کم", "کم موجود", "رو به اتمام", "رو به پایان", "کم شده"))
            return new StaffCommand(StaffCommandKind.LowStock);
        if (Rest(n, LookupCues) is { Length: >= 2 } term)
            return new StaffCommand(StaffCommandKind.ProductLookup, term);
        return new StaffCommand(StaffCommandKind.Unknown, n.Length >= 2 && n.Split(' ').Length <= 5 ? n : null);
    }

    /// <summary>
    /// بازهٔ فروش از متن. مثال: «فروش دیروز» ← Yesterday؛ «فروش این هفته» ← Week؛ «فروش ماه» ← Month؛ بقیه ← Today.
    /// </summary>
    public static StaffPeriod Period(string normalized) =>
        Has(normalized, "دیروز") ? StaffPeriod.Yesterday
        : Has(normalized, "هفته", "۷ روز", "7 روز") ? StaffPeriod.Week
        : Has(normalized, "ماه", "۳۰ روز", "30 روز") ? StaffPeriod.Month
        : StaffPeriod.Today;

    /// <summary>
    /// شمارهٔ سفارش در متن (رقم‌ها با خط تیره، دست‌کم ۳ رقم). مثال: «سفارش ۱۴۰۵-۰۰۱۲ کجاست» ← «1405-0012».
    /// </summary>
    public static string? OrderNumberIn(string normalized)
    {
        foreach (var word in normalized.Split(' '))
        {
            var token = word.Trim('-', '.', '#');
            if (token.Length is >= 3 and <= 30 && token.Count(char.IsDigit) >= 3 && token.All(c => char.IsAsciiDigit(c) || c == '-' || char.IsAsciiLetter(c)))
                return token.ToUpperInvariant();
        }
        return null;
    }

    /// <summary>متن نرمال شامل یکی از عبارت‌ها (نرمال‌شده) هست؟</summary>
    private static bool Has(string normalized, params string[] phrases) =>
        phrases.Any(p => normalized.Contains(AssistantBrain.Normalize(p), StringComparison.Ordinal));

    /// <summary>
    /// اگر یکی از نشانه‌ها در متن باشد، باقی متن (بدون نشانه و واژه‌های پرکننده) را برمی‌گرداند؛ وگرنه null.
    /// مثال: («منتشر کن پاوربانک انکر», ["منتشر کن"]) ← «پاوربانک انکر».
    /// </summary>
    private static string? Rest(string normalized, string[] cues)
    {
        foreach (var cue in cues.Select(AssistantBrain.Normalize).OrderByDescending(c => c.Length))
        {
            var index = normalized.IndexOf(cue, StringComparison.Ordinal);
            if (index < 0)
                continue;
            var rest = (normalized[..index] + " " + normalized[(index + cue.Length)..]).Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(w => !Fillers.Contains(w));
            return string.Join(' ', rest).Trim();
        }
        return null;
    }

    private static readonly HashSet<string> Fillers = new(StringComparer.Ordinal)
    {
        "را", "رو", "لطفا", "لطفاً", "کالای", "کالا", "محصول", "محصولِ", "چقدره", "چقدر", "چنده", "چند", "است", "هست", "چیه", "این", "اون", "که", "ما", "از", "تا", "عدد",
    };
}
