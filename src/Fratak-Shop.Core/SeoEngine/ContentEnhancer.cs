using System.Text;
using Fratak_Shop.Core.Utilities;

namespace Fratak_Shop.Core.SeoEngine;

/// <summary>
/// «هوشمندسازی نوشته‌ها»: rewrites the product/blog copy of a content into a
/// better structured, easier to read and more useful text — built only from the
/// real data of the content (name, category, features, warranty, price, stock).
/// The result is a proposal the specialist reviews before saving, never an
/// automatic overwrite.
/// </summary>
public static class ContentEnhancer
{
    /// <summary>
    /// Short marketing description used on cards, in the meta description and at
    /// the top of the product page.
    /// </summary>
    public static string EnhanceShortDescription(SeoAuditInput input, string keyword)
    {
        var topic = string.IsNullOrWhiteSpace(keyword) ? input.Title : keyword;
        var builder = new StringBuilder();

        builder.Append($"{input.Title}");
        if (!string.IsNullOrWhiteSpace(input.CategoryName))
            builder.Append($" از دسته‌بندی {input.CategoryName}");
        builder.Append($"، یکی از گزینه‌های مطمئن برای خرید {topic} است. ");

        var highlights = input.Features
            .Where(feature => !string.IsNullOrWhiteSpace(feature.Value) && feature.Value.Length <= 60)
            .Take(3)
            .Select(feature => $"{feature.Key} {feature.Value}")
            .ToList();

        if (highlights.Count > 0)
            builder.Append($"این محصول با {string.Join("، ", highlights)} عرضه می‌شود. ");

        if (input.Price is > 0)
            builder.Append($"قیمت {input.Title} در فراتک {PriceHelper.ToToman(input.Price.Value)} است و پیش از ارسال، کنترل کیفی می‌شود. ");

        builder.Append("برای ثبت سفارش و مشاهده شرایط گارانتی، همین صفحه را ادامه دهید.");

        var result = builder.ToString();
        return result.Length > 400 ? result[..400].TrimEnd(' ', '،') + "." : result;
    }

    /// <summary>
    /// Full product description: intro, key features, expert review, warranty and
    /// shipping, FAQ. Existing content is preserved and only re-structured.
    /// </summary>
    public static string EnhanceProductDescription(SeoAuditInput input, string keyword, List<SeoFaqSuggestion> faqs)
    {
        var topic = string.IsNullOrWhiteSpace(keyword) ? input.Title : keyword;
        var builder = new StringBuilder();

        builder.Append($"<h2>معرفی {input.Title}</h2>");
        builder.Append($"<p>{EnhanceIntro(input, topic)}</p>");

        if (input.Features.Count > 0)
        {
            builder.Append("<h2>ویژگی‌های کلیدی</h2><ul>");
            foreach (var feature in input.Features.Where(f => !string.IsNullOrWhiteSpace(f.Value)).Take(10))
                builder.Append($"<li><strong>{feature.Key}:</strong> {feature.Value}</li>");
            builder.Append("</ul>");
        }

        var existing = TextAnalyzer.StripHtml(input.Content);
        builder.Append("<h2>بررسی تخصصی</h2>");
        if (!string.IsNullOrWhiteSpace(existing))
        {
            builder.Append($"<p>{SplitIntoReadableParagraphs(existing)}</p>");
        }
        else
        {
            builder.Append($"<p>{input.Title} برای کاربرانی مناسب است که به دنبال {topic} با کیفیت ساخت قابل اعتماد و قیمت منطقی هستند. ")
                .Append("در بررسی تخصصی فراتک، معیارهای کیفیت ساخت، عملکرد واقعی در استفاده روزمره، پشتیبانی رسمی و ارزش خرید نسبت به قیمت ارزیابی شده است.</p>");
        }

        if (input.Features.Count > 2)
        {
            builder.Append("<h3>نکات کاربردی هنگام استفاده</h3><ul>");
            builder.Append("<li>پیش از استفاده، دفترچه راهنمای محصول را مطالعه کنید تا از حداکثر کارایی آن بهره ببرید.</li>");
            builder.Append($"<li>برای حفظ کیفیت {input.Title}، سرویس و نگهداری دوره‌ای را جدی بگیرید.</li>");
            builder.Append("<li>در صورت مشاهده هرگونه ایراد در روزهای ابتدایی، خدمات پس از فروش فراتک را در جریان بگذارید.</li>");
            builder.Append("</ul>");
        }

        builder.Append("<h2>گارانتی، ارسال و پشتیبانی</h2>");
        builder.Append("<p>");
        if (input.Features.TryGetValue("گارانتی", out var warranty) && !string.IsNullOrWhiteSpace(warranty))
            builder.Append($"این کالا با {warranty} عرضه می‌شود. ");
        else
            builder.Append("این کالا با گارانتی معتبر شرکتی عرضه می‌شود. ");

        builder.Append("ارسال به سراسر کشور با پست پیشتاز و تیپاکس انجام می‌شود و کارشناسان فراتک تا پس از تحویل نیز در کنار شما هستند.</p>");

        if (faqs.Count > 0)
        {
            builder.Append("<h2>پرسش‌های متداول</h2>");
            foreach (var faq in faqs.Take(5))
                builder.Append($"<h3>{faq.Question}</h3><p>{faq.Answer}</p>");
        }

        return builder.ToString();
    }

