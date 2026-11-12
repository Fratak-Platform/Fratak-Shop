using Fratak_Shop.Core.Services.Seo;
using Fratak_Shop.Core.Services.Seo.Autopilot;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Brands;
using Fratak_Shop.Data.Entities.Categories;
using Fratak_Shop.Data.Entities.Products;
using Fratak_Shop.Data.Entities.Searches;
using Fratak_Shop.Data.Utilities.SeoManagement;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;

namespace Fratak_Shop.Tests.Integration;

/// <summary>
/// تست یکپارچهٔ دور ۲۴ بستهٔ E (سئو) روی SQL Server واقعی:
/// <list type="bullet">
/// <item>۴۳ راهنمای جامع دسته از دادهٔ واقعی (برندها، بازهٔ مشخصات، پرفروش‌ها) و noindex نبودن دستهٔ پرمحتوا؛ دستهٔ ناموجود ← null.</item>
/// <item>۴۰/۴۷ نقشهٔ سایت: صفحهٔ «دسته + برند» فقط با ۳ کالا یا بیشتر، راهنمای قابل ایندکس و ویدیوی کالا (&lt;video:video&gt;).</item>
/// <item>۴۴ صفحهٔ مناسبت با نامک ثابت؛ نامک ناشناخته ← null.</item>
/// <item>۴۸ کم‌بازدیدها با دلیل؛ ۴۹ جست‌وجوی بی‌نتیجه ← نزدیک‌ترین دسته و افزودن مترادف (تکراری رد)؛ ۵۰ هم‌نوع‌خواری کلمه.</item>
/// </list>
/// هر آزمون دادهٔ خودش را با کلید یکتا می‌سازد و در finally پاک می‌کند.
/// </summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", "Integration")]
[Trait("Area", "SEO")]
public class SeoRound24IntegrationTests(SqlServerFixture db)
{
    private static string Key() => Guid.NewGuid().ToString("N")[..8];

    private static async Task DeleteCatalogAsync(FratakShopDbContext context, Guid? categoryId, params Guid[] brandIds)
    {
        context.ChangeTracker.Clear();
        if (categoryId is { } id)
        {
            var products = await context.Products.IgnoreQueryFilters().Where(p => p.CategoryId == id).Select(p => p.Id).ToListAsync();
            await context.ProductFeatures.IgnoreQueryFilters().Where(f => products.Contains(f.ProductId)).ExecuteDeleteAsync();
            await context.ProductVideos.IgnoreQueryFilters().Where(v => products.Contains(v.ProductId)).ExecuteDeleteAsync();
            await context.ProductImages.IgnoreQueryFilters().Where(i => products.Contains(i.ProductId)).ExecuteDeleteAsync();
            await context.Products.IgnoreQueryFilters().Where(p => p.CategoryId == id).ExecuteDeleteAsync();
            await context.Categories.IgnoreQueryFilters().Where(c => c.Id == id).ExecuteDeleteAsync();
        }
        if (brandIds.Length > 0)
            await context.Brands.IgnoreQueryFilters().Where(b => brandIds.Contains(b.Id)).ExecuteDeleteAsync();
    }

