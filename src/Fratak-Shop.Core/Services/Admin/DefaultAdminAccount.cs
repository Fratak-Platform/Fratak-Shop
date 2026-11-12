namespace Fratak_Shop.Core.Services.Admin;

/// <summary>
/// یک «مدیر اصلی» از تنظیمات سرور (بخش <c>Shop:DefaultAdmins</c> در appsettings یا متغیرهای محیطی)؛ مثلاً مالک و شریکش.
/// <para>هدف: صاحب سرور هر تعداد مدیر (۱، ۲، ۵ …) تعریف کند و این حساب‌ها <b>همیشه</b> وجود داشته باشند و دسترسی داشته باشند.
/// استفاده: Program.cs در <b>هر بار</b> اجرا ← <see cref="IAdminAccessService.EnsureDefaultsAsync"/>.</para>
/// <para>قانون‌ها (مرحلهٔ پانزدهم، در AdminAccessService):</para>
/// <list type="bullet">
/// <item>هر مدیر با یک کلید ثابت شناخته می‌شود (<see cref="StableKey"/>)، نه با شمارهٔ فعلی؛ پس اگر شماره، نام یا رمزش را
/// از پروفایل عوض کند، برنامه همان حساب را پیدا می‌کند و هیچ‌کدام را به مقدار اولیه برنمی‌گرداند.</item>
/// <item>اگر حسابی نباشد ساخته می‌شود (رمز اولیه: <see cref="Password"/>، پیش‌فرض <see cref="DefaultInitialPassword"/>).</item>
/// <item>حساب حذف‌شده برگردانده و حساب مسدود باز می‌شود؛ دسترسی مالک (یا نقش‌های تعیین‌شده) دوباره داده می‌شود.
/// در پنل هم حذف، مسدودسازی و برداشتن مالکیت این حساب‌ها ممنوع است.</item>
/// <item>رمز موجود هرگز بازنویسی نمی‌شود. رمز با Argon2id ذخیره می‌شود و هرگز در لاگ نمی‌آید؛ تا عوض نشده، صفحهٔ
/// «سلامت سیستم» هشدار می‌دهد و بعد از ورود با آن، کاربر به صفحهٔ تغییر رمز فرستاده می‌شود.</item>
/// </list>
/// <para>مثال appsettings.json:</para>
/// <code>
/// "DefaultAdminPassword": "12345678",
/// "DefaultAdmins": [
///   { "Key": "owner",   "PhoneNumber": "09013181090", "FirstName": "محمدامین", "LastName": "ناظری" },
///   { "Key": "partner", "PhoneNumber": "09121111111", "FirstName": "علی", "LastName": "احمدی" },
///   { "PhoneNumber": "09122222222", "FullAccess": false, "Roles": [ "پشتیبانی" ] }
/// ]
/// </code>
/// </summary>
/// <param name="PhoneNumber">موبایل اولیه (۰۹… ، ‎+98… یا با رقم فارسی). نامعتبر ← این مورد نادیده گرفته می‌شود.</param>
/// <param name="FirstName">نام کوچک برای ساخت حساب (حداکثر ۱۰۰ نویسه).</param>
/// <param name="LastName">نام خانوادگی برای ساخت حساب (حداکثر ۱۰۰ نویسه).</param>
/// <param name="Password">رمز اولیه، ۸ تا ۱۰۰ نویسه؛ خالی = بدون رمز (ورود با کد پیامکی). ShopOptions خالی را با ۱۲۳۴۵۶۷۸ پر می‌کند.</param>
/// <param name="FullAccess">true (پیش‌فرض) = مالک با همهٔ دسترسی‌ها؛ false = فقط نقش‌های <paramref name="Roles"/>.</param>
/// <param name="Roles">نام نقش‌های پنل (مثل «پشتیبانی»)، فقط وقتی FullAccess = false.</param>
/// <param name="Key">کلید ثابت اختیاری (حروف/رقم لاتین، - _ .، حداکثر ۵۰). خالی ← از شمارهٔ اولیه ساخته می‌شود.
/// توصیه: برای هر مدیر یک Key بگذارید تا بعداً بتوانید شمارهٔ داخل تنظیمات را هم عوض کنید بی‌آنکه حساب تازه ساخته شود.</param>
public sealed record DefaultAdminAccount(
    string? PhoneNumber,
    string? FirstName = null,
    string? LastName = null,
    string? Password = null,
    bool FullAccess = true,
    IReadOnlyList<string>? Roles = null,
    string? Key = null)
{
    /// <summary>رمز اولیهٔ همهٔ مدیران اصلی وقتی در تنظیمات رمزی نیامده (بعد از اولین ورود حتماً عوض شود).</summary>
    public const string DefaultInitialPassword = "12345678";

    /// <summary>بلندترین Key مجاز در تنظیمات.</summary>
    public const int MaxKeyLength = 50;

    /// <summary>
    /// کلید ثابت این مدیر در پایگاه داده (ستون Users.SystemAdminKey).
    /// مثال: Key = «Owner» ← «key:owner»؛ بدون Key و شمارهٔ «۰۹۰۱۳۱۸۱۰۹۰» ← «phone:09013181090»؛
    /// Key نامعتبر (مثلاً «مالک» یا بیش از ۵۰ نویسه) یا شمارهٔ نامعتبر ← null (این مورد نادیده گرفته می‌شود).
    /// </summary>
    public string? StableKey()
    {
        if (!string.IsNullOrWhiteSpace(Key))
        {
            var key = Key.Trim();
            return key.Length <= MaxKeyLength && key.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.')
                ? "key:" + key.ToLowerInvariant()
                : null;
        }
        var phone = AdminAccessService.NormalizeOwnerPhone(PhoneNumber);
        return phone is null ? null : "phone:" + phone;
    }

    /// <summary>
    /// کلید پشتیبان بر اساس شمارهٔ تنظیمات (بدون توجه به Key). مثال: «0991 318 1090» ← «phone:09913181090»؛ شمارهٔ نامعتبر ← null.
    /// <para>استفاده: وقتی Key تکراری یا نامعتبر است (<see cref="Resolve"/>)، این مدیر با شماره‌اش شناخته می‌شود تا نادیده نماند.</para>
    /// </summary>
    public string? PhoneKey()
    {
        var phone = AdminAccessService.NormalizeOwnerPhone(PhoneNumber);
        return phone is null ? null : "phone:" + phone;
    }

    /// <summary>
    /// مرحلهٔ هفدهم: کلید نهایی هر مدیر فهرست تنظیمات، با رفع تداخل‌ها؛ یک منبع واحد برای AdminAccessService و
    /// DefaultAdminPasswords (تا هر دو یک حساب را با یک کلید ببینند).
    /// <para>قانون‌ها به ترتیب:</para>
    /// <list type="bullet">
    /// <item>Key معتبر و یکتا ← «key:…».</item>
    /// <item>Key تکراری (مثلاً دو مدیر هر دو با «owner») یا نامعتبر (مثلاً «مالک») ← به‌جای نادیده گرفتن، کلید شماره
    /// («phone:…») با یک هشدار؛ پس مدیر دوم هم ساخته می‌شود و دسترسی می‌گیرد.</item>
    /// <item>شمارهٔ نامعتبر یا کلید شمارهٔ تکراری (یک شماره دو بار در فهرست) ← <see cref="ResolvedDefaultAdmin.Key"/> = null
    /// و یک یادداشت «نادیده».</item>
    /// </list>
    /// <para>مثال: [owner/09913181090، owner/09134426156] ← [«key:owner»، «phone:09134426156» + هشدار «Key تکراری»].</para>
    /// </summary>
    public static IReadOnlyList<ResolvedDefaultAdmin> Resolve(IEnumerable<DefaultAdminAccount?>? admins)
    {
        var used = new HashSet<string>(StringComparer.Ordinal);
        var phones = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<ResolvedDefaultAdmin>();
        foreach (var admin in admins ?? [])
        {
            if (admin is null)
                continue;
            var phone = AdminAccessService.NormalizeOwnerPhone(admin.PhoneNumber);
            if (phone is null)
            {
                result.Add(new ResolvedDefaultAdmin(admin, null, "نادیده (شمارهٔ موبایل نامعتبر در Shop:DefaultAdmins)."));
                continue;
            }

            var masked = Mask(phone);
            if (!phones.Add(phone))
            {
                // یک شماره دو بار در فهرست (مثلاً «09134426156» و «0913 442 6156»): مورد دوم یک حساب دیگر نمی‌سازد.
                result.Add(new ResolvedDefaultAdmin(admin, null, $"نادیده {masked}: این شماره دو بار در Shop:DefaultAdmins آمده است؛ مورد تکراری را حذف کنید."));
                continue;
            }

            var key = admin.StableKey();
            if (key is not null && used.Add(key))
            {
                result.Add(new ResolvedDefaultAdmin(admin, key, null));
                continue;
            }

            var reason = key is null
                ? $"Key «{admin.Key?.Trim()}» نامعتبر است (فقط حروف و رقم لاتین، - _ . و حداکثر {MaxKeyLength} نویسه)"
                : $"Key «{admin.Key?.Trim() ?? phone}» در Shop:DefaultAdmins تکراری است";
            var fallback = admin.PhoneKey();
            if (fallback is not null && fallback != key && used.Add(fallback))
                result.Add(new ResolvedDefaultAdmin(admin, fallback,
                    $"هشدار {masked}: {reason}؛ این مدیر با شماره‌اش شناخته شد. برای هر مدیر یک Key جدا (مثل owner و partner) بگذارید."));
            else
                result.Add(new ResolvedDefaultAdmin(admin, null, $"نادیده {masked}: {reason} و این شماره هم دو بار در فهرست آمده است."));
        }

        return result;
    }

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
        => $"DefaultAdminAccount {{ Key = {Key}, PhoneNumber = {PhoneNumber}, FullAccess = {FullAccess}, Password = {(string.IsNullOrEmpty(Password) ? "(none)" : "***")} }}";
}

/// <summary>
/// نتیجهٔ <see cref="DefaultAdminAccount.Resolve"/> برای یک مدیر فهرست تنظیمات.
/// </summary>
/// <param name="Admin">همان مورد تنظیمات.</param>
/// <param name="Key">کلید نهایی (ستون Users.SystemAdminKey)؛ null = این مورد نادیده گرفته می‌شود.</param>
/// <param name="Note">هشدار یا دلیل نادیده گرفتن (بدون رمز، شماره پوشیده) برای لاگ و صفحهٔ سلامت؛ null = بدون مشکل.</param>
public sealed record ResolvedDefaultAdmin(DefaultAdminAccount Admin, string? Key, string? Note);
