using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Messages;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Core.Services.Messages;

public class MessageService(FratakShopDbContext context) : IMessageService
{
    public async Task<OperationResult> CreateMessageAsync(Message message)
    {
        if (message is null)
            return OperationResult.Error("پیام نامعتبر است.");

        if (string.IsNullOrWhiteSpace(message.Content))
            return OperationResult.Error("متن پیام را وارد کنید.");

        if (message.UserId.HasValue)
        {
            var userExists = await context.Users.AnyAsync(x => x.Id == message.UserId.Value);
            if (!userExists)
                return OperationResult.NotFound("کاربر مورد نظر یافت نشد.");
        }

        message.Status = MessageStatus.New;
        message.IsRead = false;

        context.Messages.Add(message);
        await context.SaveChangesAsync();

        return OperationResult.Success("پیام با موفقیت ثبت شد.");
    }

    public async Task<OperationResult> UpdateMessageAsync(Message message)
    {
        if (message is null)
            return OperationResult.Error("پیام نامعتبر است.");

        var entity = await context.Messages.AsTracking().FirstOrDefaultAsync(x => x.Id == message.Id);
        if (entity is null)
            return OperationResult.NotFound("پیام مورد نظر یافت نشد.");

        if (string.IsNullOrWhiteSpace(message.Content))
            return OperationResult.Error("متن پیام را وارد کنید.");

        entity.UserId = message.UserId;
        entity.Type = message.Type;
        entity.FullName = message.FullName;
        entity.PhoneNumber = message.PhoneNumber;
        entity.Email = message.Email;
        entity.Subject = message.Subject;
        entity.Content = message.Content;
        entity.Status = message.Status;
        entity.IsRead = message.IsRead;
        entity.ReadAt = message.ReadAt;
        entity.RepliedAt = message.RepliedAt;

        await context.SaveChangesAsync();
        return OperationResult.Success("پیام با موفقیت ویرایش شد.");
    }

    public async Task<OperationResult> DeleteMessageAsync(Guid id)
    {
        var entity = await context.Messages.AsTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (entity is null)
            return OperationResult.NotFound("پیام مورد نظر یافت نشد.");

        context.Messages.Remove(entity);
        await context.SaveChangesAsync();

        return OperationResult.Success("پیام با موفقیت حذف شد.");
    }

    public async Task<OperationResult> MarkAsReadAsync(Guid id)
    {
        var entity = await context.Messages.AsTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (entity is null)
            return OperationResult.NotFound("پیام مورد نظر یافت نشد.");

        entity.IsRead = true;
        entity.ReadAt ??= DateTimeOffset.UtcNow;

        await context.SaveChangesAsync();
        return OperationResult.Success("پیام به‌عنوان خوانده‌شده علامت‌گذاری شد.");
    }

    public async Task<Message?> GetMessageByIdAsync(Guid id)
    {
        return await context.Messages
            .Include(x => x.User)
            .FirstOrDefaultAsync(x => x.Id == id);
    }

    public async Task<List<Message>> GetMessagesByUserAsync(Guid userId)
    {
        return await context.Messages
            .Where(x => x.UserId == userId)
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync();
    }

    public async Task<List<Message>> GetMessagesAsync(MessageStatus? status = null, MessageType? type = null)
    {
        var query = context.Messages.AsQueryable();

        if (status.HasValue)
            query = query.Where(x => x.Status == status.Value);

        if (type.HasValue)
            query = query.Where(x => x.Type == type.Value);

        return await query
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync();
    }
}
