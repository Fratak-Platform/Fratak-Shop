using System.ComponentModel.DataAnnotations;
using Fratak_Shop.Core.DTOs.Engagement;
using Fratak_Shop.Core.Services.Orders;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Web.Infrastructure;
using Fratak_Shop.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Pages.Profile;

/// <summary>New return request for a delivered order: items/quantities, reason, description, photos.</summary>
[RequestFormLimits(MultipartBodyLengthLimit = 16 * 1024 * 1024)]
[RequestSizeLimit(16 * 1024 * 1024)]
public class ReturnNewModel(IReturnService returns, IUploadStore uploads, ISiteContext site) : ShopPageModel
{
    public ReturnEligibilityDto Eligibility { get; private set; } = new();
    public List<ReturnReasonDto> Reasons { get; private set; } = [];

    [BindProperty]
    public Guid ReasonId { get; set; }

    [BindProperty]
    [StringLength(1000)]
    public string? Description { get; set; }

    [BindProperty]
    public Dictionary<Guid, int> Quantities { get; set; } = [];

    [BindProperty]
    public List<IFormFile> Images { get; set; } = [];

    public async Task<IActionResult> OnGetAsync(string orderNumber)
    {
        if (!site.Settings.IsReturnEnabled)
            return NotFound();

        return await LoadAsync(orderNumber) ? Page() : NotFound();
    }

    public async Task<IActionResult> OnPostAsync(string orderNumber)
    {
        if (!site.Settings.IsReturnEnabled)
            return NotFound();

        if (!await LoadAsync(orderNumber))
            return NotFound();

        if (!Eligibility.IsEligible)
            return Page();

        if (!ModelState.IsValid)
            return Page();

        var selected = Quantities.Where(q => q.Value > 0).ToDictionary(q => q.Key, q => q.Value);
        if (selected.Count == 0)
        {
            ModelState.AddModelError(string.Empty, "حداقل یک کالا و تعداد آن را برای مرجوعی انتخاب کنید.");
            return Page();
        }

        var (files, error) = await uploads.SaveAsync(UploadArea.Returns, Images, site.Settings.ReturnMaxImages, 4096, HttpContext.RequestAborted);
        if (error is not null)
        {
            ModelState.AddModelError(string.Empty, error);
            return Page();
        }

        var result = await returns.CreateAsync(new CreateReturnCommand
        {
            UserId = CurrentUserId!.Value,
            OrderNumber = Eligibility.OrderNumber,
            ReasonId = ReasonId,
            Description = Description,
            Quantities = selected,
            Images = files.Select(f => (f.FileName, f.ContentType, f.SizeBytes)).ToList(),
        });

        if (!result.IsSuccess() || result.Data is null)
        {
            uploads.Delete(UploadArea.Returns, files.Select(f => f.FileName));
            ModelState.AddModelError(string.Empty, result.Message ?? "ثبت درخواست مرجوعی انجام نشد.");
            return Page();
        }

        SuccessMessage = result.Message ?? "درخواست مرجوعی ثبت شد؛ نتیجه بررسی را از همین بخش و اعلان‌ها پیگیری کنید.";
        return Redirect("/profile/returns/" + result.Data.ToLowerInvariant());
    }

    private async Task<bool> LoadAsync(string orderNumber)
    {
        Eligibility = await returns.GetEligibilityAsync(CurrentUserId!.Value, orderNumber);
        if (Eligibility.OrderId == Guid.Empty)
            return false;

        Reasons = await returns.GetReasonsAsync();
        ViewData[PageMetaKeys.Title] = $"درخواست مرجوعی سفارش {Eligibility.OrderNumber}";
        ViewData[PageMetaKeys.Robots] = "noindex, nofollow";
        return true;
    }
}
