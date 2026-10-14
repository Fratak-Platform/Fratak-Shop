using Fratak_Shop.Core.DTOs.Messages.Command;
using Fratak_Shop.Core.DTOs.Messages.Query;
using Fratak_Shop.Core.Mappers.Messages;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Messages;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Core.Services.Messages;

/// <summary>
/// مدیریت پیام‌های دریافتی (تماس با ما، پشتیبانی، پیشنهاد و شکایت) و گردش
/// وضعیت پیگیری آن‌ها از «جدید» تا «پاسخ داده‌شده» و «بسته‌شده».
/// </summary>
public class MessageService(FratakShopDbContext context) : IMessageService
{
    public async Task<OperationResult> CreateMessageAsync(CreateMessageCommand command)
    {
        var error = Validate(command.Type, command.Content, command.Email, command.PhoneNumber,
            command.FullName, isContactFormRequired: true);

        if (error is not null)
            return OperationResult.Error(error);

        if (command.UserId.HasValue)
        {
            var userExists = await context.Users.AnyAsync(x => x.Id == command.UserId.Value);
            if (!userExists)
                return OperationResult.NotFound("کاربر مورد نظر یافت نشد.");
        }

        var message = new Message
        {
            UserId = command.UserId,
            Type = command.Type,
            FullName = command.FullName?.Trim(),
            PhoneNumber = command.PhoneNumber?.Trim(),
            Email = command.Email?.Trim(),
            Subject = command.Subject?.Trim(),
            Content = command.Content.Trim(),
            Status = MessageStatus.New,
            IsRead = false,
        };

        context.Messages.Add(message);
        await context.SaveChangesAsync();

        return OperationResult.Success("پیام شما با موفقیت ثبت شد و در اسرع وقت بررسی می‌شود.");
    }

    public async Task<OperationResult> EditMessageAsync(EditMessageCommand command)
    {
        var message = await context.Messages.AsTracking().FirstOrDefaultAsync(x => x.Id == command.Id);
        if (message is null)
            return OperationResult.NotFound("پیام مورد نظر یافت نشد.");

        var error = Validate(command.Type, command.Content, command.Email, command.PhoneNumber,
            command.FullName, isContactFormRequired: false);

        if (error is not null)
            return OperationResult.Error(error);

        if (command.Status == MessageStatus.None)
            return OperationResult.Error("وضعیت پیگیری پیام را انتخاب کنید.");

        message.Type = command.Type;
        message.FullName = command.FullName?.Trim();
        message.PhoneNumber = command.PhoneNumber?.Trim();
        message.Email = command.Email?.Trim();
        message.Subject = command.Subject?.Trim();
        message.Content = command.Content.Trim();
        message.Status = command.Status;

        if (command.Status == MessageStatus.Answered)
            message.RepliedAt ??= DateTimeOffset.UtcNow;

        await context.SaveChangesAsync();

        return OperationResult.Success("پیام با موفقیت ویرایش شد.");
    }

    public async Task<OperationResult> DeleteMessageAsync(Guid id)
    {
        var message = await context.Messages.AsTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (message is null)
            return OperationResult.NotFound("پیام مورد نظر یافت نشد.");

        context.Messages.Remove(message);
        await context.SaveChangesAsync();

        return OperationResult.Success("پیام با موفقیت حذف شد.");
    }

    public async Task<OperationResult> MarkAsReadAsync(Guid id)
    {
        var message = await context.Messages.AsTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (message is null)
            return OperationResult.NotFound("پیام مورد نظر یافت نشد.");

        message.IsRead = true;
        message.ReadAt ??= DateTimeOffset.UtcNow;

        // پیام جدید با مشاهده مدیر وارد مرحله بررسی می‌شود.
        if (message.Status == MessageStatus.New)
            message.Status = MessageStatus.InProgress;

        await context.SaveChangesAsync();

        return OperationResult.Success("پیام به‌عنوان خوانده‌شده علامت‌گذاری شد.");
    }

    public async Task<OperationResult> ReplyMessageAsync(Guid id)
    {
        var message = await context.Messages.AsTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (message is null)
            return OperationResult.NotFound("پیام مورد نظر یافت نشد.");

        message.IsRead = true;
        message.ReadAt ??= DateTimeOffset.UtcNow;
        message.Status = MessageStatus.Answered;
        message.RepliedAt = DateTimeOffset.UtcNow;

        await context.SaveChangesAsync();

        return OperationResult.Success("پاسخ‌گویی به پیام ثبت شد.");
    }

