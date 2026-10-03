using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Entities.Messages;

namespace Fratak_Shop.Core.Services.Messages;

public interface IMessageService
{
    Task<OperationResult> CreateMessageAsync(Message message);
    Task<OperationResult> UpdateMessageAsync(Message message);
    Task<OperationResult> DeleteMessageAsync(Guid id);
    Task<OperationResult> MarkAsReadAsync(Guid id);
    Task<Message?> GetMessageByIdAsync(Guid id);
    Task<List<Message>> GetMessagesByUserAsync(Guid userId);
    Task<List<Message>> GetMessagesAsync(MessageStatus? status = null, MessageType? type = null);
}
