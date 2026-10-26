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
            // پیشنهادهای موتور سئو منتظر تأیید (مرحلهٔ هفتم – فاز ۸).
            ["seo-review"] = await context.SeoSuggestions.CountAsync(s => s.Status == Fratak_Shop.Data.Entities.SeoEngine.SeoSuggestionStatus.Pending),
        };

        cache.Set("admin:badges", (IReadOnlyDictionary<string, int>)badges, TimeSpan.FromSeconds(30));
        return badges;
    }

    public void Invalidate() => cache.Remove("admin:badges");

    /// <summary>
    /// شمارنده‌های شخصی هر کارمند (فاز ۱۰): «approvals» = درخواست‌های تأییدی که او می‌تواند تصمیم بگیرد،
    /// «tasks» = کارهای باز دیرشده یا موعد امروز. ۳۰ ثانیه برای هر کارمند کش می‌شود.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, int>> GetPersonalAsync(Fratak_Shop.Core.DTOs.Admin.AdminAccess access,
        Fratak_Shop.Core.Services.Team.IApprovalService approvals, Fratak_Shop.Core.Services.Team.IStaffTaskService tasks)
    {
        var key = $"admin:badges:{access.UserId:N}";
        if (cache.TryGetValue<IReadOnlyDictionary<string, int>>(key, out var cached) && cached is not null)
            return cached;
        var counts = await tasks.CountsAsync(access.UserId, DateTimeOffset.UtcNow);
        var personal = new Dictionary<string, int>
        {
            ["approvals"] = await approvals.CountDecidableAsync(access),
            ["tasks"] = counts.Overdue + counts.DueToday,
        };
        cache.Set(key, (IReadOnlyDictionary<string, int>)personal, TimeSpan.FromSeconds(30));
        return personal;
    }

    /// <summary>پاک کردن شمارنده‌های شخصی یک کارمند بعد از تغییر کارها یا تصمیم دربارهٔ تأییدها.</summary>
    public void InvalidatePersonal(Guid userId) => cache.Remove($"admin:badges:{userId:N}");
}
