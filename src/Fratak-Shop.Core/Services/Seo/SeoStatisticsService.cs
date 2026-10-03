using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Blogs;
using Fratak_Shop.Data.Entities.Categories;
using Fratak_Shop.Data.Entities.Pages;
using Fratak_Shop.Data.Entities.Seo;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Core.Services.Seo;

public class SeoStatisticsService(FratakShopDbContext context) : ISeoStatisticsService
{
    public async Task RegisterVisitAsync(SeoEntityType entityType, Guid entityId, CancellationToken cancellationToken = default)
    {
        if (entityId == Guid.Empty || entityType == SeoEntityType.None)
            return;

        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var visit = await context.ContentVisits
            .AsTracking()
            .FirstOrDefaultAsync(item => item.EntityType == entityType &&
                                         item.EntityId == entityId &&
                                         item.VisitDate == today, cancellationToken);

        if (visit is null)
        {
            context.ContentVisits.Add(new ContentVisit
            {
                EntityType = entityType,
                EntityId = entityId,
                VisitDate = today,
                Count = 1,
            });
        }
        else
        {
            visit.Count++;
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<List<ContentVisitStat>> GetTopContentAsync(int days = 30, int take = 10,
        CancellationToken cancellationToken = default)
    {
        var from = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-Math.Abs(days)));

        var stats = await context.ContentVisits
            .Where(visit => visit.VisitDate >= from)
            .GroupBy(visit => new { visit.EntityType, visit.EntityId })
            .Select(group => new
            {
                group.Key.EntityType,
                group.Key.EntityId,
                TotalVisits = group.Sum(visit => visit.Count),
                LastVisitAt = group.Max(visit => visit.CreatedAt),
            })
            .OrderByDescending(item => item.TotalVisits)
            .Take(take)
            .ToListAsync(cancellationToken);

        return await FillTitlesAsync(stats.Select(item => (item.EntityType, item.EntityId, item.TotalVisits, (DateTimeOffset?)item.LastVisitAt)).ToList(), cancellationToken);
    }

    public async Task<List<ContentVisitStat>> GetUnvisitedContentAsync(int days = 30, int take = 10,
        CancellationToken cancellationToken = default)
    {
        var from = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-Math.Abs(days)));

        var visitedIds = await context.ContentVisits
            .Where(visit => visit.VisitDate >= from && visit.EntityType == SeoEntityType.Product)
            .Select(visit => visit.EntityId)
            .Distinct()
            .ToListAsync(cancellationToken);

        var products = await context.Products
            .Where(product => product.IsActive && !visitedIds.Contains(product.Id))
            .OrderByDescending(product => product.CreatedAt)
            .Take(take)
            .Select(product => new { product.Id, product.Name, product.Slug, product.CreatedAt })
            .ToListAsync(cancellationToken);

        return products
            .Select(product => new ContentVisitStat
            {
                EntityType = SeoEntityType.Product,
                EntityId = product.Id,
                Title = product.Name,
                Url = $"/product/{product.Slug}",
                TotalVisits = 0,
                LastVisitAt = null,
            })
            .ToList();
    }

    public async Task<List<DailyVisitStat>> GetDailyTrendAsync(int days = 14, CancellationToken cancellationToken = default)
    {
        var from = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-Math.Abs(days)));

        var raw = await context.ContentVisits
            .Where(visit => visit.VisitDate >= from)
            .GroupBy(visit => visit.VisitDate)
            .Select(group => new { Date = group.Key, Visits = group.Sum(visit => visit.Count) })
            .ToListAsync(cancellationToken);

        var result = new List<DailyVisitStat>();
        for (var offset = Math.Abs(days); offset >= 0; offset--)
        {
            var date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-offset));
            var visits = raw.FirstOrDefault(item => item.Date == date)?.Visits ?? 0;
            result.Add(new DailyVisitStat
            {
                Date = date,
                Visits = visits,
                PersianLabel = PriceHelper.ToPersianDigits($"{date.Month}/{date.Day}"),
            });
        }

        return result;
    }

    public async Task<int> GetTotalVisitsAsync(int days = 30, CancellationToken cancellationToken = default)
    {
        var from = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-Math.Abs(days)));
        return await context.ContentVisits
            .Where(visit => visit.VisitDate >= from)
            .SumAsync(visit => (int?)visit.Count, cancellationToken) ?? 0;
    }

    /// <summary>عنوان و آدرس محتوا را از جدول مربوط به همان نوع موجودیت می‌خواند.</summary>
    private async Task<List<ContentVisitStat>> FillTitlesAsync(
        List<(SeoEntityType EntityType, Guid EntityId, int TotalVisits, DateTimeOffset? LastVisitAt)> items,
        CancellationToken cancellationToken)
    {
        var result = new List<ContentVisitStat>();
        var productIds = items.Where(item => item.EntityType == SeoEntityType.Product).Select(item => item.EntityId).ToList();
        var postIds = items.Where(item => item.EntityType == SeoEntityType.BlogPost).Select(item => item.EntityId).ToList();
        var categoryIds = items.Where(item => item.EntityType == SeoEntityType.Category).Select(item => item.EntityId).ToList();
        var pageIds = items.Where(item => item.EntityType == SeoEntityType.Page).Select(item => item.EntityId).ToList();

        var products = await context.Products.Where(item => productIds.Contains(item.Id))
            .Select(item => new { item.Id, item.Name, item.Slug }).ToListAsync(cancellationToken);
        var posts = await context.BlogPosts.Where(item => postIds.Contains(item.Id))
            .Select(item => new { item.Id, item.Title, item.Slug }).ToListAsync(cancellationToken);
        var categories = await context.Categories.Where(item => categoryIds.Contains(item.Id))
            .Select(item => new { item.Id, item.Name, item.Slug }).ToListAsync(cancellationToken);
        var pages = await context.PageSeos.Where(item => pageIds.Contains(item.Id))
            .Select(item => new { item.Id, Name = item.Page.ToString(), Slug = item.Page.ToString() }).ToListAsync(cancellationToken);

        foreach (var item in items)
        {
            var stat = new ContentVisitStat
            {
                EntityType = item.EntityType,
                EntityId = item.EntityId,
                TotalVisits = item.TotalVisits,
                LastVisitAt = item.LastVisitAt,
            };

            switch (item.EntityType)
            {
                case SeoEntityType.Product:
                    var product = products.FirstOrDefault(x => x.Id == item.EntityId);
                    stat.Title = product?.Name ?? "محصول حذف‌شده";
                    stat.Url = product is null ? null : $"/product/{product.Slug}";
                    break;
                case SeoEntityType.BlogPost:
                    var post = posts.FirstOrDefault(x => x.Id == item.EntityId);
                    stat.Title = post?.Title ?? "مقاله حذف‌شده";
                    stat.Url = post is null ? null : $"/blog/{post.Slug}";
                    break;
                case SeoEntityType.Category:
                    var category = categories.FirstOrDefault(x => x.Id == item.EntityId);
                    stat.Title = category?.Name ?? "دسته‌بندی حذف‌شده";
                    stat.Url = category is null ? null : $"/category/{category.Slug}";
                    break;
                default:
                    var page = pages.FirstOrDefault(x => x.Id == item.EntityId);
                    stat.Title = page?.Name ?? "صفحه";
                    stat.Url = page is null ? null : $"/page/{page.Slug}";
                    break;
            }

            result.Add(stat);
        }

        return result;
    }
}
