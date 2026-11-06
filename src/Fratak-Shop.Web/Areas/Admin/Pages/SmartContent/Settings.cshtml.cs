using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Core.Services.Smart;
using Fratak_Shop.Web.Infrastructure.Admin;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Areas.Admin.Pages.SmartContent;

/// <summary>
/// تنظیمات محتوای هوشمند (دور ۱۹). آدرس: /admin/smart/settings
/// <para>روشن/خاموش کل سیستم، سقف‌های عکس، آستانهٔ مشخصات و خلاصهٔ خرید، تقویم بلاگ، یادآوری‌های مشتری (با قالب پیام و سکوت شبانه)،
/// قوانین «تمدید» هر دسته، هشدار کالای راکد، جلوگیری از فروش زیر قیمت خرید.</para>
/// <para>دور ۲۱: «دستیار محلی» و مدل زبانی (Ollama) کاملاً حذف شدند؛ همهٔ کارها با موتور داخلی خود سایت انجام می‌شود.</para>
/// </summary>
[AdminPermission(AdminPermissions.SettingsManage)]
public class SettingsModel(ISmartConfigService config, ISmartCatalogService catalog, ICustomerReminderService reminders) : AdminPageModel
{
    [BindProperty] public SmartOptions Options { get; set; } = new();

    public ReminderStats Reminders { get; private set; } = new(0, 0, 0);

    public List<(Guid Id, string Name, bool HasDescription, bool HasImage, int Products)> Categories { get; private set; } = [];

    public async Task OnGetAsync()
    {
        var ct = HttpContext.RequestAborted;
        Options = await config.GetAsync(ct);
        Reminders = await reminders.StatsAsync(ct);
        Categories = await catalog.ListCategoriesAsync(ct);
    }

    /// <summary>ذخیره. قوانین تمدید از دو آرایهٔ هم‌طول (شناسهٔ دسته، روز) ساخته می‌شوند؛ روز ۰ یعنی خاموش.</summary>
    public async Task<IActionResult> OnPostSaveAsync(Guid[]? ruleCategory, int[]? ruleDays)
    {
        var rules = new List<ReplenishRule>();
        if (ruleCategory is not null && ruleDays is not null)
        {
            for (var i = 0; i < Math.Min(ruleCategory.Length, ruleDays.Length); i++)
            {
                if (ruleDays[i] > 0)
                    rules.Add(new ReplenishRule { CategoryId = ruleCategory[i], Days = ruleDays[i] });
            }
        }
        Options.ReplenishRules = rules;
        var result = await config.SaveAsync(Options, HttpContext.RequestAborted);
        SuccessMessage = result.Message;
        return BackToList();
    }
}
