using System.Text.RegularExpressions;
using Fratak_Shop.Core.Services.Seo.Engine;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Notifications;
using Fratak_Shop.Data.Entities.Orders;
using Fratak_Shop.Data.Entities.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Fratak_Shop.Core.Services.Support;

/// <summary>
/// دور ۲۴ (پیشنهاد ۲۷): موضوع تیکت که از متن مشتری تشخیص داده می‌شود.
/// مثال: «سفارشم کی می‌رسه؟ کد رهگیری ندارم» ← <see cref="Shipping"/>.
/// </summary>
public enum TicketTopic
{
    General = 0,
    OrderStatus = 1,
    Shipping = 2,
    Return = 3,
    Payment = 4,
    Product = 5,
    Account = 6,
}

/// <summary>
/// یک پیش‌نویس پاسخ برای کارمند پشتیبانی. هرگز خودکار فرستاده نمی‌شود؛ کارمند می‌خواند، ویرایش می‌کند و می‌فرستد.
/// </summary>
/// <param name="Title">عنوان کوتاه دکمه. مثال: «وضعیت سفارش FR-1024».</param>
/// <param name="Body">متن پیشنهادی (برچسب {FirstName} هنگام ارسال با نام مشتری پر می‌شود).</param>
/// <param name="Source">منبع: «اطلاعات سفارش»، «پاسخ تیکت مشابه»، «پیام آماده» یا «پرسش‌های متداول».</param>
/// <param name="Score">میزان تطابق ۰ تا ۱ (اطلاعات سفارش همیشه ۱).</param>
public sealed record TicketReplySuggestion(string Title, string Body, string Source, double Score);

/// <summary>خروجی مشاور پاسخ: موضوع تشخیص‌داده‌شده + حداکثر ۳ پیش‌نویس.</summary>
public sealed record TicketReplyAdvice(TicketTopic Topic, string TopicTitle, IReadOnlyList<TicketReplySuggestion> Suggestions)
{
    /// <summary>خروجی خالی (تیکت ناموجود).</summary>
    public static readonly TicketReplyAdvice Empty = new(TicketTopic.General, "عمومی", []);
}

