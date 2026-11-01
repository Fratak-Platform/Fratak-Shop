namespace Fratak_Shop.Core.Services.Admin;

/// <summary>
/// یک «مدیر پیش‌فرض» از تنظیمات سرور (بخش <c>Shop:DefaultAdmins</c> در appsettings یا متغیرهای محیطی).
/// <para>هدف: صاحب سرور هر تعداد مدیر (۱، ۲، ۵ …) را با نام، موبایل و رمز اولیه تعریف کند و در اولین اجرا
/// حسابشان با همان رمز ساخته شود. استفاده: Program.cs ← <see cref="IAdminAccessService.EnsureDefaultsAsync"/>.</para>
/// <para>قانون‌های امنیتی (در AdminAccessService):</para>
/// <list type="bullet">
/// <item>حساب فقط وقتی ساخته می‌شود که هیچ‌وقت (حتی حذف‌شده) حسابی با آن شماره نبوده؛ پس حذف از پنل همیشگی است.</item>
/// <item>رمز موجود هرگز بازنویسی نمی‌شود؛ فقط حسابی که اصلاً رمز ندارد یک بار رمز اولیه می‌گیرد.</item>
/// <item>رمز با Argon2id ذخیره می‌شود و هرگز در لاگ نوشته نمی‌شود؛ تا عوض نشده، صفحهٔ «سلامت سیستم» هشدار می‌دهد
/// و بعد از ورود با آن، کاربر به صفحهٔ تغییر رمز فرستاده می‌شود.</item>
/// <item>حساب مسدودشده دست نمی‌خورد (نه ارتقا، نه رمز).</item>
/// </list>
/// <para>مثال appsettings.json:</para>
/// <code>
/// "DefaultAdmins": [
///   { "PhoneNumber": "09013181090", "FirstName": "محمدامین", "LastName": "ناظری", "Password": "12345678" },
///   { "PhoneNumber": "09121111111", "FirstName": "سارا", "LastName": "احمدی", "Password": "...", "FullAccess": false, "Roles": [ "پشتیبانی" ] }
/// ]
/// </code>
/// </summary>
/// <param name="PhoneNumber">موبایل (۰۹… ، ‎+98… یا با رقم فارسی). نامعتبر ← این مورد نادیده گرفته می‌شود.</param>
/// <param name="FirstName">نام کوچک (حداکثر ۱۰۰ نویسه).</param>
/// <param name="LastName">نام خانوادگی (حداکثر ۱۰۰ نویسه).</param>
/// <param name="Password">رمز اولیه، ۸ تا ۱۰۰ نویسه؛ خالی = بدون رمز (ورود با کد پیامکی).</param>
/// <param name="FullAccess">true (پیش‌فرض) = مالک با همهٔ دسترسی‌ها؛ false = فقط نقش‌های <paramref name="Roles"/>.</param>
/// <param name="Roles">نام نقش‌های پنل (مثل «پشتیبانی»)، فقط وقتی FullAccess = false.</param>
public sealed record DefaultAdminAccount(
    string? PhoneNumber,
    string? FirstName = null,
    string? LastName = null,
    string? Password = null,
    bool FullAccess = true,
    IReadOnlyList<string>? Roles = null)
{
    /// <summary>کوتاه‌ترین رمز اولیهٔ مجاز (هم‌اندازهٔ قانون رمز در پروفایل یا سخت‌گیرتر).</summary>
    public const int MinPasswordLength = 8;

    /// <summary>بلندترین رمز مجاز (Argon2 روی رمزهای خیلی بلند وقت و حافظه هدر می‌دهد).</summary>
    public const int MaxPasswordLength = 100;

    /// <summary>
    /// رمز اولیه معتبر است؟ مثال: «12345678» ← true؛ «1234» ← false؛ «   » ← false (یعنی بدون رمز).
    /// </summary>
    public static bool IsValidPassword(string? password)
        => !string.IsNullOrWhiteSpace(password) && password.Length is >= MinPasswordLength and <= MaxPasswordLength;

    /// <summary>متن امن برای لاگ: فقط شماره با وسط پوشیده، هرگز رمز. مثال: «0901***1090».</summary>
    public static string Mask(string phone) => phone.Length == 11 ? phone[..4] + "***" + phone[7..] : "***";

    /// <summary>رمز در ToString پیش‌فرض رکورد چاپ نشود (لاگ تصادفی یا پیام خطا).</summary>
    public override string ToString()
        => $"DefaultAdminAccount {{ PhoneNumber = {PhoneNumber}, FullAccess = {FullAccess}, Password = {(string.IsNullOrEmpty(Password) ? "(none)" : "***")} }}";
}
