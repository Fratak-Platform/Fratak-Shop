using Fratak_Shop.Core.DTOs.Admin;
using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Entities.Messages;
using Fratak_Shop.Web.Helpers;
using Fratak_Shop.Web.Infrastructure.Admin;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Web.Areas.Admin.Pages.Messages;

[AdminPermission(AdminPermissions.MessagesManage)]
public class IndexModel(IAdminDataService data, IMonitoringService monitoring) : AdminPageModel
{
    [BindProperty(SupportsGet = true, Name = "state")] public string? State { get; set; }
    [BindProperty(SupportsGet = true, Name = "type")] public MessageType? Type { get; set; }
    [BindProperty(SupportsGet = true, Name = "q")] public string? Search { get; set; }
    [FromQuery(Name = "page")] public int PageNumber { get; set; } = 1;

    public PagedList<Message> Items { get; private set; } = new([], 0, 1, 20);
    public Dictionary<MessageStatus, int> Counts { get; private set; } = new();

    public async Task OnGetAsync()
    {
        Counts = await data.Query<Message>().GroupBy(m => m.Status).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count);
        var query = Filtered();
        var total = await query.CountAsync();
        var pageNumber = ClampPage(PageNumber, total, 20);
        var rows = await query.OrderByDescending(m => m.CreatedAt).Skip((pageNumber - 1) * 20).Take(20).ToListAsync();
        Items = new PagedList<Message>(rows, total, pageNumber, 20);
    }

    public async Task<IActionResult> OnGetExportAsync(string format = "xlsx")
    {
        var rows = await Filtered().OrderByDescending(m => m.CreatedAt).Take(20000).ToListAsync();
        var table = new ExportTable("پیام‌های تماس", "تاریخ", "نوع", "وضعیت", "نام", "موبایل", "ایمیل", "موضوع", "متن");
        foreach (var m in rows)
            table.Add(Format.DateTime(m.CreatedAt), TypeTitle(m.Type), StatusTitle(m.Status), m.FullName ?? "", m.PhoneNumber ?? "", m.Email ?? "", m.Subject ?? "", m.Content);
        return Exporter.File(table, format, "messages");
    }

    public async Task<IActionResult> OnPostStatusAsync(Guid id, MessageStatus status)
    {
        ShowResult(await ApplyAsync(id, status));
        return BackToList();
    }

    public async Task<IActionResult> OnPostBulkAsync(List<Guid> ids, string action)
    {
        if (ids.Count == 0)
        {
            ErrorMessage = "هیچ پیامی انتخاب نشده است.";
            return BackToList();
        }

        int done = 0;
        foreach (var id in ids.Distinct().Take(200))
        {
            var result = action switch
            {
                "delete" => await data.DeleteAsync<Message>(id),
                "answered" => await ApplyAsync(id, MessageStatus.Answered),
                "closed" => await ApplyAsync(id, MessageStatus.Closed),
                _ => await ApplyAsync(id, MessageStatus.InProgress),
            };
            if (Succeeded(result)) done++;
        }
        await monitoring.LogActionAsync("message.bulk", "Message", null, $"{action}: {done} پیام");
        SuccessMessage = $"عملیات روی {done} پیام انجام شد.";
        return BackToList();
    }

    public async Task<IActionResult> OnPostDeleteAsync(Guid id)
    {
        var result = await data.DeleteAsync<Message>(id);
        if (Succeeded(result))
            await monitoring.LogActionAsync("message.delete", "Message", id, "حذف پیام تماس");
        ShowResult(result);
        return BackToList();
    }

    private async Task<OperationResult> ApplyAsync(Guid id, MessageStatus status)
    {
        var message = await data.FindAsync<Message>(id);
        if (message is null)
            return OperationResult.NotFound("پیام یافت نشد.");
        message.Status = status;
        message.IsRead = true;
        message.ReadAt ??= DateTimeOffset.UtcNow;
        if (status == MessageStatus.Answered)
            message.RepliedAt ??= DateTimeOffset.UtcNow;
        var result = await data.UpdateAsync(message);
        return Succeeded(result) ? OperationResult.Success("وضعیت پیام به‌روز شد.") : OperationResult.Error(result.Message ?? "ذخیره نشد.");
    }

    private IQueryable<Message> Filtered()
    {
        var query = data.Query<Message>();
        query = State switch
        {
            "all" => query,
            "progress" => query.Where(m => m.Status == MessageStatus.InProgress),
            "answered" => query.Where(m => m.Status == MessageStatus.Answered),
            "closed" => query.Where(m => m.Status == MessageStatus.Closed),
            _ => query.Where(m => m.Status == MessageStatus.New || m.Status == MessageStatus.None),
        };
        if (Type is { } type)
            query = query.Where(m => m.Type == type);
        if (!string.IsNullOrWhiteSpace(Search))
        {
            var term = SearchText.NormalizeDigits(Search.Trim());
            query = query.Where(m => (m.FullName != null && m.FullName.Contains(term)) || (m.PhoneNumber != null && m.PhoneNumber.Contains(term))
                                     || (m.Email != null && m.Email.Contains(term)) || (m.Subject != null && m.Subject.Contains(term)) || m.Content.Contains(term));
        }
        return query;
    }

    public static string TypeTitle(MessageType type) => type switch
    {
        MessageType.Support => "پشتیبانی",
        MessageType.Suggestion => "پیشنهاد",
        MessageType.Complaint => "شکایت",
        _ => "تماس",
    };

    public static string StatusTitle(MessageStatus status) => status switch
    {
        MessageStatus.InProgress => "در حال پیگیری",
        MessageStatus.Answered => "پاسخ داده شد",
        MessageStatus.Closed => "بسته",
        _ => "جدید",
    };

    public static string StatusBadge(MessageStatus status) => status switch
    {
        MessageStatus.InProgress => "a-badge-blue",
        MessageStatus.Answered => "a-badge-green",
        MessageStatus.Closed => "a-badge-gray",
        _ => "a-badge-yellow",
    };
}
