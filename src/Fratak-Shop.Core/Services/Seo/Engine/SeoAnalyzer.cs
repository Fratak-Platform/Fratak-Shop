using System.Text.RegularExpressions;
using Fratak_Shop.Data.Entities.SeoEngine;

namespace Fratak_Shop.Core.Services.Seo.Engine;

// ═══════════════════════════════════════════════════════════════════════════
//  تحلیل‌گر سئو: به هر صفحه نمرهٔ ۰ تا ۱۰۰ می‌دهد (مشابه Yoast/RankMath ولی برای فارسی و با بررسی‌های بین‌صفحه‌ای).
//  هر بررسی وزن دارد؛ «خوب» = کل وزن، «متوسط» = نصف، «بد» = صفر. نمره = مجموع امتیاز ÷ مجموع وزن‌ها.
//  بررسی‌هایی که برای آن صفحه معنی ندارند (مثلاً تراکم کلمه در متن خیلی کوتاه) حذف می‌شوند، نه صفر.
//  استفاده: SeoEngineService (نمرهٔ همهٔ صفحه‌ها و نمرهٔ پیش‌بینی پیشنهاد)، صفحهٔ «تحلیل صفحه» پنل، و نویسنده
//  (ContentWriter) برای انتخاب بهترین نسخه از بین چند متن.
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>نتیجهٔ یک بررسی.</summary>
public enum SeoCheckLevel : byte
{
    Good = 1,
    Ok = 2,
    Bad = 3,
    /// <summary>فقط اطلاع (در نمره حساب نمی‌شود). مثال: «این صفحه noindex است».</summary>
    Info = 4,
}

/// <summary>
/// یک بررسی. Fix = بخشی که اصلاحش این بررسی را درست می‌کند (None = فقط انسان می‌تواند، مثلاً افزودن عکس).
/// </summary>
public sealed record SeoCheck(string Id, string Group, SeoCheckLevel Level, string Message, int Weight, SeoFields Fix);

/// <summary>نتیجهٔ تحلیل یک صفحه.</summary>
public sealed class SeoReport
{
    public int Score { get; init; }
    public IReadOnlyList<SeoCheck> Checks { get; init; } = [];
    public int Words { get; init; }
    public int KeywordCount { get; init; }
    /// <summary>تراکم کلمهٔ کلیدی به درصد.</summary>
    public double Density { get; init; }

    public int Problems => Checks.Count(c => c.Level == SeoCheckLevel.Bad);

    /// <summary>بخش‌هایی که حداقل یک بررسیِ نه‌چندان خوب دارند (نویسنده فقط همین‌ها را بازنویسی می‌کند).</summary>
    public SeoFields WeakFields => Checks
        .Where(c => c.Level is SeoCheckLevel.Bad or SeoCheckLevel.Ok)
        .Aggregate(SeoFields.None, (all, c) => all | c.Fix);
}

