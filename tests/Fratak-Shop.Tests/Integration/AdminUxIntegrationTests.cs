using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Seeders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Fratak_Shop.Tests.Integration;

/// <summary>
/// تست‌های مرحلهٔ هفتم – P2 روی دیتابیس واقعی: میانبرها (سنجاق، اخیر، فیلتر)، راهنمای صفحه‌ها،
/// جست‌وجوی سریع پنل و کتابخانهٔ رسانه.
/// </summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", "Integration")]
public class AdminUxIntegrationTests(SqlServerFixture db)
{
    [SqlFact]
    public async Task Pins_Toggle_AndRejectExternalUrls()
    {
        var userId = await db.CreateUserAsync();
        await using var scope = db.Scope();
        var shortcuts = scope.ServiceProvider.GetRequiredService<IAdminShortcutService>();

        Assert.True(await shortcuts.TogglePinAsync(userId, "/admin/orders?status=Paid", "سفارش‌های پرداخت‌شده", "Orders"));
        Assert.Null(await shortcuts.TogglePinAsync(userId, "https://evil.example/admin", "x", null));
        // نام آیکن ناامن با آیکن پیش‌فرض جایگزین می‌شود.
        Assert.True(await shortcuts.TogglePinAsync(userId, "/admin/products", "محصولات", "<svg onload=x>"));

        var pins = await shortcuts.GetPinsAsync(userId);
        Assert.Equal(2, pins.Count);
        Assert.Equal("PushPin", pins[1].Icon);

        Assert.False(await shortcuts.TogglePinAsync(userId, "/admin/orders?status=Paid", "سفارش‌های پرداخت‌شده", "Orders"));
        Assert.Single(await shortcuts.GetPinsAsync(userId));
    }

    [SqlFact]
    public async Task Recents_SkipRepeat_MoveToTop_AndKeepEight()
    {
        var userId = await db.CreateUserAsync();
        await using var scope = db.Scope();
        var shortcuts = scope.ServiceProvider.GetRequiredService<IAdminShortcutService>();

        for (var i = 0; i < 10; i++)
            await shortcuts.TrackRecentAsync(userId, $"/admin/page{i}", $"صفحه {i}");
        await shortcuts.TrackRecentAsync(userId, "/admin/page9", "صفحه 9"); // تکرار آخرین صفحه: بدون تغییر
        await shortcuts.TrackRecentAsync(userId, "/admin/page5", "صفحه 5"); // بازدید دوباره: به بالا می‌رود

        var recents = await shortcuts.GetRecentsAsync(userId);
        Assert.Equal(AdminShortcutService.MaxRecents, recents.Count);
        Assert.Equal("/admin/page5", recents[0].Url);
        Assert.Equal("/admin/page9", recents[1].Url);
        Assert.Single(recents, r => r.Url == "/admin/page5");
        Assert.DoesNotContain(recents, r => r.Url == "/admin/page0");
    }

    [SqlFact]
    public async Task SavedFilters_SaveReplaceDelete_PerPage()
    {
        var userId = await db.CreateUserAsync();
        await using var scope = db.Scope();
        var shortcuts = scope.ServiceProvider.GetRequiredService<IAdminShortcutService>();

        Assert.NotNull(await shortcuts.SaveFilterAsync(userId, "/admin/orders", "", "?pay=COD"));
        Assert.NotNull(await shortcuts.SaveFilterAsync(userId, "/admin/orders", "خالی", "?"));
        Assert.Null(await shortcuts.SaveFilterAsync(userId, "/Admin/Orders/", "پرداخت در محل", "pay=COD"));
        Assert.Null(await shortcuts.SaveFilterAsync(userId, "/admin/orders", "پرداخت در محل", "?pay=COD&status=Pending"));
        Assert.Null(await shortcuts.SaveFilterAsync(userId, "/admin/products", "ناموجود", "?stock=out"));

        var orders = await shortcuts.GetFiltersAsync(userId, "/admin/orders");
        var filter = Assert.Single(orders);
        Assert.Equal("?pay=COD&status=Pending", filter.Query);

        var all = await shortcuts.GetAllFiltersAsync(userId);
        Assert.Equal(2, all.Count);

        await shortcuts.DeleteFilterAsync(userId, "/admin/orders", "پرداخت در محل");
        Assert.Empty(await shortcuts.GetFiltersAsync(userId, "/admin/orders"));
        Assert.Single(await shortcuts.GetAllFiltersAsync(userId));
    }

