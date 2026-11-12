using Fratak_Shop.Core.Utilities;

namespace Fratak_Shop.Tests.Unit;

public class GiftCardCodeTests
{
    [Fact]
    public void Generate_ReturnsGroupedCodeFromTheUnambiguousAlphabet()
    {
        var code = GiftCardCode.Generate();

        Assert.Matches("^[A-Z0-9]{4}-[A-Z0-9]{4}-[A-Z0-9]{4}-[A-Z0-9]{4}$", code);
        var normalized = GiftCardCode.Normalize(code);
        Assert.NotNull(normalized);
        Assert.Equal(GiftCardCode.Length, normalized!.Length);
        Assert.All(normalized, c => Assert.Contains(c, GiftCardCode.Alphabet));
        Assert.Equal(code, GiftCardCode.Format(normalized));
    }

    [Fact]
    public void Generate_ProducesUniqueCodes()
    {
        var codes = Enumerable.Range(0, 2000).Select(_ => GiftCardCode.Generate()).ToHashSet();
        Assert.Equal(2000, codes.Count);
    }

    [Theory]
    [InlineData("abcd-efgh jkmn_pqrs", "ABCDEFGHJKMNPQRS")]
    [InlineData("ABCDEFGHJKMNPQRS", "ABCDEFGHJKMNPQRS")]
    [InlineData("۲۳۴۵-6789-ABCD-EFGH", "23456789ABCDEFGH")]
    [InlineData("٢٣٤٥6789ABCDEFGH", "23456789ABCDEFGH")]
    [InlineData("  wxyz\u200cWXYZ-2345-6789  ", "WXYZWXYZ23456789")]
    public void Normalize_AcceptsWhatCustomersType(string input, string expected) =>
        Assert.Equal(expected, GiftCardCode.Normalize(input));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ABCD-EFGH")] // too short
    [InlineData("ABCD-EFGH-JKMN-PQRS-T")] // too long
    [InlineData("ABCD-EFGH-IJKL-MNOP")] // I, L, O are not in the alphabet (look like 1 / 0)
    [InlineData("ABCD-EFGH-JKMN-PQR0")] // zero
    [InlineData("ABCD-EFGH-JKMN-PQR!")]
    [InlineData("ABCD-EFGH-JKMN-PQRS-----------------------------")] // over 40 characters
    public void Normalize_RejectsInvalidInput(string? input) => Assert.Null(GiftCardCode.Normalize(input));

    [Fact]
    public void Hash_IsDeterministicAndCodeSpecific()
    {
        var a = GiftCardCode.Hash("ABCDEFGHJKMNPQRS");
        Assert.Equal(a, GiftCardCode.Hash("ABCDEFGHJKMNPQRS"));
        Assert.NotEqual(a, GiftCardCode.Hash("ABCDEFGHJKMNPQRT"));
        Assert.Matches("^[0-9A-F]{64}$", a);
    }

    [Fact]
    public void NewReference_HasPrefixAndLength()
    {
        var reference = GiftCardCode.NewReference();
        Assert.StartsWith("GC-", reference);
        Assert.Equal(11, reference.Length);
    }

    [Fact]
    public void Format_LeavesInvalidLengthUntouched() => Assert.Equal("ABC", GiftCardCode.Format("ABC"));
}
