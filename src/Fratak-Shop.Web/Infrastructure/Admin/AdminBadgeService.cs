using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Comments;
using Fratak_Shop.Data.Entities.Messages;
using Fratak_Shop.Data.Entities.Orders;
using Fratak_Shop.Data.Entities.Products;
using Fratak_Shop.Data.Entities.Support;
using Fratak_Shop.Data.Entities.Wallets;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Fratak_Shop.Web.Infrastructure.Admin;

/// <summary>Counters of pending work shown next to the menu items (cached for 30 seconds).</summary>
public sealed class AdminBadgeService(FratakShopDbContext context, IMemoryCache cache)
{
    public async Task<IReadOnlyDictionary<string, int>> GetAsync()
    {
        if (cache.TryGetValue<IReadOnlyDictionary<string, int>>("admin:badges", out var cached) && cached is not null)
            return cached;

        var since = DateTimeOffset.UtcNow.AddDays(-1);
        var badges = new Dictionary<string, int>
        {
            ["orders"] = await context.Orders.CountAsync(o =>
                (o.Status == OrderStatus.Pending && (o.PaymentStatus == PaymentStatus.Paid || o.PaymentMethod != PaymentMethod.OnlineGateway))
                || o.Status == OrderStatus.Confirmed),
            ["returns"] = await context.ReturnRequests.CountAsync(r => r.Status == ReturnStatus.Pending || r.Status == ReturnStatus.Received),
            ["comments"] = await context.Comments.CountAsync(c => c.Status == CommentStatus.Pending),
            ["tickets"] = await context.SupportTickets.CountAsync(t => t.Status == TicketStatus.Open || t.Status == TicketStatus.CustomerReplied),
            ["messages"] = await context.Messages.CountAsync(m => m.Status == MessageStatus.New),
            ["withdrawals"] = await context.WithdrawalRequests.CountAsync(w => w.Status == WithdrawalStatus.Pending),
            ["drafts"] = await context.BlogPosts.CountAsync(p => !p.IsPublished && p.SubmittedForReviewAt != null),
            ["seo"] = await context.SeoNotFoundLogs.CountAsync(l => !l.IsResolved && l.LastHitAt >= since),
            ["errors"] = await context.ErrorLogs.CountAsync(e => !e.IsResolved && e.CreatedAt >= since),
            // پیشنهادهای منتظر تأیید بررسی تأمین‌کننده (مرحلهٔ هفتم – فاز ۵).
            ["supplier"] = await context.SupplierChanges.CountAsync(c => c.Status == SupplierChangeStatus.Pending),
        };

        cache.Set("admin:badges", (IReadOnlyDictionary<string, int>)badges, TimeSpan.FromSeconds(30));
        return badges;
    }

    public void Invalidate() => cache.Remove("admin:badges");
}
