using Fratak_Shop.Core.DTOs.Pages.Command;
using Fratak_Shop.Core.DTOs.Pages.Query;
using Fratak_Shop.Core.Mappers.Pages;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Pages;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Core.Services.Pages;

/// <summary>
/// مدیریت هشدارهای صفحه‌ای: هر هشدار به یک صفحه و یک موقعیت (مثلاً بالای صفحه اصلی یا
/// زیر تصویر محصول) گره می‌خورد و می‌تواند بازه زمانی نمایش داشته باشد.
/// </summary>
public class PageAlertService(FratakShopDbContext context) : IPageAlertService
{
    public async Task<OperationResult> CreateAlertAsync(CreatePageAlertCommand command)
    {
        var error = Validate(command.Page, command.Position, command.Type, command.Content,
            command.StartAt, command.EndAt);

        if (error is not null)
            return OperationResult.Error(error);

        var alert = new PageAlert
        {
            Page = command.Page,
            Position = command.Position,
            Type = command.Type,
            Title = command.Title?.Trim(),
            Content = command.Content.Trim(),
            DisplayOrder = command.DisplayOrder,
            StartAt = command.StartAt,
            EndAt = command.EndAt,
            IsActive = command.IsActive,
        };

        context.PageAlerts.Add(alert);
        await context.SaveChangesAsync();

        return OperationResult.Success("هشدار صفحه با موفقیت ایجاد شد.");
    }

    public async Task<OperationResult> EditAlertAsync(EditPageAlertCommand command)
    {
        var alert = await context.PageAlerts.AsTracking().FirstOrDefaultAsync(x => x.Id == command.Id);
        if (alert is null)
            return OperationResult.NotFound("هشدار صفحه مورد نظر یافت نشد.");

        var error = Validate(command.Page, command.Position, command.Type, command.Content,
            command.StartAt, command.EndAt);

        if (error is not null)
            return OperationResult.Error(error);

        alert.Page = command.Page;
        alert.Position = command.Position;
        alert.Type = command.Type;
        alert.Title = command.Title?.Trim();
        alert.Content = command.Content.Trim();
        alert.DisplayOrder = command.DisplayOrder;
        alert.StartAt = command.StartAt;
        alert.EndAt = command.EndAt;
        alert.IsActive = command.IsActive;

        await context.SaveChangesAsync();

        return OperationResult.Success("هشدار صفحه با موفقیت ویرایش شد.");
    }

    public async Task<OperationResult> DeleteAlertAsync(Guid id)
    {
        var alert = await context.PageAlerts.AsTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (alert is null)
            return OperationResult.NotFound("هشدار صفحه مورد نظر یافت نشد.");

        context.PageAlerts.Remove(alert);
        await context.SaveChangesAsync();

        return OperationResult.Success("هشدار صفحه با موفقیت حذف شد.");
    }

    public async Task<OperationResult> ChangeAlertActivationAsync(Guid id, bool isActive)
    {
        var alert = await context.PageAlerts.AsTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (alert is null)
            return OperationResult.NotFound("هشدار صفحه مورد نظر یافت نشد.");

        alert.IsActive = isActive;
        await context.SaveChangesAsync();

        return OperationResult.Success(isActive
            ? "هشدار صفحه فعال شد."
            : "هشدار صفحه غیرفعال شد.");
    }

    public List<PageAlertDto> GetAllAlertsAsync()
    {
        return context.PageAlerts
            .OrderBy(x => x.Page)
            .ThenBy(x => x.Position)
            .ThenBy(x => x.DisplayOrder)
            .ThenBy(x => x.CreatedAt)
            .ToList()
            .Map();
    }

    public async Task<List<PageAlertDto>> GetAlertsByPageAsync(SitePage page, AlertPosition? position = null)
    {
        var alerts = await BuildPageQuery(page, position)
            .OrderBy(x => x.Position)
            .ThenBy(x => x.DisplayOrder)
            .ThenBy(x => x.CreatedAt)
            .ToListAsync();

        return alerts.Map();
    }

    public async Task<List<PageAlertDto>> GetActiveAlertsAsync(SitePage page, AlertPosition? position = null)
    {
        var now = DateTimeOffset.UtcNow;

        var alerts = await BuildPageQuery(page, position)
            .Where(x => x.IsActive
                        && (!x.StartAt.HasValue || x.StartAt <= now)
                        && (!x.EndAt.HasValue || x.EndAt >= now))
            .OrderBy(x => x.DisplayOrder)
            .ThenBy(x => x.CreatedAt)
            .ToListAsync();

        return alerts.Map();
    }

    public async Task<PageAlertDto?> GetAlertByIdAsync(Guid id)
    {
        var alert = await context.PageAlerts.FirstOrDefaultAsync(x => x.Id == id);
        return alert?.Map();
    }

    private IQueryable<PageAlert> BuildPageQuery(SitePage page, AlertPosition? position)
    {
        var query = context.PageAlerts.Where(x => x.Page == page);

        if (position.HasValue)
            query = query.Where(x => x.Position == position.Value);

        return query;
    }

    private static string? Validate(SitePage page, AlertPosition position, AlertType type,
        string? content, DateTimeOffset? startAt, DateTimeOffset? endAt)
    {
        if (page == SitePage.None)
            return "صفحه مورد نظر را انتخاب کنید.";

        if (position == AlertPosition.None)
            return "موقعیت نمایش هشدار را انتخاب کنید.";

        if (type == AlertType.None)
            return "نوع هشدار را انتخاب کنید.";

        if (string.IsNullOrWhiteSpace(content))
            return "متن هشدار را وارد کنید.";

        if (startAt.HasValue && endAt.HasValue && startAt > endAt)
            return "تاریخ شروع نمایش نباید بعد از تاریخ پایان باشد.";

        return null;
    }
}
