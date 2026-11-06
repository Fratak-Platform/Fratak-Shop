using Fratak_Shop.Core.DTOs.Engagement;
using Fratak_Shop.Core.DTOs.Notifications;
using Fratak_Shop.Core.Services.Notifications;
using Fratak_Shop.Core.Services.Settings;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Notifications;
using Fratak_Shop.Data.Entities.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Fratak_Shop.Core.Services.Support;

/// <summary>
/// Support tickets of the customer profile: departments, conversation with attachments (stored
/// privately, served only to the owner), statuses and unread markers. The staff side
/// (<see cref="StaffReplyAsync"/>, <see cref="SetStatusAsync"/>) is used by the admin panel later.
/// </summary>
public interface ITicketService
{
    Task<List<SupportDepartmentDto>> GetDepartmentsAsync();

    Task<OperationResult<string>> CreateAsync(CreateTicketCommand command);

    Task<OperationResult> ReplyAsync(Guid userId, string ticketNumber, string? content, List<TicketAttachmentInput> attachments);

    Task<OperationResult> CloseAsync(Guid userId, string ticketNumber);

    Task<List<TicketSummaryDto>> GetUserTicketsAsync(Guid userId);

    /// <summary>Conversation of the customer's ticket; marks staff replies as read.</summary>
    Task<TicketDetailDto?> GetUserTicketAsync(Guid userId, string ticketNumber);

    Task<int> GetUnreadCountAsync(Guid userId);

    /// <summary>True when the attachment belongs to a ticket of this customer.</summary>
    Task<TicketAttachmentDto?> GetUserAttachmentAsync(Guid userId, string fileName);

    // ── Staff (admin panel) ───────────────────────────────────────────
    Task<OperationResult> StaffReplyAsync(Guid staffUserId, Guid ticketId, string content, List<TicketAttachmentInput> attachments);

    Task<OperationResult> SetStatusAsync(Guid ticketId, TicketStatus status);
}

