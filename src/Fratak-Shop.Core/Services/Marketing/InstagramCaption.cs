using System.Globalization;
using System.Text;

namespace Fratak_Shop.Core.Services.Marketing;

/// <summary>داده‌های واقعی یک کالا برای کپشن اینستاگرام.</summary>
/// <param name="Price">قیمت فعلی به تومان (اگر تخفیف واقعی فعال است، قیمت بعد از تخفیف).</param>
/// <param name="OldPrice">قیمت قبل از تخفیف فقط وقتی تخفیف واقعی فعال است؛ وگرنه null.</param>
/// <param name="Features">مشخصات واقعی کالا (کلید، مقدار) به ترتیب اهمیت.</param>
public sealed record InstagramProduct(string Name, string? Brand, string? Category, decimal Price, decimal? OldPrice, bool InStock,
    IReadOnlyList<(string Key, string Value)> Features, string Url);

/// <summary>
/// دور ۲۳ (مورد ۴۴): سازندهٔ کپشن اینستاگرام از دادهٔ واقعی کالا – ساخته شده ولی فعلاً در هیچ صفحه‌ای استفاده نمی‌شود (خواست کاربر).
/// <para>قانون‌ها: فقط دادهٔ واقعی (نام، برند، دسته، ۳ مشخصهٔ اول، قیمت)؛ درصد تخفیف فقط اگر قیمت قبلی واقعی داده شود؛ کالای ناموجود
/// قیمت ندارد و «به‌زودی» هم ادعا نمی‌کند؛ بدون شمارش معکوس، «فقط امروز» یا کمیابی ساختگی. حداکثر <see cref="MaxHashtags"/> هشتگ
/// و کل متن حداکثر <see cref="MaxLength"/> نویسه (سقف اینستاگرام ۲۲۰۰).</para>
/// <para>مثال: <c>InstagramCaption.Build(new InstagramProduct("هدفون سونی WH-1000XM5", "سونی", "هدفون", 12_900_000, 14_500_000, true, specs, url), "فراتک")</c>
/// ← «هدفون سونی WH-1000XM5 ✨ … ۱۱٪ تخفیف … #هدفون #سونی #فراتک».</para>
/// </summary>
public static class InstagramCaption
{
    public const int MaxLength = 2000;
    public const int MaxHashtags = 12;
    private const int MaxFeatures = 3;

    public static string Build(InstagramProduct product, string siteName)
    {
        var name = Clean(product.Name);
        var builder = new StringBuilder();
        builder.Append(name).Append('\n');
        if (!string.IsNullOrWhiteSpace(product.Brand) || !string.IsNullOrWhiteSpace(product.Category))
        {
            var line = string.Join(" | ", new[] { Clean(product.Category), Clean(product.Brand) }.Where(x => x.Length > 0));
            builder.Append(line).Append('\n');
        }
        builder.Append('\n');

        var features = product.Features
            .Where(f => !string.IsNullOrWhiteSpace(f.Key) && !string.IsNullOrWhiteSpace(f.Value) && f.Value.Length <= 60)
            .Take(MaxFeatures)
            .ToList();
        foreach (var (key, value) in features)
            builder.Append("▫️ ").Append(Clean(key)).Append(": ").Append(Clean(value)).Append('\n');
        if (features.Count > 0)
            builder.Append('\n');

        if (product.InStock && product.Price > 0)
        {
            if (product.OldPrice is { } old && old > product.Price)
            {
                var percent = (int)Math.Floor((old - product.Price) * 100m / old);
                if (percent >= 1)
                    builder.Append("💰 ").Append(Toman(product.Price)).Append(" تومان (").Append(Digits(percent)).Append("٪ تخفیف؛ قبلاً ")
                        .Append(Toman(old)).Append(")\n");
                else
                    builder.Append("💰 ").Append(Toman(product.Price)).Append(" تومان\n");
            }
            else
            {
                builder.Append("💰 ").Append(Toman(product.Price)).Append(" تومان\n");
            }
            builder.Append("🛒 خرید از سایت ").Append(Clean(siteName)).Append(" – لینک در بیو\n");
        }
        else
        {
            builder.Append("فعلاً ناموجود؛ در سایت ").Append(Clean(siteName)).Append(" دکمهٔ «خبرم کن» را بزنید.\n");
        }

        var tags = Hashtags(product, siteName);
        var tagLine = tags.Count == 0 ? string.Empty : "\n" + string.Join(' ', tags);
        var body = builder.ToString().TrimEnd();
        var room = MaxLength - tagLine.Length;
        if (body.Length > room)
            body = body[..Math.Max(0, room - 1)].TrimEnd() + "…";
        return body + tagLine;
    }

    /// <summary>
    /// هشتگ‌ها از نام دسته، برند و واژه‌های نام کالا (بدون تکرار، فاصله ← «_»). مثال: «گوشی موبایل» ← «#گوشی_موبایل».
    /// </summary>
    public static List<string> Hashtags(InstagramProduct product, string siteName)
    {
        var words = new List<string>();
        void Add(string? text)
        {
            var tag = Tag(text);
            // هشتگ خیلی بلند (بیش از ۳۰ نویسه) نه خوانا است نه جست‌وجو می‌شود.
            if (tag.Length is > 1 and <= 31 && !words.Contains(tag, StringComparer.Ordinal))
                words.Add(tag);
        }
        Add(product.Category);
        Add(product.Brand);
        foreach (var word in Clean(product.Name).Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(w => w.Length >= 3 && !w.All(char.IsDigit)))
            Add(word);
        Add(siteName);
        return words.Take(MaxHashtags).ToList();
    }

    private static string Tag(string? text)
    {
        var clean = Clean(text);
        if (clean.Length == 0)
            return string.Empty;
        var builder = new StringBuilder("#");
        foreach (var ch in clean)
        {
            if (char.IsLetterOrDigit(ch))
                builder.Append(ch);
            else if ((ch == ' ' || ch == '\u200C' || ch == '-') && builder[^1] != '_' && builder.Length > 1)
                builder.Append('_');
        }
        return builder.ToString().TrimEnd('_');
    }

    private static string Clean(string? text) =>
        string.IsNullOrWhiteSpace(text) ? string.Empty
            : string.Join(' ', System.Text.RegularExpressions.Regex.Replace(text, "<[^>]*>", " ").Replace('\n', ' ').Replace('\r', ' ').Replace('#', ' ')
                .Split(' ', StringSplitOptions.RemoveEmptyEntries));

    private static string Toman(decimal value) => Digits(Math.Round(value).ToString("N0", CultureInfo.InvariantCulture).Replace(',', '٬'));

    private static string Digits(object value)
    {
        var text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        return new string(text.Select(c => c is >= '0' and <= '9' ? (char)('۰' + (c - '0')) : c).ToArray());
    }
}
