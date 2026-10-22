using Fratak_Shop.Core.DTOs.Messages.Query;
using Fratak_Shop.Core.Mappers.Users;
using Fratak_Shop.Data.Entities.Messages;

namespace Fratak_Shop.Core.Mappers.Messages;

public static class MessageMapper
{
    public static MessageDto Map(this Message message)
    {
        return new MessageDto
        {
            Id = message.Id,
            CreatedAt = message.CreatedAt,
            UpdatedAt = message.UpdatedAt,
            IsDeleted = message.IsDeleted,
            DeletedAt = message.DeletedAt,
            UserId = message.UserId,
            UserName = message.User.DisplayName(),
            Type = message.Type,
            Status = message.Status,
            FullName = message.FullName,
            PhoneNumber = message.PhoneNumber,
            Email = message.Email,
            Subject = message.Subject,
            Content = message.Content,
            IsRead = message.IsRead,
            ReadAt = message.ReadAt,
            RepliedAt = message.RepliedAt,
        };
    }

    public static List<MessageDto> Map(this List<Message> messages)
    {
        return messages.Select(message => message.Map()).ToList();
    }

    public static MessageFilterData MapFilterData(this Message message)
    {
        return new MessageFilterData
        {
            Id = message.Id,
            CreatedAt = message.CreatedAt,
            UpdatedAt = message.UpdatedAt,
            IsDeleted = message.IsDeleted,
            DeletedAt = message.DeletedAt,
            UserId = message.UserId,
            UserName = message.User.DisplayName(),
            Type = message.Type,
            Status = message.Status,
            FullName = message.FullName,
            PhoneNumber = message.PhoneNumber,
            Email = message.Email,
            Subject = message.Subject,
            Content = message.Content,
            IsRead = message.IsRead,
            ReadAt = message.ReadAt,
            RepliedAt = message.RepliedAt,
        };
    }

    public static List<MessageFilterData> MapFilterData(this List<Message> messages)
    {
        return messages.Select(message => message.MapFilterData()).ToList();
    }
}
