using System.Globalization;
using Fratak_Shop.Core.Services.Pricing;

namespace Fratak_Shop.Web.Infrastructure.Admin;

/// <summary>
/// برچسب و رنگ مشترک نمایش قیمت رقبا در پنل مدیریت (مرحلهٔ پانزدهم).
/// <para>استفاده: کارت «قیمت رقبا» در فرم محصول (Products/_CompetitorCard) و صفحهٔ «رصد قیمت رقبا» (Products/Competitors).
/// همان برچسب‌ها در price-advisor.js هم تکرار شده‌اند (دیالوگ پیشنهاد قیمت).</para>
/// </summary>
public static class CompetitorDisplay
{
    /// <summary>عنوان کوتاه داوری. مثال: «accessory» ← «لوازم جانبی».</summary>
    public static string VerdictTitle(string code) => code switch
    {
        OfferVerdict.Accepted => "حساب شد",
        OfferVerdict.Forced => "حساب شد (دستی)",
        OfferVerdict.Own => "فروشگاه خودمان",
        OfferVerdict.Excluded => "حذف دستی",
        OfferVerdict.Mismatch => "کالای دیگر",
        OfferVerdict.Model => "مدل دیگر",
        OfferVerdict.Accessory => "لوازم جانبی",
        OfferVerdict.Duplicate => "تکراری",
        OfferVerdict.TooLow => "خیلی ارزان (پرت)",
        OfferVerdict.TooHigh => "خیلی گران (پرت)",
        _ => code,
    };

    /// <summary>کلاس نشان داوری: حساب‌شده سبز، کنارگذاشته خاکستری/زرد.</summary>
    public static string VerdictCss(string code) => code switch
    {
        OfferVerdict.Accepted or OfferVerdict.Forced => "a-badge-green",
        OfferVerdict.TooLow or OfferVerdict.TooHigh or OfferVerdict.Model => "a-badge-yellow",
        _ => "a-badge-gray",
    };

    /// <summary>عنوان وضعیت محصول در «رصد قیمت رقبا».</summary>
    public static string StateTitle(string state) => state switch
    {
        "cheapest" => "ارزان‌ترین هستیم",
        "above" => "گران‌تر از رقیب",
        "none" => "قیمت معتبری نبود",
        _ => "بررسی نشده",
    };

    public static string StateCss(string state) => state switch
    {
        "cheapest" => "a-badge-green",
        "above" => "a-badge-red",
        "none" => "a-badge-yellow",
        _ => "a-badge-gray",
    };

    /// <summary>درصد با علامت. مثال: ۵٫۲ ← «+5.2٪»، −۳ ← «-3٪».</summary>
    public static string Percent(decimal? value)
        => value is not { } v ? "—" : (v > 0 ? "+" : "") + v.ToString("0.#", CultureInfo.InvariantCulture) + "٪";

    /// <summary>فاصلهٔ قیمت رقیب با قیمت ما به درصد (مثبت = رقیب گران‌تر است).</summary>
    public static decimal? Diff(decimal competitor, decimal ours)
        => ours > 0 ? Math.Round((competitor - ours) * 100 / ours, 1) : null;

    /// <summary>متن جایگاه در ترب. مثال: (۱، ۵) ← «اول از ۶ فروشنده».</summary>
    public static string RankText(int? rank, int sellers)
        => rank is not { } r ? "—" : r == 1 ? $"ارزان‌ترین (از {sellers + 1})" : $"{r} از {sellers + 1}";
}

/// <summary>مدل کارت «قیمت رقبا» (Products/_CompetitorCard).</summary>
/// <param name="ProductId">محصول.</param>
/// <param name="Detail">آخرین بررسی؛ null = هنوز بررسی نشده.</param>
/// <param name="CanRefresh">دکمهٔ «بررسی دوباره» نمایش داده شود (دسترسی مدیریت محصول).</param>
public sealed record CompetitorCardModel(Guid ProductId, ProductCompetitorDetail? Detail, bool CanRefresh);
