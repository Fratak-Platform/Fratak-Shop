using System.Globalization;

namespace Fratak_Shop.Core.Services.Design;

/// <summary>یک قالب آمادهٔ صفحه‌ساز: HTML و CSS اولیه با دادهٔ نمونه (مدیر روی بوم عوضش می‌کند).</summary>
public sealed record LandingTemplate(string Key, string Title, string Description, string Html, string Css);

/// <summary>
/// قالب‌های پیش‌فرض صفحه‌ساز کمپین (دور ۲۱): «حراج / کمپین»، «معرفی کالای تازه»، «مناسبتی (هدیه)» و «خالی».
/// <para>همه با کلاس‌های ثابت <c>fs-lp-*</c> (فایل wwwroot/assets/css/landing.css که هم در صفحهٔ عمومی و هم داخل بوم صفحه‌ساز بار می‌شود)
/// ساخته شده‌اند تا بدون CSS سفارشی هم در موبایل، تبلت و دسکتاپ درست دیده شوند. کالاها با کدهای کوتاه (<c>[[deals:8]]</c> …) می‌آیند
/// تا قیمت و موجودی همیشه «امروز» باشد، نه عددی که روز ساخت صفحه نوشته شده.</para>
/// <para>شمارش معکوس فقط وقتی معنا دارد که کمپین واقعاً پایان دارد؛ قالب تاریخ پایان را از «پایان کمپین» فرم می‌گیرد و اگر خالی باشد
/// بخش شمارش معکوس را نمی‌گذارد (بدون فوریت جعلی).</para>
/// <para>محل استفاده: صفحهٔ پنل Campaigns/Index (ساخت صفحه از قالب) و Campaigns/Edit (دکمهٔ «قالب‌ها»).</para>
/// </summary>
public static class LandingTemplates
{
    public static IReadOnlyList<(string Key, string Title, string Description)> List { get; } =
    [
        ("campaign", "حراج / کمپین تخفیف", "سربرگ بزرگ، شمارش معکوس تا پایان کمپین، شبکهٔ پرتخفیف‌ها، مزیت‌ها و پرسش‌های رایج"),
        ("launch", "معرفی کالا یا مجموعهٔ تازه", "تیتر و توضیح، سه ویژگی کلیدی، تازه‌ترین کالاها و دکمهٔ خرید"),
        ("gift", "مناسبتی و هدیه", "پیام مناسبت، راهنمای انتخاب هدیه، پرفروش‌ها و ارسال"),
        ("blank", "صفحهٔ خالی", "فقط یک بخش ساده؛ همه چیز را خودتان بچینید"),
    ];

