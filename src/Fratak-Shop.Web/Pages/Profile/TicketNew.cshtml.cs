using System.ComponentModel.DataAnnotations;
using Fratak_Shop.Core.DTOs.Engagement;
using Fratak_Shop.Core.Services.Support;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Entities.Support;
using Fratak_Shop.Web.Infrastructure;
using Fratak_Shop.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Pages.Profile;

/// <summary>New support ticket (department, optional order, priority, message and attachments).</summary>
[RequestFormLimits(MultipartBodyLengthLimit = 16 * 1024 * 1024)]
[RequestSizeLimit(16 * 1024 * 1024)]
public class TicketNewModel(ITicketService tickets, IUploadStore uploads, ISiteContext site) : ShopPageModel
{
    public List<SupportDepartmentDto> Departments { get; private set; } = [];

    [BindProperty]
    public TicketInput Input { get; set; } = new();

    [BindProperty]
    public List<IFormFile> Attachments { get; set; } = [];

    public async Task<IActionResult> OnGetAsync([FromQuery(Name = "order")] string? orderNumber)
    {
        if (!site.Settings.IsTicketEnabled)
            return NotFound();

        Input.OrderNumber = orderNumber?.Trim().ToUpperInvariant();
        await LoadAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!site.Settings.IsTicketEnabled)
            return NotFound();

        if (!ModelState.IsValid)
        {
            await LoadAsync();
            return Page();
        }

        var settings = site.Settings;
        var (files, error) = await uploads.SaveAsync(UploadArea.Tickets, Attachments, settings.TicketMaxAttachments, settings.TicketMaxAttachmentSizeKb, HttpContext.RequestAborted);
        if (error is not null)
        {
            ModelState.AddModelError(string.Empty, error);
            await LoadAsync();
            return Page();
        }

        var result = await tickets.CreateAsync(new CreateTicketCommand
        {
            UserId = CurrentUserId!.Value,
            DepartmentId = Input.DepartmentId,
            OrderNumber = Input.OrderNumber,
            Subject = Input.Subject,
            Content = Input.Content,
            Priority = Input.Priority,
            Attachments = files.Select(f => new TicketAttachmentInput(f.FileName, f.ContentType, f.SizeBytes)).ToList(),
        });

        if (!result.IsSuccess() || result.Data is null)
        {
            uploads.Delete(UploadArea.Tickets, files.Select(f => f.FileName));
            ModelState.AddModelError(string.Empty, result.Message ?? "ثبت تیکت انجام نشد.");
            await LoadAsync();
            return Page();
        }

        SuccessMessage = result.Message ?? "تیکت شما ثبت شد؛ پاسخ پشتیبانی از طریق اعلان به شما خبر داده می‌شود.";
        return Redirect("/profile/tickets/" + result.Data.ToLowerInvariant());
    }

    private async Task LoadAsync()
    {
        Departments = await tickets.GetDepartmentsAsync();
        ViewData[PageMetaKeys.Title] = "تیکت جدید";
        ViewData[PageMetaKeys.Robots] = "noindex, nofollow";
    }

    public class TicketInput
    {
        [Display(Name = "بخش")]
        [Required(ErrorMessage = "بخش مربوط را انتخاب کنید.")]
        public Guid DepartmentId { get; set; }

        [Display(Name = "شماره سفارش")]
        [StringLength(30)]
        public string? OrderNumber { get; set; }

        [Display(Name = "موضوع")]
        [Required(ErrorMessage = "موضوع را وارد کنید.")]
        [StringLength(150, MinimumLength = 3, ErrorMessage = "موضوع باید بین ۳ تا ۱۵۰ کاراکتر باشد.")]
        public string Subject { get; set; } = string.Empty;

        [Display(Name = "متن پیام")]
        [Required(ErrorMessage = "متن پیام را وارد کنید.")]
        [StringLength(4000, MinimumLength = 10, ErrorMessage = "متن پیام باید بین ۱۰ تا ۴۰۰۰ کاراکتر باشد.")]
        public string Content { get; set; } = string.Empty;

        public TicketPriority Priority { get; set; } = TicketPriority.Normal;
    }
}
