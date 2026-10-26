using System.Net;
using Fratak_Shop.Core.DTOs.Wallets;
using Fratak_Shop.Core.Services.Wallets;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Entities.Categories;
using Fratak_Shop.Data.Entities.Locations;
using Fratak_Shop.Data.Entities.Orders;
using Fratak_Shop.Data.Entities.Products;
using Fratak_Shop.Data.Entities.Users;
using Fratak_Shop.Data.Entities.Wallets;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Fratak_Shop.Tests.E2E.Infrastructure;

/// <summary>
/// سازندهٔ دادهٔ آزمون: هر آزمون مشتری و کالای «خودش» را می‌سازد تا آزمون‌ها مستقل بمانند و ترتیب اجرا مهم نباشد.
/// </summary>
public static class TestData
{
    public sealed record Customer(Guid Id, string Phone, string Password, Guid AddressId);

    /// <summary>مشتری تازه با رمز عبور، شمارهٔ تأییدشده و یک آدرس پیش‌فرض (تهران اگر باشد). کیف پول اختیاری.</summary>
    public static async Task<Customer> CreateCustomerAsync(this ShopApp app, decimal wallet = 0, string password = "Secret#123")
    {
        var phone = ShopApp.NewPhone();
        var (userId, addressId) = await app.WithDbAsync(async db =>
        {
            var user = new User
            {
                PhoneNumber = phone,
                FirstName = "مشتری",
                LastName = "آزمون",
                Role = UserRole.Customer,
                IsPhoneVerified = true,
                PasswordHash = PasswordHasher.Hash(password),
                SecurityStamp = PasswordHasher.NewSecurityStamp(),
            };
            db.Users.Add(user);

            // استان تهران (پیک فروشگاه و پرداخت در محل فقط برای تهران فعال است).
            var tehranId = Fratak_Shop.Data.Seeders.StoreDefaultsSeeder.TehranProvinceId;
            var province = await db.Locations.Where(l => l.Type == LocationType.Province && l.IsActive)
                               .OrderByDescending(l => l.Id == tehranId).ThenBy(l => l.Name).FirstAsync();
            var city = await db.Locations.Where(l => l.ParentId == province.Id && l.IsActive)
                           .OrderByDescending(l => l.Name.Contains("تهران")).ThenBy(l => l.Name).FirstAsync();

            var address = new UserAddress
            {
                User = user,
                ProvinceId = province.Id,
                CityId = city.Id,
                Title = "خانه",
                FirstName = "مشتری",
                LastName = "آزمون",
                AddressLine = "خیابان آزادی، کوچه آزمون، پلاک ۱۰",
                BuildingNumber = "10",
                Unit = "2",
                PostalCode = "1234567890",
                PhoneNumber = phone,
                IsDefault = true,
            };
            db.UserAddresses.Add(address);
            await db.SaveChangesAsync();
            return (user.Id, address.Id);
        });

        if (wallet > 0)
            await app.TopUpWalletAsync(userId, wallet);

        return new Customer(userId, phone, password, addressId);
    }

    public static async Task TopUpWalletAsync(this ShopApp app, Guid userId, decimal amount)
    {
        await using var scope = app.Scope();
        var result = await scope.ServiceProvider.GetRequiredService<IWalletService>().CreditAsync(new WalletEntry
        {
            UserId = userId,
            Amount = amount,
            Type = WalletTransactionType.TopUp,
            Description = "شارژ آزمون",
        });
        Assert.True(result.IsSuccess(), result.Message);
    }

    /// <summary>کالای فعال تازه با قیمت و موجودی دلخواه (دستهٔ «آزمون E2E»).</summary>
    public static async Task<(Guid Id, string Slug)> CreateProductAsync(this ShopApp app, decimal price = 250_000, int stock = 10,
        decimal? discountPrice = null, bool active = true)
    {
        var key = Guid.NewGuid().ToString("N")[..10];
        var slug = "e2e-" + key;
        var id = await app.WithDbAsync(async db =>
        {
            var category = await db.Categories.FirstOrDefaultAsync(c => c.Slug == "e2e-category");
            if (category is null)
            {
                category = new Category { Name = "دسته آزمون سرتاسری", Slug = "e2e-category", IsActive = true };
                db.Categories.Add(category);
                await db.SaveChangesAsync();
            }

            var product = new Product
            {
                CategoryId = category.Id,
                Name = "کالای آزمون " + key,
                Slug = slug,
                SKU = "E2E-" + key,
                Price = price,
                DiscountPrice = discountPrice,
                StockQuantity = stock,
                IsActive = active,
                PublishedAt = DateTimeOffset.UtcNow.AddDays(-1),
                ShortDescription = "کالای ساخته‌شده برای آزمون سرتاسری",
            };
            db.Products.Add(product);
            await db.SaveChangesAsync();
            return product.Id;
        });
        return (id, slug);
    }

