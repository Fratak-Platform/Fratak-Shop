using Fratak_Shop.Web.Infrastructure.Admin;
using Fratak_Shop.Web.Infrastructure.Admin.Manage;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Areas.Admin.Pages.Manage;

public class ListModel(ManageEngine engine) : AdminPageModel
{
    public MDef Def { get; private set; } = null!;
    public ManageList List { get; private set; } = null!;

    [BindProperty(SupportsGet = true, Name = "q")] public string? Search { get; set; }
    [BindProperty(SupportsGet = true)] public Guid? Parent { get; set; }

    public string? ParentLabel { get; private set; }

    public async Task<IActionResult> OnGetAsync(string key, [FromQuery(Name = "page")] int pageNumber = 1)
    {
        var def = ManageRegistry.Find(key);
        if (def is null)
            return NotFound();
        if (!Access.HasAny(def.Permissions))
            return Redirect("/admin/denied?need=" + string.Join('|', def.Permissions));

        Def = def;
        List = await engine.ListAsync(def, Search, Parent, pageNumber);
        if (Parent is { } parentId && def.ParentField is not null && def.Field(def.ParentField) is { RefType: not null } parentField)
            ParentLabel = (await engine.OptionsAsync(parentField.RefType!, parentField.RefLabel)).GetValueOrDefault(parentId);
        return Page();
    }

    public async Task<IActionResult> OnPostDeleteAsync(string key, Guid id)
    {
        var def = ManageRegistry.Find(key);
        if (def is null)
            return NotFound();
        if (!Access.HasAny(def.Permissions) || !def.CanDelete)
            return Redirect("/admin/denied?need=" + string.Join('|', def.Permissions));

        ShowResult(await engine.DeleteAsync(def, id));
        return BackToList();
    }

    public async Task<IActionResult> OnPostToggleAsync(string key, Guid id)
    {
        var def = ManageRegistry.Find(key);
        if (def is null || def.Field("IsActive") is null)
            return NotFound();
        if (!Access.HasAny(def.Permissions))
            return Redirect("/admin/denied?need=" + string.Join('|', def.Permissions));

        var entity = await engine.FindAsync(def, id);
        if (entity is null)
            return NotFound();
        var property = def.Entity.GetProperty("IsActive")!;
        property.SetValue(entity, !(bool)property.GetValue(entity)!);
        ShowResult(await engine.SaveAsync(def, entity, isNew: false));
        return BackToList();
    }
}
