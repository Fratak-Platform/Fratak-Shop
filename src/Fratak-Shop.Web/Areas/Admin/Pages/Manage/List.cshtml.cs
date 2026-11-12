using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Web.Infrastructure.Admin;
using Fratak_Shop.Web.Infrastructure.Admin.Manage;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Areas.Admin.Pages.Manage;

/// <summary>
/// فهرست یک جدول «مدیریت داده» (<c>/admin/manage/{key}</c>) – بازطراحی مرحلهٔ هفتم (فاز ۳).
/// <para>
/// امکانات: مرتب‌سازی با کلیک روی عنوان ستون، انتخاب ستون‌ها و حالت فشرده (ذخیره برای هر کارمند)، اندازهٔ صفحه،
/// ویرایش درجای خانه‌ها، فعال/غیرفعال با یک کلیک، کار گروهی (فعال، غیرفعال، حذف)، ترتیب دستی با کشیدن و رها کردن،
/// و لینک «سطل بازیافت» همان جدول. رفتار سمت مرورگر در <c>wwwroot/assets/js/admin-tables.js</c> است.
/// </para>
/// دسترسی: مجوزهای تعریف جدول (<see cref="MDef.Permissions"/>) برای همهٔ هندلرها بررسی می‌شود.
/// </summary>
public class ListModel(ManageEngine engine, IAdminPreferenceService preferences) : AdminPageModel
{
    /// <summary>کلید تنظیم شخصی ستون‌های یک جدول: <c>manage:cols:{key}</c> (فهرست نام فیلدها).</summary>
    public static string ColumnsKey(string key) => $"manage:cols:{key}";

    /// <summary>کلید تنظیم شخصی حالت فشردهٔ همهٔ جدول‌ها.</summary>
    public const string DensityKey = "manage:density";

    public MDef Def { get; private set; } = null!;
    public ManageList List { get; private set; } = null!;

    [BindProperty(SupportsGet = true, Name = "q")] public string? Search { get; set; }
    [BindProperty(SupportsGet = true)] public Guid? Parent { get; set; }
    [BindProperty(SupportsGet = true)] public string? Sort { get; set; }
    [BindProperty(SupportsGet = true)] public string? Dir { get; set; }
    [BindProperty(SupportsGet = true)] public int Take { get; set; } = ManageEngine.PageSize;

    public string? ParentLabel { get; private set; }

    /// <summary>ستون‌های قابل نمایش به ترتیب تعریف و ستون‌هایی که کارمند انتخاب کرده است.</summary>
    public List<MField> Columns { get; private set; } = [];
    public bool Compact { get; private set; }
    public int DeletedCount { get; private set; }

    /// <summary>کشیدن و رها کردن فقط وقتی معنی دارد که فهرست به ترتیب «ترتیب نمایش» و بدون جست‌وجو باشد.</summary>
    public bool Reorderable { get; private set; }

    public string? SortField => ManageEngine.SortableField(Def, Sort);
    public bool Descending => Dir == "desc";

    public async Task<IActionResult> OnGetAsync(string key, [FromQuery(Name = "page")] int pageNumber = 1)
    {
        var def = ManageRegistry.Find(key);
        if (def is null)
            return NotFound();
        if (!Access.HasAny(def.Permissions))
            return Redirect("/admin/denied?need=" + string.Join('|', def.Permissions));

        Def = def;
        if (!ManageQuery.AllowedPageSizes.Contains(Take))
            Take = ManageEngine.PageSize;

        List = await engine.ListAsync(def, new ManageQuery
        {
            Search = Search, Parent = Parent, Page = pageNumber, Sort = Sort, Descending = Descending, Take = Take,
        });
        if (Parent is { } parentId && def.ParentField is not null && def.Field(def.ParentField) is { RefType: not null } parentField)
            ParentLabel = (await engine.OptionsAsync(parentField.RefType!, parentField.RefLabel)).GetValueOrDefault(parentId);

        // ستون‌های انتخابی کارمند (اگر چیزی انتخاب نکرده یا فیلدها عوض شده‌اند ← ستون‌های پیش‌فرض تعریف).
        var chosen = await preferences.GetAsync<List<string>>(ActorId, ColumnsKey(def.Key));
        Columns = chosen is { Count: > 0 }
            ? def.Fields.Where(f => chosen.Contains(f.Name)).ToList()
            : [];
        if (Columns.Count == 0)
            Columns = def.Fields.Where(f => f.InList).ToList();

        Compact = await preferences.GetAsync<bool>(ActorId, DensityKey);
        DeletedCount = def.CanDelete ? await engine.CountDeletedAsync(def.Entity) : 0;
        Reorderable = ManageEngine.CanReorder(def) && string.IsNullOrWhiteSpace(Search) && SortField is null;
        return Page();
    }

