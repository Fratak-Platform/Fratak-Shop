using Fratak_Shop.Core.Utilities;

namespace Fratak_Shop.Core.SeoEngine;

/// <summary>
/// Rule engine behind «سئوی هوشمند». It audits a piece of content against the
/// on-page signals that actually matter for ranking (title, description, slug,
/// content depth, headings, images, links, keyword usage, readability, schema and
/// social tags) and returns a 0..100 score plus an actionable finding per rule.
/// The rules are intentionally explainable: every point a content loses comes with
/// a Persian instruction that tells even a non-specialist what to fix.
/// </summary>
public static class SeoScorer
{
    private const int IdealTitleMin = 30;
    private const int IdealTitleMax = 65;
    private const int IdealDescriptionMin = 110;
    private const int IdealDescriptionMax = 165;

    public static SeoAuditResult Audit(SeoAuditInput input)
    {
        var findings = new List<SeoFinding>();
        var keyword = string.IsNullOrWhiteSpace(input.FocusKeyword)
            ? SeoCopyWriter.GuessFocusKeyword(input)
            : input.FocusKeyword!.Trim();

        var content = input.Content ?? string.Empty;
        var plainContent = TextAnalyzer.StripHtml(content);
        var wordCount = TextAnalyzer.WordCount(content);
        var isProduct = input.EntityType == Data.Entities.Seo.SeoEntityType.Product;
        var minimumWords = isProduct ? 300 : 600;

        // ---------------------------------------------------------- keyword
        findings.Add(Rule(
            key: "focus-keyword",
            title: "کلمه کلیدی اصلی",
            maxPoints: 8,
            earned: string.IsNullOrWhiteSpace(input.FocusKeyword) ? 0 : 8,
            description: string.IsNullOrWhiteSpace(keyword)
                ? "هیچ کلمه کلیدی اصلی برای این محتوا تعیین نشده است."
                : $"کلمه کلیدی «{keyword}» برای این محتوا تعریف شده است.",
            suggestion: string.IsNullOrWhiteSpace(input.FocusKeyword)
                ? "کلمه کلیدی اصلی را تعیین کنید؛ تمام فیلدهای سئو حول همین عبارت ساخته می‌شود."
                : null));

        // ---------------------------------------------------------- title
        var seoTitle = input.Existing?.Title ?? input.Title;
        var titleLength = seoTitle.Length;
        var titlePoints = 0;
        if (titleLength is >= IdealTitleMin and <= IdealTitleMax) titlePoints += 6;
        else if (titleLength is > 0 and (< 20 or > 90)) titlePoints += 1;
        else if (titleLength > 0) titlePoints += 3;
        if (!string.IsNullOrWhiteSpace(keyword) && TextAnalyzer.KeywordOccurrences(seoTitle, keyword) > 0) titlePoints += 6;
        findings.Add(Rule(
            key: "title",
            title: "عنوان سئو",
            maxPoints: 12,
            earned: titlePoints,
            description: titleLength == 0
                ? "عنوان سئو تنظیم نشده است."
                : $"طول عنوان {PriceHelper.ToPersianDigits(titleLength.ToString())} کاراکتر است (محدوده مناسب {PriceHelper.ToPersianDigits($"{IdealTitleMin} تا {IdealTitleMax}")} کاراکتر).",
            suggestion: titlePoints >= 12 ? null :
                "عنوانی بین ۳۰ تا ۶۵ کاراکتر بنویسید که کلمه کلیدی اصلی در ابتدای آن آمده باشد."));

        // ---------------------------------------------------------- description
        var description = input.Existing?.MetaDescription ?? input.ShortDescription ?? string.Empty;
        var descriptionLength = TextAnalyzer.StripHtml(description).Length;
        var descriptionPoints = 0;
        if (descriptionLength is >= IdealDescriptionMin and <= IdealDescriptionMax) descriptionPoints += 6;
        else if (descriptionLength > 0) descriptionPoints += 2;
        if (!string.IsNullOrWhiteSpace(keyword) && TextAnalyzer.KeywordOccurrences(description, keyword) > 0) descriptionPoints += 6;
        findings.Add(Rule(
            key: "description",
            title: "توضیحات متا",
            maxPoints: 12,
            earned: descriptionPoints,
            description: descriptionLength == 0
                ? "توضیحات متا خالی است و گوگل متن دلخواه خود را نشان می‌دهد."
                : $"طول توضیحات متا {PriceHelper.ToPersianDigits(descriptionLength.ToString())} کاراکتر است.",
            suggestion: descriptionPoints >= 12 ? null :
                "توضیحی ۱۱۰ تا ۱۶۵ کاراکتری با کلمه کلیدی و یک دعوت به اقدام (مثلاً «همین حالا سفارش دهید») بنویسید."));

        // ---------------------------------------------------------- slug
        var slug = input.Slug ?? string.Empty;
        var slugPoints = 0;
        if (slug.Length is > 0 and <= 60) slugPoints += 4;
        if (!string.IsNullOrWhiteSpace(keyword) && SlugHelper.Generate(slug).Contains(SlugHelper.Generate(keyword), StringComparison.Ordinal)) slugPoints += 4;
        findings.Add(Rule(
            key: "slug",
            title: "نشانی صفحه (اسلاگ)",
            maxPoints: 8,
            earned: slugPoints,
            description: string.IsNullOrWhiteSpace(slug)
                ? "اسلاگ صفحه تعیین نشده است."
                : $"نشانی فعلی: /{slug}",
            suggestion: slugPoints >= 8 ? null :
                "اسلاگ کوتاه، خوانا و شامل کلمه کلیدی باشد (مثال: هدفون-بلوتوثی-x10)."));

        // ---------------------------------------------------------- content length
        var lengthPoints = wordCount >= minimumWords ? 12
            : wordCount >= minimumWords * 0.6 ? 7
            : wordCount >= minimumWords * 0.3 ? 3
            : 0;
        findings.Add(Rule(
            key: "content-length",
            title: "عمق محتوا",
            maxPoints: 12,
            earned: lengthPoints,
            description: $"محتوا {PriceHelper.ToPersianDigits(wordCount.ToString())} کلمه دارد" +
                         $"{(!string.IsNullOrEmpty(keyword) ? $" و زمان مطالعه آن حدود {PriceHelper.ToPersianDigits(TextAnalyzer.ReadingTimeMinutes(content).ToString())} دقیقه است" : string.Empty)}.",
            suggestion: lengthPoints >= 12 ? null :
                $"محتوای این {(isProduct ? "محصول" : "صفحه")} را به بیش از {PriceHelper.ToPersianDigits(minimumWords.ToString())} کلمه ارزشمند برسانید (معرفی، بررسی، کاربردها، پرسش‌های متداول)."));

        // ---------------------------------------------------------- headings
        var h2Count = TextAnalyzer.CountHeadings(content, 2);
        var h3Count = TextAnalyzer.CountHeadings(content, 3);
        var headingHasKeyword = TextAnalyzer.Headings(content)
            .Any(heading => TextAnalyzer.KeywordOccurrences(heading.Text, keyword) > 0);
        var headingPoints = (h2Count >= 2 ? 4 : h2Count == 1 ? 2 : 0) + (h3Count >= 1 ? 2 : 0) + (headingHasKeyword ? 2 : 0);
        findings.Add(Rule(
            key: "headings",
            title: "ساختار تیترها",
            maxPoints: 8,
            earned: headingPoints,
            description: h2Count + h3Count == 0
                ? "محتوا هیچ تیتر میانی (H2/H3) ندارد و برای خواننده و خزنده‌ها یکپارچه است."
                : $"محتوا شامل {PriceHelper.ToPersianDigits(h2Count.ToString())} تیتر H2 و {PriceHelper.ToPersianDigits(h3Count.ToString())} تیتر H3 است.",
            suggestion: headingPoints >= 8 ? null :
                "محتوا را با تیترهای H2 (مثل «ویژگی‌های کلیدی»، «بررسی تخصصی»، «پرسش‌های متداول») بخش‌بندی کنید و کلمه کلیدی را در یکی از تیترها بیاورید."));

        // ---------------------------------------------------------- images / alt
        var imageCount = input.ImageCount > 0 ? input.ImageCount : TextAnalyzer.ImageCount(content);
        var imagesWithoutAlt = TextAnalyzer.ImagesWithoutAlt(content);
        var altHasKeyword = !string.IsNullOrWhiteSpace(input.ImageAlt) &&
                            TextAnalyzer.KeywordOccurrences(input.ImageAlt, keyword) > 0;
        var imagePoints = 0;
        if (imageCount > 0) imagePoints += 3;
        if (imageCount >= 3) imagePoints += 2;
        if (!string.IsNullOrWhiteSpace(input.ImageAlt) && imagesWithoutAlt == 0) imagePoints += 2;
        if (altHasKeyword) imagePoints += 1;
        findings.Add(Rule(
            key: "images",
            title: "تصاویر و متن جانشین",
            maxPoints: 8,
            earned: imagePoints,
            description: imageCount == 0
                ? "این محتوا تصویری ندارد."
                : $"{PriceHelper.ToPersianDigits(imageCount.ToString())} تصویر ثبت شده است" +
                  (imagesWithoutAlt > 0 ? $" و {PriceHelper.ToPersianDigits(imagesWithoutAlt.ToString())} تصویر متن جانشین (alt) ندارد." : " و همه تصاویر متن جانشین دارند."),
            suggestion: imagePoints >= 8 ? null :
                "برای هر تصویر متن جانشین توصیفی و شامل کلمه کلیدی بنویسید و حداقل سه تصویر با کیفیت مناسب اضافه کنید."));

        // ---------------------------------------------------------- internal links
        var internalLinks = TextAnalyzer.InternalLinkCount(content);
        var linkPoints = internalLinks >= 3 ? 8 : internalLinks == 2 ? 5 : internalLinks == 1 ? 3 : 0;
        findings.Add(Rule(
            key: "internal-links",
            title: "لینک‌سازی داخلی",
            maxPoints: 8,
            earned: linkPoints,
            description: internalLinks == 0
                ? "هیچ لینک داخلی در متن وجود ندارد."
                : $"{PriceHelper.ToPersianDigits(internalLinks.ToString())} لینک داخلی در متن وجود دارد.",
            suggestion: linkPoints >= 8 ? null :
                "به محتوای مرتبط دیگر (محصولات مشابه، مقالات راهنما، دسته‌بندی) با متن لینک توصیفی لینک بدهید."));

        // ---------------------------------------------------------- external authority link
        var externalLinks = TextAnalyzer.ExternalLinkCount(content);
        findings.Add(Rule(
            key: "external-links",
            title: "منابع بیرونی معتبر",
            maxPoints: 4,
            earned: externalLinks >= 1 ? 4 : 0,
            description: externalLinks == 0
                ? "به هیچ منبع بیرونی معتبری ارجاع داده نشده است."
                : $"{PriceHelper.ToPersianDigits(externalLinks.ToString())} ارجاع بیرونی ثبت شده است.",
            suggestion: externalLinks >= 1 ? null : "در صورت امکان به یک منبع معتبر (سایت سازنده، مقاله تخصصی) لینک بدهید."));

        // ---------------------------------------------------------- keyword density
        var density = TextAnalyzer.KeywordDensity(content, keyword);
        var densityPoints = density switch
        {
            >= 0.6 and <= 2.5 => 8,
            > 2.5 and <= 4 => 4,
            > 0 and < 0.6 => 4,
            _ => 0,
        };
        findings.Add(Rule(
            key: "keyword-density",
            title: "چگالی کلمه کلیدی",
            maxPoints: 8,
            earned: string.IsNullOrWhiteSpace(keyword) ? 0 : densityPoints,
            description: string.IsNullOrWhiteSpace(keyword)
                ? "بدون کلمه کلیدی، چگالی قابل محاسبه نیست."
                : $"کلمه کلیدی {PriceHelper.ToPersianDigits(TextAnalyzer.KeywordOccurrences(content, keyword).ToString())} بار و با چگالی {PriceHelper.ToPersianDigits(density.ToString("0.0#"))}٪ در متن آمده است.",
            suggestion: densityPoints >= 8 ? null :
                "کلمه کلیدی را به‌صورت طبیعی در متن (۰٫۶ تا ۲٫۵ درصد) و در ۱۰۰ کلمه اول به کار ببرید و از تکرار بی‌رویه پرهیز کنید."));

        // ---------------------------------------------------------- first paragraph
        findings.Add(Rule(
            key: "keyword-intro",
            title: "کلمه کلیدی در مقدمه",
            maxPoints: 6,
            earned: TextAnalyzer.KeywordInFirstParagraph(content, keyword) ? 6 : 0,
            description: TextAnalyzer.KeywordInFirstParagraph(content, keyword)
                ? "کلمه کلیدی در ابتدای محتوا آمده است."
                : "کلمه کلیدی در ۱۰۰ کلمه اول محتوا دیده نمی‌شود.",
            suggestion: TextAnalyzer.KeywordInFirstParagraph(content, keyword) ? null :
                "در یک تا دو جمله ابتدایی محتوا کلمه کلیدی را به‌کار ببرید تا ارتباط صفحه مشخص باشد."));

        // ---------------------------------------------------------- readability
        var averageSentence = TextAnalyzer.AverageSentenceLength(content);
        var longestParagraph = TextAnalyzer.LongestParagraphWordCount(content);
        var readabilityPoints = 0;
        if (averageSentence is > 0 and <= 20) readabilityPoints += 5;
        else if (averageSentence is > 20 and <= 28) readabilityPoints += 3;
        if (longestParagraph > 0 && longestParagraph <= 150) readabilityPoints += 3;
        findings.Add(Rule(
            key: "readability",
            title: "خوانایی متن",
            maxPoints: 8,
            earned: readabilityPoints,
            description: averageSentence == 0
                ? "برای سنجش خوانایی، متن کافی وجود ندارد."
                : $"میانگین طول جمله {PriceHelper.ToPersianDigits(averageSentence.ToString("0.#"))} کلمه و بلندترین پاراگراف {PriceHelper.ToPersianDigits(longestParagraph.ToString())} کلمه است.",
            suggestion: readabilityPoints >= 8 ? null :
                "جمله‌ها را کوتاه‌تر (زیر ۲۰ کلمه) کنید و پاراگراف‌های بلند را به چند بخش کوتاه بشکنید."));

        // ---------------------------------------------------------- structured data
        var hasSchema = !string.IsNullOrWhiteSpace(input.Existing?.StructuredDataJsonLd) ||
                        !string.IsNullOrWhiteSpace(input.Existing?.SchemaType);
        findings.Add(Rule(
            key: "schema",
            title: "داده ساختاریافته (Schema)",
            maxPoints: 6,
            earned: hasSchema ? 6 : 0,
            description: hasSchema
                ? "داده ساختاریافته برای این محتوا ثبت شده است."
                : "داده ساختاریافته (JSON-LD) ثبت نشده است.",
            suggestion: hasSchema ? null : "با اجرای سئوی هوشمند، اسکیمای مناسب (Product/Article/FAQPage) ساخته و ثبت می‌شود."));

        // ---------------------------------------------------------- social tags
        var socialFields = new[]
        {
            input.Existing?.OgTitle, input.Existing?.OgDescription, input.Existing?.OgImageName, input.Existing?.XTitle,
        };
        var socialPoints = socialFields.Count(field => !string.IsNullOrWhiteSpace(field)) switch
        {
            4 => 6,
            3 => 4,
            2 => 2,
            _ => 0,
        };
        findings.Add(Rule(
            key: "social",
            title: "کارت شبکه‌های اجتماعی",
            maxPoints: 6,
            earned: socialPoints,
            description: socialPoints >= 6
                ? "تگ‌های Open Graph و کارت توییتر کامل هستند."
                : "تگ‌های Open Graph و کارت توییتر کامل نیستند و اشتراک‌گذاری جذاب نخواهد بود.",
            suggestion: socialPoints >= 6 ? null : "عنوان، توضیح و تصویر اشتراک‌گذاری (OG) را تنظیم کنید."));

        // ---------------------------------------------------------- canonical
        var canonical = input.Existing?.CanonicalUrl;
        findings.Add(Rule(
            key: "canonical",
            title: "نشانی کانونیکال",
            maxPoints: 4,
            earned: string.IsNullOrWhiteSpace(canonical) ? 0 : 4,
            description: string.IsNullOrWhiteSpace(canonical)
                ? "نشانی کانونیکال تنظیم نشده است."
                : $"کانونیکال: {canonical}",
            suggestion: string.IsNullOrWhiteSpace(canonical) ? null : "مقدار کانونیکال درست است."));

        // ---------------------------------------------------------- sitemap
        var inSitemap = input.Existing?.IncludeInSitemap ?? true;
        findings.Add(Rule(
            key: "sitemap",
            title: "حضور در نقشه سایت",
            maxPoints: 4,
            earned: inSitemap ? 4 : 0,
            description: inSitemap
                ? "این محتوا در نقشه سایت قرار می‌گیرد."
                : "این محتوا از نقشه سایت حذف شده است.",
            suggestion: inSitemap ? null : "اگر صفحه باید ایندکس شود، آن را به نقشه سایت برگردانید."));

        // ---------------------------------------------------------- indexing
        var noIndex = input.Existing?.NoIndex ?? false;
        findings.Add(Rule(
            key: "indexing",
            title: "وضعیت ایندکس",
            maxPoints: 4,
            earned: noIndex ? 0 : 4,
            description: noIndex
                ? "این صفحه برای موتورهای جستجو noindex شده است."
                : "این صفحه برای ایندکس شدن آزاد است.",
            suggestion: noIndex ? "اگر قصد ایندکس شدن دارید، گزینه noindex را بردارید." : null));

        var maxPoints = findings.Sum(finding => finding.MaxPoints);
        var earned = findings.Sum(finding => finding.Points);
        var score = maxPoints == 0 ? 0 : (int)Math.Round(earned * 100d / maxPoints);

        return new SeoAuditResult
        {
            Score = score,
            Grade = score switch
            {
                >= 85 => SeoAuditGrade.Excellent,
                >= 70 => SeoAuditGrade.Good,
                >= 50 => SeoAuditGrade.Fair,
                _ => SeoAuditGrade.Poor,
            },
            Findings = findings.OrderBy(finding => finding.Severity).ThenBy(finding => finding.Key).ToList(),
            Summary = BuildSummary(score, findings),
        };
    }

