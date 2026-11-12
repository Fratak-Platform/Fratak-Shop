using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Core.Services.Assistant;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Entities.Assistant;
using Fratak_Shop.Web.Infrastructure.Admin;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Areas.Admin.Pages.SmartContent;

/// <summary>
/// دور ۲۸ – «دانش دستیار». آدرس: /admin/smart/assistant/knowledge
/// <list type="bullet">
/// <item>فهرست جدول دانش (واژه‌نامه، راهنمای خرید، گپ کوتاه، هم‌معنی‌ها، یادگرفته‌ها) با جست‌وجو، فیلتر نوع و شمارنده‌های یادگیری.</item>
/// <item>افزودن/ویرایش دانستنی؛ دستیار از همان لحظه از آن استفاده می‌کند. خاموش/روشن و حذف.</item>
/// <item>اطمینان هر دانستنی از 👍/👎 مشتری‌ها هر شب به‌روز می‌شود؛ دانستنی بی‌فایده خودکار خاموش می‌شود.</item>
/// </list>
/// GET هیچ تغییری نمی‌دهد. دسترسی: همان کسانی که دستیار را آموزش می‌دهند.
/// </summary>
[AdminPermission(AdminPermissions.ProductsManage, AdminPermissions.MarketingManage, AdminPermissions.TicketsManage)]
public class KnowledgeModel(IAssistantFactService facts) : AdminPageModel
{
    public IReadOnlyList<AssistantFactRow> Items { get; private set; } = [];
    public int Total { get; private set; }
    public int PageNumber { get; private set; } = 1;
    public const int PageSize = 50;

    [BindProperty(SupportsGet = true)] public AssistantFactKind? Kind { get; set; }
    [BindProperty(SupportsGet = true, Name = "q")] public string? Search { get; set; }

    /// <summary>دانستنی در حال ویرایش (‎?id=‎) یا null.</summary>
    public AssistantFactRow? Editing { get; private set; }

    /// <summary>عنوان فارسی هر نوع.</summary>
    public static string KindTitle(AssistantFactKind kind) => kind switch
    {
        AssistantFactKind.Glossary => "واژه‌نامه",
        AssistantFactKind.Guide => "راهنمای خرید",
        AssistantFactKind.SmallTalk => "گپ کوتاه",
        AssistantFactKind.Synonym => "هم‌معنی",
        AssistantFactKind.Learned => "یادگرفته",
        _ => kind.ToString(),
    };

    public async Task<IActionResult> OnGetAsync(Guid? id, int p = 1)
    {
        PageNumber = Math.Max(1, p);
        (Items, Total) = await facts.ListAsync(Kind, Search, PageNumber, PageSize, HttpContext.RequestAborted);
        if (id is { } value)
        {
            Editing = await facts.GetAsync(value, HttpContext.RequestAborted);
            if (Editing is null)
                return NotFound();
        }
        return Page();
    }

    /// <summary>ثبت یا ویرایش. خطا ← پیام قرمز و بازگشت به فرم.</summary>
    public async Task<IActionResult> OnPostSaveAsync(Guid? id, AssistantFactKind kind, string? term, string? aliases, string? answer, string? url, string? topic)
    {
        var result = await facts.SaveAsync(id, new AssistantFactInput(kind, term, aliases, answer, url, topic), ActorName, HttpContext.RequestAborted);
        ShowResult(result);
        return LocalRedirect(result.IsSuccess() || id is null ? "/admin/smart/assistant/knowledge" : $"/admin/smart/assistant/knowledge?id={id}#form");
    }

    public async Task<IActionResult> OnPostToggleAsync(Guid id)
    {
        ShowResult(await facts.ToggleAsync(id, HttpContext.RequestAborted));
        return LocalRedirect("/admin/smart/assistant/knowledge");
    }

    public async Task<IActionResult> OnPostDeleteAsync(Guid id)
    {
        ShowResult(await facts.DeleteAsync(id, HttpContext.RequestAborted));
        return LocalRedirect("/admin/smart/assistant/knowledge");
    }
}