/// <summary>تحلیل‌گر سئو (ایستا و بدون وضعیت).</summary>
public static partial class SeoAnalyzer
{
    /// <summary>
    /// تحلیل یک صفحه. site (اختیاری) برای بررسی‌های تکراری بودن؛ store برای طول نام سایت در عنوان.
    /// مثال: Analyze(subject, index, store).Score → ۷۴.
    /// </summary>
    public static SeoReport Analyze(SeoSubject subject, SeoSiteIndex? site, StoreFacts store)
    {
        var checks = new List<SeoCheck>();
        var keyword = PersianText.Clean(subject.FocusKeyword);
        var keyStems = PersianText.ContentStems(keyword);
        var hasKeyword = keyStems.Count > 0;
        var title = PersianText.Clean(subject.EffectiveTitle);
        var meta = subject.EffectiveDescription;
        var html = PersianText.AnalyzeHtml(subject.BodyHtml);
        var shortText = PersianText.HtmlToText(subject.ShortText);
        var fullText = (shortText + "\n" + html.Text).Trim();
        var words = PersianText.WordCount(fullText);
        var (goodWords, okWords) = SeoLimits.ContentWords(subject.Kind);

        // ── اطلاع‌ها ─────────────────────────────────────────────
        if (subject.NoIndex)
            checks.Add(new("noindex", "فنی", SeoCheckLevel.Info, "این صفحه noindex است و در گوگل نمایش داده نمی‌شود؛ موتور برایش پیشنهاد نمی‌سازد.", 0, SeoFields.None));
        if (subject.HasCanonicalElsewhere)
            checks.Add(new("canonical", "فنی", SeoCheckLevel.Info, "آدرس کانونیکال این صفحه به صفحهٔ دیگری اشاره می‌کند.", 0, SeoFields.None));

        // ── کلمهٔ کلیدی ───────────────────────────────────────────
        checks.Add(hasKeyword
            ? new("keyword-set", "کلمهٔ کلیدی", SeoCheckLevel.Good, $"کلمهٔ کلیدی اصلی: «{keyword}».", 8, SeoFields.FocusKeyword)
            : new("keyword-set", "کلمهٔ کلیدی", SeoCheckLevel.Bad, "کلمهٔ کلیدی اصلی تعیین نشده است.", 8, SeoFields.FocusKeyword));

        if (hasKeyword && site is not null)
        {
            var others = SeoSiteIndex.Others(site.FocusKeywords, keyword, subject.EntityId);
            checks.Add(others == 0
                ? new("keyword-unique", "کلمهٔ کلیدی", SeoCheckLevel.Good, "هیچ صفحهٔ دیگری روی این کلمهٔ کلیدی کار نمی‌کند.", 4, SeoFields.FocusKeyword)
                : new("keyword-unique", "کلمهٔ کلیدی", SeoCheckLevel.Bad, $"{PersianText.Digits(others)} صفحهٔ دیگر هم همین کلمهٔ کلیدی را دارند (رقابت داخلی؛ گوگل نمی‌داند کدام را نشان دهد).", 4, SeoFields.FocusKeyword));
        }

        // ── عنوان ────────────────────────────────────────────────
        var fullTitleLength = title.Length + store.TitleSuffixLength;
        checks.Add(fullTitleLength switch
        {
            >= SeoLimits.TitleMin and <= SeoLimits.TitleMax => new("title-length", "عنوان", SeoCheckLevel.Good, $"طول عنوان مناسب است ({PersianText.Digits(fullTitleLength)} نویسه همراه نام سایت).", 8, SeoFields.Title),
            >= 20 and < SeoLimits.TitleMin => new("title-length", "عنوان", SeoCheckLevel.Ok, $"عنوان کمی کوتاه است ({PersianText.Digits(fullTitleLength)} نویسه)؛ ۳۰ تا ۶۰ بهتر است.", 8, SeoFields.Title),
            > SeoLimits.TitleMax and <= 70 => new("title-length", "عنوان", SeoCheckLevel.Ok, $"عنوان کمی بلند است ({PersianText.Digits(fullTitleLength)} نویسه) و ممکن است در نتایج گوگل بریده شود.", 8, SeoFields.Title),
            _ => new("title-length", "عنوان", SeoCheckLevel.Bad, $"طول عنوان نامناسب است ({PersianText.Digits(fullTitleLength)} نویسه)؛ ۳۰ تا ۶۰ نویسه باشد.", 8, SeoFields.Title),
        });

        if (hasKeyword)
        {
            var position = KeywordMatcher.FirstPosition(title, keyword);
            checks.Add(position switch
            {
                < 0 => new("title-keyword", "عنوان", SeoCheckLevel.Bad, "کلمهٔ کلیدی در عنوان نیست.", 8, SeoFields.Title),
                <= 0.34 => new("title-keyword", "عنوان", SeoCheckLevel.Good, "کلمهٔ کلیدی در ابتدای عنوان آمده است.", 8, SeoFields.Title),
                _ => new("title-keyword", "عنوان", SeoCheckLevel.Ok, "کلمهٔ کلیدی در عنوان هست؛ بهتر است به ابتدای عنوان نزدیک‌تر باشد.", 8, SeoFields.Title),
            });
        }

        if (site is not null && title.Length > 0)
        {
            var others = SeoSiteIndex.Others(site.Titles, title, subject.EntityId);
            checks.Add(others == 0
                ? new("title-unique", "عنوان", SeoCheckLevel.Good, "عنوان در سایت یکتاست.", 5, SeoFields.Title)
                : new("title-unique", "عنوان", SeoCheckLevel.Bad, $"عنوان با {PersianText.Digits(others)} صفحهٔ دیگر یکسان است.", 5, SeoFields.Title));
        }

        // ── توضیحات متا ───────────────────────────────────────────
        checks.Add(meta.Length switch
        {
            0 => new("meta-length", "توضیحات متا", SeoCheckLevel.Bad, "توضیحات متا خالی است.", 7, SeoFields.MetaDescription),
            >= SeoLimits.MetaMin and <= SeoLimits.MetaMax => new("meta-length", "توضیحات متا", SeoCheckLevel.Good, $"طول توضیحات متا مناسب است ({PersianText.Digits(meta.Length)} نویسه).", 7, SeoFields.MetaDescription),
            >= 80 and < SeoLimits.MetaMin => new("meta-length", "توضیحات متا", SeoCheckLevel.Ok, $"توضیحات متا کوتاه است ({PersianText.Digits(meta.Length)} نویسه)؛ ۱۲۰ تا ۱۶۰ بهتر است.", 7, SeoFields.MetaDescription),
            > SeoLimits.MetaMax and <= 200 => new("meta-length", "توضیحات متا", SeoCheckLevel.Ok, $"توضیحات متا کمی بلند است ({PersianText.Digits(meta.Length)} نویسه) و بریده می‌شود.", 7, SeoFields.MetaDescription),
            _ => new("meta-length", "توضیحات متا", SeoCheckLevel.Bad, $"طول توضیحات متا نامناسب است ({PersianText.Digits(meta.Length)} نویسه).", 7, SeoFields.MetaDescription),
        });

        if (hasKeyword && meta.Length > 0)
        {
            checks.Add(KeywordMatcher.Contains(meta, keyword)
                ? new("meta-keyword", "توضیحات متا", SeoCheckLevel.Good, "کلمهٔ کلیدی در توضیحات متا آمده است.", 6, SeoFields.MetaDescription)
                : new("meta-keyword", "توضیحات متا", SeoCheckLevel.Bad, "کلمهٔ کلیدی در توضیحات متا نیست (گوگل آن را پررنگ نشان می‌دهد).", 6, SeoFields.MetaDescription));
        }

        if (site is not null && meta.Length > 0)
        {
            var others = SeoSiteIndex.Others(site.Descriptions, meta, subject.EntityId);
            checks.Add(others == 0
                ? new("meta-unique", "توضیحات متا", SeoCheckLevel.Good, "توضیحات متا یکتاست.", 4, SeoFields.MetaDescription)
                : new("meta-unique", "توضیحات متا", SeoCheckLevel.Bad, $"توضیحات متا با {PersianText.Digits(others)} صفحهٔ دیگر یکسان است.", 4, SeoFields.MetaDescription));
        }

        // ── نامک ─────────────────────────────────────────────────
        AddSlugChecks(subject, keyword, keyStems, checks);

        // ── محتوا ────────────────────────────────────────────────
        var contentWeight = subject.Kind == SeoEntityKind.ContentPage ? 4 : 10;
        checks.Add(words >= goodWords
            ? new("content-length", "محتوا", SeoCheckLevel.Good, $"محتوای کافی ({PersianText.Digits(words)} واژه).", contentWeight, SeoFields.Description)
            : words >= okWords
                ? new("content-length", "محتوا", SeoCheckLevel.Ok, $"محتوا کمی کم است ({PersianText.Digits(words)} واژه؛ هدف: {PersianText.Digits(goodWords)}).", contentWeight, SeoFields.Description)
                : new("content-length", "محتوا", SeoCheckLevel.Bad, $"محتوا خیلی کم است ({PersianText.Digits(words)} واژه؛ حداقل {PersianText.Digits(okWords)}). صفحه‌های کم‌محتوا رتبهٔ خوبی نمی‌گیرند.", contentWeight, SeoFields.Description));

        var keywordCount = 0;
        var density = 0.0;
        if (hasKeyword && words >= 50)
        {
            var intro = html.Paragraphs.FirstOrDefault() is { } first && PersianText.WordCount(first) >= 8
                ? (shortText.Length > 0 ? shortText + " " + first : first)
                : string.Join(' ', PersianText.Tokenize(fullText).Take(80));
            checks.Add(KeywordMatcher.Contains(intro, keyword)
                ? new("keyword-intro", "محتوا", SeoCheckLevel.Good, "کلمهٔ کلیدی در پاراگراف اول آمده است.", 5, SeoFields.Description)
                : new("keyword-intro", "محتوا", SeoCheckLevel.Bad, "کلمهٔ کلیدی در پاراگراف اول نیست.", 5, SeoFields.Description));
        }

        if (hasKeyword && words >= 100)
        {
            // تراکم = تعداد تکرار کل عبارت ÷ تعداد واژه‌های متن (همان روش Yoast و RankMath برای عبارت چندکلمه‌ای).
            keywordCount = KeywordMatcher.Count(fullText, keyword);
            density = Math.Round(100.0 * keywordCount / words, 2);
            var shown = density.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
            checks.Add(density switch
            {
                0 => new("keyword-density", "محتوا", SeoCheckLevel.Bad, "کلمهٔ کلیدی در متن تکرار نشده است.", 5, SeoFields.Description),
                > 3.5 => new("keyword-density", "محتوا", SeoCheckLevel.Bad, $"تکرار بیش از حد کلمهٔ کلیدی ({shown}٪) – خطر جریمهٔ «keyword stuffing»؛ زیر ۲٫۵٪ نگه دارید.", 5, SeoFields.Description),
                >= 0.5 and <= 2.5 => new("keyword-density", "محتوا", SeoCheckLevel.Good, $"تراکم کلمهٔ کلیدی مناسب است ({shown}٪، {PersianText.Digits(keywordCount)} بار).", 5, SeoFields.Description),
                _ => new("keyword-density", "محتوا", SeoCheckLevel.Ok, $"تراکم کلمهٔ کلیدی {shown}٪ است؛ ۰٫۵ تا ۲٫۵ درصد بهتر است.", 5, SeoFields.Description),
            });
        }

        var subheadings = html.Headings.Count(h => h.Level is 2 or 3);
        if (words >= 300)
        {
            var needed = Math.Max(1, words / 350);
            checks.Add(subheadings >= needed
                ? new("subheadings", "ساختار", SeoCheckLevel.Good, $"متن با {PersianText.Digits(subheadings)} زیرتیتر بخش‌بندی شده است.", 4, SeoFields.Description)
                : subheadings > 0
                    ? new("subheadings", "ساختار", SeoCheckLevel.Ok, $"زیرتیتر کم است ({PersianText.Digits(subheadings)})؛ تقریباً هر ۳۰۰ واژه یک تیتر H2/H3 بگذارید.", 4, SeoFields.Description)
                    : new("subheadings", "ساختار", SeoCheckLevel.Bad, "متن بلند است ولی هیچ زیرتیتر (H2/H3) ندارد.", 4, SeoFields.Description));

            if (hasKeyword)
            {
                checks.Add(html.Headings.Any(h => h.Level is 2 or 3 && KeywordMatcher.Contains(h.Text, keyword, partial: true))
                    ? new("keyword-heading", "ساختار", SeoCheckLevel.Good, "کلمهٔ کلیدی در یکی از زیرتیترها آمده است.", 3, SeoFields.Description)
                    : new("keyword-heading", "ساختار", SeoCheckLevel.Ok, "کلمهٔ کلیدی (یا بخشی از آن) در هیچ زیرتیتری نیست.", 3, SeoFields.Description));
            }
        }

        if (subject.Kind is SeoEntityKind.BlogPost or SeoEntityKind.ContentPage or SeoEntityKind.Category && words >= 150)
        {
            var internalLinks = html.Links.Count(l => l.Internal);
            checks.Add(internalLinks > 0
                ? new("internal-links", "ساختار", SeoCheckLevel.Good, $"{PersianText.Digits(internalLinks)} لینک داخلی در متن هست.", 3, SeoFields.Description)
                : new("internal-links", "ساختار", subject.Kind == SeoEntityKind.BlogPost ? SeoCheckLevel.Bad : SeoCheckLevel.Ok,
                    "متن هیچ لینک داخلی به صفحه‌های دیگر سایت ندارد.", 3, SeoFields.Description));
        }

        // ── خوانایی ──────────────────────────────────────────────
        if (words >= 150)
        {
            var sentences = PersianText.Sentences(fullText);
            var longShare = sentences.Count == 0 ? 0 : (double)sentences.Count(s => PersianText.WordCount(s) > 20) / sentences.Count;
            checks.Add(longShare switch
            {
                <= 0.25 => new("sentence-length", "خوانایی", SeoCheckLevel.Good, "جمله‌ها کوتاه و خوانا هستند.", 3, SeoFields.Description),
                <= 0.40 => new("sentence-length", "خوانایی", SeoCheckLevel.Ok, $"{PersianText.Digits((int)Math.Round(longShare * 100))}٪ جمله‌ها بیش از ۲۰ واژه‌اند؛ بهتر است کمتر از ۲۵٪ باشد.", 3, SeoFields.Description),
                _ => new("sentence-length", "خوانایی", SeoCheckLevel.Bad, $"{PersianText.Digits((int)Math.Round(longShare * 100))}٪ جمله‌ها خیلی بلندند (بیش از ۲۰ واژه).", 3, SeoFields.Description),
            });

            var longest = html.Paragraphs.Count == 0 ? 0 : html.Paragraphs.Max(PersianText.WordCount);
            checks.Add(longest switch
            {
                <= 120 => new("paragraph-length", "خوانایی", SeoCheckLevel.Good, "طول پاراگراف‌ها مناسب است.", 2, SeoFields.Description),
                <= 150 => new("paragraph-length", "خوانایی", SeoCheckLevel.Ok, "یکی از پاراگراف‌ها کمی بلند است.", 2, SeoFields.Description),
                _ => new("paragraph-length", "خوانایی", SeoCheckLevel.Bad, $"پاراگرافی با {PersianText.Digits(longest)} واژه هست؛ پاراگراف‌ها زیر ۱۵۰ واژه باشند.", 2, SeoFields.Description),
            });

            if (subject.Kind == SeoEntityKind.BlogPost && words >= 300 && sentences.Count > 0)
            {
                var share = (double)sentences.Count(HasTransition) / sentences.Count;
                checks.Add(share switch
                {
                    >= 0.2 => new("transitions", "خوانایی", SeoCheckLevel.Good, "از واژه‌های ربط (همچنین، بنابراین، …) به اندازهٔ کافی استفاده شده است.", 2, SeoFields.None),
                    >= 0.1 => new("transitions", "خوانایی", SeoCheckLevel.Ok, "واژه‌های ربط (همچنین، بنابراین، در نتیجه …) کم است.", 2, SeoFields.None),
                    _ => new("transitions", "خوانایی", SeoCheckLevel.Bad, "تقریباً هیچ واژهٔ ربطی (همچنین، بنابراین، در نتیجه …) نیست؛ متن بریده‌بریده خوانده می‌شود.", 2, SeoFields.None),
                });
            }
        }

        if (site is not null && site.BodyFingerprints.TryGetValue(subject.EntityId, out var fingerprint))
        {
            var duplicates = site.BodyFingerprints.Count(p => p.Key != subject.EntityId && TextModels.Hamming(p.Value, fingerprint) <= 3);
            checks.Add(duplicates == 0
                ? new("duplicate-content", "محتوا", SeoCheckLevel.Good, "متن صفحه یکتاست.", 3, SeoFields.Description)
                : new("duplicate-content", "محتوا", SeoCheckLevel.Bad, $"متن این صفحه تقریباً با {PersianText.Digits(duplicates)} صفحهٔ دیگر یکسان است (محتوای تکراری).", 3, SeoFields.Description));
        }

        // ── تصویر ────────────────────────────────────────────────
        var allImages = subject.Images.Select(i => i.Alt).Concat(html.Images.Select(i => i.Alt)).ToList();
        if (subject.Kind is SeoEntityKind.Product or SeoEntityKind.BlogPost)
        {
            var goodCount = subject.Kind == SeoEntityKind.Product ? 3 : 1;
            checks.Add(allImages.Count >= goodCount
                ? new("images", "تصویر", SeoCheckLevel.Good, $"{PersianText.Digits(allImages.Count)} تصویر دارد.", 4, SeoFields.None)
                : allImages.Count > 0
                    ? new("images", "تصویر", SeoCheckLevel.Ok, $"فقط {PersianText.Digits(allImages.Count)} تصویر دارد؛ حداقل {PersianText.Digits(goodCount)} تصویر بهتر است.", 4, SeoFields.None)
                    : new("images", "تصویر", SeoCheckLevel.Bad, "هیچ تصویری ندارد.", 4, SeoFields.None));
        }

        if (allImages.Count > 0)
        {
            var missing = allImages.Count(string.IsNullOrWhiteSpace);
            var withKeyword = hasKeyword && allImages.Any(a => !string.IsNullOrWhiteSpace(a) && KeywordMatcher.Contains(a!, keyword, partial: true));
            checks.Add(missing > 0
                ? new("image-alt", "تصویر", SeoCheckLevel.Bad, $"{PersianText.Digits(missing)} تصویر متن جایگزین (alt) ندارد.", 4, SeoFields.ImageAlt)
                : withKeyword || !hasKeyword
                    ? new("image-alt", "تصویر", SeoCheckLevel.Good, "همهٔ تصویرها متن جایگزین دارند.", 4, SeoFields.ImageAlt)
                    : new("image-alt", "تصویر", SeoCheckLevel.Ok, "متن جایگزین هیچ تصویری کلمهٔ کلیدی ندارد.", 4, SeoFields.ImageAlt));
        }

        // ── پرسش‌وپاسخ ────────────────────────────────────────────
        if (subject.Kind is SeoEntityKind.Product or SeoEntityKind.Category or SeoEntityKind.BlogPost)
        {
            var faq = subject.Faq.Count;
            checks.Add(faq >= 2
                ? new("faq", "پرسش‌وپاسخ", SeoCheckLevel.Good, $"{PersianText.Digits(faq)} پرسش‌وپاسخ دارد (با اسکیمای FAQPage).", 3, SeoFields.Faq)
                : new("faq", "پرسش‌وپاسخ", SeoCheckLevel.Ok, "بخش پرسش‌وپاسخ ندارد یا کم دارد؛ به پاسخ دادن به پرسش‌های رایج کاربران کمک می‌کند.", 3, SeoFields.Faq));
        }

        var scored = checks.Where(c => c.Level != SeoCheckLevel.Info && c.Weight > 0).ToList();
        var total = scored.Sum(c => c.Weight);
        var earned = scored.Sum(c => c.Level switch { SeoCheckLevel.Good => c.Weight, SeoCheckLevel.Ok => c.Weight / 2.0, _ => 0 });
        var score = total == 0 ? 0 : (int)Math.Round(100.0 * earned / total);

        return new SeoReport { Score = Math.Clamp(score, 0, 100), Checks = checks, Words = words, KeywordCount = keywordCount, Density = density };
    }

