using Fratak_Shop.Core.DTOs.Admin;
using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Data.Entities.Admin;
using Fratak_Shop.Web.Infrastructure.Admin;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Areas.Admin.Pages.SystemPages;

[AdminPermission(AdminPermissions.AuditView, AdminPermissions.MonitoringView)]
public class SecurityModel(IMonitoringService monitoring) : AdminPageModel
{
    [BindProperty(SupportsGet = true, Name = "type")] public SecurityEventType? Type { get; set; }
    [BindProperty(SupportsGet = true, Name = "user")] public Guid? UserId { get; set; }
    [BindProperty(SupportsGet = true, Name = "q")] public string? Search { get; set; }
    [BindProperty(SupportsGet = true, Name = "days")] public int Days { get; set; } = 30;
    [FromQuery(Name = "page")] public int PageNumber { get; set; } = 1;

    public PagedList<SecurityLog> Events { get; private set; } = new([], 0, 1, 50);
    public Dictionary<SecurityEventType, int> Stats { get; private set; } = new();
    public List<(string Ip, int Count)> SuspiciousIps { get; private set; } = [];

    public async Task OnGetAsync()
    {
        Days = Math.Clamp(Days, 1, 365);
        Events = await monitoring.GetSecurityAsync(new SecurityFilter { Type = Type, UserId = UserId, Search = Search, Days = Days, Page = Math.Max(1, PageNumber), Take = 50 });
        Stats = await monitoring.GetSecurityStatsAsync(Days);
        var failures = await monitoring.GetSecurityAsync(new SecurityFilter { Type = SecurityEventType.LoginFailed, Days = 1, Page = 1, Take = 100 });
        SuspiciousIps = failures.Items.Where(e => e.IpAddress is not null).GroupBy(e => e.IpAddress!)
            .Select(g => (g.Key, g.Count())).Where(x => x.Item2 >= 5).OrderByDescending(x => x.Item2).Take(10).ToList();
    }
}
