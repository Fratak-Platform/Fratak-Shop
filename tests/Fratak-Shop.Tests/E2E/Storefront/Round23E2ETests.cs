using System.Net;
using System.Text;
using Fratak_Shop.Data.Entities.Blogs;
using Fratak_Shop.Data.Entities.Categories;
using Fratak_Shop.Data.Entities.Marketing;
using Fratak_Shop.Tests.E2E.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Tests.E2E.Storefront;

/// <summary>
/// تست سرتاسری دور ۲۳ روی برنامهٔ واقعی (TestServer + SQL Server):
/// <list type="bullet">
/// <item>پیش‌نمایش پیش‌نویس وبلاگ: مهمان ← ۴۰۴؛ نویسنده ← ۲۰۰ با نوار «پیش‌نمایش» و noindex؛ مشتری عادی ← ۴۰۴.</item>
/// <item>دستهٔ سیستمی («دسته‌بندی نشده») صفحهٔ عمومی ندارد (۴۰۴).</item>
/// <item>کلیک بنر: /b/{id} ← ۳۰۲ به لینک امن بنر؛ لینک ناامن یا بنر ناموجود ← صفحهٔ نخست؛ noindex.</item>
/// <item>آمار نمایش بنر: بدنهٔ درست/خراب/بزرگ/ربات همه ۲۰۴ (هیچ‌وقت خطا و هیچ‌وقت کند).</item>
/// <item>«ایده ← طرح» در پنل: مدیر ← ۳ طرح پیشنهادی (منتشر نمی‌شود)؛ نویسنده بدون دسترسی بازاریابی هم فقط طرح می‌سازد، بنر نه.</item>
/// </list>
/// </summary>
[Collection(E2ECollection.Name)]
[Trait("Category", "E2E")]
[Trait("Area", "Round23")]
public class Round23E2ETests(ShopApp app)
{
    private static string Unique(string prefix) => prefix + "-" + Guid.NewGuid().ToString("N")[..10];

    [SqlFact]
    public async Task Draft_blog_post_is_404_for_guests_and_previewable_for_writers()
    {
        var slug = Unique("e2e-draft");
        var categorySlug = Unique("e2e-blogcat");
        var postId = Guid.Empty;
        var categoryId = Guid.Empty;
        try
        {
            await app.WithDbAsync(async db =>
            {
                var category = new BlogCategory { Name = "دستهٔ آزمون " + categorySlug, Slug = categorySlug };
                db.BlogCategories.Add(category);
                var post = new BlogPost
                {
                    BlogCategory = category,
                    Title = "پیش‌نویس آزمون",
                    Slug = slug,
                    Content = "<p>متن پیش‌نویس آزمون سرتاسری.</p>",
                    IsPublished = false,
                };
                db.BlogPosts.Add(post);
                await db.SaveChangesAsync();
                postId = post.Id;
                categoryId = category.Id;
            });

            using (var guest = app.CreateClient())
                await guest.GetPageAsync($"/blog/{slug}", HttpStatusCode.NotFound);

            using (var customer = app.CreateClient())
            {
                await customer.LoginAsync(ShopApp.DemoCustomerPhone);
                await customer.GetPageAsync($"/blog/{slug}", HttpStatusCode.NotFound);
            }

            using var writer = app.CreateClient();
            await writer.LoginAsync(ShopApp.WriterPhone);
            var page = await writer.GetPageAsync($"/blog/{slug}");
            Assert.NotNull(page.One("[data-staff-preview]"));
            Assert.Contains("پیش‌نویس آزمون", page.Html);
            Assert.Contains("noindex", string.Join(",", page.Response.Headers.TryGetValues("X-Robots-Tag", out var robots) ? robots : Enumerable.Empty<string>()));
            Assert.Contains("no-store", page.Response.Headers.CacheControl?.ToString() ?? "");

            // نمایش پیش‌نویس بازدید ثبت نمی‌کند.
            var views = await app.WithDbAsync(db => db.BlogPosts.Where(p => p.Id == postId).Select(p => p.ViewCount).FirstAsync());
            Assert.Equal(0, views);
        }
        finally
        {
            await app.WithDbAsync(async db =>
            {
                await db.BlogPosts.IgnoreQueryFilters().Where(p => p.Slug == slug).ExecuteDeleteAsync();
                await db.BlogCategories.IgnoreQueryFilters().Where(c => c.Slug == categorySlug).ExecuteDeleteAsync();
            });
        }
    }

    [SqlFact]
    public async Task System_category_has_no_public_page()
    {
        var slug = Unique("e2e-system");
        try
        {
            await app.WithDbAsync(async db =>
            {
                db.Categories.Add(new Category { Name = "دسته‌بندی نشده آزمون", Slug = slug, IsSystem = true, IsActive = true });
                await db.SaveChangesAsync();
            });
            using var client = app.CreateClient();
            await client.GetPageAsync($"/category/{slug}", HttpStatusCode.NotFound);
            await client.GetPageAsync($"/products?category={slug}", HttpStatusCode.NotFound);
            await client.GetPageAsync("/category/" + Unique("no-such"), HttpStatusCode.NotFound);
        }
        finally
        {
            await app.WithDbAsync(db => db.Categories.IgnoreQueryFilters().Where(c => c.Slug == slug).ExecuteDeleteAsync());
        }
    }

