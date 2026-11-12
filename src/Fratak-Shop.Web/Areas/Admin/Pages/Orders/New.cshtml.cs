using Fratak_Shop.Core.DTOs.Locations.Query;
using Fratak_Shop.Core.DTOs.Orders.Command;
using Fratak_Shop.Core.DTOs.Users.Command;
using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Core.Services.Locations;
using Fratak_Shop.Core.Services.Orders;
using Fratak_Shop.Core.Services.Settings;
using Fratak_Shop.Core.Services.Users;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Web.Helpers;
using Fratak_Shop.Web.Infrastructure.Admin;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Areas.Admin.Pages.Orders;

/// <summary>
/// «ثبت سفارش برای مشتری» در پنل (<c>/admin/orders/new</c>).
/// <para>
/// سه گام در یک صفحه: ۱) پیدا کردن مشتری با موبایل یا ساخت حساب تازه، ۲) انتخاب/افزودن آدرس،
/// ۳) افزودن کالاها (جست‌وجوی زنده با <c>admin-orders.js</c>)، روش ارسال، کد تخفیف و پیام.
/// با ثبت، <see cref="IStaffOrderService"/> سفارش را با قوانین تسویه حساب سایت می‌سازد و لینک پرداخت پیامک می‌شود.
/// </para>
/// </summary>
[AdminPermission(AdminPermissions.OrdersCreate)]
public class NewModel(
    IStaffOrderService staffOrders,
    ILocationService locations,
    IUserAddressService addresses,
    IStoreSettingService settings) : AdminPageModel
{
    /// <summary>موبایل جست‌وجوشده (گام ۱).</summary>
    [BindProperty(SupportsGet = true, Name = "phone")] public string? Phone { get; set; }

    /// <summary>شناسهٔ مشتری انتخاب‌شده (بعد از جست‌وجو یا ساخت حساب در آدرس صفحه می‌ماند).</summary>
    [BindProperty(SupportsGet = true, Name = "customer")] public Guid? CustomerId { get; set; }

    /// <summary>بعد از افزودن آدرس تازه، همان آدرس انتخاب شود.</summary>
    [BindProperty(SupportsGet = true, Name = "fresh")] public bool FreshAddress { get; set; }

    public StaffOrderCustomer? Customer { get; private set; }
    public Guid? SelectedAddressId { get; private set; }
    public List<LocationDto> Provinces { get; private set; } = [];
    public List<StaffOrderShipping> ShippingMethods { get; private set; } = [];
    public bool OnlinePaymentReady { get; private set; }
    public bool CouponsEnabled { get; private set; }
    public int LinkHours { get; private set; }

    /// <summary>موبایل معتبر جست‌وجو شد ولی حسابی نبود ← فرم ساخت حساب نمایش داده می‌شود.</summary>
    public string? NewCustomerPhone { get; private set; }

    /// <summary>فرم ارسال‌شده‌ای که ثبتش خطا داد (برای پر کردن دوبارهٔ فرم).</summary>
    public StaffOrderCommand? Posted { get; private set; }

    /// <summary>اقلام فرم خطادار با نام و قیمت، به‌صورت JSON برای <c>admin-orders.js</c>.</summary>
    public string PostedItemsJson { get; private set; } = "[]";

    public Task OnGetAsync() => LoadAsync();

    private async Task LoadAsync()
    {
        var store = await settings.GetAsync();
        OnlinePaymentReady = store.HasZarinPal;
        CouponsEnabled = store.IsCouponEnabled;
        LinkHours = store.PaymentLinkHours;

        if (CustomerId is null && string.IsNullOrWhiteSpace(Phone))
            return;

        Customer = await staffOrders.FindCustomerAsync(CustomerId, Phone);
        if (Customer is null)
        {
            if (CustomerId is null)
            {
                NewCustomerPhone = StaffOrderService.NormalizePhone(Phone);
                if (NewCustomerPhone is null)
                    TempData["ErrorMessage"] = "شمارهٔ موبایل معتبر نیست (مثال: 09121234567).";
            }
            return;
        }

        SelectedAddressId = FreshAddress
            ? Customer.Addresses.OrderByDescending(a => a.CreatedAt).Select(a => (Guid?)a.Id).FirstOrDefault()
            : Customer.Addresses.Where(a => a.IsDefault).Select(a => (Guid?)a.Id).FirstOrDefault() ?? Customer.Addresses.Select(a => (Guid?)a.Id).FirstOrDefault();
        Provinces = await locations.GetProvincesAsync();
        ShippingMethods = await staffOrders.GetShippingMethodsAsync();
    }

    /// <summary>
    /// جست‌وجوی زندهٔ محصول برای فرم (JSON). نمونهٔ پاسخ:
    /// <c>[{"id":"…","name":"…","sku":"…","price":450000,"finalPrice":420000,"stock":12,"image":"/uploads/…"}]</c>
    /// </summary>
    public async Task<IActionResult> OnGetProductsAsync(string? q)
    {
        var items = await staffOrders.SearchProductsAsync(q);
        return new JsonResult(items.Select(p => new
        {
            id = p.Id,
            name = p.Name,
            sku = p.Sku,
            price = p.Price,
            finalPrice = p.FinalPrice,
            stock = p.Stock,
            image = ImageHelper.Sized(ImageHelper.Product(p.Image), 160),
        }));
    }

    /// <summary>ساخت حساب مشتری تازه (فقط موبایل و نام) و رفتن به گام آدرس.</summary>
    public async Task<IActionResult> OnPostCustomerAsync(string? phone, string? firstName, string? lastName)
    {
        var result = await staffOrders.EnsureCustomerAsync(phone, firstName, lastName);
        ShowResult(result);
        return result.IsSuccess()
            ? Redirect($"/admin/orders/new?customer={result.Data}")
            : Redirect("/admin/orders/new?phone=" + global::System.Uri.EscapeDataString(phone ?? ""));
    }

    /// <summary>افزودن آدرس تازه برای مشتری انتخاب‌شده (با همان اعتبارسنجی آدرس‌های پروفایل).</summary>
    public async Task<IActionResult> OnPostAddressAsync(Guid customer, CreateUserAddressCommand address)
    {
        address.UserId = customer;
        address.Title = string.IsNullOrWhiteSpace(address.Title) ? "آدرس" : address.Title;
        address.PhoneNumber = SearchText.NormalizeDigits(address.PhoneNumber);
        address.PostalCode = SearchText.NormalizeDigits(address.PostalCode);
        address.Latitude = null;
        address.Longitude = null;
        var result = await addresses.CreateAddressAsync(address);
        ShowResult(result);
        return Redirect($"/admin/orders/new?customer={customer}" + (result.IsSuccess() ? "&fresh=true" : "") + "#address");
    }

    /// <summary>ثبت نهایی سفارش و ارسال لینک پرداخت؛ موفق ← صفحهٔ جزئیات سفارش.</summary>
    public async Task<IActionResult> OnPostCreateAsync(StaffOrderCommand input)
    {
        input.Items = input.Items.Where(i => i.ProductId != global::System.Guid.Empty && i.Quantity > 0)
            .GroupBy(i => i.ProductId)
            .Select(g => new OrderItemRequest { ProductId = g.Key, Quantity = g.Sum(i => i.Quantity) })
            .ToList();

        var baseUrl = $"{Request.Scheme}://{Request.Host}";
        var result = await staffOrders.CreateAsync(input, ActorId, ActorName, baseUrl);
        ShowResult(result);
        if (result.IsSuccess() && result.Data is { } created)
            return Redirect($"/admin/orders/{created.OrderId}#payment-link");

        // خطا (مثلاً موجودی ناکافی یا کد تخفیف نامعتبر): همان فرم با اقلام واردشده دوباره نمایش داده می‌شود.
        CustomerId = input.UserId;
        Phone = null;
        await LoadAsync();
        Posted = input;
        SelectedAddressId = input.AddressId;
        var products = (await staffOrders.GetProductsAsync(input.Items.Select(i => i.ProductId).ToList())).ToDictionary(p => p.Id);
        PostedItemsJson = global::System.Text.Json.JsonSerializer.Serialize(input.Items
            .Where(i => products.ContainsKey(i.ProductId))
            .Select(i =>
            {
                var p = products[i.ProductId];
                return new { id = p.Id, name = p.Name, sku = p.Sku, price = p.Price, finalPrice = p.FinalPrice, stock = p.Stock,
                    image = ImageHelper.Sized(ImageHelper.Product(p.Image), 160), qty = i.Quantity };
            }));
        return Page();
    }
}
