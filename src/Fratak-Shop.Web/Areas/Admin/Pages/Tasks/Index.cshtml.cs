using Fratak_Shop.Core.Services.Team;
using Fratak_Shop.Data.Entities.Team;
using Fratak_Shop.Web.Helpers;
using Fratak_Shop.Web.Infrastructure.Admin;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Areas.Admin.Pages.TeamPages;

/// <summary>
/// صفحهٔ «کارهای من» (مرحلهٔ هفتم – فاز ۱۰): فهرست کارهای شخصی هر کارمند با موعد، اولویت و لینک به صفحهٔ مربوط.
/// • کار را برای خودم یا یک همکار می‌سازم (همکار اعلان می‌گیرد)؛
/// • دکمهٔ «＋ کار» بالای همهٔ صفحه‌های پنل، همین فرم را با لینک و عنوان صفحهٔ فعلی پر می‌کند؛
/// • موعد که برسد، TeamWorker یک بار یادآوری می‌فرستد.
/// handler «staff» (GET /admin/tasks?handler=staff) فهرست همکاران را برای پیشنهاد منشن (@نام) در یادداشت‌ها برمی‌گرداند.
/// </summary>
public class TasksModel(IStaffTaskService tasks, IMentionService mentions, AdminBadgeService badges) : AdminPageModel
{
    [BindProperty(SupportsGet = true, Name = "view")]
    public string? ViewKey { get; set; }

    /// <summary>?new=1 فرم «کار جدید» را باز می‌کند (از دکمهٔ سربرگ با url و title صفحهٔ فعلی).</summary>
    [BindProperty(SupportsGet = true, Name = "new")]
    public bool ShowNew { get; set; }

    [BindProperty(SupportsGet = true, Name = "edit")]
    public Guid? EditId { get; set; }

    [BindProperty(SupportsGet = true, Name = "url")]
    public string? PrefillUrl { get; set; }

    [BindProperty(SupportsGet = true, Name = "title")]
    public string? PrefillTitle { get; set; }

    public StaffTaskView CurrentView { get; private set; }

    public List<StaffTask> Items { get; private set; } = [];

    public StaffTaskCounts Counts { get; private set; } = new(0, 0, 0);

    public List<StaffMention> Staff { get; private set; } = [];

    /// <summary>کاری که در فرم ویرایش می‌شود (null = کار جدید).</summary>
    public StaffTask? Editing { get; private set; }

    public async Task OnGetAsync()
    {
        CurrentView = ViewKey switch
        {
            "done" => StaffTaskView.Done,
            "delegated" => StaffTaskView.Delegated,
            _ => StaffTaskView.Open,
        };
        Items = await tasks.ListAsync(ActorId, CurrentView);
        Counts = await tasks.CountsAsync(ActorId, DateTimeOffset.UtcNow);
        Staff = await mentions.DirectoryAsync();
        if (EditId is { } id)
        {
            Editing = Items.FirstOrDefault(t => t.Id == id)
                      ?? (await tasks.ListAsync(ActorId, StaffTaskView.Delegated)).FirstOrDefault(t => t.Id == id)
                      ?? (await tasks.ListAsync(ActorId, StaffTaskView.Open)).FirstOrDefault(t => t.Id == id);
            ShowNew = Editing is not null;
        }
    }

    /// <summary>فهرست همکاران برای پیشنهاد منشن: [{ "name": "سارا محمدی", "handle": "سارا_محمدی" }, …]</summary>
    public async Task<IActionResult> OnGetStaffAsync()
    {
        var directory = await mentions.DirectoryAsync();
        Response.Headers.CacheControl = "private, max-age=120";
        return new JsonResult(directory.Where(m => m.Handle.Length > 0).Select(m => new { name = m.Name, handle = m.Handle }));
    }

    public async Task<IActionResult> OnPostSaveAsync(Guid? id, Guid? assigneeId, string? title, string? notes, string? due, StaffTaskPriority priority, string? url)
    {
        DateTimeOffset? dueAt = null;
        if (!string.IsNullOrWhiteSpace(due))
        {
            // «1405/08/10» ← ساعت ۱۰ صبح همان روز؛ «1405/08/10 18:30» ← همان ساعت (به وقت ایران).
            dueAt = PersianDateInput.ParseDateTime(due, new TimeOnly(10, 0));
            if (dueAt is null)
            {
                ErrorMessage = "موعد را به شکل «1405/08/10» یا «1405/08/10 18:30» وارد کنید.";
                return Redirect(id is { } editId ? $"/admin/tasks?edit={editId}" : "/admin/tasks?new=1");
            }
        }

        var result = await tasks.SaveAsync(new StaffTaskInput(id, assigneeId, title, notes, dueAt, priority, url), Access);
        ShowResult(result);
        badges.InvalidatePersonal(ActorId);
        if (assigneeId is { } assignee)
            badges.InvalidatePersonal(assignee);
        if (!Succeeded(result))
            return Redirect(id is { } failedId ? $"/admin/tasks?edit={failedId}" : "/admin/tasks?new=1");
        return Redirect(assigneeId is { } other && other != ActorId ? "/admin/tasks?view=delegated" : "/admin/tasks");
    }

    public async Task<IActionResult> OnPostDoneAsync(Guid id, bool done, string? view)
    {
        ShowResult(await tasks.SetDoneAsync(id, done, Access));
        badges.InvalidatePersonal(ActorId);
        return Redirect(ViewUrl(view));
    }

    public async Task<IActionResult> OnPostDeleteAsync(Guid id, string? view)
    {
        ShowResult(await tasks.DeleteAsync(id, Access));
        badges.InvalidatePersonal(ActorId);
        return Redirect(ViewUrl(view));
    }

    private static string ViewUrl(string? view) => view is "done" or "delegated" ? $"/admin/tasks?view={view}" : "/admin/tasks";

    // ── کمکی‌های نمایش ──

    public static (string Text, string Css) PriorityLabel(StaffTaskPriority priority) => priority switch
    {
        StaffTaskPriority.High => ("فوری", "a-badge-red"),
        StaffTaskPriority.Low => ("کم", "a-badge-gray"),
        _ => ("عادی", "a-badge-blue"),
    };

    /// <summary>متن موعد با رنگ: «دیر شده» قرمز، «امروز» زرد.</summary>
    public static (string Text, string Css) DueLabel(StaffTask task, DateTimeOffset now)
    {
        if (task.DueAt is not { } due)
            return ("بدون موعد", "a-muted");
        var text = Format.DateTime(due);
        if (task.IsDone)
            return (text, "a-muted");
        if (due <= now)
            return ($"دیر شده · {text}", "text-red-600");
        return due - now <= TimeSpan.FromHours(24) ? ($"امروز/فردا · {text}", "text-amber-600") : (text, "a-muted");
    }

    public string? StaffName(Guid id) => id == ActorId ? "خودم" : Staff.FirstOrDefault(s => s.UserId == id)?.Name;
}
