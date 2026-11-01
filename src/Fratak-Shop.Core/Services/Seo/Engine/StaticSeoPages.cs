using Fratak_Shop.Core.DTOs.Settings;
using Fratak_Shop.Data.Entities.Pages;

namespace Fratak_Shop.Core.Services.Seo.Engine;

/// <summary>
/// یک صفحهٔ ثابت قابل سئو (مرحلهٔ شانزدهم). Role نقش صفحه برای نویسنده است («home»، «blog» و ...).
/// Research = آیا برای کلمهٔ کلیدی این صفحه تحقیق اینترنتی ارزش دارد (فقط صفحهٔ اصلی؛ «وبلاگ» یا «تماس با ما» جست‌وجوی تجاری ندارند).
/// </summary>
public sealed record StaticSeoPage(SitePage Page, string Path, string Role, bool Research);

/// <summary>
/// فهرست صفحه‌های ثابت سایت که موتور سئو خودش پیدا و سئو می‌کند: صفحهٔ اصلی، همهٔ محصولات، وبلاگ، سؤالات متداول،
/// تماس با ما و باشگاه مشتریان. صفحه‌های شخصی/تراکنشی (سبد خرید، ورود، پروفایل …) عمداً نیستند چون همیشه noindex‌اند.
/// <para>«درباره ما» و بقیهٔ صفحه‌های محتوایی، صفحهٔ محتوا (ContentPage) هستند و جداگانه سئو می‌شوند.</para>
/// <para>شناسهٔ هر صفحه از عدد SitePage ساخته می‌شود تا در جدول نمره‌ها و پیشنهادها ثابت بماند. مثال: صفحهٔ اصلی (۱) →
/// 5e0a0000-0000-0000-0000-000000000001. سئوی تأییدشده در جدول PageSeo همان صفحه ذخیره می‌شود (همان که PageMetaService می‌خواند).</para>
/// عنوان و توضیح پیش‌فرض باید با همان مقداری که صفحهٔ Razor به PageMetaService.ApplyAsync می‌دهد یکی باشد
/// (Pages/Index، Search، Blog، Faq، ContactUs، Club) تا نمرهٔ «قبل» دقیقاً همان چیزی باشد که گوگل می‌بیند.
/// </summary>
public static class StaticSeoPages
{
    public static readonly IReadOnlyList<StaticSeoPage> All =
    [
        new(SitePage.Home, "/", "home", true),
        new(SitePage.Products, "/search", "products", false),
        new(SitePage.Blog, "/blog", "blog", false),
        new(SitePage.Faq, "/faq", "faq", false),
        new(SitePage.ContactUs, "/contact-us", "contact", false),
        new(SitePage.CustomerClub, "/club", "club", false),
    ];

    /// <summary>شناسهٔ ثابت صفحه. مثال: SitePage.Blog (۶) → 5e0a0000-0000-0000-0000-000000000006.</summary>
    public static Guid IdOf(SitePage page) => Guid.Parse($"5e0a0000-0000-0000-0000-{(int)page:D12}");

    public static StaticSeoPage? Find(Guid id) => All.FirstOrDefault(p => IdOf(p.Page) == id);

    public static StaticSeoPage? Find(SitePage page) => All.FirstOrDefault(p => p.Page == page);

    /// <summary>
    /// نام (موضوع صفحه برای کلمهٔ کلیدی)، عنوان پیش‌فرض و توضیح پیش‌فرض. مثال برای وبلاگ:
    /// («وبلاگ فراتک»، «وبلاگ»، «جدیدترین مقالات، راهنمای خرید و اخبار دنیای تکنولوژی.»).
    /// </summary>
    public static (string Name, string Title, string Description) Defaults(StaticSeoPage page, StoreSettingDto s)
    {
        var site = PersianText.Clean(s.SiteName);
        return page.Role switch
        {
            "home" => (
                PersianText.Clean(s.Tagline) is { Length: > 0 } tagline ? tagline : ("فروشگاه اینترنتی " + site).Trim(),
                s.Tagline ?? "فروشگاه اینترنتی",
                s.SiteDescription ?? $"{s.SiteName}؛ خرید آنلاین با بهترین قیمت، ارسال سریع و ضمانت اصالت کالا."),
            "products" => ("محصولات " + site, "همه محصولات", "لیست محصولات فروشگاه با امکان فیلتر بر اساس دسته بندی، برند و قیمت."),
            "blog" => (("وبلاگ " + site).Trim(), "وبلاگ", "جدیدترین مقالات، راهنمای خرید و اخبار دنیای تکنولوژی."),
            "faq" => ("سوالات متداول", "سوالات متداول", "پاسخ پرسش های رایج درباره ثبت سفارش، ارسال، پرداخت، کیف پول و مرجوعی کالا."),
            "contact" => ("تماس با " + site, "تماس با ما", "راه های ارتباط با پشتیبانی فروشگاه و ارسال پیام."),
            "club" => (s.LoyaltyName, s.LoyaltyName,
                s.LoyaltyProgramDescription ?? $"با هر خرید، ثبت دیدگاه و معرفی دوستان در {s.LoyaltyName} {s.SiteName} امتیاز بگیرید."),
            _ => (page.Path, page.Path, ""),
        };
    }
}
