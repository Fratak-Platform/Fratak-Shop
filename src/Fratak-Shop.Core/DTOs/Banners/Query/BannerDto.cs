using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Entities.Marketing;

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
    public BannerPosition Position { get; set; }
    public int DisplayOrder { get; set; }
    public DateTimeOffset? StartAt { get; set; }
    public DateTimeOffset? EndAt { get; set; }
    public bool IsActive { get; set; }
}