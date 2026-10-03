using Fratak_Shop.Core.DTOs.Dashboard.Query;
using Fratak_Shop.Core.Mappers.Order;
using Fratak_Shop.Core.Mappers.Product;
using Fratak_Shop.Core.Services.Orders;
using Fratak_Shop.Core.Services.Payments;
using Fratak_Shop.Core.Services.Seo;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Messages;
using Fratak_Shop.Data.Entities.Seo;
using Fratak_Shop.Data.Entities.Users;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Core.Services.Dashboard;

public class DashboardService(
    FratakShopDbContext context,
    IOrderService orderService,
    ISeoAuditService seoAuditService,
    IBankTransferService bankTransferService,
    ISeoStatisticsService statisticsService) : IDashboardService
{
    public async Task<AdminDashboardDto> GetAsync(CancellationToken cancellationToken = default)
    {
        var dashboard = new AdminDashboardDto
        {
            Orders = await orderService.GetStatsAsync(cancellationToken),
            Seo = await seoAuditService.GetDashboardAsync(cancellationToken),
            ProductCount = await context.Products.CountAsync(cancellationToken),
            ActiveProductCount = await context.Products.CountAsync(product => product.IsActive, cancellationToken),
            OutOfStockCount = await context.Products.CountAsync(product => product.StockQuantity <= 0, cancellationToken),
            CategoryCount = await context.Categories.CountAsync(cancellationToken),
            BlogPostCount = await context.BlogPosts.CountAsync(cancellationToken),
            UserCount = await context.Users.CountAsync(cancellationToken),
            StaffCount = await context.Users.CountAsync(user => user.Role != UserRole.Customer && user.Role != UserRole.None, cancellationToken),
            NewMessagesCount = await context.Messages.CountAsync(message => message.Status == MessageStatus.New, cancellationToken),
            PendingCommentsCount = await context.Comments.CountAsync(
                comment => comment.Status == Data.Entities.Comments.CommentStatus.Pending, cancellationToken),
            PendingBankTransfers = await bankTransferService.GetPendingCountAsync(cancellationToken),
            PendingSeoAudits = await context.SeoAuditRecords.CountAsync(
                record => record.Status == SeoAuditStatus.PendingReview, cancellationToken),
        };

        var lowStock = await context.Products
            .Include(product => product.Images)
            .Include(product => product.Category)
            .Where(product => product.IsActive && product.StockQuantity <= 5)
            .OrderBy(product => product.StockQuantity)
            .Take(8)
            .ToListAsync(cancellationToken);

        dashboard.LowStockProducts = lowStock.Select(product => product.MapCard()).ToList();

        var latestOrders = await context.Orders
            .Include(order => order.Items)
            .ThenInclude(item => item.Product)
            .Include(order => order.StatusHistory)
            .Include(order => order.PaymentTransactions)
            .Include(order => order.BankTransferPayment)
            .Include(order => order.User)
            .OrderByDescending(order => order.CreatedAt)
            .Take(8)
            .ToListAsync(cancellationToken);

        dashboard.LatestOrders = latestOrders.MapDto();

        dashboard.LatestMessages = await context.Messages
            .OrderByDescending(message => message.CreatedAt)
            .Take(6)
            .ToListAsync(cancellationToken);

        return dashboard;
    }
}
