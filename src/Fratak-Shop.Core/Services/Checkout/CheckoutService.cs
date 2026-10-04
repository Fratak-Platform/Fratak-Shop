using Fratak_Shop.Core.DTOs.Checkout.Query;
using Fratak_Shop.Core.DTOs.Orders.Command;
using Fratak_Shop.Core.Services.Marketing;
using Fratak_Shop.Core.Services.Notifications;
using Fratak_Shop.Core.Services.Orders;
using Fratak_Shop.Core.Services.Payments;
using Fratak_Shop.Core.Services.Settings;
using Fratak_Shop.Core.Services.Shipping;
using Fratak_Shop.Core.Services.Shopping;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Notifications;
using Fratak_Shop.Data.Entities.Orders;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Core.Services.Checkout;

public class CheckoutService(
    FratakShopDbContext context,
    ICartService cartService,
    ICouponService couponService,
    IShippingMethodService shippingMethodService,
    IPaymentService paymentService,
    IBankTransferService bankTransferService,
    INotificationService notificationService,
    ISiteSettingService settingService) : ICheckoutService
{
    public async Task<CheckoutSummaryDto?> GetSummaryAsync(Guid? userId, string? sessionKey, Guid? shippingMethodId,
        string? couponCode, CancellationToken cancellationToken = default)
    {
        var settings = await settingService.GetStorefrontSettingsAsync();
        var cart = await cartService.GetCartDtoAsync(userId, sessionKey, couponCode, shippingMethodId, cancellationToken);

        if (cart.Items.Count == 0)
            return null;

        var summary = new CheckoutSummaryDto
        {
            Cart = cart,
            SelectedShippingMethodId = shippingMethodId,
            IsOnlinePaymentEnabled = settings.EnableOnlinePayment,
            IsBankTransferEnabled = settings.EnableBankTransfer,
            IsCashOnDeliveryEnabled = settings.EnableCashOnDelivery,
            PaymentInstructions = settings.PaymentInstructions,
        };

        summary.ShippingMethods = await shippingMethodService.GetActiveAsync();

        if (summary.ShippingMethods.Count > 0 && summary.SelectedShippingMethodId is null)
        {
            var defaultMethod = summary.ShippingMethods.First();
            summary.SelectedShippingMethodId = defaultMethod.Id;
            summary.Cart.ShippingMethodId = defaultMethod.Id;
            summary.Cart.ShippingAmount = defaultMethod.CalculateCost(Math.Max(0, cart.Subtotal - cart.CouponDiscount));
        }

        if (settings.EnableOnlinePayment)
            summary.AvailablePaymentMethods.Add(PaymentMethod.OnlineGateway);

        if (settings.EnableBankTransfer)
        {
            summary.AvailablePaymentMethods.Add(PaymentMethod.BankTransfer);
            summary.BankTransfer = await bankTransferService.GetInstructionsAsync(cancellationToken);
        }

        if (settings.EnableCashOnDelivery)
            summary.AvailablePaymentMethods.Add(PaymentMethod.CashOnDelivery);

        return summary;
    }

    public async Task<CheckoutFormDto> GetFormAsync(Guid? userId, string? sessionKey, Guid? shippingMethodId,
        CancellationToken cancellationToken = default)
    {
        var form = new CheckoutFormDto
        {
            ShippingMethods = await shippingMethodService.GetActiveAsync(),
        };

        var cart = await cartService.GetCartDtoAsync(userId, sessionKey, null, shippingMethodId, cancellationToken);
        form.Command.ShippingMethodId = shippingMethodId ?? form.ShippingMethods.FirstOrDefault()?.Id;
        form.Command.CouponCode = cart.CouponCode;

        if (userId.HasValue)
        {
            var user = await context.Users
                .FirstOrDefaultAsync(item => item.Id == userId.Value, cancellationToken);

            if (user is not null)
            {
                form.Command.CustomerName = $"{user.FirstName} {user.LastName}".Trim();
                form.Command.CustomerPhone = user.PhoneNumber;
                form.Command.ShippingPhone = user.PhoneNumber;
            }

            var defaultAddress = await context.Addresses
                .Include(address => address.Province)
                .Include(address => address.City)
                .Where(address => address.UserId == userId.Value)
                .OrderByDescending(address => address.IsDefault)
                .ThenByDescending(address => address.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken);

            if (defaultAddress is not null)
            {
                form.Command.ShippingProvince = defaultAddress.Province?.Name ?? form.Command.ShippingProvince;
                form.Command.ShippingCity = defaultAddress.City?.Name ?? form.Command.ShippingCity;
                form.Command.ShippingAddress = defaultAddress.AddressLine;
                form.Command.ShippingBuildingNumber = defaultAddress.BuildingNumber;
                form.Command.ShippingUnit = defaultAddress.Unit;
                form.Command.ShippingPostalCode = defaultAddress.PostalCode;
                form.Command.ShippingPhone = defaultAddress.PhoneNumber;
                if (string.IsNullOrWhiteSpace(form.Command.CustomerName) && defaultAddress.FirstName is not null)
                    form.Command.CustomerName = $"{defaultAddress.FirstName} {defaultAddress.LastName}".Trim();
            }
        }

        return form;
    }

    public async Task<OperationResult<CheckoutResultDto>> PlaceOrderAsync(CheckoutCommand command,
        CancellationToken cancellationToken = default)
    {
        // -------------------------------------------------------------- validation
        if (string.IsNullOrWhiteSpace(command.CustomerName))
            return OperationResult<CheckoutResultDto>.Error("نام و نام خانوادگی را وارد کنید.");

        if (!SlugHelper.ToLatinDigits(command.CustomerPhone).Trim().IsValidMobile())
            return OperationResult<CheckoutResultDto>.Error("شماره موبایل معتبر نیست.");

        if (string.IsNullOrWhiteSpace(command.ShippingProvince) || string.IsNullOrWhiteSpace(command.ShippingCity))
            return OperationResult<CheckoutResultDto>.Error("استان و شهر را وارد کنید.");

        if (string.IsNullOrWhiteSpace(command.ShippingAddress))
            return OperationResult<CheckoutResultDto>.Error("نشانی کامل را وارد کنید.");

        var postalCode = SlugHelper.ToLatinDigits(command.ShippingPostalCode).Trim();
        if (postalCode.Length != 10 || !postalCode.All(char.IsDigit))
            return OperationResult<CheckoutResultDto>.Error("کد پستی باید ۱۰ رقم باشد.");

        var shippingPhone = SlugHelper.ToLatinDigits(command.ShippingPhone).Trim();
        if (!shippingPhone.IsValidMobile())
            return OperationResult<CheckoutResultDto>.Error("شماره تماس گیرنده معتبر نیست.");

        // ------------------------------------------------------------------- cart
        var cart = await cartService.GetCartDtoAsync(command.UserId, command.SessionKey, command.CouponCode,
            command.ShippingMethodId, cancellationToken);

        if (cart.Items.Count == 0)
            return OperationResult<CheckoutResultDto>.Error("سبد خرید شما خالی است.");

        var outOfStock = cart.Items.FirstOrDefault(item => item.Quantity > item.StockQuantity);
        if (outOfStock is not null)
            return OperationResult<CheckoutResultDto>.Error($"موجودی محصول «{outOfStock.Product.Name}» کافی نیست.");

        // --------------------------------------------------------------- payments
        var settings = await settingService.GetStorefrontSettingsAsync();
        var allowed = new List<PaymentMethod>();
        if (settings.EnableOnlinePayment) allowed.Add(PaymentMethod.OnlineGateway);
        if (settings.EnableBankTransfer) allowed.Add(PaymentMethod.BankTransfer);
        if (settings.EnableCashOnDelivery) allowed.Add(PaymentMethod.CashOnDelivery);

        if (!allowed.Contains(command.PaymentMethod))
            return OperationResult<CheckoutResultDto>.Error("روش پرداخت انتخاب‌شده در حال حاضر فعال نیست.");

        // -------------------------------------------------------------- amounts
        Guid? couponId = null;
        var couponDiscount = 0m;
        if (!string.IsNullOrWhiteSpace(cart.CouponCode))
        {
            var validation = await couponService.ValidateAsync(cart.CouponCode!, cart.Subtotal, command.UserId, cancellationToken);
            if (validation.IsValid)
            {
                couponId = validation.CouponId;
                couponDiscount = validation.DiscountAmount;
            }
        }

        var shippingMethod = command.ShippingMethodId.HasValue
            ? await shippingMethodService.GetAsync(command.ShippingMethodId.Value)
            : null;

        var shippingCost = shippingMethod?.CalculateCost(Math.Max(0, cart.Subtotal - couponDiscount)) ?? 0m;
        var payable = Math.Max(0, cart.Subtotal - couponDiscount) + shippingCost;

        // ----------------------------------------------------------------- order
        var order = new Order
        {
            UserId = command.UserId,
            OrderNumber = await GenerateOrderNumberAsync(cancellationToken),
            Status = OrderStatus.Pending,
            PaymentMethod = command.PaymentMethod,
            PaymentStatus = PaymentStatus.None,
            SubtotalAmount = cart.Subtotal,
            DiscountAmount = couponDiscount,
            ShippingAmount = shippingCost,
            PayableAmount = payable,
            ShippingMethodName = shippingMethod?.Name,
            ShippingMethodId = shippingMethod?.Id,
            CouponId = couponId,
            RequestedDeliveryDate = command.RequestedDeliveryDate,
            CustomerName = command.CustomerName.Trim(),
            CustomerPhone = SlugHelper.ToLatinDigits(command.CustomerPhone).Trim(),
            ShippingProvince = command.ShippingProvince.Trim(),
            ShippingCity = command.ShippingCity.Trim(),
            ShippingAddress = command.ShippingAddress.Trim(),
            ShippingBuildingNumber = command.ShippingBuildingNumber,
            ShippingUnit = command.ShippingUnit,
            ShippingPostalCode = postalCode,
            ShippingPhone = shippingPhone,
            CustomerNote = command.CustomerNote,
        };

        foreach (var item in cart.Items)
        {
            order.Items.Add(new OrderItem
            {
                ProductId = item.Product.Id,
                ProductName = item.Product.Name,
                SKU = item.Product.SKU,
                UnitPrice = item.Product.FinalPrice,
                DiscountAmount = (item.Product.Price - item.Product.FinalPrice) * item.Quantity,
                Quantity = item.Quantity,
                TotalAmount = item.Product.FinalPrice * item.Quantity,
            });
        }

        context.Orders.Add(order);

        // Stock is reserved at order creation; releasing it happens when the order
        // is cancelled, so two customers can never buy the same last item.
        var productIds = cart.Items.Select(item => item.Product.Id).ToList();
        var products = await context.Products.AsTracking()
            .Where(product => productIds.Contains(product.Id))
            .ToListAsync(cancellationToken);

        foreach (var item in cart.Items)
        {
            var product = products.FirstOrDefault(product => product.Id == item.Product.Id);
            if (product is null)
                continue;

            product.StockQuantity = Math.Max(0, product.StockQuantity - item.Quantity);
        }

        context.OrderStatusHistories.Add(new OrderStatusHistory
        {
            OrderId = order.Id,
            Status = OrderStatus.Pending,
            PreviousStatus = OrderStatus.None,
            Note = "سفارش در سایت ثبت شد.",
            IsCustomerVisible = true,
        });

        await context.SaveChangesAsync(cancellationToken);

        // ------------------------------------------------------------- follow up
        if (couponId.HasValue)
            await couponService.RegisterUsageAsync(couponId.Value, order.Id, command.UserId, couponDiscount, cancellationToken);

        var redirectUrl = $"/profile/orders/{order.Id}";

        if (command.PaymentMethod == PaymentMethod.OnlineGateway)
        {
            var payment = await paymentService.StartAsync(order.Id, command.UserId, null, cancellationToken);
            redirectUrl = payment.IsSuccess() && payment.Data is not null
                ? payment.Data.RedirectUrl
                : $"/profile/orders/{order.Id}?paymentError=1";
        }
        else if (command.PaymentMethod == PaymentMethod.BankTransfer)
        {
            await bankTransferService.EnsureForOrderAsync(order.Id, cancellationToken);
        }

        await cartService.ClearAsync(command.UserId, command.SessionKey, cancellationToken);

        if (command.UserId.HasValue)
        {
            await notificationService.NotifyUserAsync(command.UserId.Value, "ثبت سفارش",
                $"سفارش {order.OrderNumber} با مبلغ {payable.ToToman()} ثبت شد.",
                NotificationType.Order, $"/profile/orders/{order.Id}", cancellationToken);
        }
        else
        {
            await notificationService.NotifyPhoneAsync(order.CustomerPhone,
                $"سفارش {order.OrderNumber} با مبلغ {payable.ToToman()} ثبت شد.", NotificationType.Order, cancellationToken);
        }

        await notificationService.NotifyStaffAsync("سفارش جدید",
            $"سفارش {order.OrderNumber} به مبلغ {payable.ToToman()} ثبت شد.",
            NotificationType.Order, $"/admin/orders/details/{order.Id}", cancellationToken);

        return OperationResult<CheckoutResultDto>.Success(new CheckoutResultDto
        {
            OrderId = order.Id,
            OrderNumber = order.OrderNumber,
            PayableAmount = payable,
            PaymentMethod = command.PaymentMethod,
            RedirectUrl = redirectUrl,
        }, "سفارش شما با موفقیت ثبت شد.");
    }

    public async Task<string> GenerateOrderNumberAsync(CancellationToken cancellationToken = default)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var candidate = $"FR-{DateTime.UtcNow:yyMMdd}-{Random.Shared.Next(1000, 9999)}";
            if (!await context.Orders.AnyAsync(order => order.OrderNumber == candidate, cancellationToken))
                return candidate;
        }

        return $"FR-{DateTime.UtcNow:yyMMddHHmmss}";
    }
}
