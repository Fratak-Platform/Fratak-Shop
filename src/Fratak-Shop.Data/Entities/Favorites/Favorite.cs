using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Fratak_Shop.Data.Entities.Products;
using Fratak_Shop.Data.Entities.Users;
using Fratak_Shop.Data.Utilities;

namespace Fratak_Shop.Data.Entities.Favorites;

public class Favorite : BaseEntity
{
    public Guid UserId { get; set; }

    public Guid ProductId { get; set; }

    /// <summary>
    /// دور ۲۵ (الف): قیمت نهایی کالا وقتی به علاقه‌مندی‌ها اضافه شد (یا آخرین قیمتی که دربارهٔ آن خبر دادیم).
    /// اگر قیمت به اندازهٔ درصد تنظیم‌شده از این عدد پایین‌تر بیاید، به مشتری خبر می‌دهیم. null = هنوز ثبت نشده (پر شدن خودکار، بدون اعلان).
    /// </summary>
    public decimal? WatchPrice { get; set; }

    /// <summary>دور ۲۵ (الف): زمان آخرین اعلان «ارزان شد» برای همین علاقه‌مندی (برای فاصلهٔ حداقل ۷ روزه بین دو اعلان).</summary>
    public DateTimeOffset? PriceAlertedAt { get; set; }

    [ForeignKey(nameof(UserId))]
    public User User { get; set; } = null!;

    [ForeignKey(nameof(ProductId))]
    public Product Product { get; set; } = null!;
}
