using System.ComponentModel.DataAnnotations;
using Fratak_Shop.Core.DTOs.Locations.Query;
using Fratak_Shop.Core.DTOs.Orders.Command;
using Fratak_Shop.Core.DTOs.Shopping.Query;
using Fratak_Shop.Core.DTOs.Users.Query;
using Fratak_Shop.Core.Services.Locations;
using Fratak_Shop.Core.Services.Orders;
using Fratak_Shop.Core.Services.Shopping;
using Fratak_Shop.Core.Services.Users;
using Fratak_Shop.Data.Entities.Orders;
using Fratak_Shop.Data.Entities.Pages;
using Fratak_Shop.Web.Infrastructure;
using Fratak_Shop.Web.Models;
using Fratak_Shop.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Fratak_Shop.Web.Pages;

/// <summary>
/// Checkout: delivery address, delivery day, shipping and payment method.
/// Requires a signed-in customer (see the authorization convention in Program.cs).
/// </summary>
public class CheckoutModel(
    ICartSessionService cartSession,
    ICartService cartService,
    IUserAddressService addressService,
    IUserService userService,
    ILocationService locationService,
    IOrderService orderService,
    IPageMetaService pageMeta,
    IOptions<ShopOptions> shopOptions) : ShopPageModel
{
    public CartDto Cart { get; private set; } = null!;
    public List<UserAddressDto> Addresses { get; private set; } = [];
    public List<LocationDto> Provinces { get; private set; } = [];
    public List<DeliveryDayOption> DeliveryDays { get; private set; } = [];
    public ShopOptions Shop => shopOptions.Value;

    public bool IsFreeShipping => Shop.FreeShippingThreshold > 0 && Cart.PayableAmount >= Shop.FreeShippingThreshold;

    public static readonly (PaymentMethod Method, string Title, string Icon)[] PaymentMethods =
    [
        (PaymentMethod.OnlineGateway, "پرداخت اینترنتی", "Wallet"),
        (PaymentMethod.CashOnDelivery, "پرداخت در محل", "Truck"),
    ];

    [BindProperty]
    public CheckoutInput Input { get; set; } = new();

    [BindProperty]
    public AddressInput Address { get; set; } = new();

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await LoadAsync())
            return RedirectToPage("/Cart");

        Input.AddressId = Addresses.FirstOrDefault(a => a.IsDefault)?.Id ?? Addresses.FirstOrDefault()?.Id;
        Input.ShippingMethod = Shop.ShippingMethods.FirstOrDefault()?.Code;
        Input.DeliveryDate = DeliveryDays.FirstOrDefault()?.Value;

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

        var shipping = Shop.ShippingMethods.FirstOrDefault(m => m.Code == Input.ShippingMethod);
        if (shipping is null)
        {
            ErrorMessage = "روش ارسال را انتخاب کنید.";
            return Page();
        }

        var deliveryDay = DeliveryDays.FirstOrDefault(d => d.Value == Input.DeliveryDate);
        if (!PaymentMethods.Any(m => m.Method == Input.PaymentMethod))
        {
            ErrorMessage = "روش پرداخت را انتخاب کنید.";
            return Page();
        }

        var user = await userService.GetUserByIdAsync(CurrentUserId!.Value);
        var recipient = $"{address.FirstName} {address.LastName}".Trim();

        var result = await orderService.CreateOrderAsync(new CreateOrderCommand
        {
            UserId = CurrentUserId,
            PaymentMethod = Input.PaymentMethod,
            ShippingMethod = shipping.Title,
            RequestedDeliveryDate = deliveryDay?.Date,
            ShippingAmount = IsFreeShipping ? 0 : shipping.Cost,
            CustomerName = string.IsNullOrWhiteSpace(recipient) ? user?.FullName ?? user?.PhoneNumber ?? "مشتری" : recipient,
            CustomerPhone = user?.PhoneNumber ?? address.PhoneNumber,
            ShippingProvince = address.ProvinceName ?? string.Empty,
            ShippingCity = address.CityName ?? string.Empty,
            ShippingAddress = address.AddressLine,
            ShippingBuildingNumber = address.BuildingNumber,
            ShippingUnit = address.Unit,
            ShippingPostalCode = address.PostalCode,
            ShippingPhone = address.PhoneNumber,
            CustomerNote = Input.Note,
            Items = Cart.Items
                .Select(item => new OrderItemRequest { ProductId = item.ProductId, Quantity = item.Quantity })
                .ToList(),
        });

        if (result.Status != Core.Utilities.EnOperationResultStatus.Success)
        {
            ErrorMessage = result.Message;
            return Page();
        }

        await cartService.ClearCartAsync(Cart.Id);

        var order = result.Data;
        if (order.PaymentMethod == PaymentMethod.OnlineGateway)
            return RedirectToPage("/Payment/Start", new { orderNumber = order.OrderNumber });

        SuccessMessage = result.Message;
        return RedirectToPage("/CheckoutComplete", new { orderNumber = order.OrderNumber });
    }

    public decimal ShippingCost(ShippingMethodOption method) => IsFreeShipping ? 0 : method.Cost;

    private async Task<bool> LoadAsync()
    {
        var cart = await cartSession.GetCartAsync();
        if (cart is null || cart.Items.Count == 0)
            return false;

        Cart = cart;
        Addresses = await addressService.GetAddressesByUserAsync(CurrentUserId!.Value);
        Provinces = await locationService.GetProvincesAsync();
        DeliveryDays = BuildDeliveryDays();

        await pageMeta.ApplyAsync(ViewData, SitePage.Checkout, "تکمیل سفارش");
        ViewData[PageMetaKeys.Robots] = "noindex, nofollow";
        return true;
    }

    private List<DeliveryDayOption> BuildDeliveryDays()
    {
        var firstOffset = Shop.ShippingMethods.Count > 0 ? Shop.ShippingMethods.Min(m => m.MinDeliveryDays) : 1;
        var days = new List<DeliveryDayOption>();
        var date = DateTimeOffset.UtcNow.Date.AddDays(Math.Max(1, firstOffset));

        while (days.Count < 6)
        {
            var moment = new DateTimeOffset(date, TimeSpan.Zero);

            // Friday is the weekly holiday in Iran – no deliveries.
            if (date.DayOfWeek != DayOfWeek.Friday)
                days.Add(new DeliveryDayOption(moment.ToString("yyyy-MM-dd"), moment));

            date = date.AddDays(1);
        }

        return days;
    }

    private string? FirstError() =>
        ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).FirstOrDefault(e => !string.IsNullOrWhiteSpace(e));

    public record DeliveryDayOption(string Value, DateTimeOffset Date);

    public class CheckoutInput
    {
        [Required(ErrorMessage = "آدرس تحویل سفارش را انتخاب کنید.")]
        public Guid? AddressId { get; set; }

        [Required(ErrorMessage = "روش ارسال را انتخاب کنید.")]
        public string? ShippingMethod { get; set; }

        public string? DeliveryDate { get; set; }

        public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.OnlineGateway;

        [MaxLength(1000, ErrorMessage = "توضیحات حداکثر ۱۰۰۰ کاراکتر است.")]
        public string? Note { get; set; }
    }
}