    private static void AddSlugChecks(SeoSubject subject, string keyword, List<string> keyStems, List<SeoCheck> checks)
    {
        var slug = Uri.UnescapeDataString(subject.Slug ?? "");
        if (slug.Length == 0)
            return;

        var slugIsLatin = PersianText.PersianRatio(slug) < 0.5;
        if (keyStems.Count > 0)
        {
            var slugStems = PersianText.ContentStems(slug.Replace('-', ' '));
            var covered = keyStems.Count(slugStems.Contains);
            var keywordIsPersian = PersianText.PersianRatio(keyword) >= 0.5;
            var ratio = (double)covered / keyStems.Count;
            checks.Add(ratio >= 0.6
                ? new("slug-keyword", "نامک", SeoCheckLevel.Good, "نامک آدرس شامل کلمهٔ کلیدی است.", 4, SeoFields.Slug)
                : slugIsLatin && keywordIsPersian
                    ? new("slug-keyword", "نامک", SeoCheckLevel.Ok, "نامک انگلیسی است و کلمهٔ کلیدی فارسی در آن نیست (اگر نام انگلیسی محصول باشد مشکلی نیست).", 4, SeoFields.Slug)
                    : ratio > 0
                        ? new("slug-keyword", "نامک", SeoCheckLevel.Ok, "فقط بخشی از کلمهٔ کلیدی در نامک آمده است.", 4, SeoFields.Slug)
                        : new("slug-keyword", "نامک", SeoCheckLevel.Bad, "نامک آدرس هیچ بخشی از کلمهٔ کلیدی را ندارد.", 4, SeoFields.Slug));
        }

        var looksRandom = RandomSlug().IsMatch(slug) || slug.All(c => char.IsDigit(c) || c == '-');
        checks.Add(looksRandom
            ? new("slug-quality", "نامک", SeoCheckLevel.Bad, "نامک آدرس بی‌معنی است (عدد یا کد تصادفی).", 3, SeoFields.Slug)
            : slug.Length <= SeoLimits.SlugMaxLength
                ? new("slug-quality", "نامک", SeoCheckLevel.Good, "نامک کوتاه و خواناست.", 3, SeoFields.Slug)
                : slug.Length <= 110
                    ? new("slug-quality", "نامک", SeoCheckLevel.Ok, $"نامک کمی بلند است ({PersianText.Digits(slug.Length)} نویسه).", 3, SeoFields.Slug)
                    : new("slug-quality", "نامک", SeoCheckLevel.Bad, $"نامک خیلی بلند است ({PersianText.Digits(slug.Length)} نویسه).", 3, SeoFields.Slug));
    }

