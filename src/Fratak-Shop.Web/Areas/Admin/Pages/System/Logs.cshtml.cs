using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Web.Infrastructure.Admin;
using Fratak_Shop.Web.Infrastructure.Ops;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Areas.Admin.Pages.SystemPages;

/// <summary>
/// صفحهٔ «فنی ← گزارش‌های سرور» (مرحلهٔ هفتم – فاز ۹): آخرین پیام‌های گزارش برنامه با فیلتر سطح/متن/بخش، و فایل‌های
/// روزانهٔ هشدار و خطا برای دانلود. جایگزین دیدن لاگ‌ها با دسترسی SSH به سرور.
/// </summary>
[AdminPermission(AdminPermissions.MonitoringView)]
public class LogsModel(LogStore store) : AdminPageModel
{
    [BindProperty(SupportsGet = true, Name = "level")] public LogLevel? Level { get; set; }
    [BindProperty(SupportsGet = true, Name = "q")] public string? Search { get; set; }
    [BindProperty(SupportsGet = true, Name = "category")] public string? Category { get; set; }
    [FromQuery(Name = "page")] public int PageNumber { get; set; } = 1;

    public const int Take = 100;

    public List<LogEntry> Entries { get; private set; } = [];
    public int Total { get; private set; }
    public int PageCount { get; private set; }
    public List<(string Category, int Count)> Categories { get; private set; } = [];
    public Dictionary<LogLevel, int> LevelCounts { get; private set; } = new();
    public List<global::System.IO.FileInfo> Files { get; private set; } = [];
    public long Warnings => store.WarningsSinceStart;
    public long Errors => store.ErrorsSinceStart;

    public void OnGet()
    {
        var all = store.Snapshot();
        LevelCounts = all.GroupBy(e => e.Level).ToDictionary(g => g.Key, g => g.Count());
        Categories = all.GroupBy(e => ShortCategory(e.Category)).Select(g => (g.Key, g.Count())).OrderByDescending(x => x.Item2).Take(25).ToList();

        IEnumerable<LogEntry> query = all;
        if (Level is { } level)
            query = query.Where(e => level == LogLevel.Error ? e.Level >= LogLevel.Error : e.Level == level);
        if (!string.IsNullOrWhiteSpace(Category))
            query = query.Where(e => ShortCategory(e.Category) == Category);
        if (!string.IsNullOrWhiteSpace(Search))
        {
            var q = Search.Trim();
            query = query.Where(e => e.Message.Contains(q, StringComparison.OrdinalIgnoreCase)
                || (e.Path?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)
                || (e.TraceId?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)
                || (e.Exception?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false));
        }
        var filtered = query.ToList();
        Total = filtered.Count;
        PageCount = Math.Max(1, (int)Math.Ceiling(Total / (double)Take));
        PageNumber = Math.Clamp(PageNumber, 1, PageCount);
        Entries = filtered.Skip((PageNumber - 1) * Take).Take(Take).ToList();
        Files = store.Files();
    }

    /// <summary>نام کوتاه بخش برای نمایش. مثال: «Fratak_Shop.Web.Infrastructure.Ops.OpsWorker» → «Ops.OpsWorker».</summary>
    public static string ShortCategory(string category)
    {
        var parts = category.Split('.');
        return parts.Length <= 2 ? category : string.Join('.', parts[^2..]);
    }

    /// <summary>دانلود فایل روزانه؛ فقط نام‌های معتبر از پوشهٔ گزارش‌ها (جلوگیری از خواندن فایل‌های دیگر).</summary>
    [AdminPermission(AdminPermissions.SystemManage)]
    public IActionResult OnGetFile(string name)
    {
        if (!LogStore.IsValidFileName(name))
            return NotFound();
        var path = global::System.IO.Path.Combine(store.Folder, name);
        if (!global::System.IO.File.Exists(path))
            return NotFound();
        // فایل ممکن است هم‌زمان در حال نوشتن باشد: با اشتراک خواندن/نوشتن باز می‌شود.
        var stream = new global::System.IO.FileStream(path, global::System.IO.FileMode.Open, global::System.IO.FileAccess.Read, global::System.IO.FileShare.ReadWrite | global::System.IO.FileShare.Delete);
        return File(stream, "text/plain; charset=utf-8", name);
    }
}
