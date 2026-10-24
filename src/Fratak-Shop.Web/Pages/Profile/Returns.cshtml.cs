using Fratak_Shop.Core.DTOs.Engagement;
using Fratak_Shop.Core.Services.Orders;
using Fratak_Shop.Data.Entities.Orders;
using Fratak_Shop.Web.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Pages.Profile;

/// <summary>Return requests of the customer (new requests start from a delivered order).</summary>
public class ReturnsModel(IReturnService returns, ISiteContext site) : ShopPageModel
{
    public List<ReturnRequestDto> Items { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync()
    {
        if (!site.Settings.IsReturnEnabled)
            return NotFound();

        Items = await returns.GetUserRequestsAsync(CurrentUserId!.Value);
        ViewData[PageMetaKeys.Title] = "مرجوعی‌ها";
        ViewData[PageMetaKeys.Robots] = "noindex, nofollow";
        return Page();
    }

    public static (string Text, string Css) StatusOf(ReturnStatus status) => status switch
    {
        ReturnStatus.Pending => ("در انتظار بررسی", "bg-amber-50 text-amber-600"),
        ReturnStatus.Approved => ("تایید شده – ارسال کالا", "bg-sky-50 text-sky-600"),
        ReturnStatus.Rejected => ("رد شده", "bg-red-50 text-red-500"),
        ReturnStatus.Received => ("کالا دریافت شد", "bg-violet-50 text-violet-600"),
        ReturnStatus.Refunded => ("وجه برگشت داده شد", "bg-emerald-50 text-emerald-600"),
        ReturnStatus.Cancelled => ("لغو شده", "bg-zinc-100 text-zinc-500"),
        _ => ("-", "bg-zinc-100 text-zinc-500"),
    };
}
