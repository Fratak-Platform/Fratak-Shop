using Fratak_Shop.Core.DTOs.Orders.Command;
using Fratak_Shop.Core.DTOs.Orders.Query;
using Fratak_Shop.Core.Mappers.Orders;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Orders;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Core.Services.Orders;

/// <summary>
/// مدیریت سفارش‌ها: ثبت سفارش با اسنپ‌شات قیمت و آدرس، گردش وضعیت سفارش،
/// وضعیت پرداخت، کد رهگیری و بازمحاسبه مبالغ.
/// </summary>
public class OrderService(FratakShopDbContext context) : IOrderService
{
    /// <summary>مسیرهای مجاز تغییر وضعیت سفارش.</summary>
    private static readonly Dictionary<OrderStatus, OrderStatus[]> AllowedTransitions = new()
    {
        [OrderStatus.Pending] = [OrderStatus.Confirmed, OrderStatus.Cancelled],
        [OrderStatus.Confirmed] = [OrderStatus.Processing, OrderStatus.Cancelled],
        [OrderStatus.Processing] = [OrderStatus.Shipped, OrderStatus.Cancelled],
        [OrderStatus.Shipped] = [OrderStatus.Delivered, OrderStatus.Returned],
        [OrderStatus.Delivered] = [OrderStatus.Returned],
        [OrderStatus.Cancelled] = [],
        [OrderStatus.Returned] = [],
    };

    public async Task<OperationResult<OrderDto>> CreateOrderAsync(CreateOrderCommand command)
    {
        var error = Validate(command);
        if (error is not null)
            return OperationResult<OrderDto>.Error(error);

        if (command.UserId.HasValue)
        {
            var userExists = await context.Users.AnyAsync(x => x.Id == command.UserId.Value);
            if (!userExists)
                return OperationResult<OrderDto>.NotFound("کاربر مورد نظر یافت نشد.");
        }

        var productIds = command.Items
            .Select(item => item.ProductId)
            .Distinct()
            .ToList();

        var products = await context.Products
            .AsTracking()
            .Where(x => productIds.Contains(x.Id) && x.IsActive)
            .ToListAsync();

        if (products.Count != productIds.Count)
            return OperationResult<OrderDto>.Error("برخی از محصولات سفارش یافت نشد یا غیرفعال است.");

        var now = DateTimeOffset.UtcNow;

        var order = new Order
        {
            UserId = command.UserId,
            OrderNumber = await GenerateOrderNumberAsync(),
            Status = OrderStatus.Pending,
            PaymentMethod = command.PaymentMethod,
            PaymentStatus = PaymentStatus.Pending,
            ShippingMethod = command.ShippingMethod?.Trim(),
            RequestedDeliveryDate = command.RequestedDeliveryDate,
            DiscountAmount = command.DiscountAmount,
            ShippingAmount = command.ShippingAmount,
            CustomerName = command.CustomerName.Trim(),
            CustomerPhone = command.CustomerPhone.Trim(),
            ShippingProvince = command.ShippingProvince.Trim(),
            ShippingCity = command.ShippingCity.Trim(),
            ShippingAddress = command.ShippingAddress.Trim(),
            ShippingBuildingNumber = command.ShippingBuildingNumber?.Trim(),
            ShippingUnit = command.ShippingUnit?.Trim(),
            ShippingPostalCode = command.ShippingPostalCode.Trim(),
            ShippingPhone = command.ShippingPhone.Trim(),
            CustomerNote = command.CustomerNote?.Trim(),
        };

        foreach (var request in command.Items)
        {
            var product = products.First(x => x.Id == request.ProductId);

            if (product.StockQuantity < request.Quantity)
                return OperationResult<OrderDto>.Error($"موجودی محصول «{product.Name}» کافی نیست.");

            var finalUnitPrice = product.GetFinalPrice(now);

            order.Items.Add(new OrderItem
            {
                ProductId = product.Id,
                ProductName = product.Name,
                SKU = product.SKU,
                UnitPrice = product.Price,
                DiscountAmount = (product.Price - finalUnitPrice) * request.Quantity,
                Quantity = request.Quantity,
                TotalAmount = finalUnitPrice * request.Quantity,
            });

            // رزرو موجودی در زمان ثبت سفارش؛ در صورت لغو یا مرجوعی برگشت داده می‌شود.
            product.StockQuantity -= request.Quantity;
            product.SalesCount += request.Quantity;
        }

        var payable = CalculatePayable(order);
        if (payable < 0)
            return OperationResult<OrderDto>.Error("مبلغ قابل پرداخت سفارش نمی‌تواند منفی باشد.");

        order.SubtotalAmount = order.Items.Sum(item => item.UnitPrice * item.Quantity);
        order.PayableAmount = payable;

        context.Orders.Add(order);
        await context.SaveChangesAsync();

        return OperationResult<OrderDto>.Success(order.Map(), $"سفارش با شماره {order.OrderNumber} با موفقیت ثبت شد.");
    }

