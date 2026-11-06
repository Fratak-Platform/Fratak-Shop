using System.ComponentModel.DataAnnotations;
using Fratak_Shop.Data.Utilities;

namespace Fratak_Shop.Data.Entities.Users;

/// <summary>
/// برچسب دستی کارکنان روی یک مشتری؛ مثلاً «عمده‌فروش»، «حساس به قیمت»، «مشتری VIP».
/// <para>
/// کاربرد: صفحهٔ جزئیات مشتری در پنل (افزودن/حذف برچسب) و فیلتر فهرست مشتریان بر اساس برچسب.
/// هر مشتری هر برچسب را فقط یک بار دارد (ایندکس یکتا روی UserId + Tag).
/// </para>
/// </summary>
public class CustomerTag : BaseEntity
{
    public Guid UserId { get; set; }

    /// <summary>متن برچسب (حداکثر ۴۰ نویسه، بدون فاصلهٔ اضافی).</summary>
    [Required]
    [MaxLength(40)]
    public string Tag { get; set; } = null!;

    /// <summary>نام کارمندی که برچسب را زد (برای پاسخ به «چه کسی این را زده؟»).</summary>
    [MaxLength(150)]
    public string? CreatedByName { get; set; }

    public User User { get; set; } = null!;
}

/// <summary>
/// یادداشت داخلی کارکنان دربارهٔ یک مشتری؛ مثلاً «تماس گرفت، آدرس دوم را برای ارسال‌ها ترجیح می‌دهد».
/// مشتری هرگز این یادداشت‌ها را نمی‌بیند. کاربرد: صفحهٔ جزئیات مشتری در پنل.
/// </summary>
public class CustomerNote : BaseEntity
{
    public Guid UserId { get; set; }

    public Guid? AuthorUserId { get; set; }

    [MaxLength(150)]
    public string? AuthorName { get; set; }

    [Required]
    [MaxLength(2000)]
    public string Content { get; set; } = null!;

    /// <summary>یادداشت سنجاق‌شده بالای صفحهٔ مشتری می‌ماند (مثلاً هشدار مهم).</summary>
    public bool IsPinned { get; set; }

    public User User { get; set; } = null!;
}
