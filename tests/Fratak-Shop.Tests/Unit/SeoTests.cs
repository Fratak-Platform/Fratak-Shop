using System.Text.Json;
using Fratak_Shop.Web.Services;
using Microsoft.AspNetCore.Http;

namespace Fratak_Shop.Tests.Unit;

public class SeoTests
{
    [Theory]
    [InlineData("/profile")]
    [InlineData("/profile/orders/FR-1")]
    [InlineData("/Checkout")]
    [InlineData("/login/code")]
    [InlineData("/track-order")]
    [InlineData("/api/search/suggest")]
    [InlineData("/uploads/returns/a.webp")]
    public void PersonalPages_AreNeverIndexed(string path) =>
        Assert.True(PrivatePaths.IsPrivate(new PathString(path)));

    [Theory]
    [InlineData("/")]
    [InlineData("/product/iphone")]
    [InlineData("/cartoon-toys")] // only whole segments count
    [InlineData("/blog/profile-of-a-gamer")]
    [InlineData("/uploads/products/a.webp")]
    public void PublicPages_StayIndexable(string path) =>
        Assert.False(PrivatePaths.IsPrivate(new PathString(path)));

    [Fact]
    public void JsonLd_CannotCloseTheScriptElement()
    {
        var json = JsonSerializer.Serialize(new { reviewBody = "</script><script>alert(1)</script> & \"x\"" }, PageMetaService.JsonLdOptions);

        Assert.DoesNotContain("<", json);
        Assert.DoesNotContain(">", json);
        Assert.DoesNotContain("&", json);
        Assert.Equal("</script><script>alert(1)</script> & \"x\"", JsonDocument.Parse(json).RootElement.GetProperty("reviewBody").GetString());
    }

    [Fact]
    public void JsonLd_KeepsPersianTextReadable()
    {
        var json = JsonSerializer.Serialize(new { name = "گوشی" }, PageMetaService.JsonLdOptions);
        Assert.Contains("گوشی", json);
    }
}
