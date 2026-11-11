using Fratak_Shop.Core.Services.Smart;

namespace Fratak_Shop.Tests.Unit;

/// <summary>
/// دور ۲۸ (#۵۱–۵۹) – موتور «امتیاز اطمینان داده». کالای کامل ← ۱۰۰؛ هر ایراد با کد ثابت، شدت درست و راه‌حل گزارش می‌شود.
/// </summary>
[Trait("Category", "Unit")]
[Trait("Area", "Round28")]
public class DataHealthRound28Tests
{
    private static ProductDataFacts Good() => new(
        Name: "پاوربانک انکر ۲۰۰۰۰ میلی آمپر",
        EnglishName: "Anker 20000mAh Power Bank",
        CategoryIsSystem: false,
        Price: 1_500_000,
        DiscountPrice: 1_350_000,
        PurchasePrice: 1_100_000,
        Stock: 4,
        DescriptionLength: 900,
        ShortLength: 80,
        ImageScores: new byte?[] { 85, 60 },
        ImageFlags: new byte?[] { 3, 2 },
        SupplierUrl: "https://supplier.example/p/1");

    private static IReadOnlyList<string> Codes(ProductDataFacts facts) => DataConfidence.Product(facts).Issues.Select(i => i.Code).ToList();

    [Fact]
    public void Complete_product_scores_full_and_is_healthy()
    {
        var result = DataConfidence.Product(Good());
        Assert.Equal(100, result.Score);
        Assert.Empty(result.Issues);
        Assert.Equal("سالم", result.Level);
    }

    [Theory]
    [InlineData("قاب", "name-short")]
    [InlineData("پاوربانک https://x.ir", "name-junk")]
    [InlineData("پاوربانک <b>ویژه</b>", "name-junk")]
    public void Bad_names_are_critical(string name, string code)
    {
        var issue = DataConfidence.Product(Good() with { Name = name }).Issues.First(i => i.Code == code);
        Assert.Equal(DataSeverity.Critical, issue.Severity);
    }

    [Fact]
    public void English_name_missing_is_warning_but_persian_english_is_critical()
    {
        Assert.Contains("english-missing", Codes(Good() with { EnglishName = null }));
        Assert.Contains("english-invalid", Codes(Good() with { EnglishName = "پاوربانک Anker" }));
        Assert.Contains("english-invalid", Codes(Good() with { EnglishName = "ab" }));
        Assert.DoesNotContain("english-missing", Codes(Good() with { EnglishName = "پاوربانک" }));
    }

    [Fact]
    public void System_category_and_missing_price_are_critical()
    {
        Assert.Contains("category", Codes(Good() with { CategoryIsSystem = true }));
        var codes = Codes(Good() with { Price = 0, DiscountPrice = null, PurchasePrice = null });
        Assert.Contains("price-missing", codes);
        Assert.DoesNotContain("loss", codes);
    }

    [Theory]
    [InlineData(1_500_000, "discount-invalid")]
    [InlineData(2_000_000, "discount-invalid")]
    [InlineData(0, "discount-invalid")]
    [InlineData(100_000, "discount-suspicious")]
    public void Unreasonable_discounts_are_reported(int discount, string code) =>
        Assert.Contains(code, Codes(Good() with { DiscountPrice = discount, PurchasePrice = null }));

    [Fact]
    public void Selling_below_purchase_price_is_a_loss()
    {
        Assert.Contains("loss", Codes(Good() with { DiscountPrice = 1_000_000 }));
        Assert.DoesNotContain("loss", Codes(Good() with { DiscountPrice = 1_100_000 }));
        Assert.Contains("price-high", Codes(Good() with { Price = 6_000_000, DiscountPrice = null }));
        Assert.DoesNotContain("loss", Codes(Good() with { PurchasePrice = null, DiscountPrice = 1 }));
    }

    [Fact]
    public void Negative_stock_is_critical_but_zero_stock_is_fine()
    {
        Assert.Contains("stock-negative", Codes(Good() with { Stock = -2 }));
        Assert.Empty(Codes(Good() with { Stock = 0 }));
    }

    [Fact]
    public void Short_texts_are_warnings()
    {
        var codes = Codes(Good() with { DescriptionLength = ProductCompleteness.ReadyDescriptionLength - 1, ShortLength = 0 });
        Assert.Contains("description", codes);
        Assert.Contains("short", codes);
        Assert.Empty(Codes(Good() with { DescriptionLength = ProductCompleteness.ReadyDescriptionLength, ShortLength = ProductCompleteness.ReadyShortLength }));
    }

    [Fact]
    public void Image_problems_follow_the_subject_flags()
    {
        Assert.Contains("image-missing", Codes(Good() with { ImageScores = Array.Empty<byte?>(), ImageFlags = Array.Empty<byte?>() }));
        Assert.Contains("image-people", Codes(Good() with { ImageFlags = new byte?[] { 1, 4 } }));
        Assert.Contains("image-watermark", Codes(Good() with { ImageFlags = new byte?[] { 9, null } }));
        Assert.Contains("image-scene", Codes(Good() with { ImageFlags = new byte?[] { 16, 2 } }));
        Assert.Contains("image-weak", Codes(Good() with { ImageScores = new byte?[] { 20, 44 } }));
        Assert.DoesNotContain("image-weak", Codes(Good() with { ImageScores = new byte?[] { 20, 45 } }));
        var unchecked_ = DataConfidence.Product(Good() with { ImageScores = new byte?[] { null }, ImageFlags = new byte?[] { null } });
        Assert.Equal(DataSeverity.Info, Assert.Single(unchecked_.Issues).Severity);
        Assert.Equal(96, unchecked_.Score);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("supplier.example/p/1")]
    [InlineData("ftp://supplier.example/p/1")]
    public void Invalid_supplier_url_is_reported(string url) => Assert.Contains("supplier-url", Codes(Good() with { SupplierUrl = url }));

    [Fact]
    public void Score_never_goes_below_zero_and_worst_issues_come_first()
    {
        var awful = new ProductDataFacts("x", "پ", true, 0, -5, null, -1, 0, 0, Array.Empty<byte?>(), Array.Empty<byte?>(), "javascript:x");
        var result = DataConfidence.Product(awful);
        Assert.Equal(0, result.Score);
        Assert.Equal("نامعتبر", result.Level);
        Assert.Equal(DataSeverity.Critical, result.Issues[0].Severity);
        Assert.True(result.Issues.Select(i => (int)i.Severity).SequenceEqual(result.Issues.Select(i => (int)i.Severity).OrderDescending()));
        Assert.All(result.Issues, i => Assert.False(string.IsNullOrWhiteSpace(i.Fix)));
    }

    [Fact]
    public void One_warning_keeps_product_healthy_two_criticals_make_it_invalid()
    {
        Assert.Equal(88, DataConfidence.Product(Good() with { EnglishName = null }).Score);
        var two = DataConfidence.Product(Good() with { CategoryIsSystem = true, Stock = -1 });
        Assert.Equal(40, two.Score);
        Assert.Equal("نامعتبر", two.Level);
        Assert.Equal("ضعیف", DataConfidence.Product(Good() with { CategoryIsSystem = true }).Level);
    }
}
