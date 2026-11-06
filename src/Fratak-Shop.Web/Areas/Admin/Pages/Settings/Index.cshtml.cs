using System.Globalization;
using System.Reflection;
using Fratak_Shop.Core.DTOs.Settings;
using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Core.Services.Settings;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Entities.Settings;
using Fratak_Shop.Web.Infrastructure.Admin;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Web.Areas.Admin.Pages.Settings;

/// <summary>
/// Store settings in tabs. Only the fields of the submitted tab are changed; all values go through the
/// settings service validation (and the database check constraints) and the settings cache is refreshed.
/// </summary>
[AdminPermission(AdminPermissions.SettingsManage)]
public class IndexModel(IAdminDataService data, IStoreSettingService settings, IMonitoringService monitoring) : AdminPageModel
{
    private static readonly Dictionary<string, PropertyInfo> CommandProperties =
        typeof(StoreSettingCommand).GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.CanWrite).ToDictionary(p => p.Name);

    private static readonly Dictionary<string, PropertyInfo> EntityProperties =
        typeof(StoreSetting).GetProperties(BindingFlags.Public | BindingFlags.Instance).ToDictionary(p => p.Name);

    [BindProperty(SupportsGet = true, Name = "tab")] public string? Tab { get; set; }

    public SGroup Group { get; private set; } = SettingsRegistry.Groups[0];
    public Dictionary<string, string?> Values { get; private set; } = new();
    public string? VapidPublicKey { get; private set; }
    public DateTimeOffset? UpdatedAt { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        // مرحلهٔ دوازدهم: لینک‌های قدیمی «?tab=brand» به استودیوی لوگو می‌روند.
        if (string.Equals(Tab, "brand", StringComparison.OrdinalIgnoreCase))
            return Redirect("/admin/settings/brand");
        Group = SettingsRegistry.Groups.FirstOrDefault(g => g.Key == Tab) ?? SettingsRegistry.Groups[0];
        var entity = await LoadAsync();
        if (entity is not null)
            Values = ReadValues(entity);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        Group = SettingsRegistry.Groups.FirstOrDefault(g => g.Key == Tab) ?? SettingsRegistry.Groups[0];
        var entity = await LoadAsync();
        if (entity is null)
            return NotFound();

        // Start from the current values, then apply the posted fields of this tab only.
        var command = new StoreSettingCommand();
        foreach (var (name, property) in CommandProperties)
            if (EntityProperties.TryGetValue(name, out var source) && source.PropertyType == property.PropertyType)
                property.SetValue(command, source.GetValue(entity));

        var posted = new Dictionary<string, string?>();
        var changed = new List<string>();
        foreach (var field in Group.Fields)
        {
            if (!CommandProperties.TryGetValue(field.Name, out var property))
                continue;
            var raw = Request.Form["f." + field.Name].ToArray();
            // Checkbox followed by a hidden "false": the first value wins.
            var text = raw.Length == 0 ? null : raw[0];
            posted[field.Name] = text;
            if (!TryConvert(text, property.PropertyType, out var value))
            {
                ErrorMessage = $"مقدار «{field.Label}» معتبر نیست.";
                Values = ReadValues(entity);
                foreach (var (k, v) in posted) Values[k] = v;
                return Page();
            }
            if (!Equals(property.GetValue(command), value))
                changed.Add(field.Label);
            property.SetValue(command, value);
        }

        if (changed.Count == 0)
        {
            SuccessMessage = "تغییری ثبت نشد.";
            return Redirect($"/admin/settings?tab={Group.Key}");
        }

        OperationResult result;
        try
        {
            result = await settings.UpdateAsync(command);
        }
        catch (DbUpdateException)
        {
            result = OperationResult.Error("یکی از مقادیر خارج از محدوده مجاز است.");
        }

        if (!Succeeded(result))
        {
            ErrorMessage = result.Message;
            Values = ReadValues(entity);
            foreach (var (k, v) in posted) Values[k] = v;
            return Page();
        }

        await monitoring.LogActionAsync("settings.update", "StoreSetting", entity.Id, $"{Group.Title}: {string.Join("، ", changed.Take(12))}");
        SuccessMessage = result.Message;
        return Redirect($"/admin/settings?tab={Group.Key}");
    }

    private async Task<StoreSetting?> LoadAsync()
    {
        var entity = await data.Query<StoreSetting>().OrderBy(s => s.CreatedAt).FirstOrDefaultAsync();
        VapidPublicKey = entity?.VapidPublicKey;
        UpdatedAt = entity?.UpdatedAt;
        return entity;
    }

    private static Dictionary<string, string?> ReadValues(StoreSetting entity)
    {
        var values = new Dictionary<string, string?>();
        foreach (var group in SettingsRegistry.Groups)
        foreach (var field in group.Fields)
        {
            if (!EntityProperties.TryGetValue(field.Name, out var property))
                continue;
            values[field.Name] = property.GetValue(entity) switch
            {
                null => null,
                bool b => b ? "true" : "false",
                decimal d => d.ToString(field.Kind == SKind.Decimal ? "0.######" : "0", CultureInfo.InvariantCulture),
                IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
                var other => other.ToString(),
            };
        }
        return values;
    }

    private static bool TryConvert(string? text, Type type, out object? value)
    {
        var trimmed = text?.Trim();
        var empty = string.IsNullOrEmpty(trimmed);
        var numeric = empty ? "" : SearchText.NormalizeDigits(trimmed!).Replace(",", "").Replace("٬", "").Replace("،", "");
        value = null;
        if (type == typeof(string))
        {
            value = empty ? null : trimmed; // required strings are checked by the service
            return true;
        }
        if (type == typeof(bool))
        {
            value = string.Equals(trimmed, "true", StringComparison.OrdinalIgnoreCase);
            return true;
        }
        if (type == typeof(int) || type == typeof(int?))
        {
            if (empty) { value = type == typeof(int?) ? null : 0; return true; }
            if (int.TryParse(numeric, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i)) { value = i; return true; }
            return false;
        }
        if (type == typeof(decimal) || type == typeof(decimal?))
        {
            if (empty) { value = type == typeof(decimal?) ? null : 0m; return true; }
            if (decimal.TryParse(numeric, NumberStyles.Number, CultureInfo.InvariantCulture, out var d)) { value = d; return true; }
            return false;
        }
        return false;
    }
}
