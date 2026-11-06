using Fratak_Shop.Core.DTOs.Admin;
using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Core.Services.Seo.Engine;
using Fratak_Shop.Core.Services.Seo.Tools;
using Fratak_Shop.Data.Entities.SeoEngine;
using Fratak_Shop.Web.Infrastructure.Admin;
using Fratak_Shop.Web.Infrastructure.SeoEngine;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Areas.Admin.Pages.Seo;

/// <summary>
/// یابندهٔ لینک شکسته (مرحلهٔ هفتم – فاز ۸ب). آدرس: /admin/seo/links
/// <para>همهٔ لینک‌ها و تصویرهای داخل متن محصولات، دسته‌ها، برندها، مقاله‌ها و صفحه‌ها روزی یک بار بررسی می‌شوند.
/// هر مشکل با علت، صفحهٔ محل لینک و پیشنهاد اصلاح نمایش داده می‌شود و با یک کلیک همین‌جا اصلاح می‌شود
/// (جایگزینی آدرس یا برداشتن لینک با حفظ متن آن). منطق: Core/Services/Seo/Tools/SeoLinkAuditService.cs.</para>
/// </summary>
[AdminPermission(AdminPermissions.SeoManage)]
public class LinksModel(ISeoLinkAuditService audit, SeoToolsQueue queue) : AdminPageModel
{
    /// <summary>زبانه‌ها: all، internal، external، images، ignored.</summary>
    [BindProperty(SupportsGet = true)] public string Tab { get; set; } = "all";
    [BindProperty(SupportsGet = true)] public SeoLinkProblem? Problem { get; set; }
    [BindProperty(SupportsGet = true, Name = "q")] public string? Search { get; set; }

    public SeoLinkDashboard Dashboard { get; private set; } = new();
    public PagedList<SeoLinkIssue> Issues { get; private set; } = new([], 0, 1, 30);
    public PagerModel Pager { get; private set; } = new(1, 1, 0);
    public bool IsRunning => SeoLinkAuditService.IsRunning;
    public bool RunQueued => queue.IsPending(SeoToolsJob.LinkAudit);

    public static readonly (string Key, string Title)[] Tabs =
    [
        ("all", "همهٔ مشکل‌ها"),
        ("internal", "لینک‌های داخلی"),
        ("external", "لینک‌های بیرونی"),
        ("images", "تصویرها"),
        ("ignored", "نادیده‌گرفته‌ها"),
    ];

    public async Task OnGetAsync([FromQuery(Name = "page")] int pageNumber = 1)
    {
        // دامنهٔ فعلی پنل = دامنهٔ سایت؛ لینک‌های مطلق به همین دامنه داخلی (بدون درخواست HTTP) بررسی می‌شوند.
        SeoEngineHost.Current = Request.Host.Host;
        if (!Tabs.Any(t => t.Key == Tab))
            Tab = "all";
        Dashboard = await audit.GetDashboardAsync(HttpContext.RequestAborted);
        Issues = await audit.ListAsync(new SeoLinkQuery(Tab, Problem, Search, pageNumber), HttpContext.RequestAborted);
        Pager = new PagerModel(Issues.Page, Math.Max(1, (int)Math.Ceiling(Issues.Total / (double)Issues.Take)), Issues.Total);
    }

    /// <summary>«همین الان بررسی کن»: درخواست به صف سرویس پس‌زمینه (صفحه منتظر نمی‌ماند).</summary>
    public IActionResult OnPostRun()
    {
        SeoEngineHost.Current = Request.Host.Host;
        if (IsRunning)
            ErrorMessage = "یک بررسی همین الان در جریان است؛ چند دقیقهٔ دیگر صفحه را تازه کنید.";
        else if (queue.Request(SeoToolsJob.LinkAudit))
            SuccessMessage = "بررسی در پس‌زمینه شروع شد. بسته به تعداد لینک‌های بیرونی چند ثانیه تا چند دقیقه طول می‌کشد؛ نتیجه را با اعلان خبر می‌دهیم.";
        else
            ErrorMessage = "درخواست بررسی قبلاً ثبت شده و منتظر شروع است.";
        return BackToList();
    }

    /// <summary>جایگزینی آدرس لینک در متن صفحه.</summary>
    public async Task<IActionResult> OnPostFixAsync(Guid id, string? newUrl)
    {
        if (string.IsNullOrWhiteSpace(newUrl))
            ErrorMessage = "آدرس تازه را وارد کنید (یا «برداشتن لینک» را بزنید).";
        else
            ShowResult(await audit.FixAsync(id, newUrl, HttpContext.RequestAborted));
        return BackToList();
    }

    /// <summary>برداشتن لینک (متن لینک می‌ماند) یا حذف تصویر خراب.</summary>
    public async Task<IActionResult> OnPostUnlinkAsync(Guid id)
    {
        ShowResult(await audit.FixAsync(id, null, HttpContext.RequestAborted));
        return BackToList();
    }

    /// <summary>همهٔ لینک‌های داخلی ریدایرکت‌دار → آدرس نهایی.</summary>
    public async Task<IActionResult> OnPostFixRedirectsAsync()
    {
        ShowResult(await audit.FixRedirectsAsync(HttpContext.RequestAborted));
        return BackToList();
    }

    public async Task<IActionResult> OnPostIgnoreAsync(Guid id, bool ignore)
    {
        ShowResult(await audit.IgnoreAsync(id, ignore, HttpContext.RequestAborted));
        return BackToList();
    }

    public async Task<IActionResult> OnPostSettingsAsync(bool enabled, bool checkExternal)
    {
        ShowResult(await audit.SaveSettingsAsync(enabled, checkExternal, HttpContext.RequestAborted));
        return BackToList();
    }

    public string UrlWith(params (string Key, string? Value)[] values) => AdminUi.With(Request, values);

    /// <summary>رنگ نشان هر نوع مشکل (قرمز = جدی).</summary>
    public static string ProblemBadge(SeoLinkProblem problem) => SeoLinkRules.Severity(problem) switch
    {
        >= 3 => "a-badge-red",
        2 => "a-badge-yellow",
        _ => "a-badge-blue",
    };

    /// <summary>عنوان کوتاه منبع لینک. مثال: Product → «محصول».</summary>
    public static string SourceTitle(SeoEntityKind kind) => SeoEngineUi.KindTitle(kind);
}
