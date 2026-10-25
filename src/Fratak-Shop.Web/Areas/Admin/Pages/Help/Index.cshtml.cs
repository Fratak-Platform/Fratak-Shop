using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Web.Infrastructure.Admin;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Areas.Admin.Pages.Help;

/// <summary>
/// مدیریت متن راهنمای صفحه‌های پنل (<c>/admin/help</c>).
/// <para>
/// فهرست همهٔ راهنماها + فرم ویرایش (<c>?id=</c>) یا ساخت (<c>?new=1&amp;path=/admin/...</c>).
/// دکمهٔ «ویرایش این راهنما» داخل کشوی راهنمای هر صفحه مستقیم به همین فرم می‌آید.
/// </para>
/// متن با ویرایشگر دیداری نوشته و هنگام ذخیره با HtmlSanitizer پاک‌سازی می‌شود.
/// </summary>
[AdminPermission(AdminPermissions.SettingsManage)]
public class IndexModel(IAdminHelpService help, IMonitoringService monitoring) : AdminPageModel
{
    public List<AdminHelpDto> Items { get; private set; } = [];
    public AdminHelpDto? Editing { get; private set; }
    public bool IsNew { get; private set; }

    [BindProperty] public HelpInput Input { get; set; } = new();

    public sealed class HelpInput
    {
        public string Path { get; set; } = "";
        public string Title { get; set; } = "";
        public string Body { get; set; } = "";
    }

    public async Task<IActionResult> OnGetAsync(Guid? id, bool @new, string? path)
    {
        Items = await help.ListAsync();
        if (id is { } helpId)
        {
            Editing = await help.FindAsync(helpId);
            if (Editing is null)
                return NotFound();
            Input = new HelpInput { Path = Editing.Path, Title = Editing.Title, Body = Editing.Body };
        }
        else if (@new)
        {
            IsNew = true;
            Input = new HelpInput { Path = AdminHelpService.ResolveKeys(path ?? "/admin")[0] };
        }

        return Page();
    }

    public async Task<IActionResult> OnPostSaveAsync(Guid? id)
    {
        var result = await help.SaveAsync(id, Input.Path, Input.Title, Input.Body);
        if (!result.IsSuccess())
        {
            TempData["ErrorMessage"] = result.Message;
            Items = await help.ListAsync();
            IsNew = id is null;
            Editing = id is { } existing ? await help.FindAsync(existing) : null;
            return Page();
        }

        await monitoring.LogActionAsync("help.save", "AdminHelpArticle", result.Data, Input.Path);
        TempData["SuccessMessage"] = "راهنما ذخیره شد.";
        return RedirectToPage(new { id = result.Data });
    }

    public async Task<IActionResult> OnPostResetAsync(Guid id)
    {
        var result = await help.ResetAsync(id);
        TempData[result.IsSuccess() ? "SuccessMessage" : "ErrorMessage"] = result.IsSuccess() ? "متن پیش‌فرض بازگردانده شد." : result.Message;
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostDeleteAsync(Guid id)
    {
        var result = await help.DeleteAsync(id);
        TempData[result.IsSuccess() ? "SuccessMessage" : "ErrorMessage"] = result.IsSuccess() ? "راهنما حذف شد." : result.Message;
        return RedirectToPage();
    }
}
