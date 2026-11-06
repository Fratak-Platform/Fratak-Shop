using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Core.Utilities;
using Microsoft.Extensions.Options;

namespace Fratak_Shop.Web.Infrastructure.Auth;

/// <summary>
/// تشخیص «هنوز رمز اولیهٔ تنظیمات را دارد» برای مدیران پیش‌فرض (<c>Shop:DefaultAdmins</c>، مرحلهٔ چهاردهم).
/// <para>چرا: رمز اولیه (مثلاً 12345678) در فایل تنظیمات/مخزن دیده می‌شود؛ تا عوض نشده، حساب مالک در خطر است.
/// دو جا استفاده می‌شود:</para>
/// <list type="bullet">
/// <item>صفحهٔ ورود (Pages/Login): بعد از ورود موفق با همان رمز، کاربر مستقیم به «پروفایل › رمز عبور» می‌رود با پیام هشدار.</item>
/// <item>سلامت سیستم (HealthService): تا وقتی هر مدیر پیش‌فرضی رمز اولیه را دارد هشدار نشان می‌دهد.</item>
/// </list>
/// <para>امنیت: مقایسهٔ رمز در زمان ثابت (FixedTimeEquals)؛ رمز هرگز لاگ یا نمایش داده نمی‌شود؛
/// نتیجهٔ Argon2 (گران: ۶۴ مگابایت حافظه) برای هر هش ذخیره می‌شود تا صفحهٔ سلامت سرور را کند نکند.</para>
/// </summary>
public sealed class DefaultAdminPasswords
{
    private readonly Dictionary<string, byte[]> _passwords = new(StringComparer.Ordinal);
    private readonly Dictionary<string, byte[]> _byKey = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, bool> _verified = new(StringComparer.Ordinal);

    public DefaultAdminPasswords(IOptions<ShopOptions> options)
    {
        // مرحلهٔ هفدهم: کلیدها دقیقاً مثل AdminAccessService (با رفع Key تکراری/نامعتبر) ساخته می‌شوند.
        foreach (var resolved in DefaultAdminAccount.Resolve(options.Value.DefaultAdminAccounts()))
        {
            var admin = resolved.Admin;
            var phone = AdminAccessService.NormalizeOwnerPhone(admin.PhoneNumber);
            if (phone is null || resolved.Key is null || !DefaultAdminAccount.IsValidPassword(admin.Password))
                continue;
            var bytes = Encoding.UTF8.GetBytes(admin.Password!);
            _passwords[phone] = bytes;
            // مرحلهٔ پانزدهم: با کلید ثابت هم نگه می‌داریم تا اگر مدیر شماره‌اش را عوض کرد، هشدار «رمز اولیه» باز هم درست باشد.
            _byKey[resolved.Key] = bytes;
        }
    }

    /// <summary>کلیدهای ثابت مدیران اصلی که رمز اولیه دارند (ستون Users.SystemAdminKey؛ برای پرس‌وجوی سلامت).</summary>
    public IReadOnlyCollection<string> Keys => _byKey.Keys;

    /// <summary>
    /// هش ذخیره‌شدهٔ مدیر اصلی با این کلید هنوز با رمز اولیه می‌خواند؟ مثال: («key:owner», هشِ 12345678) ← true.
    /// </summary>
    public bool StillUsesDefaultForKey(string key, string? passwordHash)
    {
        if (string.IsNullOrEmpty(passwordHash) || !_byKey.TryGetValue(key, out var expected))
            return false;
        return _verified.GetOrAdd(key + "|" + passwordHash, _ => PasswordHasher.Verify(Encoding.UTF8.GetString(expected), passwordHash));
    }

    /// <summary>شماره‌های مدیران پیش‌فرضی که رمز اولیه دارند (برای پرس‌وجوی سلامت).</summary>
    public IReadOnlyCollection<string> Phones => _passwords.Keys;

    /// <summary>
    /// رمزی که کاربر همین الان با آن وارد شد همان رمز اولیهٔ تنظیمات است؟ (بدون Argon2، مقایسهٔ زمان‌ثابت)
    /// مثال: ("09013181090", "12345678") ← true تا وقتی در تنظیمات همین است؛ ("09013181090", "رمز‌جدید") ← false.
    /// </summary>
    public bool IsDefault(string? phoneNumber, string? password)
    {
        var phone = AdminAccessService.NormalizeOwnerPhone(phoneNumber);
        if (phone is null || string.IsNullOrEmpty(password) || !_passwords.TryGetValue(phone, out var expected))
            return false;
        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(password), expected);
    }

    /// <summary>
    /// هش ذخیره‌شدهٔ این مدیر هنوز با رمز اولیه می‌خواند؟ (Argon2؛ نتیجه برای هر هش کش می‌شود، تغییر رمز = هش تازه).
    /// </summary>
    public bool StillUsesDefault(string phoneNumber, string? passwordHash)
    {
        if (string.IsNullOrEmpty(passwordHash) || !_passwords.TryGetValue(phoneNumber, out var expected))
            return false;
        return _verified.GetOrAdd(phoneNumber + "|" + passwordHash, _ => PasswordHasher.Verify(Encoding.UTF8.GetString(expected), passwordHash));
    }
}
