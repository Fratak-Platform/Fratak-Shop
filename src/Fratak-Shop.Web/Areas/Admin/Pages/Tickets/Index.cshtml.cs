using Fratak_Shop.Core.DTOs.Admin;
using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Entities.Support;
using Fratak_Shop.Web.Infrastructure.Admin;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Web.Areas.Admin.Pages.Tickets;

public sealed class AdminTicketRow
{
    public Guid Id { get; init; }
    public string TicketNumber { get; init; } = "";
    public string Subject { get; init; } = "";
    public string Department { get; init; } = "";
    public TicketStatus Status { get; init; }
    public TicketPriority Priority { get; init; }
    public Guid UserId { get; init; }
    public string CustomerName { get; init; } = "";
    public string Phone { get; init; } = "";
    public string? OrderNumber { get; init; }
    public Guid? AssignedToUserId { get; init; }
    public string? AssignedTo { get; init; }
    public int MessageCount { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset LastActivityAt { get; init; }
}

[AdminPermission(AdminPermissions.TicketsManage)]
public class IndexModel(IAdminDataService data) : AdminPageModel
{
    [BindProperty(SupportsGet = true, Name = "state")] public string? State { get; set; }
    [BindProperty(SupportsGet = true, Name = "q")] public string? Search { get; set; }
    [BindProperty(SupportsGet = true, Name = "department")] public Guid? DepartmentId { get; set; }
    [BindProperty(SupportsGet = true, Name = "priority")] public TicketPriority? Priority { get; set; }
    [BindProperty(SupportsGet = true, Name = "assigned")] public string? Assigned { get; set; }
    [FromQuery(Name = "page")] public int PageNumber { get; set; } = 1;

    public PagedList<AdminTicketRow> Items { get; private set; } = new([], 0, 1, 30);
    public List<SupportDepartment> Departments { get; private set; } = [];
    public int WaitingCount { get; private set; }
    public int MineCount { get; private set; }

    public async Task OnGetAsync()
    {
        State ??= "waiting";
        Departments = await data.Query<SupportDepartment>().OrderBy(d => d.DisplayOrder).ToListAsync();
        var all = data.Query<SupportTicket>();
        WaitingCount = await all.CountAsync(t => t.Status == TicketStatus.Open || t.Status == TicketStatus.CustomerReplied);
        var me = ActorId;
        MineCount = await all.CountAsync(t => t.AssignedToUserId == me && t.Status != TicketStatus.Closed);

        var query = State switch
        {
            "waiting" => all.Where(t => t.Status == TicketStatus.Open || t.Status == TicketStatus.CustomerReplied),
            "progress" => all.Where(t => t.Status == TicketStatus.InProgress),
            "answered" => all.Where(t => t.Status == TicketStatus.Answered),
            "closed" => all.Where(t => t.Status == TicketStatus.Closed),
            _ => all,
        };
        if (DepartmentId is { } dep) query = query.Where(t => t.DepartmentId == dep);
        if (Priority is { } pr) query = query.Where(t => t.Priority == pr);
        if (Assigned == "me") query = query.Where(t => t.AssignedToUserId == me);
        else if (Assigned == "none") query = query.Where(t => t.AssignedToUserId == null);
        if (!string.IsNullOrWhiteSpace(Search))
        {
            var term = SearchText.NormalizeDigits(Search.Trim());
            query = query.Where(t => t.TicketNumber.Contains(term) || t.Subject.Contains(term) || t.User.PhoneNumber.Contains(term)
                                     || (t.Order != null && t.Order.OrderNumber.Contains(term)));
        }

        var total = await query.CountAsync();
        var pageNumber = ClampPage(PageNumber, total, 30);
        var users = data.Query<Data.Entities.Users.User>();
        var rows = await query
            .OrderByDescending(t => t.Priority).ThenBy(t => t.Status == TicketStatus.Closed).ThenByDescending(t => t.LastActivityAt)
            .Skip((pageNumber - 1) * 30).Take(30)
            .Select(t => new AdminTicketRow
            {
                Id = t.Id,
                TicketNumber = t.TicketNumber,
                Subject = t.Subject,
                Department = t.Department.Title,
                Status = t.Status,
                Priority = t.Priority,
                UserId = t.UserId,
                CustomerName = ((t.User.FirstName ?? "") + " " + (t.User.LastName ?? "")).Trim(),
                Phone = t.User.PhoneNumber,
                OrderNumber = t.Order != null ? t.Order.OrderNumber : null,
                AssignedToUserId = t.AssignedToUserId,
                AssignedTo = users.Where(u => u.Id == t.AssignedToUserId).Select(u => ((u.FirstName ?? "") + " " + (u.LastName ?? "")).Trim()).FirstOrDefault(),
                MessageCount = t.Messages.Count,
                CreatedAt = t.CreatedAt,
                LastActivityAt = t.LastActivityAt,
            })
            .ToListAsync();
        Items = new PagedList<AdminTicketRow>(rows, total, pageNumber, 30);
    }

    public static string StatusTitle(TicketStatus status) => status switch
    {
        TicketStatus.Open => "باز – منتظر پاسخ",
        TicketStatus.Answered => "پاسخ داده شد",
        TicketStatus.CustomerReplied => "پاسخ مشتری",
        TicketStatus.InProgress => "در حال بررسی",
        TicketStatus.Closed => "بسته",
        _ => "—",
    };

    public static string StatusBadge(TicketStatus status) => status switch
    {
        TicketStatus.Open or TicketStatus.CustomerReplied => "a-badge-red",
        TicketStatus.InProgress => "a-badge-yellow",
        TicketStatus.Answered => "a-badge-green",
        _ => "a-badge-gray",
    };

    public static string PriorityTitle(TicketPriority priority) => priority switch
    {
        TicketPriority.High => "فوری",
        TicketPriority.Low => "کم",
        _ => "عادی",
    };
}
