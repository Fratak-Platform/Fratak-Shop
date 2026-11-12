using Fratak_Shop.Core.Services.Seo.Autopilot;
using Fratak_Shop.Web.Infrastructure.Middleware;
using Microsoft.Extensions.Options;

namespace Fratak_Shop.Web.Infrastructure.Seo;

/// <summary>
/// دامنهٔ اصلی سایت برای سرویس‌های سئو، از تنظیم <c>Shop:CanonicalHost</c> (مثال: «shop.fratak.ir»).
/// <para>چرا از تنظیمات و نه از درخواست؟ چون هدر Host را هر کسی می‌تواند جعل کند؛ IndexNow، فید ترب، آدرس‌های مطلق نقشهٔ
/// سایت و بررسی توکن ترب باید همیشه دامنهٔ واقعی را ببینند.</para>
/// <para>مقدار «off» یا خالی (محیط تست/توسعه) ← <c>null</c>؛ هر سرویسی که دامنه لازم دارد در این حالت کارش را با
/// دامنهٔ درخواست جاری انجام می‌دهد یا اصلاً انجام نمی‌دهد (مثلاً IndexNow ارسال نمی‌شود).</para>
/// <para>ثبت: Singleton در <c>WebServiceCollectionExtensions</c>.</para>
/// </summary>
public sealed class SeoHost(IOptions<ShopOptions> options) : ISeoHost
{
    public string? Host { get; } = CanonicalHostMiddleware.Normalize(options.Value.CanonicalHost);

    public string? BaseUrl => Host is null ? null : "https://" + Host;

    /// <summary>
    /// آدرس پایه برای یک درخواست: دامنهٔ تنظیم‌شده، وگرنه دامنهٔ خود درخواست (فقط برای محیط تست/توسعه).
    /// مثال: <c>BaseFor(http) == "https://shop.fratak.ir"</c>.
    /// </summary>
    public string BaseFor(HttpContext http) => BaseUrl ?? $"{http.Request.Scheme}://{http.Request.Host}";
}
