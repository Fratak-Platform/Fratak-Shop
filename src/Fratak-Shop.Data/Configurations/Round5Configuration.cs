using Fratak_Shop.Data.Entities.Comments;
using Fratak_Shop.Data.Entities.Marketing;
using Fratak_Shop.Data.Entities.Notifications;
using Fratak_Shop.Data.Entities.Orders;
using Fratak_Shop.Data.Entities.Products;
using Fratak_Shop.Data.Entities.Searches;
using Fratak_Shop.Data.Entities.SeoRedirects;
using Fratak_Shop.Data.Entities.Shopping;
using Fratak_Shop.Data.Entities.Support;
using Fratak_Shop.Data.Entities.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Fratak_Shop.Data.Configurations;

// Product alerts, price history, videos, gift cards, known devices, saved cart items,
// returns, order feedback, search synonyms/statistics, comment votes, support tickets
// and browser push subscriptions. Foreign keys to users are Restrict (soft delete keeps
// history); children of an aggregate cascade with their parent.

public class ProductAlertConfiguration : IEntityTypeConfiguration<ProductAlert>
{
    public void Configure(EntityTypeBuilder<ProductAlert> builder)
    {
        builder.HasOne(a => a.User).WithMany().HasForeignKey(a => a.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(a => a.Product).WithMany().HasForeignKey(a => a.ProductId).OnDelete(DeleteBehavior.Restrict);

        builder.Property(a => a.PriceAtSubscribe).HasPrecision(18, 0);
        builder.Property(a => a.TargetPrice).HasPrecision(18, 0);
        builder.Property(a => a.NotifiedPrice).HasPrecision(18, 0);

        // One active alert of a type per customer and product.
        builder.HasIndex(a => new { a.UserId, a.ProductId, a.Type })
            .IsUnique()
            .HasFilter("[IsActive] = 1 AND [IsDeleted] = 0");
        builder.HasIndex(a => new { a.ProductId, a.IsActive, a.Type });

        builder.ToTable(t => t.HasCheckConstraint("CK_ProductAlerts_Values",
            "[PriceAtSubscribe] >= 0 AND ([TargetPrice] IS NULL OR [TargetPrice] > 0)"));
    }
}

public class ProductPriceHistoryConfiguration : IEntityTypeConfiguration<ProductPriceHistory>
{
    public void Configure(EntityTypeBuilder<ProductPriceHistory> builder)
    {
        builder.HasOne(h => h.Product).WithMany().HasForeignKey(h => h.ProductId).OnDelete(DeleteBehavior.Cascade);
        builder.Property(h => h.Price).HasPrecision(18, 0);
        builder.Property(h => h.FinalPrice).HasPrecision(18, 0);
        builder.HasIndex(h => new { h.ProductId, h.CreatedAt });
    }
}

public class ProductVideoConfiguration : IEntityTypeConfiguration<ProductVideo>
{
    public void Configure(EntityTypeBuilder<ProductVideo> builder)
    {
        builder.HasOne(v => v.Product).WithMany(p => p.Videos).HasForeignKey(v => v.ProductId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(v => new { v.ProductId, v.DisplayOrder });
        builder.ToTable(t => t.HasCheckConstraint("CK_ProductVideos_Provider", "[Provider] BETWEEN 1 AND 3"));
    }
}

public class GiftCardConfiguration : IEntityTypeConfiguration<GiftCard>
{
    public void Configure(EntityTypeBuilder<GiftCard> builder)
    {
        builder.HasOne(g => g.Purchaser).WithMany().HasForeignKey(g => g.PurchaserUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(g => g.RedeemedBy).WithMany().HasForeignKey(g => g.RedeemedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(g => g.PaymentTransaction).WithMany().HasForeignKey(g => g.PaymentTransactionId).OnDelete(DeleteBehavior.Restrict);

        builder.Property(g => g.Amount).HasPrecision(18, 0);
        builder.HasIndex(g => g.CodeHash).IsUnique();
        builder.HasIndex(g => g.Reference).IsUnique();
        builder.HasIndex(g => g.PurchaserUserId);
        builder.HasIndex(g => g.RedeemedByUserId);
        builder.HasIndex(g => g.PaymentTransactionId);

        builder.ToTable(t => t.HasCheckConstraint("CK_GiftCards_Amount", "[Amount] > 0"));
    }
}

public class UserKnownDeviceConfiguration : IEntityTypeConfiguration<UserKnownDevice>
{
    public void Configure(EntityTypeBuilder<UserKnownDevice> builder)
    {
        builder.HasOne(d => d.User).WithMany().HasForeignKey(d => d.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(d => new { d.UserId, d.DeviceHash }).IsUnique();
    }
}

public class SavedCartItemConfiguration : IEntityTypeConfiguration<SavedCartItem>
{
    public void Configure(EntityTypeBuilder<SavedCartItem> builder)
    {
        builder.HasOne(i => i.Cart).WithMany(c => c.SavedItems).HasForeignKey(i => i.CartId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(i => i.Product).WithMany().HasForeignKey(i => i.ProductId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(i => new { i.CartId, i.ProductId }).IsUnique();
        builder.ToTable(t => t.HasCheckConstraint("CK_SavedCartItems_Quantity", "[Quantity] > 0"));
    }
}

public class ReturnRequestConfiguration : IEntityTypeConfiguration<ReturnRequest>
{
    public void Configure(EntityTypeBuilder<ReturnRequest> builder)
    {
        builder.HasOne(r => r.Order).WithMany().HasForeignKey(r => r.OrderId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(r => r.User).WithMany().HasForeignKey(r => r.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(r => r.Reason).WithMany().HasForeignKey(r => r.ReasonId).OnDelete(DeleteBehavior.SetNull);

        builder.Property(r => r.RequestedAmount).HasPrecision(18, 0);
        builder.Property(r => r.ApprovedAmount).HasPrecision(18, 0);
        builder.Property(r => r.RefundedAmount).HasPrecision(18, 0);
        builder.Property(r => r.RefundedBonusAmount).HasPrecision(18, 0);

        builder.HasIndex(r => r.RequestNumber).IsUnique();
        builder.HasIndex(r => r.OrderId);
        builder.HasIndex(r => new { r.UserId, r.Status });

        builder.ToTable(t => t.HasCheckConstraint("CK_ReturnRequests_Amounts",
            "[RequestedAmount] >= 0 AND ([ApprovedAmount] IS NULL OR [ApprovedAmount] >= 0) AND [RefundedAmount] >= 0 " +
            "AND [RefundedBonusAmount] >= 0 AND [RefundedBonusAmount] <= [RefundedAmount]"));
    }
}

public class ReturnRequestItemConfiguration : IEntityTypeConfiguration<ReturnRequestItem>
{
    public void Configure(EntityTypeBuilder<ReturnRequestItem> builder)
    {
        builder.HasOne(i => i.ReturnRequest).WithMany(r => r.Items).HasForeignKey(i => i.ReturnRequestId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(i => i.OrderItem).WithMany().HasForeignKey(i => i.OrderItemId).OnDelete(DeleteBehavior.Restrict);
        builder.Property(i => i.UnitRefundAmount).HasPrecision(18, 0);
        builder.HasIndex(i => i.OrderItemId);
        builder.ToTable(t => t.HasCheckConstraint("CK_ReturnRequestItems_Values", "[Quantity] > 0 AND [UnitRefundAmount] >= 0"));
    }
}

public class ReturnRequestImageConfiguration : IEntityTypeConfiguration<ReturnRequestImage>
{
    public void Configure(EntityTypeBuilder<ReturnRequestImage> builder)
    {
        builder.HasOne(i => i.ReturnRequest).WithMany(r => r.Images).HasForeignKey(i => i.ReturnRequestId).OnDelete(DeleteBehavior.Cascade);
        builder.ToTable(t => t.HasCheckConstraint("CK_ReturnRequestImages_Size", "[SizeBytes] > 0"));
    }
}

public class OrderFeedbackConfiguration : IEntityTypeConfiguration<OrderFeedback>
{
    public void Configure(EntityTypeBuilder<OrderFeedback> builder)
    {
        builder.HasOne(f => f.Order).WithMany().HasForeignKey(f => f.OrderId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(f => f.User).WithMany().HasForeignKey(f => f.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(f => f.OrderId).IsUnique();
        builder.HasIndex(f => f.UserId);
        builder.ToTable(t => t.HasCheckConstraint("CK_OrderFeedbacks_Ratings",
            "[OverallRating] BETWEEN 1 AND 5 AND [DeliveryRating] BETWEEN 1 AND 5 AND [PackagingRating] BETWEEN 1 AND 5 AND [ProductMatchRating] BETWEEN 1 AND 5"));
    }
}

public class SearchQueryLogConfiguration : IEntityTypeConfiguration<SearchQueryLog>
{
    public void Configure(EntityTypeBuilder<SearchQueryLog> builder)
    {
        builder.HasIndex(q => q.NormalizedQuery).IsUnique();
        builder.HasIndex(q => new { q.IsHidden, q.SearchCount });
    }
}

public class CommentVoteConfiguration : IEntityTypeConfiguration<CommentVote>
{
    public void Configure(EntityTypeBuilder<CommentVote> builder)
    {
        builder.HasOne(v => v.Comment).WithMany(c => c.Votes).HasForeignKey(v => v.CommentId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(v => v.User).WithMany().HasForeignKey(v => v.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(v => new { v.CommentId, v.UserId }).IsUnique();
    }
}

public class SupportTicketConfiguration : IEntityTypeConfiguration<SupportTicket>
{
    public void Configure(EntityTypeBuilder<SupportTicket> builder)
    {
        builder.HasOne(t => t.User).WithMany().HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(t => t.Department).WithMany().HasForeignKey(t => t.DepartmentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(t => t.Order).WithMany().HasForeignKey(t => t.OrderId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(t => t.TicketNumber).IsUnique();
        builder.HasIndex(t => new { t.UserId, t.Status });
        builder.HasIndex(t => new { t.Status, t.LastActivityAt });
    }
}

public class SupportTicketMessageConfiguration : IEntityTypeConfiguration<SupportTicketMessage>
{
    public void Configure(EntityTypeBuilder<SupportTicketMessage> builder)
    {
        builder.HasOne(m => m.Ticket).WithMany(t => t.Messages).HasForeignKey(m => m.TicketId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(m => m.User).WithMany().HasForeignKey(m => m.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(m => new { m.TicketId, m.CreatedAt });
    }
}

public class SupportTicketAttachmentConfiguration : IEntityTypeConfiguration<SupportTicketAttachment>
{
    public void Configure(EntityTypeBuilder<SupportTicketAttachment> builder)
    {
        builder.HasOne(a => a.Message).WithMany(m => m.Attachments).HasForeignKey(a => a.MessageId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(a => a.FileName).IsUnique();
        builder.ToTable(t => t.HasCheckConstraint("CK_SupportTicketAttachments_Size", "[SizeBytes] > 0"));
    }
}

public class SeoNotFoundLogConfiguration : IEntityTypeConfiguration<SeoNotFoundLog>
{
    public void Configure(EntityTypeBuilder<SeoNotFoundLog> builder)
    {
        builder.HasIndex(l => l.Path).IsUnique();
        builder.HasIndex(l => new { l.IsResolved, l.HitCount });
    }
}

public class WebPushSubscriptionConfiguration : IEntityTypeConfiguration<WebPushSubscription>
{
    public void Configure(EntityTypeBuilder<WebPushSubscription> builder)
    {
        builder.HasOne(s => s.User).WithMany().HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(s => s.EndpointHash).IsUnique();
        builder.HasIndex(s => s.UserId);
    }
}
