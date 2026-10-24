using Fratak_Shop.Core.DTOs.Settings;
using Fratak_Shop.Data.Entities.Settings;

namespace Fratak_Shop.Core.Mappers.Settings;

public static class StoreSettingMapper
{
    public static StoreSettingDto Map(this StoreSetting entity)
    {
        return new StoreSettingDto
        {
            Id = entity.Id,
            UpdatedAt = entity.UpdatedAt,
            SiteName = entity.SiteName,
            Tagline = entity.Tagline,
            SiteDescription = entity.SiteDescription,
            LogoUrl = entity.LogoUrl,
            FaviconUrl = entity.FaviconUrl,
            SupportPhone = entity.SupportPhone,
            SupportEmail = entity.SupportEmail,
            Address = entity.Address,
            PostalCode = entity.PostalCode,
            WorkingHours = entity.WorkingHours,
            InstagramUrl = entity.InstagramUrl,
            TelegramUrl = entity.TelegramUrl,
            WhatsAppUrl = entity.WhatsAppUrl,
            LinkedinUrl = entity.LinkedinUrl,
            TwitterUrl = entity.TwitterUrl,
            AparatUrl = entity.AparatUrl,
            YoutubeUrl = entity.YoutubeUrl,
            FooterTitle = entity.FooterTitle,
            FooterSubtitle = entity.FooterSubtitle,
            FooterText = entity.FooterText,
            CopyrightText = entity.CopyrightText,
            FreeShippingThreshold = entity.FreeShippingThreshold,
            MinimumOrderAmount = entity.MinimumOrderAmount,
            DeliveryDaysAhead = entity.DeliveryDaysAhead,
            DeliveryOffDays = entity.DeliveryOffDays,
            SameDayLeadMinutes = entity.SameDayLeadMinutes,
            UnpaidOrderTimeoutMinutes = entity.UnpaidOrderTimeoutMinutes,
            AllowCustomerCancellation = entity.AllowCustomerCancellation,
            IsOnlinePaymentEnabled = entity.IsOnlinePaymentEnabled,
            IsCashOnDeliveryEnabled = entity.IsCashOnDeliveryEnabled,
            CashOnDeliveryMaxAmount = entity.CashOnDeliveryMaxAmount,
            ZarinPalMerchantId = entity.ZarinPalMerchantId,
            ZarinPalSandbox = entity.ZarinPalSandbox,
            IsWalletEnabled = entity.IsWalletEnabled,
            IsWalletPaymentEnabled = entity.IsWalletPaymentEnabled,
            IsWalletTopUpEnabled = entity.IsWalletTopUpEnabled,
            IsWithdrawalEnabled = entity.IsWithdrawalEnabled,
            WalletMinTopUpAmount = entity.WalletMinTopUpAmount,
            WalletMaxTopUpAmount = entity.WalletMaxTopUpAmount,
            WalletMinWithdrawalAmount = entity.WalletMinWithdrawalAmount,
            TitleSeparator = entity.TitleSeparator,
            DefaultMetaKeywords = entity.DefaultMetaKeywords,
            DefaultOgImageUrl = entity.DefaultOgImageUrl,
            GoogleSiteVerification = entity.GoogleSiteVerification,
            BingSiteVerification = entity.BingSiteVerification,
            RobotsTxtExtra = entity.RobotsTxtExtra,
            OrganizationLegalName = entity.OrganizationLegalName,
        };
    }

    /// <summary>Copies the editable values of the command onto the entity.</summary>
    public static void Apply(this StoreSetting entity, StoreSettingCommand command)
    {
        entity.SiteName = command.SiteName;
        entity.Tagline = command.Tagline;
        entity.SiteDescription = command.SiteDescription;
        entity.LogoUrl = command.LogoUrl;
        entity.FaviconUrl = command.FaviconUrl;
        entity.SupportPhone = command.SupportPhone;
        entity.SupportEmail = command.SupportEmail;
        entity.Address = command.Address;
        entity.PostalCode = command.PostalCode;
        entity.WorkingHours = command.WorkingHours;
        entity.InstagramUrl = command.InstagramUrl;
        entity.TelegramUrl = command.TelegramUrl;
        entity.WhatsAppUrl = command.WhatsAppUrl;
        entity.LinkedinUrl = command.LinkedinUrl;
        entity.TwitterUrl = command.TwitterUrl;
        entity.AparatUrl = command.AparatUrl;
        entity.YoutubeUrl = command.YoutubeUrl;
        entity.FooterTitle = command.FooterTitle;
        entity.FooterSubtitle = command.FooterSubtitle;
        entity.FooterText = command.FooterText;
        entity.CopyrightText = command.CopyrightText;
        entity.FreeShippingThreshold = command.FreeShippingThreshold;
        entity.MinimumOrderAmount = command.MinimumOrderAmount;
        entity.DeliveryDaysAhead = command.DeliveryDaysAhead;
        entity.DeliveryOffDays = command.DeliveryOffDays;
        entity.SameDayLeadMinutes = command.SameDayLeadMinutes;
        entity.UnpaidOrderTimeoutMinutes = command.UnpaidOrderTimeoutMinutes;
        entity.AllowCustomerCancellation = command.AllowCustomerCancellation;
        entity.IsOnlinePaymentEnabled = command.IsOnlinePaymentEnabled;
        entity.IsCashOnDeliveryEnabled = command.IsCashOnDeliveryEnabled;
        entity.CashOnDeliveryMaxAmount = command.CashOnDeliveryMaxAmount;
        entity.ZarinPalMerchantId = command.ZarinPalMerchantId;
        entity.ZarinPalSandbox = command.ZarinPalSandbox;
        entity.IsWalletEnabled = command.IsWalletEnabled;
        entity.IsWalletPaymentEnabled = command.IsWalletPaymentEnabled;
        entity.IsWalletTopUpEnabled = command.IsWalletTopUpEnabled;
        entity.IsWithdrawalEnabled = command.IsWithdrawalEnabled;
        entity.WalletMinTopUpAmount = command.WalletMinTopUpAmount;
        entity.WalletMaxTopUpAmount = command.WalletMaxTopUpAmount;
        entity.WalletMinWithdrawalAmount = command.WalletMinWithdrawalAmount;
        entity.TitleSeparator = command.TitleSeparator;
        entity.DefaultMetaKeywords = command.DefaultMetaKeywords;
        entity.DefaultOgImageUrl = command.DefaultOgImageUrl;
        entity.GoogleSiteVerification = command.GoogleSiteVerification;
        entity.BingSiteVerification = command.BingSiteVerification;
        entity.RobotsTxtExtra = command.RobotsTxtExtra;
        entity.OrganizationLegalName = command.OrganizationLegalName;
    }
}