/// <summary>
/// دور ۲۴ (پیشنهاد ۲۷): «پیشنهاد پاسخ هوشمند» در صفحهٔ تیکت پنل.
/// <para>
/// منابع (همه داخلی، بدون هوش مصنوعی بیرونی):
/// ۱) اطلاعات واقعی سفارش همان مشتری (تیکت متصل به سفارش، شمارهٔ سفارش داخل متن یا آخرین سفارش برای موضوع ارسال/مرجوعی)؛
/// ۲) «یادگیری از پاسخ‌های قبلی»: نزدیک‌ترین تیکت پاسخ‌داده‌شدهٔ گذشته با شباهت TF-IDF و اولین پاسخ کارمند به آن
///    (نام مشتری قبلی ← {FirstName} و عددهای بلند/شماره سفارش‌ها پوشانده می‌شود تا اطلاعات شخص دیگری نشت نکند)؛
/// ۳) «پیام‌های آماده» تیکت که بیشترین شباهت را دارند؛ ۴) پاسخ «پرسش‌های متداول».
/// </para>
/// محل استفاده: <c>/admin/tickets/{id}</c> (کارت «پیشنهاد پاسخ هوشمند» بالای فرم پاسخ).
/// </summary>
public interface ITicketReplyAdvisor
{
    /// <summary>پیش‌نویس‌های پاسخ یک تیکت. تیکت ناموجود ← <see cref="TicketReplyAdvice.Empty"/>.</summary>
    Task<TicketReplyAdvice> SuggestAsync(Guid ticketId, CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="ITicketReplyAdvisor"/>
public sealed partial class TicketReplyAdvisor(FratakShopDbContext context, IMemoryCache cache) : ITicketReplyAdvisor
{
    /// <summary>حداقل شباهت تیکت قبلی تا پاسخش پیشنهاد شود.</summary>
    public const double MinSimilarTicket = 0.30;

    /// <summary>حداقل شباهت پیام آماده / پرسش متداول.</summary>
    public const double MinLibrary = 0.20;

    /// <summary>تعداد تیکت‌های پاسخ‌داده‌شدهٔ اخیر که برای «یادگیری» خوانده می‌شوند (کش ۱۰ دقیقه).</summary>
    public const int HistorySize = 400;

    private const string HistoryKey = "support:reply-history";

    /// <inheritdoc />
    public async Task<TicketReplyAdvice> SuggestAsync(Guid ticketId, CancellationToken cancellationToken = default)
    {
        var ticket = await context.SupportTickets.Where(t => t.Id == ticketId)
            .Select(t => new { t.Id, t.Subject, t.UserId, t.OrderId })
            .FirstOrDefaultAsync(cancellationToken);
        if (ticket is null)
            return TicketReplyAdvice.Empty;

        var customerText = await context.SupportTicketMessages.Where(m => m.TicketId == ticketId && !m.IsStaff)
            .OrderByDescending(m => m.CreatedAt).Take(5).Select(m => m.Content).ToListAsync(cancellationToken);
        var text = Clip(ticket.Subject + "\n" + string.Join("\n", customerText), 3000);
        var topic = DetectTopic(text);
        var suggestions = new List<TicketReplySuggestion>();

        // ۱) اطلاعات سفارش: فقط سفارش‌های خود صاحب تیکت (هرگز سفارش مشتری دیگر).
        var order = await FindOrderAsync(ticket.UserId, ticket.OrderId, text, topic, cancellationToken);
        if (order is not null)
            suggestions.Add(new TicketReplySuggestion($"وضعیت سفارش {order.OrderNumber}",
                OrderReply(order.OrderNumber, order.Status, order.TrackingCode, order.ShippedAt, order.DeliveredAt), "اطلاعات سفارش", 1));

        // ۲) یادگیری از پاسخ‌های قبلی کارکنان.
        var history = await HistoryAsync(cancellationToken);
        var best = history.Where(h => h.TicketId != ticketId)
            .Select(h => (Item: h, Score: Similarity(text, h.Question)))
            .Where(x => x.Score >= MinSimilarTicket)
            .OrderByDescending(x => x.Score).FirstOrDefault();
        if (best.Item is not null)
            suggestions.Add(new TicketReplySuggestion("پاسخ تیکت مشابه", Anonymize(best.Item.Answer, best.Item.FirstName, best.Item.LastName),
                "پاسخ تیکت مشابه", Math.Round(best.Score, 2)));

        // ۳) پیام‌های آمادهٔ تیکت.
        var ready = await context.ReadyMessages.Where(r => r.IsActive && (r.Scopes & ReadyMessageScope.Ticket) != 0)
            .OrderBy(r => r.SortOrder).Take(200).Select(r => new { r.Name, r.Title, r.Body }).ToListAsync(cancellationToken);
        var readyBest = ready.Select(r => (Item: r, Score: Similarity(text, $"{r.Name} {r.Title} {r.Body}")))
            .Where(x => x.Score >= MinLibrary).OrderByDescending(x => x.Score).FirstOrDefault();
        if (readyBest.Item is not null)
            suggestions.Add(new TicketReplySuggestion(readyBest.Item.Name, readyBest.Item.Body, "پیام آماده", Math.Round(readyBest.Score, 2)));

        // ۴) پرسش‌های متداول.
        var faqs = await context.FaqItems.Where(f => f.IsActive).OrderBy(f => f.DisplayOrder).Take(300)
            .Select(f => new { f.Question, f.Answer }).ToListAsync(cancellationToken);
        var faqBest = faqs.Select(f => (Item: f, Score: Similarity(text, f.Question + " " + f.Answer)))
            .Where(x => x.Score >= MinLibrary).OrderByDescending(x => x.Score).FirstOrDefault();
        if (faqBest.Item is not null)
            suggestions.Add(new TicketReplySuggestion(Clip(faqBest.Item.Question, 60),
                $"سلام {{FirstName}} عزیز،\n{PersianText.HtmlToText(faqBest.Item.Answer).Trim()}\nاگر سؤال دیگری دارید در خدمتیم.", "پرسش‌های متداول", Math.Round(faqBest.Score, 2)));

        var final = suggestions
            .GroupBy(s => Normalize(s.Body)).Select(g => g.First())
            .OrderByDescending(s => s.Score).Take(3).ToList();
        return new TicketReplyAdvice(topic, TopicTitle(topic), final);
    }

    private sealed record OrderFacts(string OrderNumber, OrderStatus Status, string? TrackingCode, DateTimeOffset? ShippedAt, DateTimeOffset? DeliveredAt);

    private sealed record HistoryItem(Guid TicketId, string Question, string Answer, string? FirstName, string? LastName);

    private async Task<OrderFacts?> FindOrderAsync(Guid userId, Guid? linkedOrderId, string text, TicketTopic topic, CancellationToken cancellationToken)
    {
        var orders = context.Orders.Where(o => o.UserId == userId);
        if (linkedOrderId is { } id)
        {
            var linked = await orders.Where(o => o.Id == id)
                .Select(o => new OrderFacts(o.OrderNumber, o.Status, o.TrackingCode, o.ShippedAt, o.DeliveredAt)).FirstOrDefaultAsync(cancellationToken);
            if (linked is not null)
                return linked;
        }

        var recent = await orders.OrderByDescending(o => o.CreatedAt).Take(30)
            .Select(o => new OrderFacts(o.OrderNumber, o.Status, o.TrackingCode, o.ShippedAt, o.DeliveredAt)).ToListAsync(cancellationToken);
        if (recent.Count == 0)
            return null;
        var mentioned = MentionedOrder(text, recent.Select(o => o.OrderNumber));
        if (mentioned is not null)
            return recent.First(o => o.OrderNumber == mentioned);
        return topic is TicketTopic.OrderStatus or TicketTopic.Shipping or TicketTopic.Return ? recent[0] : null;
    }

    private async Task<List<HistoryItem>> HistoryAsync(CancellationToken cancellationToken)
    {
        if (cache.TryGetValue(HistoryKey, out List<HistoryItem>? cached) && cached is not null)
            return cached;
        var since = DateTimeOffset.UtcNow.AddDays(-365);
        var rows = await context.SupportTickets
            .Where(t => t.CreatedAt >= since && (t.Status == TicketStatus.Answered || t.Status == TicketStatus.Closed))
            .OrderByDescending(t => t.LastActivityAt).Take(HistorySize)
            .Select(t => new
            {
                t.Id,
                t.Subject,
                Question = t.Messages.Where(m => !m.IsStaff).OrderBy(m => m.CreatedAt).Select(m => m.Content).FirstOrDefault(),
                Answer = t.Messages.Where(m => m.IsStaff).OrderBy(m => m.CreatedAt).Select(m => m.Content).FirstOrDefault(),
                t.User.FirstName,
                t.User.LastName,
            })
            .ToListAsync(cancellationToken);
        var items = rows.Where(r => !string.IsNullOrWhiteSpace(r.Answer) && r.Answer!.Trim().Length >= 15)
            .Select(r => new HistoryItem(r.Id, Clip($"{r.Subject}\n{r.Question}", 3000), r.Answer!.Trim(), r.FirstName, r.LastName))
            .ToList();
        cache.Set(HistoryKey, items, new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10), Size = 1 });
        return items;
    }

