using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Core.Services.Wallets;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Entities.Admin;
using Fratak_Shop.Data.Entities.Loyalty;
using Fratak_Shop.Data.Entities.Orders;
using Fratak_Shop.Data.Entities.Support;
using Fratak_Shop.Data.Entities.Users;
using Fratak_Shop.Data.Entities.Wallets;
using Fratak_Shop.Web.Infrastructure.Admin;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Web.Areas.Admin.Pages.Customers;

[AdminPermission(AdminPermissions.CustomersView)]
public class DetailModel(IAdminDataService data, IAdminCustomerService customers, IWalletService wallet, IAdminAccessService access) : AdminPageModel
{
    public User Customer { get; private set; } = null!;
    public List<Order> Orders { get; private set; } = [];
    public List<UserAddress> Addresses { get; private set; } = [];
    public Dictionary<Guid, string> LocationNames { get; private set; } = new();
    public List<WalletTransaction> WalletTransactions { get; private set; } = [];
    public List<LoyaltyTransaction> PointTransactions { get; private set; } = [];
    public List<SupportTicket> Tickets { get; private set; } = [];
    public List<SecurityLog> SecurityEvents { get; private set; } = [];
    public List<string> RoleNames { get; private set; } = [];
    public int OrderCount { get; private set; }
    public decimal TotalSpent { get; private set; }
    public int CommentCount { get; private set; }
    public int ReferralCount { get; private set; }
    public string? ReferredBy { get; private set; }

    public async Task<IActionResult> OnGetAsync(Guid id)
    {
        var user = await data.Query<User>().FirstOrDefaultAsync(u => u.Id == id);
        if (user is null)
            return NotFound();
        Customer = user;

        var orders = data.Query<Order>().Where(o => o.UserId == id);
        OrderCount = await orders.CountAsync();
        TotalSpent = await orders.Where(o => o.PaymentStatus == PaymentStatus.Paid && o.Status != OrderStatus.Cancelled && o.Status != OrderStatus.Returned)
            .SumAsync(o => (decimal?)(o.PayableAmount - o.RefundedAmount)) ?? 0;
        Orders = await orders.OrderByDescending(o => o.CreatedAt).Take(15).ToListAsync();
        Addresses = await data.Query<UserAddress>().Where(a => a.UserId == id).OrderByDescending(a => a.IsDefault).ToListAsync();
        var locationIds = Addresses.SelectMany(a => new[] { a.ProvinceId, a.CityId }).Distinct().ToList();
        var locations = await data.Query<Data.Entities.Locations.Location>().Where(l => locationIds.Contains(l.Id)).Select(l => new { l.Id, l.Name }).ToListAsync();
        LocationNames = locations.ToDictionary(l => l.Id, l => l.Name);
        if (CanAny(AdminPermissions.CustomersWallet, AdminPermissions.FinanceView))
            WalletTransactions = await data.Query<WalletTransaction>().Where(t => t.UserId == id).OrderByDescending(t => t.CreatedAt).Take(15).ToListAsync();
        PointTransactions = await data.Query<LoyaltyTransaction>().Where(t => t.UserId == id).OrderByDescending(t => t.CreatedAt).Take(10).ToListAsync();
        if (Can(AdminPermissions.TicketsManage))
            Tickets = await data.Query<SupportTicket>().Where(t => t.UserId == id).OrderByDescending(t => t.LastActivityAt).Take(10).ToListAsync();
        if (CanAny(AdminPermissions.AuditView, AdminPermissions.MonitoringView))
            SecurityEvents = await data.Query<SecurityLog>().Where(s => s.UserId == id).OrderByDescending(s => s.CreatedAt).Take(12).ToListAsync();
        RoleNames = (await access.GetUserRolesAsync(id)).Select(r => r.Name).ToList();
        CommentCount = await data.Query<Data.Entities.Comments.Comment>().CountAsync(c => c.UserId == id);
        ReferralCount = await data.Query<User>().CountAsync(u => u.ReferredByUserId == id);
        if (user.ReferredByUserId is { } referrerId)
            ReferredBy = await data.Query<User>().Where(u => u.Id == referrerId).Select(u => u.PhoneNumber).FirstOrDefaultAsync();
        return Page();
    }

    [AdminPermission(AdminPermissions.CustomersManage)]
    public async Task<IActionResult> OnPostProfileAsync(Guid id, string? firstName, string? lastName, string? email, string? adminNote)
    {
        ShowResult(await customers.UpdateProfileAsync(id, firstName, lastName, email, adminNote));
        return Redirect($"/admin/customers/{id}");
    }

    [AdminPermission(AdminPermissions.CustomersManage)]
    public async Task<IActionResult> OnPostBlockAsync(Guid id, bool blocked, string? reason)
    {
        ShowResult(await customers.SetBlockedAsync(id, blocked, reason, Access));
        return Redirect($"/admin/customers/{id}");
    }

    [AdminPermission(AdminPermissions.CustomersManage)]
    public async Task<IActionResult> OnPostSessionsAsync(Guid id)
    {
        ShowResult(await customers.RevokeSessionsAsync(id, Access));
        return Redirect($"/admin/customers/{id}");
    }

    [AdminPermission(AdminPermissions.CustomersWallet)]
    public async Task<IActionResult> OnPostWalletAsync(Guid id, decimal amount, string direction, string? description, bool promotional = true)
    {
        if (amount <= 0 || amount > 1_000_000_000)
        {
            ErrorMessage = "مبلغ معتبر وارد کنید.";
            return Redirect($"/admin/customers/{id}#wallet");
        }
        if (string.IsNullOrWhiteSpace(description))
        {
            ErrorMessage = "علت تغییر موجودی را بنویسید (در تاریخچه مشتری نمایش داده می‌شود).";
            return Redirect($"/admin/customers/{id}#wallet");
        }

        var signed = direction == "debit" ? -amount : amount;
        var text = $"{description.Trim()} – {ActorName}";
        ShowResult(await wallet.AdjustBalanceAsync(id, signed, text.Length > 450 ? text[..450] : text, promotional));
        return Redirect($"/admin/customers/{id}#wallet");
    }

    [AdminPermission(AdminPermissions.LoyaltyManage, AdminPermissions.CustomersManage)]
    public async Task<IActionResult> OnPostPointsAsync(Guid id, int points, string direction, string? reason)
    {
        var signed = direction == "remove" ? -Math.Abs(points) : Math.Abs(points);
        ShowResult(await customers.AdjustPointsAsync(id, signed, reason));
        return Redirect($"/admin/customers/{id}#points");
    }
}
