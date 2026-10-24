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

        builder.ToTable(t => t.HasCheckConstraint(
            "CK_StoreSettings_Values",
            "[FreeShippingThreshold] >= 0 AND [MinimumOrderAmount] >= 0 AND [CashOnDeliveryMaxAmount] >= 0 " +
            "AND [WalletMinTopUpAmount] >= 0 AND [WalletMaxTopUpAmount] >= [WalletMinTopUpAmount] " +
            "AND [WalletMinWithdrawalAmount] >= 0 AND [UnpaidOrderTimeoutMinutes] > 0 AND [DeliveryDaysAhead] > 0"));
    }
}
