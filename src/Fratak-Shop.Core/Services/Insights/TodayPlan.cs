namespace Fratak_Shop.Core.Services.Insights;

/// <summary>
/// دور ۲۸ (#۹۴): یک کار «امروز» در داشبورد. Priority کمتر = فوری‌تر (۱ = همین حالا).
/// </summary>
/// <param name="Key">شناسهٔ ثابت (برای آزمون و CSS).</param>
/// <param name="Title">متن کوتاه فارسی؛ مثال: «۳ سفارش منتظر تأیید».</param>
/// <param name="Hint">چرا مهم است / قدم بعدی؛ مثال: «مشتری منتظر است؛ زودتر از ۲ ساعت تأیید شود».</param>
/// <param name="Url">صفحهٔ انجام کار.</param>
/// <param name="Count">تعداد.</param>
/// <param name="Priority">۱ فوری، ۲ امروز، ۳ در صورت فرصت.</param>
public sealed record TodayTask(string Key, string Title, string Hint, string Url, int Count, int Priority);

/// <summary>
/// دور ۲۸ (#۹۴): ورودی «کارهای امروز» – شمارش‌ها؛ بخشی که کارمند دسترسی ندارد null است (اصلاً نشان داده نمی‌شود).
/// </summary>
public sealed record TodayInputs(
    int? OrdersToConfirm = null,
    int? OrdersAwaitingInvoice = null,
    int? OrdersToShip = null,
    int? PendingReturns = null,
    int? OpenTickets = null,
    int? PendingWithdrawals = null,
    int? NewMessages = null,
    int? ReadyToPublish = null,
    int? SupplierPending = null,
    int? OutOfStock = null,
    int? UnresolvedErrors = null,
    int? MyOverdue = null,
    int? MyDueToday = null,
    int? PendingComments = null);

/// <summary>
/// دور ۲۸ (#۹۴ – «کارهای امروز» داشبورد): به‌جای ده‌ها عدد، فهرست کوتاه و مرتب «اول این، بعد آن» برای امروز.
/// <para>
/// ترتیب: کار مشتری‌محور و پولی اول (سفارش منتظر تأیید، برداشت، مرجوعی، فاکتور خرید)، بعد کارهای عقب‌افتادهٔ خود کارمند،
/// بعد انتشار پیش‌نویس‌های آماده و تغییرات تأمین‌کننده، و در آخر کارهای «در صورت فرصت». کار با تعداد صفر نمی‌آید.
/// حداکثر <see cref="Max"/> کار؛ خالص و آزمون‌پذیر.
/// </para>
/// مثال: (۲ سفارش منتظر، ۵ آمادهٔ انتشار) ← [«۲ سفارش منتظر تأیید» (فوری)، «۵ کالای آمادهٔ انتشار» (امروز)].
/// </summary>
public static class TodayPlan
{
    public const int Max = 7;

    public static IReadOnlyList<TodayTask> Build(TodayInputs input)
    {
        var tasks = new List<TodayTask>();

        void Add(int? count, string key, string title, string hint, string url, int priority)
        {
            if (count is > 0)
                tasks.Add(new TodayTask(key, $"{Digits(count.Value)} {title}", hint, url, count.Value, priority));
        }

        Add(input.OrdersToConfirm, "confirm", "سفارش منتظر تأیید", "مشتری منتظر است؛ هرچه زودتر تأیید شود.", "/admin/orders?status=Pending", 1);
        Add(input.PendingWithdrawals, "withdraw", "درخواست برداشت از کیف پول", "پول مشتری است؛ امروز رسیدگی شود.", "/admin/finance/withdrawals", 1);
        Add(input.MyOverdue, "my-overdue", "کار عقب‌افتادهٔ شما", "موعدش گذشته است.", "/admin/tasks", 1);
        Add(input.PendingReturns, "returns", "درخواست مرجوعی", "پاسخ سریع اعتماد مشتری را نگه می‌دارد.", "/admin/returns", 1);
        Add(input.OrdersAwaitingInvoice, "invoice", "سفارش بدون فاکتور خرید", "بدون فاکتور خرید، آماده‌سازی شروع نمی‌شود.", "/admin/orders?invoice=missing", 2);
        Add(input.OrdersToShip, "ship", "سفارش آمادهٔ ارسال", "تحویل به پست/پیک و ثبت کد رهگیری.", "/admin/orders?status=Processing", 2);
        Add(input.OpenTickets, "tickets", "تیکت باز", "پاسخ به پرسش مشتری‌ها.", "/admin/tickets", 2);
        Add(input.MyDueToday, "my-today", "کار امروز شما", "موعدش امروز است.", "/admin/tasks", 2);
        Add(input.ReadyToPublish, "ready", "کالای آمادهٔ انتشار", "دستیار کامل کرده؛ فقط بازبینی و انتشار.", "/admin/products?missing=ready", 2);
        Add(input.SupplierPending, "supplier", "تغییر تأمین‌کننده منتظر تأیید", "قیمت/موجودی تازهٔ تأمین‌کننده را تأیید یا رد کنید.", "/admin/products/supplier-sync", 2);
        Add(input.UnresolvedErrors, "errors", "خطای سامانه", "یک نگاه کوتاه به خطاهای تازه.", "/admin/system/errors", 3);
        Add(input.NewMessages, "messages", "پیام جدید", "پیام‌های فرم تماس.", "/admin/messages", 3);
        Add(input.PendingComments, "comments", "نظر منتظر تأیید", "نظرهای تازهٔ مشتری‌ها.", "/admin/comments", 3);
        Add(input.OutOfStock, "oos", "کالای ناموجود", "سفارش خرید یا پنهان کردن کالا.", "/admin/products?stock=out", 3);

        // مرتب‌سازی پایدار: اولویت، سپس ترتیب بالا (اهمیت کسب‌وکار).
        return tasks.Select((t, i) => (t, i)).OrderBy(x => x.t.Priority).ThenBy(x => x.i).Select(x => x.t).Take(Max).ToList();
    }

    // ارقام فارسی برای متن کوتاه.
    private static string Digits(int value) =>
        new(value.ToString(System.Globalization.CultureInfo.InvariantCulture).Select(c => c is >= '0' and <= '9' ? (char)('۰' + (c - '0')) : c).ToArray());
}
