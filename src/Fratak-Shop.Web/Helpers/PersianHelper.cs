using System.Globalization;

namespace Fratak_Shop.Web.Helpers;

public static class PersianHelper
{
    private static readonly CultureInfo FaCulture = new("fa-IR");

    /// <summary>
    /// ۱۲۳۴۵۶۷ → ‎۱٬۲۳۴٬۵۶۷ تومان
    /// </summary>
    public static string ToTomanPrice(this decimal price)
        => $"{price:N0} تومان";

    public static string ToTomanPriceNoUnit(this decimal price)
        => price.ToString("N0", FaCulture);

    public static string ToPersianDigits(this string input)
    {
        if (string.IsNullOrEmpty(input)) return input;
        return input
            .Replace('0', '۰').Replace('1', '۱').Replace('2', '۲').Replace('3', '۳').Replace('4', '۴')
            .Replace('5', '۵').Replace('6', '۶').Replace('7', '۷').Replace('8', '۸').Replace('9', '۹');
    }

    public static string ToPersianDigits(this int value) => value.ToString().ToPersianDigits();
    public static string ToPersianDigits(this long value) => value.ToString().ToPersianDigits();
    public static string ToPersianDigits(this decimal value) => value.ToString("N0", FaCulture).ToPersianDigits();

    public static string ToPersianDate(this DateTimeOffset date)
    {
        try
        {
            var pc = new PersianCalendar();
            return $"{pc.GetYear(date.DateTime):0000}/{pc.GetMonth(date.DateTime):00}/{pc.GetDayOfMonth(date.DateTime):00}";
        }
        catch { return date.ToString("yyyy/MM/dd"); }
    }

    public static string ToPersianDateTime(this DateTimeOffset date)
    {
        try
        {
            var pc = new PersianCalendar();
            return $"{pc.GetYear(date.DateTime):0000}/{pc.GetMonth(date.DateTime):00}/{pc.GetDayOfMonth(date.DateTime):00} {date:HH:mm}";
        }
        catch { return date.ToString("yyyy/MM/dd HH:mm"); }
    }

    public static string ToRelativePersian(this DateTimeOffset date)
    {
        var ts = DateTimeOffset.UtcNow - date;
        if (ts.TotalMinutes < 1) return "همین الان";
        if (ts.TotalMinutes < 60) return $"{(int)ts.TotalMinutes} دقیقه پیش";
        if (ts.TotalHours < 24) return $"{(int)ts.TotalHours} ساعت پیش";
        if (ts.TotalDays < 7) return $"{(int)ts.TotalDays} روز پیش";
        if (ts.TotalDays < 30) return $"{(int)(ts.TotalDays / 7)} هفته پیش";
        return date.ToPersianDate();
    }

    public static int DiscountPercent(decimal price, decimal? discountPrice)
    {
        if (discountPrice is null || discountPrice >= price || price <= 0) return 0;
        return (int)Math.Round((1 - discountPrice.Value / price) * 100);
    }

    public static string DiscountPercentText(decimal price, decimal? discountPrice)
        => $"{DiscountPercent(price, discountPrice)}٪";

    public static decimal EffectivePrice(decimal price, decimal? discountPrice, DateTimeOffset? start, DateTimeOffset? end)
    {
        if (discountPrice is null) return price;
        var now = DateTimeOffset.UtcNow;
        if (start.HasValue && now < start.Value) return price;
        if (end.HasValue && now > end.Value) return price;
        return discountPrice.Value < price ? discountPrice.Value : price;
    }
}

public static class PriceCalculatorHelper
{
    public static decimal EffectivePrice(this Fratak_Shop.Core.DTOs.Products.Query.ProductDto p)
        => PersianHelper.EffectivePrice(p.Price, p.DiscountPrice, p.DiscountStartAt, p.DiscountEndAt);

    public static int DiscountPercent(this Fratak_Shop.Core.DTOs.Products.Query.ProductDto p)
        => PersianHelper.DiscountPercent(p.Price, p.DiscountPrice);

    public static decimal EffectivePrice(this Fratak_Shop.Core.DTOs.Products.Query.ProductFilterData p)
        => p.HasActiveDiscount ? p.FinalPrice : p.Price;
}
