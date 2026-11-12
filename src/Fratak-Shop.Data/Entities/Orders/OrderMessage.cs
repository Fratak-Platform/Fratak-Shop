using System.ComponentModel.DataAnnotations;
using Fratak_Shop.Data.Utilities;

namespace Fratak_Shop.Data.Entities.Orders;

/// <summary>
/// پیام بین کارکنان و مشتری دربارهٔ یک سفارش مشخص (برخلاف <see cref="OrderNote"/> که داخلی است، این پیام را مشتری می‌بیند).
/// <para>
/// دو نوع دارد:
/// ۱) پیام ساده؛ مثلاً «سفارش شما فردا تحویل پست می‌شود».
/// ۲) سؤال «کالا ناموجود است»: کارمند یک قلم سفارش را انتخاب می‌کند و از مشتری می‌پرسد
///    «این کالا را حذف کنیم و بقیه را بفرستیم یا کل سفارش لغو شود؟». مشتری در صفحهٔ سفارش یکی را انتخاب می‌کند
///    و سیستم خودش قلم را حذف و مبلغش را به کیف پول برمی‌گرداند (یا کل سفارش را لغو می‌کند).
/// </para>
/// کاربرد: صفحهٔ جزئیات سفارش در پنل (<c>/admin/orders/{id}</c>) و صفحهٔ سفارش مشتری (<c>/profile/orders/{number}</c>).
/// </summary>
public class OrderMessage : BaseEntity
{
    public Guid OrderId { get; set; }

    /// <summary>true یعنی کارمند فرستاده؛ false یعنی پاسخ/پیام مشتری.</summary>
    public bool FromStaff { get; set; }

    public Guid? AuthorUserId { get; set; }

    /// <summary>نام نمایشی فرستنده (اسنپ‌شات)؛ برای مشتری به‌جای نام کارمند «پشتیبانی فروشگاه» نمایش داده می‌شود.</summary>
    [MaxLength(150)]
    public string? AuthorName { get; set; }

    [Required]
    [MaxLength(2000)]
    public string Body { get; set; } = null!;

    public OrderMessageKind Kind { get; set; } = OrderMessageKind.Message;

    /// <summary>برای سؤال «کالا ناموجود»: قلمی از سفارش که موجود نیست.</summary>
    public Guid? OrderItemId { get; set; }

    /// <summary>نام کالای ناموجود (اسنپ‌شات تا اگر قلم حذف شد، متن پیام بی‌معنا نشود).</summary>
    [MaxLength(300)]
    public string? ItemName { get; set; }

    /// <summary>وضعیت سؤال؛ برای پیام ساده همیشه <see cref="OrderMessageDecision.None"/> است.</summary>
    public OrderMessageDecision Decision { get; set; } = OrderMessageDecision.None;

    /// <summary>زمان پاسخ مشتری به سؤال.</summary>
    public DateTimeOffset? DecidedAt { get; set; }

    /// <summary>زمان دیده شدن پیام کارمند توسط مشتری (برای تیک «خوانده شد»).</summary>
    public DateTimeOffset? ReadByCustomerAt { get; set; }

    /// <summary>زمان دیده شدن پیام مشتری توسط کارکنان.</summary>
    public DateTimeOffset? ReadByStaffAt { get; set; }

    public Order Order { get; set; } = null!;
}

/// <summary>نوع پیام سفارش.</summary>
public enum OrderMessageKind
{
    None = 0,

    /// <summary>پیام متنی ساده.</summary>
    Message = 1,

    /// <summary>سؤال «کالا ناموجود است؛ حذف شود و ادامه دهیم یا سفارش لغو شود؟».</summary>
    ItemUnavailable = 2,
}

/// <summary>پاسخ مشتری به سؤال «کالا ناموجود».</summary>
public enum OrderMessageDecision
{
    /// <summary>پیام ساده (سؤالی ندارد).</summary>
    None = 0,

    /// <summary>منتظر پاسخ مشتری.</summary>
    Pending = 1,

    /// <summary>مشتری پذیرفت: کالا حذف شد و بقیهٔ سفارش ادامه پیدا می‌کند.</summary>
    RemoveItem = 2,

    /// <summary>مشتری کل سفارش را لغو کرد.</summary>
    CancelOrder = 3,

    /// <summary>سؤال بی‌اثر شد (مثلاً کارمند پس گرفت یا سفارش در این فاصله لغو شد).</summary>
    Withdrawn = 4,
}
