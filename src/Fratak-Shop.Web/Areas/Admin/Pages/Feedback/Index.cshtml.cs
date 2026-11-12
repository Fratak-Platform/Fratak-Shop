using Fratak_Shop.Core.DTOs.Admin;
using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Data.Entities.Orders;
using Fratak_Shop.Web.Helpers;
using Fratak_Shop.Web.Infrastructure.Admin;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Web.Areas.Admin.Pages.Feedback;

public sealed class AdminFeedbackRow
{
    public Guid Id { get; init; }
    public Guid OrderId { get; init; }
    public string OrderNumber { get; init; } = "";
    public Guid UserId { get; init; }
    public string CustomerName { get; init; } = "";
    public string Phone { get; init; } = "";
    public byte Overall { get; init; }
    public byte Delivery { get; init; }
    public byte Packaging { get; init; }
    public byte ProductMatch { get; init; }
    public bool? WouldRecommend { get; init; }
    public string? Comment { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}

public sealed class FeedbackSummary
{
    public int Count { get; init; }
    public double Overall { get; init; }
    public double Delivery { get; init; }
    public double Packaging { get; init; }
    public double ProductMatch { get; init; }
    public int Recommend { get; init; }
    public int NotRecommend { get; init; }
    public int[] Distribution { get; init; } = new int[5];
}

[AdminPermission(AdminPermissions.OrdersView, AdminPermissions.TicketsManage)]
public class IndexModel(IAdminDataService data) : AdminPageModel
{
    [BindProperty(SupportsGet = true, Name = "period")] public string? Preset { get; set; }
    [BindProperty(SupportsGet = true, Name = "from")] public string? From { get; set; }
    [BindProperty(SupportsGet = true, Name = "to")] public string? To { get; set; }
    [BindProperty(SupportsGet = true, Name = "rating")] public string? Rating { get; set; }
    [BindProperty(SupportsGet = true, Name = "comment")] public bool OnlyWithComment { get; set; }
    [FromQuery(Name = "page")] public int PageNumber { get; set; } = 1;

    public ResolvedPeriod Period { get; private set; } = null!;
    public FeedbackSummary Summary { get; private set; } = new();
    public PagedList<AdminFeedbackRow> Items { get; private set; } = new([], 0, 1, 30);

    public async Task OnGetAsync()
    {
        Period = AdminPeriods.Resolve(Preset, From, To, "90d");
        var inPeriod = InPeriod();
        var stats = await inPeriod.GroupBy(_ => 1).Select(g => new
        {
            Count = g.Count(),
            Overall = g.Average(f => (double)f.OverallRating),
            Delivery = g.Average(f => (double)f.DeliveryRating),
            Packaging = g.Average(f => (double)f.PackagingRating),
            ProductMatch = g.Average(f => (double)f.ProductMatchRating),
            Recommend = g.Count(f => f.WouldRecommend == true),
            NotRecommend = g.Count(f => f.WouldRecommend == false),
        }).FirstOrDefaultAsync();
        var distribution = await inPeriod.GroupBy(f => f.OverallRating).Select(g => new { g.Key, Count = g.Count() }).ToListAsync();
        var dist = new int[5];
        foreach (var d in distribution.Where(d => d.Key is >= 1 and <= 5))
            dist[d.Key - 1] = d.Count;
        if (stats is not null)
        {
            Summary = new FeedbackSummary
            {
                Count = stats.Count, Overall = stats.Overall, Delivery = stats.Delivery, Packaging = stats.Packaging,
                ProductMatch = stats.ProductMatch, Recommend = stats.Recommend, NotRecommend = stats.NotRecommend, Distribution = dist,
            };
        }

        var query = Filtered(inPeriod);
        var total = await query.CountAsync();
        var pageNumber = ClampPage(PageNumber, total, 30);
        var rows = await Project(query.OrderByDescending(f => f.CreatedAt).Skip((pageNumber - 1) * 30).Take(30)).ToListAsync();
        Items = new PagedList<AdminFeedbackRow>(rows, total, pageNumber, 30);
    }

    public async Task<IActionResult> OnGetExportAsync(string format = "xlsx")
    {
        Period = AdminPeriods.Resolve(Preset, From, To, "90d");
        var rows = await Project(Filtered(InPeriod()).OrderByDescending(f => f.CreatedAt).Take(50000)).ToListAsync();
        var table = new ExportTable("نظرسنجی خرید", "تاریخ", "سفارش", "مشتری", "موبایل", "کلی", "ارسال", "بسته‌بندی", "تطابق کالا", "پیشنهاد", "توضیح");
        foreach (var r in rows)
            table.Add(Format.DateTime(r.CreatedAt), r.OrderNumber, r.CustomerName, r.Phone, r.Overall.ToString(), r.Delivery.ToString(), r.Packaging.ToString(),
                r.ProductMatch.ToString(), r.WouldRecommend switch { true => "بله", false => "خیر", _ => "" }, r.Comment ?? "");
        return Exporter.File(table, format, "order-feedback");
    }

    private IQueryable<OrderFeedback> InPeriod()
    {
        var query = data.Query<OrderFeedback>();
        if (Period.Current.From is { } from) query = query.Where(f => f.CreatedAt >= from);
        if (Period.Current.To is { } to) query = query.Where(f => f.CreatedAt < to);
        return query;
    }

    private IQueryable<OrderFeedback> Filtered(IQueryable<OrderFeedback> query)
    {
        query = Rating switch
        {
            "low" => query.Where(f => f.OverallRating <= 2),
            "mid" => query.Where(f => f.OverallRating == 3),
            "high" => query.Where(f => f.OverallRating >= 4),
            _ => query,
        };
        if (OnlyWithComment)
            query = query.Where(f => f.Comment != null && f.Comment != "");
        return query;
    }

    private static IQueryable<AdminFeedbackRow> Project(IQueryable<OrderFeedback> query) => query.Select(f => new AdminFeedbackRow
    {
        Id = f.Id, OrderId = f.OrderId, OrderNumber = f.Order.OrderNumber, UserId = f.UserId,
        CustomerName = ((f.User.FirstName ?? "") + " " + (f.User.LastName ?? "")).Trim(), Phone = f.User.PhoneNumber,
        Overall = f.OverallRating, Delivery = f.DeliveryRating, Packaging = f.PackagingRating, ProductMatch = f.ProductMatchRating,
        WouldRecommend = f.WouldRecommend, Comment = f.Comment, CreatedAt = f.CreatedAt,
    });
}
