using Fratak_Shop.Core.Utilities;

namespace Fratak_Shop.Tests.Unit;

public class FuzzyTests
{
    [Theory]
    [InlineData("kitten", "sitting", 3)]
    [InlineData("abcd", "abdc", 1)] // adjacent transposition counts as one edit
    [InlineData("", "abc", 3)]
    [InlineData("abc", "", 3)]
    [InlineData("abc", "abc", 0)]
    [InlineData("هندزفری", "هدزفری", 1)]
    [InlineData("سامسونگ", "سامسونک", 1)]
    public void Distance_IsOptimalStringAlignment(string a, string b, int expected) =>
        Assert.Equal(expected, Fuzzy.Distance(a, b));

    [Fact]
    public void Distance_StopsEarlyAboveMax()
    {
        Assert.Equal(3, Fuzzy.Distance("a", "abcdef", max: 2));
        Assert.Equal(2, Fuzzy.Distance("abcdef", "ghijkl", max: 1));
    }

    [Theory]
    [InlineData(2, 0)]
    [InlineData(3, 0)]
    [InlineData(4, 1)]
    [InlineData(5, 1)]
    [InlineData(6, 2)]
    [InlineData(20, 2)]
    public void Tolerance_GrowsWithWordLength(int length, int expected) => Assert.Equal(expected, Fuzzy.Tolerance(length));
}

public class SearchTextTests
{
    [Theory]
    [InlineData("  علي   كتاب ", "علی کتاب")]
    [InlineData("آيفون ۱۵", "آیفون 15")]
    [InlineData("٣٢ گيگ", "32 گیگ")]
    [InlineData("می\u200cخواهم", "می خواهم")]
    [InlineData("خانۀ", "خانه")]
    [InlineData("a\tb\nc", "a b c")]
    [InlineData(null, "")]
    [InlineData("   ", "")]
    public void Normalize_UnifiesPersianText(string? input, string expected) => Assert.Equal(expected, SearchText.Normalize(input));

    [Fact]
    public void Normalize_CapsLength() => Assert.Equal(SearchText.MaxLength, SearchText.Normalize(new string('x', 300)).Length);

    [Fact]
    public void Terms_AreDistinctAndLimited()
    {
        var terms = SearchText.Terms("a b A c d e f g");
        Assert.Equal(new[] { "a", "b", "c", "d", "e" }, terms);
    }
}

public class SlugHelperTests
{
    [Theory]
    [InlineData("Hello World!", "hello-world")]
    [InlineData("گوشی  سامسونگ ۱۲۸", "گوشی-سامسونگ-128")]
    [InlineData("--abc--", "abc")]
    [InlineData("علي و كتاب", "علی-و-کتاب")]
    [InlineData("A/B\\C?D", "a-b-c-d")]
    [InlineData(null, "")]
    [InlineData("   ", "")]
    public void ToSlug_ProducesCleanUrls(string? input, string expected) => Assert.Equal(expected, input.ToSlug());

    [Fact]
    public void ToSlug_RespectsMaxLength() => Assert.Equal("abc", "abcdef".ToSlug(3));
}

public class XssSecurityTests
{
    [Fact]
    public void SanitizeText_RemovesScriptsButKeepsFormatting()
    {
        var clean = "<script>alert(1)</script><b>ok</b><img src=x onerror=alert(1)>".SanitizeText();
        Assert.DoesNotContain("<script", clean, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("onerror", clean, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<b>ok</b>", clean);
    }

    [Fact]
    public void ToPlainText_StripsTagsAndCollapsesWhitespace() =>
        Assert.Equal("Hello World", "<p>Hello&nbsp;<b>World</b></p>\n".ToPlainText());

    [Fact]
    public void SanitizeOrNull_ReturnsNullForBlank()
    {
        Assert.Null("  ".SanitizeOrNull());
        Assert.Null(((string?)null).SanitizeOrNull());
    }
}
