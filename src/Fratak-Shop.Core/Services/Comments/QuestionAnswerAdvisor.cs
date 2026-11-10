using Fratak_Shop.Core.Services.Assistant;
using Fratak_Shop.Core.Services.Seo.Engine;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Comments;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Core.Services.Comments;

// ═══════════════════════════════════════════════════════════════════════════
//  دور ۲۵ – «پیش‌نویس هوشمند پاسخ پرسش کالا» (بخش ب)
//  در صفحهٔ «نظرات و پرسش‌ها»ی پنل، زیر هر پرسشِ بی‌پاسخ چند دکمهٔ پیش‌نویس می‌آید. کلیک ← متن در کادر پاسخ درج می‌شود
//  (هرگز خودکار منتشر نمی‌شود؛ کارمند می‌خواند، ویرایش می‌کند و «انتشار پاسخ» را می‌زند).
//  منابع (همه داخلی):
//   ۱) مشخصات ثبت‌شدهٔ همان کالا (موضوع پرسش ← کلید مشخصه؛ مثل «باتری» ← «ظرفیت باتری»)، موجودی و گارانتی؛
//   ۲) پاسخ فروشگاه به پرسش خیلی شبیه در همان کالا یا کالاهای همان دسته؛
//   ۳) خلاصهٔ نظر خریداران دربارهٔ همان جنبه (صدا، باتری، کیفیت…).
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>یک پیش‌نویس پاسخ. Title = متن دکمه، Body = متن درج‌شونده، Source = منبع («مشخصات کالا»، «پرسش مشابه»، «نظر خریداران»).</summary>
public sealed record QuestionAnswerSuggestion(string Title, string Body, string Source, double Score);

/// <summary>اطلاعات کالا برای ساخت پیش‌نویس (خالص؛ برای تست بدون پایگاه داده).</summary>
public sealed record QuestionProductFacts(string Name, int Stock, string? WarrantyTitle, int? WarrantyMonths, IReadOnlyList<(string Key, string Value)> Features);

