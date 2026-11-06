using System.Text.RegularExpressions;
using Fratak_Shop.Core.Services.Seo.Autopilot;
using Fratak_Shop.Core.Services.Seo.Engine;

namespace Fratak_Shop.Core.Services.Smart;

/// <summary>
/// نگهبان متن بازنویسی‌شده با مدل زبانی محلی (Ollama روی کامپیوتر مدیر، A31).
/// <para>مدل زبانی ممکن است عدد بسازد، ادعا اضافه کند یا انگلیسی جواب دهد. متن فقط وقتی پذیرفته می‌شود که:
/// ۱) همهٔ عددهای متن اصلی (قیمت، ساعت، گیگابایت…) دقیقاً در متن تازه باشند و عدد تازه‌ای اضافه نشده باشد؛
/// ۲) طول بین ۶۰٪ تا ۱۴۰٪ متن اصلی باشد؛ ۳) دست‌کم ۷۰٪ حروف فارسی باشد؛ ۴) امتیاز طبیعی بودن (<see cref="AiPatternGuard"/>) ≥ ۷۰ باشد؛
/// ۵) پیوند، تگ HTML یا ادعای ممنوع («بهترین در ایران»، «تضمینی») نداشته باشد.</para>
/// <para>مثال: «باتری ۴۰ ساعت کار می‌کند» ← «شارژ باتری تا ۴۰ ساعت دوام دارد» پذیرفته؛ «… تا ۵۰ ساعت …» رد.</para>
/// <para>محل استفاده: SmartSources.PolishAsync.</para>
/// </summary>
public static partial class LlmGuard
{
    public static bool Accept(string original, string candidate)
    {
        if (string.IsNullOrWhiteSpace(original) || string.IsNullOrWhiteSpace(candidate))
            return false;
        var cleaned = PersianText.Clean(candidate);
        if (cleaned.Length < original.Length * 0.6 || cleaned.Length > original.Length * 1.4)
            return false;
        if (cleaned.Contains('<') || cleaned.Contains("http", StringComparison.OrdinalIgnoreCase))
            return false;
        if (PersianText.PersianRatio(cleaned) < 0.7)
            return false;
        var before = Numbers(original);
        var after = Numbers(cleaned);
        if (!before.SetEquals(after))
            return false;
        if (AiPatternGuard.Claims.Keys.Any(claim => cleaned.Contains(claim, StringComparison.Ordinal) && !original.Contains(claim, StringComparison.Ordinal)))
            return false;
        return AiPatternGuard.Analyze(cleaned).Score >= 70;
    }

    /// <summary>عددهای متن (ارقام فارسی/عربی به لاتین). مثال: «۴۰ ساعت و 5.3» ← {40، 5.3}.</summary>
    public static HashSet<string> Numbers(string text)
    {
        var latin = new string(text.Select(c => c switch
        {
            >= '۰' and <= '۹' => (char)('0' + (c - '۰')),
            >= '٠' and <= '٩' => (char)('0' + (c - '٠')),
            '٫' => '.',
            _ => c,
        }).ToArray()).Replace("٬", string.Empty).Replace(",", string.Empty);
        try
        {
            return NumberRegex().Matches(latin).Select(m => m.Value.TrimEnd('.')).ToHashSet(StringComparer.Ordinal);
        }
        catch (RegexMatchTimeoutException)
        {
            return [];
        }
    }

    [GeneratedRegex(@"\d+(?:\.\d+)?", RegexOptions.CultureInvariant, 200)]
    private static partial Regex NumberRegex();
}
