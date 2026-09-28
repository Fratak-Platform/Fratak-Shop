using Fratak_Shop.Data.Entities.Comments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Fratak_Shop.Data.Configurations;

public class CommentConfiguration : IEntityTypeConfiguration<Comment>
{
    public void Configure(EntityTypeBuilder<Comment> builder)
    {
        // SetNull: deleting the user preserves the comment.
        builder.HasOne(c => c.User)
            .WithMany(u => u.Comments)
            .HasForeignKey(c => c.UserId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(c => c.Product)
            .WithMany(p => p.Comments)
            .HasForeignKey(c => c.ProductId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(c => c.BlogPost)
            .WithMany(b => b.Comments)
            .HasForeignKey(c => c.BlogPostId)
            .OnDelete(DeleteBehavior.Cascade);

        // Restrict: comments that already have replies cannot be deleted.
        builder.HasOne(c => c.Parent)
            .WithMany(c => c.Replies)
            .HasForeignKey(c => c.ParentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(c => c.ProductId);
        builder.HasIndex(c => c.BlogPostId);
        builder.HasIndex(c => c.Status);

        builder.ToTable(t =>
        {
            t.HasCheckConstraint(
                "CK_Comments_ProductOrBlogPost",
                "([ProductId] IS NOT NULL AND [BlogPostId] IS NULL) OR ([ProductId] IS NULL AND [BlogPostId] IS NOT NULL)");

            t.HasCheckConstraint(
                "CK_Comments_Rating",
                "[Rating] IS NULL OR ([Rating] >= 1 AND [Rating] <= 5)");
        });
    }
}