    /// <summary>
    /// تشخیص موضوع با واژه‌های کلیدی (ترتیب مهم است: مرجوعی و پرداخت پیش از «سفارش» عمومی).
    /// مثال‌ها: «می‌خوام مرجوع کنم» ← Return؛ «پول کم شد ولی سفارش ثبت نشد» ← Payment؛ «کد رهگیری» ← Shipping؛
    /// «رمزم یادم رفته» ← Account؛ «این هدفون اصله؟» ← Product؛ «سفارشم کجاست» ← OrderStatus.
    /// </summary>
    public static TicketTopic DetectTopic(string? text)
    {
        var t = Normalize(text);
        if (t.Length == 0)
            return TicketTopic.General;
        bool Any(params string[] words) => words.Any(w => t.Contains(w, StringComparison.Ordinal));
        if (Any("مرجوع", "برگشت کالا", "پس بدم", "عودت", "تعویض", "خراب رسید", "معیوب", "شکسته"))
            return TicketTopic.Return;
        if (Any("پرداخت", "کسر شد", "کم شد", "درگاه", "بازگشت وجه", "کیف پول", "استرداد", "واریز"))
            return TicketTopic.Payment;
        if (Any("رهگیری", "پست", "ارسال", "نرسید", "نرسیده", "کی میرسه", "کی می رسه", "تحویل", "پیک", "مرسوله"))
            return TicketTopic.Shipping;
        if (Any("رمز", "ورود", "لاگین", "حساب کاربری", "پیامک کد", "کد تایید", "کد تأیید", "شماره موبایل"))
            return TicketTopic.Account;
        if (Any("سفارش", "فاکتور", "وضعیت"))
            return TicketTopic.OrderStatus;
        if (Any("اصل", "گارانتی", "مشخصات", "موجود", "قیمت", "رنگ", "سایز", "کیفیت"))
            return TicketTopic.Product;
        return TicketTopic.General;
    }

