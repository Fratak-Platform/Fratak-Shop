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
    /// Place of the banner on the storefront.
    /// </summary>
    public BannerPosition Position { get; set; } = BannerPosition.HeroSlider;

    public int DisplayOrder { get; set; }

    public DateTimeOffset? StartAt { get; set; }

    public DateTimeOffset? EndAt { get; set; }

    public bool IsActive { get; set; } = true;
}

public enum BannerPosition
{
    None = 0,
    HeroSlider = 1,       // اسلایدر اصلی صفحه نخست
    WideBanner = 2,       // بنر تمام‌عرض میان صفحه نخست
    PromoCard = 3,        // کارت‌های تبلیغاتی دوتایی صفحه نخست
}
