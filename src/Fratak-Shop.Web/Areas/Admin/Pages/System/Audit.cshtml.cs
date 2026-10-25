using System.Text.Json;
using Fratak_Shop.Core.DTOs.Admin;
using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Data.Entities.Admin;
using Fratak_Shop.Web.Infrastructure.Admin;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Areas.Admin.Pages.SystemPages;

public sealed record AuditChange(string Field, string? Before, string? After);

[AdminPermission(AdminPermissions.AuditView)]
public class AuditModel(IMonitoringService monitoring) : AdminPageModel
{
    [BindProperty(SupportsGet = true, Name = "q")] public string? Search { get; set; }
    [BindProperty(SupportsGet = true, Name = "user")] public Guid? UserId { get; set; }
    [BindProperty(SupportsGet = true, Name = "entity")] public string? EntityType { get; set; }
    [BindProperty(SupportsGet = true, Name = "id")] public string? EntityId { get; set; }
    [BindProperty(SupportsGet = true, Name = "action")] public string? ActionName { get; set; }
    [BindProperty(SupportsGet = true, Name = "from")] public string? From { get; set; }
    [BindProperty(SupportsGet = true, Name = "to")] public string? To { get; set; }
    [FromQuery(Name = "page")] public int PageNumber { get; set; } = 1;

    public PagedList<AuditLog> Logs { get; private set; } = new([], 0, 1, 50);
    public List<string> EntityTypes { get; private set; } = [];

    public async Task OnGetAsync()
    {
        var (from, to) = PersianDateInput.Period(From, To);
        Logs = await monitoring.GetAuditAsync(new AuditFilter
        {
            Search = Search, UserId = UserId, EntityType = EntityType, EntityId = EntityId, Action = ActionName,
            From = from, To = to, Page = Math.Max(1, PageNumber), Take = 50,
        });
        EntityTypes = await monitoring.GetAuditEntityTypesAsync();
    }

    public static List<AuditChange> ParseChanges(string? json)
    {
        var list = new List<AuditChange>();
        if (string.IsNullOrWhiteSpace(json))
            return list;
        try
        {
            using var doc = JsonDocument.Parse(json);
            foreach (var property in doc.RootElement.EnumerateObject())
            {
                if (property.Value.ValueKind != JsonValueKind.Array)
                    continue;
                var values = property.Value.EnumerateArray().Select(Text).ToList();
                list.Add(new AuditChange(property.Name, values.ElementAtOrDefault(0), values.ElementAtOrDefault(1)));
            }
        }
        catch (JsonException)
        {
            list.Add(new AuditChange("—", null, json));
        }
        return list;

        static string? Text(JsonElement element) => element.ValueKind switch
        {
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            JsonValueKind.String => element.GetString(),
            _ => element.GetRawText(),
        };
    }

    public static string ActionTitle(string action) => action switch
    {
        "create" => "ایجاد",
        "update" => "ویرایش",
        "delete" => "حذف",
        "restore" => "بازیابی",
        _ => action,
    };

    /// <summary>
    /// کوتاه‌شدهٔ مقدار برای جدول گزارش (حداکثر ۳۰۰ نویسه). از مرحلهٔ هفتم مقدارهای طولانی (مثل متن کامل مقاله تا ۶۰ هزار نویسه)
    /// برای «بازگردانی» کامل ذخیره می‌شوند؛ نمایش کاملشان صفحه را سنگین می‌کند. متن کامل در تاریخچهٔ صفحهٔ ویرایش همان مورد قابل بازگردانی است.
    /// </summary>
    public static string Short(string? value) =>
        value is null ? "∅" : value.Length <= 300 ? value : value[..299] + "…";

    public static string ActionBadge(string action) => action switch
    {
        "create" => "a-badge-green",
        "update" => "a-badge-blue",
        "delete" => "a-badge-red",
        "restore" => "a-badge-sky",
        _ => "a-badge-purple",
    };
}
