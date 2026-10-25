using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Data.Utilities.SeoManagement;
using Fratak_Shop.Web.Infrastructure.Admin;
using Fratak_Shop.Web.Infrastructure.Admin.Manage;
using Fratak_Shop.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Areas.Admin.Pages.Manage;

[RequestFormLimits(MultipartBodyLengthLimit = 9 * 1024 * 1024)]
[RequestSizeLimit(10 * 1024 * 1024)]
/// <summary>
/// فرم ایجاد/ویرایش همهٔ جدول‌های «مدیریت داده».
/// <para>
/// ابزارهای مرحلهٔ هفتم (فاز ۳): هشدار ویرایش هم‌زمان (حضور زنده با SignalR + بررسی نسخهٔ رکورد هنگام ذخیره)،
/// پیش‌نویس خودکار و ادغام نسخه‌ها (admin-edit.js + EditDraftService) و تاریخچهٔ تغییرات با بازگردانی (AuditHistoryService).
/// </para>
/// </summary>
public class EditModel(ManageEngine engine, IAuditHistoryService history, IEditDraftService drafts, EditPresenceNotifier presence) : AdminPageModel
{
    /// <summary>کلید حضور و پیش‌نویس، مثلاً <c>manage:faq-items:3f2a…</c> یا برای مورد جدید <c>manage:faq-items:new</c>.</summary>
    public string EditKey => $"manage:{Def.Key}:{(IsNew ? "new" : ManageEngine.IdOf(Entity).ToString())}";

    public EditHistoryModel? History { get; private set; }
    public string? ConflictMessage { get; private set; }

    /// <summary>نسخهٔ رکوردی که فرم بر اساس آن نمایش داده شده (هنگام تعارض همان نسخهٔ ارسالی می‌ماند).</summary>
    public string Stamp { get; private set; } = "0";

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
        await LoadHistoryAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(string key, Guid? id)
    {
        var denied = await LoadAsync(key, id);
        if (denied is not null)
            return denied;

        // کارمند دیگری در این فاصله همین رکورد را ذخیره کرده؟
        var stale = !IsNew && EditStamp.IsStale(Request.Form, await history.StampAsync(Def.Entity.Name, ManageEngine.IdOf(Entity)));

        var saved = new List<(UploadArea Area, string Name)>();
        var error = await engine.BindAsync(Def, Entity, Request.Form, saved, HttpContext.RequestAborted);
        if (error is null && stale)
        {
            ConflictMessage = await EditStamp.ConflictMessageAsync(history, Def.Entity.Name, ManageEngine.IdOf(Entity));
            Stamp = Request.Form[EditStamp.FieldName].FirstOrDefault() ?? Stamp;
            error = "ذخیره نشد: این مورد هم‌زمان توسط شخص دیگری تغییر کرده است.";
        }
        else if (error is null)
        {
            var draftKey = EditKey;
            var result = await engine.SaveAsync(Def, Entity, IsNew);
            if (Succeeded(result))
            {
                await drafts.DeleteAsync(ActorId, draftKey);
                if (!IsNew)
                    await presence.SavedAsync(EditKey, ActorName, ActorId);
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
        await LoadHistoryAsync();
        return Page();
    }

    /// <summary>بازگردانی یک ویرایش از تاریخچه (فقط فیلدهایی که بعداً دوباره تغییر نکرده‌اند).</summary>
    public async Task<IActionResult> OnPostRevertAsync(string key, Guid? id, Guid auditId)
    {
        if (id is null)
            return NotFound();
        var denied = await LoadAsync(key, id);
        if (denied is not null)
            return denied;

        var result = await history.RevertAsync(auditId, Def.Entity.Name, id.Value);
        if (Succeeded(result))
            await presence.SavedAsync(EditKey, ActorName, ActorId);
        ShowResult(result);
        return Redirect($"/admin/manage/{Def.Key}/edit/{id}" + (Parent is { } p ? $"?parent={p}" : "") + "#history");
    }

    private async Task LoadHistoryAsync()
    {
        if (IsNew)
            return;
        var id = ManageEngine.IdOf(Entity);
        var items = await history.ListAsync(Def.Entity.Name, id);
        var labels = Def.Fields.ToDictionary(f => f.Name, f => f.Label);
        History = new EditHistoryModel(Def.Entity.Name, id, items, $"/admin/manage/{Def.Key}/edit/{id}?handler=Revert" + (Parent is { } p ? $"&parent={p}" : ""), labels, CanRevert: true);
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

        Stamp = IsNew ? "0" : EditStamp.Format(await history.StampAsync(def.Entity.Name, ManageEngine.IdOf(Entity)));
        Options = await engine.AllOptionsAsync(def);
        if (def.HasSeo)
            Seo = SeoInput.From(def.Entity.GetProperty("Seo")?.GetValue(Entity) as SeoMetaData);
        return null;
    }
}
