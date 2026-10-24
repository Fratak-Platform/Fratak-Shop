using Fratak_Shop.Web.Infrastructure.ModelBinding;

namespace Fratak_Shop.Tests.Unit;

public class ModelBindingTests
{
    [Theory]
    [InlineData("35.6892", 356892, 4)]
    [InlineData("-12.5", -125, 1)]
    [InlineData("۳۵٫۶۸۹۲", 356892, 4)]
    [InlineData(" 51 ", 51, 0)]
    public void Coordinates_ParseIndependentOfCulture(string text, int unscaled, int scale)
    {
        Assert.True(InvariantDecimalModelBinder.TryParse(text, out var value));
        Assert.Equal(new decimal(Math.Abs(unscaled), 0, 0, unscaled < 0, (byte)scale), value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Empty_MeansNoLocation(string? text)
    {
        Assert.True(InvariantDecimalModelBinder.TryParse(text, out var value));
        Assert.Null(value);
    }

    [Theory]
    [InlineData("35,6892")]
    [InlineData("abc")]
    [InlineData("1e5")]
    public void Garbage_IsRejected(string text) =>
        Assert.False(InvariantDecimalModelBinder.TryParse(text, out _));
}
