using System.ComponentModel.DataAnnotations;
using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Core.Services.Seo.Engine;
using Fratak_Shop.Core.Services.Seo.Smart;
using Fratak_Shop.Data.Entities.Pages;
using Fratak_Shop.Data.Entities.SeoEngine;
using Fratak_Shop.Web.Infrastructure.Admin;
using Fratak_Shop.Web.Infrastructure.SeoEngine;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

namespace Fratak_Shop.Web.Areas.Admin.Pages.Seo;

/// <summary>
/// «سئوی هوشمند» و «پیشنهاد هوشمند» درجا (مرحلهٔ شانزدهم). صفحه‌های ویرایش (محصول، مقاله، دسته، برند، صفحهٔ محتوا،
/// صفحه‌های ثابت) با اسکریپت smart-seo.js / smart-text.js همین هندلرها را صدا می‌زنند؛ هیچ چیز ذخیره نمی‌شود و کاربر
/// بعد از «استفاده از این متن» خودش فرم را ذخیره می‌کند. صفحهٔ GET یک ابزار مستقل بازنویسی متن است.
/// <para>دسترسی: هر کسی که یکی از این مجوزها را دارد؛ هر هندلر دوباره مجوز همان نوع صفحه را می‌سنجد
/// (مثلاً سئوی محصول فقط با «مدیریت محصولات» یا «سئو»).</para>
/// <para>محدودیت: هر کارمند حداکثر ۱۲ «سئوی هوشمند» (جست‌وجوی اینترنتی دارد) و ۶۰ «پیشنهاد هوشمند» در دقیقه.</para>
/// </summary>
[AdminPermission(AdminPermissions.ProductsManage, AdminPermissions.CatalogManage, AdminPermissions.BlogWrite,
    AdminPermissions.ContentManage, AdminPermissions.SeoManage)]
public class SmartModel(ISeoEngineService engine, ISmartTextService smart, IMemoryCache cache, ILogger<SmartModel> logger) : AdminPageModel
{
    public const int DraftsPerMinute = 12;
    public const int TextsPerMinute = 60;
    public const int MaxText = 200_000;

    public void OnGet()
    {
    }

    /// <summary>
    /// سئوی هوشمند یک صفحه با مقدارهای فعلی فرم (حتی محصول ذخیره‌نشده). پاسخ JSON: نمره قبل/بعد، ۲–۳ عنوان و توضیح،
    /// کلمهٔ کلیدی، نامک، متن تکمیلی، پرسش‌وپاسخ، هشدارهای جریمه و خلاصهٔ تحقیق.
    /// </summary>
    public async Task<IActionResult> OnPostDraftAsync([FromForm] SmartDraftForm form)
    {
        if (!Enum.IsDefined(form.Kind) || form.Kind == SeoEntityKind.None)
            return Fail("نوع صفحه نامعتبر است.");
        if (!CanEdit(form.Kind))
            return Fail("دسترسی سئوی این نوع صفحه را ندارید.", 403);
        if (!Allow("draft", DraftsPerMinute))
            return Fail("درخواست‌ها زیاد است؛ یک دقیقهٔ دیگر دوباره امتحان کنید.", 429);

        SeoEngineHost.Current = Request.Host.Host;
        try
        {
            var draft = await engine.DraftAsync(new SeoDraftInput
            {
                Kind = form.Kind,
                Id = form.Id,
                Page = form.Page is { } page && Enum.IsDefined(page) ? page : null,
                Name = Limit(form.Name, 300),
                EnglishName = Limit(form.EnglishName, 300),
                Slug = Limit(form.Slug, 300),
                ShortText = Limit(form.ShortText, 4000),
                BodyHtml = Limit(form.Body, MaxText),
                CategoryId = form.CategoryId,
                BrandId = form.BrandId,
                WarrantyTitle = Limit(form.WarrantyTitle, 200),
                WarrantyMonths = form.WarrantyMonths is >= 0 and <= 240 ? form.WarrantyMonths : null,
                SeoTitle = Limit(form.SeoTitle, 300),
                MetaDescription = Limit(form.MetaDescription, 600),
                FocusKeyword = Limit(form.FocusKeyword, 200),
                Research = form.Research,
                Variant = Math.Clamp(form.Variant, 0, 50),
            }, HttpContext.RequestAborted);

            return new JsonResult(new
            {
                ok = draft.Errors.Count == 0,
                message = draft.Errors.FirstOrDefault(),
                kind = SeoEngineUi.KindTitle(draft.Kind),
                path = draft.Path,
                scoreBefore = draft.ScoreBefore,
                scoreAfter = draft.ScoreAfter,
                keyword = draft.Keyword,
                keywordIsNew = draft.KeywordIsNew,
                keywords = draft.Keywords.Select(k => new { phrase = k.Phrase, score = k.Score, reasons = k.Reasons }),
                titles = draft.Titles.Select(Option),
                metas = draft.Metas.Select(Option),
                slug = draft.Slug,
                slugReason = draft.SlugReason,
                body = draft.BodyHtml,
                faq = draft.Faq.Select(f => new { q = f.Question, a = f.Answer }),
                todo = draft.Todo,
                reasons = draft.Reasons,
                warnings = draft.Warnings.Select(w => new { message = w.Message, severity = w.Severity }),
                research = new
                {
                    query = draft.Research.Query,
                    pages = draft.Research.Pages,
                    hosts = draft.Research.Hosts,
                    suggestions = draft.Research.Suggestions,
                    terms = draft.Research.Terms,
                    errors = draft.Research.Errors,
                },
                titleSuffix = draft.TitleSuffixLength,
            });
        }
        catch (OperationCanceledException)
        {
            return Fail("درخواست لغو شد.");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Smart SEO draft failed for {Kind} {Id}", form.Kind, form.Id);
            return Fail("ساخت پیشنهاد سئو ممکن نشد؛ دوباره امتحان کنید.");
        }
    }

