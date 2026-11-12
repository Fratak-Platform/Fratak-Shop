using Fratak_Shop.Core.DTOs.Orders.Command;
using Fratak_Shop.Core.DTOs.Orders.Query;
using Fratak_Shop.Core.Mappers.Orders;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Orders;
using Microsoft.EntityFrameworkCore;
using StockContext = Fratak_Shop.Data.Utilities.StockContext;
using StockMovementReason = Fratak_Shop.Data.Entities.Products.StockMovementReason;

namespace Fratak_Shop.Core.Services.Orders;

/// <summary>
/// مدیریت اقلام سفارش. هر تغییر قلم، موجودی انبار و مبالغ کل سفارش را هم
/// به‌روزرسانی می‌کند تا جمع سفارش همیشه با اقلام آن بخواند.
/// </summary>
public class OrderItemService(FratakShopDbContext context, IOrderService orderService) : IOrderItemService
{
    public async Task<OperationResult> AddItemAsync(AddOrderItemCommand command)
    {
        if (command.OrderId == Guid.Empty)
            return OperationResult.Error("شناسه سفارش معتبر نیست.");

        if (command.ProductId == Guid.Empty)
            return OperationResult.Error("شناسه محصول معتبر نیست.");

        if (command.Quantity <= 0)
            return OperationResult.Error("تعداد مورد نظر باید بزرگ‌تر از صفر باشد.");

        var order = await context.Orders
            .AsTracking()
            .Include(x => x.Items)
            .FirstOrDefaultAsync(x => x.Id == command.OrderId);

        if (order is null)
            return OperationResult.NotFound("سفارش مورد نظر یافت نشد.");

        if (!CanModify(order.Status))
            return OperationResult.Error("به سفارش ارسال‌شده، لغوشده یا مرجوعی نمی‌توان قلم جدید اضافه کرد.");

        var product = await context.Products
            .AsTracking()
            .FirstOrDefaultAsync(x => x.Id == command.ProductId && x.IsActive);

        if (product is null)
            return OperationResult.NotFound("محصول مورد نظر یافت نشد یا غیرفعال است.");

        if (product.StockQuantity < command.Quantity)
            return OperationResult.Error($"موجودی محصول «{product.Name}» کافی نیست.");

        var finalUnitPrice = product.GetFinalPrice();

        order.Items.Add(new OrderItem
        {
            ProductId = product.Id,
            ProductName = product.Name,
            SKU = product.SKU,
            UnitPrice = product.Price,
            DiscountAmount = (product.Price - finalUnitPrice) * command.Quantity,
            Quantity = command.Quantity,
            TotalAmount = finalUnitPrice * command.Quantity,
        });

        product.StockQuantity -= command.Quantity;

        // دفتر موجودی: دلیل «ویرایش اقلام سفارش» با شمارهٔ سفارش.
        using (StockContext.Begin(StockMovementReason.OrderEdit, "افزودن قلم به سفارش", order.Id, order.OrderNumber))
            await context.SaveChangesAsync();

        var recalculated = await orderService.RecalculateTotalsAsync(order.Id);
        if (!recalculated.IsSuccess())
            return recalculated;

        return OperationResult.Success("قلم سفارش با موفقیت اضافه شد.");
    }

    public async Task<OperationResult> EditItemAsync(EditOrderItemCommand command)
    {
        if (command.Quantity <= 0)
            return OperationResult.Error("تعداد مورد نظر باید بزرگ‌تر از صفر باشد.");

        var item = await context.OrderItems
            .AsTracking()
            .Include(x => x.Order)
            .FirstOrDefaultAsync(x => x.Id == command.Id);

        if (item is null)
            return OperationResult.NotFound("قلم سفارش مورد نظر یافت نشد.");

        if (item.Order is null)
            return OperationResult.NotFound("سفارش این قلم یافت نشد.");

        if (!CanModify(item.Order.Status))
            return OperationResult.Error("اقلام سفارش ارسال‌شده، لغوشده یا مرجوعی قابل ویرایش نیست.");

        var delta = command.Quantity - item.Quantity;

        if (delta != 0 && item.ProductId.HasValue)
        {
            var product = await context.Products
                .AsTracking()
                .FirstOrDefaultAsync(x => x.Id == item.ProductId.Value);

            if (product is not null)
            {
                if (delta > 0 && product.StockQuantity < delta)
                    return OperationResult.Error($"موجودی محصول «{product.Name}» کافی نیست.");

                product.StockQuantity -= delta;
            }
        }

        // مبلغ تخفیف ردیف متناسب با تعداد جدید بازمحاسبه می‌شود.
        var unitDiscount = item.Quantity > 0
            ? item.DiscountAmount / item.Quantity
            : 0m;

        var newDiscount = decimal.Round(unitDiscount * command.Quantity, 0, MidpointRounding.AwayFromZero);

        item.Quantity = command.Quantity;
        item.DiscountAmount = newDiscount;
        item.TotalAmount = (item.UnitPrice * command.Quantity) - newDiscount;

        using (StockContext.Begin(StockMovementReason.OrderEdit, "تغییر تعداد قلم سفارش", item.OrderId, item.Order.OrderNumber))
            await context.SaveChangesAsync();

        var recalculated = await orderService.RecalculateTotalsAsync(item.OrderId);
        if (!recalculated.IsSuccess())
            return recalculated;

        return OperationResult.Success("قلم سفارش با موفقیت ویرایش شد.");
    }

    public async Task<OperationResult> RemoveItemAsync(Guid id)
    {
        var item = await context.OrderItems
            .AsTracking()
            .Include(x => x.Order)
            .FirstOrDefaultAsync(x => x.Id == id);

        if (item is null)
            return OperationResult.NotFound("قلم سفارش مورد نظر یافت نشد.");

        if (item.Order is null)
            return OperationResult.NotFound("سفارش این قلم یافت نشد.");

        if (!CanModify(item.Order.Status))
            return OperationResult.Error("اقلام سفارش ارسال‌شده، لغوشده یا مرجوعی قابل حذف نیست.");

        if (item.ProductId.HasValue)
        {
            var product = await context.Products
                .AsTracking()
                .FirstOrDefaultAsync(x => x.Id == item.ProductId.Value);

            if (product is not null)
                product.StockQuantity += item.Quantity;
        }

        var orderId = item.OrderId;

        context.OrderItems.Remove(item);
        using (StockContext.Begin(StockMovementReason.OrderEdit, "حذف قلم از سفارش", orderId, item.Order.OrderNumber))
            await context.SaveChangesAsync();

        var recalculated = await orderService.RecalculateTotalsAsync(orderId);
        if (!recalculated.IsSuccess())
            return recalculated;

        return OperationResult.Success("قلم سفارش با موفقیت حذف شد.");
    }

    public async Task<OrderItemDto?> GetItemByIdAsync(Guid id)
    {
        var item = await context.OrderItems
            .Include(x => x.Product)
            .FirstOrDefaultAsync(x => x.Id == id);

        return item?.Map();
    }

    public async Task<List<OrderItemDto>> GetItemsByOrderAsync(Guid orderId)
    {
        var items = await context.OrderItems
            .Where(x => x.OrderId == orderId)
            .Include(x => x.Product)
            .OrderBy(x => x.CreatedAt)
            .ToListAsync();

        return items.Map();
    }

    /// <summary>فقط سفارش‌های در جریان پردازش قابل تغییر اقلام هستند.</summary>
    private static bool CanModify(OrderStatus status) =>
        status is OrderStatus.Pending or OrderStatus.Confirmed or OrderStatus.Processing;
}
