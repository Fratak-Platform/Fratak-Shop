using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Entities.Orders;
using Fratak_Shop.Data.Entities.Products;

namespace Fratak_Shop.Tests.Unit;

public class BankValidatorTests
{
    private const string ValidCard = "6037991234567893";

    [Fact]
    public void DigitsOnly_ConvertsPersianDigitsAndDropsSeparators() =>
        Assert.Equal("603799123456", BankAccountValidator.DigitsOnly("۶۰۳۷-۹۹۱۲ 3456"));

    [Theory]
    [InlineData(ValidCard, true)]
    [InlineData("6037991234567894", false)] // wrong check digit
    [InlineData("1111111111111111", false)] // repeated digits
    [InlineData("0000000000000000", false)]
    [InlineData("603799123456789", false)] // 15 digits
    [InlineData("60379912345678930", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsValidCardNumber_UsesLuhn(string? digits, bool expected) =>
        Assert.Equal(expected, BankAccountValidator.IsValidCardNumber(digits));

    [Fact]
    public void Card_MaskAndFormat()
    {
        Assert.Equal("6037-99••-••••-7893", BankAccountValidator.MaskCard(ValidCard));
        Assert.Equal("6037-9912-3456-7893", BankAccountValidator.FormatCard(ValidCard));
        Assert.Equal("123", BankAccountValidator.MaskCard("123"));
    }

    [Theory]
    [InlineData("IR820540102680020817909002", true)]
    [InlineData("IR830540102680020817909002", false)] // wrong check digits
    [InlineData("IR82054010268002081790900", false)] // too short
    [InlineData("DE820540102680020817909002", false)]
    [InlineData(null, false)]
    public void Iban_IsValid(string? iban, bool expected) => Assert.Equal(expected, IbanValidator.IsValid(iban));

    [Theory]
    [InlineData("ir 82 0540 1026 8002 0817 9090 02", "IR820540102680020817909002")]
    [InlineData("۸۲۰۵۴۰۱۰۲۶۸۰۰۲۰۸۱۷۹۰۹۰۰۲", "IR820540102680020817909002")]
    [InlineData("IR-820540102680020817909002", "IR820540102680020817909002")]
    [InlineData(null, "")]
    public void Iban_NormalizeAcceptsCommonInput(string? input, string expected) => Assert.Equal(expected, IbanValidator.Normalize(input));

    [Theory]
    [InlineData("علی رضایی", true)]
    [InlineData("علی", false)] // needs first and last name
    [InlineData("  ", false)]
    public void HolderName(string name, bool expected) => Assert.Equal(expected, BankAccountValidator.IsValidHolderName(name));
}

public class PasswordHasherTests
{
    [Fact]
    public void HashAndVerify()
    {
        var hash = PasswordHasher.Hash("S3cret-رمز");

        Assert.True(PasswordHasher.Verify("S3cret-رمز", hash));
        Assert.False(PasswordHasher.Verify("s3cret-رمز", hash));
        Assert.NotEqual(hash, PasswordHasher.Hash("S3cret-رمز")); // random salt
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("no-separator")]
    [InlineData("a:b:c")]
    [InlineData("!!!:???")]
    public void Verify_RejectsMalformedHashes(string? hash) => Assert.False(PasswordHasher.Verify("password", hash));

    [Fact]
    public void Hash_OfBlankIsEmpty() => Assert.Equal("", PasswordHasher.Hash("  "));
}

public class OrderRulesTests
{
    [Theory]
    [InlineData(OrderStatus.Pending, true)]
    [InlineData(OrderStatus.Confirmed, true)]
    [InlineData(OrderStatus.Processing, false)] // preparation started → only the shop can cancel
    [InlineData(OrderStatus.Shipped, false)]
    [InlineData(OrderStatus.Delivered, false)]
    [InlineData(OrderStatus.Cancelled, false)]
    public void CustomerCanCancelOnlyBeforePreparation(OrderStatus status, bool expected) =>
        Assert.Equal(expected, OrderRules.IsCancellableByCustomer(status));

    [Theory]
    [InlineData(OrderStatus.Processing, true)]
    [InlineData(OrderStatus.Shipped, false)]
    public void ShopCanCancelUntilShipped(OrderStatus status, bool expected) =>
        Assert.Equal(expected, OrderRules.IsCancellableByShop(status));

    [Fact]
    public void IsPayable_OnlyPendingUnpaidOnlineOrdersBeforeDeadline()
    {
        var now = DateTimeOffset.UtcNow;
        Assert.True(OrderRules.IsPayable(OrderStatus.Pending, PaymentMethod.OnlineGateway, PaymentStatus.Pending, now.AddMinutes(5), now));
        Assert.True(OrderRules.IsPayable(OrderStatus.Pending, PaymentMethod.OnlineGateway, PaymentStatus.Failed, null, now));
        Assert.False(OrderRules.IsPayable(OrderStatus.Pending, PaymentMethod.OnlineGateway, PaymentStatus.Pending, now.AddMinutes(-1), now));
        Assert.False(OrderRules.IsPayable(OrderStatus.Pending, PaymentMethod.OnlineGateway, PaymentStatus.Paid, null, now));
        Assert.False(OrderRules.IsPayable(OrderStatus.Pending, PaymentMethod.CashOnDelivery, PaymentStatus.Pending, null, now));
        Assert.False(OrderRules.IsPayable(OrderStatus.Cancelled, PaymentMethod.OnlineGateway, PaymentStatus.Pending, null, now));
    }
}

public class PriceCalculatorTests
{
    private static Product Product(decimal price, decimal? discount, DateTimeOffset? start = null, DateTimeOffset? end = null) =>
        new() { Price = price, DiscountPrice = discount, DiscountStartAt = start, DiscountEndAt = end };

    [Fact]
    public void ActiveDiscount_LowersTheFinalPrice()
    {
        var product = Product(100_000, 80_000);
        Assert.True(product.HasActiveDiscount());
        Assert.Equal(80_000m, product.GetFinalPrice());
        Assert.Equal(20_000m, product.GetDiscountAmount());
    }

    [Fact]
    public void DiscountOutsideItsWindow_IsIgnored()
    {
        var now = DateTimeOffset.UtcNow;
        Assert.False(Product(100_000, 80_000, start: now.AddDays(1)).HasActiveDiscount(now));
        Assert.False(Product(100_000, 80_000, end: now.AddDays(-1)).HasActiveDiscount(now));
        Assert.True(Product(100_000, 80_000, now.AddDays(-1), now.AddDays(1)).HasActiveDiscount(now));
    }

    [Theory]
    [InlineData(80_000, 80_000)] // "discount" equal to the price
    [InlineData(80_000, 90_000)] // higher than the price
    public void DiscountNotBelowPrice_IsIgnored(int price, int discount)
    {
        var product = Product(price, discount);
        Assert.False(product.HasActiveDiscount());
        Assert.Equal((decimal)price, product.GetFinalPrice());
        Assert.Equal(0m, product.GetDiscountAmount());
    }
}
