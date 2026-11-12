using System.ComponentModel.DataAnnotations;
using Fratak_Shop.Data.Utilities;

namespace Fratak_Shop.Data.Entities.Searches;

/// <summary>
/// دور ۲۴ (رتبه‌بندی یادگیرنده، پیشنهاد ۳۰): چند بار مشتری‌ها بعد از جست‌وجوی یک عبارت روی یک کالا کلیک کرده‌اند.
/// مثال: عبارت «هدفون بی سیم» و کالای X با ۱۲ کلیک ← در مرتب‌سازی «مرتبط‌ترین» کالای X بالاتر می‌آید.
/// <para>محل استفاده: Core/Services/Searches/SearchLearningService.cs (ثبت کلیک) و ProductService (مرتب‌سازی Relevance).</para>
/// </summary>
public class SearchClickStat : BaseEntity
{
    /// <summary>عبارت نرمال‌شده (حروف عربی→فارسی، فاصله‌ها یکی، حروف کوچک). حداکثر ۱۰۰ نویسه.</summary>
    [Required]
    [MaxLength(100)]
    public string NormalizedQuery { get; set; } = null!;

    /// <summary>کالایی که کلیک شده.</summary>
    public Guid ProductId { get; set; }

    /// <summary>تعداد کلیک‌های یکتا (هر مرورگر روزی یک بار برای هر جفت عبارت/کالا).</summary>
    public int Clicks { get; set; }

    /// <summary>زمان آخرین کلیک؛ ردیف‌های خیلی قدیمی در نگه‌داری روزانه کم‌وزن یا پاک می‌شوند.</summary>
    public DateTimeOffset LastClickAt { get; set; }
}
