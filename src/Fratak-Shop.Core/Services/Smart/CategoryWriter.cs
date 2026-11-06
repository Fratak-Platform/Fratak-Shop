using System.Net;
using System.Text;
using Fratak_Shop.Core.Services.Seo.Engine;

namespace Fratak_Shop.Core.Services.Smart;

/// <summary>خروجی نویسندهٔ دسته: توضیح HTML، پرسش‌های رایج (JSON استاندارد سئو) و تعداد کلمه.</summary>
public sealed record CategoryText(string Html, string FaqJson, int Words);

/// <summary>
/// نویسندهٔ توضیح دسته (B19) – فقط از دادهٔ واقعی خود فروشگاه: تعداد کالا، بازهٔ قیمت، برندها، مشخصات پرتکرار و پرفروش‌ها.
/// <para>چرا؟ صفحهٔ دستهٔ بدون متن برای خریدار راهنمایی ندارد و برای گوگل «کم‌محتوا» است. این متن بالای فهرست کالاها
/// «چه چیزی را نگاه کنم؟» را کوتاه جواب می‌دهد.</para>
/// <para>ضدتکرار: جمله‌ها با <see cref="SmartText.Pick"/> و نام دسته به‌عنوان بذر انتخاب می‌شوند تا دو دسته متن یکسان نگیرند.
/// هیچ ادعای ساختگی («بهترین قیمت ایران»، «ارسال رایگان») نوشته نمی‌شود.</para>
/// <para>کمتر از ۳ کالای موجود ← null (دادهٔ کافی نیست؛ متن ضعیف نوشته نمی‌شود).</para>
/// <para>محل استفاده: SmartOrchestrator (کار CategoryComplete) و صفحهٔ «پنل ← محتوای هوشمند ← دسته‌ها».</para>
/// </summary>
public static class CategoryWriter
{
    public static CategoryText? Write(SmartCategorySubject subject)
    {
        var products = subject.TopProducts.Where(p => p.InStock && p.FinalPrice > 0).ToList();
        if (products.Count < 3)
            return null;

        var name = PersianText.Clean(subject.Name);
        var e = (string s) => WebUtility.HtmlEncode(s);
        var min = products.Min(p => p.FinalPrice);
        var max = products.Max(p => p.FinalPrice);
        var brands = products.Select(p => p.BrandName).Where(b => !string.IsNullOrWhiteSpace(b)).Select(b => b!.Trim())
            .GroupBy(b => b, StringComparer.OrdinalIgnoreCase).OrderByDescending(g => g.Count()).Select(g => g.Key).Take(5).ToList();
        var facts = products.SelectMany(p => p.Features).Select(SpecKnowledge.Read).Where(f => f is not null).Select(f => f!).ToList();
        var topics = facts.GroupBy(f => f.Topic).Where(g => g.Key != SpecTopic.Generic && g.Count() >= 2)
            .OrderByDescending(g => SpecKnowledge.Weight(g.Key)).ThenByDescending(g => g.Count()).Take(4).ToList();

        var html = new StringBuilder();
        html.Append("<p>");
        html.Append(e(SmartText.Pick(name,
            $"در این بخش {PersianText.Digits(subject.ProductCount)} مدل {name} گردآوری شده است",
            $"فهرست {name} فروشگاه {PersianText.Digits(subject.ProductCount)} مدل دارد",
            $"{PersianText.Digits(subject.ProductCount)} مدل {name} را این‌جا می‌بینید")));
        html.Append(e($"؛ قیمت مدل‌های موجود از {PersianText.Money(min)} تا {PersianText.Money(max)} تومان است."));
        if (brands.Count > 0)
            html.Append(e($" برندهای پرتکرار: {PersianText.JoinList(brands)}."));
        html.Append("</p>");

        if (topics.Count > 0)
        {
            html.Append("<h2>").Append(e(SmartText.Pick(name + "h", $"هنگام خرید {name} به چه نکاتی توجه کنیم؟", $"راهنمای سریع انتخاب {name}", $"چه چیزی در {name} مهم است؟"))).Append("</h2><ul>");
            foreach (var topic in topics)
            {
                var sample = topic.First();
                var numbers = topic.Where(f => f.Number is > 0).Select(f => f.Number!.Value).OrderBy(n => n).ToList();
                var range = numbers.Count >= 2 && numbers[0] < numbers[^1]
                    ? $" (در مدل‌های این دسته از {SpecKnowledge.Display(numbers[0])} تا {SpecKnowledge.Display(numbers[^1])})"
                    : string.Empty;
                var explain = SpecKnowledge.Explain(sample).FirstOrDefault();
                html.Append("<li><strong>").Append(e(sample.Key)).Append("</strong>").Append(e(range));
                if (!string.IsNullOrWhiteSpace(explain))
                    html.Append(": ").Append(e(explain));
                html.Append("</li>");
            }
            html.Append("</ul>");
        }

        var cheapest = products.OrderBy(p => p.FinalPrice).First();
        var popular = products.OrderByDescending(p => p.SalesCount).ThenByDescending(p => p.ReviewCount).First();
        var rated = products.Where(p => p.ReviewCount >= 3).OrderByDescending(p => p.Rating).FirstOrDefault();
        html.Append("<h2>").Append(e(SmartText.Pick(name + "p", "پیشنهاد سریع", "اگر وقت ندارید", "انتخاب‌های پرطرفدار"))).Append("</h2><ul>");
        html.Append("<li>").Append(e($"اقتصادی‌ترین گزینهٔ موجود: {cheapest.Name} ({PersianText.Money(cheapest.FinalPrice)} تومان)")).Append("</li>");
        if (popular.Id != cheapest.Id && popular.SalesCount > 0)
            html.Append("<li>").Append(e($"پرفروش‌ترین: {popular.Name}")).Append("</li>");
        if (rated is not null && rated.Id != popular.Id && rated.Id != cheapest.Id)
            html.Append("<li>").Append(e($"بیشترین رضایت خریداران: {rated.Name} (امتیاز {PersianText.Decimal1(rated.Rating)} از ۵)")).Append("</li>");
        html.Append("</ul>");

        var faq = new List<SeoFaqItem>
        {
            new($"قیمت {name} چقدر است؟", $"قیمت مدل‌های موجود در فروشگاه از {PersianText.Money(min)} تا {PersianText.Money(max)} تومان است و با تغییر بازار به‌روز می‌شود."),
        };
        if (brands.Count > 1)
            faq.Add(new($"چه برندهایی از {name} موجود است؟", $"در حال حاضر {PersianText.JoinList(brands)} بیشترین مدل را دارند."));
        if (topics.Count > 0)
            faq.Add(new($"مهم‌ترین نکته در خرید {name} چیست؟", $"به {PersianText.JoinList(topics.Select(t => t.First().Key).ToList())} دقت کنید؛ جدول مشخصات هر کالا این موارد را دارد."));

        var text = html.ToString();
        return new CategoryText(text, SeoFaq.Serialize(faq), PersianText.WordCount(PersianText.HtmlToText(text)));
    }
}
