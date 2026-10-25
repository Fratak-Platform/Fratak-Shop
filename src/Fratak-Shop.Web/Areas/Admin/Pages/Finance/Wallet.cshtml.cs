using Fratak_Shop.Core.DTOs.Admin;
using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Entities.Wallets;
using Fratak_Shop.Web.Helpers;
using Fratak_Shop.Web.Infrastructure.Admin;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Web.Areas.Admin.Pages.Finance;

public sealed class AdminWalletRow
{
    public Guid Id { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public Guid UserId { get; init; }
    public string UserName { get; init; } = "";
    public string Phone { get; init; } = "";
    public WalletTransactionType Type { get; init; }
    public decimal Amount { get; init; }
    public decimal BonusAmount { get; init; }
    public decimal BalanceAfter { get; init; }
    public decimal BonusBalanceAfter { get; init; }
    public string? Description { get; init; }
    public Guid? OrderId { get; init; }
    public string? OrderNumber { get; init; }
}

[AdminPermission(AdminPermissions.FinanceView, AdminPermissions.CustomersWallet)]
public class WalletModel(IAdminDataService data) : AdminPageModel
{
    [BindProperty(SupportsGet = true, Name = "type")] public WalletTransactionType? Type { get; set; }
    [BindProperty(SupportsGet = true, Name = "q")] public string? Search { get; set; }
    [BindProperty(SupportsGet = true, Name = "from")] public string? From { get; set; }
    [BindProperty(SupportsGet = true, Name = "to")] public string? To { get; set; }
    [FromQuery(Name = "page")] public int PageNumber { get; set; } = 1;

    public PagedList<AdminWalletRow> Items { get; private set; } = new([], 0, 1, 40);
    public decimal SumIn { get; private set; }
    public decimal SumOut { get; private set; }

    public async Task OnGetAsync()
    {
        var query = Filter();
        var total = await query.CountAsync();
        SumIn = await query.Where(t => t.Amount + t.BonusAmount > 0).SumAsync(t => (decimal?)(t.Amount + t.BonusAmount)) ?? 0;
        SumOut = await query.Where(t => t.Amount + t.BonusAmount < 0).SumAsync(t => (decimal?)(t.Amount + t.BonusAmount)) ?? 0;
        var pageNumber = ClampPage(PageNumber, total, 40);
        var rows = await Project(query.OrderByDescending(t => t.CreatedAt)).Skip((pageNumber - 1) * 40).Take(40).ToListAsync();
        Items = new PagedList<AdminWalletRow>(rows, total, pageNumber, 40);
    }

    [AdminPermission(AdminPermissions.FinanceExport)]
    public async Task<IActionResult> OnGetExportAsync(string format = "xlsx")
    {
        var rows = await Project(Filter().OrderByDescending(t => t.CreatedAt)).Take(100_000).ToListAsync();
        var table = new ExportTable("تراکنش‌های کیف پول", "تاریخ", "مشتری", "موبایل", "نوع", "مبلغ نقدی", "اعتبار هدیه", "مانده نقدی", "مانده هدیه", "شرح", "سفارش");
        foreach (var t in rows)
            table.Add(t.CreatedAt, t.UserName, t.Phone, Display.WalletTransactionType(t.Type), t.Amount, t.BonusAmount, t.BalanceAfter, t.BonusBalanceAfter, t.Description, t.OrderNumber);
        return Exporter.File(table, format, "wallet-transactions");
    }

    private IQueryable<WalletTransaction> Filter()
    {
        var query = data.Query<WalletTransaction>();
        if (Type is { } type)
            query = query.Where(t => t.Type == type);
        var period = PersianDateInput.Period(From, To);
        if (period.From is { } from)
            query = query.Where(t => t.CreatedAt >= from);
        if (period.To is { } to)
            query = query.Where(t => t.CreatedAt < to);
        if (!string.IsNullOrWhiteSpace(Search))
        {
            var term = SearchText.NormalizeDigits(Search.Trim());
            query = query.Where(t => t.User.PhoneNumber.Contains(term) || (t.Description != null && t.Description.Contains(term)));
        }
        return query;
    }

    private IQueryable<AdminWalletRow> Project(IQueryable<WalletTransaction> query)
    {
        var orders = data.Query<Data.Entities.Orders.Order>();
        return query.Select(t => new AdminWalletRow
        {
            Id = t.Id,
            CreatedAt = t.CreatedAt,
            UserId = t.UserId,
            UserName = ((t.User.FirstName ?? "") + " " + (t.User.LastName ?? "")).Trim(),
            Phone = t.User.PhoneNumber,
            Type = t.Type,
            Amount = t.Amount,
            BonusAmount = t.BonusAmount,
            BalanceAfter = t.BalanceAfter,
            BonusBalanceAfter = t.BonusBalanceAfter,
            Description = t.Description,
            OrderId = t.OrderId,
            OrderNumber = t.OrderId == null ? null : orders.Where(o => o.Id == t.OrderId).Select(o => o.OrderNumber).FirstOrDefault(),
        });
    }
}
