namespace Fratak_Shop.Web.Helpers;

/// <summary>
/// میان‌بر لایهٔ Web برای <see cref="Fratak_Shop.Core.Utilities.PersianNumberWords"/> (کد اصلی به Core منتقل شد
/// تا اعلان‌ها و پیامک‌ها هم بتوانند «مبلغ به حروف» داشته باشند).
/// </summary>
public static class PersianNumberWords
{
    public static string ToWords(decimal amount) => Fratak_Shop.Core.Utilities.PersianNumberWords.ToWords(amount);
}
