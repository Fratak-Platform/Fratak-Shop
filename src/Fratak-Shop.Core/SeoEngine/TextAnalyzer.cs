using System.Text.RegularExpressions;
using Fratak_Shop.Core.Utilities;

namespace Fratak_Shop.Core.SeoEngine;

/// <summary>
/// Small, dependency-free text analysis used by the SEO engine: word/sentence
/// statistics, keyword density, heading extraction and reading time.
/// Everything is Persian aware (ZWNJ is treated as a word separator, Arabic
/// characters are normalised first).
/// </summary>
public static partial class TextAnalyzer
{
    [GeneratedRegex("<[^>]+>")]
    private static partial Regex HtmlTagRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhiteSpaceRegex();

    [GeneratedRegex(@"[.!?؟…\n]+")]
    private static partial Regex SentenceRegex();

    [GeneratedRegex(@"<h([1-6])[^>]*>(.*?)</h\1>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex HeadingRegex();

    /// <summary>Removes every HTML tag and returns clean, single-spaced text.</summary>
    public static string StripHtml(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return string.Empty;

        var text = HtmlTagRegex().Replace(html, " ");
        text = text.Replace("&nbsp;", " ").Replace("&amp;", "&").Replace("&quot;", "\"").Replace("&lt;", "<").Replace("&gt;", ">");
        return WhiteSpaceRegex().Replace(text, " ").Trim();
    }

    /// <summary>Words of a text, ZWNJ separated compounds count as one word.</summary>
    public static string[] Words(string? text) =>
        StripHtml(text)
            .Split([' ', '‌', '\u200c', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(word => word.Trim('.', ',', '،', '؛', ':', '!', '?', '؟', '»', '«', '"', '\'', ')', '(', '[', ']'))
            .Where(word => word.Length > 0)
            .ToArray();

    public static int WordCount(string? text) => Words(text).Length;

    public static string[] Sentences(string? text) =>
        SentenceRegex().Split(StripHtml(text))
            .Select(sentence => sentence.Trim())
            .Where(sentence => sentence.Length > 1)
            .ToArray();

    /// <summary>Average number of words per sentence (a readability signal).</summary>
    public static double AverageSentenceLength(string? text)
    {
        var sentences = Sentences(text);
        if (sentences.Length == 0)
            return 0;

        return Math.Round((double)Words(text).Length / sentences.Length, 1);
    }

    /// <summary>Reading time in minutes (≈ 200 Persian words per minute).</summary>
    public static int ReadingTimeMinutes(string? text)
    {
        var words = WordCount(text);
        return words == 0 ? 0 : Math.Max(1, (int)Math.Ceiling(words / 200d));
    }

    /// <summary>Number of times a keyword (or key phrase) appears, ignoring case.</summary>
    public static int KeywordOccurrences(string? text, string? keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword))
            return 0;

        var haystack = SlugHelper.NormalizePersian(StripHtml(text)).ToLowerInvariant();
        var needle = SlugHelper.NormalizePersian(keyword).Trim().ToLowerInvariant();
        if (needle.Length == 0)
            return 0;

        var count = 0;
        var index = haystack.IndexOf(needle, StringComparison.Ordinal);
        while (index >= 0)
        {
            count++;
            index = haystack.IndexOf(needle, index + needle.Length, StringComparison.Ordinal);
        }

        return count;
    }

    /// <summary>Keyword density in percent of the total word count.</summary>
    public static double KeywordDensity(string? text, string? keyword)
    {
        var total = WordCount(text);
        if (total == 0)
            return 0;

        var occurrences = KeywordOccurrences(text, keyword);
        var keywordWords = Words(keyword).Length;

        return Math.Round(occurrences * Math.Max(keywordWords, 1) * 100d / total, 2);
    }

    /// <summary>True when the keyword appears in the first N words of the text.</summary>
    public static bool KeywordInFirstParagraph(string? text, string? keyword, int firstWords = 100)
    {
        if (string.IsNullOrWhiteSpace(keyword))
            return false;

        var words = Words(text);
        var head = string.Join(' ', words.Take(firstWords));
        return KeywordOccurrences(head, keyword) > 0;
    }

    /// <summary>Headings (h1..h6) of an HTML body with their level.</summary>
    public static List<(int Level, string Text)> Headings(string? html)
    {
        var result = new List<(int, string)>();
        if (string.IsNullOrWhiteSpace(html))
            return result;

        foreach (Match match in HeadingRegex().Matches(html))
            result.Add((int.Parse(match.Groups[1].Value), StripHtml(match.Groups[2].Value)));

        return result;
    }

    public static int CountHeadings(string? html, int level) => Headings(html).Count(heading => heading.Level == level);

    public static int LinkCount(string? html) =>
        string.IsNullOrWhiteSpace(html) ? 0 : Regex.Matches(html, "<a\\s", RegexOptions.IgnoreCase).Count;

    public static int ExternalLinkCount(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return 0;

        return Regex.Matches(html, "<a\\s[^>]*href=\"https?://", RegexOptions.IgnoreCase).Count;
    }

    public static int InternalLinkCount(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return 0;

        return Regex.Matches(html, "<a\\s[^>]*href=\"/(?!/)", RegexOptions.IgnoreCase).Count;
    }

    /// <summary>Image tags without an alt attribute (an SEO and accessibility issue).</summary>
    public static int ImagesWithoutAlt(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return 0;

        var images = Regex.Matches(html, "<img\\s[^>]*>", RegexOptions.IgnoreCase);
        return images.Count(image => !image.Value.Contains("alt=", StringComparison.OrdinalIgnoreCase));
    }

    public static int ImageCount(string? html) =>
        string.IsNullOrWhiteSpace(html) ? 0 : Regex.Matches(html, "<img\\s", RegexOptions.IgnoreCase).Count;

    /// <summary>Longest paragraph in words (very long paragraphs hurt readability).</summary>
    public static int LongestParagraphWordCount(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return 0;

        var paragraphs = Regex.Matches(html, "<p[^>]*>(.*?)</p>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (paragraphs.Count == 0)
            return WordCount(html);

        return paragraphs.Select(paragraph => WordCount(paragraph.Groups[1].Value)).Max();
    }

    /// <summary>
    /// Most frequent meaningful terms of a text — the base of the automatic keyword
    /// suggestion. Persian stop words are filtered out.
    /// </summary>
    public static List<string> TopTerms(string? text, int take = 12)
    {
        var stopWords = new HashSet<string>(StringComparer.Ordinal)
        {
            "و", "در", "به", "از", "که", "این", "را", "با", "است", "برای", "آن", "یک", "خود", "تا",
            "کرد", "بر", "هم", "نیز", "وی", "اما", "یا", "شده", "باید", "هر", "دو", "بیش", "های",
            "می", "شود", "شما", "ما", "او", "آنها", "بود", "شد", "دارد", "دارای", "می‌شود", "می‌توان",
            "سال", "روز", "طور", "صورت", "بین", "روی", "دیگر", "همه", "چند", "اگر", "چون", "پس",
        };

        return Words(text)
            .Select(word => SlugHelper.NormalizePersian(word).Trim())
            .Where(word => word.Length > 2 && !stopWords.Contains(word) && !int.TryParse(SlugHelper.ToLatinDigits(word), out _))
            .GroupBy(word => word)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key)
            .Take(take)
            .Select(group => group.Key)
            .ToList();
    }
}
