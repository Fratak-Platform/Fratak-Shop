using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Entities.Settings;

namespace Fratak_Shop.Core.DTOs.Settings.Query;

public class SiteSettingDto : BaseDto
{
    public string Key { get; set; } = null!;

    public string? Value { get; set; }

    public string? Group { get; set; }

    public string? Title { get; set; }

    public string? Description { get; set; }

    public SettingValueType ValueType { get; set; }

    public string? Options { get; set; }

    public bool IsPublic { get; set; }

    public int DisplayOrder { get; set; }

    /// <summary>گزینه‌های انتخابی برای فیلدهای Select.</summary>
    public List<SettingOptionDto> SelectOptions { get; set; } = [];

    /// <summary>مقدار بولی برای راحتی استفاده در ویو.</summary>
    public bool BoolValue => Value is "true" or "1" or "on" or "True";
}

public class SettingOptionDto
{
    public string Value { get; set; } = null!;

    public string Text { get; set; } = null!;
}

/// <summary>یک گروه تنظیمات (تب) در صفحه تنظیمات پنل مدیریت.</summary>
public class SettingGroupDto
{
    public string Group { get; set; } = null!;

    public string Title { get; set; } = null!;

    public List<SiteSettingDto> Settings { get; set; } = [];
}

/// <summary>
/// مجموعه تنظیمات آماده‌ی استفاده در قالب سایت (هدر، فوتر و اطلاعات تماس).
/// لایه وب آن را کش می‌کند تا در هر درخواست کوئری اضافه زده نشود.
/// </summary>
public class StorefrontSettings
{
    public string SiteName { get; set; } = "فراتک";
    public string SiteUrl { get; set; } = "https://fratak.ir";
    public string? SiteDescription { get; set; }
    public string? LogoImageName { get; set; }
    public string? FaviconImageName { get; set; }
    public string? FooterLogoImageName { get; set; }
    public string? PhoneNumber { get; set; }
    public string? MobileNumber { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
    public string? WorkingHours { get; set; }
    public string? PostalCode { get; set; }
    public string? InstagramUrl { get; set; }
    public string? TelegramUrl { get; set; }
    public string? WhatsappUrl { get; set; }
    public string? MapUrl { get; set; }
    public string? FooterAbout { get; set; }
    public string? Copyright { get; set; }
    public string? EnamadCode { get; set; }
    public string? SamandehiCode { get; set; }
    public string? TrustBadgesJson { get; set; }
    public string? HeaderAnnouncement { get; set; }
    public bool HeaderAnnouncementEnabled { get; set; }
    public bool ShowHeaderTopBar { get; set; } = true;
    public bool ShowSocialInHeader { get; set; }
    public bool ShowSocialInFooter { get; set; } = true;
    public string? FooterColumnsJson { get; set; }
    public string? PrimaryColor { get; set; } = "#0ea5e9";
    public string? DefaultShareImageName { get; set; }
    public bool EnableOnlinePayment { get; set; } = true;
    public bool EnableBankTransfer { get; set; } = true;
    public bool EnableCashOnDelivery { get; set; } = true;
    public decimal? FreeShippingThreshold { get; set; }
    public string? PaymentInstructions { get; set; }
    public string? BankTransferDeadlineHours { get; set; }
    public bool ShowOutOfStock { get; set; } = true;
    public bool ShowPricesToGuests { get; set; } = true;
    public string? DefaultMetaDescription { get; set; }
    public string? DefaultMetaKeywords { get; set; }
    public string? GoogleSiteVerification { get; set; }
    public string? GoogleAnalyticsId { get; set; }
    public string? GoogleTagManagerId { get; set; }
    public string? SearchConsoleUrl { get; set; }
    public string? RobotsExtra { get; set; }
    public bool ShowVerificationCodeInDevelopment { get; set; } = true;
    public string? InstagramFeedHtml { get; set; }
    public string? AboutShortText { get; set; }
}
