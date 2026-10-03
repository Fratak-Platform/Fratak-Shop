using System.ComponentModel.DataAnnotations;
using Fratak_Shop.Data.Utilities;

namespace Fratak_Shop.Data.Entities.Marketing;

public class Banner : BaseEntity
{
    [MaxLength(250)]
    public string? Title { get; set; }

    [MaxLength(500)]
    public string? Subtitle { get; set; }

    /// <summary>
    /// File name of the desktop image stored on the server.
    /// </summary>
    [Required]
    [MaxLength(250)]
    public string ImageName { get; set; } = null!;

    /// <summary>
    /// File name of the mobile image stored on the server.
    /// </summary>
    [MaxLength(250)]
    public string? MobileImageName { get; set; }

    [MaxLength(250)]
    public string? ImageAlt { get; set; }

    [MaxLength(100)]
    public string? ButtonText { get; set; }

    [MaxLength(500)]
    public string? LinkUrl { get; set; }

    /// <summary>
    /// Placement of the banner inside the storefront; the admin decides where each
    /// banner appears without any code change.
    /// </summary>
    public BannerPosition Position { get; set; } = BannerPosition.HomeHero;

    /// <summary>
    /// Simple performance counters shown in the banner report.
    /// </summary>
    public int ViewCount { get; set; }

    public int ClickCount { get; set; }

    public int DisplayOrder { get; set; }

    public DateTimeOffset? StartAt { get; set; }

    public DateTimeOffset? EndAt { get; set; }

    public bool IsActive { get; set; } = true;
}

/// <summary>
/// Where a banner is rendered on the storefront.
/// </summary>
public enum BannerPosition
{
    None = 0,
    /// <summary>اسلایدر اصلی صفحه خانه</summary>
    HomeHero = 1,
    /// <summary>بنر عریض بالای صفحه خانه</summary>
    HomeTop = 2,
    /// <summary>بنر وسط صفحه خانه</summary>
    HomeMiddle = 3,
    /// <summary>بنر پایین صفحه خانه</summary>
    HomeBottom = 4,
    /// <summary>بنر کنار ستون فیلتر فروشگاه</summary>
    ShopSidebar = 5,
    /// <summary>بنر پایین صفحه محصول</summary>
    ProductBottom = 6,
    /// <summary>بنر بالای لیست مقالات</summary>
    BlogTop = 7,
    /// <summary>بنر صفحه سبد خرید</summary>
    CartTop = 8,
}