    /// <summary>واژه‌ها و عبارت‌های ربط فارسی (نشانهٔ متن پیوسته و خوانا).</summary>
    public static readonly string[] TransitionWords =
    [
        "همچنین", "علاوه بر این", "بنابراین", "در نتیجه", "به همین دلیل", "از سوی دیگر", "از طرف دیگر", "با این حال", "در حالی که",
        "به عنوان مثال", "برای مثال", "مثلا", "در واقع", "به طور کلی", "در ادامه", "نخست", "سپس", "در پایان", "در نهایت", "اما", "ولی",
        "زیرا", "چون", "یعنی", "ضمنا", "از این رو", "پس از", "قبل از", "به علاوه", "به خصوص", "به ویژه", "خلاصه", "اگرچه", "هرچند",
    ];

    private static bool HasTransition(string sentence)
    {
        var normalized = " " + PersianText.Normalize(sentence).Replace("ً", "") + " ";
        return TransitionWords.Any(t => normalized.Contains(" " + t + " ", StringComparison.Ordinal));
    }

    // کد هگز یا عدد طولانی (≥ ۸ نویسه) داخل نامک: «a3f9c2e1b7» یا «product-12345678».
    [GeneratedRegex(@"(^|-)([0-9a-f]{10,}|[0-9]{8,})(-|$)", RegexOptions.IgnoreCase)]
    private static partial Regex RandomSlug();
}

