using Fratak_Shop.Core.DTOs.Products.Query;
using Fratak_Shop.Core.Services.Learning;
using Fratak_Shop.Core.Services.Seo.Engine;

namespace Fratak_Shop.Core.Services.Assistant;

// ═══════════════════════════════════════════════════════════════════════════
//  دور ۲۵ – بخش‌های خالص (بدون پایگاه داده) دستیار حرفه‌ای:
//   • AssistantVariety  (لایهٔ ۵): چند جمله‌بندی برای یک پاسخ تا دستیار «رباتی» و تکراری به نظر نرسد.
//                        انتخاب قطعی است (از روی متن پیام)، پس یک پیام همیشه همان جواب را می‌گیرد و تست‌پذیر است.
//   • AssistantAdvisor  (لایهٔ ۳): امتیازدهی و دلیل پیشنهاد کالاها برای «مشاور خرید».
//   • AssistantReviews  (لایهٔ ۴): خلاصهٔ نظر خریداران («از ۱۲ نظر، ۹ مثبت…») با جنبه‌ها (کیفیت، باتری، صدا…).
//  تست: Unit/AssistantRound25Tests.cs. محل استفاده: AssistantService.
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>انتخاب قطعی یک جمله‌بندی از چند جمله‌بندی هم‌معنی.</summary>
public static class AssistantVariety
{
    /// <summary>
    /// یکی از <paramref name="options"/> بر اساس متن <paramref name="seed"/> (همیشه برای یک متن یکی است).
    /// مثال: Pick(«هدفون زیر ۲ میلیون»، «این‌ها را پیشنهاد می‌کنم:»، «گزینه‌های خوب:») ← همیشه همان یکی.
    /// </summary>
    public static string Pick(string? seed, params string[] options)
    {
        if (options.Length == 0)
            return string.Empty;
        return options[(int)(Hash(seed) % (uint)options.Length)];
    }

    /// <summary>درهم‌سازی پایدار FNV-1a (string.GetHashCode در هر اجرا فرق می‌کند و به درد «قطعی» نمی‌خورد).</summary>
    public static uint Hash(string? value)
    {
        var hash = 2166136261u;
        foreach (var ch in value ?? string.Empty)
        {
            hash ^= ch;
            hash *= 16777619u;
        }
        return hash;
    }
}

/// <summary>
/// یک پیشنهاد مشاور خرید: کالا، امتیاز، برچسب کوتاه روی کارت و دلیل‌ها/نکته‌ها.
/// مثال: (هدفون X، ۸٫۲، «بیشترین رضایت»، [«امتیاز ۴٫۶ از ۵ (۳۱ نظر)»، «۲۰٪ تخفیف»]، [«فقط ۲ عدد مانده»]).
/// </summary>
public sealed record AdvisorPick(ProductFilterData Product, double Score, string? Badge, IReadOnlyList<string> Pros, IReadOnlyList<string> Cons);

/// <summary>امتیازدهی و توضیح پیشنهادهای مشاور خرید.</summary>
public static class AssistantAdvisor
{
    /// <summary>بیشترین پیشنهاد در یک پاسخ.</summary>
    public const int Top = 3;

    /// <summary>
    /// امتیاز یک کالا برای پیشنهاد (هرچه بیشتر بهتر). اجزا:
    /// موجود بودن (۳)، رضایت خریداران (امتیاز/۵ × لگاریتم تعداد نظر)، فروش (لگاریتم تعداد فروش × ۰٫۶)، تخفیف (درصد/۲۰)،
    /// تناسب با کاربرد (هر واژهٔ نشانه در نام کالا ۱٫۵ تا سقف ۳).
    /// مثال: کالای موجود با ۴٫۵ ستاره از ۳۰ نظر و ۵۰ فروش ← حدود ۳ + ۳٫۱ + ۲٫۴ = ۸٫۵.
    /// </summary>
    public static double Score(ProductFilterData product, AssistantPurpose? purpose)
    {
        var score = product.IsInStock ? 3.0 : 0.0;
        if (product.ReviewCount > 0)
            score += product.AverageRating / 5.0 * Math.Log(1 + product.ReviewCount) * 1.0;
        score += Math.Log(1 + Math.Max(0, product.SalesCount)) * 0.6;
        score += Math.Max(0, product.DiscountPercent) / 20.0;
        score += Math.Min(3, PurposeHits(product, purpose) * 1.5);
        return Math.Round(score, 3);
    }

