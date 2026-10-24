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
