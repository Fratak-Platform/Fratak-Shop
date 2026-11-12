using System.Globalization;

namespace Fratak_Shop.Core.Services.Categories;

/// <summary>
/// آمار واقعی یک دسته (دور ۲۳ – مورد ۲۲): فقط از کالاهای فعال خود دسته و زیردسته‌های مستقیمش.
/// مثال: «هدفون»: ۱۲ کالا، ۹ موجود، ارزان‌ترین ۴۵۰٬۰۰۰، گران‌ترین ۸٬۹۰۰٬۰۰۰ تومان، برندها: سامسونگ (۴)، انکر (۳).
/// </summary>
/// <param name="Count">تعداد کالای فعال.</param>
/// <param name="InStock">تعداد کالای موجود.</param>
/// <param name="MinPrice">کمترین قیمت نهایی کالای موجود (تومان؛ بدون کالای موجود ← null).</param>
/// <param name="MaxPrice">بیشترین قیمت نهایی کالای موجود.</param>
/// <param name="Discounted">تعداد کالای موجود تخفیف‌دار.</param>
/// <param name="TopBrands">پرتکرارترین برندها (نام، تعداد) – حداکثر ۵.</param>
public sealed record CategoryFacts(int Count, int InStock, decimal? MinPrice, decimal? MaxPrice, int Discounted, IReadOnlyList<(string Name, int Count)> TopBrands)
{
    /// <summary>دستهٔ بدون کالا.</summary>
    public static readonly CategoryFacts Empty = new(0, 0, null, null, 0, []);
}

/// <summary>یک پرسش و پاسخ صفحهٔ دسته.</summary>
public sealed record CategoryQuestion(string Question, string Answer);

/// <summary>
/// پرسش‌های متداول پایین صفحهٔ دسته (دور ۲۳ – مورد ۲۲) – فقط از دادهٔ واقعی؛ عدد ساختگی یا ادعای «بهترین» ساخته نمی‌شود.
/// <para>بدون کالای موجود ← فهرست خالی (صفحهٔ کم‌محتوا متن پرکنندهٔ بی‌ارزش نمی‌گیرد).</para>
/// <para>محل استفاده: Pages/Search (صفحهٔ /category/{slug}) – بخش «پرسش‌های متداول» زیر فهرست کالاها.</para>
/// </summary>
public static class CategoryFaq
{
    /// <summary>
    /// ساخت پرسش‌ها. مثال: ("هدفون", facts) ← «قیمت هدفون از چند شروع می‌شود؟» / «از ۴۵۰٬۰۰۰ تا ۸٬۹۰۰٬۰۰۰ تومان…».
    /// </summary>
    public static List<CategoryQuestion> Build(string categoryName, CategoryFacts facts, string siteName)
    {
        var result = new List<CategoryQuestion>();
        if (string.IsNullOrWhiteSpace(categoryName) || facts.InStock <= 0)
            return result;
        var name = categoryName.Trim();
        var shop = string.IsNullOrWhiteSpace(siteName) ? "فروشگاه" : siteName.Trim();

        if (facts.MinPrice is { } min && facts.MaxPrice is { } max)
        {
            result.Add(new CategoryQuestion($"قیمت {name} از چند شروع می‌شود؟",
                min == max
                    ? $"قیمت {name} موجود در {shop} امروز {Toman(min)} است."
                    : $"قیمت {name}‌های موجود در {shop} امروز از {Toman(min)} تا {Toman(max)} است. برای دیدن ارزان‌ترین‌ها، مرتب‌سازی «ارزان‌ترین» را انتخاب کنید."));
        }

        result.Add(new CategoryQuestion($"چند مدل {name} موجود است؟",
            facts.InStock == facts.Count
                ? $"{Number(facts.InStock)} مدل {name} موجود و آمادهٔ ارسال است."
                : $"از {Number(facts.Count)} مدل {name}، {Number(facts.InStock)} مدل همین حالا موجود است. برای کالاهای ناموجود می‌توانید «خبرم کن» را بزنید."));

        if (facts.TopBrands.Count > 0)
        {
            var brands = string.Join("، ", facts.TopBrands.Take(4).Select(b => $"{b.Name} ({Number(b.Count)} مدل)"));
            result.Add(new CategoryQuestion($"{name} چه برندهایی دارد؟", $"برندهای این دسته در {shop}: {brands}."));
        }

        if (facts.Discounted > 0)
        {
            result.Add(new CategoryQuestion($"{name} تخفیف‌دار هم دارید؟",
                $"بله، {Number(facts.Discounted)} مدل {name} موجود همین حالا تخفیف دارد؛ فیلتر «فقط تخفیف‌دار» را بزنید."));
        }

        return result;
    }

    private static string Number(int value) => FaDigits(value.ToString("#,0", CultureInfo.InvariantCulture));

    private static string Toman(decimal value) => FaDigits(decimal.Round(value).ToString("#,0", CultureInfo.InvariantCulture)) + " تومان";

    private static string FaDigits(string text)
    {
        var chars = text.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (chars[i] is >= '0' and <= '9')
                chars[i] = (char)('۰' + (chars[i] - '0'));
            else if (chars[i] == ',')
                chars[i] = '٬';
        }
        return new string(chars);
    }
}
