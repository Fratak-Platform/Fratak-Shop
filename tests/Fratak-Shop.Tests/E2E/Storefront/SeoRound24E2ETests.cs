using System.Net;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Entities.Brands;
using Fratak_Shop.Data.Entities.Categories;
using Fratak_Shop.Data.Entities.Products;
using Fratak_Shop.Tests.E2E.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;

namespace Fratak_Shop.Tests.E2E.Storefront;

/// <summary>
/// تست سرتاسری دور ۲۴ بستهٔ E (سئو) روی برنامهٔ واقعی:
/// <list type="bullet">
/// <item>۴۰ صفحهٔ «دسته + برند»: H1، canonical تمیز (نسخهٔ ?brand= هم به همان)، ایندکس با ۳ کالا و noindex با کمتر؛ لینک از صفحهٔ دسته؛ حضور در نقشهٔ سایت.</item>
/// <item>۴۲/۴۳/۴۴ واژه‌نامه، راهنمای جامع (کم‌محتوا ← noindex) و مناسبت؛ نامک ناشناخته ← ۴۰۴؛ ورودی مخرب ← بدون خطای ۵۰۰.</item>
/// <item>۴۸ تا ۵۰ صفحهٔ «فرصت‌های سئو» برای مدیر (سه زبانه)، افزودن مترادف با توکن ضدجعل، مشتری و مهمان بدون دسترسی.</item>
/// </list>
/// </summary>
[Collection(E2ECollection.Name)]
[Trait("Category", "E2E")]
[Trait("Area", "SEO")]
public class SeoRound24E2ETests(ShopApp app)
{
    private sealed record Catalog(Guid CategoryId, string CategorySlug, string CategoryName, Guid RichBrandId, string RichBrand, string RichBrandName,
        Guid ThinBrandId, string ThinBrand);

    /// <summary>دسته با ۶ کالا: برند «پر» با ۳ کالا، برند «کم» با ۲ کالا و یک کالای بی‌برند.</summary>
    private async Task<Catalog> CreateCatalogAsync()
    {
        var key = Guid.NewGuid().ToString("N")[..8];
        return await app.WithDbAsync(async db =>
        {
            var category = new Category { Name = "اسپیکر آزمون " + key, Slug = "r24e-" + key, IsActive = true };
            var rich = new Brand { Name = "برند پر " + key, Slug = "r24e-rich-" + key, IsActive = true };
            var thin = new Brand { Name = "برند کم " + key, Slug = "r24e-thin-" + key, IsActive = true };
            db.AddRange(category, rich, thin);
            await db.SaveChangesAsync();
            for (var i = 0; i < 6; i++)
            {
                var product = new Product
                {
                    CategoryId = category.Id,
                    BrandId = i < 3 ? rich.Id : i < 5 ? thin.Id : null,
                    Name = $"اسپیکر آزمون {key} {i}",
                    Slug = $"r24e-p{i}-{key}",
                    SKU = $"R24E-{key}-{i}",
                    Price = 500_000 * (i + 1),
                    StockQuantity = 3,
                    IsActive = true,
                    PublishedAt = DateTimeOffset.UtcNow.AddDays(-1),
                };
                product.Features.Add(new ProductFeature { Key = "شارژدهی", Value = $"{5 * (i + 1)} ساعت" }); // بخش «بازهٔ مشخصات» راهنما
                db.Products.Add(product);
            }
            await db.SaveChangesAsync();
            return new Catalog(category.Id, category.Slug, category.Name, rich.Id, rich.Slug, rich.Name, thin.Id, thin.Slug);
        });
    }

    private Task DeleteCatalogAsync(Catalog catalog) => app.WithDbAsync(async db =>
    {
        await db.ProductFeatures.IgnoreQueryFilters().Where(f => f.Product.CategoryId == catalog.CategoryId).ExecuteDeleteAsync();
        await db.Products.IgnoreQueryFilters().Where(p => p.CategoryId == catalog.CategoryId).ExecuteDeleteAsync();
        await db.Categories.IgnoreQueryFilters().Where(c => c.Id == catalog.CategoryId).ExecuteDeleteAsync();
        await db.Brands.IgnoreQueryFilters().Where(b => b.Id == catalog.RichBrandId || b.Id == catalog.ThinBrandId).ExecuteDeleteAsync();
    });

    private static string Robots(HtmlPage page) => page.One("meta[name='robots']")?.GetAttribute("content") ?? "";

