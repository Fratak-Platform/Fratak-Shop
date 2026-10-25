namespace Fratak_Shop.Core.DTOs.Settings;

/// <summary>
/// Editable store settings (admin panel form). See <c>StoreSetting</c> for the meaning of each field.
/// </summary>
public class StoreSettingCommand
{
    public string SiteName { get; set; } = null!;
    public string? Tagline { get; set; }
    public string? SiteDescription { get; set; }
    public string? LogoUrl { get; set; }
    public string? FaviconUrl { get; set; }
    public string? SupportPhone { get; set; }
    public string? SupportEmail { get; set; }
    public string? Address { get; set; }
    public string? PostalCode { get; set; }
    public string? WorkingHours { get; set; }
    public string? MapEmbedUrl { get; set; }
    public string? InstagramUrl { get; set; }
    public string? TelegramUrl { get; set; }
    public string? WhatsAppUrl { get; set; }
    public string? LinkedinUrl { get; set; }
    public string? TwitterUrl { get; set; }
    public string? AparatUrl { get; set; }
    public string? YoutubeUrl { get; set; }
    public string? FooterTitle { get; set; }
    public string? FooterSubtitle { get; set; }
    public string? FooterText { get; set; }
    public string? CopyrightText { get; set; }
    public decimal FreeShippingThreshold { get; set; }
    public decimal MinimumOrderAmount { get; set; }
    public int DeliveryDaysAhead { get; set; } = 6;
    public string? DeliveryOffDays { get; set; } = "5";
    public int SameDayLeadMinutes { get; set; } = 120;
    public int UnpaidOrderTimeoutMinutes { get; set; } = 60;
    public bool AllowCustomerCancellation { get; set; } = true;
    public bool IsOnlinePaymentEnabled { get; set; } = true;
    public bool IsCashOnDeliveryEnabled { get; set; } = true;
    public decimal CashOnDeliveryMaxAmount { get; set; }
    public string? ZarinPalMerchantId { get; set; }
    public bool ZarinPalSandbox { get; set; } = true;
    public bool IsWalletEnabled { get; set; } = true;
    public bool IsWalletPaymentEnabled { get; set; } = true;
    public bool IsWalletTopUpEnabled { get; set; } = true;
    public bool IsWithdrawalEnabled { get; set; } = true;
    public decimal WalletMinTopUpAmount { get; set; } = 10_000;
    public decimal WalletMaxTopUpAmount { get; set; } = 100_000_000;
    public decimal WalletMinWithdrawalAmount { get; set; } = 50_000;
    public string TitleSeparator { get; set; } = "|";
    public string? DefaultMetaKeywords { get; set; }
    public string? DefaultOgImageUrl { get; set; }
    public string? GoogleSiteVerification { get; set; }
    public string? BingSiteVerification { get; set; }
    public string? RobotsTxtExtra { get; set; }
    public string? XSiteHandle { get; set; }
    public string? OrganizationLegalName { get; set; }
    public bool IsPasswordLoginEnabled { get; set; } = true;
    public bool IsOtpLoginEnabled { get; set; } = true;
    public bool IsOtpRegistrationEnabled { get; set; } = true;
    public bool IsTwoFactorAvailable { get; set; } = true;
    public bool IsCouponEnabled { get; set; } = true;
    public bool IsGuestOrderTrackingEnabled { get; set; } = true;
    public bool IsRecommendationEnabled { get; set; } = true;
    public bool IsLoyaltyEnabled { get; set; } = true;
    public string? LoyaltyProgramName { get; set; }
    public int? LoyaltyPointsExpireDays { get; set; }
    public bool IsReferralEnabled { get; set; } = true;
    public bool IsAnnouncementEnabled { get; set; }
    public string? AnnouncementText { get; set; }
    public string? AnnouncementUrl { get; set; }
    public string? AnnouncementColor { get; set; }
    public string? FooterButtonText { get; set; }
    public string? FooterButtonUrl { get; set; }
    public string? SellerEconomicCode { get; set; }
    public string? SellerNationalId { get; set; }
    public string? SellerRegistrationNumber { get; set; }
    public string? InvoiceNote { get; set; }
    public bool IsPwaEnabled { get; set; } = true;
    public string? PwaShortName { get; set; }
    public string? ThemeColor { get; set; }

    // Round 4
    public decimal WalletMaxWithdrawalAmount { get; set; }
    public int WalletMaxPendingWithdrawals { get; set; } = 1;
    public string? LoyaltyProgramDescription { get; set; }
    public string? LoyaltyReferralDescription { get; set; }
    public bool IsAbandonedCartEnabled { get; set; } = true;
    public int AbandonedCartAfterMinutes { get; set; } = 60;
    public decimal AbandonedCartMinAmount { get; set; }
    public int AbandonedCartExpireDays { get; set; } = 7;
    public bool IsReviewImageEnabled { get; set; } = true;
    public int ReviewMaxImages { get; set; } = 3;
    public int ReviewMaxImageSizeKb { get; set; } = 2048;
    public bool IsReviewOnlyForBuyers { get; set; }
    public bool IsInstantSearchEnabled { get; set; } = true;
    public string? InvoiceNumberPrefix { get; set; } = "INV";
    public bool IsPwaInstallPromptEnabled { get; set; } = true;
    public int PwaInstallPromptAfterPages { get; set; } = 3;
    public int PwaInstallPromptSnoozeDays { get; set; } = 14;
    public string? PwaInstallPromptTitle { get; set; }
    public string? PwaInstallPromptText { get; set; }

