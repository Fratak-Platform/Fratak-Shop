using Fratak_Shop.Core.Utilities;

namespace Fratak_Shop.Core.DTOs.Banners.Query;

public class BannerDto : BaseDto
{
    public string? Title { get; set; }
    public string? Subtitle { get; set; }
    public string? ImageName { get; set; }
    public string? MobileImageName { get; set; }
    public string? ImageAlt { get; set; }
    public string? ButtonText { get; set; }
    public string? LinkUrl { get; set; }

    /// <summary>محل نمایش بنر در سایت (اسلایدر اصلی، کنار فروشگاه، بالای مقاله و ...).</summary>
    public Data.Entities.Marketing.BannerPosition Position { get; set; }
    public int DisplayOrder { get; set; }
    public DateTimeOffset? StartAt { get; set; }
    public DateTimeOffset? EndAt { get; set; }
    public bool IsActive { get; set; }
}