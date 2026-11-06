using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Web.Infrastructure.Admin;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Areas.Admin.Pages.SystemPages;

[AdminPermission(AdminPermissions.MonitoringView)]
public class LiveModel(ActivityTracker tracker) : AdminPageModel
{
    public IReadOnlyList<OnlineVisitor> Visitors { get; private set; } = [];
    public IReadOnlyList<MinuteStat> Minutes { get; private set; } = [];
    public int Members => Visitors.Count(v => v.UserId is not null);
    public int Staff => Visitors.Count(v => v.IsStaffArea);
    public List<(string Path, int Count)> TopPages { get; private set; } = [];
    public List<(string Device, int Count)> Devices { get; private set; } = [];
    public List<(string Source, int Count)> Sources { get; private set; } = [];

    public void OnGet() => Load();

    /// <summary>Fragment refreshed every few seconds by <c>[data-live]</c>.</summary>
    public IActionResult OnGetFragment()
    {
        Load();
        return Partial("_LiveFragment", this);
    }

    private void Load()
    {
        Visitors = tracker.Online().OrderByDescending(v => v.LastSeen).ToList();
        Minutes = tracker.Minutes(15);
        var publicVisitors = Visitors.Where(v => !v.IsStaffArea).ToList();
        TopPages = publicVisitors.GroupBy(v => v.Path).Select(g => (g.Key, g.Count())).OrderByDescending(x => x.Item2).Take(10).ToList();
        Devices = publicVisitors.GroupBy(v => v.Device).Select(g => (g.Key, g.Count())).OrderByDescending(x => x.Item2).ToList();
        Sources = publicVisitors.GroupBy(v => SourceOf(v.Referrer)).Select(g => (g.Key, g.Count())).OrderByDescending(x => x.Item2).Take(8).ToList();
    }

    private string SourceOf(string? referrer)
    {
        if (string.IsNullOrEmpty(referrer) || !Uri.TryCreate(referrer, UriKind.Absolute, out var uri))
            return "مستقیم";
        if (string.Equals(uri.Host, Request.Host.Host, StringComparison.OrdinalIgnoreCase))
            return "داخلی";
        var host = uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? uri.Host[4..] : uri.Host;
        return host.Contains("google.") ? "گوگل" : host;
    }
}
