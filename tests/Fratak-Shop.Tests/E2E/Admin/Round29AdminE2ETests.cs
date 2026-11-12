using System.Net;
using Fratak_Shop.Data.Entities.Smart;
using Fratak_Shop.Tests.E2E.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Tests.E2E.Admin;

/// <summary>
/// تست سرتاسری دور ۲۹ – صفحه‌های تازهٔ پنل:
/// <list type="bullet">
/// <item>#۱۸ «ویدیوهای پیشنهادی»: مدیر فهرست را می‌بیند، «رد» و «افزودن از آپارات» کار می‌کند؛ بدون توکن ← 400؛ مشتری ← 404؛ نویسنده ← نه 200.</item>
/// <item>#۲۷ «منوی بالای سایت»: مدیر 200، مشتری 404.</item>
/// <item>#۱۷ «سربرگ همهٔ دسته‌ها»: POST ← بازگشت به پیشنهادها.</item>
/// <item>#۱۱ قاب کد کمپین ناموجود ← 404.</item>
/// </list>
/// </summary>
[Collection(E2ECollection.Name)]
[Trait("Category", "E2E")]
[Trait("Area", "Round29")]
public class Round29AdminE2ETests(ShopApp app)
{
    [SqlFact]
    public async Task Video_suggestions_page_lists_dismisses_and_adds_aparat_videos()
    {
        var (productId, _) = await app.CreateProductAsync();
        var aparatId = Guid.NewGuid();
        var otherId = Guid.NewGuid();
        await app.WithDbAsync(async db =>
        {
            db.ProductVideoSuggestions.Add(new ProductVideoSuggestion
            {
                Id = aparatId, ProductId = productId, Source = "aparat", PageUrl = "https://www.aparat.com/v/e2e" + productId.ToString("N")[..8],
                Title = "معرفی کالای آزمایشی سرتاسری", Score = 100,
            });
            db.ProductVideoSuggestions.Add(new ProductVideoSuggestion
            {
                Id = otherId, ProductId = productId, Source = "youtube", PageUrl = "https://www.youtube.com/watch?v=e2eTest0001",
                Title = "ویدیوی یوتیوب آزمایشی", Score = 80,
            });
            await db.SaveChangesAsync();
        });
        try
        {
            using var owner = app.CreateClient();
            await owner.LoginAsync(ShopApp.OwnerPhone);

            var page = await owner.GetPageAsync($"/admin/smart/videos?product={productId}");
            Assert.Contains("معرفی کالای آزمایشی سرتاسری", page.Text, StringComparison.Ordinal);
            Assert.Contains("افزودن از آپارات", page.Text, StringComparison.Ordinal);
            using (var junk = await owner.GetAsync("/admin/smart/videos?status=%3Cscript%3E"))
                Assert.Equal(HttpStatusCode.OK, junk.StatusCode);

            using (var csrf = await owner.PostRawFormAsync("/admin/smart/videos?handler=Dismiss", ("id", otherId.ToString())))
                Assert.Equal(HttpStatusCode.BadRequest, csrf.StatusCode);

            using (var dismiss = await owner.PostFormAsync("/admin/smart/videos?handler=Dismiss", ("id", otherId.ToString()), ("product", productId.ToString())))
            {
                Assert.Equal(HttpStatusCode.Redirect, dismiss.StatusCode);
                Assert.StartsWith("/admin/smart/videos", dismiss.Headers.Location?.ToString(), StringComparison.OrdinalIgnoreCase);
            }
            Assert.Equal(VideoSuggestionStatus.Dismissed,
                await app.WithDbAsync(db => db.ProductVideoSuggestions.Where(v => v.Id == otherId).Select(v => v.Status).SingleAsync()));

            using (var add = await owner.PostFormAsync("/admin/smart/videos?handler=Aparat", ("id", aparatId.ToString())))
                Assert.Equal(HttpStatusCode.Redirect, add.StatusCode);
            Assert.True(await app.WithDbAsync(db => db.ProductVideos.AnyAsync(v => v.ProductId == productId && v.IsActive)));

            // صفحهٔ ویرایش کالا لینک «ویدیوی پیشنهادی» دارد.
            var edit = await owner.GetPageAsync($"/admin/products/edit/{productId}");
            Assert.Contains("ویدیوی پیشنهادی", edit.Text, StringComparison.Ordinal);

            using var customer = app.CreateClient();
            await customer.LoginAsync(ShopApp.DemoCustomerPhone);
            using (var denied = await customer.GetAsync("/admin/smart/videos"))
                Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);

            using var writer = app.CreateClient();
            await writer.LoginAsync(ShopApp.WriterPhone);
            using (var staff = await writer.GetAsync("/admin/smart/videos"))
                Assert.NotEqual(HttpStatusCode.OK, staff.StatusCode);
        }
        finally
        {
            await app.WithDbAsync(async db =>
            {
                await db.ProductVideoSuggestions.IgnoreQueryFilters().Where(v => v.ProductId == productId).ExecuteDeleteAsync();
                await db.ProductVideos.IgnoreQueryFilters().Where(v => v.ProductId == productId).ExecuteDeleteAsync();
            });
        }
    }

    [SqlFact]
    public async Task Header_menu_page_is_for_staff_only()
    {
        using var owner = app.CreateClient();
        await owner.LoginAsync(ShopApp.OwnerPhone);
        using (var ok = await owner.GetAsync("/admin/content/header-menu"))
            Assert.Equal(HttpStatusCode.OK, ok.StatusCode);

        using var customer = app.CreateClient();
        await customer.LoginAsync(ShopApp.DemoCustomerPhone);
        using (var denied = await customer.GetAsync("/admin/content/header-menu"))
            Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
    }

    [SqlFact]
    public async Task Category_headers_button_redirects_to_suggestions()
    {
        using var owner = app.CreateClient();
        await owner.LoginAsync(ShopApp.OwnerPhone);
        using var response = await owner.PostFormAsync("/admin/design?handler=CategoryHeaders");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/admin/design", response.Headers.Location?.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [SqlFact]
    public async Task Unknown_campaign_frame_is_not_found()
    {
        using var visitor = app.CreateClient();
        using var response = await visitor.GetAsync("/campaign/no-such-campaign-r29/frame");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