    /// <summary>
    /// Blog enhancement: structure, a takeaway box and a conclusion are added
    /// around the untouched original text.
    /// </summary>
    public static string EnhanceBlogContent(SeoAuditInput input, string keyword)
    {
        var topic = string.IsNullOrWhiteSpace(keyword) ? input.Title : keyword;
        var existing = TextAnalyzer.StripHtml(input.Content);
        var builder = new StringBuilder();

        builder.Append($"<p>در این مقاله از {input.SiteName} درباره {topic} صحبت می‌کنیم؛ ")
            .Append("اصطلاحات تخصصی را ساده کرده‌ایم و در پایان، جمع‌بندی و پیشنهادهای کاربردی آورده شده است تا در کوتاه‌ترین زمان به نتیجه برسید.</p>");

        if (!string.IsNullOrWhiteSpace(existing))
        {
            var paragraphs = existing.Split([".", "؟", "!", "……"], StringSplitOptions.RemoveEmptyEntries)
                .Select(part => part.Trim())
                .Where(part => part.Length > 0)
                .ToList();

            var chunkSize = paragraphs.Count > 12 ? 3 : 2;
            for (var index = 0; index < paragraphs.Count; index += chunkSize)
            {
                var chunk = string.Join(". ", paragraphs.Skip(index).Take(chunkSize)).Trim();
                if (chunk.Length == 0)
                    continue;

                if (index > 0 && index % (chunkSize * 3) == 0)
                    builder.Append($"<h2>{topic} — بخش {PriceHelper.ToPersianDigits((index / (chunkSize * 3) + 1).ToString())}</h2>");

                builder.Append($"<p>{chunk}.</p>");
            }
        }

        builder.Append("<h2>جمع‌بندی</h2>");
        builder.Append($"<p>انتخاب درست در {topic} با شناخت نیاز واقعی، مقایسه مشخصات و توجه به پشتیبانی رسمی انجام می‌شود. ")
            .Append("اگر پس از خواندن این مقاله پرسشی دارید، کارشناسان فراتک آماده پاسخ‌گویی هستند.</p>");

        return builder.ToString();
    }

    /// <summary>
    /// Adds headings and breaks the long paragraphs of an existing body without
    /// touching its meaning (used when the specialist only wants a clean-up).
    /// </summary>
    public static string RestructureExistingContent(string? html, string topic)
    {
        var text = TextAnalyzer.StripHtml(html);
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var builder = new StringBuilder();
        var chunks = SplitSentences(text);

        for (var index = 0; index < chunks.Count; index += 3)
        {
            var chunk = string.Join(" ", chunks.Skip(index).Take(3)).Trim();
            if (chunk.Length == 0)
                continue;

            if (index == 0)
                builder.Append($"<p>{chunk}</p>");
            else if (index % 9 == 0)
                builder.Append($"<h2>{topic} — نکات تکمیلی</h2><p>{chunk}</p>");
            else
                builder.Append($"<p>{chunk}</p>");
        }

        return builder.ToString();
    }

    private static string EnhanceIntro(SeoAuditInput input, string topic)
    {
        var builder = new StringBuilder();
        builder.Append($"{input.Title} یکی از محصولات ");
        builder.Append(string.IsNullOrWhiteSpace(input.CategoryName) ? "فروشگاه فراتک" : $"دسته {input.CategoryName} در فروشگاه فراتک");
        builder.Append(" است که برای کاربرانی طراحی شده که به دنبال ");
        builder.Append($"{topic} با تعادل مناسب میان کیفیت، کارایی و قیمت هستند. ");

        if (input.Features.TryGetValue("برند", out var brand) && !string.IsNullOrWhiteSpace(brand))
            builder.Append($"این محصول از برند {brand} عرضه می‌شود. ");

        if (input.Price is > 0)
            builder.Append($"قیمت این محصول در حال حاضر {PriceHelper.ToToman(input.Price.Value)} است. ");

        builder.Append("در ادامه، مشخصات کامل، بررسی کارشناسی و شرایط گارانتی را مرور می‌کنیم.");
        return builder.ToString();
    }

    private static string SplitIntoReadableParagraphs(string text)
    {
        var sentences = SplitSentences(text);
        var builder = new StringBuilder();
        for (var index = 0; index < sentences.Count; index++)
        {
            builder.Append(sentences[index]);
            if (index < sentences.Count - 1)
                builder.Append(index % 2 == 1 ? "</p><p>" : " ");
        }

        return builder.ToString();
    }

    private static List<string> SplitSentences(string text) =>
        System.Text.RegularExpressions.Regex
            .Split(text, @"(?<=[.!?؟])\s+")
            .Select(sentence => sentence.Trim())
            .Where(sentence => sentence.Length > 1)
            .ToList();
}
