using Fratak_Shop.Data.Entities.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Fratak_Shop.Data.Configurations;

public class StoreSettingConfiguration : IEntityTypeConfiguration<StoreSetting>
{
    public void Configure(EntityTypeBuilder<StoreSetting> builder)
    {
        builder.Property(s => s.FreeShippingThreshold).HasPrecision(18, 0);
        builder.Property(s => s.MinimumOrderAmount).HasPrecision(18, 0);
        builder.Property(s => s.CashOnDeliveryMaxAmount).HasPrecision(18, 0);
        builder.Property(s => s.WalletMinTopUpAmount).HasPrecision(18, 0);
        builder.Property(s => s.WalletMaxTopUpAmount).HasPrecision(18, 0);
        builder.Property(s => s.WalletMinWithdrawalAmount).HasPrecision(18, 0);
        builder.Property(s => s.WalletMaxWithdrawalAmount).HasPrecision(18, 0);
        builder.Property(s => s.AbandonedCartMinAmount).HasPrecision(18, 0);

        // Existing rows get the same defaults as new installations.
        builder.Property(s => s.IsPasswordLoginEnabled).HasDefaultValue(true).HasSentinel(true);
        builder.Property(s => s.IsOtpLoginEnabled).HasDefaultValue(true).HasSentinel(true);
        builder.Property(s => s.IsOtpRegistrationEnabled).HasDefaultValue(true).HasSentinel(true);
        builder.Property(s => s.IsTwoFactorAvailable).HasDefaultValue(true).HasSentinel(true);
        builder.Property(s => s.IsCouponEnabled).HasDefaultValue(true).HasSentinel(true);
        builder.Property(s => s.IsGuestOrderTrackingEnabled).HasDefaultValue(true).HasSentinel(true);
        builder.Property(s => s.IsRecommendationEnabled).HasDefaultValue(true).HasSentinel(true);
        builder.Property(s => s.IsLoyaltyEnabled).HasDefaultValue(true).HasSentinel(true);
        builder.Property(s => s.IsReferralEnabled).HasDefaultValue(true).HasSentinel(true);
        builder.Property(s => s.IsPwaEnabled).HasDefaultValue(true).HasSentinel(true);
        builder.Property(s => s.IsPwaInstallPromptEnabled).HasDefaultValue(true).HasSentinel(true);
        builder.Property(s => s.IsAbandonedCartEnabled).HasDefaultValue(true).HasSentinel(true);
        builder.Property(s => s.IsReviewImageEnabled).HasDefaultValue(true).HasSentinel(true);
        builder.Property(s => s.IsInstantSearchEnabled).HasDefaultValue(true).HasSentinel(true);
        builder.Property(s => s.WalletMaxPendingWithdrawals).HasDefaultValue(1).HasSentinel(1);
        builder.Property(s => s.AbandonedCartAfterMinutes).HasDefaultValue(60).HasSentinel(60);
        builder.Property(s => s.AbandonedCartExpireDays).HasDefaultValue(7).HasSentinel(7);
        builder.Property(s => s.ReviewMaxImages).HasDefaultValue(3).HasSentinel(3);
        builder.Property(s => s.ReviewMaxImageSizeKb).HasDefaultValue(2048).HasSentinel(2048);
        builder.Property(s => s.PwaInstallPromptAfterPages).HasDefaultValue(3).HasSentinel(3);
        builder.Property(s => s.PwaInstallPromptSnoozeDays).HasDefaultValue(14).HasSentinel(14);
        builder.Property(s => s.InvoiceNumberPrefix).HasDefaultValue("INV");

        // Round 5
        builder.Property(s => s.IsProductAlertEnabled).HasDefaultValue(true).HasSentinel(true);
        builder.Property(s => s.ProductAlertMaxPerUser).HasDefaultValue(50).HasSentinel(50);
        builder.Property(s => s.IsGiftCardEnabled).HasDefaultValue(true).HasSentinel(true);
        builder.Property(s => s.GiftCardMinAmount).HasPrecision(18, 0);
        builder.Property(s => s.GiftCardMinAmount).HasDefaultValue(100000m).HasSentinel(100000m);
        builder.Property(s => s.GiftCardMaxAmount).HasPrecision(18, 0);
        builder.Property(s => s.GiftCardMaxAmount).HasDefaultValue(10000000m).HasSentinel(10000000m);
        builder.Property(s => s.GiftCardPresetAmounts).HasDefaultValue("200000,500000,1000000,2000000");
        builder.Property(s => s.GiftCardValidDays).HasDefaultValue(365).HasSentinel(365);
        builder.Property(s => s.ReferralCookieDays).HasDefaultValue(30).HasSentinel(30);
        builder.Property(s => s.IsCaptchaEnabled).HasDefaultValue(true).HasSentinel(true);
        builder.Property(s => s.CaptchaAfterFailedAttempts).HasDefaultValue(3).HasSentinel(3);
        builder.Property(s => s.IsNewDeviceAlertEnabled).HasDefaultValue(true).HasSentinel(true);
        builder.Property(s => s.IsSaveForLaterEnabled).HasDefaultValue(true).HasSentinel(true);
        builder.Property(s => s.IsPriceHistoryEnabled).HasDefaultValue(true).HasSentinel(true);
        builder.Property(s => s.PriceHistoryDays).HasDefaultValue(90).HasSentinel(90);
        builder.Property(s => s.IsAddressMapEnabled).HasDefaultValue(true).HasSentinel(true);
        builder.Property(s => s.MapTileUrl).HasDefaultValue("https://tile.openstreetmap.org/{z}/{x}/{y}.png");
        builder.Property(s => s.MapAttribution).HasDefaultValue("© OpenStreetMap");
        builder.Property(s => s.MapDefaultLatitude).HasPrecision(9, 6);
        builder.Property(s => s.MapDefaultLatitude).HasDefaultValue(35.6892m).HasSentinel(35.6892m);
        builder.Property(s => s.MapDefaultLongitude).HasPrecision(9, 6);
        builder.Property(s => s.MapDefaultLongitude).HasDefaultValue(51.3890m).HasSentinel(51.3890m);
        builder.Property(s => s.MapDefaultZoom).HasDefaultValue(12).HasSentinel(12);
        builder.Property(s => s.IsReturnEnabled).HasDefaultValue(true).HasSentinel(true);
        builder.Property(s => s.ReturnWindowDays).HasDefaultValue(7).HasSentinel(7);
        builder.Property(s => s.ReturnMaxImages).HasDefaultValue(3).HasSentinel(3);
        builder.Property(s => s.IsOrderFeedbackEnabled).HasDefaultValue(true).HasSentinel(true);
        builder.Property(s => s.OrderFeedbackWindowDays).HasDefaultValue(30).HasSentinel(30);
        builder.Property(s => s.IsDarkModeEnabled).HasDefaultValue(true).HasSentinel(true);
        builder.Property(s => s.IsSearchSynonymsEnabled).HasDefaultValue(true).HasSentinel(true);
        builder.Property(s => s.IsPopularSearchesEnabled).HasDefaultValue(true).HasSentinel(true);
        builder.Property(s => s.PopularSearchesCount).HasDefaultValue(8).HasSentinel(8);
        builder.Property(s => s.PopularSearchMinCount).HasDefaultValue(3).HasSentinel(3);
        builder.Property(s => s.IsDidYouMeanEnabled).HasDefaultValue(true).HasSentinel(true);
        builder.Property(s => s.IsCustomerAnswerEnabled).HasDefaultValue(true).HasSentinel(true);
        builder.Property(s => s.IsCommentVotingEnabled).HasDefaultValue(true).HasSentinel(true);
        builder.Property(s => s.IsSmartCompareEnabled).HasDefaultValue(true).HasSentinel(true);
        builder.Property(s => s.IsProductVideoEnabled).HasDefaultValue(true).HasSentinel(true);
        builder.Property(s => s.IsTicketEnabled).HasDefaultValue(true).HasSentinel(true);
        builder.Property(s => s.TicketMaxOpenPerUser).HasDefaultValue(5).HasSentinel(5);
        builder.Property(s => s.TicketMaxAttachments).HasDefaultValue(3).HasSentinel(3);
        builder.Property(s => s.TicketMaxAttachmentSizeKb).HasDefaultValue(2048).HasSentinel(2048);
        builder.Property(s => s.IsWebPushEnabled).HasDefaultValue(true).HasSentinel(true);
        builder.Property(s => s.IsImageOptimizationEnabled).HasDefaultValue(true).HasSentinel(true);
        builder.Property(s => s.ImageQuality).HasDefaultValue(80).HasSentinel(80);
        builder.Property(s => s.IsPageCacheEnabled).HasDefaultValue(true).HasSentinel(true);
        builder.Property(s => s.PageCacheSeconds).HasDefaultValue(300).HasSentinel(300);
        builder.Property(s => s.RequirePurchaseInvoiceForProcessing).HasDefaultValue(true).HasSentinel(true);
        builder.Property(s => s.LowStockThreshold).HasDefaultValue(3).HasSentinel(3);
        builder.Property(s => s.ErrorLogRetentionDays).HasDefaultValue(60).HasSentinel(60);
        builder.Property(s => s.AuditLogRetentionDays).HasDefaultValue(365).HasSentinel(365);

        // مرحلهٔ هفتم – زمان‌بندی سفارش، لینک پرداخت و محدودیت پرداخت در محل.
        builder.Property(s => s.IsOrderSlaEnabled).HasDefaultValue(true).HasSentinel(true);
        builder.Property(s => s.SlaSupplierOrderHours).HasDefaultValue(24).HasSentinel(24);
        builder.Property(s => s.SlaPreparationHours).HasDefaultValue(48).HasSentinel(48);
        builder.Property(s => s.SlaTrackingCodeHours).HasDefaultValue(12).HasSentinel(12);
        builder.Property(s => s.SlaDeliveryMinDays).HasDefaultValue(3).HasSentinel(3);
        builder.Property(s => s.SlaDeliveryMaxDays).HasDefaultValue(4).HasSentinel(4);
        builder.Property(s => s.SlaWarningPercent).HasDefaultValue(75).HasSentinel(75);
        builder.Property(s => s.PaymentLinkHours).HasDefaultValue(48).HasSentinel(48);
        builder.ToTable(t => t.HasCheckConstraint(
            "CK_StoreSettings_Round7Orders",
            "[SlaSupplierOrderHours] >= 1 AND [SlaPreparationHours] >= 1 AND [SlaTrackingCodeHours] >= 1 " +
            "AND [SlaDeliveryMinDays] >= 0 AND [SlaDeliveryMaxDays] >= [SlaDeliveryMinDays] AND [PaymentLinkHours] >= 1 " +
            "AND [CodMinDeliveredOrders] >= 0 AND [CodMaxReturnedOrders] >= 0"));

        builder.ToTable(t => t.HasCheckConstraint(
            "CK_StoreSettings_Round6",
            "[LowStockThreshold] >= 0 AND [ErrorLogRetentionDays] >= 1 AND [AuditLogRetentionDays] >= 1"));

        builder.ToTable(t => t.HasCheckConstraint(
            "CK_StoreSettings_Round5",
            "[ProductAlertMaxPerUser] >= 0 AND [GiftCardMinAmount] > 0 AND [GiftCardMaxAmount] >= [GiftCardMinAmount] AND [GiftCardValidDays] >= 0 " +
            "AND [ReferralCookieDays] >= 0 AND [CaptchaAfterFailedAttempts] >= 0 AND [PriceHistoryDays] > 0 AND [MapDefaultZoom] BETWEEN 1 AND 19 " +
            "AND [ReturnWindowDays] >= 0 AND [ReturnMaxImages] BETWEEN 0 AND 10 AND [OrderFeedbackWindowDays] >= 0 AND [DefaultTheme] BETWEEN 0 AND 2 " +
            "AND [PopularSearchesCount] BETWEEN 0 AND 30 AND [PopularSearchMinCount] >= 1 AND [TicketMaxOpenPerUser] >= 1 " +
            "AND [TicketMaxAttachments] BETWEEN 0 AND 10 AND [TicketMaxAttachmentSizeKb] > 0 AND [ImageQuality] BETWEEN 40 AND 95 AND [PageCacheSeconds] >= 0"));

        builder.ToTable(t => t.HasCheckConstraint(
            "CK_StoreSettings_Values",
            "[FreeShippingThreshold] >= 0 AND [MinimumOrderAmount] >= 0 AND [CashOnDeliveryMaxAmount] >= 0 " +
            "AND [WalletMinTopUpAmount] >= 0 AND [WalletMaxTopUpAmount] >= [WalletMinTopUpAmount] " +
            "AND [WalletMinWithdrawalAmount] >= 0 AND [UnpaidOrderTimeoutMinutes] > 0 AND [DeliveryDaysAhead] > 0"));

        builder.ToTable(t => t.HasCheckConstraint(
            "CK_StoreSettings_Round4",
            "[WalletMaxWithdrawalAmount] >= 0 AND [WalletMaxPendingWithdrawals] > 0 AND [AbandonedCartAfterMinutes] > 0 " +
            "AND [AbandonedCartMinAmount] >= 0 AND [AbandonedCartExpireDays] > 0 AND [ReviewMaxImages] >= 0 AND [ReviewMaxImages] <= 10 " +
            "AND [ReviewMaxImageSizeKb] > 0 AND [PwaInstallPromptAfterPages] >= 0 AND [PwaInstallPromptSnoozeDays] >= 0"));
    }
}