    /// <summary>چند واژهٔ نشانهٔ کاربرد در نام/دستهٔ کالا هست. مثال: «هدفون گیمینگ RGB» با کاربرد بازی ← ۲.</summary>
    public static int PurposeHits(ProductFilterData product, AssistantPurpose? purpose)
    {
        if (purpose is null)
            return 0;
        var text = " " + PersianText.Normalize(product.Name + " " + product.CategoryName) + " ";
        return purpose.Hints.Count(h => text.Contains(PersianText.Normalize(h), StringComparison.Ordinal));
    }

    /// <summary>
    /// انتخاب بهترین‌ها. <paramref name="keepOrder"/> = مشتری مرتب‌سازی خواسته (ارزان‌ترین، …) ← ترتیب پایگاه داده حفظ می‌شود (فقط موجودها اول).
    /// برچسب کارت: «بیشترین رضایت»، «پرفروش»، «به‌صرفه‌ترین»، «بیشترین تخفیف»، «مناسب {کاربرد}».
    /// </summary>
    public static IReadOnlyList<AdvisorPick> Pick(IReadOnlyList<ProductFilterData> candidates, AssistantPurpose? purpose, bool keepOrder, decimal? budgetMax = null)
    {
        if (candidates.Count == 0)
            return [];
        var ordered = keepOrder
            ? candidates.Select((p, i) => (Product: p, Score: Score(p, purpose), Index: i)).OrderByDescending(x => x.Product.IsInStock).ThenBy(x => x.Index).ToList()
            : candidates.Select((p, i) => (Product: p, Score: Score(p, purpose), Index: i)).OrderByDescending(x => x.Score).ThenBy(x => x.Product.FinalPrice).ToList();
        var top = ordered.Take(Top).ToList();
        var bestRated = top.Where(x => x.Product.ReviewCount >= 2).OrderByDescending(x => x.Product.AverageRating).ThenByDescending(x => x.Product.ReviewCount).Select(x => x.Product.Id).FirstOrDefault();
        var bestSeller = top.Where(x => x.Product.SalesCount > 0).OrderByDescending(x => x.Product.SalesCount).Select(x => x.Product.Id).FirstOrDefault();
        var cheapest = top.Count > 1 ? top.Where(x => x.Product.IsInStock).OrderBy(x => x.Product.FinalPrice).Select(x => x.Product.Id).FirstOrDefault() : Guid.Empty;
        var deal = top.Where(x => x.Product.DiscountPercent >= 10).OrderByDescending(x => x.Product.DiscountPercent).Select(x => x.Product.Id).FirstOrDefault();

        var picks = new List<AdvisorPick>();
        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (product, score, _) in top)
        {
            string? badge = null;
            void Badge(string text)
            {
                if (badge is null && used.Add(text))
                    badge = text;
            }
            if (PurposeHits(product, purpose) > 0 && purpose is not null)
                Badge("مناسب " + purpose.Title);
            if (product.Id == bestRated && bestRated != Guid.Empty)
                Badge("بیشترین رضایت");
            if (product.Id == bestSeller && bestSeller != Guid.Empty)
                Badge("پرفروش");
            if (product.Id == deal && deal != Guid.Empty)
                Badge("بیشترین تخفیف");
            if (product.Id == cheapest && cheapest != Guid.Empty)
                Badge("به‌صرفه‌ترین");
            picks.Add(new AdvisorPick(product, score, badge, Pros(product, purpose), Cons(product, budgetMax)));
        }
        return picks;
    }

    /// <summary>نکته‌های مثبت از دادهٔ واقعی. مثال: [«امتیاز ۴٫۵ از ۵ (۱۲ نظر)»، «۱۵٪ تخفیف»، «پرفروش (۴۰ فروش)»].</summary>
    public static IReadOnlyList<string> Pros(ProductFilterData product, AssistantPurpose? purpose)
    {
        var pros = new List<string>();
        if (product.ReviewCount > 0 && product.AverageRating >= 3.5)
            pros.Add($"امتیاز {PersianText.Decimal1(product.AverageRating)} از ۵ ({PersianText.Digits(product.ReviewCount)} نظر)");
        if (product.HasActiveDiscount && product.DiscountPercent > 0)
            pros.Add($"{PersianText.Digits(product.DiscountPercent)}٪ تخفیف");
        if (product.SalesCount >= 5)
            pros.Add($"پرفروش ({PersianText.Digits(product.SalesCount)} فروش)");
        if (purpose is not null && PurposeHits(product, purpose) > 0)
            pros.Add("مناسب " + purpose.Title);
        if (!string.IsNullOrWhiteSpace(product.BrandName))
            pros.Add("برند " + product.BrandName);
        return pros.Take(3).ToList();
    }

    /// <summary>نکته‌های منفی صادقانه. مثال: [«فقط ۲ عدد مانده»]، [«هنوز نظری ثبت نشده»]، [«کمی بالاتر از بودجه»].</summary>
    public static IReadOnlyList<string> Cons(ProductFilterData product, decimal? budgetMax)
    {
        var cons = new List<string>();
        if (!product.IsInStock)
            cons.Add("فعلاً ناموجود");
        else if (product.StockQuantity is > 0 and <= 3)
            cons.Add($"فقط {PersianText.Digits(product.StockQuantity)} عدد مانده");
        if (product.ReviewCount == 0)
            cons.Add("هنوز نظری ثبت نشده");
        else if (product.AverageRating < 3)
            cons.Add($"امتیاز پایین ({PersianText.Decimal1(product.AverageRating)} از ۵)");
        if (budgetMax is { } max && product.FinalPrice > max)
            cons.Add("بالاتر از بودجهٔ شما");
        return cons.Take(2).ToList();
    }

    /// <summary>
    /// یک سطر توضیح برای هر پیشنهاد. مثال: «۱. هدفون X – ۱٬۲۰۰٬۰۰۰ تومان | ✓ امتیاز ۴٫۵ از ۵ (۱۲ نظر)، ۱۵٪ تخفیف | ✗ فقط ۲ عدد مانده».
    /// </summary>
    public static string Line(int index, AdvisorPick pick)
    {
        var line = $"{PersianText.Digits(index)}. {pick.Product.Name} – {PersianText.Money(pick.Product.FinalPrice)} تومان";
        if (pick.Pros.Count > 0)
            line += " | ✓ " + string.Join("، ", pick.Pros);
        if (pick.Cons.Count > 0)
            line += " | ✗ " + string.Join("، ", pick.Cons);
        return line;
    }

    /// <summary>
    /// دکمه‌های بازهٔ قیمت از روی قیمت نامزدها (سه بازهٔ گرد شده). مثال: قیمت‌های ۴۰۰ هزار تا ۳ میلیون ← «زیر ۱ میلیون»، «۱ تا ۲ میلیون»، «بالای ۲ میلیون».
    /// </summary>
    public static IReadOnlyList<(decimal? Min, decimal? Max)> Bands(IEnumerable<decimal> prices)
    {
        var sorted = prices.Where(p => p > 0).OrderBy(p => p).ToList();
        if (sorted.Count < 3 || sorted[0] == sorted[^1])
            return [];
        var low = Round(sorted[sorted.Count / 3]);
        var high = Round(sorted[sorted.Count * 2 / 3]);
        if (low <= 0 || high <= low)
            return [];
        return [(null, low), (low, high), (high, null)];
    }

    /// <summary>گرد کردن مبلغ به عدد «خوانا»: زیر ۱ میلیون ← نزدیک‌ترین ۱۰۰ هزار؛ بالاتر ← نزدیک‌ترین ۵۰۰ هزار.</summary>
    public static decimal Round(decimal value)
    {
        var step = value < 1_000_000 ? 100_000m : 500_000m;
        return Math.Max(step, Math.Round(value / step, MidpointRounding.AwayFromZero) * step);
    }
}

