using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Entities.Notifications;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Core.Services.Notifications;

/// <summary>پیش‌نمایش یک قالب پیام با دادهٔ واقعی (برای ویرایشگر قالب در پنل).</summary>
/// <param name="Title">عنوان جایگزین‌شده.</param>
/// <param name="Body">متن جایگزین‌شده.</param>
/// <param name="Sms">متن پیامک جایگزین‌شده (اگر وارد شده باشد).</param>
/// <param name="Source">توضیح منبع داده، مثلاً «سفارش FR-1024 از حساب شما».</param>
/// <param name="UnknownKeys">برچسب‌هایی که در متن هست ولی برای این رویداد معتبر نیست (اشتباه تایپی).</param>
public sealed record NotificationPreview(string Title, string Body, string? Sms, string Source, IReadOnlyList<string> UnknownKeys);

// بخش «برچسب‌ها» در سرویس اعلان: پر کردن خودکار مقدار برچسب‌ها و پیش‌نمایش زنده.
public partial class NotificationService
{
    /// <summary>
    /// پر کردن خودکار برچسب‌های عمومی (نام، موبایل، کیف پول، ...) و برچسب‌های سفارش.
    /// <para>
    /// فقط برچسب‌هایی که در متن استفاده شده‌اند حساب می‌شوند تا کوئری اضافه زده نشود.
    /// مقدارهایی که محل صدا زدن رویداد فرستاده (مثلاً <c>Amount</c>) هرگز بازنویسی نمی‌شوند.
    /// </para>
    /// مثال: اگر قالب «سلام {FirstName}، سفارش {OrderNumber} به {City} ارسال شد» باشد و فقط
    /// <c>OrderNumber</c> فرستاده شده باشد، نام و شهر از دیتابیس خوانده می‌شوند.
    /// </summary>
    private async Task EnrichAsync(Guid userId, Dictionary<string, string?> values, IEnumerable<string?> texts)
    {
        var used = UsedKeys(texts);
        if (used.Count == 0)
            return;

        bool Need(string key) => used.Contains(key) && !values.ContainsKey(key);

        if (Need("FirstName") || Need("LastName") || Need("FullName") || Need("Name") || Need("Greeting") || Need("Phone") || Need("WalletBalance") || Need("LoyaltyPoints"))
        {
            var user = await context.Users.Where(u => u.Id == userId)
                .Select(u => new { u.FirstName, u.LastName, u.BirthDate, u.PhoneNumber, u.WalletBalance, u.WalletBonusBalance, u.LoyaltyPoints })
                .FirstOrDefaultAsync();
            if (user is not null)
            {
                var first = string.IsNullOrWhiteSpace(user.FirstName) ? "کاربر" : user.FirstName.Trim();
                values.TryAdd("FirstName", first);
                values.TryAdd("LastName", user.LastName?.Trim() ?? string.Empty);
                values.TryAdd("FullName", $"{user.FirstName} {user.LastName}".Trim() is { Length: > 0 } full ? full : "کاربر");
                values.TryAdd("Name", first);
                // دور ۲۹: خطاب مناسب نسل، مثلاً «علی جان» برای جوان‌ها و «رضایی گرامی» برای میان‌سال‌ها.
                values.TryAdd("Greeting", Fratak_Shop.Core.Services.Users.UserGreeting.For(user.FirstName, user.LastName, user.BirthDate,
                    IranClock.Today(DateTimeOffset.UtcNow)));
                values.TryAdd("Phone", user.PhoneNumber);
                values.TryAdd("WalletBalance", $"{user.WalletBalance + user.WalletBonusBalance:N0} تومان");
                values.TryAdd("LoyaltyPoints", user.LoyaltyPoints.ToString("N0"));
            }
        }

        if (Need("Today"))
            values["Today"] = PersianDate.Format(DateTimeOffset.UtcNow)[..10];

        // برچسب‌های سفارش فقط وقتی شمارهٔ سفارش معلوم است و برچسبی از سفارش در متن استفاده شده.
        var orderKeys = NotificationPlaceholders.Order.Items.Select(i => i.Key);
        if (values.TryGetValue("OrderNumber", out var orderNumber) && !string.IsNullOrWhiteSpace(orderNumber)
            && orderKeys.Any(Need))
        {
            await AddOrderValuesAsync(values, o => o.OrderNumber == orderNumber && o.UserId == userId);
        }
    }

