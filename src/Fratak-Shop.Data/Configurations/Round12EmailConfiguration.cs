using Fratak_Shop.Data.Entities.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Fratak_Shop.Data.Configurations;

/// <summary>
/// جدول تنظیمات سرویس ایمیل (مرحلهٔ دوازدهم): درگاه بین ۱ تا ۶۵۵۳۵ و سقف ساعتی نامنفی
/// (بررسی در خود SQL Server هم انجام می‌شود تا داده‌ی خراب حتی با دور زدن سرویس هم ذخیره نشود).
/// </summary>
public class EmailProviderConfigConfiguration : IEntityTypeConfiguration<EmailProviderConfig>
{
    public void Configure(EntityTypeBuilder<EmailProviderConfig> builder)
    {
        builder.Property(c => c.Port).HasDefaultValue(587).HasSentinel(587);
        builder.Property(c => c.MaxPerHour).HasDefaultValue(100).HasSentinel(100);
        builder.ToTable(t =>
        {
            t.HasCheckConstraint("CK_EmailProviderConfigs_Port", "[Port] BETWEEN 1 AND 65535");
            t.HasCheckConstraint("CK_EmailProviderConfigs_MaxPerHour", "[MaxPerHour] >= 0");
        });
    }
}
