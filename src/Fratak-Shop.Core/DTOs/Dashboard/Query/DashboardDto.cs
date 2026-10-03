using Fratak_Shop.Core.DTOs.Orders.Query;
using Fratak_Shop.Core.DTOs.Products.Query;
using Fratak_Shop.Core.DTOs.Seo.Query;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Entities.Messages;

namespace Fratak_Shop.Core.DTOs.Dashboard.Query;

/// <summary>داشبورد پنل مدیریت: همه شاخص‌های کلیدی سیستم در یک نگاه.</summary>
public class AdminDashboardDto
{
    public OrderStatsDto Orders { get; set; } = new();
    public SeoDashboardDto Seo { get; set; } = new();

    public int ProductCount { get; set; }
    public int ActiveProductCount { get; set; }
    public int OutOfStockCount { get; set; }
    public int CategoryCount { get; set; }
    public int BlogPostCount { get; set; }
    public int UserCount { get; set; }
    public int StaffCount { get; set; }
    public int NewMessagesCount { get; set; }
    public int PendingCommentsCount { get; set; }
    public int PendingBankTransfers { get; set; }
    public int PendingSeoAudits { get; set; }

    public List<ProductCardDto> LowStockProducts { get; set; } = [];
    public List<OrderDto> LatestOrders { get; set; } = [];
    public List<Message> LatestMessages { get; set; } = [];
}
