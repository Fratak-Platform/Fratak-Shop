using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Core.DTOs.Checkout.Query;
using Fratak_Shop.Core.DTOs.Orders.Command;
using Fratak_Shop.Core.DTOs.Seo.Query;
using Fratak_Shop.Core.Services.Checkout;
using Fratak_Shop.Core.Services.Locations;
using Fratak_Shop.Core.Services.Navigation;
using Fratak_Shop.Core.Services.Notifications;
using Fratak_Shop.Core.Services.Seo;
using Fratak_Shop.Core.Services.Settings;
using Fratak_Shop.Core.Services.Shopping;
using Fratak_Shop.Core.Services.Users;
using Fratak_Shop.Data.Entities.Locations;
using Fratak_Shop.Data.Entities.Orders;
using Fratak_Shop.Data.Entities.Seo;
using Fratak_Shop.Web.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Pages;

public class CheckoutModel(
    ISiteSettingService settings,
    IMenuService menus,
    ICartService cart,
    INotificationService notifications,
    ISeoService seoService,
    ICheckoutService checkout,
    ILocationService locations,
    IUserAddressService addresses)
    : FratakPageModel(settings, menus, cart, notifications, seoService)
{
    public CheckoutSummaryDto Summary { get; private set; } = null!;

    public List<Core.DTOs.Locations.Query.LocationDto> Provinces { get; private set; } = [];

    public List<Data.Entities.Users.UserAddress> SavedAddresses { get; private set; } = [];

    [BindProperty]
    public CheckoutCommand Input { get; set; } = new();

    [BindProperty(SupportsGet = true, Name = "shippingMethodId")]
    public Guid? ShippingMethodId { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var summary = await checkout.GetSummaryAsync(CurrentUserId, CartSessionKey, ShippingMethodId, null);
        if (summary is null || summary.Cart.IsEmpty)
        {
            Notify("سبد خرید شما خالی است.", "error");
            return RedirectToPage("/Cart");
        }

        Summary = summary;
        Input = (await checkout.GetFormAsync(CurrentUserId, CartSessionKey, ShippingMethodId)).Command;
        Input.PaymentMethod = summary.AvailablePaymentMethods.Contains(PaymentMethod.OnlineGateway)
            ? PaymentMethod.OnlineGateway
            : summary.AvailablePaymentMethods.FirstOrDefault();

        var roots = await locations.GetActiveLocationsAsync();
        var country = roots.FirstOrDefault(item => item.Type == LocationType.Country);
        Provinces = country is null ? roots.Where(item => item.Type == LocationType.Province).ToList() : await locations.GetChildrenByParentIdAsync(country.Id);
        if (CurrentUserId is { } userId)
            SavedAddresses = await addresses.GetAddressesByUserAsync(userId);

        ApplySeo(null, new SeoFallback
        {
            SiteName = Site.SiteName,
            SiteUrl = Site.SiteUrl,
            Path = "/checkout",
            ContentTitle = "تسویه حساب",
            ShortDescription = "ثبت اطلاعات ارسال، انتخاب روش پرداخت (آنلاین، کارت به کارت، پرداخت در محل) و نهایی‌کردن سفارش.",
            EntityType = SeoEntityType.Page,
            ForceNoIndex = true,
        });

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        Input.UserId = CurrentUserId;
        Input.SessionKey = CartSessionKey;

        var result = await checkout.PlaceOrderAsync(Input);

        if (!result.IsSuccess() || result.Data is null)
        {
            Notify(result.Message, "error");
            return RedirectToPage("/Checkout", new { shippingMethodId = Input.ShippingMethodId });
        }

        Notify($"سفارش {result.Data.OrderNumber} با موفقیت ثبت شد.");

        return Redirect(result.Data.RedirectUrl);
    }
}
