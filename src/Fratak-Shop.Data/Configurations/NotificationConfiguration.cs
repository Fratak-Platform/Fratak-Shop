using Fratak_Shop.Data.Entities.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Fratak_Shop.Data.Configurations;

public class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        // Cascade: notifications of a deleted user are removed with the account.
        builder.HasOne(n => n.User)
            .WithMany(u => u.Notifications)
            .HasForeignKey(n => n.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // Non-cascading author reference avoids a second delete path from Users.
        builder.HasOne(n => n.CreatedByUser)
            .WithMany()
            .HasForeignKey(n => n.CreatedByUserId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasIndex(n => new { n.UserId, n.IsRead });
        builder.HasIndex(n => n.Type);
    }
}
