using System.Net;
using Fratak_Shop.Core.DTOs.Settings;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Entities.Categories;
using Fratak_Shop.Data.Entities.Orders;
using Fratak_Shop.Data.Entities.Products;
using Fratak_Shop.Data.Entities.Searches;
using Fratak_Shop.Data.Entities.Settings;
using Fratak_Shop.Data.Entities.Shopping;
using Fratak_Shop.Data.Utilities;
using Fratak_Shop.Web.Helpers;
using Fratak_Shop.Web.Infrastructure.Media;
using Fratak_Shop.Web.Infrastructure.PageCache;
using Fratak_Shop.Web.Infrastructure.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;

namespace Fratak_Shop.Tests.Unit;

public class CacheInvalidationTests
{
    [Fact]
    public void KeysFor_MapsEntityChangesToCacheKeys()
    {
        Assert.Contains(CacheKeys.StoreSettings, CacheInvalidation.KeysFor([typeof(StoreSetting)]));
        Assert.Contains(CacheInvalidation.SitemapKey, CacheInvalidation.KeysFor([typeof(Product)]));
        Assert.Contains(CacheInvalidation.SearchVocabularyKey, CacheInvalidation.KeysFor([typeof(Product)]));
        Assert.Contains(CacheKeys.MenuCategories, CacheInvalidation.KeysFor([typeof(Category)]));
        Assert.Contains(CacheInvalidation.SearchSynonymsKey, CacheInvalidation.KeysFor([typeof(SearchSynonym)]));
        Assert.Empty(CacheInvalidation.KeysFor([typeof(Cart)]));
    }

    [Fact]
    public void PublishedChange_EvictsTheMappedEntries()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        CacheInvalidation.Wire(cache);
        cache.Set(CacheKeys.StoreSettings, "old settings");
        cache.Set("unrelated", 1);

        DataChangeNotifier.Publish(typeof(StoreSetting));

        Assert.False(cache.TryGetValue(CacheKeys.StoreSettings, out _));
        Assert.True(cache.TryGetValue("unrelated", out _));
    }

    [Fact]
    public void CatalogVersion_ChangesWithCatalogData()
    {
        var before = CacheInvalidation.CatalogVersion;
        DataChangeNotifier.Publish(typeof(Product));
        Assert.True(CacheInvalidation.CatalogVersion > before);
    }
}

public class PageCachePolicyTests
{
    [Fact]
    public void PersonalDataChanges_DoNotEvictPublicPages()
    {
        Assert.False(PageCachePolicy.AffectsPages([typeof(Cart)]));
        Assert.False(PageCachePolicy.AffectsPages([typeof(Order), typeof(SearchQueryLog)]));
        Assert.True(PageCachePolicy.AffectsPages([typeof(Product)]));
        Assert.True(PageCachePolicy.AffectsPages([typeof(Cart), typeof(StoreSetting)]));
    }

    [Theory]
    [InlineData("fratak_at=abc", true)]
    [InlineData("fratak_cart=abc", true)]
    [InlineData(".AspNetCore.Antiforgery.x1=abc", true)]
    [InlineData("_ga=GA1.1.1; theme=dark", false)]
    [InlineData(null, false)]
    public void HasSessionState_DetectsShopAndFrameworkCookies(string? cookie, bool expected)
    {
        var http = new DefaultHttpContext();
        if (cookie is not null)
            http.Request.Headers.Cookie = cookie;

        Assert.Equal(expected, PageCachePolicy.HasSessionState(http.Request));
    }

    [Fact]
    public void HasSessionState_TreatsBearerRequestsAsPersonal()
    {
        var http = new DefaultHttpContext();
        http.Request.Headers.Authorization = "Bearer abc";
        Assert.True(PageCachePolicy.HasSessionState(http.Request));
    }
}

