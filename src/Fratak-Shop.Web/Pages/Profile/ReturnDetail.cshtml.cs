using System.ComponentModel.DataAnnotations;
using Fratak_Shop.Core.DTOs.Engagement;
using Fratak_Shop.Core.Services.Orders;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Web.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Pages.Profile;

/// <summary>One return request: status timeline, items, photos, cancel (pending) and the parcel tracking code (approved).</summary>
public class ReturnDetailModel(IReturnService returns, ISiteContext site) : ShopPageModel
{
    public ReturnRequestDto ReturnRequest { get; private set; } = null!;

    [BindProperty]
    [StringLength(50)]
    public string? TrackingCode { get; set; }

    public async Task<IActionResult> OnGetAsync(string requestNumber)
    {
        if (!site.Settings.IsReturnEnabled)
            return NotFound();

        var request = await returns.GetUserRequestAsync(CurrentUserId!.Value, requestNumber);
        if (request is null)
            return NotFound();

        ReturnRequest = request;
        TrackingCode = request.ReturnTrackingCode;
        ViewData[PageMetaKeys.Title] = $"مرجوعی {request.RequestNumber}";
        ViewData[PageMetaKeys.Robots] = "noindex, nofollow";
        return Page();
    }

    public async Task<IActionResult> OnPostCancelAsync(string requestNumber)
    {
        var result = await returns.CancelAsync(CurrentUserId!.Value, requestNumber);
        Report(result, "درخواست مرجوعی لغو شد.");
        return Redirect("/profile/returns/" + requestNumber.ToLowerInvariant());
    }

    public async Task<IActionResult> OnPostTrackingAsync(string requestNumber)
    {
        var result = await returns.SetTrackingCodeAsync(CurrentUserId!.Value, requestNumber, TrackingCode);
        Report(result, "کد رهگیری مرسوله ثبت شد.");
        return Redirect("/profile/returns/" + requestNumber.ToLowerInvariant());
    }

    private void Report(OperationResult result, string success)
    {
        if (result.IsSuccess())
            SuccessMessage = result.Message ?? success;
        else
            ErrorMessage = result.Message ?? "انجام نشد.";
    }
}