    public async Task<OperationResult> ChangeMessageStatusAsync(Guid id, MessageStatus status)
    {
        if (status == MessageStatus.None)
            return OperationResult.Error("وضعیت پیگیری پیام را انتخاب کنید.");

        var message = await context.Messages.AsTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (message is null)
            return OperationResult.NotFound("پیام مورد نظر یافت نشد.");

        message.Status = status;

        if (status == MessageStatus.Answered)
            message.RepliedAt ??= DateTimeOffset.UtcNow;

        await context.SaveChangesAsync();

        return OperationResult.Success("وضعیت پیام با موفقیت تغییر کرد.");
    }

    public async Task<MessageFilterResult> GetMessagesByFilterAsync(MessageFilterParams filterParams)
    {
        var query = context.Messages
            .Include(x => x.User)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(filterParams.Search))
        {
            var search = filterParams.Search.Trim();
            query = query.Where(x => (x.FullName != null && x.FullName.Contains(search))
                                     || (x.PhoneNumber != null && x.PhoneNumber.Contains(search))
                                     || (x.Email != null && x.Email.Contains(search))
                                     || (x.Subject != null && x.Subject.Contains(search)));
        }

        if (filterParams.MessageType.HasValue)
            query = query.Where(x => x.Type == filterParams.MessageType.Value);

        if (filterParams.Status.HasValue)
            query = query.Where(x => x.Status == filterParams.Status.Value);

        if (filterParams.IsRead.HasValue)
            query = query.Where(x => x.IsRead == filterParams.IsRead.Value);

        if (filterParams.UserId.HasValue)
            query = query.Where(x => x.UserId == filterParams.UserId.Value);

        query = query.OrderByDescending(x => x.CreatedAt);

        var skip = (filterParams.PageId - 1) * filterParams.Take;

        var messages = await query
            .Skip(skip)
            .Take(filterParams.Take)
            .ToListAsync();

        var result = new MessageFilterResult
        {
            Data = messages.MapFilterData(),
            FilterParams = filterParams,
        };

        result.GeneratePaging(query, filterParams.Take, filterParams.PageId);

        return result;
    }

    public async Task<MessageDto?> GetMessageByIdAsync(Guid id)
    {
        var message = await context.Messages
            .Include(x => x.User)
            .FirstOrDefaultAsync(x => x.Id == id);

        return message?.Map();
    }

    public async Task<List<MessageFilterData>> GetMessagesByUserAsync(Guid userId)
    {
        var messages = await context.Messages
            .Where(x => x.UserId == userId)
            .Include(x => x.User)
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync();

        return messages.MapFilterData();
    }

    public async Task<List<MessageFilterData>> GetMessagesAsync(MessageStatus? status = null, MessageType? type = null)
    {
        var query = context.Messages
            .Include(x => x.User)
            .AsQueryable();

        if (status.HasValue)
            query = query.Where(x => x.Status == status.Value);

        if (type.HasValue)
            query = query.Where(x => x.Type == type.Value);

        var messages = await query
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync();

        return messages.MapFilterData();
    }

    private static string? Validate(MessageType type, string? content, string? email, string? phoneNumber,
        string? fullName, bool isContactFormRequired)
    {
        if (type == MessageType.None)
            return "نوع پیام را انتخاب کنید.";

        if (string.IsNullOrWhiteSpace(content))
            return "متن پیام را وارد کنید.";

        if (!string.IsNullOrWhiteSpace(email) && !email.IsValidEmail())
            return "ایمیل وارد‌شده معتبر نیست.";

        if (isContactFormRequired && type == MessageType.Contact)
        {
            if (string.IsNullOrWhiteSpace(fullName))
                return "نام و نام خانوادگی را وارد کنید.";

            if (string.IsNullOrWhiteSpace(phoneNumber) && string.IsNullOrWhiteSpace(email))
                return "برای پاسخ‌گویی، شماره تماس یا ایمیل خود را وارد کنید.";
        }

        return null;
    }
}
