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
///   "DefaultAdmins": [
///     { "PhoneNumber": "09013181090", "FirstName": "محمدامین", "LastName": "ناظری", "Password": "12345678" }
///   ],
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
    /// مدیران پیش‌فرض (هر تعداد). هر مورد در اولین اجرا با رمز اولیه‌اش ساخته می‌شود؛ قانون‌ها و مثال کامل:
    /// <see cref="Core.Services.Admin.DefaultAdminAccount"/> و docs/DEPLOYMENT.md.
    /// <para>رمز را روی سرور واقعی در فایل نگذارید؛ با متغیر محیطی بدهید: <c>Shop__DefaultAdmins__0__Password</c>
    /// (عدد = جایگاه در فهرست، از ۰). مدیر دوم: <c>Shop__DefaultAdmins__1__PhoneNumber</c>، <c>…__1__Password</c> و …</para>
    /// <para>نکته: فهرست‌ها در appsettings.{Environment}.json جایگاه‌به‌جایگاه با appsettings.json ادغام می‌شوند (حذف نمی‌شوند)؛
    /// برای حذف یک مدیر پیش‌فرض او را از appsettings.json بردارید و حسابش را از پنل مسدود یا حذف کنید.</para>
    /// </summary>
    public List<DefaultAdminOptions> DefaultAdmins { get; set; } = [];

    /// <summary>فهرست تنظیمات به مدل Core (موارد خالی حذف می‌شوند).</summary>
    public IReadOnlyList<Core.Services.Admin.DefaultAdminAccount> DefaultAdminAccounts()
        => DefaultAdmins.Where(a => a is not null && !string.IsNullOrWhiteSpace(a.PhoneNumber))
            .Select(a => new Core.Services.Admin.DefaultAdminAccount(a.PhoneNumber, a.FirstName, a.LastName, a.Password, a.FullAccess, a.Roles))
            .ToList();

    /// <summary>
    /// دامنهٔ اصلی سایت بدون https و بدون www. مثال: <c>faratek.ir</c>.
    /// <para>وقتی تنظیم باشد: درخواست به دامنهٔ دیگر (مثل www.faratek.ir) با ۳۰۱ به همین دامنه می‌رود و نقشهٔ سایت،
    /// robots.txt و لینک‌های کانونیکال همیشه با همین دامنه ساخته می‌شوند (جلوگیری از محتوای تکراری در گوگل).
    /// localhost و آدرس IP هرگز ریدایرکت نمی‌شوند تا آزمایش روی سرور ممکن بماند. خالی یا «off» (هر مقدار بدون نقطه) = خاموش.</para>
    /// </summary>
    public string? CanonicalHost { get; set; }
}

/// <summary>
/// یک مدیر پیش‌فرض در appsettings (<c>Shop:DefaultAdmins[n]</c>). معنی هر فیلد: <see cref="Core.Services.Admin.DefaultAdminAccount"/>.
/// </summary>
public sealed class DefaultAdminOptions
{
    /// <summary>موبایل، مثال «09013181090».</summary>
    public string? PhoneNumber { get; set; }

    /// <summary>نام کوچک، مثال «محمدامین».</summary>
    public string? FirstName { get; set; }

    /// <summary>نام خانوادگی، مثال «ناظری».</summary>
    public string? LastName { get; set; }

    /// <summary>رمز اولیه (۸ تا ۱۰۰ نویسه). فقط هنگام ساخت حساب (یا حساب بی‌رمز) استفاده می‌شود؛ بعد از اولین ورود عوضش کنید.</summary>
    public string? Password { get; set; }

    /// <summary>true (پیش‌فرض) = همهٔ دسترسی‌ها؛ false = فقط <see cref="Roles"/>.</summary>
    public bool FullAccess { get; set; } = true;

    /// <summary>نام نقش‌های پنل وقتی FullAccess = false. مثال: [ "پشتیبانی", "بازاریابی" ].</summary>
    public List<string> Roles { get; set; } = [];

    /// <summary>رمز در لاگ/دیباگر چاپ نشود.</summary>
    public override string ToString() => $"DefaultAdmin {PhoneNumber} (password {(string.IsNullOrEmpty(Password) ? "none" : "***")})";
}