    [SqlFact]
    public async Task Help_IsSeeded_ResolvesParents_AndCustomTextSurvivesReseed()
    {
        await using var scope = db.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
        var help = scope.ServiceProvider.GetRequiredService<IAdminHelpService>();

        var list = await help.ListAsync();
        Assert.All(AdminHelpDefaults.All, d => Assert.Contains(list, h => h.Path == d.Path));

        var detail = await help.GetForPathAsync($"/admin/orders/{Guid.NewGuid()}");
        Assert.Equal("/admin/orders/*", detail?.Path);
        var coupon = await help.GetForPathAsync($"/admin/manage/coupons/edit/{Guid.NewGuid()}");
        Assert.Equal("/admin/manage/coupons", coupon?.Path);

        var feedback = list.Single(h => h.Path == "/admin/feedback");
        var saved = await help.SaveAsync(feedback.Id, "/admin/feedback", "عنوان سفارشی", "<p>متن سفارشی</p><script>alert(1)</script>");
        Assert.True(saved.IsSuccess(), saved.Message);

        // اجرای دوبارهٔ seeder متن ویرایش‌شده را برنمی‌گرداند.
        await Round7DefaultsSeeder.SeedAsync(context);
        var custom = await help.FindAsync(feedback.Id);
        Assert.Equal("عنوان سفارشی", custom?.Title);
        Assert.DoesNotContain("<script", custom!.Body);
        Assert.True(custom.IsCustomized);

        Assert.True((await help.ResetAsync(feedback.Id)).IsSuccess());
        var reset = await help.FindAsync(feedback.Id);
        Assert.Equal(AdminHelpDefaults.All.Single(d => d.Path == "/admin/feedback").Body, reset?.Body);

        Assert.False((await help.SaveAsync(null, "/admin/feedback", "تکراری", "<p>x</p>")).IsSuccess());
        Assert.False((await help.SaveAsync(null, "javascript:alert(1)", "x", "<p>x</p>")).IsSuccess());
    }

    [SqlFact]
    public async Task Search_FindsByScope_AndRespectsPermissions()
    {
        var productId = await db.CreateProductAsync();
        var userId = await db.CreateUserAsync();
        await using var scope = db.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
        var search = scope.ServiceProvider.GetRequiredService<IAdminSearchService>();
        var product = await context.Products.SingleAsync(p => p.Id == productId);
        var user = await context.Users.SingleAsync(u => u.Id == userId);

        var bySku = await search.SearchAsync(product.SKU, AdminSearchScope.Products);
        Assert.Contains(bySku, h => h.Url == $"/admin/products/edit/{productId}");

        // بدون مجوز محصولات، همان عبارت هیچ محصولی برنمی‌گرداند.
        var noScope = await search.SearchAsync(product.SKU, AdminSearchScope.Orders | AdminSearchScope.Tickets);
        Assert.DoesNotContain(noScope, h => h.Group == "محصولات");

        // موبایل با ارقام فارسی و بدون صفر اول هم پیدا می‌شود.
        var persianPhone = new string(user.PhoneNumber[1..].Select(c => (char)('۰' + (c - '0'))).ToArray());
        var byPhone = await search.SearchAsync(persianPhone, AdminSearchScope.Customers);
        Assert.Contains(byPhone, h => h.Url == $"/admin/customers/{userId}");

        Assert.Empty(await search.SearchAsync("a", AdminSearchScope.Products));
        Assert.Empty(await search.SearchAsync(product.SKU, AdminSearchScope.None));
    }

    [SqlFact]
    public async Task Media_AddUpdateListDelete()
    {
        await using var scope = db.Scope();
        var media = scope.ServiceProvider.GetRequiredService<IMediaLibraryService>();
        var key = Guid.NewGuid().ToString("N");

        var added = await media.AddAsync([new AddMediaCommand($"{key}.webp", $"/uploads/media/{key}.webp", $"گوشی-آبی-{key[..6]}.jpg", "image/webp", 12345, 800, 600, null)]);
        var file = Assert.Single(added);
        Assert.Equal($"گوشی آبی {key[..6]}", file.AltText);

        Assert.True((await media.UpdateAltAsync(file.Id, "<b>گوشی</b> سامسونگ " + key[..6])).IsSuccess());
        var found = await media.ListAsync("سامسونگ " + key[..6], 1, 24);
        Assert.Equal("گوشی سامسونگ " + key[..6], Assert.Single(found.Items).AltText);

        Assert.Equal($"{key}.webp", await media.DeleteAsync(file.Id));
        Assert.Null(await media.DeleteAsync(file.Id));
        Assert.Empty((await media.ListAsync("سامسونگ " + key[..6], 1, 24)).Items);
    }
}