    public async Task<OperationResult> EditOrderAsync(EditOrderCommand command)
    {
        var order = await context.Orders
            .AsTracking()
            .Include(x => x.Items)
            .FirstOrDefaultAsync(x => x.Id == command.Id);

        if (order is null)
            return OperationResult.NotFound("سفارش مورد نظر یافت نشد.");

        var error = Validate(command);
        if (error is not null)
            return OperationResult.Error(error);

        var statusError = await ApplyStatusChangeAsync(order, command.Status);
        if (statusError is not null)
            return OperationResult.Error(statusError);

        order.PaymentMethod = command.PaymentMethod;
        order.PaymentStatus = command.PaymentStatus;
        order.PaidAt = command.PaymentStatus == PaymentStatus.Paid
            ? command.PaidAt ?? order.PaidAt ?? DateTimeOffset.UtcNow
            : command.PaidAt;
        order.TransactionId = command.TransactionId?.Trim();
        order.TrackingCode = command.TrackingCode?.Trim();
        order.ShippingMethod = command.ShippingMethod?.Trim();
        order.RequestedDeliveryDate = command.RequestedDeliveryDate;
        order.CustomerName = command.CustomerName.Trim();
        order.CustomerPhone = command.CustomerPhone.Trim();
        order.ShippingProvince = command.ShippingProvince.Trim();
        order.ShippingCity = command.ShippingCity.Trim();
        order.ShippingAddress = command.ShippingAddress.Trim();
        order.ShippingBuildingNumber = command.ShippingBuildingNumber?.Trim();
        order.ShippingUnit = command.ShippingUnit?.Trim();
        order.ShippingPostalCode = command.ShippingPostalCode.Trim();
        order.ShippingPhone = command.ShippingPhone.Trim();
        order.CustomerNote = command.CustomerNote?.Trim();

        await context.SaveChangesAsync();

        return OperationResult.Success("سفارش با موفقیت ویرایش شد.");
    }

    public async Task<OperationResult> DeleteOrderAsync(Guid id)
    {
        var order = await context.Orders
            .AsTracking()
            .Include(x => x.Items)
            .FirstOrDefaultAsync(x => x.Id == id);

        if (order is null)
            return OperationResult.NotFound("سفارش مورد نظر یافت نشد.");

        if (order.Status is not (OrderStatus.Cancelled or OrderStatus.Returned))
            await RestoreStockAsync(order.Items);

        context.Orders.Remove(order);
        await context.SaveChangesAsync();

        return OperationResult.Success("سفارش با موفقیت حذف شد.");
    }

    public async Task<OperationResult> ChangeOrderStatusAsync(Guid id, OrderStatus status)
    {
        var order = await context.Orders
            .AsTracking()
            .Include(x => x.Items)
            .FirstOrDefaultAsync(x => x.Id == id);

        if (order is null)
            return OperationResult.NotFound("سفارش مورد نظر یافت نشد.");

        var error = await ApplyStatusChangeAsync(order, status);
        if (error is not null)
            return OperationResult.Error(error);

        await context.SaveChangesAsync();

        return OperationResult.Success("وضعیت سفارش با موفقیت تغییر کرد.");
    }

    public async Task<OperationResult> ChangePaymentStatusAsync(Guid id, PaymentStatus paymentStatus,
        string? transactionId = null)
    {
        if (paymentStatus == PaymentStatus.None)
            return OperationResult.Error("وضعیت پرداخت را انتخاب کنید.");

        var order = await context.Orders.AsTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (order is null)
            return OperationResult.NotFound("سفارش مورد نظر یافت نشد.");

        order.PaymentStatus = paymentStatus;

        if (paymentStatus == PaymentStatus.Paid)
            order.PaidAt ??= DateTimeOffset.UtcNow;

        if (!string.IsNullOrWhiteSpace(transactionId))
            order.TransactionId = transactionId.Trim();

        await context.SaveChangesAsync();

        return OperationResult.Success("وضعیت پرداخت سفارش با موفقیت به‌روزرسانی شد.");
    }

    public async Task<OperationResult> SetTrackingCodeAsync(Guid id, string trackingCode)
    {
        if (string.IsNullOrWhiteSpace(trackingCode))
            return OperationResult.Error("کد رهگیری مرسوله را وارد کنید.");

        var order = await context.Orders.AsTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (order is null)
            return OperationResult.NotFound("سفارش مورد نظر یافت نشد.");

        order.TrackingCode = trackingCode.Trim();

        // ثبت کد رهگیری یعنی سفارش تحویل پست/پیک شده است.
        if (order.Status is OrderStatus.Confirmed or OrderStatus.Processing)
            order.Status = OrderStatus.Shipped;

        await context.SaveChangesAsync();

        return OperationResult.Success("کد رهگیری مرسوله با موفقیت ثبت شد.");
    }

