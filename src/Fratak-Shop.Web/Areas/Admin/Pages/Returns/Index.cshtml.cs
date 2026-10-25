using Fratak_Shop.Core.DTOs.Admin;
using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Entities.Orders;
using Fratak_Shop.Web.Infrastructure.Admin;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Web.Areas.Admin.Pages.Returns;

public sealed class AdminReturnRow
{
    public Guid Id { get; init; }
    public string RequestNumber { get; init; } = "";
    public Guid OrderId { get; init; }
    public string OrderNumber { get; init; } = "";
    public Guid UserId { get; init; }
    public string CustomerName { get; init; } = "";
    public string Phone { get; init; } = "";
    public string ReasonTitle { get; init; } = "";
    public ReturnStatus Status { get; init; }
    public decimal RequestedAmount { get; init; }
    public decimal? ApprovedAmount { get; init; }
    public decimal RefundedAmount { get; init; }
    public int ItemCount { get; init; }
    public int ImageCount { get; init; }
    public string? TrackingCode { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}

[AdminPermission(AdminPermissions.ReturnsManage)]
public class IndexModel(IAdminDataService data) : AdminPageModel
{
    [BindProperty(SupportsGet = true, Name = "status")] public ReturnStatus? Status { get; set; }
    [BindProperty(SupportsGet = true, Name = "q")] public string? Search { get; set; }
    [FromQuery(Name = "page")] public int PageNumber { get; set; } = 1;

    public PagedList<AdminReturnRow> Items { get; private set; } = new([], 0, 1, 30);
    public Dictionary<ReturnStatus, int> Counts { get; private set; } = new();

    public async Task OnGetAsync()
    {
        Counts = await data.Query<ReturnRequest>().GroupBy(r => r.Status).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count);
        if (Status is null && !Request.Query.ContainsKey("status"))
            Status = ReturnStatus.Pending;

        var query = Filtered();
        var total = await query.CountAsync();
        var pageNumber = ClampPage(PageNumber, total, 30);
        var rows = await Project(query.OrderByDescending(r => r.CreatedAt).Skip((pageNumber - 1) * 30).Take(30)).ToListAsync();
        Items = new PagedList<AdminReturnRow>(rows, total, pageNumber, 30);
    }

    public async Task<IActionResult> OnGetExportAsync(string format = "xlsx")
    {
        var rows = await Project(Filtered().OrderByDescending(r => r.CreatedAt).Take(20_000)).ToListAsync();
        var table = new ExportTable("مرجوعی‌ها", "شماره درخواست", "تاریخ", "سفارش", "مشتری", "موبایل", "دلیل", "وضعیت",
            "تعداد کالا", "مبلغ درخواستی", "مبلغ تأییدشده", "مبلغ بازپرداخت", "کد رهگیری مرجوعی");
        foreach (var r in rows)
            table.Add(r.RequestNumber, r.CreatedAt, r.OrderNumber, r.CustomerName, r.Phone, r.ReasonTitle, StatusTitle(r.Status),
                r.ItemCount, r.RequestedAmount, r.ApprovedAmount, r.RefundedAmount, r.TrackingCode);
        return Exporter.File(table, format, "returns");
    }

    private IQueryable<ReturnRequest> Filtered()
    {
        var query = data.Query<ReturnRequest>();
        if (Status is { } s) query = query.Where(r => r.Status == s);
        if (!string.IsNullOrWhiteSpace(Search))
        {
            var term = SearchText.NormalizeDigits(Search.Trim());
            query = query.Where(r => r.RequestNumber.Contains(term) || r.Order.OrderNumber.Contains(term) || r.User.PhoneNumber.Contains(term));
        }
        return query;
    }

    private static IQueryable<AdminReturnRow> Project(IQueryable<ReturnRequest> query) =>
        query.Select(r => new AdminReturnRow
            {
                Id = r.Id, RequestNumber = r.RequestNumber, OrderId = r.OrderId, OrderNumber = r.Order.OrderNumber, UserId = r.UserId,
                CustomerName = ((r.User.FirstName ?? "") + " " + (r.User.LastName ?? "")).Trim(), Phone = r.User.PhoneNumber,
                ReasonTitle = r.ReasonTitle, Status = r.Status, RequestedAmount = r.RequestedAmount, ApprovedAmount = r.ApprovedAmount,
                RefundedAmount = r.RefundedAmount, ItemCount = r.Items.Sum(i => i.Quantity), ImageCount = r.Images.Count,
                TrackingCode = r.ReturnTrackingCode, CreatedAt = r.CreatedAt,
            });

    public static string StatusTitle(ReturnStatus status) => status switch
    {
        ReturnStatus.Pending => "در انتظار بررسی",
        ReturnStatus.Approved => "تأییدشده – منتظر ارسال کالا",
        ReturnStatus.Rejected => "ردشده",
        ReturnStatus.Received => "کالا دریافت شد",
        ReturnStatus.Refunded => "بازپرداخت شد",
        ReturnStatus.Cancelled => "لغو مشتری",
        _ => "—",
    };

    public static string StatusBadge(ReturnStatus status) => status switch
    {
        ReturnStatus.Pending => "a-badge-yellow",
        ReturnStatus.Approved or ReturnStatus.Received => "a-badge-blue",
        ReturnStatus.Refunded => "a-badge-green",
        ReturnStatus.Rejected => "a-badge-red",
        _ => "a-badge-gray",
    };
}