    public static Task<int> StockAsync(this ShopApp app, Guid productId) =>
        app.WithDbAsync(db => db.Products.IgnoreQueryFilters().Where(p => p.Id == productId).Select(p => p.StockQuantity).FirstAsync());

    public static Task<Order?> OrderAsync(this ShopApp app, string orderNumber) =>
        app.WithDbAsync(db => db.Orders.IgnoreQueryFilters().Include(o => o.Items).FirstOrDefaultAsync(o => o.OrderNumber == orderNumber));

    public static Task<decimal> WalletBalanceAsync(this ShopApp app, Guid userId) =>
        app.WithDbAsync(db => db.Users.IgnoreQueryFilters().Where(u => u.Id == userId).Select(u => u.WalletBalance).FirstAsync());

    // ── جریان خرید از طریق صفحه‌های واقعی ───────────────────────────────────

    public static async Task AddToCartAsync(this ShopClient client, Guid productId, int quantity = 1)
    {
        using var response = await client.PostFormAsync("/cart?handler=Add",
            ("productId", productId.ToString()), ("quantity", quantity.ToString(System.Globalization.CultureInfo.InvariantCulture)), ("returnUrl", "/cart"));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    /// <summary>
    /// صفحهٔ تسویه را باز می‌کند، آدرس پیش‌فرض و یک روش ارسال (ترجیحاً بدون بازهٔ زمانی، وگرنه اولین بازهٔ آزاد) را
    /// انتخاب و سفارش را ثبت می‌کند. خروجی: پاسخ ثبت سفارش (ریدایرکت به موفقیت/درگاه/خطا).
    /// </summary>
    public static async Task<HttpResponseMessage> PlaceOrderAsync(this ShopClient client, PaymentMethod method,
        params (string Key, string Value)[] overrides)
    {
        var page = await client.GetPageAsync("/checkout");
        var fields = CheckoutFields(page, method).ToDictionary(f => f.Key, f => f.Value);
        foreach (var (key, value) in overrides)
            fields[key] = value;
        return await client.PostFormAsync("/checkout?handler=PlaceOrder", fields.Select(f => (f.Key, f.Value)).ToArray());
    }

    public static List<(string Key, string Value)> CheckoutFields(HtmlPage page, PaymentMethod method)
    {
        var address = page.One("input[name='Input.AddressId'][checked]")?.GetAttribute("value")
                      ?? page.One("input[name='Input.AddressId']")?.GetAttribute("value")
                      ?? throw new Xunit.Sdk.XunitException("Checkout page has no address.\n" + ShopClient.Excerpt(page.Html));

        // پرداخت در محل فقط با روش‌های ارسالی که data-cod="1" دارند (پست پیشتاز ندارد، پیک دارد).
        var methods = page.All("input[name='Input.ShippingMethodId']")
            .Where(i => method != PaymentMethod.CashOnDelivery || i.GetAttribute("data-cod") == "1")
            .Select(i => i.GetAttribute("value")!).ToList();
        if (methods.Count == 0)
            throw new Xunit.Sdk.XunitException($"Checkout page offers no shipping method for {method}.\n" + ShopClient.Excerpt(page.Html));

        var withoutSlot = methods.FirstOrDefault(m => page.One($"#slots-{m}") is null);
        var fields = new List<(string, string)> { ("Input.AddressId", address), ("Input.PaymentMethod", method.ToString()), ("Input.Note", "سفارش آزمون") };
        if (withoutSlot is not null)
        {
            fields.Add(("Input.ShippingMethodId", withoutSlot));
        }
        else
        {
            var chosen = methods[0];
            var slot = page.All($"#slots-{chosen} input[name='Input.DeliverySlot']").FirstOrDefault(i => !i.HasAttribute("disabled"))?.GetAttribute("value")
                       ?? throw new Xunit.Sdk.XunitException("No free delivery slot.");
            fields.Add(("Input.ShippingMethodId", chosen));
            fields.Add(("Input.DeliverySlot", slot));
        }
        return fields;
    }

    /// <summary>شمارهٔ سفارش از آدرس ریدایرکت /checkout/success/{n} یا /checkout/failed/{n}.</summary>
    public static string OrderNumberFrom(HttpResponseMessage response)
    {
        var location = response.Headers.Location?.ToString() ?? throw new Xunit.Sdk.XunitException($"No redirect ({(int)response.StatusCode}).");
        var path = location.Split('?')[0];
        return Uri.UnescapeDataString(path[(path.LastIndexOf('/') + 1)..]);
    }
}