    public async Task<IActionResult> OnPostDeleteAsync(string key, Guid id)
    {
        if (Resolve(key, needDelete: true) is not { } def)
            return Denied(key);

        ShowResult(await engine.DeleteAsync(def, id));
        return BackToList();
    }

    public async Task<IActionResult> OnPostToggleAsync(string key, Guid id)
    {
        if (Resolve(key) is not { } def || def.Field("IsActive") is null)
            return Denied(key);

        var entity = await engine.FindAsync(def, id);
        if (entity is null)
            return NotFound();
        var property = def.Entity.GetProperty("IsActive")!;
        property.SetValue(entity, !(bool)property.GetValue(entity)!);
        var result = await engine.SaveAsync(def, entity, isNew: false);
        if (IsFetch)
            return new JsonResult(new { ok = result.IsSuccess(), message = result.Message, value = property.GetValue(entity) is true });
        ShowResult(result);
        return BackToList();
    }

    /// <summary>
    /// ویرایش درجای یک خانه (fetch از admin-tables.js). پاسخ: <c>{ok, message, html, value}</c> که html همان محتوای جدید خانه است.
    /// </summary>
    public async Task<IActionResult> OnPostInlineAsync(string key, Guid id, string? field, string? value, CancellationToken cancellationToken)
    {
        if (Resolve(key) is not { } def)
            return Json(false, "دسترسی ندارید.");
        var meta = def.Field(field ?? "");
        if (meta is null || !meta.CanInline)
            return Json(false, "این فیلد در جدول قابل ویرایش نیست.");

        var entity = await engine.FindAsync(def, id);
        if (entity is null)
            return Json(false, "این ردیف پیدا نشد؛ شاید حذف شده است. صفحه را تازه کنید.");

        var error = await engine.BindInlineAsync(def, meta, entity, value, cancellationToken);
        if (error is not null)
            return Json(false, error);

        var result = await engine.SaveAsync(def, entity, isNew: false);
        if (!result.IsSuccess())
            return Json(false, result.Message ?? "ذخیره نشد.");

        var html = ManageEngine.Cell(def, meta, entity, await RefLabelsAsync(def, meta));
        using var writer = new StringWriter();
        html.WriteTo(writer, global::System.Text.Encodings.Web.HtmlEncoder.Default);
        return new JsonResult(new { ok = true, message = "ذخیره شد.", html = writer.ToString(), value = ManageEngine.InputValue(entity, meta) });
    }

