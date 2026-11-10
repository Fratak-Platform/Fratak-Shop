using System.Net;
using System.Text.Json;
using Fratak_Shop.Core.Services.Design;
using Fratak_Shop.Data.Entities.Categories;
using Fratak_Shop.Data.Entities.Marketing;
using Fratak_Shop.Tests.E2E.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Tests.E2E.Storefront;

/// <summary>
/// تست سرتاسری دور ۲۴ (بستهٔ C – بنر هوشمند) روی برنامهٔ واقعی.
/// <list type="bullet">
/// <item>فرم «ایده ← طرح» انتخاب‌گر کالا دارد و جست‌وجوی کالا JSON برمی‌گرداند.</item>
/// <item>«حراج {دسته} ۴۰٪ با امتیاز دوبرابر» در جایگاه سربرگ دسته ← ۳ طرح وصل به دسته + پیش‌نویس کمپین با کوپن غیرفعال؛
/// تأیید یکی ← بنر سربرگ همان دسته و کمپین/کوپن فعال؛ «توقف» ← کمپین تمام و کوپن غیرفعال.</item>
/// <item>سربرگ دسته بدون نام دسته ← خطا و هیچ طرحی ساخته نمی‌شود.</item>
/// <item>مشتری به جست‌وجوی کالای پنل و لغو کمپین دسترسی ندارد.</item>
/// </list>
/// </summary>
[Collection(E2ECollection.Name)]
[Trait("Category", "E2E")]
public class CampaignRound24E2ETests(ShopApp app)
{
    [SqlFact]
    public async Task Brief_form_has_a_product_picker_backed_by_live_search()
    {
        var (productId, _) = await app.CreateProductAsync(stock: 3);
        var sku = await app.WithDbAsync(db => db.Products.Where(p => p.Id == productId).Select(p => p.SKU).SingleAsync());
        using var client = app.CreateClient();
        await client.LoginAsync(ShopApp.OwnerPhone);
        var page = await client.GetPageAsync("/admin/design");
        Assert.NotNull(page.One("form#brief input[data-pick-product]"));
        Assert.NotNull(page.One("form#brief input[type=hidden][name=productId]"));

        using var response = await client.Http.GetAsync("/admin/design?handler=Products&q=" + Uri.EscapeDataString(sku));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Contains(json.RootElement.EnumerateArray(), e => e.GetProperty("id").GetGuid() == productId);

        // کمتر از ۲ حرف ← فهرست خالی (نه خطا).
        using var tiny = await client.Http.GetAsync("/admin/design?handler=Products&q=a");
        Assert.Equal(HttpStatusCode.OK, tiny.StatusCode);
        Assert.Equal("[]", (await tiny.Content.ReadAsStringAsync()).Trim());
    }

