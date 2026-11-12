using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Core.Services.Assistant;
using Fratak_Shop.Data.Entities.Assistant;
using Fratak_Shop.Web.Infrastructure.Admin;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Areas.Admin.Pages.SmartContent;

/// <summary>
/// دور ۲۴ – «گفت‌وگو با کارشناس». آدرس: /admin/smart/assistant/handoffs (فهرست) و ‎?id={شناسه}‎ (یک گفت‌وگو).
/// <para>وقتی مشتری در چت «با کارشناس حرف بزنم» می‌نویسد، گفت‌وگو این‌جا می‌آید و به کارشناسان پشتیبانی اعلان (زنگوله) می‌رود.
/// کارشناس جواب می‌نویسد و جواب در همان پنجرهٔ چت مشتری (بدون بارگذاری دوباره) نشان داده می‌شود. گفت‌وگوی بی‌پیام بعد از ۲۴ ساعت خودکار بسته می‌شود.</para>
/// <para>دسترسی: پشتیبانی (تیکت یا پیام‌ها). موبایل و کارت در متن پیام‌ها پیش از ذخیره پوشانده شده است.</para>
/// </summary>
[AdminPermission(AdminPermissions.TicketsManage, AdminPermissions.MessagesManage)]
public class HandoffsModel(IAssistantHandoffService handoffs) : AdminPageModel
{
    /// <summary>فهرست گفت‌وگوها (باز اول).</summary>
    public IReadOnlyList<HandoffSummaryDto> Items { get; private set; } = [];

    /// <summary>گفت‌وگوی انتخاب‌شده (با ‎?id=‎) یا null.</summary>
    public HandoffSummaryDto? Current { get; private set; }

    /// <summary>پیام‌های گفت‌وگوی انتخاب‌شده (قدیمی به جدید).</summary>
    public IReadOnlyList<HandoffMessageDto> Messages { get; private set; } = [];

    /// <summary>بسته‌ها هم نشان داده شوند؟ (‎?all=true‎)</summary>
    public bool ShowClosed { get; private set; }

    public async Task<IActionResult> OnGetAsync(Guid? id, bool all = false)
    {
        ShowClosed = all;
        Items = await handoffs.ListAsync(all, HttpContext.RequestAborted);
        if (id is { } value)
        {
            var found = await handoffs.GetAsync(value, HttpContext.RequestAborted);
            if (found is null)
                return NotFound();
            Current = found.Value.Summary;
            Messages = found.Value.Messages;
        }
        return Page();
    }

    /// <summary>جواب کارشناس (حداکثر ۱۰۰۰ نویسه). گفت‌وگوی «در انتظار» با اولین جواب «در حال گفت‌وگو» می‌شود.</summary>
    public async Task<IActionResult> OnPostReplyAsync(Guid id, string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            ErrorMessage = "متن جواب خالی است.";
        else if (!await handoffs.ReplyAsync(id, ActorId, ActorName, text, HttpContext.RequestAborted))
            ErrorMessage = "این گفت‌وگو بسته شده یا وجود ندارد.";
        return LocalRedirect($"/admin/smart/assistant/handoffs?id={id}#reply");
    }

    /// <summary>بستن گفت‌وگو (مشتری در چت می‌بیند که گفت‌وگو تمام شد و دستیار دوباره جواب می‌دهد).</summary>
    public async Task<IActionResult> OnPostCloseAsync(Guid id)
    {
        if (await handoffs.CloseAsync(id, HttpContext.RequestAborted))
            SuccessMessage = "گفت‌وگو بسته شد.";
        return LocalRedirect("/admin/smart/assistant/handoffs");
    }

    /// <summary>عنوان وضعیت. مثال: Waiting ← «در انتظار کارشناس».</summary>
    public static string StatusTitle(AssistantHandoffStatus status) => status switch
    {
        AssistantHandoffStatus.Waiting => "در انتظار کارشناس",
        AssistantHandoffStatus.Active => "در حال گفت‌وگو",
        AssistantHandoffStatus.Closed => "بسته",
        _ => "نامشخص",
    };

    /// <summary>رنگ نشان وضعیت.</summary>
    public static string StatusBadge(AssistantHandoffStatus status) => status switch
    {
        AssistantHandoffStatus.Waiting => "a-badge-red",
        AssistantHandoffStatus.Active => "a-badge-green",
        _ => "a-badge-gray",
    };
}
