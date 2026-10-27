using System.Net;
using Fratak_Shop.Data.Entities.Orders;
using Fratak_Shop.Data.Entities.Users;
using Fratak_Shop.Tests.E2E.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Tests.E2E.Profile;

/// <summary>
/// حساب کاربری از دید مهاجمی که خودش هم مشتری است (IDOR): دیدن/لغو سفارش دیگران، فاکتور دیگران، حذف آدرس دیگران،
/// بستن نشست دیگران، تغییر نقش یا موجودی با فیلدهای اضافهٔ فرم (mass assignment) و تزریق اسکریپت (XSS).
/// </summary>
[Collection(E2ECollection.Name)]
[Trait("Category", "E2E")]
[Trait("Category", "Security")]
[Trait("Area", "Profile")]
public class ProfileSecurityE2ETests(ShopApp app)
{
    private async Task<(TestData.Customer Victim, string OrderNumber, ShopClient Attacker, TestData.Customer AttackerAccount)> SetupAsync()
    {
        var victim = await app.CreateCustomerAsync();
        var (productId, _) = await app.CreateProductAsync(stock: 5);
        using (var victimClient = app.CreateClient())
        {
            await victimClient.LoginAsync(victim.Phone, victim.Password);
            await victimClient.AddToCartAsync(productId);
            using var placed = await victimClient.PlaceOrderAsync(PaymentMethod.CashOnDelivery);
            var number = TestData.OrderNumberFrom(placed);

            var attackerAccount = await app.CreateCustomerAsync();
            var attacker = app.CreateClient();
            await attacker.LoginAsync(attackerAccount.Phone, attackerAccount.Password);
            return (victim, number, attacker, attackerAccount);
        }
    }

    [SqlFact]
    public async Task Another_customers_order_and_invoice_are_not_found()
    {
        var (_, number, attacker, _) = await SetupAsync();
        using var _ = attacker;

        await attacker.GetPageAsync($"/profile/orders/{number}", HttpStatusCode.NotFound);
        await attacker.GetPageAsync($"/profile/orders/{number}/invoice", HttpStatusCode.NotFound);
        using var returnPage = await attacker.GetAsync($"/profile/returns/new/{number}");
        Assert.NotEqual(HttpStatusCode.OK, returnPage.StatusCode);
        var orders = await attacker.GetPageAsync("/profile/orders");
        Assert.DoesNotContain(number, orders.Html, StringComparison.OrdinalIgnoreCase);
    }

    [SqlFact]
    public async Task Another_customers_order_cannot_be_cancelled()
    {
        var (_, number, attacker, _) = await SetupAsync();
        using var _ = attacker;

        using var cancel = await attacker.PostFormAsync($"/profile/orders/{number}?handler=Cancel");

        Assert.NotEqual(OrderStatus.Cancelled, (await app.OrderAsync(number))!.Status);
    }

    [SqlFact]
    public async Task Another_customers_address_cannot_be_edited_deleted_or_made_default()
    {
        var (victim, _, attacker, _) = await SetupAsync();
        using var _ = attacker;
        var id = victim.AddressId.ToString();

        using var delete = await attacker.PostFormAsync("/profile/addresses?handler=Delete", ("id", id));
        using var makeDefault = await attacker.PostFormAsync("/profile/addresses?handler=Default", ("id", id));
        var address = await app.WithDbAsync(db => db.UserAddresses.FirstAsync(a => a.Id == victim.AddressId));
        using var edit = await attacker.PostFormAsync("/profile/addresses?handler=Save",
            ("Address.Id", id), ("Address.Title", "hacked"), ("Address.FirstName", "x"), ("Address.LastName", "y"),
            ("Address.ProvinceId", address.ProvinceId.ToString()), ("Address.CityId", address.CityId.ToString()),
            ("Address.AddressLine", "hacked street"), ("Address.PostalCode", "1111111111"), ("Address.PhoneNumber", "09111111111"));

        Assert.Equal(HttpStatusCode.NotFound, delete.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, makeDefault.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, edit.StatusCode);
        var after = await app.WithDbAsync(db => db.UserAddresses.FirstOrDefaultAsync(a => a.Id == victim.AddressId));
        Assert.NotNull(after);
        Assert.Equal(victim.Id, after!.UserId);
        Assert.Equal("خانه", after.Title);
    }

