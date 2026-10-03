using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Entities.Navigation;

namespace Fratak_Shop.Core.DTOs.Navigation.Query;

public class MenuItemDto : BaseDto
{
    public MenuLocation Location { get; set; }
    public Guid? ParentId { get; set; }
    public string? ParentTitle { get; set; }
    public string Title { get; set; } = null!;
    public string? Url { get; set; }
    public string? Icon { get; set; }
    public string? BadgeText { get; set; }
    public string? BadgeColor { get; set; }
    public bool OpenInNewTab { get; set; }
    public bool ShowOnMobile { get; set; }
    public bool IsHighlighted { get; set; }
    public int DisplayOrder { get; set; }
    public bool IsActive { get; set; }

    public List<MenuItemDto> Children { get; set; } = [];

    public string LocationTitle => Location.GetTitle();

    public bool HasChildren => Children.Count > 0;
}

public class SocialLinkDto : BaseDto
{
    public SocialPlatform Platform { get; set; }
    public string? Url { get; set; }
    public string? Title { get; set; }
    public bool ShowInHeader { get; set; }
    public bool ShowInFooter { get; set; }
    public int DisplayOrder { get; set; }
    public bool IsActive { get; set; }

    public string PlatformTitle => string.IsNullOrWhiteSpace(Title) ? Platform.GetTitle() : Title!;
}

public static class MenuTitleExtensions
{
    public static string GetTitle(this MenuLocation location) => location switch
    {
        MenuLocation.Header => "منوی اصلی (هدر)",
        MenuLocation.Footer => "ستون‌های فوتر",
        MenuLocation.FooterBottom => "لینک‌های پایین فوتر",
        MenuLocation.ProfileSidebar => "منوی پنل کاربری",
        MenuLocation.QuickAccess => "دسترسی سریع",
        _ => "نامشخص",
    };

    public static string GetTitle(this SocialPlatform platform) => platform switch
    {
        SocialPlatform.Instagram => "اینستاگرام",
        SocialPlatform.Telegram => "تلگرام",
        SocialPlatform.WhatsApp => "واتس‌اپ",
        SocialPlatform.X => "ایکس (توییتر)",
        SocialPlatform.LinkedIn => "لینکدین",
        SocialPlatform.YouTube => "یوتیوب",
        SocialPlatform.Aparat => "آپارات",
        SocialPlatform.Facebook => "فیسبوک",
        SocialPlatform.Pinterest => "پینترست",
        SocialPlatform.Rubika => "روبیکا",
        SocialPlatform.Bale => "بله",
        SocialPlatform.Eitaa => "ایتا",
        SocialPlatform.Phone => "تلفن",
        SocialPlatform.Email => "ایمیل",
        _ => "نامشخص",
    };
}