/// <summary>یک دیدگاه برای خلاصه. مثال: («صداش عالیه ولی باتریش زود تموم میشه»، ۴، true).</summary>
public sealed record ReviewInput(string Text, byte? Rating, bool? Recommended);

/// <summary>یک جنبه در خلاصهٔ نظرها. مثال: («battery»، «باتری»، ۲ مثبت، ۵ منفی).</summary>
public sealed record ReviewAspect(string Key, string Title, int Positive, int Negative);

/// <summary>خلاصهٔ نظر خریداران یک کالا.</summary>
public sealed record ReviewSummary(int Count, int Positive, int Negative, double? AverageRating, IReadOnlyList<ReviewAspect> Aspects, string? GoodQuote, string? BadQuote);

/// <summary>خلاصهٔ نظر خریداران (کاملاً داخلی؛ بر پایهٔ <see cref="ReviewSentiment"/>).</summary>
public static partial class AssistantReviews
{
    /// <summary>جنبه‌ها و واژه‌های نشانه. کلید با موضوع پرسش دستیار یکی است (sound، screen، camera، battery، …).</summary>
    public static readonly (string Key, string Title, string[] Words)[] Aspects =
    [
        ("quality", "کیفیت ساخت", ["کیفیت", "کیفیتش", "جنس", "جنسش", "بدنه", "ساخت", "محکم", "شکسته", "خراب", "معیوب"]),
        ("sound", "صدا", ["صدا", "صداش", "صدای", "باس", "میکروفون", "بلندگو", "نویز"]),
        ("battery", "باتری و شارژ", ["باتری", "باتریش", "شارژ", "شارژش", "شارژدهی"]),
        ("screen", "نمایشگر", ["صفحه", "نمایشگر", "رزولوشن", "وضوح", "نور صفحه"]),
        ("camera", "دوربین", ["دوربین", "دوربینش", "عکس", "عکسش", "فیلم"]),
        ("price", "قیمت و ارزش خرید", ["قیمت", "قیمتش", "ارزش", "ارزون", "ارزان", "گرون", "گران", "به صرفه", "بصرفه"]),
        ("shipping", "ارسال و بسته‌بندی", ["ارسال", "بسته بندی", "بسته", "رسید", "پست", "دیر", "تاخیر", "سالم رسید"]),
        ("comfort", "راحتی", ["راحت", "راحتی", "خوش دست", "سبک", "سنگین", "گوش", "اذیت"]),
        ("durability", "دوام", ["دوام", "عمر", "زود خراب", "خراب شد", "هنوز سالم"]),
        ("original", "اصالت", ["اصل", "اصله", "اورجینال", "تقلبی", "فیک", "کپی"]),
    ];

