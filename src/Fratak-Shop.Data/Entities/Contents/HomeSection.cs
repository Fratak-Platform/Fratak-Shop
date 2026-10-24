using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Fratak_Shop.Data.Entities.Categories;
using Fratak_Shop.Data.Utilities;

namespace Fratak_Shop.Data.Entities.Contents;

/// <summary>
/// One block of the home page. Order, titles, item count and visibility of every
/// block are managed from the admin panel.
/// </summary>
public class HomeSection : BaseEntity
{
    public HomeSectionType Type { get; set; }

    [MaxLength(200)]
    public string? Title { get; set; }

    [MaxLength(300)]
    public string? Subtitle { get; set; }

    /// <summary>"View all" link of the block (optional).</summary>
    [MaxLength(500)]
    public string? LinkUrl { get; set; }

    /// <summary>Number of items shown (products, posts, categories, ...).</summary>
    public int ItemCount { get; set; } = 10;

    /// <summary>Source category of a <see cref="HomeSectionType.CategoryProducts"/> block.</summary>
    public Guid? CategoryId { get; set; }

    public bool IsActive { get; set; } = true;

    public int DisplayOrder { get; set; }

    [ForeignKey(nameof(CategoryId))]
    public Category? Category { get; set; }
}

public enum HomeSectionType
{
    None = 0,
    HeroSlider = 1,        // اسلایدر اصلی
    Categories = 2,        // دسته‌بندی‌ها
    Deals = 3,             // تخفیف‌های زمان‌دار
    PromoBanners = 4,      // بنرهای تبلیغاتی دوتایی
    BestSellers = 5,       // پرفروش‌ترین‌ها
    WideBanners = 6,       // بنر تمام‌عرض
    Newest = 7,            // جدیدترین‌ها
    MostViewed = 8,        // پربازدیدترین‌ها
    CategoryProducts = 9,  // محصولات یک دسته
    Features = 10,         // مزایای خرید
    Brands = 11,           // برندها
    LatestPosts = 12,      // آخرین مقالات
}
