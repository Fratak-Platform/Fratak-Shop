namespace Fratak_Shop.Web.Infrastructure;

/// <summary>
/// Deployment-level switches (section <c>Shop</c>). Everything a store manager may want to
/// change – store name, contact details, shipping, payment, wallet, SEO – lives in the
/// database (<c>StoreSetting</c> and friends) so it can be edited from the admin panel.
/// <para>نمونهٔ appsettings.json:</para>
/// <code>
/// "Shop": {
///   "SeedDefaultData": true,     // دادهٔ پیش‌فرض سیستم، فقط روی پایگاه دادهٔ کاملاً تازه (یک بار برای همیشه)
///   "SeedDemoData": false,       // محصولات، دسته‌ها، کاربران و کدهای تخفیف ساختگی
///   "OwnerPhoneNumber": "09013181090",
///   "OwnerFirstName": "محمدامین",
///   "OwnerLastName": "ناظری",
///   "CanonicalHost": "faratek.ir"
/// }
/// </code>
/// </summary>
public class ShopOptions
{
    public const string SectionName = "Shop";

    /// <summary>
    /// دادهٔ پیش‌فرض سیستم (نام فروشگاه، تنظیمات، صفحه‌ها، پرسش‌های متداول، منوها، قالب‌های اعلان، پیشنهادهای جست‌وجو و …).
    /// <para>true: فقط اولین بار که پایگاه داده کاملاً خالی است ساخته می‌شود؛ اجراهای بعدی هرگز چیزی را دوباره نمی‌سازند
    /// (حتی اگر مدیر چیزی را حذف کرده باشد). false: هیچ‌وقت ساخته نمی‌شود (مثلاً وقتی پشتیبان یک پایگاه دادهٔ قدیمی را برمی‌گردانید).
    /// جزئیات: Data/Seeders/DefaultDataSeeder.cs. پیش‌فرض: true.</para>
    /// </summary>
    public bool SeedDefaultData { get; set; } = true;

    /// <summary>
    /// Seeds demo catalog data (categories, brands, products, banners, posts, demo customer and staff
    /// accounts, sample coupons, ...). Only runs while the catalog is still empty, so it never mixes
    /// fake products into a real shop. Never enable on the live site: demo accounts use the password 123456.
    /// </summary>
    public bool SeedDemoData { get; set; }

    /// <summary>
    /// Mobile number of the store owner (full access to the admin panel). On the first run the owner
    /// account is created without a password (sign-in with an SMS code; a password can be set later in
    /// the profile). If the account already exists it is promoted to owner.
    /// </summary>
    public string? OwnerPhoneNumber { get; set; }

    /// <summary>نام کوچک مالک برای ساخت حساب در اولین اجرا. مثال: «محمدامین».</summary>
    public string? OwnerFirstName { get; set; }

    /// <summary>نام خانوادگی مالک برای ساخت حساب در اولین اجرا. مثال: «ناظری».</summary>
    public string? OwnerLastName { get; set; }

    /// <summary>
    /// دامنهٔ اصلی سایت بدون https و بدون www. مثال: <c>faratek.ir</c>.
    /// <para>وقتی تنظیم باشد: درخواست به دامنهٔ دیگر (مثل www.faratek.ir) با ۳۰۱ به همین دامنه می‌رود و نقشهٔ سایت،
    /// robots.txt و لینک‌های کانونیکال همیشه با همین دامنه ساخته می‌شوند (جلوگیری از محتوای تکراری در گوگل).
    /// localhost و آدرس IP هرگز ریدایرکت نمی‌شوند تا آزمایش روی سرور ممکن بماند. خالی = خاموش.</para>
    /// </summary>
    public string? CanonicalHost { get; set; }
}