    /// <summary>عنوان فارسی موضوع. مثال: Shipping ← «ارسال و رهگیری».</summary>
    public static string TopicTitle(TicketTopic topic) => topic switch
    {
        TicketTopic.OrderStatus => "پیگیری سفارش",
        TicketTopic.Shipping => "ارسال و رهگیری",
        TicketTopic.Return => "مرجوعی و تعویض",
        TicketTopic.Payment => "پرداخت و بازگشت وجه",
        TicketTopic.Product => "پرسش دربارهٔ کالا",
        TicketTopic.Account => "حساب کاربری و ورود",
        _ => "عمومی",
    };

    /// <summary>
    /// متن پاسخ از وضعیت واقعی سفارش. مثال: (FR-1024، Shipped، «1234567890»، ۱ آبان) ←
    /// «سلام {FirstName} عزیز، سفارش FR-1024 در تاریخ 1405/08/01 ارسال شده است. کد رهگیری مرسوله: 1234567890 …».
    /// </summary>
    public static string OrderReply(string orderNumber, OrderStatus status, string? trackingCode, DateTimeOffset? shippedAt, DateTimeOffset? deliveredAt)
    {
        var line = status switch
        {
            OrderStatus.Pending => $"سفارش {orderNumber} ثبت شده و در انتظار تأیید است؛ به‌محض تأیید، آماده‌سازی آن شروع می‌شود.",
            OrderStatus.Confirmed => $"سفارش {orderNumber} تأیید شده و به‌زودی آماده‌سازی می‌شود.",
            OrderStatus.Processing => $"سفارش {orderNumber} در حال آماده‌سازی است؛ پس از ارسال، کد رهگیری برایتان فرستاده می‌شود.",
            OrderStatus.Shipped => $"سفارش {orderNumber}{(shippedAt is { } s ? $" در تاریخ {Day(s)}" : "")} ارسال شده است."
                                   + (string.IsNullOrWhiteSpace(trackingCode) ? "" : $" کد رهگیری مرسوله: {trackingCode.Trim()}"),
            OrderStatus.Delivered => $"سفارش {orderNumber}{(deliveredAt is { } d ? $" در تاریخ {Day(d)}" : "")} تحویل شده است. اگر مشکلی در کالا هست، لطفاً جزئیات را بفرمایید تا بررسی کنیم.",
            OrderStatus.Cancelled => $"سفارش {orderNumber} لغو شده است؛ اگر مبلغی پرداخت کرده‌اید، طبق قوانین فروشگاه به کیف پول شما برگشت داده می‌شود.",
            OrderStatus.Returned => $"سفارش {orderNumber} مرجوع شده و مبلغ آن به کیف پول شما برگشت داده می‌شود.",
            _ => $"سفارش {orderNumber} در حال بررسی است.",
        };
        return $"سلام {{FirstName}} عزیز،\n{line}\nوضعیت لحظه‌ای را در «پروفایل ← سفارش‌ها» هم می‌توانید ببینید.";
    }