    [SqlFact]
    public async Task Guide_combo_pages_and_video_sitemap_come_from_real_data()
    {
        var key = Key();
        await using var scope = db.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
        var category = new Category { Name = "هدفون آزمون " + key, Slug = "r24-guide-" + key, IsActive = true };
        var brandA = new Brand { Name = "برند الف " + key, Slug = "r24-a-" + key, IsActive = true };
        var brandB = new Brand { Name = "برند ب " + key, Slug = "r24-b-" + key, IsActive = true };
        try
        {
            context.AddRange(category, brandA, brandB);
            await context.SaveChangesAsync();
            var products = new List<Product>();
            for (var i = 0; i < 6; i++)
            {
                var product = new Product
                {
                    CategoryId = category.Id,
                    BrandId = i < 3 ? brandA.Id : i < 5 ? brandB.Id : null,
                    Name = $"هدفون آزمون {key} مدل {i}",
                    Slug = $"r24-p{i}-{key}",
                    SKU = $"R24-{key}-{i}",
                    Price = 1_000_000 * (i + 1),
                    StockQuantity = 5,
                    IsActive = true,
                    PublishedAt = DateTimeOffset.UtcNow.AddDays(-2),
                    SalesCount = 10 - i,
                };
                product.Features.Add(new ProductFeature { Key = "شارژدهی", Value = $"{10 * (i + 1)} ساعت" });
                products.Add(product);
            }
            products[0].Images.Add(new ProductImage { ImageName = $"r24-{key}.webp", IsPrimary = true });
            products[0].Videos.Add(new ProductVideo { Title = "آنباکس", Provider = ProductVideoProvider.Aparat, Source = "abc123XYZ", IsActive = true, DurationSeconds = 95 });
            context.Products.AddRange(products);
            await context.SaveChangesAsync();

            var hubs = scope.ServiceProvider.GetRequiredService<ISeoHubService>();
            var guide = await hubs.GuideAsync(category.Slug);
            Assert.NotNull(guide);
            Assert.Equal(6, guide!.Facts.Count);
            Assert.Equal(2, guide.Brands.Count);
            Assert.Equal(brandA.Slug, guide.Brands[0].Slug);
            Assert.Equal(3, guide.Brands[0].Count);
            var battery = Assert.Single(guide.Specs, s => s.Name == "شارژدهی");
            Assert.Equal("10 ساعت", battery.Lowest);
            Assert.Equal("60 ساعت", battery.Highest);
            Assert.Equal("battery-life", battery.TermSlug);
            Assert.Equal(products[0].Id, guide.TopProducts[0]); // پرفروش‌ترین اول
            Assert.True(guide.Indexable);
            Assert.Null(await hubs.GuideAsync("r24-missing-" + key));
            Assert.Null(await hubs.GuideAsync(""));

            var cache = scope.ServiceProvider.GetRequiredService<IMemoryCache>();
            cache.Remove(CacheInvalidation.SitemapKey);
            var entries = await scope.ServiceProvider.GetRequiredService<ISitemapService>().GetEntriesAsync();
            Assert.Contains(entries, e => e.Path == $"/category/{category.Slug}/brand/{brandA.Slug}");
            Assert.DoesNotContain(entries, e => e.Path == $"/category/{category.Slug}/brand/{brandB.Slug}"); // فقط ۲ کالا
            Assert.Contains(entries, e => e.Path == $"/guide/{category.Slug}");
            var withVideo = Assert.Single(entries, e => e.Path == $"/product/{products[0].Slug}");
            var video = Assert.Single(withVideo.Videos!);
            Assert.Contains("aparat.com", video.PlayerUrl);
            Assert.Equal($"/uploads/products/r24-{key}.webp", video.Thumbnail);
            Assert.StartsWith("آنباکس", video.Description, StringComparison.Ordinal);
            Assert.Equal(95, video.DurationSeconds);
            Assert.Null(entries.Single(e => e.Path == $"/product/{products[1].Slug}").Videos);
        }
        finally
        {
            await DeleteCatalogAsync(context, category.Id, brandA.Id, brandB.Id);
            scope.ServiceProvider.GetRequiredService<IMemoryCache>().Remove(CacheInvalidation.SitemapKey);
        }
    }

    [SqlFact]
    public async Task Occasion_pages_resolve_fixed_slugs_only()
    {
        await using var scope = db.Scope();
        var hubs = scope.ServiceProvider.GetRequiredService<ISeoHubService>();
        var yalda = await hubs.OccasionAsync("yalda");
        Assert.NotNull(yalda);
        Assert.Equal("yalda", yalda!.Occasion.Slug);
        Assert.True(yalda.Questions.Count >= 2);
        Assert.False(string.IsNullOrWhiteSpace(yalda.DateLabel));
        Assert.True(yalda.Products.Count <= 24);
        Assert.Null(await hubs.OccasionAsync("nope"));
        Assert.Null(await hubs.OccasionAsync(""));

        var glossary = await hubs.GlossaryAsync();
        Assert.Equal(glossary.Terms.Count, glossary.Terms.Select(t => t.Slug).Distinct().Count());
    }

    [SqlFact]
    public async Task Low_traffic_lists_old_unvisited_products_with_reasons()
    {
        var productId = await db.CreateProductAsync();
        await using var scope = db.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
        try
        {
            await context.Products.Where(p => p.Id == productId).ExecuteUpdateAsync(s => s
                .SetProperty(p => p.PublishedAt, DateTimeOffset.UtcNow.AddDays(-3650))
                .SetProperty(p => p.ViewCount, 0)
                .SetProperty(p => p.Description, (string?)null));
            var slug = await context.Products.Where(p => p.Id == productId).Select(p => p.Slug).SingleAsync();

            var rows = await scope.ServiceProvider.GetRequiredService<ISeoOpportunityService>().LowTrafficAsync(200);
            var row = Assert.Single(rows, r => r.Url == "/product/" + slug);
            Assert.Equal("کالا", row.Kind);
            Assert.Equal($"/admin/products/edit/{productId}", row.AdminUrl);
            Assert.True(row.AgeDays >= 3600);
            Assert.Contains(row.Reasons, r => r.Contains("توضیح کالا کوتاه است", StringComparison.Ordinal));
        }
        finally
        {
            await context.Products.Where(p => p.Id == productId).ExecuteUpdateAsync(s => s.SetProperty(p => p.IsActive, false));
        }
    }

