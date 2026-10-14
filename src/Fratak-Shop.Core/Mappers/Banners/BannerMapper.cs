using Fratak_Shop.Core.DTOs.Banners.Query;
using Fratak_Shop.Data.Entities.Marketing;

namespace Fratak_Shop.Core.Mappers.Banners;

public static class BannerMapper
{
    public static BannerDto Map(this Banner banner)
    {
        return new BannerDto
        {
            Id = banner.Id,
            CreatedAt = banner.CreatedAt,
            UpdatedAt = banner.UpdatedAt,
            IsDeleted = banner.IsDeleted,
            DeletedAt = banner.DeletedAt,
            Title = banner.Title,
            Subtitle = banner.Subtitle,
            ImageName = banner.ImageName,
            MobileImageName = banner.MobileImageName,
            ImageAlt = banner.ImageAlt,
            ButtonText = banner.ButtonText,
            LinkUrl = banner.LinkUrl,
            DisplayOrder = banner.DisplayOrder,
            StartAt = banner.StartAt,
            EndAt = banner.EndAt,
            IsActive = banner.IsActive,
        };
    }

    public static List<BannerDto> Map(this List<Banner> banners)
    {
        return banners.Select(banner => banner.Map()).ToList();
    }
}