/// <summary>
/// پیدا کردن کلمهٔ کلیدی در متن با در نظر گرفتن ریشهٔ واژه‌ها و نادیده گرفتن واژه‌های ایست.
/// «هدفون بی‌سیم» در «خرید هدفون‌های بی سیم» پیدا می‌شود.
/// </summary>
public static class KeywordMatcher
{
    /// <summary>
    /// کلمه در متن هست؟ پیش‌فرض: ریشه‌ها پشت سر هم. partial=true: حداقل ۶۰٪ ریشه‌های کلمه در متن باشند (برای تیترها و alt).
    /// </summary>
    public static bool Contains(string text, string keyword, bool partial = false)
    {
        var keyStems = PersianText.ContentStems(keyword);
        if (keyStems.Count == 0)
            return false;
        var textStems = PersianText.ContentStems(text);
        if (IndexOf(textStems, keyStems, 0) >= 0)
            return true;
        if (!partial)
            return false;
        var set = new HashSet<string>(textStems, StringComparer.Ordinal);
        return keyStems.Count(set.Contains) >= Math.Ceiling(keyStems.Count * 0.6);
    }

    /// <summary>تعداد دفعات تکرار کلمه (پشت سر هم، بدون هم‌پوشانی).</summary>
    public static int Count(string text, string keyword)
    {
        var keyStems = PersianText.ContentStems(keyword);
        if (keyStems.Count == 0)
            return 0;
        var textStems = PersianText.ContentStems(text);
        var count = 0;
        var index = IndexOf(textStems, keyStems, 0);
        while (index >= 0)
        {
            count++;
            index = IndexOf(textStems, keyStems, index + keyStems.Count);
        }
        return count;
    }

    /// <summary>
    /// جای اولین تکرار کلمه در متن به نسبت طول متن (۰ = ابتدا، نزدیک ۱ = انتها، ۱- = نیست).
    /// مثال: FirstPosition("هدفون سونی مدل X", "هدفون سونی") → ۰.
    /// </summary>
    public static double FirstPosition(string text, string keyword)
    {
        var keyStems = PersianText.ContentStems(keyword);
        var textStems = PersianText.ContentStems(text);
        if (keyStems.Count == 0 || textStems.Count == 0)
            return -1;
        var index = IndexOf(textStems, keyStems, 0);
        return index < 0 ? -1 : (double)index / textStems.Count;
    }

    private static int IndexOf(List<string> haystack, List<string> needle, int start)
    {
        for (var i = start; i <= haystack.Count - needle.Count; i++)
        {
            var match = true;
            for (var j = 0; j < needle.Count && match; j++)
                match = string.Equals(haystack[i + j], needle[j], StringComparison.Ordinal);
            if (match)
                return i;
        }
        return -1;
    }
}
