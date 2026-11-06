using System.ComponentModel.DataAnnotations;
using Fratak_Shop.Core.DTOs.Engagement;
using Fratak_Shop.Core.Services.Support;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Web.Infrastructure;
using Fratak_Shop.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Pages.Profile;

/// <summary>Ticket conversation: messages, reply with attachments, close, and private attachment download.</summary>
[RequestFormLimits(MultipartBodyLengthLimit = 16 * 1024 * 1024)]
[RequestSizeLimit(16 * 1024 * 1024)]
public class TicketDetailModel(ITicketService tickets, IUploadStore uploads, ISiteContext site) : ShopPageModel
{
    public TicketDetailDto Detail { get; private set; } = null!;

    [BindProperty]
    [StringLength(4000)]
    public string? Reply { get; set; }

    [BindProperty]
    public List<IFormFile> Attachments { get; set; } = [];

    public async Task<IActionResult> OnGetAsync(string ticketNumber)
    {
        if (!site.Settings.IsTicketEnabled)
            return NotFound();

        var detail = await tickets.GetUserTicketAsync(CurrentUserId!.Value, ticketNumber);
        if (detail is null)
            return NotFound();

        Detail = detail;
        ViewData[PageMetaKeys.Title] = $"تیکت {detail.Ticket.TicketNumber}";
        ViewData[PageMetaKeys.Robots] = "noindex, nofollow";
        return Page();
    }

    public async Task<IActionResult> OnPostReplyAsync(string ticketNumber)
    {
        if (!site.Settings.IsTicketEnabled)
            return NotFound();

        var settings = site.Settings;
        var (files, error) = await uploads.SaveAsync(UploadArea.Tickets, Attachments, settings.TicketMaxAttachments, settings.TicketMaxAttachmentSizeKb, HttpContext.RequestAborted);
        if (error is not null)
        {
            ErrorMessage = error;
            return Back(ticketNumber);
        }

        var result = await tickets.ReplyAsync(CurrentUserId!.Value, ticketNumber, Reply,
            files.Select(f => new TicketAttachmentInput(f.FileName, f.ContentType, f.SizeBytes)).ToList());
        if (!result.IsSuccess())
            uploads.Delete(UploadArea.Tickets, files.Select(f => f.FileName));

        ShowResult(result);
        return Back(ticketNumber);
    }

    public async Task<IActionResult> OnPostCloseAsync(string ticketNumber)
    {
        ShowResult(await tickets.CloseAsync(CurrentUserId!.Value, ticketNumber));
        return Back(ticketNumber);
    }

    /// <summary>Attachments are private files: only the ticket owner can download them, always as a download.</summary>
    public async Task<IActionResult> OnGetAttachmentAsync(string ticketNumber, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return NotFound();

        var attachment = await tickets.GetUserAttachmentAsync(CurrentUserId!.Value, name);
        var path = attachment is null ? null : uploads.GetPath(UploadArea.Tickets, attachment.FileName);
        if (attachment is null || path is null)
            return NotFound();

        Response.Headers.CacheControl = "private, no-store";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return PhysicalFile(path, attachment.ContentType, attachment.FileName);
    }

    private RedirectResult Back(string ticketNumber) => Redirect("/profile/tickets/" + ticketNumber.ToLowerInvariant() + "#reply");
}
