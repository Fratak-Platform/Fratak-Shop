namespace Fratak_Shop.Core.Services.Notifications;

/// <summary>
/// ارسال پیامک (کد یک‌بارمصرف و متن اعلان‌ها). پیاده‌سازی اصلی: ConfigurableSmsSender در لایهٔ وب که بر اساس
/// تنظیمات پنل («فنی ← سرویس پیامک») از SMS.ir می‌فرستد یا فقط در لاگ می‌نویسد.
/// هیچ‌کدام از متدها استثنا بیرون نمی‌دهند: خطای سرویس پیامک نباید ثبت سفارش یا ورود را خراب کند.
/// </summary>
public interface ISmsSender
{
    /// <summary>پیامک متنی (اعلان سفارش، پیام فروشگاه و …) از خط ارسال.</summary>
    Task SendAsync(string phoneNumber, string message);

    /// <summary>
    /// کد یک‌بارمصرف. اگر سرویس قالب «ارسال سریع» داشته باشد (SMS.ir: send/verify) با قالب فرستاده می‌شود،
    /// وگرنه همان <paramref name="message"/> به‌صورت متن معمولی. خروجی false یعنی پیامک به سرویس‌دهنده تحویل نشد.
    /// پیش‌فرض (برای ارسال‌کننده‌های ساده و آزمون‌ها): همان SendAsync و true.
    /// </summary>
    async Task<bool> SendOtpAsync(string phoneNumber, string code, string message)
    {
        await SendAsync(phoneNumber, message);
        return true;
    }
}
