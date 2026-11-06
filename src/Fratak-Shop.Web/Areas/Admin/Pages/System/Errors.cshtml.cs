using Fratak_Shop.Core.DTOs.Admin;
using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Data.Entities.Admin;
using Fratak_Shop.Web.Infrastructure.Admin;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Areas.Admin.Pages.SystemPages;

[AdminPermission(AdminPermissions.MonitoringView)]
public class ErrorsModel(IMonitoringService monitoring) : AdminPageModel
{
    [BindProperty(SupportsGet = true, Name = "q")] public string? Search { get; set; }
    [BindProperty(SupportsGet = true, Name = "level")] public string? Level { get; set; }
    [BindProperty(SupportsGet = true, Name = "days")] public int Days { get; set; } = 7;
    [BindProperty(SupportsGet = true, Name = "resolved")] public bool IncludeResolved { get; set; }
    [BindProperty(SupportsGet = true, Name = "fp")] public string? Fingerprint { get; set; }
    [FromQuery(Name = "page")] public int PageNumber { get; set; } = 1;

    public PagedList<ErrorGroupDto> Groups { get; private set; } = new([], 0, 1, 30);
    public PagedList<ErrorLog> Occurrences { get; private set; } = new([], 0, 1, 50);
    public ErrorStatsDto Stats { get; private set; } = new();

    public async Task OnGetAsync()
    {
        Days = Math.Clamp(Days, 1, 3650);
        var filter = new ErrorFilter { Search = Search, Level = Level, Days = Days, IncludeResolved = IncludeResolved || Fingerprint is not null, Fingerprint = Fingerprint, Page = Math.Max(1, PageNumber), Take = Fingerprint is null ? 30 : 50 };
        Stats = await monitoring.GetErrorStatsAsync();
        if (Fingerprint is null)
            Groups = await monitoring.GetErrorGroupsAsync(filter);
        else
            Occurrences = await monitoring.GetErrorsAsync(filter);
    }

    [AdminPermission(AdminPermissions.MonitoringView, AdminPermissions.SystemManage)]
    public async Task<IActionResult> OnPostResolveAsync(string fingerprint)
    {
        var count = await monitoring.ResolveAsync(fingerprint, ActorId);
        SuccessMessage = $"{count} مورد به‌عنوان «رفع‌شده» علامت خورد. اگر دوباره رخ دهد، باز هم نمایش داده می‌شود.";
        return BackToList();
    }

    [AdminPermission(AdminPermissions.SystemManage)]
    public async Task<IActionResult> OnPostResolveAllAsync()
    {
        var count = await monitoring.ResolveAllAsync(ActorId);
        await monitoring.LogActionAsync("ResolveAllErrors", "ErrorLog", null, $"{count} خطا رفع‌شده علامت خورد");
        SuccessMessage = $"{count} خطا به‌عنوان «رفع‌شده» علامت خورد.";
        return Redirect("/admin/system/errors");
    }
}
