using Fratak_Shop.Data.Entities.Messages;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Fratak_Shop.Data.Configurations;

public class MessageConfiguration : IEntityTypeConfiguration<Message>
{
    public void Configure(EntityTypeBuilder<Message> builder)
    {
        // NoAction avoids SQL Server multiple cascade/set-null paths while
        // preserving the message history as an explicit retention record.
        builder.HasOne(m => m.User)
            .WithMany(u => u.Messages)
            .HasForeignKey(m => m.UserId)
            .OnDelete(DeleteBehavior.NoAction);

        // SetNull: the agent who answered can be removed from the panel safely.
        builder.HasOne(m => m.RepliedByUser)
            .WithMany()
            .HasForeignKey(m => m.RepliedByUserId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(m => m.UserId);
        builder.HasIndex(m => m.Status);
        builder.HasIndex(m => m.RepliedByUserId);
    }
}
