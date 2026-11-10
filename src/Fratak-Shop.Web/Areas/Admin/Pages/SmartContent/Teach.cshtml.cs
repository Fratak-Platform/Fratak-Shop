using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Core.Services.Assistant;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Entities.Assistant;
using Fratak_Shop.Web.Infrastructure.Admin;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Areas.Admin.Pages.SmartContent;

/// <summary>
/// دور ۲۵ – «آموزش دستیار». آدرس: /admin/smart/assistant/teach
/// <list type="bullet">
/// <item><b>پیشنهادها:</b> پیام‌هایی که دستیار جواب نداد یا مشتری برایشان 👎 زد (۳۰ روز اخیر، پرتکرارها اول). دکمهٔ «آموزش» فرم را با همان پیام پر می‌کند.</item>
/// <item><b>فرم:</b> برای یک پرسش، جواب ثابت (متن + لینک داخلی اختیاری) یا یک «کار» (پیگیری سفارش، فاکتور، …) تعیین می‌شود.
/// از همان لحظه پیام‌های خیلی شبیه همین جواب را می‌گیرند.</item>
/// <item><b>فهرست:</b> آموزش‌ها با تعداد استفاده؛ ویرایش (‎?id=‎)، خاموش/روشن و حذف.</item>
/// </list>
/// GET هیچ تغییری نمی‌دهد. دسترسی: همان کسانی که پیام‌های مشتری را جواب می‌دهند یا کالا/بازاریابی را مدیریت می‌کنند.
/// </summary>
[AdminPermission(AdminPermissions.ProductsManage, AdminPermissions.MarketingManage, AdminPermissions.TicketsManage)]
public class TeachModel(IAssistantTeachingService teachings) : AdminPageModel
{
    /// <summary>پیشنهادهای آموزش (پیام‌های بی‌جواب یا 👎).</summary>
    public IReadOnlyList<AssistantTeachCandidate> Candidates { get; private set; } = [];

    /// <summary>آموزش‌های ثبت‌شده.</summary>
    public IReadOnlyList<AssistantTeachingRow> Items { get; private set; } = [];

    /// <summary>آموزشی که در حال ویرایش است (‎?id=‎) یا null.</summary>
    public AssistantTeachingRow? Editing { get; private set; }

    /// <summary>متن پیش‌فرض کادر پرسش (از ‎?q=‎ یا آموزش در حال ویرایش).</summary>
    public string Question { get; private set; } = "";

    /// <summary>کارهای مجاز برای فهرست کشویی (به ترتیب عنوان).</summary>
    public static IReadOnlyList<AssistantIntent> Intents { get; } =
        AssistantTeachingService.Teachable.OrderBy(i => AssistantModel.IntentTitle(i), StringComparer.Ordinal).ToList();

    public async Task<IActionResult> OnGetAsync(Guid? id, string? q)
    {
        Items = await teachings.ListAsync(HttpContext.RequestAborted);
        Candidates = await teachings.CandidatesAsync(30, HttpContext.RequestAborted);
        if (id is { } value)
        {
            Editing = Items.FirstOrDefault(t => t.Id == value);
            if (Editing is null)
                return NotFound();
            Question = Editing.Question;
        }
        else if (!string.IsNullOrWhiteSpace(q))
        {
            var trimmed = q.Trim();
            Question = trimmed.Length > 300 ? trimmed[..300] : trimmed;
        }
        return Page();
    }

    /// <summary>
    /// ثبت یا ویرایش آموزش. intent خالی = «فقط جواب». isActive از چک‌باکس (با hidden=false).
    /// خطای اعتبارسنجی ← پیام قرمز و بازگشت به فرم با همان پرسش.
    /// </summary>
    public async Task<IActionResult> OnPostSaveAsync(Guid? id, string? question, string? answer, string? url, string? intent, bool isActive = false)
    {
        AssistantIntent? chosen = Enum.TryParse<AssistantIntent>(intent, out var parsed) && Enum.IsDefined(parsed) ? parsed : null;
        var result = await teachings.SaveAsync(id, new AssistantTeachingInput(question, answer, url, chosen, isActive), ActorName, HttpContext.RequestAborted);
        if (result.IsSuccess())
        {
            SuccessMessage = result.Message;
            return LocalRedirect("/admin/smart/assistant/teach");
        }
        ErrorMessage = result.Message;
        return LocalRedirect(id is { } value
            ? $"/admin/smart/assistant/teach?id={value}#form"
            : "/admin/smart/assistant/teach?q=" + Uri.EscapeDataString((question ?? "").Trim() is { Length: > 300 } longText ? longText[..300] : (question ?? "").Trim()) + "#form");
    }

    /// <summary>حذف یک آموزش (نرم؛ دستیار دیگر از آن استفاده نمی‌کند).</summary>
    public async Task<IActionResult> OnPostDeleteAsync(Guid id)
    {
        var result = await teachings.DeleteAsync(id, HttpContext.RequestAborted);
        if (result.IsSuccess())
            SuccessMessage = result.Message;
        else
            ErrorMessage = result.Message;
        return LocalRedirect("/admin/smart/assistant/teach");
    }
}
