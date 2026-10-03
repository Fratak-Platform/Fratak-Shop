using Fratak_Shop.Core.DTOs.Pages.Query;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Pages;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Core.Services.Pages;

public class PageAlertService(FratakShopDbContext context) : IPageAlertService
{
    public async Task<List<PageAlertDto>> GetAllAsync(SitePage? page = null)
    {
        var query = context.PageAlerts.AsQueryable();
        if (page.HasValue)
            query = query.Where(alert => alert.Page == page.Value);

        var alerts = await query
            .OrderBy(alert => alert.Page)
            .ThenBy(alert => alert.DisplayOrder)
            .ToListAsync();

        return alerts.Select(Map).ToList();
    }

    public async Task<List<PageAlertDto>> GetVisibleAsync(SitePage page, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;

        var alerts = await context.PageAlerts
            .Where(alert => alert.Page == page &&
                            alert.IsActive &&
                            (alert.StartAt == null || alert.StartAt <= now) &&
                            (alert.EndAt == null || alert.EndAt >= now))
            .OrderBy(alert => alert.DisplayOrder)
            .ToListAsync(cancellationToken);

        return alerts.Select(Map).ToList();
    }

    public async Task<PageAlertDto?> GetAsync(Guid id)
    {
        var alert = await context.PageAlerts.FirstOrDefaultAsync(item => item.Id == id);
        return alert is null ? null : Map(alert);
    }

    public async Task<OperationResult> SaveAsync(PageAlertDto model)
    {
        if (model.Page == SitePage.None)
            return OperationResult.Error("صفحه مورد نظر را انتخاب کنید.");

        if (string.IsNullOrWhiteSpace(model.Content))
            return OperationResult.Error("متن هشدار را وارد کنید.");

        if (model.StartAt.HasValue && model.EndAt.HasValue && model.StartAt > model.EndAt)
            return OperationResult.Error("تاریخ پایان باید بعد از تاریخ شروع باشد.");

        var content = model.Content.SanitizeText();

        if (model.Id != Guid.Empty)
        {
            var alert = await context.PageAlerts.AsTracking().FirstOrDefaultAsync(item => item.Id == model.Id);
            if (alert is null)
                return OperationResult.NotFound("هشدار مورد نظر یافت نشد.");

            alert.Page = model.Page;
            alert.Position = model.Position;
            alert.Type = model.Type;
            alert.Title = model.Title;
            alert.Content = content;
            alert.DisplayOrder = model.DisplayOrder;
            alert.StartAt = model.StartAt;
            alert.EndAt = model.EndAt;
            alert.IsActive = model.IsActive;

            await context.SaveChangesAsync();
            return OperationResult.Success("هشدار ویرایش شد.");
        }

        context.PageAlerts.Add(new PageAlert
        {
            Page = model.Page,
            Position = model.Position,
            Type = model.Type,
            Title = model.Title,
            Content = content,
            DisplayOrder = model.DisplayOrder,
            StartAt = model.StartAt,
            EndAt = model.EndAt,
            IsActive = model.IsActive,
        });

        await context.SaveChangesAsync();
        return OperationResult.Success("هشدار ثبت شد.");
    }

    public async Task<OperationResult> DeleteAsync(Guid id)
    {
        var alert = await context.PageAlerts.AsTracking().FirstOrDefaultAsync(item => item.Id == id);
        if (alert is null)
            return OperationResult.NotFound("هشدار مورد نظر یافت نشد.");

        context.PageAlerts.Remove(alert);
        await context.SaveChangesAsync();

        return OperationResult.Success("هشدار حذف شد.");
    }

    public async Task<OperationResult> ToggleActiveAsync(Guid id)
    {
        var alert = await context.PageAlerts.AsTracking().FirstOrDefaultAsync(item => item.Id == id);
        if (alert is null)
            return OperationResult.NotFound("هشدار مورد نظر یافت نشد.");

        alert.IsActive = !alert.IsActive;
        await context.SaveChangesAsync();

        return OperationResult.Success(alert.IsActive ? "هشدار فعال شد." : "هشدار غیرفعال شد.");
    }

    private static PageAlertDto Map(PageAlert alert) => new()
    {
        Id = alert.Id,
        CreatedAt = alert.CreatedAt,
        UpdatedAt = alert.UpdatedAt,
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
