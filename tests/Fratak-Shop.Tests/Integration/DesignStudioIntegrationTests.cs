using Fratak_Shop.Core.Services.Design;
using Fratak_Shop.Core.Services.Seo;
using Fratak_Shop.Core.Services.Smart;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Design;
using Fratak_Shop.Data.Entities.Products;
using Fratak_Shop.Data.Entities.Smart;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;

namespace Fratak_Shop.Tests.Integration;

/// <summary>
/// تست یکپارچهٔ دور ۲۱ روی SQL Server واقعی: صفحه‌های کمپین (<see cref="LandingPageService"/>)، نقشهٔ سایت، طرح‌های استودیو
/// (<see cref="DesignService"/>) و ثبت کارهای «رشد خودکار».
/// <list type="bullet">
/// <item>نامک انگلیسی و یکتا (دو صفحه با یک عنوان ← دو نامک)، پاک‌سازی HTML/CSS هنگام ذخیره، تصویر اشتراک فقط محلی.</item>
/// <item>نمایش عمومی فقط برای صفحهٔ فعال و شروع‌شده؛ صفحهٔ تمام‌شده «Ended».</item>
/// <item>کدهای کوتاه کالا فقط کالای فعال و موجود (پرتخفیف/دسته) را می‌آورند.</item>
/// <item>نقشهٔ سایت فقط کمپین فعالِ در جریان را دارد.</item>
/// <item>ایدهٔ ردشده با همان کلید دوباره «در انتظار» نمی‌شود؛ اجرای روزانهٔ رشد خودکار در یک روز تکرار نمی‌شود.</item>
/// </list>
/// </summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", "Integration")]
[Trait("Area", "Design")]
public class DesignStudioIntegrationTests(SqlServerFixture db)
{
    private static LandingSaveInput Input(string title, string? slug = null, bool active = true, DateTimeOffset? start = null, DateTimeOffset? end = null,
        string? html = null, string? css = null, string? image = null) =>
        new(null, title, slug, null, null, image, null, html ?? "<section class=\"fs-lp-section\"><h2>سلام</h2></section>", css, true, true, active, start, end, "campaign");

    [SqlFact]
    public async Task Landing_pages_get_unique_english_slugs_and_are_sanitized_on_save()
    {
        await using var scope = db.Scope();
        var service = scope.ServiceProvider.GetRequiredService<ILandingPageService>();
        var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
        var key = Guid.NewGuid().ToString("N")[..6];
        var ids = new List<Guid>();
        try
        {
            var first = await service.SaveAsync(Input("حراج آزمون", "itest-" + key,
                html: "<body><h1 onclick=\"x()\">حراج</h1><script>alert(1)</script><div id=\"i9zz\" class=\"fs-lp-grid\">[[deals:4]]</div></body>",
                css: "body{margin:0} #i9zz{gap:8px} @import url(https://evil.com/a.css);", image: "https://evil.com/a.png"), "آزمون");
            Assert.True(first.IsSuccess(), first.Message);
            ids.Add(first.Data);
            var second = await service.SaveAsync(Input("حراج آزمون", "itest-" + key), "آزمون");
            Assert.True(second.IsSuccess(), second.Message);
            ids.Add(second.Data);

            var rows = await context.LandingPages.AsNoTracking().Where(p => ids.Contains(p.Id)).ToListAsync();
            var a = rows.Single(r => r.Id == first.Data);
            var b = rows.Single(r => r.Id == second.Data);
            Assert.Equal("itest-" + key, a.Slug);
            Assert.NotEqual(a.Slug, b.Slug);
            Assert.StartsWith("itest-" + key, b.Slug, StringComparison.Ordinal);
            Assert.DoesNotContain("<script", a.Html);
            Assert.DoesNotContain("onclick", a.Html);
            Assert.DoesNotContain("<body", a.Html);
            Assert.Contains("id=\"i9zz\" class=\"fs-lp-grid\"", a.Html);
            Assert.Contains("[[deals:4]]", a.Html);
            Assert.Equal(".fs-lp{margin:0}\n.fs-lp #i9zz{gap:8px}", a.Css);
            Assert.Null(a.ImageUrl); // تصویر اشتراک بیرونی پذیرفته نمی‌شود
            Assert.Equal("آزمون", a.UpdatedByName);

            // صفحهٔ فارسی‌نام بدون نامک ← نامک انگلیسی ساخته می‌شود.
            var persian = await service.SaveAsync(Input("کمپین یلدا " + key), "آزمون");
            ids.Add(persian.Data);
            var slug = (await context.LandingPages.AsNoTracking().SingleAsync(p => p.Id == persian.Data)).Slug;
            Assert.Matches("^[a-z0-9][a-z0-9-]*$", slug);

            // ورودی نادرست.
            Assert.False((await service.SaveAsync(Input("  "), "آزمون")).IsSuccess());
            Assert.False((await service.SaveAsync(Input("x", start: DateTimeOffset.UtcNow, end: DateTimeOffset.UtcNow.AddHours(-1)), "آزمون")).IsSuccess());
            Assert.False((await service.SaveAsync(Input("x", html: new string('a', 600_000)), "آزمون")).IsSuccess());
            Assert.Equal(EnOperationResultStatus.NotFound,
                (await service.SaveAsync(Input("x") with { Id = Guid.NewGuid() }, "آزمون")).Status);
        }
        finally
        {
            await context.LandingPages.IgnoreQueryFilters().Where(p => ids.Contains(p.Id)).ExecuteDeleteAsync();
        }
    }

