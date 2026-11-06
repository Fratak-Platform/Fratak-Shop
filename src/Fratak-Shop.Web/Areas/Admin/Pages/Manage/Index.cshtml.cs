using Fratak_Shop.Web.Infrastructure.Admin;
using Fratak_Shop.Web.Infrastructure.Admin.Manage;

namespace Fratak_Shop.Web.Areas.Admin.Pages.Manage;

/// <summary>All tables of the generic engine the current staff member may manage, grouped.</summary>
public class IndexModel(ManageEngine engine) : AdminPageModel
{
    public List<(MDef Def, int Count)> Items { get; private set; } = [];

    public async Task OnGetAsync()
    {
        foreach (var def in ManageRegistry.All.Where(d => Access.HasAny(d.Permissions)))
            Items.Add((def, await engine.CountAsync(def)));
    }
}
