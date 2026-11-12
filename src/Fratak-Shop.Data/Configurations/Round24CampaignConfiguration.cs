using Fratak_Shop.Data.Entities.Marketing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Fratak_Shop.Data.Configurations;

/// <summary>دور ۲۴ (بنر هوشمند): پیدا کردن پیش‌نویس هر گروه طرح و قاعده‌های فعال (برای ضریب امتیاز و پایان خودکار).</summary>
public class CampaignOfferConfiguration : IEntityTypeConfiguration<CampaignOffer>
{
    public void Configure(EntityTypeBuilder<CampaignOffer> builder)
    {
        builder.Property(o => o.PointsMultiplier).HasPrecision(4, 2);
        builder.HasIndex(o => o.VariantGroup);
        builder.HasIndex(o => new { o.Status, o.EndsAt });
    }
}