public class MediaAndImageTests
{
    [Theory]
    [InlineData("/uploads/products/a.jpg", true)]
    [InlineData("/uploads/products/a.webp?v=3", true)]
    [InlineData("/UPLOADS/PRODUCTS/A.JPG", true)]
    [InlineData("/assets/image/heroSlider/1.webp", true)]
    [InlineData("/uploads/products/a.gif", false)]
    [InlineData("/uploads/a/../../appsettings.jpg", false)]
    [InlineData("/uploads/../appsettings.json", false)]
    [InlineData("/assets/js/main.js", false)]
    [InlineData("/other/a.jpg", false)]
    [InlineData("//evil.example.com/uploads/a.jpg", false)]
    [InlineData("https://cdn.example.com/uploads/a.jpg", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsOptimizable_OnlyLocalRasterImages(string? url, bool expected) =>
        Assert.Equal(expected, MediaEndpoints.IsOptimizable(url));

    [Theory]
    [InlineData(100, 160)]
    [InlineData(300, 320)]
    [InlineData(320, 320)]
    [InlineData(5000, 1280)]
    public void Sized_PicksTheNextAvailableWidth(int requested, int expected) =>
        Assert.Equal($"/media/{expected}/uploads/products/a.jpg", ImageHelper.Sized("/uploads/products/a.jpg", requested));

    [Fact]
    public void Sized_KeepsTheOriginalWhenNotOptimizable()
    {
        Assert.Equal("/uploads/products/a.jpg", ImageHelper.Sized("/uploads/products/a.jpg", 300, enabled: false));
        Assert.Equal("https://cdn.example.com/a.jpg", ImageHelper.Sized("https://cdn.example.com/a.jpg", 300));
        Assert.Equal(string.Empty, ImageHelper.Sized(null, 300));
    }

    [Fact]
    public void Srcset_ListsWidthsUpToTheMaximum()
    {
        Assert.Equal("/media/160/uploads/p/a.jpg 160w, /media/320/uploads/p/a.jpg 320w, /media/480/uploads/p/a.jpg 480w",
            ImageHelper.Srcset("/uploads/p/a.jpg", maxWidth: 480));
        Assert.Null(ImageHelper.Srcset("/uploads/p/a.jpg", enabled: false));
        Assert.Null(ImageHelper.Srcset("https://cdn.example.com/a.jpg"));
    }

    [Fact]
    public void ProductImage_ResolvesNamesAndPlaceholders()
    {
        Assert.Equal(ImageHelper.ProductPlaceholder, ImageHelper.Product(null));
        Assert.Equal("/uploads/products/a%20b.jpg", ImageHelper.Product("a b.jpg"));
        Assert.Equal("/custom/x.jpg", ImageHelper.Product("/custom/x.jpg"));
    }
}

public class FormatTests
{
    [Fact]
    public void Money()
    {
        Assert.Equal("1,234,567", Format.Price(1_234_567));
        Assert.Equal("5,000 تومان", Format.Toman(5_000));
        Assert.Equal("0", Format.Price(0));
    }

    [Fact]
    public void Date_IsSolarHijriInIranTime()
    {
        Assert.Equal("1405/08/01", Format.Date(new DateTimeOffset(2026, 10, 23, 12, 0, 0, TimeSpan.Zero)));
        // 22:00 UTC is already the next day in Tehran (UTC+03:30).
        Assert.Equal("1405/08/02", Format.Date(new DateTimeOffset(2026, 10, 23, 22, 0, 0, TimeSpan.Zero)));
        Assert.Equal("-", Format.Date((DateTimeOffset?)null));
    }
}

public class CaptchaServiceTests
{
    private readonly EphemeralDataProtectionProvider _protection = new();
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());

    private CaptchaService Service() => new(_protection, _cache);

    private string AnswerOf(string token) =>
        _protection.CreateProtector("Fratak.Captcha.v1").ToTimeLimitedDataProtector().Unprotect(token).Split('|')[1];

    private static HttpContext Visitor(string ip = "203.0.113.7")
    {
        var http = new DefaultHttpContext();
        http.Connection.RemoteIpAddress = IPAddress.Parse(ip);
        return http;
    }

    [Fact]
    public void Create_ReturnsAnSvgImageWithoutReadableText()
    {
        var challenge = Service().Create();
        Assert.StartsWith("data:image/svg+xml;base64,", challenge.Image);
        var svg = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(challenge.Image["data:image/svg+xml;base64,".Length..]));
        Assert.StartsWith("<svg", svg);
        Assert.DoesNotContain("<text", svg);
    }

    [Fact]
    public void Validate_AcceptsTheRightAnswerOnce()
    {
        var service = Service();
        var challenge = service.Create();
        var answer = AnswerOf(challenge.Token);

        Assert.True(service.Validate(challenge.Token, answer));
        Assert.False(service.Validate(challenge.Token, answer)); // single use
    }

    [Fact]
    public void Validate_BurnsTheTokenOnAWrongAnswer()
    {
        var service = Service();
        var challenge = service.Create();
        var answer = AnswerOf(challenge.Token);

        Assert.False(service.Validate(challenge.Token, answer == "22222" ? "33333" : "22222"));
        Assert.False(service.Validate(challenge.Token, answer));
    }

    [Fact]
    public void Validate_AcceptsPersianDigits()
    {
        var service = Service();
        var challenge = service.Create();
        var persian = string.Concat(AnswerOf(challenge.Token).Select(c => (char)('۰' + (c - '0'))));

        Assert.True(service.Validate(challenge.Token, persian));
    }

    [Theory]
    [InlineData(null, "12345")]
    [InlineData("tampered-token", "12345")]
    [InlineData("", "")]
    public void Validate_RejectsInvalidTokens(string? token, string answer) => Assert.False(Service().Validate(token, answer));

    [Fact]
    public void IsRequired_AfterTheConfiguredNumberOfFailures()
    {
        var service = Service();
        var settings = new StoreSettingDto { IsCaptchaEnabled = true, CaptchaAfterFailedAttempts = 2 };
        var visitor = Visitor();

        Assert.False(service.IsRequired(CaptchaActions.Login, visitor, settings));
        service.RecordFailure(CaptchaActions.Login, visitor);
        Assert.False(service.IsRequired(CaptchaActions.Login, visitor, settings));
        service.RecordFailure(CaptchaActions.Login, visitor);
        Assert.True(service.IsRequired(CaptchaActions.Login, visitor, settings));

        // Other actions and other visitors are counted separately.
        Assert.False(service.IsRequired(CaptchaActions.Register, visitor, settings));
        Assert.False(service.IsRequired(CaptchaActions.Login, Visitor("198.51.100.1"), settings));

        service.Reset(CaptchaActions.Login, visitor);
        Assert.False(service.IsRequired(CaptchaActions.Login, visitor, settings));
    }

    [Fact]
    public void IsRequired_HonoursTheStoreSettings()
    {
        var service = Service();
        Assert.False(service.IsRequired(CaptchaActions.Login, Visitor(), new StoreSettingDto { IsCaptchaEnabled = false, CaptchaAfterFailedAttempts = 0 }));
        Assert.True(service.IsRequired(CaptchaActions.Login, Visitor(), new StoreSettingDto { IsCaptchaEnabled = true, CaptchaAfterFailedAttempts = 0 }));
    }

    [Fact]
    public void NormalizeDigits_ConvertsAndFilters() => Assert.Equal("1234", CaptchaService.NormalizeDigits("۱۲a٣4 "));
}
