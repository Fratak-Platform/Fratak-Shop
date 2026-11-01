using System.Net;
using Fratak_Shop.Core.DTOs.Settings;
using Fratak_Shop.Core.Services.Pricing;

namespace Fratak_Shop.Web.Infrastructure.Admin;

/// <summary>
/// نام‌ها و دامنه‌هایی که «فروشگاه خودمان» حساب می‌شوند تا قیمت خودمان در فهرست رقبا رقیب حساب نشود (مرحلهٔ پانزدهم).
/// <para>منابع: نام فروشگاه در تنظیمات، دامنهٔ اصلی (Shop:CanonicalHost) و دامنهٔ همین درخواست.
/// localhost و آدرس IP نادیده گرفته می‌شوند. مثال: («فراتک»، faratek.ir، www.faratek.ir) ← ["فراتک", "faratek.ir"].</para>
/// <para>استفاده: دیالوگ «پیشنهاد قیمت فروش» و دکمهٔ «بررسی دوباره» قیمت رقبا در پنل مدیریت.</para>
/// </summary>
public static class OwnShopNames
{
    public static IReadOnlyCollection<string> For(StoreSettingDto store, ShopOptions shop, HttpRequest request)
        => ProductCompetitorService.OwnShops(store, Domain(shop.CanonicalHost), Domain(request.Host.Host));

    /// <summary>دامنهٔ بدون www؛ null برای مقدار خالی، بدون نقطه («off»/localhost) یا IP.</summary>
    public static string? Domain(string? host)
    {
        host = host?.Trim().TrimEnd('.').ToLowerInvariant();
        if (string.IsNullOrEmpty(host) || !host.Contains('.') || IPAddress.TryParse(host, out _))
            return null;
        return host.StartsWith("www.", StringComparison.Ordinal) ? host[4..] : host;
    }
}
