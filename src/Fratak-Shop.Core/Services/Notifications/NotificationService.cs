using Fratak_Shop.Core.DTOs.Notifications.Query;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Notifications;
using Fratak_Shop.Data.Entities.Users;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Core.Services.Notifications;

public class NotificationService(FratakShopDbContext context) : INotificationService
{
    public async Task NotifyUserAsync(Guid userId, string title, string? message, NotificationType type,
        string? url = null, CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty)
            return;

        context.Notifications.Add(new Notification
        {
            UserId = userId,
            Title = title,
            Message = message,
            Type = type,
            Channel = NotificationChannel.InApp,
            Url = url,
        });

        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task NotifyPhoneAsync(string phoneNumber, string message, NotificationType type,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
            return;

        var userId = await context.Users
            .Where(user => user.PhoneNumber == phoneNumber)
            .Select(user => (Guid?)user.Id)
            .FirstOrDefaultAsync(cancellationToken);

        // Codes are also kept for guests (a customer that has not signed in yet):
        // in that case only the log carries the message, so nothing is stored for
        // a phone number that does not belong to an account.
        context.Notifications.Add(new Notification
        {
            UserId = userId,
            Title = "پیام سیستمی",
            Message = message,
            Type = type,
            Channel = NotificationChannel.InApp,
        });

        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task NotifyStaffAsync(string title, string? message, NotificationType type,
        string? url = null, CancellationToken cancellationToken = default)
    {
        var staffIds = await context.Users
            .Where(user => user.Role != UserRole.Customer && user.Role != UserRole.None)
            .Select(user => user.Id)
            .ToListAsync(cancellationToken);

        foreach (var userId in staffIds)
        {
            context.Notifications.Add(new Notification
            {
                UserId = userId,
                Title = title,
                Message = message,
                Type = type,
                Url = url,
                Channel = NotificationChannel.InApp,
            });
        }

        if (staffIds.Count > 0)
            await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<OperationResult> BroadcastAsync(string title, string? message, string? url, NotificationType type,
        Guid? createdByUserId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(title))
            return OperationResult.Error("عنوان اعلان را وارد کنید.");

        var customerCount = await context.Users
            .CountAsync(user => user.Role == UserRole.Customer, cancellationToken);

        // Store one broadcast row. Creating one row per customer as well would
        // make every user see the same notification twice and would scale poorly.
        context.Notifications.Add(new Notification
        {
            Title = title,
            Message = message,
            Url = url,
            Type = type,
            Channel = NotificationChannel.InApp,
            IsBroadcast = true,
            CreatedByUserId = createdByUserId,
        });

        await context.SaveChangesAsync(cancellationToken);

        return OperationResult.Success($"اعلان برای {PriceHelper.ToPersianDigits(customerCount.ToString())} مشتری ارسال شد.");
    }

    public async Task<List<NotificationDto>> GetForUserAsync(Guid userId, int take = 20, bool unreadOnly = false)
    {
        var query = context.Notifications
            .Where(notification => notification.UserId == userId || notification.IsBroadcast);

        if (unreadOnly)
            query = query.Where(notification => !notification.IsRead);

        return await query
            .OrderByDescending(notification => notification.CreatedAt)
            .Take(take)
            .Select(notification => Map(notification))
            .ToListAsync();
    }

    public async Task<int> GetUnreadCountAsync(Guid userId) =>
        await context.Notifications.CountAsync(notification =>
            (notification.UserId == userId || notification.IsBroadcast) && !notification.IsRead);

    public async Task<OperationResult> MarkAsReadAsync(Guid notificationId, Guid? userId)
    {
        var notification = await context.Notifications
            .AsTracking()
            .FirstOrDefaultAsync(item => item.Id == notificationId &&
                                         (userId == null || item.UserId == userId || item.IsBroadcast));

        if (notification is null)
            return OperationResult.NotFound("اعلان مورد نظر یافت نشد.");

        if (!notification.IsRead)
        {
            notification.IsRead = true;
            notification.ReadAt = DateTimeOffset.UtcNow;
            await context.SaveChangesAsync();
        }

        return OperationResult.Success("اعلان خوانده شد.");
    }

    public async Task<OperationResult> MarkAllAsReadAsync(Guid userId)
    {
        var notifications = await context.Notifications
            .AsTracking()
            .Where(notification => (notification.UserId == userId || notification.IsBroadcast) && !notification.IsRead)
            .ToListAsync();

        if (notifications.Count == 0)
            return OperationResult.Success("اعلان خوانده‌نشده‌ای وجود ندارد.");

        var now = DateTimeOffset.UtcNow;
        foreach (var notification in notifications)
        {
            notification.IsRead = true;
            notification.ReadAt = now;
        }

        await context.SaveChangesAsync();
        return OperationResult.Success("همه اعلان‌ها خوانده شدند.");
    }

    public async Task<NotificationFilterResult> GetFilterAsync(NotificationFilterParams filterParams)
    {
        var query = context.Notifications
            .Include(notification => notification.User)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(filterParams.Search))
        {
            var search = filterParams.Search.Trim();
            query = query.Where(notification => notification.Title.Contains(search) ||
                                                (notification.Message != null && notification.Message.Contains(search)));
        }

        if (filterParams.Type.HasValue)
            query = query.Where(notification => notification.Type == filterParams.Type.Value);

        if (filterParams.IsRead.HasValue)
            query = query.Where(notification => notification.IsRead == filterParams.IsRead.Value);

        if (filterParams.IsBroadcast.HasValue)
            query = query.Where(notification => notification.IsBroadcast == filterParams.IsBroadcast.Value);

        var total = await query.CountAsync();
        var skip = (filterParams.PageId - 1) * filterParams.Take;

        var items = await query
            .OrderByDescending(notification => notification.CreatedAt)
            .Skip(skip)
            .Take(filterParams.Take)
            .ToListAsync();

        var result = new NotificationFilterResult
        {
            Data = items.Select(Map).ToList(),
            FilterParams = filterParams,
        };
        result.GeneratePaging(total, filterParams.Take, filterParams.PageId);

        return result;
    }

    public async Task<OperationResult> DeleteAsync(Guid id)
    {
        var notification = await context.Notifications.AsTracking().FirstOrDefaultAsync(item => item.Id == id);
        if (notification is null)
            return OperationResult.NotFound("اعلان مورد نظر یافت نشد.");

        context.Notifications.Remove(notification);
        await context.SaveChangesAsync();

        return OperationResult.Success("اعلان حذف شد.");
    }

    private static NotificationDto Map(Notification notification) => new()
    {
        Id = notification.Id,
        UserId = notification.UserId,
        Title = notification.Title,
        Message = notification.Message,
        Type = notification.Type,
        Channel = notification.Channel,
        Url = notification.Url,
        IsRead = notification.IsRead,
        ReadAt = notification.ReadAt,
        IsBroadcast = notification.IsBroadcast,
        CreatedAt = notification.CreatedAt,
        UpdatedAt = notification.UpdatedAt,
        UserFullName = notification.User is null
            ? null
            : $"{notification.User.FirstName} {notification.User.LastName}".Trim(),
        UserPhoneNumber = notification.User?.PhoneNumber,
    };
}
