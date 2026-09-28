using Fratak_Shop.Data.Entities.Blogs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Fratak_Shop.Data.Configurations;

public class BlogCategoryConfiguration : IEntityTypeConfiguration<BlogCategory>
{
    public void Configure(EntityTypeBuilder<BlogCategory> builder)
    {
        builder.HasIndex(c => c.Slug)
            .IsUnique();

        builder.OwnsOne(c => c.Seo);
    }
}
