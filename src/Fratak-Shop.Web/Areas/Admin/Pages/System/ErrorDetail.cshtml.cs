using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Data.Entities.Admin;
using Fratak_Shop.Web.Infrastructure.Admin;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Areas.Admin.Pages.SystemPages;

[AdminPermission(AdminPermissions.MonitoringView)]
public class ErrorDetailModel(IMonitoringService monitoring) : AdminPageModel
{
    public ErrorLog Error { get; private set; } = null!;

    public async Task<IActionResult> OnGetAsync(Guid id)
    {
        var error = await monitoring.GetErrorAsync(id);
        if (error is null)
            return NotFound();
        Error = error;
        return Page();
    }

    public async Task<IActionResult> OnPostResolveAsync(Guid id, string fingerprint)
    {
        var count = await monitoring.ResolveAsync(fingerprint, ActorId);
        SuccessMessage = $"{count} رخداد رفع‌شده علامت خورد.";
        return Redirect($"/admin/system/errors/{id}");
    }
}
