using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Orders;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Core.Services.Orders;

public class OrderService(FratakShopDbContext context) : IOrderService
{
    public async Task<OperationResult> CreateOrderAsync(Order order)
    {
        if (order is null)
            return OperationResult.Error("سفارش نامعتبر است.");

        if (string.IsNullOrWhiteSpace(order.OrderNumber))
            return OperationResult.Error("شماره سفارش را وارد کنید.");

        if (string.IsNullOrWhiteSpace(order.CustomerName))
            return OperationResult.Error("نام مشتری را وارد کنید.");

        if (string.IsNullOrWhiteSpace(order.CustomerPhone))
            return OperationResult.Error("شماره تماس مشتری را وارد کنید.");

        var exists = await context.Orders.AnyAsync(x => x.OrderNumber == order.OrderNumber && !x.IsDeleted);
        if (exists)
            return OperationResult.Error("این شماره سفارش قبلاً ثبت شده است.");

        context.Orders.Add(order);
        await context.SaveChangesAsync();

        return OperationResult.Success("سفارش با موفقیت ثبت شد.");
    }

    public async Task<OperationResult> UpdateOrderAsync(Order order)
    {
        if (order is null)
            return OperationResult.Error("سفارش نامعتبر است.");

        var entity = await context.Orders.AsTracking().FirstOrDefaultAsync(x => x.Id == order.Id && !x.IsDeleted);
        if (entity is null)
            return OperationResult.NotFound("سفارش مورد نظر یافت نشد.");

        entity.UserId = order.UserId;
        entity.Status = order.Status;
        entity.PaymentMethod = order.PaymentMethod;
        entity.PaymentStatus = order.PaymentStatus;
        entity.TransactionId = order.TransactionId;
        entity.TrackingCode = order.TrackingCode;
        entity.SubtotalAmount = order.SubtotalAmount;
        entity.DiscountAmount = order.DiscountAmount;
        entity.ShippingAmount = order.ShippingAmount;
        entity.PayableAmount = order.PayableAmount;
        entity.ShippingMethod = order.ShippingMethod;
        entity.RequestedDeliveryDate = order.RequestedDeliveryDate;
        entity.CustomerName = order.CustomerName;
        entity.CustomerPhone = order.CustomerPhone;
        entity.ShippingProvince = order.ShippingProvince;
        entity.ShippingCity = order.ShippingCity;
        entity.ShippingAddress = order.ShippingAddress;
        entity.ShippingBuildingNumber = order.ShippingBuildingNumber;
        entity.ShippingUnit = order.ShippingUnit;
        entity.ShippingPostalCode = order.ShippingPostalCode;
        entity.ShippingPhone = order.ShippingPhone;
        entity.CustomerNote = order.CustomerNote;
        entity.PaidAt = order.PaidAt;

        await context.SaveChangesAsync();
        return OperationResult.Success("سفارش با موفقیت ویرایش شد.");
    }

    public async Task<OperationResult> DeleteOrderAsync(Guid id)
    {
        var entity = await context.Orders.AsTracking().FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
        if (entity is null)
            return OperationResult.NotFound("سفارش مورد نظر یافت نشد.");

        context.Orders.Remove(entity);
        await context.SaveChangesAsync();

        return OperationResult.Success("سفارش با موفقیت حذف شد.");
    }

    public async Task<Order?> GetOrderByIdAsync(Guid id)
    {
        return await context.Orders
            .Include(x => x.Items)
            .ThenInclude(x => x.Product)
            .FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
    }

    public async Task<List<Order>> GetOrdersByUserAsync(Guid userId)
    {
        return await context.Orders
            .Where(x => x.UserId == userId && !x.IsDeleted)
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync();
    }

    public async Task<List<Order>> GetOrdersAsync()
    {
        return await context.Orders
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync();
    }
}
