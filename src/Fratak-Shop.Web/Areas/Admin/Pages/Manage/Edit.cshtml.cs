using Fratak_Shop.Data.Utilities.SeoManagement;
using Fratak_Shop.Web.Infrastructure.Admin;
using Fratak_Shop.Web.Infrastructure.Admin.Manage;
using Fratak_Shop.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Areas.Admin.Pages.Manage;

[RequestFormLimits(MultipartBodyLengthLimit = 9 * 1024 * 1024)]
[RequestSizeLimit(10 * 1024 * 1024)]
public class EditModel(ManageEngine engine) : AdminPageModel
{
    public MDef Def { get; private set; } = null!;
    public object Entity { get; private set; } = null!;
    public bool IsNew { get; private set; }
    public Dictionary<string, Dictionary<Guid, string>> Options { get; private set; } = new();
    public SeoInput Seo { get; private set; } = new();

    [BindProperty(SupportsGet = true)] public Guid? Parent { get; set; }

    public async Task<IActionResult> OnGetAsync(string key, Guid? id)
    {
        var denied = await LoadAsync(key, id);
        if (denied is not null)
            return denied;

        if (IsNew && Parent is { } parentId && Def.ParentField is not null)
        {
            var property = Def.Entity.GetProperty(Def.ParentField)!;
            property.SetValue(Entity, parentId);
        }
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(string key, Guid? id)
    {
        var denied = await LoadAsync(key, id);
        if (denied is not null)
            return denied;

        var saved = new List<(UploadArea Area, string Name)>();
        var error = await engine.BindAsync(Def, Entity, Request.Form, saved, HttpContext.RequestAborted);
        if (error is null)
        {
            var result = await engine.SaveAsync(Def, Entity, IsNew);
            if (Succeeded(result))
            {
                SuccessMessage = result.Message;
                return Request.Form["after"] == "new"
                    ? Redirect($"/admin/manage/{Def.Key}/edit" + (Parent is { } p ? $"?parent={p}" : ""))
                    : Redirect($"/admin/manage/{Def.Key}" + (Parent is { } p2 ? $"?parent={p2}" : ""));
            }
            error = result.Message;
        }

        engine.DeleteFiles(saved);
        ErrorMessage = error;
        if (Def.HasSeo)
            Seo = SeoInput.From(Def.Entity.GetProperty("Seo")?.GetValue(Entity) as SeoMetaData);
        return Page();
    }

    private async Task<IActionResult?> LoadAsync(string key, Guid? id)
    {
        var def = ManageRegistry.Find(key);
        if (def is null)
            return NotFound();
        if (!Access.HasAny(def.Permissions))
            return Redirect("/admin/denied?need=" + string.Join('|', def.Permissions));
        Def = def;

        if (id is { } entityId)
        {
            var entity = await engine.FindAsync(def, entityId);
            if (entity is null)
                return NotFound();
            Entity = entity;
        }
        else
        {
            if (!def.CanCreate)
                return NotFound();
            Entity = engine.Create(def);
            IsNew = true;
        }

        Options = await engine.AllOptionsAsync(def);
        if (def.HasSeo)
            Seo = SeoInput.From(def.Entity.GetProperty("Seo")?.GetValue(Entity) as SeoMetaData);
        return null;
    }
}
