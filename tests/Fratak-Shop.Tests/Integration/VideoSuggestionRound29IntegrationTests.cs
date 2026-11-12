using Fratak_Shop.Core.Services.Smart;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Products;
using Fratak_Shop.Data.Entities.Smart;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Fratak_Shop.Tests.Integration;

/// <summary>
/// دور ۲۹ (#۱۸) – «ویدیوی پیشنهادی کالا» روی SQL Server واقعی (بدون شبکه؛ نتیجهٔ جست‌وجو دستی ساخته می‌شود):
/// <list type="bullet">
/// <item>ذخیره بدون تکرار؛ لینکی که رد شده دوباره پیشنهاد نمی‌شود.</item>
/// <item>«افزودن از آپارات» ویدیوی جاسازی به کالا اضافه می‌کند؛ منبع غیرآپارات رد می‌شود.</item>
/// <item>نامزدها: کالای دارای ویدیو یا پیشنهاد منتظر، و کالای «اخیراً بررسی‌شده» نمی‌آیند.</item>
/// </list>
/// هر آزمون دادهٔ خودش را در finally پاک می‌کند.
/// </summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", "Integration")]
[Trait("Area", "Round29")]
public class VideoSuggestionRound29IntegrationTests(SqlServerFixture db)
{
    private static async Task CleanAsync(FratakShopDbContext context, Guid productId)
    {
        context.ChangeTracker.Clear();
        await context.ProductVideoSuggestions.IgnoreQueryFilters().Where(v => v.ProductId == productId).ExecuteDeleteAsync();
        await context.ProductVideos.IgnoreQueryFilters().Where(v => v.ProductId == productId).ExecuteDeleteAsync();
    }

    [SqlFact]
    public async Task Add_is_deduplicated_and_dismissed_links_never_return()
    {
        var productId = await db.CreateProductAsync();
        await using var scope = db.Scope();
        var service = scope.ServiceProvider.GetRequiredService<IVideoSuggestionService>();
        var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
        try
        {
            var hits = new List<VideoHit>
            {
                new("aparat", "https://www.aparat.com/v/abc123", "معرفی کالا", "https://static.example/a.jpg", 100),
                new("supplier", "https://shop.example.com/media/a.mp4", null, null, 95),
            };
            Assert.Equal(2, await service.AddAsync(productId, hits));
            // دوباره همان‌ها ← هیچ.
            Assert.Equal(0, await service.AddAsync(productId, hits));
            Assert.Equal(0, await service.AddAsync(productId, []));

            var list = await service.ListAsync(VideoSuggestionStatus.New, productId);
            Assert.Equal(2, list.Count);
            Assert.All(list, r => Assert.Equal(productId, r.ProductId));
            Assert.True(await service.PendingCountAsync() >= 2);

            var mp4 = list.Single(r => r.Source == "supplier");
            var dismissed = await service.SetStatusAsync(mp4.Id, VideoSuggestionStatus.Dismissed);
            Assert.True(dismissed.IsSuccess(), dismissed.Message);
            Assert.Single(await service.ListAsync(VideoSuggestionStatus.Dismissed, productId));

            // لینک ردشده دوباره ذخیره نمی‌شود.
            Assert.Equal(0, await service.AddAsync(productId, [hits[1]]));
            Assert.Single(await service.ListAsync(VideoSuggestionStatus.New, productId));

            // منبع غیرآپارات ← «افزودن از آپارات» رد می‌شود.
            Assert.False((await service.SetStatusAsync(mp4.Id, VideoSuggestionStatus.Used, useAsAparat: true)).IsSuccess());
            Assert.Equal(OperationResult.NotFound().Status, (await service.SetStatusAsync(Guid.NewGuid(), VideoSuggestionStatus.Used)).Status);
        }
        finally
        {
            await CleanAsync(context, productId);
        }
    }

    [SqlFact]
    public async Task Use_as_aparat_adds_an_embedded_video_and_marks_used()
    {
        var productId = await db.CreateProductAsync();
        await using var scope = db.Scope();
        var service = scope.ServiceProvider.GetRequiredService<IVideoSuggestionService>();
        var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
        try
        {
            await service.AddAsync(productId, [new VideoHit("aparat", "https://www.aparat.com/v/i7228or", "معرفی کالای آزمایشی", null, 100)]);
            var row = Assert.Single(await service.ListAsync(VideoSuggestionStatus.New, productId));

            var result = await service.SetStatusAsync(row.Id, VideoSuggestionStatus.New, useAsAparat: true);
            Assert.True(result.IsSuccess(), result.Message);

            context.ChangeTracker.Clear();
            var video = await context.ProductVideos.AsNoTracking().SingleAsync(v => v.ProductId == productId);
            Assert.Equal(ProductVideoProvider.Aparat, video.Provider);
            Assert.Equal("i7228or", video.Source);
            Assert.True(video.IsActive);
            Assert.Single(await service.ListAsync(VideoSuggestionStatus.Used, productId));
            Assert.Empty(await service.ListAsync(VideoSuggestionStatus.New, productId));
        }
        finally
        {
            await CleanAsync(context, productId);
        }
    }

    [SqlFact]
    public async Task Candidates_skip_products_with_video_pending_suggestion_or_recent_scan()
    {
        var withVideo = await db.CreateProductAsync();
        var withPending = await db.CreateProductAsync();
        var scanned = await db.CreateProductAsync();
        var fresh = await db.CreateProductAsync();
        await using var scope = db.Scope();
        var service = scope.ServiceProvider.GetRequiredService<IVideoSuggestionService>();
        var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
        try
        {
            context.ProductVideos.Add(new ProductVideo { ProductId = withVideo, Provider = ProductVideoProvider.Aparat, Source = "abc123", IsActive = true });
            await context.SaveChangesAsync();
            await service.AddAsync(withPending, [new VideoHit("aparat", "https://www.aparat.com/v/pend1", "x", null, 90)]);

            // پربازدید کردن کالاهای آزمون تا در ۵۰ نامزد اول باشند.
            await context.Products.Where(p => p.Id == withVideo || p.Id == withPending || p.Id == scanned || p.Id == fresh)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.ViewCount, 9_000_000));

            var ids = (await service.CandidatesAsync(50, [scanned])).Select(c => c.Id).ToList();
            Assert.Contains(fresh, ids);
            Assert.DoesNotContain(withVideo, ids);
            Assert.DoesNotContain(withPending, ids);
            Assert.DoesNotContain(scanned, ids);
            Assert.Empty(await service.CandidatesAsync(0, []));

            var info = await service.ProductAsync(fresh);
            Assert.NotNull(info);
            Assert.Equal(fresh, info.Value.Id);
            Assert.Null(await service.ProductAsync(Guid.NewGuid()));
        }
        finally
        {
            await context.Products.Where(p => p.Id == withVideo || p.Id == withPending || p.Id == scanned || p.Id == fresh)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.ViewCount, 0));
            foreach (var id in new[] { withVideo, withPending, scanned, fresh })
                await CleanAsync(context, id);
        }
    }
}