    [SqlFact]
    public async Task Category_brand_landing_is_indexable_only_with_enough_products()
    {
        var catalog = await CreateCatalogAsync();
        try
        {
            using var client = app.CreateClient();
            var path = $"/category/{catalog.CategorySlug}/brand/{catalog.RichBrand}";

            var landing = await client.GetPageAsync(path);
            var h1 = landing.One("h1")?.TextContent ?? "";
            Assert.Contains("خرید", h1, StringComparison.Ordinal);
            Assert.Contains(catalog.RichBrandName, h1, StringComparison.Ordinal);
            Assert.Contains(catalog.CategoryName, h1, StringComparison.Ordinal);
            Assert.EndsWith(path, landing.One("link[rel='canonical']")?.GetAttribute("href"), StringComparison.Ordinal);
            Assert.DoesNotContain("noindex", Robots(landing));
            var grid = landing.One("[data-search-q]")?.TextContent ?? "";
            Assert.Contains(catalog.CategoryName + " 0", grid, StringComparison.Ordinal);
            Assert.Contains(catalog.CategoryName + " 2", grid, StringComparison.Ordinal);
            Assert.DoesNotContain(catalog.CategoryName + " 3", grid, StringComparison.Ordinal); // کالای برند دیگر

            // نسخهٔ کوئری‌دار همان صفحه است و canonical به نشانی تمیز اشاره می‌کند.
            var query = await client.GetPageAsync($"/category/{catalog.CategorySlug}?brand={catalog.RichBrand}");
            Assert.EndsWith(path, query.One("link[rel='canonical']")?.GetAttribute("href"), StringComparison.Ordinal);

            // برند با ۲ کالا ← noindex (صفحهٔ کم‌محتوا به گوگل نمی‌رسد).
            var thin = await client.GetPageAsync($"/category/{catalog.CategorySlug}/brand/{catalog.ThinBrand}");
            Assert.Contains("noindex", Robots(thin));

            // صفحهٔ دسته به صفحهٔ برند پر لینک می‌دهد، نه به برند کم.
            var categoryPage = await client.GetPageAsync($"/category/{catalog.CategorySlug}");
            Assert.NotNull(categoryPage.One($"a[href='{path}']"));
            Assert.Null(categoryPage.One($"a[href='/category/{catalog.CategorySlug}/brand/{catalog.ThinBrand}']"));
            Assert.NotNull(categoryPage.One($"a[href='/guide/{catalog.CategorySlug}']")); // ۶ کالا + دو برند ← راهنمای قابل ایندکس

            // برند ناموجود در مسیر ← بدون خطای سرور.
            using var unknown = await client.GetAsync($"/category/{catalog.CategorySlug}/brand/no-such-brand");
            Assert.True((int)unknown.StatusCode < 500);

            // نقشهٔ سایت (کش پاک می‌شود تا دادهٔ تازه دیده شود).
            await using (var scope = app.Scope())
                scope.ServiceProvider.GetRequiredService<IMemoryCache>().Remove(CacheInvalidation.SitemapKey);
            using var sitemap = await client.GetAsync("/sitemap.xml");
            var xml = await sitemap.Content.ReadAsStringAsync();
            Assert.Contains(path + "</loc>", xml);
            Assert.DoesNotContain($"/category/{catalog.CategorySlug}/brand/{catalog.ThinBrand}</loc>", xml);
            Assert.Contains("xmlns:video=\"http://www.google.com/schemas/sitemap-video/1.1\"", xml);
            Assert.Contains($"/guide/{catalog.CategorySlug}</loc>", xml);
        }
        finally
        {
            await DeleteCatalogAsync(catalog);
            await using var scope = app.Scope();
            scope.ServiceProvider.GetRequiredService<IMemoryCache>().Remove(CacheInvalidation.SitemapKey);
        }
    }