    /// <summary>
    /// «پیشنهاد هوشمند» یک فیلد: ۲ تا ۳ نسخهٔ بهتر با توضیح تغییرات. پاسخ JSON:
    /// { ok, before: { score, words, issues }, variants: [{ id, title, text, isHtml, changes, notes, rules, score }] }.
    /// </summary>
    public async Task<IActionResult> OnPostTextAsync([FromForm] SmartTextForm form)
    {
        if (!Enum.IsDefined(form.Purpose) || !Enum.IsDefined(form.Goal))
            return Fail("نوع فیلد نامعتبر است.");
        if ((form.Text?.Length ?? 0) > MaxText)
            return Fail("متن بیش از حد بلند است.");
        if (form.Kind is { } kind && (!Enum.IsDefined(kind) || (kind != SeoEntityKind.None && !CanEdit(kind))))
            return Fail("دسترسی به این نوع صفحه را ندارید.", 403);
        if (!Allow("text", TextsPerMinute))
            return Fail("درخواست‌ها زیاد است؛ یک دقیقهٔ دیگر دوباره امتحان کنید.", 429);

        try
        {
            var result = await smart.SuggestAsync(new SmartTextInput
            {
                Purpose = form.Purpose,
                Goal = form.Goal,
                Text = form.Text ?? "",
                IsHtml = form.IsHtml,
                Kind = form.Kind,
                EntityId = form.Id,
                Subject = Limit(form.Subject, 300),
                EnglishName = Limit(form.EnglishName, 300),
                Keyword = Limit(form.Keyword, 200),
                CategoryId = form.CategoryId,
                BrandId = form.BrandId,
            }, HttpContext.RequestAborted);

            return new JsonResult(new
            {
                ok = result.Variants.Count > 0,
                message = result.Variants.Count == 0 ? result.Warnings.FirstOrDefault() ?? "پیشنهادی پیدا نشد." : null,
                before = new { score = result.Before.Score, words = result.Before.Words, characters = result.Before.Characters, issues = result.Before.Issues },
                warnings = result.Warnings,
                variants = result.Variants.Select(v => new
                {
                    id = v.Id, title = v.Title, text = v.Text, isHtml = v.IsHtml, changes = v.Changes, notes = v.Notes,
                    rules = v.RuleIds, score = v.Score, length = v.Length, words = v.Words,
                }),
            });
        }
        catch (OperationCanceledException)
        {
            return Fail("درخواست لغو شد.");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Smart text suggestion failed for {Purpose}", form.Purpose);
            return Fail("ساخت پیشنهاد ممکن نشد؛ دوباره امتحان کنید.");
        }
    }

    /// <summary>ثبت انتخاب کاربر: نسخهٔ انتخاب‌شده (Accepted=true) یا «هیچ‌کدام» (Variant=«*»، Accepted=false).</summary>
    public async Task<IActionResult> OnPostFeedbackAsync([FromForm] SmartFeedbackForm form)
    {
        if (!Enum.IsDefined(form.Purpose) || string.IsNullOrWhiteSpace(form.Variant) || form.Variant.Length > 40)
            return Fail("بازخورد نامعتبر است.");
        if (!Allow("feedback", TextsPerMinute))
            return Fail("درخواست‌ها زیاد است.", 429);
        await smart.RecordFeedbackAsync(new SmartTextFeedbackInput
        {
            Purpose = form.Purpose,
            Variant = form.Variant.Trim(),
            Accepted = form.Accepted && form.Variant.Trim() != "*",
            RuleIds = (form.Rules ?? []).Take(60).ToList(),
            Input = form.Input is { Length: > MaxText } ? form.Input[..MaxText] : form.Input,
            OutputLength = form.OutputLength,
        }, ActorId == Guid.Empty ? null : ActorId, HttpContext.RequestAborted);
        return new JsonResult(new { ok = true });
    }

