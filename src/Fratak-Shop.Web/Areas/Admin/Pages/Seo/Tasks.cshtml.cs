using Fratak_Shop.Core.DTOs.Admin;
using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Core.Services.Seo.Autopilot;
using Fratak_Shop.Data.Entities.SeoEngine;
using Fratak_Shop.Web.Infrastructure.Admin;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Areas.Admin.Pages.Seo;

/// <summary>
/// «کارهای سئو» (دور سئوی خودکار، موارد ۲۰۶ تا ۲۱۰). آدرس: /admin/seo/tasks
/// <para>هر مشکلی که خزش شبانه پیدا می‌کند (صفحهٔ ۴۰۴، عنوان تکراری، تصویر بدون Alt، اسکیمای ناقص…) یک «کار» با توضیح،
/// راه‌حل، صفحه‌ها و اهمیت است. کارهایی که «رفع خودکار» دارند را خود سیستم درست می‌کند و بعد از خزش بعدی بسته می‌شوند.</para>
/// <para>مدیر سئو می‌تواند کاری را «نادیده» بگیرد (مثلاً صفحه‌ای که عمداً کوتاه است) یا دوباره باز کند؛ مشاور سئو فقط می‌بیند.</para>
/// </summary>
[AdminPermission(AdminPermissions.SeoView, AdminPermissions.SeoManage)]
public class TasksModel(ISeoReportService reports) : AdminPageModel
{
    /// <summary>open (پیش‌فرض)، resolved (حل‌شده‌ها)، dismissed (نادیده‌ها)، all.</summary>
    [BindProperty(SupportsGet = true)] public string Tab { get; set; } = "open";
    [BindProperty(SupportsGet = true)] public SeoTaskSeverity? Severity { get; set; }
    [BindProperty(SupportsGet = true, Name = "q")] public string? Search { get; set; }

    public PagedList<SeoTask> Tasks { get; private set; } = new([], 0, 1, 30);
    public PagerModel Pager { get; private set; } = new(1, 1, 0);
    public bool CanManage => Can(AdminPermissions.SeoManage);

    public static readonly (string Key, string Title)[] Tabs =
    [
        ("open", "باز"),
        ("resolved", "حل‌شده"),
        ("dismissed", "نادیده‌گرفته"),
        ("all", "همه"),
    ];

    public async Task OnGetAsync([FromQuery(Name = "page")] int pageNumber = 1)
    {
        if (!Tabs.Any(t => t.Key == Tab))
            Tab = "open";
        SeoTaskStatus[]? statuses = Tab switch
        {
            "open" => [SeoTaskStatus.Open],
            "dismissed" => [SeoTaskStatus.Dismissed],
            "resolved" => [SeoTaskStatus.Fixed, SeoTaskStatus.AutoFixed], // رفع دستی + رفع خودکار
            _ => null,
        };

        Tasks = await reports.TasksAsync(new SeoTaskQuery(statuses, Severity, Search, ClampPage(pageNumber)), HttpContext.RequestAborted);

        Pager = new PagerModel(Tasks.Page, Math.Max(1, (int)Math.Ceiling(Tasks.Total / (double)Tasks.Take)), Tasks.Total);
    }

    /// <summary>نادیده گرفتن / بازگشایی / علامت «انجام شد».</summary>
    [AdminPermission(AdminPermissions.SeoManage)]
    public async Task<IActionResult> OnPostStatusAsync(Guid id, SeoTaskStatus status)
    {
        ShowResult(await reports.SetTaskStatusAsync(id, status, ActorName, HttpContext.RequestAborted));
        return BackToList();
    }

    public string UrlWith(params (string Key, string? Value)[] values) => AdminUi.With(Request, values);

    public static string StatusTitle(SeoTaskStatus status) => status switch
    {
        SeoTaskStatus.Open => "باز",
        SeoTaskStatus.Fixed => "حل‌شده",
        SeoTaskStatus.AutoFixed => "رفع خودکار",
        SeoTaskStatus.Dismissed => "نادیده",
        _ => "—",
    };
}
