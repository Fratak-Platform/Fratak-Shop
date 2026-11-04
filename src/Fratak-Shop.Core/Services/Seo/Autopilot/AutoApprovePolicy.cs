using Fratak_Shop.Data.Entities.SeoEngine;

namespace Fratak_Shop.Core.Services.Seo.Autopilot;

/// <summary>وضعیت فعلی فیلدهای یک صفحه (خالی بودن هر کدام) برای تصمیم تأیید خودکار.</summary>
public sealed record FieldState(bool TitleEmpty, bool MetaEmpty, bool FocusEmpty, bool FaqEmpty, bool AllAltsEmpty, bool BodyThin);

/// <summary>
/// سیاست «تأیید خودکار» پیشنهادهای موتور سئو (قلب خلبان خودکار؛ بدون کارشناس سئو).
/// <list type="number">
/// <item><b>پر کردن</b>: فیلد خالی (عنوان، توضیحات، کلمهٔ کلیدی، FAQ، Alt همهٔ تصویرها) همیشه پر می‌شود؛ خالی ماندن هیچ‌وقت بهتر نیست.</item>
/// <item><b>متن صفحه</b>: فقط وقتی متن فعلی «کم» است (موتور متن فعلی را نگه می‌دارد و بخش‌های واقعی مشخصات/شرایط فروش را اضافه می‌کند).</item>
/// <item><b>بازنویسی</b> متن موجود (عنوان/توضیحات): فقط با دادهٔ واقعی (CTR ضعیف در سرچ کنسول)، با دورهٔ استراحت و سقف هفتگی؛
/// همیشه همراه «آزمایش عنوان» تا اگر بدتر شد خودکار برگردد.</item>
/// <item><b>نامک</b>: هرگز (تغییر آدرس صفحهٔ ایندکس‌شده ریسک دارد؛ نامک انگلیسی هنگام ذخیره ساخته می‌شود).</item>
/// <item>کلمهٔ کلیدی موجود: هرگز عوض نمی‌شود (انتخاب انسان یا انتخاب قبلی سیستم که داده رویش جمع شده).</item>
/// </list>
/// </summary>
public static class AutoApprovePolicy
{
    /// <summary>بخش‌هایی که بدون هیچ داده‌ای مجاز به تأییدند (فقط پر کردن جاهای خالی).</summary>
    public static SeoFields Fill(SeoFields proposed, FieldState state)
    {
        var fields = SeoFields.None;
        if (state.TitleEmpty) fields |= SeoFields.Title;
        if (state.MetaEmpty) fields |= SeoFields.MetaDescription;
        if (state.FocusEmpty) fields |= SeoFields.FocusKeyword;
        if (state.FaqEmpty) fields |= SeoFields.Faq;
        if (state.AllAltsEmpty) fields |= SeoFields.ImageAlt;
        if (state.BodyThin) fields |= SeoFields.Description;
        return fields & proposed & ~SeoFields.Slug;
    }

    /// <summary>
    /// بخش‌های «بازنویسی» (عنوان/توضیحات موجود). <paramref name="dataSaysRewrite"/> = CtrModel گفته عملکرد ضعیف است و
    /// دورهٔ استراحت و سقف هفتگی اجازه می‌دهند.
    /// </summary>
    public static SeoFields Rewrite(SeoFields proposed, FieldState state, bool dataSaysRewrite)
    {
        if (!dataSaysRewrite)
            return SeoFields.None;
        var fields = SeoFields.None;
        if (!state.TitleEmpty) fields |= SeoFields.Title;
        if (!state.MetaEmpty) fields |= SeoFields.MetaDescription;
        return fields & proposed;
    }
}