    [SqlFact]
    public async Task Only_active_started_pages_are_public_and_ended_ones_are_flagged()
    {
        await using var scope = db.Scope();
        var service = scope.ServiceProvider.GetRequiredService<ILandingPageService>();
        var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
        var cache = scope.ServiceProvider.GetRequiredService<IMemoryCache>();
        var sitemap = scope.ServiceProvider.GetRequiredService<ISitemapService>();
        var key = Guid.NewGuid().ToString("N")[..6];
        var now = DateTimeOffset.UtcNow;
        var ids = new List<Guid>();
        try
        {
            async Task<string> Add(string name, bool active, DateTimeOffset? start, DateTimeOffset? end)
            {
                var result = await service.SaveAsync(Input(name, $"itest-{name}-{key}", active, start, end), "آزمون");
                Assert.True(result.IsSuccess(), result.Message);
                ids.Add(result.Data);
                return $"itest-{name}-{key}";
            }

            var live = await Add("live", true, now.AddDays(-1), now.AddDays(2));
            var draft = await Add("draft", false, null, null);
            var future = await Add("future", true, now.AddDays(1), null);
            var ended = await Add("ended", true, now.AddDays(-5), now.AddMinutes(-1));

            var view = await service.GetPublicAsync(live, now);
            Assert.NotNull(view);
            Assert.False(view!.Ended);
            Assert.NotNull(view.EndAt);
            Assert.Null(await service.GetPublicAsync(draft, now));
            Assert.Null(await service.GetPublicAsync(future, now));
            Assert.True((await service.GetPublicAsync(ended, now))!.Ended);
            Assert.Null(await service.GetPublicAsync("کمپین", now));
            Assert.Null(await service.GetPublicAsync("no-such-" + key, now));

            cache.Remove(CacheInvalidation.SitemapKey);
            var paths = (await sitemap.GetEntriesAsync()).Select(e => e.Path).ToHashSet();
            Assert.Contains($"/campaign/{live}", paths);
            Assert.DoesNotContain($"/campaign/{draft}", paths);
            Assert.DoesNotContain($"/campaign/{future}", paths);
            Assert.DoesNotContain($"/campaign/{ended}", paths);

            // فهرست پنل همه را دارد؛ حذف واقعاً پاک می‌کند.
            var list = await service.ListAsync();
            Assert.True(ids.All(id => list.Any(i => i.Id == id)));
            Assert.True((await service.DeleteAsync(ids[1])).IsSuccess());
            Assert.Null(await service.GetAsync(ids[1]));
            Assert.False((await service.DeleteAsync(Guid.NewGuid())).IsSuccess());
        }
        finally
        {
            await context.LandingPages.IgnoreQueryFilters().Where(p => ids.Contains(p.Id)).ExecuteDeleteAsync();
            cache.Remove(CacheInvalidation.SitemapKey);
        }
    }

