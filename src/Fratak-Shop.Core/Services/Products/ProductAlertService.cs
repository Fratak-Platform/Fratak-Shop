using Fratak_Shop.Core.DTOs.Engagement;
using Fratak_Shop.Core.DTOs.Notifications;
using Fratak_Shop.Core.Services.Notifications;
using Fratak_Shop.Core.Services.Settings;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Notifications;
using Fratak_Shop.Data.Entities.Products;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Fratak_Shop.Core.Services.Products;

/// <summary>
/// «خبرم کن»: a customer asks to be told when a product is back in stock or its price drops
/// (optionally below a target price). A background job compares the active requests with the
/// current products; the channels (in-site / SMS / push) come from the notification templates.
/// </summary>
public interface IProductAlertService
{
    Task<OperationResult> SubscribeAsync(Guid userId, Guid productId, ProductAlertType type, decimal? targetPrice = null);

    Task<OperationResult> UnsubscribeAsync(Guid userId, Guid productId, ProductAlertType type);

    Task<OperationResult> DeleteAsync(Guid userId, Guid alertId);

    Task<ProductAlertState> GetStateAsync(Guid userId, Guid productId);

    Task<List<ProductAlertDto>> GetUserAlertsAsync(Guid userId, bool includeFinished = true);

    /// <summary>Background job: sends due alerts. Returns how many customers were notified.</summary>
    Task<int> ProcessAsync(CancellationToken cancellationToken = default);
}

