using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Orders;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Core.Services.Orders;

public class OrderItemService(FratakShopDbContext context) : IOrderItemService
{
    public async Task<OperationResult> AddItemAsync(OrderItem item)
    {
        if (item is null)
            return OperationResult.Error("آیتم سفارش نامعتبر است.");

        if (item.OrderId == Guid.Empty)
            return OperationResult.Error("شناسه سفارش معتبر نیست.");

        if (item.ProductId == Guid.Empty)
            return OperationResult.Error("شناسه محصول معتبر نیست.");

        if (item.Quantity <= 0)
            return OperationResult.Error("تعداد مورد نظر باید بزرگ‌تر از صفر باشد.");

        context.OrderItems.Add(item);
        await context.SaveChangesAsync();

        return OperationResult.Success("آیتم سفارش با موفقیت اضافه شد.");
    }

    public async Task<OperationResult> UpdateItemAsync(OrderItem item)
    {
        if (item is null)
            return OperationResult.Error("آیتم سفارش نامعتبر است.");

        var entity = await context.OrderItems.AsTracking().FirstOrDefaultAsync(x => x.Id == item.Id && !x.IsDeleted);
        if (entity is null)
            return OperationResult.NotFound("آیتم سفارش مورد نظر یافت نشد.");

        if (item.Quantity <= 0)
            return OperationResult.Error("تعداد مورد نظر باید بزرگ‌تر از صفر باشد.");

        entity.OrderId = item.OrderId;
        entity.ProductId = item.ProductId;
        entity.ProductName = item.ProductName;
        entity.SKU = item.SKU;
        entity.UnitPrice = item.UnitPrice;
        entity.DiscountAmount = item.DiscountAmount;
        entity.Quantity = item.Quantity;
        entity.TotalAmount = item.TotalAmount;

        await context.SaveChangesAsync();
        return OperationResult.Success("آیتم سفارش با موفقیت ویرایش شد.");
    }

    public async Task<OperationResult> RemoveItemAsync(Guid id)
    {
        var entity = await context.OrderItems.AsTracking().FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
        if (entity is null)
            return OperationResult.NotFound("آیتم سفارش مورد نظر یافت نشد.");

        context.OrderItems.Remove(entity);
        await context.SaveChangesAsync();

        return OperationResult.Success("آیتم سفارش با موفقیت حذف شد.");
    }

    public async Task<OrderItem?> GetItemByIdAsync(Guid id)
    {
        return await context.OrderItems
            .Include(x => x.Product)
            .FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
    }

    public async Task<List<OrderItem>> GetItemsByOrderAsync(Guid orderId)
    {
        return await context.OrderItems
            .Where(x => x.OrderId == orderId && !x.IsDeleted)
            .Include(x => x.Product)
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync();
    }
}
