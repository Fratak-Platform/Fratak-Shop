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
///   "DefaultAdminPassword": "12345678",
///   "DefaultAdmins": [
///     { "Key": "owner", "PhoneNumber": "09013181090", "FirstName": "محمدامین", "LastName": "ناظری" }
///   ],
///   "CanonicalHost": "shop.fratak.ir"
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
    /// دور ۲۳: پیامک هشدار فنی به مدیران اصلی (جهش خطا، قطع پایگاه داده، خاموشی ناگهانی). هر نوع حداکثر یک بار در ۲ ساعت و
    /// کل هشدارها حداکثر ۶ پیامک در روز. جزئیات: Infrastructure/Ops/OpsAlerts.cs. پیش‌فرض: روشن. متغیر محیطی: <c>Shop__OpsAlertSms</c>.
    /// </summary>
    public bool OpsAlertSms { get; set; } = true;

    /// <summary>
    /// Seeds demo catalog data (categories, brands, products, banners, posts, demo customer and staff
    /// accounts, sample coupons, ...). Only runs while the catalog is still empty, so it never mixes
    /// fake products into a real shop. Never enable on the live site: demo accounts use the password 123456.
    /// </summary>
    public bool SeedDemoData { get; set; }

    /// <summary>
    /// مدیران اصلی (هر تعداد؛ مثلاً مالک و شریک). در <b>هر</b> اجرای برنامه بررسی می‌شوند: اگر حسابشان نیست ساخته می‌شود،
    /// اگر حذف یا مسدود شده برمی‌گردد و دسترسی‌شان تضمین می‌شود؛ ولی نام، شماره و رمزی که خودشان عوض کرده‌اند دست نمی‌خورد.
    /// قانون‌ها و مثال کامل: <see cref="Core.Services.Admin.DefaultAdminAccount"/> و docs/DEPLOYMENT.md.
    /// <para>متغیر محیطی: <c>Shop__DefaultAdmins__0__PhoneNumber</c>، <c>Shop__DefaultAdmins__1__Key</c> و … (عدد = جایگاه در فهرست، از ۰).</para>
    /// <para>نکته: فهرست‌ها در appsettings.{Environment}.json جایگاه‌به‌جایگاه با appsettings.json ادغام می‌شوند (حذف نمی‌شوند)؛
    /// برای کنار گذاشتن یک مدیر اصلی او را از appsettings.json بردارید؛ حسابش می‌ماند ولی از این به بعد مثل بقیهٔ کارکنان از پنل مدیریت می‌شود.</para>
    /// </summary>
    public List<DefaultAdminOptions> DefaultAdmins { get; set; } = [];

    /// <summary>
    /// رمز اولیهٔ مشترک مدیران اصلی که در فهرست رمز جداگانه ندارند (پیش‌فرض ۱۲۳۴۵۶۷۸). فقط هنگام ساخت حساب به کار می‌رود؛
    /// بعد از اولین ورود، سیستم خودش کاربر را به صفحهٔ تغییر رمز می‌برد. خالی = بدون رمز (ورود فقط با کد پیامکی).
    /// متغیر محیطی: <c>Shop__DefaultAdminPassword</c>.
    /// </summary>
    public string? DefaultAdminPassword { get; set; } = Core.Services.Admin.DefaultAdminAccount.DefaultInitialPassword;

    /// <summary>فهرست تنظیمات به مدل Core (موارد بدون شماره حذف می‌شوند؛ رمز خالی ← <see cref="DefaultAdminPassword"/>).</summary>
    public IReadOnlyList<Core.Services.Admin.DefaultAdminAccount> DefaultAdminAccounts()
        => DefaultAdmins.Where(a => a is not null && !string.IsNullOrWhiteSpace(a.PhoneNumber))
            .Select(a => new Core.Services.Admin.DefaultAdminAccount(a.PhoneNumber, a.FirstName, a.LastName,
                string.IsNullOrEmpty(a.Password) ? DefaultAdminPassword : a.Password, a.FullAccess, a.Roles, a.Key))
            .ToList();

    /// <summary>
    /// دامنهٔ اصلی سایت بدون https. مثال: <c>shop.fratak.ir</c> (دامنهٔ واقعی فروشگاه).
    /// <para>وقتی تنظیم باشد: نسخهٔ www همین دامنه (و دامنه‌های <see cref="CanonicalAliases"/>) با ۳۰۱ به همین دامنه می‌رود و نقشهٔ سایت،
    /// robots.txt و لینک‌های کانونیکال همیشه با همین دامنه ساخته می‌شوند (جلوگیری از محتوای تکراری در گوگل).
    /// localhost و آدرس IP هرگز ریدایرکت نمی‌شوند تا آزمایش روی سرور ممکن بماند. خالی یا «off» (هر مقدار بدون نقطه) = خاموش.</para>
    /// </summary>
    public string? CanonicalHost { get; set; }

    /// <summary>
    /// مرحلهٔ هفدهم: دامنه‌های دیگری که باید با ۳۰۱ به <see cref="CanonicalHost"/> بروند (مثلاً دامنهٔ قدیمی). نسخهٔ www دامنهٔ
    /// اصلی خودکار ریدایرکت می‌شود و لازم نیست اینجا بیاید. هر دامنهٔ دیگری که اینجا نباشد ریدایرکت نمی‌شود.
    /// مثال: <c>"CanonicalAliases": [ "old-shop.ir", "www.old-shop.ir" ]</c>؛ متغیر محیطی: <c>Shop__CanonicalAliases__0</c>.
    /// </summary>
    public List<string> CanonicalAliases { get; set; } = [];
}

/// <summary>
/// یک مدیر پیش‌فرض در appsettings (<c>Shop:DefaultAdmins[n]</c>). معنی هر فیلد: <see cref="Core.Services.Admin.DefaultAdminAccount"/>.
/// </summary>
public sealed class DefaultAdminOptions
{
    /// <summary>
    /// کلید ثابت اختیاری (لاتین، مثل «owner» یا «partner»). با آن، حساب در هر اجرا پیدا می‌شود حتی اگر شمارهٔ این فهرست را بعداً
    /// عوض کنید. خالی = شمارهٔ همین فهرست کلید است.
    /// </summary>
    public string? Key { get; set; }

    /// <summary>موبایل، مثال «09013181090».</summary>
    public string? PhoneNumber { get; set; }

    /// <summary>نام کوچک، مثال «محمدامین».</summary>
    public string? FirstName { get; set; }

    /// <summary>نام خانوادگی، مثال «ناظری».</summary>
    public string? LastName { get; set; }

    /// <summary>رمز اولیهٔ اختصاصی (۸ تا ۱۰۰ نویسه)؛ خالی = <see cref="ShopOptions.DefaultAdminPassword"/>. فقط هنگام ساخت حساب استفاده می‌شود.</summary>
    public string? Password { get; set; }

    /// <summary>true (پیش‌فرض) = همهٔ دسترسی‌ها؛ false = فقط <see cref="Roles"/>.</summary>
    public bool FullAccess { get; set; } = true;

    /// <summary>نام نقش‌های پنل وقتی FullAccess = false. مثال: [ "پشتیبانی", "بازاریابی" ].</summary>
    public List<string> Roles { get; set; } = [];

    /// <summary>رمز در لاگ/دیباگر چاپ نشود.</summary>
    public override string ToString() => $"DefaultAdmin {Key} {PhoneNumber} (password {(string.IsNullOrEmpty(Password) ? "none" : "***")})";
}
