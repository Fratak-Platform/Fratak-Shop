using Fratak_Shop.Data.Entities.Marketing;

namespace Fratak_Shop.Core.DTOs.Banners.Command;

public class CreateBannerCommand
{
    public string? Title { get; set; }
    public string? Subtitle { get; set; }
    public string ImageName { get; set; } = null!;
    public string? MobileImageName { get; set; }
    public string? ImageAlt { get; set; }
    public string? ButtonText { get; set; }
    public string? LinkUrl { get; set; }
    public BannerPosition Position { get; set; } = BannerPosition.HeroSlider;
    public int DisplayOrder { get; set; }
    public DateTimeOffset? StartAt { get; set; }
    public DateTimeOffset? EndAt { get; set; }
    public bool IsActive { get; set; } = true;
}

public class EditBannerCommand
{
    public Guid Id { get; set; }
    public string? Title { get; set; }
    public string? Subtitle { get; set; }
    public string ImageName { get; set; } = null!;
    public string? MobileImageName { get; set; }
    public string? ImageAlt { get; set; }
    public string? ButtonText { get; set; }
    public string? LinkUrl { get; set; }
    public BannerPosition Position { get; set; } = BannerPosition.HeroSlider;
    public int DisplayOrder { get; set; }
    public DateTimeOffset? StartAt { get; set; }
    public DateTimeOffset? EndAt { get; set; }
    public bool IsActive { get; set; } = true;
}
