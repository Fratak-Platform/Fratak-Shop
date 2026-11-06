using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Entities.Products;

namespace Fratak_Shop.Tests.Unit;

public class DeviceInfoTests
{
    [Theory]
    [InlineData("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Safari/537.36", "Chrome", "Windows", false)]
    [InlineData("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Safari/537.36 Edg/130.0.0.0", "Edge", "Windows", false)]
    [InlineData("Mozilla/5.0 (iPhone; CPU iPhone OS 17_5 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.5 Mobile/15E148 Safari/604.1", "Safari", "iOS", true)]
    [InlineData("Mozilla/5.0 (Linux; Android 14; SM-S918B) AppleWebKit/537.36 (KHTML, like Gecko) SamsungBrowser/25.0 Chrome/121.0.0.0 Mobile Safari/537.36", "Samsung Internet", "Android", true)]
    [InlineData("Mozilla/5.0 (Macintosh; Intel Mac OS X 14.5; rv:131.0) Gecko/20100101 Firefox/131.0", "Firefox", "macOS", false)]
    [InlineData("Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/129.0.0.0 Safari/537.36 OPR/114.0.0.0", "Opera", "Linux", false)]
    [InlineData("curl/8.4.0", "curl", "سیستم نامشخص", false)]
    public void Parse_DetectsBrowserAndSystem(string userAgent, string browser, string os, bool mobile)
    {
        var device = DeviceInfo.Parse(userAgent);
        Assert.Equal(browser, device.Browser);
        Assert.Equal(os, device.OperatingSystem);
        Assert.Equal(mobile, device.IsMobile);
    }

    [Fact]
    public void Parse_HandlesMissingUserAgent()
    {
        var device = DeviceInfo.Parse(null);
        Assert.Equal("مرورگر نامشخص", device.Browser);
        Assert.Equal("سیستم نامشخص", device.OperatingSystem);
    }

    [Fact]
    public void Hash_IdentifiesTheDeviceKindNotTheVersion()
    {
        var chrome130 = DeviceInfo.Parse("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Safari/537.36");
        var chrome131 = DeviceInfo.Parse("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36");
        var firefox = DeviceInfo.Parse("Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:131.0) Gecko/20100101 Firefox/131.0");

        Assert.Equal(chrome130.Hash, chrome131.Hash); // browser updates must not trigger "new device" alerts
        Assert.NotEqual(chrome130.Hash, firefox.Hash);
        Assert.Contains("Chrome", chrome130.DisplayName);
    }
}

public class VideoSourceTests
{
    [Theory]
    [InlineData("https://www.aparat.com/v/AbC123", "AbC123")]
    [InlineData("https://aparat.com/v/AbC123/some-title", "AbC123")]
    [InlineData("https://www.aparat.com/video/video/embed/videohash/AbC123/vt/frame", "AbC123")]
    [InlineData("AbC123", "AbC123")]
    [InlineData("  AbC123  ", "AbC123")]
    [InlineData("https://evil.example.com/v/AbC123", null)]
    [InlineData("http://www.aparat.com/v/AbC123", null)]
    [InlineData("javascript:alert(1)", null)]
    [InlineData("", null)]
    public void Aparat_AcceptsLinksAndIds(string input, string? expected) =>
        Assert.Equal(expected, VideoSource.Normalize(ProductVideoProvider.Aparat, input));

    [Theory]
    [InlineData("https://cdn.example.com/videos/a.mp4", true)]
    [InlineData("https://cdn.example.com/videos/a.WEBM?token=1", true)]
    [InlineData("http://cdn.example.com/videos/a.mp4", false)]
    [InlineData("https://user:pass@cdn.example.com/a.mp4", false)]
    [InlineData("https://cdn.example.com/videos/a.mov", false)]
    [InlineData("https://cdn.example.com/videos/a.mp4.exe", false)]
    [InlineData("/uploads/videos/a.mp4", false)]
    public void DirectUrl_OnlyHttpsVideoFiles(string input, bool valid) =>
        Assert.Equal(valid, VideoSource.Normalize(ProductVideoProvider.DirectUrl, input) is not null);

    [Theory]
    [InlineData("clip_01.mp4", true)]
    [InlineData("a-b.webm", true)]
    [InlineData("../secret.mp4", false)]
    [InlineData("a b.mp4", false)]
    [InlineData("clip.exe", false)]
    public void Upload_OnlySafeFileNames(string input, bool valid) =>
        Assert.Equal(valid, VideoSource.Normalize(ProductVideoProvider.Upload, input) is not null);

    [Fact]
    public void Resolve_BuildsPlayerData()
    {
        var aparat = VideoSource.Resolve(ProductVideoProvider.Aparat, "https://www.aparat.com/v/AbC123");
        Assert.NotNull(aparat);
        Assert.Equal("https://www.aparat.com/video/video/embed/videohash/AbC123/vt/frame", aparat!.EmbedUrl);
        Assert.Null(aparat.FileUrl);

        var upload = VideoSource.Resolve(ProductVideoProvider.Upload, "clip.mp4");
        Assert.Equal("/uploads/videos/clip.mp4", upload!.FileUrl);
        Assert.Equal("video/mp4", upload.MimeType);

        var direct = VideoSource.Resolve(ProductVideoProvider.DirectUrl, "https://cdn.example.com/a.webm?x=1");
        Assert.Equal("video/webm", direct!.MimeType);

        Assert.Null(VideoSource.Resolve(ProductVideoProvider.Aparat, "https://evil.example.com/x"));
    }
}