/// <summary>پیش‌نویس‌های پاسخ برای پرسش‌های بی‌پاسخ.</summary>
public interface IQuestionAnswerAdvisor
{
    /// <summary>
    /// برای هر پرسش (فقط پرسش اصلی کالا) حداکثر ۳ پیش‌نویس. پرسش ناموجود یا بدون پیش‌نویس در خروجی نیست.
    /// حداکثر ۳۰ پرسش در هر بار (یک صفحهٔ پنل).
    /// </summary>
    Task<IReadOnlyDictionary<Guid, IReadOnlyList<QuestionAnswerSuggestion>>> SuggestAsync(IReadOnlyCollection<Guid> questionIds, CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public sealed class QuestionAnswerAdvisor(FratakShopDbContext context) : IQuestionAnswerAdvisor
{
    /// <summary>کمترین شباهت دوطرفه برای استفاده از پاسخ یک پرسش قبلی.</summary>
    public const double MinSimilar = 0.5;

    /// <summary>بیشترین تعداد پیش‌نویس برای هر پرسش.</summary>
    public const int MaxSuggestions = 3;

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<QuestionAnswerSuggestion>>> SuggestAsync(IReadOnlyCollection<Guid> questionIds, CancellationToken cancellationToken = default)
    {
        var result = new Dictionary<Guid, IReadOnlyList<QuestionAnswerSuggestion>>();
        var ids = questionIds.Distinct().Take(30).ToList();
        if (ids.Count == 0)
            return result;

        var questions = await context.Comments.AsNoTracking()
            .Where(c => ids.Contains(c.Id) && c.Type == CommentType.Question && c.ParentId == null && c.ProductId != null)
            .Select(c => new { c.Id, c.Content, ProductId = c.ProductId!.Value, c.Product!.CategoryId })
            .ToListAsync(cancellationToken);
        if (questions.Count == 0)
            return result;

        var productIds = questions.Select(q => q.ProductId).Distinct().ToList();
        var facts = (await context.Products.AsNoTracking()
                .Where(p => productIds.Contains(p.Id))
                .Select(p => new
                {
                    p.Id,
                    p.Name,
                    p.StockQuantity,
                    p.WarrantyTitle,
                    p.WarrantyMonths,
                    Features = p.Features.OrderBy(f => f.DisplayOrder).Select(f => new { f.Key, f.Value }).Take(60).ToList(),
                })
                .ToListAsync(cancellationToken))
            .ToDictionary(p => p.Id, p => new QuestionProductFacts(p.Name, p.StockQuantity, p.WarrantyTitle, p.WarrantyMonths,
                p.Features.Select(f => (Key: f.Key.Trim(), Value: f.Value.Trim())).Where(f => f.Key.Length > 0 && f.Value.Length > 0).ToList()));

        // پرسش‌های پاسخ‌داده‌شدهٔ همین کالاها و هم‌دسته‌ها (۴۰۰ تای اخیر) با اولین پاسخ تأییدشده.
        var categoryIds = questions.Select(q => q.CategoryId).Distinct().ToList();
        var answered = await context.Comments.AsNoTracking()
            .Where(c => c.Type == CommentType.Question && c.ParentId == null && c.Status == CommentStatus.Approved && c.ProductId != null
                        && !ids.Contains(c.Id) && (productIds.Contains(c.ProductId!.Value) || categoryIds.Contains(c.Product!.CategoryId)))
            .OrderByDescending(c => c.CreatedAt)
            .Select(c => new
            {
                c.Content,
                ProductId = c.ProductId!.Value,
                Answer = c.Replies.Where(r => r.Status == CommentStatus.Approved).OrderBy(r => r.CreatedAt).Select(r => r.Content).FirstOrDefault(),
            })
            .Take(400)
            .ToListAsync(cancellationToken);

        foreach (var question in questions)
        {
            if (!facts.TryGetValue(question.ProductId, out var product))
                continue;
            // پاسخ‌های همان کالا جلوتر از هم‌دسته‌ها.
            var similar = answered
                .Where(a => !string.IsNullOrWhiteSpace(a.Answer))
                .OrderByDescending(a => a.ProductId == question.ProductId)
                .Select(a => (a.Content, Answer: a.Answer!))
                .ToList();
            var topic = AssistantNlu.Parse(question.Content).Topic;
            ReviewSummary? reviews = null;
            if (topic is not null && AssistantReviews.Aspects.Any(a => a.Key == topic))
                reviews = AssistantReviews.Summarize(await ReviewsAsync(question.ProductId, cancellationToken));
            var suggestions = Build(question.Content, product, similar, reviews);
            if (suggestions.Count > 0)
                result[question.Id] = suggestions;
        }
        return result;
    }

    private async Task<IReadOnlyList<ReviewInput>> ReviewsAsync(Guid productId, CancellationToken cancellationToken) =>
        (await context.Comments.AsNoTracking()
            .Where(c => c.ProductId == productId && c.Type == CommentType.Review && c.Status == CommentStatus.Approved && c.ParentId == null)
            .OrderByDescending(c => c.CreatedAt)
            .Select(c => new { c.Content, c.Rating, c.IsRecommended })
            .Take(60)
            .ToListAsync(cancellationToken))
        .Select(c => new ReviewInput(c.Content, c.Rating, c.IsRecommended))
        .ToList();

    /// <summary>
    /// ساخت پیش‌نویس‌ها (خالص). ترتیب: مشخصات/موجودی/گارانتی ← پاسخ پرسش مشابه ← نظر خریداران؛ حداکثر ۳ تا.
    /// هر متن با «سلام {FirstName} عزیز،» شروع می‌شود (هنگام انتشار با نام پرسش‌کننده پر می‌شود).
    /// مثال: «باتریش چند میلی‌آمپره؟» + مشخصهٔ «ظرفیت باتری: 10000 میلی‌آمپر» ← «… طبق مشخصات: ظرفیت باتری: 10000 میلی‌آمپر.»
    /// </summary>
    public static IReadOnlyList<QuestionAnswerSuggestion> Build(string question, QuestionProductFacts product,
        IReadOnlyList<(string Question, string Answer)> answered, ReviewSummary? reviews)
    {
        const string Hello = "سلام {FirstName} عزیز، ";
        var list = new List<QuestionAnswerSuggestion>();
        var parse = AssistantNlu.Parse(question);
        var topic = AssistantNlu.Topic(parse.Topic);

        // ۱. موجودی و گارانتی (دادهٔ قطعی).
        if (topic?.Key == "stock")
            list.Add(new("موجودی", Hello + (product.Stock > 0
                ? "این کالا هم‌اکنون موجود است و می‌توانید سفارش دهید."
                : "این کالا فعلاً ناموجود است؛ با زدن «خبرم کن» در صفحهٔ کالا، به محض موجود شدن خبرتان می‌کنیم."), "اطلاعات کالا", 1));
        if (topic?.Key == "warranty" && !string.IsNullOrWhiteSpace(product.WarrantyTitle))
            list.Add(new("گارانتی", Hello + $"این کالا {product.WarrantyTitle.Trim()}" +
                (product.WarrantyMonths is > 0 ? $" ({PersianText.Digits(product.WarrantyMonths.Value)} ماه)" : "") + " دارد.", "اطلاعات کالا", 1));

        // ۲. مشخصه‌های مرتبط (موضوع پرسش یا واژه‌های پرسش در نام مشخصه).
        var keys = (topic?.FeatureKeys ?? []).Select(PersianText.Normalize).ToList();
        var words = AssistantNlu.Words(parse.Query).Where(w => w.Length >= 3).ToList();
        var matched = product.Features
            .Where(f =>
            {
                var key = PersianText.Normalize(f.Key);
                return keys.Any(k => key.Contains(k, StringComparison.Ordinal)) || words.Any(w => key.Contains(w, StringComparison.Ordinal));
            })
            .Take(3)
            .ToList();
        if (matched.Count > 0)
            list.Add(new("طبق مشخصات", Hello + "طبق مشخصات کالا: " + string.Join("؛ ", matched.Select(f => $"{f.Key}: {f.Value}")) + ".", "مشخصات کالا", 0.9));

        // ۳. پاسخ پرسش خیلی شبیه (شباهت دوطرفه ≥ ۰٫۵).
        var best = answered
            .Select(a => (a.Answer, Score: Math.Min(AssistantNlu.Similarity(question, a.Question), AssistantNlu.Similarity(a.Question, question))))
            .Where(a => a.Score >= MinSimilar)
            .OrderByDescending(a => a.Score)
            .FirstOrDefault();
        if (best.Answer is not null)
        {
            var answer = PersianText.Truncate(PersianText.HtmlToText(best.Answer).Trim(), 600);
            if (answer.Length > 0)
                list.Add(new("پاسخ پرسش مشابه", answer.StartsWith("سلام", StringComparison.Ordinal) ? answer : Hello + answer, "پرسش مشابه", Math.Round(best.Score, 2)));
        }

        // ۴. نظر خریداران دربارهٔ همان جنبه.
        if (reviews is not null && topic is not null && reviews.Aspects.Any(a => a.Key == topic.Key)
            && AssistantReviews.Text(reviews, product.Name, topic.Key) is { } reviewText)
            list.Add(new("نظر خریداران", Hello + "بر اساس نظر خریداران: " + reviewText.Replace("\n", " "), "نظر خریداران", 0.6));

        return list.Take(MaxSuggestions).ToList();
    }
}