    /// <summary>خواندن اطلاعات یک سفارش و افزودن برچسب‌های سفارش (بدون بازنویسی مقدارهای موجود).</summary>
    private async Task<string?> AddOrderValuesAsync(Dictionary<string, string?> values, System.Linq.Expressions.Expression<Func<Fratak_Shop.Data.Entities.Orders.Order, bool>> filter)
    {
        var order = await context.Orders.Where(filter)
            .OrderByDescending(o => o.CreatedAt)
            .Select(o => new
            {
                o.OrderNumber, o.PayableAmount, o.Status, o.CreatedAt, o.ShippingMethod, o.PaymentMethod,
                o.ShippingCity, o.TrackingCode, o.RefundedAmount,
                Items = o.Items.OrderBy(i => i.CreatedAt).Select(i => new { i.ProductName, i.Quantity }).ToList(),
            })
            .FirstOrDefaultAsync();
        if (order is null)
            return null;

        values.TryAdd("OrderNumber", order.OrderNumber);
        values.TryAdd("Amount", order.PayableAmount.ToString("N0"));
        values.TryAdd("AmountWords", PersianNumberWords.ToWords(order.PayableAmount) + " تومان");
        values.TryAdd("OrderStatus", AdminOrderService.OrderStatusTitle(order.Status));
        values.TryAdd("OrderDate", PersianDate.Format(order.CreatedAt)[..10]);
        values.TryAdd("ItemCount", order.Items.Sum(i => i.Quantity).ToString("N0"));
        values.TryAdd("ItemsSummary", string.Join("، ", order.Items.Take(3).Select(i => i.ProductName))
                                      + (order.Items.Count > 3 ? $" و {order.Items.Count - 3} کالای دیگر" : string.Empty));
        values.TryAdd("ShippingMethod", order.ShippingMethod ?? "—");
        values.TryAdd("PaymentMethod", AdminReportService.PaymentMethodTitle(order.PaymentMethod));
        values.TryAdd("City", order.ShippingCity);
        values.TryAdd("TrackingCode", string.IsNullOrWhiteSpace(order.TrackingCode) ? "—" : order.TrackingCode);
        values.TryAdd("RefundText", order.RefundedAmount > 0 ? $"مبلغ {order.RefundedAmount:N0} تومان به کیف پول شما برگشت داده شد." : string.Empty);
        return order.OrderNumber;
    }

    /// <summary>
    /// پیش‌نمایش قالب با دادهٔ واقعی: اطلاعات حساب خود کارمند و آخرین سفارش او
    /// (اگر سفارشی نداشت، آخرین سفارش فروشگاه). برچسب‌هایی که داده ندارند با نمونه پر می‌شوند.
    /// محل استفاده: صفحهٔ «قالب‌های پیام» (handler پیش‌نمایش).
    /// </summary>
    public async Task<NotificationPreview> PreviewAsync(NotificationEvent @event, Guid userId, string? title, string? body, string? sms)
    {
        // رویداد None یعنی متن آزاد (پیام آماده/اعلان تکی): برچسب‌های مشتری، فروشگاه و سفارش معتبرند.
        var groups = @event == NotificationEvent.None ? NotificationPlaceholders.ForOrderMessage : NotificationPlaceholders.For(@event);
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var settings = await settingService.GetAsync();
        values["SiteName"] = settings.SiteName;

        var source = "اطلاعات حساب شما";
        if (@event == NotificationEvent.None || NotificationPlaceholders.IsOrderEvent(@event))
        {
            var number = await AddOrderValuesAsync(values, o => o.UserId == userId)
                         ?? await AddOrderValuesAsync(values, o => true);
            if (number is not null)
                source += $" و سفارش {number}";
        }

        var texts = new[] { title, body, sms };
        await EnrichAsync(userId, values, texts);

        // برچسب‌های بدون دادهٔ واقعی (مثلاً کد رهگیری یا امتیاز) با نمونه پر می‌شوند تا پیش‌نمایش کامل باشد.
        foreach (var (key, sample) in NotificationPlaceholders.Samples(groups))
            values.TryAdd(key, sample);

        var known = groups.SelectMany(g => g.Items).Select(i => i.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unknown = UsedKeys(texts).Where(k => !known.Contains(k)).OrderBy(k => k).ToList();

        return new NotificationPreview(
            Render(title ?? string.Empty, values),
            Render(body ?? string.Empty, values),
            string.IsNullOrWhiteSpace(sms) ? null : Render(sms, values),
            source,
            unknown);
    }

    /// <summary>
    /// جایگزینی برچسب‌های یک متن آزاد برای یک کاربر مشخص.
    /// کاربرد: پاسخ آماده در تیکت/دیدگاه/پیام سفارش و اعلان تکی، مثلاً «سلام {FirstName}» → «سلام علی».
    /// <paramref name="extra"/> مقدارهای اضافه مثل <c>OrderNumber</c> یا <c>TicketNumber</c> است.
    /// </summary>
    public async Task<string> RenderForUserAsync(Guid? userId, string text, IReadOnlyDictionary<string, string?>? extra = null)
    {
        if (string.IsNullOrEmpty(text) || !text.Contains('{'))
            return text;

        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        if (extra is not null)
        {
            foreach (var (key, value) in extra)
                values[key] = value;
        }

        values.TryAdd("SiteName", (await settingService.GetAsync()).SiteName);
        if (userId is { } id)
            await EnrichAsync(id, values, [text]);

        return RenderKeepUnknown(text, values);
    }

    /// <summary>کلیدهای برچسب استفاده‌شده در چند متن، مثلاً «سلام {FirstName}» → FirstName.</summary>
    private static HashSet<string> UsedKeys(IEnumerable<string?> texts)
    {
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var text in texts)
        {
            if (string.IsNullOrEmpty(text))
                continue;
            foreach (System.Text.RegularExpressions.Match match in PlaceholderRegex().Matches(text))
                keys.Add(match.Groups[1].Value);
        }

        return keys;
    }

    /// <summary>
    /// مثل Render ولی برچسب‌های ناشناخته را دست‌نخورده نگه می‌دارد
    /// (برای متن‌های آزاد کارکنان که ممکن است آکولاد معمولی داشته باشند).
    /// </summary>
    private static string RenderKeepUnknown(string template, IReadOnlyDictionary<string, string?> values) =>
        PlaceholderRegex().Replace(template, match =>
            values.TryGetValue(match.Groups[1].Value, out var value) ? value ?? string.Empty : match.Value);
}
