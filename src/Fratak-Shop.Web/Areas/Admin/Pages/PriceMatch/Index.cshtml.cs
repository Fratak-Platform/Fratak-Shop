using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Core.Services.Marketing;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Entities.Marketing;
using Fratak_Shop.Web.Infrastructure.Admin;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Areas.Admin.Pages.PriceMatch;

/// <summary>
/// دور ۲۴ (پیشنهاد ۱۱) – «تضمین قیمت». آدرس: /admin/marketing/price-match (‎?status=Pending|Approved|Rejected‎ برای فیلتر).
/// <para>درخواست‌های «قیمت کمتر دیدید؟» مشتری‌ها. مدیر پیوند فروشگاه دیگر را باز می‌کند و تأیید یا رد می‌کند.
/// تأیید ← کد تخفیف شخصی ۷۲ ساعته فقط برای همان کالا به اندازهٔ اختلاف قیمت «امروز» + اعلان به مشتری؛ رد ← اعلان با دلیل.</para>
/// <para>دسترسی: بازاریابی. منطق: Core/Services/Marketing/PriceMatchService.cs.</para>
/// </summary>
[AdminPermission(AdminPermissions.MarketingManage)]
public class IndexModel(IPriceMatchService priceMatch) : AdminPageModel
{
    /// <summary>ردیف‌ها (در انتظار اول).</summary>
    public IReadOnlyList<PriceMatchRow> Items { get; private set; } = [];

    /// <summary>فیلتر وضعیت (خالی = همه).</summary>
    public PriceMatchStatus? Status { get; private set; }

    /// <summary>تعداد در انتظار.</summary>
    public int Pending { get; private set; }

    public async Task OnGetAsync(PriceMatchStatus? status)
    {
        Status = status is { } s && Enum.IsDefined(s) ? s : null;
        Items = await priceMatch.ListAsync(Status, 200, HttpContext.RequestAborted);
        Pending = await priceMatch.PendingCountAsync(HttpContext.RequestAborted);
    }

    /// <summary>تأیید و ساخت کد تخفیف.</summary>
    public async Task<IActionResult> OnPostApproveAsync(Guid id, string? note)
    {
        var result = await priceMatch.ApproveAsync(id, ActorName, note, HttpContext.RequestAborted);
        if (result.IsSuccess())
            SuccessMessage = result.Message;
        else
            ErrorMessage = result.Message;
        return LocalRedirect("/admin/marketing/price-match");
    }

    /// <summary>رد با دلیل.</summary>
    public async Task<IActionResult> OnPostRejectAsync(Guid id, string? note)
    {
        var result = await priceMatch.RejectAsync(id, ActorName, note, HttpContext.RequestAborted);
        if (result.IsSuccess())
            SuccessMessage = result.Message;
        else
            ErrorMessage = result.Message;
        return LocalRedirect("/admin/marketing/price-match");
    }

    /// <summary>عنوان وضعیت. مثال: Pending ← «در انتظار».</summary>
    public static string StatusTitle(PriceMatchStatus status) => status switch
    {
        PriceMatchStatus.Pending => "در انتظار",
        PriceMatchStatus.Approved => "تأییدشده",
        PriceMatchStatus.Rejected => "ردشده",
        _ => "نامشخص",
    };

    /// <summary>رنگ نشان وضعیت.</summary>
    public static string StatusBadge(PriceMatchStatus status) => status switch
    {
        PriceMatchStatus.Pending => "a-badge-red",
        PriceMatchStatus.Approved => "a-badge-green",
        _ => "a-badge-gray",
    };

    /// <summary>درصد اختلاف. مثال: (۱٬۰۰۰٬۰۰۰، ۹۵۰٬۰۰۰) ← ۵.</summary>
    public static decimal GapPercent(decimal ours, decimal theirs) => ours <= 0 ? 0 : Math.Round((ours - theirs) * 100m / ours, 1);
}