    public async Task<OperationResult> RecalculateTotalsAsync(Guid orderId)
    {
        var order = await context.Orders
            .AsTracking()
            .Include(x => x.Items)
            .FirstOrDefaultAsync(x => x.Id == orderId);

        if (order is null)
            return OperationResult.NotFound("سفارش مورد نظر یافت نشد.");

        var payable = CalculatePayable(order);
        if (payable < 0)
            return OperationResult.Error("مبلغ قابل پرداخت سفارش نمی‌تواند منفی باشد.");

        order.SubtotalAmount = order.Items.Sum(item => item.UnitPrice * item.Quantity);
        order.PayableAmount = payable;

        await context.SaveChangesAsync();

        return OperationResult.Success("مبالغ سفارش بازمحاسبه شد.");
    }

    public async Task<OrderFilterResult> GetOrdersByFilterAsync(OrderFilterParams filterParams)
    {
        var query = context.Orders
            .Include(x => x.Items)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(filterParams.Search))
        {
            var search = filterParams.Search.Trim();
            query = query.Where(x => x.OrderNumber.Contains(search)
                                     || x.CustomerName.Contains(search)
                                     || x.CustomerPhone.Contains(search));
        }

        if (filterParams.Status.HasValue)
            query = query.Where(x => x.Status == filterParams.Status.Value);

        if (filterParams.PaymentStatus.HasValue)
            query = query.Where(x => x.PaymentStatus == filterParams.PaymentStatus.Value);

        if (filterParams.UserId.HasValue)
            query = query.Where(x => x.UserId == filterParams.UserId.Value);

        if (filterParams.FromDate.HasValue)
            query = query.Where(x => x.CreatedAt >= filterParams.FromDate.Value);

        if (filterParams.ToDate.HasValue)
        {
            var toDate = filterParams.ToDate.Value;
            var endOfDay = new DateTimeOffset(toDate.Date.AddDays(1).AddTicks(-1), toDate.Offset);
            query = query.Where(x => x.CreatedAt <= endOfDay);
        }

        query = query.OrderByDescending(x => x.CreatedAt);

        var skip = (filterParams.PageId - 1) * filterParams.Take;

        var orders = await query
            .Skip(skip)
            .Take(filterParams.Take)
            .ToListAsync();

        var result = new OrderFilterResult
        {
            Data = orders.MapFilterData(),
            FilterParams = filterParams,
        };

        result.GeneratePaging(query, filterParams.Take, filterParams.PageId);