    [SqlFact]
    public async Task Guide_page_renders_real_sections_with_faq_schema()
    {
        var catalog = await CreateCatalogAsync();
        try
        {
            using var client = app.CreateClient();
            var page = await client.GetPageAsync($"/guide/{catalog.CategorySlug}");
            Assert.Contains("راهنمای جامع خرید " + catalog.CategoryName, page.One("h1")?.TextContent, StringComparison.Ordinal);
            Assert.DoesNotContain("noindex", Robots(page));
            Assert.NotNull(page.One("#brands"));
            Assert.NotNull(page.One($"#brands a[href='/category/{catalog.CategorySlug}/brand/{catalog.RichBrand}']"));
            Assert.NotNull(page.One("#popular"));
            Assert.Contains("\"FAQPage\"", page.Html);
            Assert.EndsWith($"/guide/{catalog.CategorySlug}", page.One("link[rel='canonical']")?.GetAttribute("href"), StringComparison.Ordinal);

            // دسته‌ای با کمتر از ۶ کالا ← noindex.
            await app.WithDbAsync(db => db.Products.Where(p => p.CategoryId == catalog.CategoryId && p.BrandId == null).ExecuteUpdateAsync(x => x.SetProperty(p => p.IsActive, false)));
            await using (var scope = app.Scope())
                scope.ServiceProvider.GetRequiredService<IMemoryCache>().Remove("seo:hub:guide:" + catalog.CategorySlug);
            var thin = await client.GetPageAsync($"/guide/{catalog.CategorySlug}?v=2");
            Assert.Contains("noindex", Robots(thin));
        }
        finally
        {
            await DeleteCatalogAsync(catalog);
        }
    }

    [SqlFact]
    public async Task Glossary_and_occasion_pages_and_unknown_slugs()
    {
        using var client = app.CreateClient();
        var glossary = await client.GetPageAsync("/glossary");
        Assert.Contains("واژه‌نامه", glossary.One("h1")?.TextContent, StringComparison.Ordinal);

        var yalda = await client.GetPageAsync("/occasion/yalda");
        Assert.Contains("شب یلدا", yalda.One("h1")?.TextContent, StringComparison.Ordinal);
        Assert.NotEmpty(yalda.All("details"));

        foreach (var url in new[] { "/occasion/no-such-day", "/guide/no-such-category-r24" })
        {
            using var response = await client.GetAsync(url);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
        foreach (var url in new[] { "/occasion/%3Cscript%3E", "/guide/%27%20or%201%3D1--", "/guide/" + new string('a', 300) })
        {
            using var response = await client.GetAsync(url);
            Assert.True((int)response.StatusCode < 500, url);
        }
    }

    [SqlFact]
    public async Task Seo_opportunities_admin_page_and_synonym_post()
    {
        var key = Guid.NewGuid().ToString("N")[..8];
        try
        {
            using var owner = app.CreateClient();
            await owner.LoginAsync(ShopApp.OwnerPhone);
            foreach (var tab in new[] { "low", "searches", "cannibal", "<script>" })
            {
                var page = await owner.GetPageAsync("/admin/seo/opportunities?tab=" + Uri.EscapeDataString(tab));
                Assert.Contains("فرصت‌های سئو", page.Html, StringComparison.Ordinal);
            }

            using var added = await owner.PostFormAsync("/admin/seo/opportunities?handler=Synonym", ("terms", $"zz{key}, yy{key}"));
            Assert.Equal(HttpStatusCode.Redirect, added.StatusCode);
            Assert.Equal(1, await app.WithDbAsync(db => db.SearchSynonyms.CountAsync(s => s.Terms.Contains("zz" + key))));

            using var bad = await owner.PostFormAsync("/admin/seo/opportunities?handler=Synonym", ("terms", $"<b>{key}</b>, x"));
            Assert.Equal(HttpStatusCode.Redirect, bad.StatusCode);
            Assert.Equal(0, await app.WithDbAsync(db => db.SearchSynonyms.CountAsync(s => s.Terms.Contains("<b>" + key))));

            // بدون توکن ضدجعل ← رد.
            using var csrf = await owner.PostRawFormAsync("/admin/seo/opportunities?handler=Synonym", ("terms", $"aa{key}, bb{key}"));
            Assert.NotEqual(HttpStatusCode.Redirect, csrf.StatusCode);

            using var customer = app.CreateClient();
            await customer.LoginAsync(ShopApp.DemoCustomerPhone);
            using var asCustomer = await customer.GetAsync("/admin/seo/opportunities");
            Assert.NotEqual(HttpStatusCode.OK, asCustomer.StatusCode);
            using var guest = app.CreateClient();
            using var asGuest = await guest.GetAsync("/admin/seo/opportunities");
            Assert.NotEqual(HttpStatusCode.OK, asGuest.StatusCode);
        }
        finally
        {
            await app.WithDbAsync(db => db.SearchSynonyms.IgnoreQueryFilters().Where(s => s.Terms.Contains(key)).ExecuteDeleteAsync());
        }
    }
}
