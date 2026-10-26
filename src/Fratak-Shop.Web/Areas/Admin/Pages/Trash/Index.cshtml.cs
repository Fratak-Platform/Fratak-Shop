using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Entities.Admin;
using Fratak_Shop.Web.Infrastructure.Admin;
using Fratak_Shop.Web.Infrastructure.Admin.Manage;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Web.Areas.Admin.Pages.Trash;

/// <summary>
/// «سطل بازیافت» (<c>/admin/trash</c>): همهٔ موارد حذف‌شده (حذف نرم) با امکان بازگرداندن.
/// <para>
/// هر نوع داده فقط برای کسی نمایش داده می‌شود که مجوز مدیریت/حذف همان داده را دارد (TrashRegistry)؛
/// مثلاً محصولات حذف‌شده فقط برای دارندهٔ مجوز «حذف محصول». نام حذف‌کننده از گزارش فعالیت خوانده می‌شود.
/// </para>
/// بازگردانی با <see cref="IAdminDataService.RestoreAsync{T}"/> انجام می‌شود و در گزارش فعالیت با عملیات «بازیابی» ثبت می‌شود.
/// </summary>
[AdminPermission(AdminPermissions.ProductsDelete, AdminPermissions.BlogPublish, AdminPermissions.CatalogManage, AdminPermissions.ContentManage,
    AdminPermissions.LoyaltyManage, AdminPermissions.MarketingManage, AdminPermissions.ProductsManage, AdminPermissions.ReturnsManage,
    AdminPermissions.SeoManage, AdminPermissions.ShippingManage, AdminPermissions.SuppliersManage, AdminPermissions.TicketsManage,
    AdminPermissions.ExpensesManage)]
public class IndexModel(ManageEngine engine, IAdminDataService data) : AdminPageModel
{
    public const int Take = 50;

    /// <summary>نوع‌های در دسترس کارمند با تعداد موارد حذف‌شده.</summary>
    public List<(TrashSource Source, int Count)> Sources { get; private set; } = [];
    public TrashSource? Selected { get; private set; }
    public List<TrashRow> Rows { get; private set; } = [];
    public Dictionary<Guid, string> DeletedBy { get; private set; } = [];
    public int SelectedCount { get; private set; }
    public int CurrentPage { get; private set; } = 1;

    public async Task OnGetAsync(string? type, [FromQuery(Name = "page")] int pageNumber = 1)
    {
        foreach (var source in TrashRegistry.All.Where(s => Access.HasAny(s.Permissions)))
            Sources.Add((source, await engine.CountDeletedAsync(source.Entity)));

        // نوع انتخاب‌شده، یا اولین نوعی که مورد حذف‌شده دارد.
        var selected = Sources.FirstOrDefault(s => string.Equals(s.Source.Key, type, StringComparison.OrdinalIgnoreCase));
        if (selected.Source is null)
            selected = Sources.FirstOrDefault(s => s.Count > 0);
        if (selected.Source is null)
            return;

        Selected = selected.Source;
        SelectedCount = selected.Count;
        var pageCount = Math.Max(1, (int)Math.Ceiling(SelectedCount / (double)Take));
        CurrentPage = Math.Clamp(pageNumber, 1, pageCount);
        Rows = await engine.TrashAsync(Selected.Entity, Selected.LabelProperty, CurrentPage, Take);

        // چه کسی حذف کرده؟ (آخرین «حذف» ثبت‌شده در گزارش فعالیت برای هر ردیف)
        var ids = Rows.Select(r => r.Id.ToString()).ToList();
        var typeName = Selected.Entity.Name;
        var logs = await data.Query<AuditLog>()
            .Where(l => l.EntityType == typeName && l.Action == "delete" && l.EntityId != null && ids.Contains(l.EntityId))
            .OrderByDescending(l => l.CreatedAt)
            .Select(l => new { l.EntityId, l.UserName })
            .ToListAsync();
        foreach (var log in logs)
            if (Guid.TryParse(log.EntityId, out var id) && !DeletedBy.ContainsKey(id) && log.UserName is not null)
                DeletedBy[id] = log.UserName;
    }

    /// <summary>بازگرداندن یک یا چند مورد (دکمهٔ هر ردیف یا انتخاب گروهی).</summary>
    public async Task<IActionResult> OnPostRestoreAsync(string? type, List<Guid> ids)
    {
        var source = TrashRegistry.Find(type);
        if (source is null)
            return NotFound();
        if (!Access.HasAny(source.Permissions))
            return Redirect("/admin/denied?need=" + string.Join('|', source.Permissions));

        ids = ids.Distinct().Take(Take).ToList();
        if (ids.Count == 0)
        {
            ErrorMessage = "موردی انتخاب نشده است.";
            return BackToList();
        }

        var restored = 0;
        var errors = new List<string>();
        foreach (var id in ids)
        {
            var result = await engine.RestoreAsync(source.Entity, id);
            if (result.IsSuccess())
                restored++;
            else if (result.Message is { } message && !errors.Contains(message))
                errors.Add(message);
        }

        if (restored > 0)
            SuccessMessage = ids.Count == 1 ? "مورد از سطل بازیافت برگردانده شد." : $"{restored} مورد از سطل بازیافت برگردانده شد.";
        if (errors.Count > 0)
            ErrorMessage = string.Join(" ", errors);
        return BackToList();
    }
}
