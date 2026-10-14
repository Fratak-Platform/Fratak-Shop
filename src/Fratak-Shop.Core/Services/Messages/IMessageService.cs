using Fratak_Shop.Core.DTOs.Messages.Command;
using Fratak_Shop.Core.DTOs.Messages.Query;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Entities.Messages;

namespace Fratak_Shop.Core.Services.Messages;

public interface IMessageService
{
    Task<OperationResult> CreateMessageAsync(CreateMessageCommand command);
    Task<OperationResult> EditMessageAsync(EditMessageCommand command);
    Task<OperationResult> DeleteMessageAsync(Guid id);

    /// <summary>خوانده‌شده کردن پیام و در صورت جدید بودن، انتقال به «در حال بررسی».</summary>
    Task<OperationResult> MarkAsReadAsync(Guid id);

    /// <summary>ثبت پاسخ‌گویی به پیام (وضعیت «پاسخ داده‌شده» و زمان پاسخ).</summary>
    Task<OperationResult> ReplyMessageAsync(Guid id);

    Task<OperationResult> ChangeMessageStatusAsync(Guid id, MessageStatus status);

    Task<MessageFilterResult> GetMessagesByFilterAsync(MessageFilterParams filterParams);
    Task<MessageDto?> GetMessageByIdAsync(Guid id);
    Task<List<MessageFilterData>> GetMessagesByUserAsync(Guid userId);
    Task<List<MessageFilterData>> GetMessagesAsync(MessageStatus? status = null, MessageType? type = null);
}
