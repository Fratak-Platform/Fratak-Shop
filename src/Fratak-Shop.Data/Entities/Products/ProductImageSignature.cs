using System.ComponentModel.DataAnnotations;
using Fratak_Shop.Data.Utilities;

namespace Fratak_Shop.Data.Entities.Products;

/// <summary>
/// دور ۲۴ (جست‌وجوی تصویری، پیشنهاد ۲۹): «اثر انگشت» تصویر اصلی هر کالا.
/// شامل یک هش ادراکی ۶۴ بیتی (شکل کلی) و هیستوگرام رنگ ۳۲ خانه‌ای (رنگ‌های غالب، بدون پس‌زمینهٔ سفید).
/// مثال: مشتری عکس یک کتانی قرمز را می‌فرستد ← کالاهایی که امضای نزدیک دارند پیشنهاد می‌شوند.
/// <para>محل استفاده: Web/Infrastructure/Search/ImageSearchService.cs.</para>
/// </summary>
public class ProductImageSignature : BaseEntity
{
    /// <summary>کالا (یکتا).</summary>
    public Guid ProductId { get; set; }

    /// <summary>نام فایل تصویری که امضا از آن ساخته شده؛ اگر تصویر اصلی عوض شود امضا دوباره ساخته می‌شود.</summary>
    [Required]
    [MaxLength(200)]
    public string ImageName { get; set; } = null!;

    /// <summary>هش تفاضلی (dHash) ۶۴ بیتی.</summary>
    public long Hash { get; set; }

    /// <summary>هیستوگرام رنگ نرمال‌شده؛ ۳۲ بایت (هر خانه ۰ تا ۲۵۵).</summary>
    [Required]
    [MaxLength(32)]
    public byte[] Colors { get; set; } = [];

    /// <summary>زمان ساخت امضا.</summary>
    public DateTimeOffset ComputedAt { get; set; }
}
