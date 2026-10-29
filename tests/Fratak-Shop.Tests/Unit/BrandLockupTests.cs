using Fratak_Shop.Core.DTOs.Settings;
using Fratak_Shop.Web.Infrastructure;

namespace Fratak_Shop.Tests.Unit;

/// <summary>
/// تست‌های برند قابل‌تنظیم (دور ۱۰): لوگو = آیکون بدون متن + متن HTML «فراتک» و «شاپ» رنگی که از تنظیمات می‌آید.
/// <para>پیش‌فرض‌ها، جایگزینی مقدار خالی/نامعتبر با پیش‌فرض برند، و جلوگیری از تزریق (آدرس javascript:، رنگ با CSS اضافه).</para>
/// </summary>
[Trait("Category", "Unit")]
public class BrandLockupTests
{
    [Fact]
    public void Defaults_are_the_brand_mark_and_persian_text()
    {
        var brand = BrandLockup.From(new StoreSettingDto());

        Assert.Equal(BrandIdentity.Mark, brand.IconUrl);
        Assert.Equal(BrandIdentity.MarkOnDark, brand.IconDarkUrl);
        Assert.Equal(BrandIdentity.MarkPng, brand.IconRasterUrl);
        Assert.True(brand.IsDefaultIcon);
        Assert.Equal("فراتک", brand.Primary);
        Assert.Equal("شاپ", brand.Accent);
        Assert.Equal("فراتک شاپ", brand.FullText);
        Assert.Null(brand.TextColor);
        Assert.Equal(BrandIdentity.Blue, brand.AccentColor);
    }

    [Fact]
    public void Custom_values_are_used()
    {
        var brand = BrandLockup.From(new StoreSettingDto
        {
            LogoUrl = "/uploads/media/icon.png",
            LogoDarkUrl = "/uploads/media/icon-white.png",
            BrandTextPrimary = "  فروشگاه من ",
            BrandTextAccent = "آنلاین",
            BrandTextColor = "#111",
            BrandAccentColor = "#E11D48",
        });

        Assert.Equal("/uploads/media/icon.png", brand.IconUrl);
        Assert.Equal("/uploads/media/icon-white.png", brand.IconDarkUrl);
        Assert.Equal("/uploads/media/icon.png", brand.IconRasterUrl);
        Assert.False(brand.IsDefaultIcon);
        Assert.Equal("فروشگاه من", brand.Primary);
        Assert.Equal("آنلاین", brand.Accent);
        Assert.Equal("#111", brand.TextColor);
        Assert.Equal("#E11D48", brand.AccentColor);
    }

    [Fact]
    public void Custom_icon_is_also_used_on_dark_backgrounds_when_no_dark_version_exists()
    {
        var brand = BrandLockup.From(new StoreSettingDto { LogoUrl = "/uploads/media/icon.png" });
        Assert.Equal("/uploads/media/icon.png", brand.IconDarkUrl);
    }

    [Fact]
    public void Dash_removes_the_accent_part()
    {
        var brand = BrandLockup.From(new StoreSettingDto { BrandTextPrimary = "فراتک", BrandTextAccent = "-" });
        Assert.Null(brand.Accent);
        Assert.Equal("فراتک", brand.FullText);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("//evil.example/x.svg")]
    [InlineData("   ")]
    public void Unsafe_or_empty_icon_urls_fall_back_to_the_brand(string url)
    {
        var brand = BrandLockup.From(new StoreSettingDto { LogoUrl = url, LogoDarkUrl = url });
        Assert.Equal(BrandIdentity.Mark, brand.IconUrl);
        Assert.Equal(BrandIdentity.MarkOnDark, brand.IconDarkUrl);
        Assert.True(brand.IsDefaultIcon);
    }

    [Theory]
    [InlineData("#0069FD", "#0069FD")]
    [InlineData("#abc", "#abc")]
    [InlineData(" #ABCDEF ", "#ABCDEF")]
    [InlineData("red", null)]
    [InlineData("#12345", null)]
    [InlineData("#000;background:url(x)", null)]
    [InlineData("expression(alert(1))", null)]
    [InlineData(null, null)]
    public void Only_hex_colours_are_accepted(string? value, string? expected)
        => Assert.Equal(expected, BrandLockup.Hex(value));

    [Fact]
    public void Long_text_is_clipped_to_forty_characters()
    {
        var brand = BrandLockup.From(new StoreSettingDto { BrandTextPrimary = new string('ف', 60) });
        Assert.Equal(40, brand.Primary.Length);
    }

    [Theory]
    [InlineData(44, 29)]
    [InlineData(40, 26)]
    [InlineData(30, 20)]
    [InlineData(10, 12)] // حداقل اندازهٔ قلم
    public void Text_size_follows_the_icon(int height, int fontSize)
    {
        // مرحلهٔ دوازدهم: اندازه‌ها از BrandResolver می‌آیند؛ قلم خالی = «خودکار» ≈ ۰٫۶۵ × آیکون (حداقل ۱۲).
        var config = new Fratak_Shop.Core.Services.Branding.BrandStyleConfig();
        config.Places["Auth"] = new Fratak_Shop.Core.Services.Branding.BrandPlaceStyle { IconHeight = height };
        var view = Fratak_Shop.Core.Services.Branding.BrandResolver.Resolve(Fratak_Shop.Core.Services.Branding.BrandPlace.Auth,
            new StoreSettingDto(), config, BrandIdentity.Defaults);
        Assert.Equal(fontSize, view.FontSize);
    }

    /// <summary>اعتبارسنجی تنظیمات دور ۱۰ (متد خصوصی سرویس تنظیمات، با بازتاب صدا زده می‌شود تا بدون دیتابیس تست شود).</summary>
    private static string? ValidateRound10(Action<StoreSettingCommand> change)
    {
        var command = new StoreSettingCommand();
        change(command);
        var method = typeof(Fratak_Shop.Core.Services.Settings.StoreSettingService)
            .GetMethod("ValidateRound10", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        return (string?)method.Invoke(null, [command]);
    }

    [Fact]
    public void Default_round10_settings_are_valid() => Assert.Null(ValidateRound10(_ => { }));

    public static TheoryData<string, Action<StoreSettingCommand>> InvalidSettings => new()
    {
        { "متن با تگ HTML", c => c.BrandTextPrimary = "<script>" },
        { "رنگ غیر هگز", c => c.BrandAccentColor = "blue" },
        { "آدرس آیکون javascript", c => c.PwaIconUrl = "javascript:alert(1)" },
        { "آدرس آیکون //", c => c.LogoDarkUrl = "//evil.example/icon.png" },
        { "حالت نامعتبر", c => c.SupplierRepriceMode = 9 },
        { "مجموع درصدها ۶۰", c => { c.PricingGatewayFeePercent = 20; c.PricingOtherCostPercent = 30; c.PricingTaxPercent = 10; } },
        { "کف سود بیشتر از هدف", c => { c.PricingMinMarginPercent = 40; c.PricingTargetMarginPercent = 30; } },
        { "هزینهٔ منفی", c => c.PricingPackagingCost = -1 },
    };

    [Theory]
    [MemberData(nameof(InvalidSettings))]
    public void Invalid_round10_settings_are_rejected(string name, Action<StoreSettingCommand> change)
        => Assert.True(ValidateRound10(change) is not null, name);
}