public class ProductAlertService(
    FratakShopDbContext context,
    IStoreSettingService settingService,
    INotificationService notifications,
    ILogger<ProductAlertService> logger) : IProductAlertService
{
    public async Task<OperationResult> SubscribeAsync(Guid userId, Guid productId, ProductAlertType type, decimal? targetPrice = null)
    {
        var settings = await settingService.GetAsync();
        if (!settings.IsProductAlertEnabled)
            return OperationResult.Error("امکان «خبرم کن» در حال حاضر فعال نیست.");

        if (type is not (ProductAlertType.BackInStock or ProductAlertType.PriceDrop))
            return OperationResult.Error("نوع اطلاع‌رسانی معتبر نیست.");

        var product = await context.Products
            .Where(p => p.Id == productId && p.IsActive)
            .Select(p => new { p.Id, p.Name, p.Price, p.DiscountPrice, p.DiscountStartAt, p.DiscountEndAt, p.StockQuantity })
            .FirstOrDefaultAsync();
        if (product is null)
            return OperationResult.NotFound("محصول مورد نظر یافت نشد.");

        var finalPrice = FratakShopDbContext.FinalPrice(product.Price, product.DiscountPrice, product.DiscountStartAt, product.DiscountEndAt, DateTimeOffset.UtcNow);

        if (type == ProductAlertType.BackInStock && product.StockQuantity > 0)
            return OperationResult.Error("این محصول هم‌اکنون موجود است.");

        if (targetPrice.HasValue)
        {
            targetPrice = decimal.Truncate(targetPrice.Value);
            if (targetPrice <= 0 || targetPrice >= finalPrice)
                return OperationResult.Error($"قیمت دلخواه باید کمتر از قیمت فعلی ({finalPrice:N0} تومان) باشد.");
        }

        var existing = await context.ProductAlerts
            .AsTracking()
            .FirstOrDefaultAsync(a => a.UserId == userId && a.ProductId == productId && a.Type == type && a.IsActive);

        if (existing is not null)
        {
            existing.TargetPrice = type == ProductAlertType.PriceDrop ? targetPrice : null;
            existing.PriceAtSubscribe = finalPrice;
            await context.SaveChangesAsync();
            return OperationResult.Success("درخواست «خبرم کن» شما به‌روزرسانی شد.");
        }

        var activeCount = await context.ProductAlerts.CountAsync(a => a.UserId == userId && a.IsActive);
        if (activeCount >= settings.ProductAlertMaxPerUser)
            return OperationResult.Error($"حداکثر {settings.ProductAlertMaxPerUser} درخواست فعال «خبرم کن» مجاز است؛ چند مورد قدیمی را حذف کنید.");

        context.ProductAlerts.Add(new ProductAlert
        {
            UserId = userId,
            ProductId = productId,
            Type = type,
            PriceAtSubscribe = finalPrice,
            TargetPrice = type == ProductAlertType.PriceDrop ? targetPrice : null,
        });

        try
        {
            await context.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Double click: the unique index already holds the same active request.
            context.ChangeTracker.Clear();
        }

        return OperationResult.Success(type == ProductAlertType.BackInStock
            ? "به محض موجود شدن این محصول به شما خبر می‌دهیم."
            : targetPrice.HasValue
                ? $"وقتی قیمت به {targetPrice:N0} تومان یا کمتر برسد به شما خبر می‌دهیم."
                : "به محض کاهش قیمت این محصول به شما خبر می‌دهیم.");
    }

    public async Task<OperationResult> UnsubscribeAsync(Guid userId, Guid productId, ProductAlertType type)
    {
        var affected = await context.ProductAlerts
            .Where(a => a.UserId == userId && a.ProductId == productId && a.Type == type && a.IsActive)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.IsActive, false).SetProperty(a => a.UpdatedAt, DateTimeOffset.UtcNow));

        return affected == 0
            ? OperationResult.NotFound("درخواست فعالی یافت نشد.")
            : OperationResult.Success("درخواست «خبرم کن» لغو شد.");
    }

    public async Task<OperationResult> DeleteAsync(Guid userId, Guid alertId)
    {
        var alert = await context.ProductAlerts.AsTracking().FirstOrDefaultAsync(a => a.Id == alertId && a.UserId == userId);
        if (alert is null)
            return OperationResult.NotFound("درخواست مورد نظر یافت نشد.");

        context.ProductAlerts.Remove(alert);
        await context.SaveChangesAsync();
        return OperationResult.Success("درخواست حذف شد.");
    }

    public async Task<ProductAlertState> GetStateAsync(Guid userId, Guid productId)
    {
        var alerts = await context.ProductAlerts
            .Where(a => a.UserId == userId && a.ProductId == productId && a.IsActive)
            .Select(a => new { a.Type, a.TargetPrice })
            .ToListAsync();

        var priceDrop = alerts.FirstOrDefault(a => a.Type == ProductAlertType.PriceDrop);
        return new ProductAlertState(
            alerts.Any(a => a.Type == ProductAlertType.BackInStock),
            priceDrop is not null,
            priceDrop?.TargetPrice);
    }

    public async Task<List<ProductAlertDto>> GetUserAlertsAsync(Guid userId, bool includeFinished = true)
    {
        var rows = await context.ProductAlerts
            .Where(a => a.UserId == userId && (includeFinished || a.IsActive))
            .OrderByDescending(a => a.IsActive).ThenByDescending(a => a.CreatedAt)
            .Take(200)
            .Select(a => new
            {
                a.Id, a.Type, a.IsActive, a.PriceAtSubscribe, a.TargetPrice, a.CreatedAt, a.NotifiedAt, a.NotifiedPrice,
                a.ProductId, a.Product.Name, a.Product.Slug, ProductIsActive = a.Product.IsActive,
                a.Product.Price, a.Product.DiscountPrice, a.Product.DiscountStartAt, a.Product.DiscountEndAt, a.Product.StockQuantity,
                Image = a.Product.Images.OrderByDescending(i => i.IsPrimary).ThenBy(i => i.DisplayOrder).Select(i => i.ImageName).FirstOrDefault(),
            })
            .ToListAsync();

        var now = DateTimeOffset.UtcNow;
        return rows.Select(r => new ProductAlertDto
        {
            Id = r.Id,
            Type = r.Type,
            IsActive = r.IsActive,
            PriceAtSubscribe = r.PriceAtSubscribe,
            TargetPrice = r.TargetPrice,
            CreatedAt = r.CreatedAt,
            NotifiedAt = r.NotifiedAt,
            NotifiedPrice = r.NotifiedPrice,
            ProductId = r.ProductId,
            ProductName = r.Name,
            ProductSlug = r.Slug,
            ImageName = r.Image,
            CurrentPrice = r.Price,
            CurrentFinalPrice = FratakShopDbContext.FinalPrice(r.Price, r.DiscountPrice, r.DiscountStartAt, r.DiscountEndAt, now),
            IsAvailable = r.ProductIsActive && r.StockQuantity > 0,
            ProductIsActive = r.ProductIsActive,
        }).ToList();
    }

    public async Task<int> ProcessAsync(CancellationToken cancellationToken = default)
    {
        var settings = await settingService.GetAsync();
        if (!settings.IsProductAlertEnabled)
            return 0;

        var notified = 0;
        var now = DateTimeOffset.UtcNow;

        // Both alert kinds need the product in stock, so only in-stock products with active
        // alerts are inspected (bounded by the catalog size, not by the number of alerts).
        var products = await context.Products
            .Where(p => p.IsActive && p.StockQuantity > 0 && context.ProductAlerts.Any(a => a.ProductId == p.Id && a.IsActive))
            .Select(p => new { p.Id, p.Name, p.Slug, p.Price, p.DiscountPrice, p.DiscountStartAt, p.DiscountEndAt })
            .ToListAsync(cancellationToken);

        foreach (var chunk in products.Chunk(100))
        {
            var ids = chunk.Select(p => p.Id).ToList();
            var alerts = await context.ProductAlerts
                .Where(a => a.IsActive && ids.Contains(a.ProductId))
                .Select(a => new { a.Id, a.UserId, a.ProductId, a.Type, a.PriceAtSubscribe, a.TargetPrice })
                .ToListAsync(cancellationToken);

            foreach (var alert in alerts)
            {
                if (cancellationToken.IsCancellationRequested)
                    return notified;

                var product = chunk.First(p => p.Id == alert.ProductId);
                var finalPrice = FratakShopDbContext.FinalPrice(product.Price, product.DiscountPrice, product.DiscountStartAt, product.DiscountEndAt, now);

                var due = alert.Type switch
                {
                    ProductAlertType.BackInStock => true,
                    ProductAlertType.PriceDrop => finalPrice < alert.PriceAtSubscribe
                                                  && (alert.TargetPrice is null || finalPrice <= alert.TargetPrice),
                    _ => false,
                };

                if (!due)
                    continue;

                // Claim first (exactly once even with two app instances), then notify.
                var claimed = await context.ProductAlerts
                    .Where(a => a.Id == alert.Id && a.IsActive)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(a => a.IsActive, false)
                        .SetProperty(a => a.NotifiedAt, now)
                        .SetProperty(a => a.NotifiedPrice, finalPrice)
                        .SetProperty(a => a.UpdatedAt, now), cancellationToken);

                if (claimed == 0)
                    continue;

                var isStock = alert.Type == ProductAlertType.BackInStock;
                await notifications.NotifyAsync(new NotificationRequest
                {
                    UserId = alert.UserId,
                    Event = isStock ? NotificationEvent.BackInStock : NotificationEvent.PriceDrop,
                    Category = NotificationCategory.Promotion,
                    LinkUrl = "/product/" + product.Slug,
                    Values =
                    {
                        ["ProductName"] = product.Name,
                        ["Price"] = finalPrice.ToString("N0"),
                        ["OldPrice"] = alert.PriceAtSubscribe.ToString("N0"),
                        ["Saving"] = Math.Max(0, alert.PriceAtSubscribe - finalPrice).ToString("N0"),
                    },
                });
                notified++;
            }
        }

        if (notified > 0)
            logger.LogInformation("Product alerts: {Count} customers notified.", notified);

        return notified;
    }
}