    /// <summary>
    /// خلاصهٔ نظرها. هر دیدگاه به بندها تقسیم می‌شود («ولی/اما/، /.»)، احساس کل دیدگاه با ستاره و احساس هر بند جدا حساب می‌شود
    /// و هر بند به جنبه‌هایی که واژه‌اش را دارد امتیاز مثبت/منفی می‌دهد. مثال: «صداش عالیه ولی باتریش زود تموم میشه» ← صدا +۱، باتری ‎−۱.
    /// </summary>
    public static ReviewSummary Summarize(IReadOnlyList<ReviewInput> reviews)
    {
        var positive = 0;
        var negative = 0;
        var aspectPos = new Dictionary<string, int>(StringComparer.Ordinal);
        var aspectNeg = new Dictionary<string, int>(StringComparer.Ordinal);
        string? good = null, bad = null;
        var ratings = reviews.Where(r => r.Rating is >= 1 and <= 5).Select(r => (double)r.Rating!.Value).ToList();
        foreach (var review in reviews)
        {
            var overall = ReviewSentiment.Analyze(review.Text, review.Rating, review.Recommended);
            if (overall.Label == SentimentLabel.Positive)
                positive++;
            else if (overall.Label == SentimentLabel.Negative)
                negative++;
            var clauses = Clauses(review.Text);
            foreach (var clause in clauses)
            {
                var padded = " " + PersianText.Normalize(clause) + " ";
                var label = ReviewSentiment.Analyze(clause).Label;
                // بند بی‌واژهٔ احساسی در دیدگاه تک‌بندی ← همان احساس کل دیدگاه (ستاره).
                if (label == SentimentLabel.Neutral && clauses.Count == 1)
                    label = overall.Label;
                if (label == SentimentLabel.Neutral)
                    continue;
                foreach (var (key, _, words) in Aspects)
                {
                    if (!words.Any(w => Mentions(padded, w)))
                        continue;
                    var target = label == SentimentLabel.Positive ? aspectPos : aspectNeg;
                    target[key] = target.GetValueOrDefault(key) + 1;
                }
                var quote = clause.Trim();
                if (quote.Length is >= 12 and <= 140)
                {
                    if (label == SentimentLabel.Positive && (good is null || quote.Length > good.Length && good.Length < 40))
                        good = quote;
                    else if (label == SentimentLabel.Negative && (bad is null || quote.Length > bad.Length && bad.Length < 40))
                        bad = quote;
                }
            }
        }
        var aspects = Aspects
            .Select(a => new ReviewAspect(a.Key, a.Title, aspectPos.GetValueOrDefault(a.Key), aspectNeg.GetValueOrDefault(a.Key)))
            .Where(a => a.Positive + a.Negative > 0)
            .OrderByDescending(a => a.Positive + a.Negative)
            .ToList();
        return new ReviewSummary(reviews.Count, positive, negative, ratings.Count == 0 ? null : Math.Round(ratings.Average(), 1), aspects, good, bad);
    }

