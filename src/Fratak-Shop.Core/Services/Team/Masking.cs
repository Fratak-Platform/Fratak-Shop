namespace Fratak_Shop.Core.Services.Team;

/// <summary>
/// پوشاندن اطلاعات شخصی برای نمایش به کارکنانی که اجازهٔ دیدن کامل ندارند (مرحلهٔ هفتم – فاز ۱۰).
/// فقط «نمایش» را عوض می‌کند؛ داده در دیتابیس دست نمی‌خورد. توابع خالص‌اند تا در آزمون‌های واحد بررسی شوند.
/// محل استفاده: Web/Infrastructure/Team/SensitiveData (در نماهای پنل با @Pii.Phone(...) و …).
/// </summary>
public static class Masking
{
    /// <summary>نویسهٔ جایگزین بخش پنهان.</summary>
    public const string Dots = "•••";

    /// <summary>موبایل: ۴ رقم اول و ۴ رقم آخر می‌ماند. مثال: «09121234567» ← «0912•••4567». ورودی کوتاه ← «•••».</summary>
    public static string Phone(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;
        var v = value.Trim();
        return v.Length >= 9 ? $"{v[..4]}{Dots}{v[^4..]}" : Dots;
    }

    /// <summary>ایمیل: فقط حرف اول نام کاربری و دامنه. مثال: «ali.rezaei@gmail.com» ← «a•••@gmail.com».</summary>
    public static string Email(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;
        var v = value.Trim();
        var at = v.IndexOf('@');
        return at <= 0 ? Dots : $"{v[0]}{Dots}{v[at..]}";
    }

    /// <summary>
    /// نشانی: فقط دو بخش اول (معمولاً خیابان اصلی) می‌ماند تا محدوده معلوم باشد ولی پلاک و واحد نه.
    /// مثال: «خیابان ولیعصر، کوچه نیلوفر، پلاک ۱۲، واحد ۳» ← «خیابان ولیعصر، •••».
    /// </summary>
    public static string Address(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;
        var v = value.Trim();
        var parts = v.Split(['،', ',', '-'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var head = parts.Length > 1 ? parts[0] : string.Join(' ', v.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2));
        if (head.Length > 30)
            head = head[..30];
        return $"{head}، {Dots}";
    }

    /// <summary>شبا: ۴ نویسهٔ اول و ۴ رقم آخر. مثال: «IR120570000000123456789012» ← «IR12 ••• 9012».</summary>
    public static string Iban(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;
        var v = value.Replace(" ", "").Trim();
        return v.Length >= 10 ? $"{v[..4]} {Dots} {v[^4..]}" : Dots;
    }

    /// <summary>کارت/شماره حساب: ۴ رقم اول و ۴ رقم آخر. مثال: «6037991234567890» ← «6037 ••• 7890».</summary>
    public static string Card(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;
        var v = new string(value.Where(c => !char.IsWhiteSpace(c) && c != '-').ToArray());
        return v.Length >= 10 ? $"{v[..4]} {Dots} {v[^4..]}" : Dots;
    }
}
