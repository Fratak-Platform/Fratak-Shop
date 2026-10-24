using System.ComponentModel.DataAnnotations;
using Fratak_Shop.Core.DTOs.Orders.Query;
using Fratak_Shop.Core.Services.Orders;
using Fratak_Shop.Web.Infrastructure;
using Fratak_Shop.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Fratak_Shop.Web.Pages;

/// <summary>
/// Order tracking without signing in (order number + the mobile number of the order).
/// Only the status, items and amounts are shown – no address details. POST only, so
/// the mobile number never ends up in URLs or logs; rate limited like the login.
/// </summary>
public class TrackOrderModel(IOrderService orderService, ISiteContext site) : ShopPageModel
{
    [BindProperty]
    public TrackInput Input { get; set; } = new();

    public OrderDto? Order { get; private set; }

    public bool Searched { get; private set; }

    public IActionResult OnGet()
    {
        if (!site.Settings.IsGuestOrderTrackingEnabled)
            return NotFound();

        ViewData[PageMetaKeys.Title] = "پیگیری سفارش";
        ViewData[PageMetaKeys.Description] = "پیگیری وضعیت سفارش با شماره سفارش و شماره موبایل، بدون نیاز به ورود.";
        PrepareCaptcha(Infrastructure.Security.CaptchaActions.TrackOrder);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (OnGet() is not PageResult)
            return NotFound();

        if (!VerifyCaptcha(Infrastructure.Security.CaptchaActions.TrackOrder) || !ModelState.IsValid)
            return Page();

        Searched = true;
        Order = await orderService.TrackOrderAsync(Input.OrderNumber, Input.PhoneNumber);
        if (Order is null)
            CaptchaFailed(Infrastructure.Security.CaptchaActions.TrackOrder); // order-number guessing guard

        // Signed-in owners get the full order page.
        if (Order is not null && Order.UserId.HasValue && Order.UserId == CurrentUserId)
            return Redirect($"/profile/orders/{Order.OrderNumber.ToLowerInvariant()}");

        return Page();
    }

    public class TrackInput
    {
        [Required(ErrorMessage = "شماره سفارش را وارد کنید.")]
        [StringLength(30, ErrorMessage = "شماره سفارش معتبر نیست.")]
        public string OrderNumber { get; set; } = string.Empty;

        [Required(ErrorMessage = "شماره موبایل را وارد کنید.")]
        [RegularExpression(@"^09[0-9]{9}$", ErrorMessage = "شماره موبایل باید با ۰۹ شروع شود و ۱۱ رقم باشد.")]
        public string PhoneNumber { get; set; } = string.Empty;
    }
}