    /// <summary>
    /// ساخت قالب. <paramref name="endsAt"/> = پایان کمپین (برای شمارش معکوس؛ null ← بدون شمارش معکوس).
    /// مثال: Build("campaign", "فراتک شاپ", 2026-11-30) ← صفحهٔ حراج با شمارش معکوس تا همان تاریخ.
    /// </summary>
    public static LandingTemplate Build(string? key, string siteName, DateTimeOffset? endsAt)
    {
        var site = System.Net.WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(siteName) ? "فروشگاه" : siteName.Trim());
        var countdown = endsAt is { } end
            ? $"<div class=\"fs-lp-countdown\" data-fs-countdown=\"{end.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture)}\">" +
              "<div class=\"fs-lp-countdown__cell\"><b data-u=\"d\">۰</b><span>روز</span></div>" +
              "<div class=\"fs-lp-countdown__cell\"><b data-u=\"h\">۰</b><span>ساعت</span></div>" +
              "<div class=\"fs-lp-countdown__cell\"><b data-u=\"m\">۰</b><span>دقیقه</span></div>" +
              "<div class=\"fs-lp-countdown__cell\"><b data-u=\"s\">۰</b><span>ثانیه</span></div></div>"
            : "";
        return key switch
        {
            "campaign" => new LandingTemplate("campaign", "حراج / کمپین تخفیف", "",
                $"""
                <section class="fs-lp-hero fs-lp-hero--sale">
                  <div class="fs-lp-wrap">
                    <span class="fs-lp-kicker">کمپین ویژهٔ {site}</span>
                    <h1 class="fs-lp-title">حراج بزرگ این هفته</h1>
                    <p class="fs-lp-lead">تخفیف واقعی روی کالاهای منتخب؛ قیمت‌ها و موجودی همین حالا از فروشگاه خوانده می‌شود.</p>
                    {countdown}
                    <a class="fs-lp-btn" href="/products?Discounted=true&amp;Sort=MostDiscounted">دیدن همهٔ تخفیف‌ها</a>
                  </div>
                </section>
                <section class="fs-lp-section">
                  <div class="fs-lp-wrap">
                    <h2 class="fs-lp-h2">پرتخفیف‌ترین‌ها</h2>
                    <div class="fs-lp-products" data-fs-code="deals:8">[[deals:8]]</div>
                  </div>
                </section>
                <section class="fs-lp-section fs-lp-section--soft">
                  <div class="fs-lp-wrap fs-lp-features">
                    <div class="fs-lp-feature"><b>ضمانت اصالت</b><p>همهٔ کالاها اصل و با ضمانت معتبر.</p></div>
                    <div class="fs-lp-feature"><b>ارسال سریع</b><p>بسته‌بندی امن و ارسال به سراسر کشور.</p></div>
                    <div class="fs-lp-feature"><b>پشتیبانی واقعی</b><p>پیش و پس از خرید پاسخگوی شما هستیم.</p></div>
                  </div>
                </section>
                <section class="fs-lp-section">
                  <div class="fs-lp-wrap">
                    <h2 class="fs-lp-h2">پرسش‌های رایج</h2>
                    <details class="fs-lp-faq"><summary>تخفیف‌ها تا کی معتبر است؟</summary><p>تا پایان کمپین یا تمام شدن موجودی هر کالا؛ قیمت نهایی همیشه در صفحهٔ کالا نوشته شده است.</p></details>
                    <details class="fs-lp-faq"><summary>امکان مرجوع کردن کالای تخفیف‌دار هست؟</summary><p>بله، طبق همان شرایط عادی بازگشت کالا.</p></details>
                  </div>
                </section>
                """, ""),
            "launch" => new LandingTemplate("launch", "معرفی کالا یا مجموعهٔ تازه", "",
                $"""
                <section class="fs-lp-hero fs-lp-hero--launch">
                  <div class="fs-lp-wrap">
                    <span class="fs-lp-kicker">تازه در {site}</span>
                    <h1 class="fs-lp-title">مجموعهٔ جدید رسید</h1>
                    <p class="fs-lp-lead">یک جملهٔ کوتاه که بگوید این مجموعه چه مشکلی از خریدار حل می‌کند.</p>
                    <a class="fs-lp-btn" href="/products?Sort=Newest">مشاهدهٔ کالاها</a>
                  </div>
                </section>
                <section class="fs-lp-section">
                  <div class="fs-lp-wrap fs-lp-features">
                    <div class="fs-lp-feature"><b>ویژگی اول</b><p>مهم‌ترین مزیت برای خریدار را این‌جا بنویسید.</p></div>
                    <div class="fs-lp-feature"><b>ویژگی دوم</b><p>کیفیت، دوام یا امکان خاص.</p></div>
                    <div class="fs-lp-feature"><b>ویژگی سوم</b><p>گارانتی، ارسال یا خدمات پس از فروش.</p></div>
                  </div>
                </section>
                <section class="fs-lp-section fs-lp-section--soft">
                  <div class="fs-lp-wrap">
                    <h2 class="fs-lp-h2">تازه‌ترین کالاها</h2>
                    <div class="fs-lp-products" data-fs-code="newest:8">[[newest:8]]</div>
                  </div>
                </section>
                """, ""),
            "gift" => new LandingTemplate("gift", "مناسبتی و هدیه", "",
                $"""
                <section class="fs-lp-hero fs-lp-hero--gift">
                  <div class="fs-lp-wrap">
                    <span class="fs-lp-kicker">{site}</span>
                    <h1 class="fs-lp-title">هدیه‌ای که به کار بیاید</h1>
                    <p class="fs-lp-lead">برای عزیزانتان کالایی انتخاب کنید که هر روز از آن استفاده کنند.</p>
                    {countdown}
                    <a class="fs-lp-btn" href="/products?Sort=BestSelling">پرفروش‌ترین‌ها</a>
                  </div>
                </section>
                <section class="fs-lp-section">
                  <div class="fs-lp-wrap fs-lp-features">
                    <div class="fs-lp-feature"><b>برای علاقه‌مند به فناوری</b><p>هدفون، ساعت هوشمند و لوازم جانبی کاربردی.</p></div>
                    <div class="fs-lp-feature"><b>برای خانه</b><p>کالاهایی که زندگی روزمره را راحت‌تر می‌کنند.</p></div>
                    <div class="fs-lp-feature"><b>با هر بودجه</b><p>از هدیه‌های کوچک تا خریدهای ماندگار.</p></div>
                  </div>
                </section>
                <section class="fs-lp-section fs-lp-section--soft">
                  <div class="fs-lp-wrap">
                    <h2 class="fs-lp-h2">پرفروش‌ها</h2>
                    <div class="fs-lp-products" data-fs-code="bestsellers:8">[[bestsellers:8]]</div>
                  </div>
                </section>
                """, ""),
            _ => new LandingTemplate("blank", "صفحهٔ خالی", "",
                """
                <section class="fs-lp-section">
                  <div class="fs-lp-wrap">
                    <h1 class="fs-lp-title fs-lp-title--dark">عنوان صفحه</h1>
                    <p class="fs-lp-lead fs-lp-lead--dark">از ستون بلوک‌ها، بخش‌ها را بکشید و این‌جا رها کنید.</p>
                  </div>
                </section>
                """, ""),
        };
    }
}
