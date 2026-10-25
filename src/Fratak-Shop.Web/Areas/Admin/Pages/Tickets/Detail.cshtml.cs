using Fratak_Shop.Core.DTOs.Admin;
using Fratak_Shop.Core.DTOs.Engagement;
using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Core.Services.Support;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Entities.Support;
using Fratak_Shop.Web.Infrastructure;
using Fratak_Shop.Web.Infrastructure.Admin;
using Fratak_Shop.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Web.Areas.Admin.Pages.Tickets;

public sealed record AdminTicketMessage(Guid Id, bool IsStaff, string AuthorName, string? StaffName, string Content, DateTimeOffset CreatedAt, List<SupportTicketAttachment> Attachments);

[AdminPermission(AdminPermissions.TicketsManage)]
[RequestFormLimits(MultipartBodyLengthLimit = 16 * 1024 * 1024)]
[RequestSizeLimit(16 * 1024 * 1024)]
public class DetailModel(IAdminDataService data, ITicketService tickets, IUploadStore uploads, ISiteContext site, IAdminAccessService access) : AdminPageModel
{
    public SupportTicket Ticket { get; private set; } = null!;
    public string DepartmentTitle { get; private set; } = "";
    public string CustomerName { get; private set; } = "";
    public string CustomerPhone { get; private set; } = "";
    public string? OrderNumber { get; private set; }
    public int CustomerTicketCount { get; private set; }
    public List<AdminTicketMessage> Messages { get; private set; } = [];
    public List<StaffMemberDto> Staff { get; private set; } = [];
    public List<SupportDepartment> Departments { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(Guid id)
    {
        var ticket = await data.Query<SupportTicket>().Include(t => t.Department).Include(t => t.User).Include(t => t.Order)
            .FirstOrDefaultAsync(t => t.Id == id);
        if (ticket is null)
            return NotFound();
        Ticket = ticket;
        DepartmentTitle = ticket.Department.Title;
        CustomerName = $"{ticket.User.FirstName} {ticket.User.LastName}".Trim();
        CustomerPhone = ticket.User.PhoneNumber;
        OrderNumber = ticket.Order?.OrderNumber;
        CustomerTicketCount = await data.Query<SupportTicket>().CountAsync(t => t.UserId == ticket.UserId);

        var users = data.Query<Data.Entities.Users.User>();
        Messages = await data.Query<SupportTicketMessage>().Where(m => m.TicketId == id).OrderBy(m => m.CreatedAt)
            .Select(m => new AdminTicketMessage(m.Id, m.IsStaff, m.AuthorName,
                m.IsStaff ? users.Where(u => u.Id == m.UserId).Select(u => ((u.FirstName ?? "") + " " + (u.LastName ?? "")).Trim()).FirstOrDefault() : null,
                m.Content, m.CreatedAt, m.Attachments.ToList()))
            .ToListAsync();
        Staff = await access.GetStaffAsync();
        Departments = await data.Query<SupportDepartment>().Where(d => d.IsActive || d.Id == ticket.DepartmentId).OrderBy(d => d.DisplayOrder).ToListAsync();

        // Staff opened it: the customer's message is now being looked at.
        if (ticket.Status == TicketStatus.Open && ticket.AssignedToUserId is null)
        {
            var tracked = await data.FindAsync<SupportTicket>(id);
            if (tracked is not null)
            {
                tracked.AssignedToUserId = ActorId;
                await data.UpdateAsync(tracked);
                Ticket.AssignedToUserId = ActorId;
            }
        }
        return Page();
    }

    public async Task<IActionResult> OnPostReplyAsync(Guid id, string? content, List<IFormFile> attachments, bool close = false)
    {
        var settings = site.Settings;
        var (files, error) = await uploads.SaveAsync(UploadArea.Tickets, attachments, Math.Max(settings.TicketMaxAttachments, 3), Math.Max(settings.TicketMaxAttachmentSizeKb, 2048), HttpContext.RequestAborted);
        if (error is not null)
        {
            ErrorMessage = error;
            return Redirect($"/admin/tickets/{id}#reply");
        }

        var result = await tickets.StaffReplyAsync(ActorId, id, content ?? "", files.Select(f => new TicketAttachmentInput(f.FileName, f.ContentType, f.SizeBytes)).ToList());
        if (!Succeeded(result))
        {
            uploads.Delete(UploadArea.Tickets, files.Select(f => f.FileName));
            ShowResult(result);
            return Redirect($"/admin/tickets/{id}#reply");
        }

        if (close)
            await tickets.SetStatusAsync(id, TicketStatus.Closed);
        SuccessMessage = close ? "پاسخ ثبت و تیکت بسته شد." : result.Message;
        return Redirect($"/admin/tickets/{id}");
    }

    public async Task<IActionResult> OnPostStatusAsync(Guid id, TicketStatus status)
    {
        ShowResult(await tickets.SetStatusAsync(id, status));
        return Redirect($"/admin/tickets/{id}");
    }

    public async Task<IActionResult> OnPostMetaAsync(Guid id, TicketPriority priority, Guid? assignedTo, Guid departmentId)
    {
        var ticket = await data.FindAsync<SupportTicket>(id);
        if (ticket is null)
            return NotFound();
        if (!Enum.IsDefined(priority) || !await data.Query<SupportDepartment>().AnyAsync(d => d.Id == departmentId))
        {
            ErrorMessage = "مقادیر معتبر نیست.";
            return Redirect($"/admin/tickets/{id}");
        }
        ticket.Priority = priority;
        ticket.AssignedToUserId = assignedTo;
        ticket.DepartmentId = departmentId;
        ShowResult(await data.UpdateAsync(ticket));
        return Redirect($"/admin/tickets/{id}");
    }

    public async Task<IActionResult> OnGetAttachmentAsync(Guid id, string name)
    {
        var attachment = await data.Query<SupportTicketAttachment>()
            .FirstOrDefaultAsync(a => a.FileName == name && a.Message.TicketId == id);
        var path = attachment is null ? null : uploads.GetPath(UploadArea.Tickets, attachment.FileName);
        if (attachment is null || path is null)
            return NotFound();
        Response.Headers.CacheControl = "private, no-store";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return PhysicalFile(path, attachment.ContentType, attachment.FileName);
    }
}
