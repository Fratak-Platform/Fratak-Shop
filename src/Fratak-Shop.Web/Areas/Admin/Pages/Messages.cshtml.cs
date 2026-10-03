using Fratak_Shop.Core.Services.Messages;
using Fratak_Shop.Core.Services.Messaging;
using Fratak_Shop.Core.Services.Navigation;
using Fratak_Shop.Core.Services.Notifications;
using Fratak_Shop.Core.Services.Payments;
using Fratak_Shop.Core.Services.Seo;
using Fratak_Shop.Core.Services.Settings;
using Fratak_Shop.Data.Entities.Messages;
using Fratak_Shop.Web.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Areas.Admin.Pages;

[Authorize(Policy = PolicyNames.AdminOnly)]
public class MessagesModel(
    ISiteSettingService settings,
    IMenuService menus,
    INotificationService notifications,
    IBankTransferService bankTransfers,
    ISeoAuditService seoAudits,
    ISmsSender smsSender,
    IMessageService messages)
    : AdminPageModel(settings, menus, notifications, bankTransfers, seoAudits, smsSender)
{
    public List<Message> Items { get; private set; } = [];

    [BindProperty(SupportsGet = true, Name = "status")]
    public MessageStatus? Status { get; set; }

    [BindProperty(SupportsGet = true)]
    public Guid? Read { get; set; }

    [BindProperty]
    public ReplyInput Reply { get; set; } = new();

    public async Task OnGetAsync()
    {
        ViewData["Title"] = "پیام‌ها";
        Items = await messages.GetMessagesAsync(Status);

        if (Read.HasValue)
        {
            await messages.MarkAsReadAsync(Read.Value);
            Reply.MessageId = Read.Value;
        }
    }

    /// <summary>ثبت پاسخ پشتیبانی؛ متن پاسخ برای مشتری ذخیره می‌شود.</summary>
    public async Task<IActionResult> OnPostReplyAsync()
    {
        if (string.IsNullOrWhiteSpace(Reply.Content))
        {
            Notify("متن پاسخ را وارد کنید.", "error");
            return RedirectToPage("/Messages", new { area = "Admin", read = Reply.MessageId });
        }

        var message = await messages.GetMessageByIdAsync(Reply.MessageId);
        if (message is null)
        {
            Notify("پیام یافت نشد.", "error");
            return RedirectToPage("/Messages", new { area = "Admin" });
        }

        message.Reply = Reply.Content;
        message.RepliedAt = DateTimeOffset.UtcNow;
        message.RepliedByUserId = CurrentUserId;
        message.Status = MessageStatus.Answered;
        message.IsRead = true;
        message.ReadAt ??= DateTimeOffset.UtcNow;

        var result = await messages.UpdateMessageAsync(message);

        if (result.IsSuccess() && message.UserId.HasValue)
        {
            await notifications.NotifyUserAsync(message.UserId.Value, "پاسخ پشتیبانی",
                "پاسخ پیام شما در پنل کاربری ثبت شد.", NotificationType.Message, "/profile/messages");
        }

        Notify(result.Message, result.IsSuccess() ? "success" : "error");
        return RedirectToPage("/Messages", new { area = "Admin", read = message.Id });
    }

    public async Task<IActionResult> OnPostStatusAsync(Guid id, MessageStatus status)
    {
        var message = await messages.GetMessageByIdAsync(id);
        if (message is null)
        {
            Notify("پیام یافت نشد.", "error");
            return RedirectToPage("/Messages", new { area = "Admin" });
        }

        message.Status = status;
        var result = await messages.UpdateMessageAsync(message);
        Notify(result.Message, result.IsSuccess() ? "success" : "error");
        return RedirectToPage("/Messages", new { area = "Admin" });
    }

    public async Task<IActionResult> OnPostDeleteAsync(Guid id)
    {
        var result = await messages.DeleteMessageAsync(id);
        Notify(result.Message, result.IsSuccess() ? "success" : "error");
        return RedirectToPage("/Messages", new { area = "Admin" });
    }

    public class ReplyInput
    {
        public Guid MessageId { get; set; }

        [System.ComponentModel.DataAnnotations.Required(ErrorMessage = "متن پاسخ را وارد کنید.")]
        public string Content { get; set; } = null!;
    }
}
