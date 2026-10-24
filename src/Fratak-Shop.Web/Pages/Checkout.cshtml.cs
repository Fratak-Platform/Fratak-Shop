using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Fratak_Shop.Core.DTOs.Locations.Query;
using Fratak_Shop.Core.DTOs.Orders.Command;
using Fratak_Shop.Core.DTOs.Shipping;
using Fratak_Shop.Core.DTOs.Shopping.Query;
using Fratak_Shop.Core.DTOs.Users.Query;
using Fratak_Shop.Core.Services.Locations;
using Fratak_Shop.Core.Services.Orders;
using Fratak_Shop.Core.Services.Shipping;
using Fratak_Shop.Core.Services.Shopping;
using Fratak_Shop.Core.Services.Users;
using Fratak_Shop.Data.Entities.Orders;
using Fratak_Shop.Data.Entities.Pages;
using Fratak_Shop.Web.Infrastructure;
using Fratak_Shop.Web.Models;
using Fratak_Shop.Web.Services;
using Fratak_Shop.Web.Services.Payments;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Pages;

/// <summary>
/// Checkout: delivery address, shipping method (with delivery day / time slot for
/// courier methods) and payment method. Everything offered here – methods, costs,
/// slots, cash on delivery, wallet – comes from the database settings; the server
/// re-validates every choice when the order is placed.
/// </summary>
public class CheckoutModel(
    ICartSessionService cartSession,
    ICartService cartService,
    IUserAddressService addressService,
    IUserService userService,
    ILocationService locationService,
    IShippingService shippingService,
    IOrderService orderService,
    IPaymentFlowService paymentFlow,
    ISiteContext site,
    IPageMetaService pageMeta) : ShopPageModel
{
    public CartDto Cart { get; private set; } = null!;
    public List<UserAddressDto> Addresses { get; private set; } = [];
    public UserAddressDto? SelectedAddress { get; private set; }
    public List<LocationDto> Provinces { get; private set; } = [];
    public List<ShippingOption> ShippingOptions { get; private set; } = [];
    public List<PaymentChoice> PaymentChoices { get; private set; } = [];
    public decimal WalletBalance { get; private set; }

    /// <summary>Amount of the items (after product discounts) – base for free shipping and minimum order.</summary>
    public decimal ItemsAmount => Cart.PayableAmount;

    public decimal MinimumOrderAmount => site.Settings.MinimumOrderAmount;
    public bool IsBelowMinimum => MinimumOrderAmount > 0 && ItemsAmount < MinimumOrderAmount;

    [BindProperty]
    public CheckoutInput Input { get; set; } = new();

    [BindProperty]
    public AddressInput Address { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(Guid? addressId)
    {
        Input.AddressId = addressId;
        if (!await LoadAsync())
            return RedirectToPage("/Cart");

        var firstOption = ShippingOptions.FirstOrDefault();
        Input.ShippingMethodId = firstOption?.Method.Id;

        var firstSlot = firstOption?.Days.SelectMany(day => day.Slots.Where(s => s.IsAvailable).Select(s => (day.Date, s.SlotId))).FirstOrDefault();
        if (firstSlot is { SlotId: var slotId } && slotId != Guid.Empty)
            Input.DeliverySlot = SlotValue(firstSlot.Value.Date, slotId);

        Input.PaymentMethod = PaymentChoices.FirstOrDefault(choice => choice.IsAvailable)?.Method ?? PaymentMethod.OnlineGateway;
        return Page();
    }

    public async Task<IActionResult> OnPostAddAddressAsync()
    {
        ModelState.Clear();
        if (!TryValidateModel(Address, nameof(Address)))
        {
            ErrorMessage = FirstError() ?? "اطلاعات آدرس معتبر نیست.";
            return RedirectToPage();
        }

        var result = await addressService.CreateAddressAsync(Address.ToCreateCommand(CurrentUserId!.Value));
        ShowResult(result);
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostPlaceOrderAsync()
    {
        if (!await LoadAsync())
            return RedirectToPage("/Cart");

        ModelState.Clear();
        if (!TryValidateModel(Input, nameof(Input)))
        {
            ErrorMessage = FirstError() ?? "اطلاعات سفارش کامل نیست.";
            return Page();
        }

        if (Cart.Items.Any(item => !item.HasEnoughStock))
        {
            ErrorMessage = "موجودی برخی از کالاهای سبد خرید کافی نیست؛ سبد خرید را بررسی کنید.";
            return RedirectToPage("/Cart");
        }

        var address = Addresses.FirstOrDefault(a => a.Id == Input.AddressId);
        if (address is null)
        {
            ErrorMessage = "آدرس تحویل سفارش را انتخاب کنید.";
            return Page();
        }

        var option = ShippingOptions.FirstOrDefault(o => o.Method.Id == Input.ShippingMethodId);
        if (option is null)
        {
            ErrorMessage = "روش ارسال انتخاب‌شده برای این آدرس در دسترس نیست.";
            return Page();
        }

        DateOnly? deliveryDate = null;
        Guid? timeSlotId = null;
        if (option.Method.RequiresTimeSlot)
        {
            if (!TryParseSlot(Input.DeliverySlot, out var date, out var slot))
            {
                ErrorMessage = "روز و بازه زمانی تحویل را انتخاب کنید.";
                return Page();
            }

            deliveryDate = date;
            timeSlotId = slot;
        }

        var user = await userService.GetUserByIdAsync(CurrentUserId!.Value);
        var recipient = $"{address.FirstName} {address.LastName}".Trim();

        var result = await orderService.CreateOrderAsync(new CreateOrderCommand
        {
            UserId = CurrentUserId,
            PaymentMethod = Input.PaymentMethod,
            ShippingMethodId = option.Method.Id,
            ShippingProvinceId = address.ProvinceId,
            DeliveryDate = deliveryDate,
            DeliveryTimeSlotId = timeSlotId,
            CustomerName = string.IsNullOrWhiteSpace(recipient) ? user?.FullName ?? user?.PhoneNumber ?? "مشتری" : recipient,
            CustomerPhone = user?.PhoneNumber ?? address.PhoneNumber,
            ShippingProvince = address.ProvinceName ?? string.Empty,
            ShippingCity = address.CityName ?? string.Empty,
            ShippingAddress = address.AddressLine,
            ShippingBuildingNumber = address.BuildingNumber,
            ShippingUnit = address.Unit,
            ShippingPostalCode = address.PostalCode,
            ShippingPhone = address.PhoneNumber,
            CustomerNote = string.IsNullOrWhiteSpace(Input.Note) ? null : Input.Note.Trim(),
            Items = Cart.Items
                .Select(item => new OrderItemRequest { ProductId = item.ProductId, Quantity = item.Quantity })
                .ToList(),
        });

        if (!Succeeded(result) || result.Data is null)
        {
            ErrorMessage = result.Message;
            return Page();
        }

        await cartService.ClearCartAsync(Cart.Id);
        var order = result.Data;

        if (order.PaymentMethod == PaymentMethod.OnlineGateway)
        {
            var start = await paymentFlow.StartOrderPaymentAsync(order, CurrentUserId!.Value);
            if (start.Succeeded)
                return Redirect(start.RedirectUrl!);

            ErrorMessage = start.Error;
            return RedirectToPage("/CheckoutFailed", new { orderNumber = order.OrderNumber });
        }

        SuccessMessage = result.Message;
        return RedirectToPage("/CheckoutComplete", new { orderNumber = order.OrderNumber });
    }

    public static string SlotValue(DateOnly date, Guid slotId) =>
        $"{date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}|{slotId}";

    private static bool TryParseSlot(string? value, out DateOnly date, out Guid slotId)
    {
        date = default;
        slotId = Guid.Empty;
        var parts = value?.Split('|');
        return parts is { Length: 2 }
               && DateOnly.TryParseExact(parts[0], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date)
               && Guid.TryParse(parts[1], out slotId);
    }

    private async Task<bool> LoadAsync()
    {
        var cart = await cartSession.GetCartAsync();
        if (cart is null || cart.Items.Count == 0)
            return false;

        Cart = cart;
        var userId = CurrentUserId!.Value;
        Addresses = await addressService.GetAddressesByUserAsync(userId);
        Provinces = await locationService.GetProvincesAsync();

        SelectedAddress = Addresses.FirstOrDefault(a => a.Id == Input.AddressId)
                          ?? Addresses.FirstOrDefault(a => a.IsDefault)
                          ?? Addresses.FirstOrDefault();
        Input.AddressId = SelectedAddress?.Id;

        ShippingOptions = SelectedAddress is null
            ? []
            : await shippingService.GetOptionsAsync(SelectedAddress.ProvinceId, ItemsAmount, DateTimeOffset.UtcNow, HttpContext.RequestAborted);

        var user = await userService.GetUserByIdAsync(userId);
        WalletBalance = user?.WalletBalance ?? 0;
        PaymentChoices = BuildPaymentChoices();

        await pageMeta.ApplyAsync(ViewData, SitePage.Checkout, "تکمیل سفارش");
        ViewData[PageMetaKeys.Robots] = "noindex, nofollow";
        return true;
    }

    private List<PaymentChoice> BuildPaymentChoices()
    {
        var settings = site.Settings;
        var choices = new List<PaymentChoice>();

        if (settings.HasZarinPal)
            choices.Add(new PaymentChoice(PaymentMethod.OnlineGateway, "پرداخت اینترنتی", "CreditCard", "درگاه امن زرین‌پال (همه کارت‌های عضو شتاب)", true));

        if (settings.CanPayWithWallet)
            choices.Add(new PaymentChoice(PaymentMethod.Wallet, "کیف پول", "Wallet", $"موجودی: {Helpers.Format.Toman(WalletBalance)}", WalletBalance > 0));

        if (settings.IsCashOnDeliveryEnabled)
        {
            var note = settings.CashOnDeliveryMaxAmount > 0
                ? $"با کارتخوان هنگام تحویل (تا {Helpers.Format.Toman(settings.CashOnDeliveryMaxAmount)})"
                : "با کارتخوان هنگام تحویل";
            choices.Add(new PaymentChoice(PaymentMethod.CashOnDelivery, "پرداخت در محل", "Truck", note, true));
        }

        return choices;
    }

    private string? FirstError() =>
        ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).FirstOrDefault(e => !string.IsNullOrWhiteSpace(e));

    public record PaymentChoice(PaymentMethod Method, string Title, string Icon, string Note, bool IsAvailable);

    public class CheckoutInput
    {
        [Required(ErrorMessage = "آدرس تحویل سفارش را انتخاب کنید.")]
        public Guid? AddressId { get; set; }

        [Required(ErrorMessage = "روش ارسال را انتخاب کنید.")]
        public Guid? ShippingMethodId { get; set; }

        /// <summary>"yyyy-MM-dd|slotId" for methods with delivery time slots.</summary>
        [MaxLength(60)]
        public string? DeliverySlot { get; set; }

        public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.OnlineGateway;

        [MaxLength(1000, ErrorMessage = "توضیحات حداکثر ۱۰۰۰ کاراکتر است.")]
        public string? Note { get; set; }
    }
}
