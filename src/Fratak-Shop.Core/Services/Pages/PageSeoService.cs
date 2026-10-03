using Fratak_Shop.Core.DTOs.Pages.Query;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Pages;
using Fratak_Shop.Data.Utilities.SeoManagement;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Core.Services.Pages;

public class PageSeoService(FratakShopDbContext context) : IPageSeoService
{
    public async Task<List<PageSeoDto>> GetAllAsync()
    {
        var records = await context.PageSeos
            .OrderBy(record => record.Page)
            .ToListAsync();

        return records.Select(record => new PageSeoDto
        {
            Id = record.Id,
            CreatedAt = record.CreatedAt,
            UpdatedAt = record.UpdatedAt,
            Page = record.Page,
            Seo = record.Seo,
        }).ToList();
    }

    public async Task<PageSeoDto?> GetAsync(SitePage page)
    {
        var record = await context.PageSeos.FirstOrDefaultAsync(item => item.Page == page);
        if (record is null)
            return new PageSeoDto { Page = page };

        return new PageSeoDto
        {
            Id = record.Id,
            CreatedAt = record.CreatedAt,
            UpdatedAt = record.UpdatedAt,
            Page = record.Page,
            Seo = record.Seo,
        };
    }

    public async Task<OperationResult> SaveAsync(SitePage page, SeoMetaData seo, string? slug = null)
    {
        if (page == SitePage.None)
            return OperationResult.Error("صفحه مورد نظر را انتخاب کنید.");

        var record = await context.PageSeos.AsTracking().FirstOrDefaultAsync(item => item.Page == page);
        if (record is null)
        {
            record = new PageSeo { Page = page };
            context.PageSeos.Add(record);
        }

        record.Seo = seo;
        await context.SaveChangesAsync();

        return OperationResult.Success("اطلاعات سئوی صفحه ذخیره شد.");
    }

    public async Task<OperationResult> MarkReviewedAsync(SitePage page, Guid? userId)
    {
        var record = await context.PageSeos.AsTracking().FirstOrDefaultAsync(item => item.Page == page);
        if (record is null)
            return OperationResult.NotFound("رکورد سئوی این صفحه یافت نشد.");

        record.Seo ??= new SeoMetaData();
        record.Seo.IsReviewed = true;
        record.Seo.ReviewedAt = DateTimeOffset.UtcNow;
        record.Seo.ReviewedByUserId = userId;
        record.Seo.LastAuditedAt = DateTimeOffset.UtcNow;

        await context.SaveChangesAsync();
        return OperationResult.Success("سئوی صفحه به‌عنوان بررسی‌شده علامت‌گذاری شد.");
    }

    public async Task EnsureDefaultsAsync(CancellationToken cancellationToken = default)
    {
        var existing = await context.PageSeos
            .Select(record => record.Page)
            .ToListAsync(cancellationToken);

        var missing = Enum.GetValues<SitePage>()
            .Where(page => page != SitePage.None && !existing.Contains(page))
            .Select(page => new PageSeo { Page = page })
            .ToList();

        if (missing.Count == 0)
            return;

        context.PageSeos.AddRange(missing);
        await context.SaveChangesAsync(cancellationToken);
    }
}