public class TicketService(
    FratakShopDbContext context,
    IStoreSettingService settingService,
    INotificationService notifications,
    IMemoryCache cache) : ITicketService
{
    public async Task<List<SupportDepartmentDto>> GetDepartmentsAsync() =>
        await cache.GetOrCreateAsync(CacheInvalidation.SupportDepartmentsKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheKeys.DefaultLifetime;
            return await context.SupportDepartments
                .Where(d => d.IsActive)
                .OrderBy(d => d.DisplayOrder)
                .Select(d => new SupportDepartmentDto(d.Id, d.Title, d.Description))
                .ToListAsync();
        }) ?? [];

    public async Task<OperationResult<string>> CreateAsync(CreateTicketCommand command)
    {
        var settings = await settingService.GetAsync();
        if (!settings.IsTicketEnabled)
            return OperationResult<string>.Error("ثبت تیکت در حال حاضر فعال نیست؛ از صفحه تماس با ما استفاده کنید.");

        var subject = command.Subject?.Trim();
        var content = command.Content?.Trim();
        if (string.IsNullOrEmpty(subject) || subject.Length < 3 || subject.Length > 200)
            return OperationResult<string>.Error("موضوع تیکت را بنویسید (۳ تا ۲۰۰ کاراکتر).");
        if (string.IsNullOrEmpty(content) || content.Length < 10 || content.Length > 4000)
            return OperationResult<string>.Error("متن تیکت را بنویسید (۱۰ تا ۴۰۰۰ کاراکتر).");

        var attachmentError = ValidateAttachments(command.Attachments, settings.TicketMaxAttachments);
        if (attachmentError is not null)
            return OperationResult<string>.Error(attachmentError);

        if (!(await GetDepartmentsAsync()).Any(d => d.Id == command.DepartmentId))
            return OperationResult<string>.Error("واحد پشتیبانی را انتخاب کنید.");

        var open = await context.SupportTickets.CountAsync(t => t.UserId == command.UserId && t.Status != TicketStatus.Closed);
        if (open >= settings.TicketMaxOpenPerUser)
            return OperationResult<string>.Error($"حداکثر {settings.TicketMaxOpenPerUser} تیکت باز مجاز است؛ لطفاً در تیکت‌های فعلی پیگیری کنید.");

        Guid? orderId = null;
        if (!string.IsNullOrWhiteSpace(command.OrderNumber))
        {
            var number = command.OrderNumber.Trim().ToUpperInvariant();
            orderId = await context.Orders.Where(o => o.OrderNumber == number && o.UserId == command.UserId).Select(o => (Guid?)o.Id).FirstOrDefaultAsync();
            if (orderId is null)
                return OperationResult<string>.Error("سفارش انتخاب‌شده متعلق به شما نیست.");
        }

        var author = await AuthorNameAsync(command.UserId);
        var now = DateTimeOffset.UtcNow;
        var ticket = new SupportTicket
        {
            TicketNumber = await NewNumberAsync(),
            UserId = command.UserId,
            DepartmentId = command.DepartmentId,
            OrderId = orderId,
            Subject = subject,
            Priority = Enum.IsDefined(command.Priority) ? command.Priority : TicketPriority.Normal,
            Status = TicketStatus.Open,
            LastActivityAt = now,
        };
        ticket.Messages.Add(NewMessage(command.UserId, false, author, content, command.Attachments));

        context.SupportTickets.Add(ticket);
        await context.SaveChangesAsync();

        await notifications.NotifyAsync(new NotificationRequest
        {
            UserId = command.UserId,
            Event = NotificationEvent.TicketCreated,
            Category = NotificationCategory.Support,
            LinkUrl = "/profile/tickets/" + ticket.TicketNumber.ToLowerInvariant(),
            Values = { ["TicketNumber"] = ticket.TicketNumber, ["Subject"] = subject },
        });

        return OperationResult<string>.Success(ticket.TicketNumber, $"تیکت {ticket.TicketNumber} ثبت شد؛ پاسخ پشتیبانی به شما اطلاع داده می‌شود.");
    }

    public async Task<OperationResult> ReplyAsync(Guid userId, string ticketNumber, string? content, List<TicketAttachmentInput> attachments)
    {
        var settings = await settingService.GetAsync();
        var text = content?.Trim();
        if (string.IsNullOrEmpty(text) || text.Length < 2 || text.Length > 4000)
            return OperationResult.Error("متن پیام را بنویسید (حداکثر ۴۰۰۰ کاراکتر).");

        var attachmentError = ValidateAttachments(attachments, settings.TicketMaxAttachments);
        if (attachmentError is not null)
            return OperationResult.Error(attachmentError);

        var number = (ticketNumber ?? string.Empty).Trim().ToUpperInvariant();
        var ticket = await context.SupportTickets.AsTracking().FirstOrDefaultAsync(t => t.TicketNumber == number && t.UserId == userId);
        if (ticket is null)
            return OperationResult.NotFound("تیکت مورد نظر یافت نشد.");

        var now = DateTimeOffset.UtcNow;
        if (ticket.Status == TicketStatus.Closed && ticket.LastActivityAt < now.AddDays(-30))
            return OperationResult.Error("این تیکت بسته شده است؛ لطفاً تیکت جدیدی ثبت کنید.");

        // Flooding guard: at most 10 customer messages per ticket and hour.
        var recent = await context.SupportTicketMessages.CountAsync(m => m.TicketId == ticket.Id && !m.IsStaff && m.CreatedAt > now.AddHours(-1));
        if (recent >= 10)
            return OperationResult.Error("تعداد پیام‌های شما در این تیکت زیاد است؛ لطفاً منتظر پاسخ پشتیبانی بمانید.");

        context.SupportTicketMessages.Add(NewMessage(userId, false, await AuthorNameAsync(userId), text, attachments, ticket.Id));
        ticket.Status = TicketStatus.CustomerReplied;
        ticket.ClosedAt = null;
        ticket.LastActivityAt = now;
        await context.SaveChangesAsync();

        return OperationResult.Success("پیام شما ثبت شد.");
    }

    public async Task<OperationResult> CloseAsync(Guid userId, string ticketNumber)
    {
        var number = (ticketNumber ?? string.Empty).Trim().ToUpperInvariant();
        var now = DateTimeOffset.UtcNow;
        var affected = await context.SupportTickets
            .Where(t => t.TicketNumber == number && t.UserId == userId && t.Status != TicketStatus.Closed)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.Status, TicketStatus.Closed)
                .SetProperty(t => t.ClosedAt, now)
                .SetProperty(t => t.HasUnreadReply, false)
                .SetProperty(t => t.UpdatedAt, now));

        return affected == 0
            ? OperationResult.Error("این تیکت قبلاً بسته شده است.")
            : OperationResult.Success("تیکت بسته شد. در صورت نیاز می‌توانید تا ۳۰ روز دوباره در آن پیام دهید.");
    }

    public async Task<List<TicketSummaryDto>> GetUserTicketsAsync(Guid userId) =>
        await Summaries(context.SupportTickets.Where(t => t.UserId == userId))
            .OrderByDescending(t => t.Status != TicketStatus.Closed)
            .ThenByDescending(t => t.LastActivityAt)
            .Take(200)
            .ToListAsync();

    public async Task<TicketDetailDto?> GetUserTicketAsync(Guid userId, string ticketNumber)
    {
        var number = (ticketNumber ?? string.Empty).Trim().ToUpperInvariant();
        var summary = await Summaries(context.SupportTickets.Where(t => t.TicketNumber == number && t.UserId == userId)).FirstOrDefaultAsync();
        if (summary is null)
            return null;

        var messages = await context.SupportTicketMessages
            .Where(m => m.TicketId == summary.Id)
            .OrderBy(m => m.CreatedAt)
            .Select(m => new TicketMessageDto(m.Id, m.IsStaff, m.AuthorName, m.Content, m.CreatedAt,
                m.Attachments.Select(a => new TicketAttachmentDto(a.FileName, a.ContentType, a.SizeBytes)).ToList()))
            .ToListAsync();

        if (summary.HasUnreadReply)
        {
            await context.SupportTickets
                .Where(t => t.Id == summary.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.HasUnreadReply, false));
            cache.Remove(UnreadKey(userId));
        }

        return new TicketDetailDto { Ticket = summary, Messages = messages };
    }

    public async Task<int> GetUnreadCountAsync(Guid userId) =>
        await cache.GetOrCreateAsync(UnreadKey(userId), async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1);
            return await context.SupportTickets.CountAsync(t => t.UserId == userId && t.HasUnreadReply);
        });

    public async Task<TicketAttachmentDto?> GetUserAttachmentAsync(Guid userId, string fileName) =>
        await context.SupportTicketAttachments
            .Where(a => a.FileName == fileName && a.Message.Ticket.UserId == userId)
            .Select(a => new TicketAttachmentDto(a.FileName, a.ContentType, a.SizeBytes))
            .FirstOrDefaultAsync();

    public async Task<OperationResult> StaffReplyAsync(Guid staffUserId, Guid ticketId, string content, List<TicketAttachmentInput> attachments)
    {
        var text = content?.Trim();
        if (string.IsNullOrEmpty(text) || text.Length > 4000)
            return OperationResult.Error("متن پاسخ را بنویسید (حداکثر ۴۰۰۰ کاراکتر).");

        var ticket = await context.SupportTickets.AsTracking().FirstOrDefaultAsync(t => t.Id == ticketId);
        if (ticket is null)
            return OperationResult.NotFound("تیکت یافت نشد.");

        var settings = await settingService.GetAsync();
        context.SupportTicketMessages.Add(NewMessage(staffUserId, true, $"پشتیبانی {settings.SiteName}", text, attachments, ticket.Id));
        ticket.Status = TicketStatus.Answered;
        ticket.HasUnreadReply = true;
        ticket.ClosedAt = null;
        ticket.LastActivityAt = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync();
        cache.Remove(UnreadKey(ticket.UserId));

        await notifications.NotifyAsync(new NotificationRequest
        {
            UserId = ticket.UserId,
            Event = NotificationEvent.TicketReplied,
            Category = NotificationCategory.Support,
            LinkUrl = "/profile/tickets/" + ticket.TicketNumber.ToLowerInvariant(),
            Values = { ["TicketNumber"] = ticket.TicketNumber, ["Subject"] = ticket.Subject },
        });

        return OperationResult.Success("پاسخ ثبت و به مشتری اطلاع داده شد.");
    }

    public async Task<OperationResult> SetStatusAsync(Guid ticketId, TicketStatus status)
    {
        if (!Enum.IsDefined(status) || status == TicketStatus.None)
            return OperationResult.Error("وضعیت معتبر نیست.");

        var now = DateTimeOffset.UtcNow;
        var affected = await context.SupportTickets
            .Where(t => t.Id == ticketId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.Status, status)
                .SetProperty(t => t.ClosedAt, status == TicketStatus.Closed ? (DateTimeOffset?)now : null)
                .SetProperty(t => t.UpdatedAt, now));

        return affected == 0 ? OperationResult.NotFound("تیکت یافت نشد.") : OperationResult.Success("وضعیت تیکت تغییر کرد.");
    }

    private static SupportTicketMessage NewMessage(Guid userId, bool isStaff, string author, string content, List<TicketAttachmentInput> attachments, Guid? ticketId = null)
    {
        var message = new SupportTicketMessage
        {
            UserId = userId,
            IsStaff = isStaff,
            AuthorName = author.Length > 100 ? author[..100] : author,
            Content = content,
            Attachments = attachments.Select(a => new SupportTicketAttachment
            {
                FileName = a.FileName,
                ContentType = a.ContentType,
                SizeBytes = a.SizeBytes,
            }).ToList(),
        };
        if (ticketId.HasValue)
            message.TicketId = ticketId.Value;
        return message;
    }

    private static string? ValidateAttachments(List<TicketAttachmentInput> attachments, int max)
    {
        if (attachments.Count > max)
            return max == 0 ? "ارسال پیوست فعال نیست." : $"حداکثر {max} پیوست قابل ارسال است.";

        if (attachments.Any(a => string.IsNullOrWhiteSpace(a.FileName) || a.FileName.Length > 100
                                 || a.FileName.Contains('/') || a.FileName.Contains('\\') || a.FileName.Contains("..")))
            return "پیوست ارسالی معتبر نیست.";

        return null;
    }

    private async Task<string> AuthorNameAsync(Guid userId)
    {
        var user = await context.Users.Where(u => u.Id == userId).Select(u => new { u.FirstName, u.LastName }).FirstOrDefaultAsync();
        var name = $"{user?.FirstName} {user?.LastName}".Trim();
        return string.IsNullOrEmpty(name) ? "مشتری" : name;
    }

    private async Task<string> NewNumberAsync()
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var number = "TK-" + System.Security.Cryptography.RandomNumberGenerator.GetInt32(10_000_000, 99_999_999);
            if (!await context.SupportTickets.IgnoreQueryFilters().AnyAsync(t => t.TicketNumber == number))
                return number;
        }
        return "TK-" + Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();
    }

    private static IQueryable<TicketSummaryDto> Summaries(IQueryable<SupportTicket> query) =>
        query.Select(t => new TicketSummaryDto
        {
            Id = t.Id,
            TicketNumber = t.TicketNumber,
            Subject = t.Subject,
            DepartmentTitle = t.Department.Title,
            Status = t.Status,
            Priority = t.Priority,
            HasUnreadReply = t.HasUnreadReply,
            CreatedAt = t.CreatedAt,
            LastActivityAt = t.LastActivityAt,
            OrderNumber = t.Order != null ? t.Order.OrderNumber : null,
        });

    private static string UnreadKey(Guid userId) => $"core:ticket-unread:{userId:N}";
}
