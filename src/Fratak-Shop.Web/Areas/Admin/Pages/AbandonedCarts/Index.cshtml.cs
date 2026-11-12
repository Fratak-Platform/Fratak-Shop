using Fratak_Shop.Core.DTOs.Admin;
using Fratak_Shop.Core.DTOs.Shopping.Query;
using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Core.Services.Shopping;
using Fratak_Shop.Data.Entities.Shopping;
using Fratak_Shop.Web.Helpers;
using Fratak_Shop.Web.Infrastructure.Admin;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Web.Areas.Admin.Pages.AbandonedCarts;

public sealed class AdminAbandonedRow
{
    public Guid Id { get; init; }
    public Guid UserId { get; init; }
    public string CustomerName { get; init; } = "";
    public string Phone { get; init; } = "";
    public AbandonedCartStatus Status { get; init; }
    public DateTimeOffset AbandonedAt { get; init; }
    public decimal CartAmount { get; init; }
    public int ItemCount { get; init; }
    public int RemindersSent { get; init; }
    public DateTimeOffset? LastReminderAt { get; init; }
    public string? CouponCode { get; init; }
    public Guid? RecoveredOrderId { get; init; }
    public decimal RecoveredAmount { get; init; }
    public List<string> Products { get; init; } = [];
}

[AdminPermission(AdminPermissions.MarketingManage, AdminPermissions.OrdersView)]
public class IndexModel(IAdminDataService data, IAbandonedCartService abandoned) : AdminPageModel
{
    [BindProperty(SupportsGet = true, Name = "period")] public string? Preset { get; set; }
    [BindProperty(SupportsGet = true, Name = "from")] public string? From { get; set; }
    [BindProperty(SupportsGet = true, Name = "to")] public string? To { get; set; }
    [BindProperty(SupportsGet = true, Name = "status")] public AbandonedCartStatus? Status { get; set; }
    [FromQuery(Name = "page")] public int PageNumber { get; set; } = 1;

    public ResolvedPeriod Period { get; private set; } = null!;
    public AbandonedCartStatsDto Stats { get; private set; } = new();
    public PagedList<AdminAbandonedRow> Items { get; private set; } = new([], 0, 1, 30);
    public List<(string Name, int Count)> TopProducts { get; private set; } = [];

    public async Task OnGetAsync()
    {
        Period = AdminPeriods.Resolve(Preset, From, To, "30d");
        Stats = await abandoned.GetStatsAsync(Period.Current.From, Period.Current.To);

        var query = Filtered();
        var total = await query.CountAsync();
        var pageNumber = ClampPage(PageNumber, total, 30);
        var rows = await Project(query.OrderByDescending(a => a.AbandonedAt).Skip((pageNumber - 1) * 30).Take(30)).ToListAsync();
        Items = new PagedList<AdminAbandonedRow>(rows, total, pageNumber, 30);

        // Products most often left in open carts (what to discount / restock).
        var top = await data.Query<CartItem>()
            .Where(i => data.Query<AbandonedCart>().Any(a => a.CartId == i.CartId && a.Status == AbandonedCartStatus.Open))
            .GroupBy(i => i.Product.Name)
            .Select(g => new { g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count).Take(8).ToListAsync();
        TopProducts = top.Select(x => (x.Key, x.Count)).ToList();
    }

    public async Task<IActionResult> OnGetExportAsync(string format = "xlsx")
    {
        Period = AdminPeriods.Resolve(Preset, From, To, "30d");
        var rows = await Project(Filtered().OrderByDescending(a => a.AbandonedAt).Take(50000)).ToListAsync();
        var table = new ExportTable("سبدهای رهاشده", "تاریخ", "مشتری", "موبایل", "وضعیت", "مبلغ سبد", "تعداد کالا", "یادآوری", "کوپن", "مبلغ بازگشتی", "کالاها");
        foreach (var r in rows)
            table.Add(Format.DateTime(r.AbandonedAt), r.CustomerName, r.Phone, StatusTitle(r.Status), r.CartAmount.ToString("0"), r.ItemCount.ToString(),
                r.RemindersSent.ToString(), r.CouponCode ?? "", r.RecoveredAmount.ToString("0"), string.Join("، ", r.Products));
        return Exporter.File(table, format, "abandoned-carts");
    }

    private IQueryable<AbandonedCart> Filtered()
    {
        var query = data.Query<AbandonedCart>();
        if (Period.Current.From is { } from) query = query.Where(a => a.AbandonedAt >= from);
        if (Period.Current.To is { } to) query = query.Where(a => a.AbandonedAt < to);
        if (Status is { } status) query = query.Where(a => a.Status == status);
        return query;
    }

    private IQueryable<AdminAbandonedRow> Project(IQueryable<AbandonedCart> query) => query.Select(a => new AdminAbandonedRow
    {
        Id = a.Id, UserId = a.UserId, CustomerName = ((a.User.FirstName ?? "") + " " + (a.User.LastName ?? "")).Trim(), Phone = a.User.PhoneNumber,
        Status = a.Status, AbandonedAt = a.AbandonedAt, CartAmount = a.CartAmount, ItemCount = a.ItemCount, RemindersSent = a.RemindersSent,
        LastReminderAt = a.LastReminderAt, CouponCode = a.CouponCode, RecoveredOrderId = a.RecoveredOrderId, RecoveredAmount = a.RecoveredAmount,
        Products = a.Cart.Items.Select(i => i.Product.Name).Take(5).ToList(),
    });

    public static string StatusTitle(AbandonedCartStatus status) => status switch
    {
        AbandonedCartStatus.Open => "باز – در حال یادآوری",
        AbandonedCartStatus.Recovered => "بازگشت و خرید",
        AbandonedCartStatus.Expired => "از دست رفته",
        AbandonedCartStatus.Cleared => "سبد خالی شد",
        _ => "—",
    };

    public static string StatusBadge(AbandonedCartStatus status) => status switch
    {
        AbandonedCartStatus.Open => "a-badge-yellow",
        AbandonedCartStatus.Recovered => "a-badge-green",
        AbandonedCartStatus.Expired => "a-badge-red",
        _ => "a-badge-gray",
    };
}
