using System.Globalization;

namespace Fratak_Shop.Core.Utilities;

/// <summary>
/// Money/price helpers shared by the storefront and the admin panel so a price is
/// always rendered the same way (grouped digits + Persian digit shape).
/// </summary>
public static class PriceHelper
{
    private static readonly CultureInfo Persian = CultureInfo.GetCultureInfo("fa-IR");

    /// <summary>«۱٫۸۰۰٫۰۰۰ تومان»</summary>
    public static string ToToman(this decimal value) => $"{ToPersianDigits(value.ToString("#,0", CultureInfo.InvariantCulture))} تومان";

    public static string ToToman(this decimal? value) => value.HasValue ? value.Value.ToToman() : "-";

    /// <summary>Grouped digits only: «۱٫۸۰۰٫۰۰۰»</summary>
    public static string ToPrice(this decimal value) =>
        ToPersianDigits(value.ToString("#,0", CultureInfo.InvariantCulture));

    public static string ToPrice(this decimal? value) => value.HasValue ? value.Value.ToPrice() : "-";

    public static string ToPrice(this int value) => ToPersianDigits(value.ToString("#,0", CultureInfo.InvariantCulture));

    /// <summary>Percentage of the discount between the original and the final price.</summary>
    public static int DiscountPercent(decimal price, decimal? discountPrice)
    {
        if (discountPrice is null || discountPrice <= 0 || price <= 0 || discountPrice >= price)
            return 0;

        return (int)Math.Round((price - discountPrice.Value) / price * 100);
    }

    /// <summary>
    /// Final price of a product: the discounted price when the discount window is
    /// active, otherwise the normal price.
    /// </summary>
    public static decimal FinalPrice(decimal price, decimal? discountPrice, DateTimeOffset? startAt, DateTimeOffset? endAt)
    {
        if (discountPrice is null || discountPrice <= 0 || discountPrice >= price)
            return price;

        var now = DateTimeOffset.UtcNow;
        if (startAt.HasValue && startAt.Value > now)
            return price;

        if (endAt.HasValue && endAt.Value < now)
            return price;

        return discountPrice.Value;
    }

    public static bool IsDiscountActive(decimal price, decimal? discountPrice, DateTimeOffset? startAt, DateTimeOffset? endAt) =>
        FinalPrice(price, discountPrice, startAt, endAt) < price;

    /// <summary>Latin digits → Persian digits (۰۱۲۳۴۵۶۷۸۹).</summary>
    public static string ToPersianDigits(string? input)
    {
        if (string.IsNullOrEmpty(input))
            return string.Empty;

        var builder = new System.Text.StringBuilder(input.Length);
        foreach (var character in input)
            builder.Append(character is >= '0' and <= '9' ? (char)(character - '0' + '۰') : character);

        return builder.ToString();
    }

    public static decimal ToEnglishDigits(this string? input)
    {
        var normalized = SlugHelper.ToLatinDigits(input ?? string.Empty)
            .Replace(",", string.Empty)
            .Replace("٫", string.Empty)
            .Trim();

        return decimal.TryParse(normalized, NumberStyles.Any, CultureInfo.InvariantCulture, out var value) ? value : 0;
    }
}