    /// <summary>کار گروهی روی ردیف‌های تیک‌خورده: activate | deactivate | delete.</summary>
    public async Task<IActionResult> OnPostBulkAsync(string key, List<Guid> ids, string? action)
    {
        if (Resolve(key, needDelete: action == "delete") is not { } def)
            return Denied(key);
        ids = ids.Distinct().Take(100).ToList();
        if (ids.Count == 0)
        {
            ErrorMessage = "ابتدا ردیف‌ها را تیک بزنید.";
            return BackToList();
        }

        var done = 0;
        var errors = new List<string>();
        foreach (var id in ids)
        {
            OperationResult result;
            switch (action)
            {
                case "delete":
                    result = await engine.DeleteAsync(def, id);
                    break;
                case "activate" or "deactivate" when def.Field("IsActive") is not null:
                    var entity = await engine.FindAsync(def, id);
                    if (entity is null)
                        continue;
                    def.Entity.GetProperty("IsActive")!.SetValue(entity, action == "activate");
                    var saved = await engine.SaveAsync(def, entity, isNew: false);
                    result = saved.IsSuccess() ? OperationResult.Success() : OperationResult.Error(saved.Message ?? "ذخیره نشد.");
                    break;
                default:
                    ErrorMessage = "عملیات گروهی نامعتبر است.";
                    return BackToList();
            }

            if (result.IsSuccess())
                done++;
            else if (result.Message is { } message && !errors.Contains(message))
                errors.Add(message);
        }

        if (done > 0)
            SuccessMessage = action switch
            {
                "delete" => $"{done} ردیف به سطل بازیافت رفت.",
                "activate" => $"{done} ردیف فعال شد.",
                _ => $"{done} ردیف غیرفعال شد.",
            };
        if (errors.Count > 0)
            ErrorMessage = string.Join(" ", errors);
        return BackToList();
    }

    /// <summary>ذخیرهٔ ترتیب جدید بعد از کشیدن و رها کردن (fetch). ids = ردیف‌های همین صفحه به ترتیب جدید.</summary>
    public async Task<IActionResult> OnPostReorderAsync(string key, List<Guid> ids)
    {
        if (Resolve(key) is not { } def || !ManageEngine.CanReorder(def))
            return Json(false, "این جدول ترتیب دستی ندارد.");
        var result = await engine.ReorderAsync(def, ids);
        return Json(result.IsSuccess(), result.Message ?? "");
    }

    /// <summary>ذخیرهٔ ستون‌های انتخابی و حالت فشرده (فرم «ستون‌ها»).</summary>
    public async Task<IActionResult> OnPostColumnsAsync(string key, List<string> cols, bool compact, bool reset)
    {
        if (Resolve(key) is not { } def)
            return Denied(key);

        if (reset)
        {
            await preferences.RemoveAsync(ActorId, ColumnsKey(def.Key));
        }
        else
        {
            var valid = def.Fields.Select(f => f.Name).Where(cols.Contains).ToList();
            if (valid.Count == 0)
            {
                ErrorMessage = "حداقل یک ستون را انتخاب کنید.";
                return BackToList();
            }
            await preferences.SetAsync(ActorId, ColumnsKey(def.Key), valid);
        }
        await preferences.SetAsync(ActorId, DensityKey, compact);
        return BackToList();
    }

    /// <summary>آدرس همین فهرست با تغییر چند پارامتر (برای لینک مرتب‌سازی و اندازهٔ صفحه).</summary>
    public string ListUrl(params (string Key, string? Value)[] changes) => AdminUi.With(Request, changes);

    private bool IsFetch => Request.Headers.XRequestedWith == "fetch" || Request.Headers.Accept.ToString().Contains("application/json");

    private MDef? Resolve(string key, bool needDelete = false)
    {
        var def = ManageRegistry.Find(key);
        if (def is null || !Access.HasAny(def.Permissions) || (needDelete && !def.CanDelete))
            return null;
        return def;
    }

    private IActionResult Denied(string key)
    {
        var def = ManageRegistry.Find(key);
        if (def is null)
            return NotFound();
        if (IsFetch)
            return Json(false, "دسترسی ندارید.");
        return Redirect("/admin/denied?need=" + string.Join('|', def.Permissions));
    }

    private static JsonResult Json(bool ok, string message) => new(new { ok, message });

    private async Task<IReadOnlyDictionary<string, Dictionary<Guid, string>>> RefLabelsAsync(MDef def, MField field) =>
        field.Kind == MKind.Ref && field.RefType is not null
            ? new Dictionary<string, Dictionary<Guid, string>> { [field.Name] = await engine.OptionsAsync(field.RefType, field.RefLabel) }
            : new Dictionary<string, Dictionary<Guid, string>>();
}