    [SqlFact]
    public async Task Product_codes_expand_to_real_in_stock_products_only()
    {
        await using var scope = db.Scope();
        var service = scope.ServiceProvider.GetRequiredService<ILandingPageService>();
        var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
        var category = await context.Categories.AsNoTracking().Where(c => c.IsActive && !c.IsSystem).OrderBy(c => c.CreatedAt).FirstAsync();
        var key = Guid.NewGuid().ToString("N")[..10];

        Product Make(string suffix, bool active, int stock) => new()
        {
            CategoryId = category.Id,
            Name = "کالای کمپین " + suffix,
            Slug = $"lp-{suffix}-{key}",
            SKU = $"LP-{suffix}-{key}",
            Price = 1_000_000,
            DiscountPrice = 10_000, // ۹۹٪ ← بالاترین تخفیف
            StockQuantity = stock,
            IsActive = active,
            SalesCount = 9_000_000,
            PublishedAt = DateTimeOffset.UtcNow.AddDays(-1),
        };
        var good = Make("good", true, 5);
        var hidden = Make("hidden", false, 5);
        var empty = Make("empty", true, 0);
        context.Products.AddRange(good, hidden, empty);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        try
        {
            var deals = await service.ExpandListsAsync("<div>[[deals:24]]</div>");
            Assert.Contains($"[[product:{good.Slug}]]", deals);
            Assert.DoesNotContain(hidden.Slug, deals);
            Assert.DoesNotContain(empty.Slug, deals);
            Assert.Contains("fs-lp-grid", deals);
            Assert.DoesNotContain("[[deals:24]]", deals);

            var byCategory = await service.ExpandListsAsync($"[[category:{category.Slug}:3]]");
            Assert.Contains($"[[product:{good.Slug}]]", byCategory);
            Assert.True(byCategory.Split("[[product:").Length - 1 <= 3);

            Assert.Equal("<p>بدون کد</p>", await service.ExpandListsAsync("<p>بدون کد</p>"));
            // کد ناقص/خطرناک دست نمی‌خورد (و چیزی از پایگاه داده بیرون نمی‌آید).
            Assert.Equal("[[category:../x]]", await service.ExpandListsAsync("[[category:../x]]"));
        }
        finally
        {
            var slugs = new[] { good.Slug, hidden.Slug, empty.Slug };
            await context.Products.Where(p => slugs.Contains(p.Slug)).ExecuteDeleteAsync();
        }
    }

    [SqlFact]
    public async Task Rejected_ideas_stay_rejected_and_links_are_safe()
    {
        await using var scope = db.Scope();
        var designs = scope.ServiceProvider.GetRequiredService<IDesignService>();
        var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
        var ideaKey = "itest:" + Guid.NewGuid().ToString("N");
        Guid? id = null;
        try
        {
            var pendingBefore = await designs.PendingIdeasAsync();
            id = await designs.AddAsync(new DesignDoc
            {
                Title = "ایدهٔ آزمون",
                Kind = DesignKind.Banner,
                Status = DesignStatus.Suggested,
                IsSmart = true,
                PresetKey = DesignPresets.Wide,
                Width = 1600,
                Height = 400,
                Json = DesignTemplates.Blank(1600, 400),
                IdeaKey = ideaKey,
                LinkUrl = "/products",
            });
            Assert.Equal(pendingBefore + 1, await designs.PendingIdeasAsync());
            Assert.True((await designs.RejectAsync(id.Value, "آزمون")).IsSuccess());
            var row = await context.DesignDocs.AsNoTracking().SingleAsync(d => d.Id == id);
            Assert.Equal(DesignStatus.Rejected, row.Status);
            Assert.Equal(pendingBefore, await designs.PendingIdeasAsync());
            Assert.False((await designs.RejectAsync(Guid.NewGuid(), "آزمون")).IsSuccess());

            // انتشار طرح بدون تصویر ممکن نیست.
            Assert.False((await designs.PublishBannerAsync(id.Value, Data.Entities.Marketing.BannerPosition.WideBanner, "/x", "خرید", "آزمون")).IsSuccess());
        }
        finally
        {
            if (id is { } d)
                await context.DesignDocs.Where(x => x.Id == d).ExecuteDeleteAsync();
        }
    }

    [SqlFact]
    public async Task Growth_autopilot_runs_at_most_once_a_day()
    {
        await using var scope = db.Scope();
        var growth = scope.ServiceProvider.GetRequiredService<IGrowthAutopilot>();
        var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
        var now = DateTimeOffset.UtcNow;
        var today = IranClock.Today(now);
        var marker = "itest-" + Guid.NewGuid().ToString("N");
        try
        {
            await growth.LogAsync(today, GrowthActionKind.DailyRun, 0, 1, marker);
            var before = await context.GrowthActions.CountAsync();
            var report = await growth.RunDailyAsync(now);
            Assert.False(report.Ran);
            Assert.Equal(0, report.BannerIdeas(3));
            Assert.Equal(before, await context.GrowthActions.CountAsync());
            Assert.Contains(await growth.RecentAsync(), a => a.Summary == marker);
            var visitors = await growth.VisitorsAsync(today, 15);
            Assert.True(visitors.Count <= 15);
            Assert.All(visitors, v => Assert.True(v.Day <= today && v.Visitors >= 0));
        }
        finally
        {
            await context.GrowthActions.Where(a => a.Summary == marker).ExecuteDeleteAsync();
        }
    }
}
