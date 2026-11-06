using Fratak_Shop.Data.Entities.Pages;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Fratak_Shop.Data.Configurations;

public class PageAlertConfiguration : IEntityTypeConfiguration<PageAlert>
{
    public void Configure(EntityTypeBuilder<PageAlert> builder)
    {
        builder.HasIndex(a => new { a.Page, a.Position });
        builder.HasIndex(a => a.IsActive);
        builder.HasIndex(a => a.ProductId);
        builder.HasIndex(a => a.CategoryId);
    }
}