        return result;
    }

    public async Task<OrderDto?> GetOrderByIdAsync(Guid id)
    {
        var order = await context.Orders
            .Include(x => x.Items)
            .ThenInclude(x => x.Product)
            .ThenInclude(x => x!.Images)
            .FirstOrDefaultAsync(x => x.Id == id);

        return order?.Map();
    }

    public async Task<OrderDto?> GetOrderByNumberAsync(string orderNumber)
    {
        if (string.IsNullOrWhiteSpace(orderNumber))
            return null;

        var order = await context.Orders
            .Include(x => x.Items)
            .ThenInclude(x => x.Product)
            .ThenInclude(x => x!.Images)
            .FirstOrDefaultAsync(x => x.OrderNumber == orderNumber.Trim());

        return order?.Map();
    }

    public async Task<List<OrderFilterData>> GetOrdersByUserAsync(Guid userId)
    {
        var orders = await context.Orders
            .Where(x => x.UserId == userId)
            .Include(x => x.Items)
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync();

        return orders.MapFilterData();
    }

    public async Task<List<OrderFilterData>> GetOrdersAsync()
    {
        var orders = await context.Orders
            .Include(x => x.Items)
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync();

        return orders.MapFilterData();
    }

    /// <summary>
    /// اعتبارسنجی مسیر تغییر وضعیت سفارش؛ در صورت لغو یا مرجوعی، موجودی اقلام
    /// به انبار برگردانده می‌شود.
    /// </summary>
    private async Task<string?> ApplyStatusChangeAsync(Order order, OrderStatus newStatus)
    {
        if (newStatus == OrderStatus.None)
            return "وضعیت سفارش را انتخاب کنید.";

        if (order.Status == newStatus)
            return null;

        if (!AllowedTransitions.TryGetValue(order.Status, out var allowed) || !allowed.Contains(newStatus))
            return "تغییر وضعیت سفارش از این مرحله امکان‌پذیر نیست.";

        if (newStatus is OrderStatus.Cancelled or OrderStatus.Returned)
            await RestoreStockAsync(order.Items);

        order.Status = newStatus;

        return null;
    }

    private async Task RestoreStockAsync(IEnumerable<OrderItem> items)
    {
        var productIds = items
            .Where(item => item.ProductId.HasValue)
            .Select(item => item.ProductId!.Value)
            .Distinct()
            .ToList();

        if (productIds.Count == 0)
            return;

        var products = await context.Products
            .AsTracking()
            .Where(x => productIds.Contains(x.Id))
            .ToListAsync();

        foreach (var item in items)
        {
            if (!item.ProductId.HasValue)
                continue;

            var product = products.FirstOrDefault(x => x.Id == item.ProductId.Value);
            if (product is null)
                continue;

            product.StockQuantity += item.Quantity;
            product.SalesCount = Math.Max(0, product.SalesCount - item.Quantity);
        }
    }

    private static decimal CalculatePayable(Order order)
    {
        var subtotal = order.Items.Sum(item => item.UnitPrice * item.Quantity);
        var itemsDiscount = order.Items.Sum(item => item.DiscountAmount);

        return subtotal - itemsDiscount - order.DiscountAmount + order.ShippingAmount;
    }

    private async Task<string> GenerateOrderNumberAsync()
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var orderNumber = $"FR-{DateTime.UtcNow:yyyyMMdd}-{Random.Shared.Next(100000, 999999)}";

            var exists = await context.Orders
                .IgnoreQueryFilters()
                .AnyAsync(x => x.OrderNumber == orderNumber);

            if (!exists)
                return orderNumber;
        }

        return $"FR-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{Random.Shared.Next(1000, 9999)}";
    }

    private static string? Validate(CreateOrderCommand command)
    {
        if (command.Items.Count == 0)
            return "سفارش باید حداقل یک محصول داشته باشد.";

        if (command.Items.Any(item => item.ProductId == Guid.Empty))
            return "شناسه محصول انتخابی معتبر نیست.";

        if (command.Items.Any(item => item.Quantity <= 0))
            return "تعداد هر محصول باید بزرگ‌تر از صفر باشد.";

        if (command.PaymentMethod == PaymentMethod.None)
            return "روش پرداخت را انتخاب کنید.";

        if (command.DiscountAmount < 0)
            return "مبلغ تخفیف سفارش نمی‌تواند منفی باشد.";

        if (command.ShippingAmount < 0)
            return "هزینه ارسال نمی‌تواند منفی باشد.";

        if (command.UserId.HasValue && command.UserId.Value == Guid.Empty)
            return "شناسه کاربر معتبر نیست.";

        return ValidateDelivery(command.CustomerName, command.CustomerPhone, command.ShippingProvince,
            command.ShippingCity, command.ShippingAddress, command.ShippingPostalCode, command.ShippingPhone);
    }

    private static string? Validate(EditOrderCommand command)
    {
        if (command.PaymentMethod == PaymentMethod.None)
            return "روش پرداخت را انتخاب کنید.";

        return ValidateDelivery(command.CustomerName, command.CustomerPhone, command.ShippingProvince,
            command.ShippingCity, command.ShippingAddress, command.ShippingPostalCode, command.ShippingPhone);
    }

    private static string? ValidateDelivery(string? customerName, string? customerPhone, string? province,
        string? city, string? address, string? postalCode, string? shippingPhone)
    {
        if (string.IsNullOrWhiteSpace(customerName))
            return "نام گیرنده سفارش را وارد کنید.";

        if (string.IsNullOrWhiteSpace(province))
            return "استان مقصد را وارد کنید.";

        if (string.IsNullOrWhiteSpace(city))
            return "شهر مقصد را وارد کنید.";

        if (string.IsNullOrWhiteSpace(address))
            return "نشانی کامل پستی را وارد کنید.";

        var normalizedPostalCode = postalCode?.Trim();

        if (string.IsNullOrWhiteSpace(normalizedPostalCode)
            || normalizedPostalCode.Length != 10
            || !normalizedPostalCode.All(char.IsDigit))
            return "کد پستی باید ۱۰ رقم باشد.";

        if (string.IsNullOrWhiteSpace(customerPhone) || !IsValidPhoneNumber(customerPhone))
            return "شماره تماس خریدار معتبر نیست.";

        if (string.IsNullOrWhiteSpace(shippingPhone) || !IsValidPhoneNumber(shippingPhone))
            return "شماره تماس گیرنده سفارش معتبر نیست.";

        return null;
    }

    /// <summary>شماره موبایل (۰۹xxxxxxxxx) یا تلفن ثابت (۰۲۱xxxxxxxx) را می‌پذیرد.</summary>
    private static bool IsValidPhoneNumber(string phoneNumber)
    {
        var phone = phoneNumber.Trim();

        return phone.IsValidMobile() || phone.IsValidPhone();
    }
}
