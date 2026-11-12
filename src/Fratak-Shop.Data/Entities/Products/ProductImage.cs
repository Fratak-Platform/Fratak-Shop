using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Fratak_Shop.Data.Utilities;

namespace Fratak_Shop.Data.Entities.Products;

public class ProductImage : BaseEntity
{
    public Guid ProductId { get; set; }

    /// <summary>
    /// File name of the image stored on the server.
    /// </summary>
    [Required]
    [MaxLength(250)]
    public string ImageName { get; set; } = null!;

    [MaxLength(250)]
    public string? AltText { get; set; }

    public int DisplayOrder { get; set; }

    public bool IsPrimary { get; set; }

    /// <summary>
    /// دور ۲۳: «اثر انگشت» تصویر (dHash ۶۴ بیتی). دو عکس با فاصلهٔ همینگ کم یکی‌اند ← عکس تکراری برای همین کالا رد می‌شود و
    /// گزارش «عکس مشترک بین کالاها» ساخته می‌شود. null = هنوز محاسبه نشده (کارگر هوشمند کم‌کم پر می‌کند).
    /// </summary>
    public long? Hash { get; set; }

    /// <summary>
    /// دور ۲۴ (پیشنهاد ۵۸): امتیاز کیفیت تصویر ۰ تا ۱۰۰ (وضوح، تیزی/تاری با واریانس لاپلاسین، نور، کنتراست و پس‌زمینهٔ تمیز).
    /// مبنای انتخاب خودکار بهترین عکس اصلی کالا. null = هنوز محاسبه نشده (کارگر هوشمند کم‌کم پر می‌کند).
    /// </summary>
    public byte? Quality { get; set; }

    /// <summary>
    /// دور ۲۸ (#۳۶ تا #۵۰): اطمینان «بررسی سوژه» ۰ تا ۱۰۰ – عکس خود کالاست، ترجیحاً روی سفید، بدون آدم و واترمارک
    /// (ImageSubjectCheck). null = هنوز بررسی نشده (کارگر هوشمند عکس‌های موجود را کم‌کم بازبینی می‌کند).
    /// </summary>
    public byte? SubjectScore { get; set; }

    /// <summary>
    /// دور ۲۸: نشانه‌های بررسی سوژه (بیت‌ها): ۱ = زمینهٔ سفید، ۲ = زمینهٔ یکدست، ۴ = احتمال آدم، ۸ = احتمال واترمارک، ۱۶ = عکس صحنه.
    /// مثال: ۳ = روی سفید (عالی)؛ ۴ = آدم دارد (در گزارش سلامت داده برای بازبینی می‌آید).
    /// </summary>
    public byte? SubjectFlags { get; set; }

    [ForeignKey(nameof(ProductId))]
    public Product Product { get; set; } = null!;
}