    /// <summary>
    /// شمارهٔ سفارشی از فهرست سفارش‌های همین مشتری که در متن آمده (با رقم فارسی یا بدون خط تیره). مثال: «سفارش ۱۰۲۴» و FR-1024 ← FR-1024.
    /// </summary>
    public static string? MentionedOrder(string? text, IEnumerable<string> orderNumbers)
    {
        var digits = DigitsOnly(text);
        var plain = Normalize(text).Replace("-", "", StringComparison.Ordinal);
        foreach (var number in orderNumbers)
        {
            var compact = number.ToLowerInvariant().Replace("-", "", StringComparison.Ordinal);
            if (compact.Length >= 4 && plain.Contains(compact, StringComparison.Ordinal))
                return number;
            var numberDigits = DigitsOnly(number);
            if (numberDigits.Length >= 4 && Regex.IsMatch(digits, $@"(^|\D){numberDigits}(\D|$)"))
                return number;
        }
        return null;
    }

    /// <summary>
    /// پوشاندن اطلاعات مشتری قبلی در پاسخ قدیمی: نام ← {FirstName}، نام خانوادگی حذف، عدد ۵ رقمی یا بیشتر (کد رهگیری، تلفن،
    /// شماره سفارش) ← «…». مثال: «سلام علی رضایی، کد رهگیری 123456789 است» ← «سلام {FirstName}، کد رهگیری … است».
    /// </summary>
    public static string Anonymize(string answer, string? firstName, string? lastName)
    {
        var text = answer;
        // فقط واژهٔ کامل: نام «علی» نباید «فعلی» را خراب کند (مرز واژه با حروف، اعراب و نیم‌فاصله).
        if (!string.IsNullOrWhiteSpace(lastName) && lastName.Trim().Length >= 2)
            text = Regex.Replace(text, @" ?" + WholeWord(lastName.Trim()), "", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        if (!string.IsNullOrWhiteSpace(firstName) && firstName.Trim().Length >= 2)
            text = Regex.Replace(text, WholeWord(firstName.Trim()), "{FirstName}", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        text = LongNumber().Replace(text, "…");
        return text.Trim();
    }

    /// <summary>
    /// شباهت کسینوسی دو متن روی ریشهٔ واژه‌ها (۰ تا ۱)؛ واژه‌های پرتکرار عمومی وزن کمتری دارند (جذر بسامد).
    /// مثال: («کد رهگیری سفارشم نیومده»، «کد رهگیری ندارم») ← حدود ۰٫۵.
    /// </summary>
    public static double Similarity(string? a, string? b)
    {
        var left = Vector(a);
        var right = Vector(b);
        if (left.Count == 0 || right.Count == 0)
            return 0;
        double dot = 0;
        foreach (var (term, weight) in left)
            if (right.TryGetValue(term, out var other))
                dot += weight * other;
        var norm = Math.Sqrt(left.Values.Sum(v => v * v)) * Math.Sqrt(right.Values.Sum(v => v * v));
        return norm == 0 ? 0 : dot / norm;
    }

    private static Dictionary<string, double> Vector(string? text) =>
        PersianText.ContentStems(PersianText.HtmlToText(text ?? "")).Where(t => t.Length >= 2)
            .GroupBy(t => t, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => Math.Sqrt(g.Count()), StringComparer.Ordinal);

    private static string Normalize(string? text) => PersianText.Normalize(text).ToLowerInvariant();

    private static string DigitsOnly(string? text) =>
        new((text ?? "").Select(c => c is >= '۰' and <= '۹' ? (char)('0' + (c - '۰')) : c is >= '٠' and <= '٩' ? (char)('0' + (c - '٠')) : c)
            .Select(c => char.IsAsciiDigit(c) ? c : ' ').ToArray());

    private static string Day(DateTimeOffset moment) => PersianDate.Format(moment)[..10];

    private static string Clip(string text, int max) => text.Length <= max ? text : text[..max];

    private static string WholeWord(string word) => @"(?<![\p{L}\p{Mn}\u200c])" + Regex.Escape(word) + @"(?![\p{L}\p{Mn}\u200c])";

    [GeneratedRegex(@"[0-9۰-۹٠-٩]{5,}")]
    private static partial Regex LongNumber();
}
