using Fratak_Shop.Data.Entities.SeoEngine;

namespace Fratak_Shop.Core.Services.Seo.Engine;

/// <summary>
/// دور ۳۱ (#۹) – «نگهبان محتوای تکراری» پیشنهادهای سئو: هیچ پیشنهادی نباید متنی بسازد که صفحهٔ دیگری از سایت (یا پیشنهاد دیگری
/// که منتظر تأیید است) همان را دارد؛ گوگل و سرچ کنسول صفحه‌های با عنوان/توضیحات/متن تکراری را «کم‌ارزش/تکراری» حساب می‌کنند.
/// <list type="bullet">
/// <item>عنوان سئو: همان عنوان (پس از یکسان‌سازی حروف و فاصله‌ها) روی صفحهٔ دیگری هست ← حذف از پیشنهاد.</item>
/// <item>توضیحات متا: همان قاعده.</item>
/// <item>متن توضیحات: اثر انگشت SimHash با متن صفحهٔ دیگری فاصلهٔ همینگ ≤ <see cref="NearBody"/> دارد ← حذف.</item>
/// <item>پرسش‌وپاسخ: پرسش‌ها اگر بیش از نیمه‌شان روی صفحهٔ دیگری عیناً تکرار شده باشد ← حذف (FAQ قالبی جریمه دارد).</item>
/// </list>
/// خالص و بدون پایگاه داده؛ ورودی همان <see cref="SeoSiteIndex"/> اجرای موتور است که پیشنهادهای منتظر هم در آن «رزرو» شده‌اند.
/// </summary>
public static class SeoDuplicateGuard
{
    /// <summary>بیشترین فاصلهٔ همینگ SimHash که «تقریباً تکراری» حساب می‌شود (همان مرز تحلیل‌گر).</summary>
    public const int NearBody = 3;

    /// <summary>
    /// بخش‌های تکراری را از پیشنهاد پاک می‌کند و بخش‌های حذف‌شده را برمی‌گرداند.
    /// مثال: عنوان پیشنهادی «خرید هدفون بی‌سیم» وقتی صفحهٔ دیگری همین عنوان را دارد ← Title حذف و SeoFields.Title برمی‌گردد.
    /// </summary>
    public static SeoFields Prune(SeoProposal proposal, Guid self, SeoSiteIndex? site)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        if (site is null)
            return SeoFields.None;
        var removed = SeoFields.None;

        if (!string.IsNullOrWhiteSpace(proposal.Title) && SeoSiteIndex.Others(site.Titles, proposal.Title, self) > 0)
        {
            proposal.Title = null;
            removed |= SeoFields.Title;
        }
        if (!string.IsNullOrWhiteSpace(proposal.MetaDescription) && SeoSiteIndex.Others(site.Descriptions, proposal.MetaDescription, self) > 0)
        {
            proposal.MetaDescription = null;
            removed |= SeoFields.MetaDescription;
        }
        if (!string.IsNullOrWhiteSpace(proposal.DescriptionHtml) && IsNearDuplicateBody(proposal.DescriptionHtml, self, site))
        {
            proposal.DescriptionHtml = null;
            removed |= SeoFields.Description;
        }
        if (proposal.Faq is { Count: > 0 } faq)
        {
            var repeated = faq.Count(f => SeoSiteIndex.Others(site.FaqQuestions, f.Question, self) > 0);
            if (repeated * 2 > faq.Count)
            {
                proposal.Faq = [];
                removed |= SeoFields.Faq;
            }
        }
        return removed;
    }

    /// <summary>
    /// آیا متن با متن صفحهٔ دیگری تقریباً یکی است؟ متن کوتاه (کمتر از ۶۰ واژه) سنجیده نمی‌شود (اثر انگشت‌اش قابل اعتماد نیست).
    /// </summary>
    public static bool IsNearDuplicateBody(string html, Guid self, SeoSiteIndex site)
    {
        ArgumentNullException.ThrowIfNull(site);
        var text = PersianText.HtmlToText(html);
        if (PersianText.WordCount(text) < 60 || site.BodyFingerprints.Count == 0)
            return false;
        var fingerprint = TextModels.SimHash(text);
        foreach (var (id, other) in site.BodyFingerprints)
        {
            if (id != self && TextModels.Hamming(fingerprint, other) <= NearBody)
                return true;
        }
        return false;
    }
}
