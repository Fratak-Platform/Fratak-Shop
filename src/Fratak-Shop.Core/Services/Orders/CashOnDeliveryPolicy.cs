using Fratak_Shop.Core.DTOs.Settings;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Orders;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Core.Services.Orders;

/// <summary>
/// سابقهٔ خرید مشتری که برای تصمیم «پرداخت در محل» لازم است.
/// </summary>
/// <param name="DeliveredOrders">تعداد سفارش‌های تحویل‌شدهٔ قبلی (با هر روش پرداخت).</param>
/// <param name="ReturnedCashOrders">تعداد سفارش‌های پرداخت در محل که مرجوع شده‌اند (یعنی تحویل گرفته نشده‌اند).</param>
public sealed record CustomerCodHistory(int DeliveredOrders, int ReturnedCashOrders);

/// <summary>
/// قوانین خالص (بدون دیتابیس) پرداخت در محل؛ مستقیم در تست واحد بررسی می‌شود.
/// <para>
/// ترتیب بررسی: فعال بودن → سقف مبلغ → استان مجاز → حداقل سفارش تحویل‌شده → سقف مرجوعی.
/// خروجی null یعنی مجاز است؛ وگرنه متن فارسی دلیل که مستقیم به مشتری نشان داده می‌شود.
/// </para>
/// مثال: با تنظیم «حداقل ۱ سفارش تحویل‌شده»، مشتری تازه پیام «پرداخت در محل بعد از اولین خرید موفق فعال می‌شود» را می‌بیند.
/// </summary>
public static class CashOnDeliveryRules
{
    /// <summary>دلیل عدم امکان پرداخت در محل یا null اگر مجاز باشد.</summary>
    /// <param name="province">استان مقصد؛ اگر هنوز آدرس انتخاب نشده null بدهید تا این شرط نادیده گرفته شود.</param>
    /// <param name="payable">مبلغ قابل پرداخت؛ null یعنی هنوز معلوم نیست (شرط سقف مبلغ بررسی نمی‌شود).</param>
    public static string? Check(StoreSettingDto settings, CustomerCodHistory? history, string? province, decimal? payable)
    {
        if (!settings.IsCashOnDeliveryEnabled)
            return "پرداخت در محل در حال حاضر فعال نیست.";

        if (payable is { } amount && !settings.AllowsCashOnDelivery(amount))
            return $"پرداخت در محل برای سفارش‌های بالای {settings.CashOnDeliveryMaxAmount:N0} تومان امکان‌پذیر نیست.";

        if (province is not null && !IsProvinceAllowed(settings, province))
            return $"پرداخت در محل فقط برای استان‌های {string.Join("، ", settings.CodProvinces)} فعال است.";

        if (settings.CodMinDeliveredOrders > 0 && (history?.DeliveredOrders ?? 0) < settings.CodMinDeliveredOrders)
            return settings.CodMinDeliveredOrders == 1
                ? "پرداخت در محل بعد از اولین خرید موفق (تحویل‌شده) فعال می‌شود."
                : $"پرداخت در محل بعد از {settings.CodMinDeliveredOrders:N0} خرید موفق (تحویل‌شده) فعال می‌شود.";

        if (settings.CodMaxReturnedOrders > 0 && (history?.ReturnedCashOrders ?? 0) >= settings.CodMaxReturnedOrders)
            return "به دلیل مرجوع شدن سفارش‌های پرداخت در محل قبلی، این روش برای حساب شما فعال نیست؛ لطفاً آنلاین پرداخت کنید.";

        return null;
    }

    /// <summary>
    /// آیا استان در فهرست مجاز است؟ فهرست خالی یعنی همه مجازند.
    /// مقایسه با یکسان‌سازی «ي/ی» و «ك/ک» و حذف پیشوند «استان» انجام می‌شود؛ مثلاً «استان تهران» = «تهران».
    /// </summary>
    public static bool IsProvinceAllowed(StoreSettingDto settings, string? province)
    {
        var allowed = settings.CodProvinces;
        if (allowed.Count == 0)
            return true;

        var target = NormalizeProvince(province);
        return target.Length > 0 && allowed.Any(p => NormalizeProvince(p) == target);
    }

    private static string NormalizeProvince(string? value)
    {
        var text = SearchText.Normalize(value);
        if (text.StartsWith("استان ", StringComparison.Ordinal))
            text = text["استان ".Length..];
        return text.Trim();
    }
}

/// <summary>
/// اعمال قوانین پرداخت در محل با خواندن سابقهٔ مشتری از دیتابیس.
/// <para>
/// کاربرد: ۱) صفحهٔ تسویه حساب (غیرفعال کردن گزینه با نمایش دلیل) و ۲) <see cref="OrderService.CreateOrderAsync"/>
/// (بررسی نهایی سمت سرور؛ حتی اگر کسی فرم را دستکاری کند، سفارش پرداخت در محل غیرمجاز ثبت نمی‌شود).
/// </para>
/// </summary>
public interface ICashOnDeliveryPolicy
{
    /// <summary>سابقهٔ مشتری (برای مهمان یا کاربر نامعلوم، صفر).</summary>
    Task<CustomerCodHistory> GetHistoryAsync(Guid? userId);

    /// <summary>دلیل عدم امکان پرداخت در محل یا null اگر مجاز باشد.</summary>
    Task<string?> CheckAsync(StoreSettingDto settings, Guid? userId, string? province, decimal? payable);
}

public class CashOnDeliveryPolicy(FratakShopDbContext context) : ICashOnDeliveryPolicy
{
    public async Task<CustomerCodHistory> GetHistoryAsync(Guid? userId)
    {
        if (userId is null)
            return new CustomerCodHistory(0, 0);

        var counts = await context.Orders
            .Where(o => o.UserId == userId)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Delivered = g.Count(o => o.Status == OrderStatus.Delivered),
                Returned = g.Count(o => o.Status == OrderStatus.Returned && o.PaymentMethod == PaymentMethod.CashOnDelivery),
            })
            .FirstOrDefaultAsync();

        return new CustomerCodHistory(counts?.Delivered ?? 0, counts?.Returned ?? 0);
    }

    public async Task<string?> CheckAsync(StoreSettingDto settings, Guid? userId, string? province, decimal? payable)
    {
        // اگر شرط سابقه‌ای تنظیم نشده، کوئری اضافه لازم نیست.
        var needsHistory = settings.CodMinDeliveredOrders > 0 || settings.CodMaxReturnedOrders > 0;
        var history = needsHistory ? await GetHistoryAsync(userId) : null;
        return CashOnDeliveryRules.Check(settings, history, province, payable);
    }
}
