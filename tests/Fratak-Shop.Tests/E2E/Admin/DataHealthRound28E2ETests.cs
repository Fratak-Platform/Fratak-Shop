using System.Net;
using Fratak_Shop.Core.Services.Smart;
using Fratak_Shop.Tests.E2E.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Tests.E2E.Admin;

/// <summary>
/// تست سرتاسری دور ۲۸ (#۵۱–۵۹) – صفحهٔ «سلامت داده»:
/// مدیر خلاصه و ایرادها را می‌بیند؛ «درست کردن خودکار» پیش‌نویس ایراددار را درست می‌کند؛ «آماده‌سازی دوباره» کالای منتشرشده را
/// رد می‌کند؛ بدون توکن ضدجعل ← 400؛ مشتری ← 404؛ نویسنده (بی‌دسترسی) ← نه 200؛ فیلتر ایراد نامعتبر نادیده گرفته می‌شود.
/// </summary>
[Collection(E2ECollection.Name)]
[Trait("Category", "E2E")]
[Trait("Area", "Round28")]
public class DataHealthRound28E2ETests(ShopApp app)
{
    [SqlFact]
    public async Task Data_health_page_reports_and_fixes_drafts()
    {
        var (draftId, _) = await app.CreateProductAsync(stock: 3, active: false);
        var (liveId, _) = await app.CreateProductAsync(stock: 3);
        // پیش‌نویس واقعی (هرگز منتشرنشده) با نام انگلیسی فارسی‌دار و موجودی منفی.
        await app.WithDbAsync(db => db.Products.Where(p => p.Id == draftId)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.PublishedAt, (DateTimeOffset?)null)
                .SetProperty(p => p.EnglishName, "کالا Test").SetProperty(p => p.StockQuantity, -2)));

        using var owner = app.CreateClient();
        await owner.LoginAsync(ShopApp.OwnerPhone);

        var page = await owner.GetPageAsync("/admin/smart/data-health");
        Assert.NotNull(page.One("[data-health-summary]"));
        Assert.NotNull(page.One("[data-fix-all]"));
        Assert.Contains("موجودی منفی", page.Text, StringComparison.Ordinal);

        var filtered = await owner.GetPageAsync("/admin/smart/data-health?issue=stock-negative");
        Assert.Contains("نمایش همه", filtered.Text, StringComparison.Ordinal);
        using (var junk = await owner.GetAsync("/admin/smart/data-health?issue=%3Cscript%3E"))
            Assert.Equal(HttpStatusCode.OK, junk.StatusCode);

        using (var csrf = await owner.PostRawFormAsync("/admin/smart/data-health?handler=Fix"))
            Assert.Equal(HttpStatusCode.BadRequest, csrf.StatusCode);

        using (var fix = await owner.PostFormAsync("/admin/smart/data-health?handler=Fix"))
        {
            Assert.Equal(HttpStatusCode.Redirect, fix.StatusCode);
            Assert.Equal("/admin/smart/data-health", fix.Headers.Location?.ToString());
        }
        var draft = await app.WithDbAsync(db => db.Products.AsNoTracking().Where(p => p.Id == draftId)
            .Select(p => new { p.EnglishName, p.StockQuantity }).SingleAsync());
        // کارگر هوشمند ممکن است هم‌زمان نام انگلیسی درست بگذارد؛ مهم این است که نام فارسی‌دار نماند.
        Assert.True(draft.EnglishName is null || ProductNaming.IsUsableEnglish(draft.EnglishName), draft.EnglishName);
        Assert.Equal(0, draft.StockQuantity);

        using (var reprepareLive = await owner.PostFormAsync("/admin/smart/data-health?handler=Reprepare", ("id", liveId.ToString())))
            Assert.Equal(HttpStatusCode.Redirect, reprepareLive.StatusCode);
        Assert.True(await app.WithDbAsync(db => db.Products.Where(p => p.Id == liveId).Select(p => p.IsActive).SingleAsync()));

        using (var reprepare = await owner.PostFormAsync("/admin/smart/data-health?handler=Reprepare", ("id", draftId.ToString())))
            Assert.Equal(HttpStatusCode.Redirect, reprepare.StatusCode);
        Assert.True(await app.WithDbAsync(db => db.Products.Where(p => p.Id == draftId).Select(p => p.SmartReadyAttempts).SingleAsync()) <= 1);

        using (var recheck = await owner.PostFormAsync("/admin/smart/data-health?handler=Recheck", ("id", draftId.ToString())))
            Assert.Equal(HttpStatusCode.Redirect, recheck.StatusCode);

        using var customer = app.CreateClient();
        await customer.LoginAsync(ShopApp.DemoCustomerPhone);
        using (var denied = await customer.GetAsync("/admin/smart/data-health"))
            Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);

        using var writer = app.CreateClient();
        await writer.LoginAsync(ShopApp.WriterPhone);
        using (var staff = await writer.GetAsync("/admin/smart/data-health"))
            Assert.NotEqual(HttpStatusCode.OK, staff.StatusCode);
        using (var staffPost = await writer.PostFormAsync("/admin/smart/data-health?handler=Fix"))
            Assert.NotEqual("/admin/smart/data-health", staffPost.Headers.Location?.ToString());
    }
}
