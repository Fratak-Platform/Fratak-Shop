using Fratak_Shop.Core.DTOs.Engagement;
using Fratak_Shop.Core.Services.Support;
using Fratak_Shop.Data.Entities.Support;
using Fratak_Shop.Web.Infrastructure;
using Fratak_Shop.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Pages.Profile;

/// <summary>Support tickets of the customer.</summary>
public class TicketsModel(ITicketService tickets, ISiteContext site) : ShopPageModel
{
    public List<TicketSummaryDto> Items { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync()
    {
        if (!site.Settings.IsTicketEnabled)
            return NotFound();

        Items = await tickets.GetUserTicketsAsync(CurrentUserId!.Value);
        ViewData[PageMetaKeys.Title] = "تیکت‌های پشتیبانی";
        ViewData[PageMetaKeys.Robots] = "noindex, nofollow";
        return Page();
    }

    public static (string Text, string Css) StatusOf(TicketStatus status) => status switch
    {
        TicketStatus.Open => ("در انتظار پاسخ", "bg-amber-50 text-amber-600"),
        TicketStatus.Answered => ("پاسخ داده شد", "bg-emerald-50 text-emerald-600"),
        TicketStatus.CustomerReplied => ("پاسخ شما – در انتظار پشتیبانی", "bg-amber-50 text-amber-600"),
        TicketStatus.InProgress => ("در حال بررسی", "bg-sky-50 text-sky-600"),
        TicketStatus.Closed => ("بسته شده", "bg-zinc-100 text-zinc-500"),
        _ => ("-", "bg-zinc-100 text-zinc-500"),
    };

    public static string PriorityText(TicketPriority priority) => priority switch
    {
        TicketPriority.Low => "کم",
        TicketPriority.High => "زیاد",
        _ => "معمولی",
    };
}
