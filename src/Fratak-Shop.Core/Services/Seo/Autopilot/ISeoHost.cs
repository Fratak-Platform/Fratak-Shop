namespace Fratak_Shop.Core.Services.Seo.Autopilot;

/// <summary>
/// آدرس عمومی سایت برای سرویس‌های سئو (لایهٔ Core به تنظیمات وب دسترسی ندارد).
/// <para>پیاده‌سازی: <c>Web/Infrastructure/Seo/SeoHost.cs</c> از <c>Shop:CanonicalHost</c> (مثلاً «shop.fratak.ir»).</para>
/// <para>کجا استفاده می‌شود؟ نسبی کردن لینک‌های مطلق داخلی، IndexNow، فیدها و خزش.</para>
/// </summary>
public interface ISeoHost
{
    /// <summary>دامنهٔ اصلی بدون پروتکل (مثال: «shop.fratak.ir»). تنظیم نشده ← null.</summary>
    string? Host { get; }

    /// <summary>آدرس پایه با https (مثال: «https://shop.fratak.ir»). تنظیم نشده ← null.</summary>
    string? BaseUrl { get; }
}

/// <summary>
/// اطلاع فوری به موتورهای جست‌وجو (IndexNow: بینگ، یاندکس، ناور، سیک…) دربارهٔ صفحه‌های تازه/تغییرکرده.
/// <para>پیاده‌سازی واقعی: <c>Web/Infrastructure/Seo/IndexNowPinger.cs</c>؛ پیش‌فرض Core (<see cref="NullSeoPinger"/>) کاری نمی‌کند
/// تا آزمون‌ها و ابزارها بدون شبکه کار کنند.</para>
/// <para>کجا استفاده می‌شود؟ کارگر خلبان خودکار بعد از هر تغییر صفحه.</para>
/// </summary>
public interface ISeoPinger
{
    /// <summary>ارسال آدرس‌های نسبی (مثال: ["/product/a55"]). موفق ← true. هرگز استثنا نمی‌دهد.</summary>
    Task<bool> PingAsync(IReadOnlyCollection<string> paths, CancellationToken cancellationToken = default);
}

/// <summary>پیش‌فرض بی‌اثر <see cref="ISeoPinger"/>.</summary>
public sealed class NullSeoPinger : ISeoPinger
{
    public Task<bool> PingAsync(IReadOnlyCollection<string> paths, CancellationToken cancellationToken = default) => Task.FromResult(false);
}

/// <summary>پیش‌فرض بی‌اثر <see cref="ISeoPageFetcher"/> (بدون شبکه): همه‌چیز «در دسترس نیست» (وضعیت ۰).</summary>
public sealed class OfflineSeoPageFetcher : ISeoPageFetcher
{
    public Task<CrawlResponse> FetchAsync(string path, CancellationToken cancellationToken) =>
        Task.FromResult(new CrawlResponse(0, null, null, 0));
}

/// <summary>پیش‌فرض <see cref="ISeoHost"/> وقتی لایهٔ وب ثبت نکرده (آزمون‌ها): دامنه نامعلوم.</summary>
public sealed class UnknownSeoHost : ISeoHost
{
    public string? Host => null;
    public string? BaseUrl => null;
}
