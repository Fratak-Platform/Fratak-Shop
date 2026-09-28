using Fratak_Shop.Data.Entities.Blogs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Fratak_Shop.Data.Configurations;

public class BlogPostConfiguration : IEntityTypeConfiguration<BlogPost>
{
    public void Configure(EntityTypeBuilder<BlogPost> builder)
    {
        // SetNull: deleting the author preserves the post.
        builder.HasOne(p => p.Author)
            .WithMany(u => u.BlogPosts)
            .HasForeignKey(p => p.AuthorId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(p => p.BlogCategory)
            .WithMany(c => c.BlogPosts)
            .HasForeignKey(p => p.BlogCategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(p => p.Slug)
            .IsUnique()
            .HasFilter("[IsDeleted] = 0");

        builder.HasIndex(p => p.AuthorId);
        builder.HasIndex(p => p.BlogCategoryId);
        builder.HasIndex(p => p.IsPublished);

        builder.OwnsOne(p => p.Seo);
    }
}
