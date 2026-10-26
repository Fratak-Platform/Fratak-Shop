using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Core.Services.Seo.Engine;
using Fratak_Shop.Data.Entities.SeoEngine;
using Fratak_Shop.Web.Infrastructure.Admin;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Areas.Admin.Pages.Seo;

/// <summary>
/// تحلیل زندهٔ سئوی یک صفحه (مرحلهٔ هفتم – فاز ۸). آدرس: /admin/seo/analyze?kind=Product&amp;id=…
/// <para>نمره و همهٔ بررسی‌ها (گروه‌بندی‌شده با توضیح و راه اصلاح)، پیش‌نمایش گوگل، کلمه‌های کلیدی نامزد
/// و تاریخچهٔ پیشنهادهای این صفحه. دکمهٔ «ساخت پیشنهاد» همین الان پیشنهاد تازه می‌سازد و به صفحهٔ بررسی می‌برد.</para>
/// </summary>
[AdminPermission(AdminPermissions.SeoManage)]
public class AnalyzeModel(ISeoEngineService engine) : AdminPageModel
{
    [BindProperty(SupportsGet = true)] public SeoEntityKind Kind { get; set; }
    [BindProperty(SupportsGet = true)] public Guid Id { get; set; }

    public SeoAnalysisView Analysis { get; private set; } = null!;

    public async Task<IActionResult> OnGetAsync()
    {
        SeoEngineHost.Current = Request.Host.Host;
        var view = await engine.AnalyzeAsync(Kind, Id, HttpContext.RequestAborted);
        if (view is null)
        {
            ErrorMessage = "صفحه پیدا نشد (حذف، غیرفعال یا منتشرنشده است).";
            return LocalRedirect("/admin/seo/engine?tab=pages");
        }
        Analysis = view;
        return Page();
    }

    /// <summary>ساخت فوری پیشنهاد (پیشنهاد منتظر قبلی همین صفحه جایگزین می‌شود).</summary>
    public async Task<IActionResult> OnPostGenerateAsync()
    {
        var result = await engine.GenerateAsync(Kind, Id, HttpContext.RequestAborted);
        ShowResult(result);
        if (Succeeded(result))
            return LocalRedirect($"/admin/seo/review?focus={result.Data}");
        return LocalRedirect($"/admin/seo/analyze?kind={Kind}&id={Id}");
    }
}