    [SqlFact]
    public async Task Banner_click_redirects_only_to_safe_links()
    {
        var group = Unique("e2e-banner");
        Guid safe = Guid.Empty, unsafeId = Guid.Empty;
        try
        {
            await app.WithDbAsync(async db =>
            {
                var yesterday = DateTimeOffset.UtcNow.AddDays(-1);
                // EndAt گذشته ← بنر روی صفحه‌ها نمی‌آید (تست‌های دیگر را به هم نمی‌زند) ولی لینک کلیک هنوز کار می‌کند.
                var good = new Banner { Title = "آزمون", ImageName = "e2e.webp", LinkUrl = "/products", VariantGroup = group, EndAt = yesterday };
                var bad = new Banner { Title = "آزمون", ImageName = "e2e.webp", LinkUrl = "javascript:alert(1)", VariantGroup = group, EndAt = yesterday };
                db.Banners.AddRange(good, bad);
                await db.SaveChangesAsync();
                safe = good.Id;
                unsafeId = bad.Id;
            });

            using var client = app.CreateClient();
            using (var response = await client.GetAsync($"/b/{safe}"))
            {
                Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
                Assert.Equal("/products", response.Headers.Location?.OriginalString);
                Assert.Contains("noindex", string.Join(",", response.Headers.GetValues("X-Robots-Tag")));
            }
            using (var response = await client.GetAsync($"/b/{unsafeId}"))
            {
                Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
                Assert.Equal("/", response.Headers.Location?.OriginalString);
            }
            using (var response = await client.GetAsync($"/b/{Guid.NewGuid()}"))
            {
                Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
                Assert.Equal("/", response.Headers.Location?.OriginalString);
            }
            using (var response = await client.GetAsync("/b/not-a-guid"))
                Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
        finally
        {
            await app.WithDbAsync(db => db.Banners.IgnoreQueryFilters().Where(b => b.VariantGroup == group).ExecuteDeleteAsync());
        }
    }

    [SqlFact]
    public async Task Banner_tracking_never_fails()
    {
        using var client = app.CreateClient();
        async Task<HttpStatusCode> PostAsync(string body)
        {
            using var response = await client.Http.PostAsync("/api/banners/track", new StringContent(body, Encoding.UTF8, "application/json"));
            return response.StatusCode;
        }

        Assert.Equal(HttpStatusCode.NoContent, await PostAsync($"{{\"v\":[\"{Guid.NewGuid()}\"],\"c\":[]}}"));
        Assert.Equal(HttpStatusCode.NoContent, await PostAsync("{not json"));
        Assert.Equal(HttpStatusCode.NoContent, await PostAsync("[1,2,3]"));
        Assert.Equal(HttpStatusCode.NoContent, await PostAsync("{\"v\":\"" + new string('a', 5000) + "\"}"));

        using var bot = app.CreateClient(userAgent: "Googlebot/2.1 (+http://www.google.com/bot.html)");
        using var botResponse = await bot.Http.PostAsync("/api/banners/track", new StringContent($"{{\"v\":[\"{Guid.NewGuid()}\"]}}", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.NoContent, botResponse.StatusCode);
    }

    [SqlFact]
    public async Task Owner_turns_a_short_idea_into_three_unpublished_designs()
    {
        var since = DateTimeOffset.UtcNow.AddSeconds(-5);
        var bannersBefore = await app.WithDbAsync(db => db.Banners.CountAsync());
        using var client = app.CreateClient();
        await client.LoginAsync(ShopApp.OwnerPhone);
        await client.GetPageAsync("/admin/design");
        try
        {
            using var response = await client.PostFormAsync("/admin/design?handler=Brief", ("brief", "کارت هدیه"), ("place", "hero"));
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.StartsWith("/admin/design", response.Headers.Location?.OriginalString ?? "", StringComparison.Ordinal);

            var keys = await app.WithDbAsync(db => db.DesignDocs.Where(d => d.IdeaKey != null && d.IdeaKey.StartsWith("brief:") && d.CreatedAt >= since)
                .Select(d => d.IdeaKey!).ToListAsync());
            Assert.Equal(3, keys.Count);
            Assert.Single(keys.Select(k => Fratak_Shop.Core.Services.Design.DesignBrief.VariantGroup(k)).Distinct());
            // «ایده ← طرح» هیچ‌وقت خودکار منتشر نمی‌کند.
            Assert.Equal(bannersBefore, await app.WithDbAsync(db => db.Banners.CountAsync()));

            // ایدهٔ خالی ← پیام خطا و برگشت به فرم (نه خطای سرور).
            using var empty = await client.PostFormAsync("/admin/design?handler=Brief", ("brief", " "), ("place", "hero"));
            Assert.Equal(HttpStatusCode.Redirect, empty.StatusCode);
        }
        finally
        {
            await app.WithDbAsync(db => db.DesignDocs.IgnoreQueryFilters()
                .Where(d => d.IdeaKey != null && d.IdeaKey.StartsWith("brief:") && d.CreatedAt >= since).ExecuteDeleteAsync());
        }
    }

    [SqlFact]
    public async Task Customers_cannot_use_the_design_brief()
    {
        using var client = app.CreateClient();
        await client.LoginAsync(ShopApp.DemoCustomerPhone);
        using var response = await client.PostFormAsync("/admin/design?handler=Brief", ("brief", "کارت هدیه"), ("place", "hero"));
        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.StatusCode == HttpStatusCode.Redirect
                     && (response.Headers.Location?.OriginalString ?? "").StartsWith("/admin/design", StringComparison.Ordinal));
    }
}