    /// <summary>مجوز ویرایش سئوی هر نوع صفحه («سئو» همه را می‌تواند).</summary>
    public bool CanEdit(SeoEntityKind kind) => Can(AdminPermissions.SeoManage) || kind switch
    {
        SeoEntityKind.Product => Can(AdminPermissions.ProductsManage),
        SeoEntityKind.Category or SeoEntityKind.Brand => Can(AdminPermissions.CatalogManage),
        SeoEntityKind.BlogPost => Can(AdminPermissions.BlogWrite),
        SeoEntityKind.ContentPage or SeoEntityKind.StaticPage => Can(AdminPermissions.ContentManage),
        _ => false,
    };

    /// <summary>محدودیت ساده برای هر کارمند (پنجرهٔ یک‌دقیقه‌ای در حافظه).</summary>
    private bool Allow(string action, int perMinute)
    {
        var key = $"smart-limit:{action}:{ActorId}:{DateTime.UtcNow:yyyyMMddHHmm}";
        var count = cache.GetOrCreate(key, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(2);
            return new StrongBox();
        })!;
        return Interlocked.Increment(ref count.Value) <= perMinute;
    }

    private sealed class StrongBox
    {
        public int Value;
    }

    private static object Option(SeoDraftOption option) => new { text = option.Text, length = option.Length, full = option.FullLength, note = option.Note };

    private static string? Limit(string? value, int max) => value is null ? null : value.Length > max ? value[..max] : value;

    private JsonResult Fail(string message, int status = 200) => new(new { ok = false, message }) { StatusCode = status };
}

/// <summary>
/// فیلدهای فرم «سئوی هوشمند». null = فیلد در فرم نیست؛ رشتهٔ خالی = خالی شده (برای همین تبدیل خودکار رشتهٔ خالی به null
/// خاموش است).
/// </summary>
public sealed class SmartDraftForm
{
    public SeoEntityKind Kind { get; set; }
    public Guid? Id { get; set; }
    public SitePage? Page { get; set; }
    [DisplayFormat(ConvertEmptyStringToNull = false)] public string? Name { get; set; }
    [DisplayFormat(ConvertEmptyStringToNull = false)] public string? EnglishName { get; set; }
    [DisplayFormat(ConvertEmptyStringToNull = false)] public string? Slug { get; set; }
    [DisplayFormat(ConvertEmptyStringToNull = false)] public string? ShortText { get; set; }
    [DisplayFormat(ConvertEmptyStringToNull = false)] public string? Body { get; set; }
    public Guid? CategoryId { get; set; }
    public Guid? BrandId { get; set; }
    [DisplayFormat(ConvertEmptyStringToNull = false)] public string? WarrantyTitle { get; set; }
    public int? WarrantyMonths { get; set; }
    [DisplayFormat(ConvertEmptyStringToNull = false)] public string? SeoTitle { get; set; }
    [DisplayFormat(ConvertEmptyStringToNull = false)] public string? MetaDescription { get; set; }
    [DisplayFormat(ConvertEmptyStringToNull = false)] public string? FocusKeyword { get; set; }
    public bool Research { get; set; } = true;
    public int Variant { get; set; }
}

/// <summary>فیلدهای فرم «پیشنهاد هوشمند».</summary>
public sealed class SmartTextForm
{
    public SmartPurpose Purpose { get; set; }
    public SmartGoal Goal { get; set; }
    public string? Text { get; set; }
    public bool IsHtml { get; set; }
    public SeoEntityKind? Kind { get; set; }
    public Guid? Id { get; set; }
    public string? Subject { get; set; }
    public string? EnglishName { get; set; }
    public string? Keyword { get; set; }
    public Guid? CategoryId { get; set; }
    public Guid? BrandId { get; set; }
}

/// <summary>بازخورد «پیشنهاد هوشمند».</summary>
public sealed class SmartFeedbackForm
{
    public SmartPurpose Purpose { get; set; }
    public string? Variant { get; set; }
    public bool Accepted { get; set; }
    public List<string>? Rules { get; set; }
    public string? Input { get; set; }
    public int OutputLength { get; set; }
}