    [SqlFact]
    public async Task Category_campaign_brief_drafts_a_real_offer_that_goes_live_only_on_publish()
    {
        var key = Guid.NewGuid().ToString("N")[..6];
        var categoryName = "کمپینی zq" + key;
        var categoryId = Guid.Empty;
        var since = DateTimeOffset.UtcNow.AddSeconds(-5);
        var (productId, _) = await app.CreateProductAsync(stock: 4);
        await app.WithDbAsync(async db =>
        {
            var category = new Category { Name = categoryName, Slug = "camp-e2e-" + key, IsActive = true };
            db.Categories.Add(category);
            await db.SaveChangesAsync();
            categoryId = category.Id;
            await db.Products.Where(p => p.Id == productId).ExecuteUpdateAsync(s => s.SetProperty(p => p.CategoryId, category.Id));
        });

        using var client = app.CreateClient();
        await client.LoginAsync(ShopApp.OwnerPhone);
        await client.GetPageAsync("/admin/design");
        List<Guid> docIds = [];
        string? group = null;
        try
        {
            using var response = await client.PostFormAsync("/admin/design?handler=Brief", ("brief", $"حراج {categoryName} ۴۰٪ با امتیاز دوبرابر"), ("place", "category"));
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

            var docs = await app.WithDbAsync(db => db.DesignDocs.AsNoTracking()
                .Where(d => d.CategoryId == categoryId && d.CreatedAt >= since).ToListAsync());
            docIds = docs.Select(d => d.Id).ToList();
            Assert.Equal(3, docs.Count);
            Assert.All(docs, d => Assert.Equal(DesignPresets.CategoryHeader, d.PresetKey));
            group = Assert.Single(docs.Select(d => DesignBrief.VariantGroup(d.IdeaKey)).Distinct());
            Assert.NotNull(group);

            var offer = await app.WithDbAsync(db => db.CampaignOffers.AsNoTracking().SingleAsync(o => o.VariantGroup == group));
            Assert.Equal(CampaignOfferStatus.Draft, offer.Status);
            Assert.Equal(40, offer.DiscountPercent);
            Assert.Equal(2m, offer.PointsMultiplier);
            Assert.Equal(CampaignOfferScope.Category, offer.Scope);
            Assert.Equal(categoryId, offer.ScopeId);
            Assert.NotNull(offer.CouponCode);
            // متن بنر کد واقعی را می‌گوید.
            Assert.All(docs, d => Assert.Contains(offer.CouponCode!, d.Subline ?? ""));
            Assert.False(await app.WithDbAsync(db => db.Coupons.Where(c => c.Id == offer.CouponId).Select(c => c.IsActive).SingleAsync()));

            // صفحهٔ استودیو پیش‌نویس را نشان می‌دهد.
            var studio = await client.GetPageAsync("/admin/design");
            Assert.NotNull(studio.One("#campaigns"));

            // تأیید یکی از طرح‌ها ← بنر سربرگ همان دسته + کمپین و کوپن فعال.
            // (اگر رندر سرور روی این ماشین فونت/کتابخانه نداشت، طرح بی‌تصویر می‌ماند؛ برای آزمون انتشار یک نام فایل می‌گذاریم.)
            await app.WithDbAsync(db => db.DesignDocs.Where(d => d.Id == docIds[0] && d.ImageName == null)
                .ExecuteUpdateAsync(s => s.SetProperty(d => d.ImageName, "e2e-campaign.webp")));
            using var approve = await client.PostFormAsync($"/admin/design?handler=Approve&id={docIds[0]}");
            Assert.Equal(HttpStatusCode.Redirect, approve.StatusCode);
            var live = await app.WithDbAsync(db => db.CampaignOffers.AsNoTracking().SingleAsync(o => o.Id == offer.Id));
            Assert.Equal(CampaignOfferStatus.Active, live.Status);
            Assert.True(await app.WithDbAsync(db => db.Coupons.Where(c => c.Id == offer.CouponId).Select(c => c.IsActive).SingleAsync()));
            var banner = await app.WithDbAsync(db => db.Banners.AsNoTracking().SingleAsync(b => b.VariantGroup == group));
            Assert.Equal(BannerPosition.CategoryHeader, banner.Position);
            Assert.Equal(categoryId, banner.CategoryId);
            Assert.Equal(live.EndsAt, banner.EndAt);

            // توقف ← تمام‌شده و کوپن غیرفعال.
            using var stop = await client.PostFormAsync($"/admin/design?handler=CancelOffer&id={offer.Id}");
            Assert.Equal(HttpStatusCode.Redirect, stop.StatusCode);
            Assert.Equal(CampaignOfferStatus.Ended, await app.WithDbAsync(db => db.CampaignOffers.Where(o => o.Id == offer.Id).Select(o => o.Status).SingleAsync()));
            Assert.False(await app.WithDbAsync(db => db.Coupons.Where(c => c.Id == offer.CouponId).Select(c => c.IsActive).SingleAsync()));
        }
        finally
        {
            await app.WithDbAsync(async db =>
            {
                if (group is not null)
                {
                    var couponIds = await db.CampaignOffers.IgnoreQueryFilters().Where(o => o.VariantGroup == group && o.CouponId != null).Select(o => o.CouponId!.Value).ToListAsync();
                    await db.CouponTargets.IgnoreQueryFilters().Where(t => couponIds.Contains(t.CouponId)).ExecuteDeleteAsync();
                    await db.Coupons.IgnoreQueryFilters().Where(c => couponIds.Contains(c.Id)).ExecuteDeleteAsync();
                    await db.CampaignOffers.IgnoreQueryFilters().Where(o => o.VariantGroup == group).ExecuteDeleteAsync();
                    await db.Banners.IgnoreQueryFilters().Where(b => b.VariantGroup == group).ExecuteDeleteAsync();
                }
                await db.DesignDocs.IgnoreQueryFilters().Where(d => d.CategoryId == categoryId).ExecuteDeleteAsync();
                await db.Products.Where(p => p.Id == productId).ExecuteUpdateAsync(s => s.SetProperty(p => p.IsActive, false));
            });
        }
    }

    [SqlFact]
    public async Task Category_header_brief_without_a_category_is_refused()
    {
        var since = DateTimeOffset.UtcNow.AddSeconds(-2);
        using var client = app.CreateClient();
        await client.LoginAsync(ShopApp.OwnerPhone);
        await client.GetPageAsync("/admin/design");
        using var response = await client.PostFormAsync("/admin/design?handler=Brief", ("brief", "بنر خیلی ویژهٔ بی‌دسته zzqq"), ("place", "category"));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("#brief", response.Headers.Location?.OriginalString ?? "", StringComparison.Ordinal);
        Assert.False(await app.WithDbAsync(db => db.DesignDocs.AnyAsync(d => d.PresetKey == DesignPresets.CategoryHeader && d.CreatedAt >= since && d.Title.Contains("zzqq"))));
    }

    [SqlFact]
    public async Task Customers_cannot_search_admin_products_or_cancel_campaigns()
    {
        using var client = app.CreateClient();
        await client.LoginAsync(ShopApp.DemoCustomerPhone);
        using var search = await client.Http.GetAsync("/admin/design?handler=Products&q=test");
        Assert.NotEqual(HttpStatusCode.OK, search.StatusCode);

        using var cancel = await client.PostFormAsync($"/admin/design?handler=CancelOffer&id={Guid.NewGuid()}");
        Assert.NotEqual(HttpStatusCode.OK, cancel.StatusCode);
        Assert.False(cancel.StatusCode == HttpStatusCode.Redirect
                     && (cancel.Headers.Location?.OriginalString ?? "").StartsWith("/admin/design", StringComparison.Ordinal));

        // مهمان هم همین‌طور.
        using var guest = app.CreateClient();
        using var anonymous = await guest.Http.GetAsync("/admin/design?handler=Products&q=test");
        Assert.NotEqual(HttpStatusCode.OK, anonymous.StatusCode);
    }
}