    [SqlFact]
    public async Task Another_customers_session_cannot_be_revoked()
    {
        var victim = await app.CreateCustomerAsync();
        using var victimClient = app.CreateClient();
        await victimClient.LoginAsync(victim.Phone, victim.Password);
        var family = await app.WithDbAsync(db => db.UserRefreshTokens.Where(t => t.UserId == victim.Id && t.RevokedAt == null).Select(t => t.FamilyId).FirstAsync());
        var attackerAccount = await app.CreateCustomerAsync();
        using var attacker = app.CreateClient();
        await attacker.LoginAsync(attackerAccount.Phone, attackerAccount.Password);

        using (await attacker.PostFormAsync("/profile/sessions?handler=Revoke", ("familyId", family.ToString())))
        {
        }

        Assert.True(await app.WithDbAsync(db => db.UserRefreshTokens.AnyAsync(t => t.FamilyId == family && t.RevokedAt == null)));
        await victimClient.GetPageAsync("/profile");
    }

    [SqlFact]
    public async Task Extra_form_fields_cannot_change_role_wallet_or_verification()
    {
        var customer = await app.CreateCustomerAsync();
        using var client = app.CreateClient();
        await client.LoginAsync(customer.Phone, customer.Password);

        using (await client.PostFormAsync("/profile/info",
                   ("Input.FirstName", "علی"), ("Input.LastName", "رضایی"),
                   ("Input.Role", "SuperAdmin"), ("Role", "SuperAdmin"), ("Input.WalletBalance", "999999999"), ("WalletBalance", "999999999"),
                   ("Input.IsBlocked", "false"), ("Input.PhoneNumber", "09120000009"), ("PhoneNumber", "09120000009")))
        {
        }

        var user = await app.WithDbAsync(db => db.Users.FirstAsync(u => u.Id == customer.Id));
        Assert.Equal(UserRole.Customer, user.Role);
        Assert.Equal(0m, user.WalletBalance);
        Assert.Equal(customer.Phone, user.PhoneNumber);
        using var admin = await client.GetAsync("/admin");
        Assert.NotEqual(HttpStatusCode.OK, admin.StatusCode);
    }

    [SqlFact]
    public async Task Address_with_a_city_from_another_province_is_rejected()
    {
        var customer = await app.CreateCustomerAsync();
        using var client = app.CreateClient();
        await client.LoginAsync(customer.Phone, customer.Password);
        var (provinceId, foreignCityId) = await app.WithDbAsync(async db =>
        {
            var provinces = await db.Locations.Where(l => l.Type == Fratak_Shop.Data.Entities.Locations.LocationType.Province)
                .OrderBy(l => l.Name).Select(l => l.Id).Take(2).ToListAsync();
            var city = await db.Locations.Where(l => l.ParentId == provinces[1]).Select(l => l.Id).FirstAsync();
            return (provinces[0], city);
        });

        using (await client.PostFormAsync("/profile/addresses?handler=Save",
                   ("Address.Title", "ناجور"), ("Address.FirstName", "x"), ("Address.LastName", "y"),
                   ("Address.ProvinceId", provinceId.ToString()), ("Address.CityId", foreignCityId.ToString()),
                   ("Address.AddressLine", "نشانی"), ("Address.PostalCode", "1234567890"), ("Address.PhoneNumber", "09121111111")))
        {
        }

        Assert.False(await app.WithDbAsync(db => db.UserAddresses.AnyAsync(a => a.UserId == customer.Id && a.Title == "ناجور")));
    }

    [SqlFact]
    public async Task Script_in_names_and_addresses_is_rendered_as_text()
    {
        const string payload = "<script>alert('xss')</script><img src=x onerror=alert(1)>";
        var customer = await app.CreateCustomerAsync();
        using var client = app.CreateClient();
        await client.LoginAsync(customer.Phone, customer.Password);
        var address = await app.WithDbAsync(db => db.UserAddresses.FirstAsync(a => a.Id == customer.AddressId));

        using (await client.PostFormAsync("/profile/info", ("Input.FirstName", payload), ("Input.LastName", "تست")))
        {
        }
        using (await client.PostFormAsync("/profile/addresses?handler=Save",
                   ("Address.Title", "xss"), ("Address.FirstName", payload), ("Address.LastName", "y"),
                   ("Address.ProvinceId", address.ProvinceId.ToString()), ("Address.CityId", address.CityId.ToString()),
                   ("Address.AddressLine", payload), ("Address.PostalCode", "1234567890"), ("Address.PhoneNumber", "09121111111")))
        {
        }

        foreach (var url in new[] { "/profile", "/profile/info", "/profile/addresses" })
        {
            var page = await client.GetPageAsync(url);
            Assert.DoesNotContain("<script>alert('xss')", page.Html, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("onerror=alert(1)>", page.Html, StringComparison.OrdinalIgnoreCase);
            Assert.Empty(page.All("img[onerror]"));
        }
    }
}
