using Fratak_Shop.Core.DTOs.Pages.Query;
using Fratak_Shop.Data.Entities.Pages;

namespace Fratak_Shop.Core.Mappers.Pages;

public static class PageAlertMapper
{
    public static PageAlertDto Map(this PageAlert alert)
    {
        return new PageAlertDto
        {
            Id = alert.Id,
            CreatedAt = alert.CreatedAt,
            UpdatedAt = alert.UpdatedAt,
            IsDeleted = alert.IsDeleted,
            DeletedAt = alert.DeletedAt,
            Page = alert.Page,
            Position = alert.Position,
            Type = alert.Type,
            Title = alert.Title,
            Content = alert.Content,
            DisplayOrder = alert.DisplayOrder,
            StartAt = alert.StartAt,
            EndAt = alert.EndAt,
            IsActive = alert.IsActive,
        };
    }

    public static List<PageAlertDto> Map(this List<PageAlert> alerts)
    {
        return alerts.Select(alert => alert.Map()).ToList();
    }
}