    // Round 5
    public bool IsProductAlertEnabled { get; set; } = true;
    public int ProductAlertMaxPerUser { get; set; } = 50;
    public bool IsGiftCardEnabled { get; set; } = true;
    public decimal GiftCardMinAmount { get; set; } = 100000m;
    public decimal GiftCardMaxAmount { get; set; } = 10000000m;
    public string? GiftCardPresetAmounts { get; set; } = "200000,500000,1000000,2000000";
    public int GiftCardValidDays { get; set; } = 365;
    public bool IsGiftCardCreditWithdrawable { get; set; }
    public string? GiftCardTerms { get; set; }
    public string? ReferralShareText { get; set; }
    public int ReferralCookieDays { get; set; } = 30;
    public bool IsCaptchaEnabled { get; set; } = true;
    public int CaptchaAfterFailedAttempts { get; set; } = 3;
    public bool IsNewDeviceAlertEnabled { get; set; } = true;
    public bool IsSaveForLaterEnabled { get; set; } = true;
    public bool IsPriceHistoryEnabled { get; set; } = true;
    public int PriceHistoryDays { get; set; } = 90;
    public bool IsAddressMapEnabled { get; set; } = true;
    public string? MapTileUrl { get; set; } = "https://tile.openstreetmap.org/{z}/{x}/{y}.png";
    public string? MapAttribution { get; set; } = "© OpenStreetMap";
    public decimal MapDefaultLatitude { get; set; } = 35.6892m;
    public decimal MapDefaultLongitude { get; set; } = 51.3890m;
    public int MapDefaultZoom { get; set; } = 12;
    public bool IsReturnEnabled { get; set; } = true;
    public int ReturnWindowDays { get; set; } = 7;
    public int ReturnMaxImages { get; set; } = 3;
    public string? ReturnPolicyText { get; set; }
    public bool IsOrderFeedbackEnabled { get; set; } = true;
    public int OrderFeedbackWindowDays { get; set; } = 30;
    public bool IsDarkModeEnabled { get; set; } = true;
    public int DefaultTheme { get; set; }
    public bool IsSearchSynonymsEnabled { get; set; } = true;
    public bool IsPopularSearchesEnabled { get; set; } = true;
    public int PopularSearchesCount { get; set; } = 8;
    public int PopularSearchMinCount { get; set; } = 3;
    public bool IsDidYouMeanEnabled { get; set; } = true;
    public bool IsCustomerAnswerEnabled { get; set; } = true;
    public bool IsCommentVotingEnabled { get; set; } = true;
    public bool IsSmartCompareEnabled { get; set; } = true;
    public bool IsProductVideoEnabled { get; set; } = true;
    public bool IsTicketEnabled { get; set; } = true;
    public int TicketMaxOpenPerUser { get; set; } = 5;
    public int TicketMaxAttachments { get; set; } = 3;
    public int TicketMaxAttachmentSizeKb { get; set; } = 2048;
    public bool IsWebPushEnabled { get; set; } = true;
    public string? VapidSubject { get; set; }
    public bool IsImageOptimizationEnabled { get; set; } = true;
    public int ImageQuality { get; set; } = 80;
    public bool IsPageCacheEnabled { get; set; } = true;
    public int PageCacheSeconds { get; set; } = 300;
    public bool RequirePurchaseInvoiceForProcessing { get; set; } = true;
    public int LowStockThreshold { get; set; } = 3;
    public int ErrorLogRetentionDays { get; set; } = 60;
    public int AuditLogRetentionDays { get; set; } = 365;
}

/// <summary>Store settings as read by the storefront (cached).</summary>
public class StoreSettingDto : StoreSettingCommand
{
    public Guid Id { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>Public VAPID key for browser push subscriptions (generated automatically).</summary>
    public string? VapidPublicKey { get; set; }

    public bool CanUseWebPush => IsWebPushEnabled && IsPwaEnabled && !string.IsNullOrWhiteSpace(VapidPublicKey);

    /// <summary>Gift card quick-choice amounts inside the allowed range.</summary>
    public IReadOnlyList<decimal> GiftCardPresets =>
        (GiftCardPresetAmounts ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => decimal.TryParse(part, out var amount) ? amount : 0)
            .Where(amount => amount >= GiftCardMinAmount && amount <= GiftCardMaxAmount)
            .Distinct()
            .OrderBy(amount => amount)
            .ToList();

    /// <summary>Days of the week without delivery.</summary>
    public IReadOnlySet<DayOfWeek> OffDays =>
        (DeliveryOffDays ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => int.TryParse(part, out var day) && day is >= 0 and <= 6 ? (DayOfWeek?)day : null)
            .Where(day => day.HasValue)
            .Select(day => day!.Value)
            .ToHashSet();

    public bool HasZarinPal => IsOnlinePaymentEnabled && !string.IsNullOrWhiteSpace(ZarinPalMerchantId);

    /// <summary>Cash on delivery is enabled and allowed for the given payable amount.</summary>
    public bool AllowsCashOnDelivery(decimal payableAmount) =>
        IsCashOnDeliveryEnabled && (CashOnDeliveryMaxAmount <= 0 || payableAmount <= CashOnDeliveryMaxAmount);

    public bool CanPayWithWallet => IsWalletEnabled && IsWalletPaymentEnabled;

    public bool CanTopUpWallet => IsWalletEnabled && IsWalletTopUpEnabled && HasZarinPal;

    /// <summary>Name of the customer club shown in the storefront.</summary>
    public string LoyaltyName => string.IsNullOrWhiteSpace(LoyaltyProgramName) ? "باشگاه مشتریان" : LoyaltyProgramName;

    /// <summary>At least one sign-in method is always available (password is the fallback).</summary>
    public bool PasswordLoginAvailable => IsPasswordLoginEnabled || !IsOtpLoginEnabled;

    public bool CanWithdraw => IsWalletEnabled && IsWithdrawalEnabled;
}