    [SqlFact]
    public async Task Search_gap_suggests_closest_category_and_synonym_is_added_once()
    {
        var key = Key();
        var name = $"zorvex{key} blender";
        var query = $"zorvex{key} blendr";
        await using var scope = db.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
        var category = new Category { Name = name, Slug = "r24-gap-" + key, IsActive = true };
        var log = new SearchQueryLog { Query = query, NormalizedQuery = query, SearchCount = 1_000_000, LastResultCount = 0, LastSearchedAt = DateTimeOffset.UtcNow };
        var noise = new SearchQueryLog { Query = $"q{key} once", NormalizedQuery = $"q{key} once", SearchCount = 1, LastResultCount = 0, LastSearchedAt = DateTimeOffset.UtcNow };
        try
        {
            context.AddRange(category, log, noise);
            await context.SaveChangesAsync();
            var service = scope.ServiceProvider.GetRequiredService<ISeoOpportunityService>();

            var gaps = await service.SearchGapsAsync(200);
            Assert.DoesNotContain(gaps, g => g.Query == noise.Query); // یک بار جست‌وجو نویز است
            var gap = Assert.Single(gaps, g => g.Query == query);
            Assert.Equal("/category/" + category.Slug, gap.TargetUrl);
            Assert.Equal($"{query}, {name}", gap.SynonymTerms);

            Assert.True(await service.AddSynonymAsync(gap.SynonymTerms!));
            Assert.False(await service.AddSynonymAsync(gap.SynonymTerms!));
            Assert.False(await service.AddSynonymAsync($"{name}، {query}")); // همان مجموعه با ترتیب دیگر
            Assert.False(await service.AddSynonymAsync("<script>, x"));
            Assert.Equal(1, await context.SearchSynonyms.CountAsync(s => s.Terms.Contains("zorvex" + key)));

            var again = Assert.Single(await service.SearchGapsAsync(200), g => g.Query == query);
            Assert.Null(again.SynonymTerms);
        }
        finally
        {
            context.ChangeTracker.Clear();
            await context.SearchSynonyms.IgnoreQueryFilters().Where(s => s.Terms.Contains("zorvex" + key)).ExecuteDeleteAsync();
            await context.SearchQueryLogs.IgnoreQueryFilters().Where(l => l.Id == log.Id || l.Id == noise.Id).ExecuteDeleteAsync();
            await context.Categories.IgnoreQueryFilters().Where(c => c.Id == category.Id).ExecuteDeleteAsync();
        }
    }

    [SqlFact]
    public async Task Cannibalization_detects_category_and_product_with_same_keyword()
    {
        var key = Key();
        var keyword = "kw" + key;
        await using var scope = db.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
        var category = new Category { Name = "دسته " + key, Slug = "r24-can-" + key, IsActive = true, Seo = new SeoMetaData { FocusKeyword = keyword } };
        try
        {
            context.Categories.Add(category);
            await context.SaveChangesAsync();
            context.Products.Add(new Product
            {
                CategoryId = category.Id, Name = "کالا " + key, Slug = "r24-can-p-" + key, SKU = "R24C-" + key, Price = 100_000, StockQuantity = 1,
                IsActive = true, PublishedAt = DateTimeOffset.UtcNow.AddDays(-1), Seo = new SeoMetaData { FocusKeyword = keyword.ToUpperInvariant() },
            });
            await context.SaveChangesAsync();

            var groups = await scope.ServiceProvider.GetRequiredService<ISeoOpportunityService>().CannibalizationAsync(200);
            var group = Assert.Single(groups, g => g.Keyword.Equals(keyword, StringComparison.OrdinalIgnoreCase));
            Assert.Equal(2, group.Pages.Count);
            Assert.Contains(group.Pages, p => p.Kind == "دسته" && p.Url == "/category/" + category.Slug);
            Assert.Contains(group.Pages, p => p.Kind == "کالا" && p.AdminUrl!.StartsWith("/admin/products/edit/", StringComparison.Ordinal));
            Assert.Contains("صفحهٔ دسته", group.Advice, StringComparison.Ordinal);
        }
        finally
        {
            await DeleteCatalogAsync(context, category.Id);
        }
    }
}
