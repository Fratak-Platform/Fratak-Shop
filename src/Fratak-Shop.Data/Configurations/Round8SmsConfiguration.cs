using Fratak_Shop.Data.Entities.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Fratak_Shop.Data.Configurations;

/// <summary>جدول تنظیمات سرویس پیامک: مقادیر عددی فقط مثبت (بررسی در خود SQL Server هم انجام می‌شود).</summary>
public class SmsProviderConfigConfiguration : IEntityTypeConfiguration<SmsProviderConfig>
{
    public void Configure(EntityTypeBuilder<SmsProviderConfig> builder)
    {
        builder.ToTable(t =>
        {
            t.HasCheckConstraint("CK_SmsProviderConfigs_Line", "[LineNumber] IS NULL OR [LineNumber] > 0");
            t.HasCheckConstraint("CK_SmsProviderConfigs_Template", "[OtpTemplateId] IS NULL OR [OtpTemplateId] > 0");
        });
    }
}