    /// <summary>
    /// متن خلاصه برای دستیار. <paramref name="focus"/> = کلید جنبهٔ پرسیده‌شده (مثلاً sound) یا null.
    /// مثال: «از ۱۲ نظر خریداران «X»: ۹ مثبت، ۲ منفی (میانگین ۴٫۳ از ۵).\nنقاط قوت: صدا (۵)، کیفیت ساخت (۳)\nنقطهٔ ضعف پرتکرار: باتری و شارژ (۲)\nیکی از خریداران: «…»».
    /// بدون دیدگاه ← null.
    /// </summary>
    public static string? Text(ReviewSummary summary, string productName, string? focus = null)
    {
        if (summary.Count == 0)
            return null;
        var lines = new List<string>();
        var head = $"از {PersianText.Digits(summary.Count)} نظر خریداران «{productName}»: {PersianText.Digits(summary.Positive)} مثبت، {PersianText.Digits(summary.Negative)} منفی";
        if (summary.AverageRating is { } average)
            head += $" (میانگین {PersianText.Decimal1(average)} از ۵)";
        lines.Add(head + ".");
        if (focus is not null && summary.Aspects.FirstOrDefault(a => a.Key == focus) is { } aspect)
        {
            lines.Add($"دربارهٔ {aspect.Title}: {PersianText.Digits(aspect.Positive)} نظر مثبت و {PersianText.Digits(aspect.Negative)} نظر منفی.");
        }
        else if (focus is not null)
        {
            var title = Aspects.FirstOrDefault(a => a.Key == focus).Title;
            if (title is not null)
                lines.Add($"خریداران دربارهٔ {title} چیزی ننوشته‌اند.");
        }
        var strengths = summary.Aspects.Where(a => a.Positive > a.Negative).Take(3).Select(a => $"{a.Title} ({PersianText.Digits(a.Positive)})").ToList();
        var weaknesses = summary.Aspects.Where(a => a.Negative >= a.Positive && a.Negative > 0).Take(2).Select(a => $"{a.Title} ({PersianText.Digits(a.Negative)})").ToList();
        if (strengths.Count > 0)
            lines.Add("نقاط قوت از نگاه خریداران: " + string.Join("، ", strengths));
        if (weaknesses.Count > 0)
            lines.Add("نقاط ضعف: " + string.Join("، ", weaknesses));
        if (summary.GoodQuote is not null)
            lines.Add($"یکی از خریداران: «{summary.GoodQuote}»");
        if (summary.BadQuote is not null && summary.Negative > 0)
            lines.Add($"انتقاد: «{summary.BadQuote}»");
        return string.Join("\n", lines);
    }

    /// <summary>بندهای یک دیدگاه. مثال: «صداش عالیه ولی باتریش ضعیفه.» ← [«صداش عالیه»، «باتریش ضعیفه»].</summary>
    public static List<string> Clauses(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [];
        return ClauseSplit().Split(text).Select(c => c.Trim()).Where(c => c.Length >= 3).Take(12).ToList();
    }

    /// <summary>واژهٔ جنبه در بند (تک‌واژه: کامل یا با پسوند کوتاه؛ عبارت: کامل).</summary>
    private static bool Mentions(string padded, string word)
    {
        var normalized = PersianText.Normalize(word);
        if (normalized.Contains(' '))
            return padded.Contains(" " + normalized + " ", StringComparison.Ordinal) || padded.Contains(" " + normalized, StringComparison.Ordinal);
        var at = padded.IndexOf(" " + normalized, StringComparison.Ordinal);
        while (at >= 0)
        {
            var end = padded.IndexOf(' ', at + 1 + normalized.Length);
            if (end >= 0 && end - (at + 1 + normalized.Length) <= (normalized.Length <= 3 ? 0 : 3))
                return true;
            at = padded.IndexOf(" " + normalized, at + 1, StringComparison.Ordinal);
        }
        return false;
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"[.!؟?،,;؛\n]+|\s(?:ولی|اما|ولیکن|منتها|فقط|البته)\s", System.Text.RegularExpressions.RegexOptions.CultureInvariant)]
    private static partial System.Text.RegularExpressions.Regex ClauseSplit();
}
