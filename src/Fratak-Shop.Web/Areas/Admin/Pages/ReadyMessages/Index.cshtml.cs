using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Core.Services.Notifications;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Entities.Notifications;
using Fratak_Shop.Web.Infrastructure.Admin;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Areas.Admin.Pages.ReadyMessages;

/// <summary>
/// «پیام‌های آماده» (آدرس <c>/admin/ready-messages</c>): متن‌های ذخیره‌شده برای اعلان تخفیف، پاسخ تیکت،
/// پاسخ دیدگاه، پیام تماس و پیام سفارش.
/// <para>
/// هر پیام می‌تواند برچسب‌هایی مثل <c>{FirstName}</c> یا <c>{OrderNumber}</c> داشته باشد که هنگام ارسال
/// با اطلاعات همان مشتری پر می‌شوند. در فرم‌های ارسال، فهرست کشویی «پیام آماده» فقط پیام‌های همان محل
/// (Scope) را نشان می‌دهد (<see cref="ViewComponents.ReadyMessagePickerViewComponent"/>).
/// </para>
/// </summary>
[AdminPermission(AdminPermissions.NotificationsSend, AdminPermissions.TicketsManage, AdminPermissions.CommentsModerate,
    AdminPermissions.MessagesManage, AdminPermissions.OrdersManage)]
public class IndexModel(IReadyMessageService readyMessages, INotificationService notifications, IMonitoringService monitoring) : AdminPageModel
{
    /// <summary>
    /// محل‌های قابل انتخاب با عنوان فارسی (ترتیب نمایش).
    /// «پیام تماس» فعلاً فرم پاسخ ندارد، پس در فرم نمایش داده نمی‌شود (مقدار آن در enum برای آینده رزرو است).
    /// </summary>
    public static readonly (ReadyMessageScope Scope, string Title)[] ScopeTitles =
    [
        (ReadyMessageScope.Broadcast, "پیام همگانی"),
        (ReadyMessageScope.Direct, "اعلان تکی"),
        (ReadyMessageScope.Ticket, "پاسخ تیکت"),
        (ReadyMessageScope.Comment, "پاسخ دیدگاه"),
        (ReadyMessageScope.Order, "پیام سفارش"),
    ];

    public List<ReadyMessageDto> Items { get; private set; } = [];
    [BindProperty] public SaveReadyMessageCommand Input { get; set; } = new();
    /// <summary>محل‌های تیک‌خورده در فرم (به Flags تبدیل می‌شود).</summary>
    [BindProperty] public List<ReadyMessageScope> SelectedScopes { get; set; } = [];
    public bool IsEditing => Input.Id is not null;
    public IReadOnlyList<NotificationPlaceholderGroup> Groups => NotificationPlaceholders.ForOrderMessage;

    public async Task<IActionResult> OnGetAsync(Guid? id)
    {
        Items = await readyMessages.AllAsync();
        if (id is { } editId)
        {
            var item = Items.FirstOrDefault(m => m.Id == editId);
            if (item is null)
                return NotFound();
            Input = new SaveReadyMessageCommand
            {
                Id = item.Id, Name = item.Name, Title = item.Title, Body = item.Body, LinkUrl = item.LinkUrl,
                Scopes = item.Scopes, SortOrder = item.SortOrder, IsActive = item.IsActive,
            };
        }

        SelectedScopes = ScopeTitles.Select(s => s.Scope).Where(s => Input.Scopes.HasFlag(s)).ToList();
        return Page();
    }

    public async Task<IActionResult> OnPostSaveAsync()
    {
        Input.Scopes = SelectedScopes.Aggregate(ReadyMessageScope.None, (all, s) => all | s);
        var result = await readyMessages.SaveAsync(Input);
        if (!result.IsSuccess())
        {
            Items = await readyMessages.AllAsync();
            ModelState.AddModelError(string.Empty, result.Message);
            return Page();
        }

        await monitoring.LogActionAsync("ready-message.save", "ReadyMessage", Input.Id, Input.Name ?? string.Empty);
        TempData["SuccessMessage"] = result.Message;
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(Guid id)
    {
        var result = await readyMessages.DeleteAsync(id);
        if (result.IsSuccess())
            await monitoring.LogActionAsync("ready-message.delete", "ReadyMessage", id, "حذف پیام آماده");
        TempData[result.IsSuccess() ? "SuccessMessage" : "ErrorMessage"] = result.Message;
        return RedirectToPage();
    }

    /// <summary>پیش‌نمایش زنده با اطلاعات واقعی کارمند (درخواست fetch از admin.js).</summary>
    public async Task<IActionResult> OnPostPreviewAsync()
    {
        var preview = await notifications.PreviewAsync(NotificationEvent.None, ActorId, Input.Title, Input.Body, null);
        return new JsonResult(new { title = preview.Title, body = preview.Body, sms = (string?)null, source = preview.Source, unknown = preview.UnknownKeys });
    }

    /// <summary>یک واحد به شمارندهٔ استفادهٔ پیام اضافه می‌کند (fetch از انتخابگر پیام آماده در admin.js).</summary>
    public async Task<IActionResult> OnPostUsedAsync(Guid id)
    {
        await readyMessages.MarkUsedAsync(id);
        return new NoContentResult();
    }

    public static string ScopesText(ReadyMessageScope scopes) =>
        (scopes & ~ReadyMessageScope.ContactMessage) == (ReadyMessageScope.All & ~ReadyMessageScope.ContactMessage) ? "همه‌جا" : string.Join("، ", ScopeTitles.Where(s => scopes.HasFlag(s.Scope)).Select(s => s.Title));
}