    private static SeoFinding Rule(string key, string title, int maxPoints, int earned, string description, string? suggestion)
    {
        earned = Math.Clamp(earned, 0, maxPoints);
        var severity = earned >= maxPoints
            ? SeoFindingSeverity.Good
            : earned == 0
                ? SeoFindingSeverity.Critical
                : SeoFindingSeverity.Warning;

        return new SeoFinding
        {
            Key = key,
            Title = title,
            Description = description,
            Suggestion = suggestion,
            MaxPoints = maxPoints,
            Points = earned,
            Severity = severity,
        };
    }

    private static string BuildSummary(int score, List<SeoFinding> findings)
    {
        var critical = findings.Count(finding => finding.Severity == SeoFindingSeverity.Critical);
        var warning = findings.Count(finding => finding.Severity == SeoFindingSeverity.Warning);

        if (critical == 0 && warning == 0)
            return "این محتوا از نظر سئوی درون‌صفحه‌ای کامل است و آماده ایندکس شدن است.";

        var parts = new List<string> { $"امتیاز سئو: {PriceHelper.ToPersianDigits(score.ToString())} از ۱۰۰" };
        if (critical > 0)
            parts.Add($"{PriceHelper.ToPersianDigits(critical.ToString())} مورد بحرانی");
        if (warning > 0)
            parts.Add($"{PriceHelper.ToPersianDigits(warning.ToString())} مورد قابل بهبود");

        return string.Join(" • ", parts);
    }
}
