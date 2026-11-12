using Fratak_Shop.Core.Services.Assistant;

namespace Fratak_Shop.Core.Services.Users;

/// <summary>
/// دور ۲۹ – «خطاب مناسب نسل» در همهٔ جاهایی که سایت مشتری را صدا می‌زند (منوی کاربر، پیشخوان حساب، ستون پروفایل، پیام‌ها).
/// <para>
/// قاعده (همان نسل‌بندی دستیار، <see cref="AssistantPersona.Generation"/>؛ جنسیت ثبت نمی‌شود پس «آقا/خانم» به کار نمی‌رود):
/// </para>
/// <list type="bullet">
/// <item>زیر ۳۰ سال ← «محمد جان»</item>
/// <item>۳۰ تا ۴۴ سال یا سن نامعلوم ← «محمد عزیز»</item>
/// <item>۴۵ تا ۵۹ سال ← «ناظری گرامی» (نام خانوادگی؛ نبود ← نام کوچک)</item>
/// <item>۶۰ سال و بالاتر ← «محمد ناظری گرامی»</item>
/// <item>بدون نام ← «کاربر عزیز»</item>
/// </list>
/// </summary>
public static class UserGreeting
{
    /// <summary>خطاب پیش‌فرض وقتی هیچ نامی ثبت نشده است.</summary>
    public const string Anonymous = "کاربر عزیز";

    /// <summary>
    /// خطاب مناسب. مثال: («محمد»، «ناظری»، تولد ۱۹۹۸، امروز ۲۰۲۶) ← «محمد جان»؛ (…، تولد ۱۹۶۰) ← «محمد ناظری گرامی»؛
    /// (null، null، …) ← «کاربر عزیز».
    /// </summary>
    public static string For(string? firstName, string? lastName, DateOnly? birthDate, DateOnly today)
    {
        var first = AssistantPersona.CleanName(firstName);
        var last = AssistantPersona.CleanName(lastName);
        if (first is null && last is null)
            return Anonymous;
        return AssistantPersona.Generation(birthDate, today) switch
        {
            AssistantGeneration.Young => (first ?? last) + " جان",
            AssistantGeneration.Middle => (last ?? first) + " گرامی",
            AssistantGeneration.Senior => string.Join(' ', new[] { first, last }.Where(n => n is not null)) + " گرامی",
            _ => (first ?? last) + " عزیز",
        };
    }

    /// <summary>
    /// سلام کوتاه با زمان روز (ساعت ایران). مثال: (ساعت ۹، «محمد عزیز») ← «صبح بخیر، محمد عزیز».
    /// </summary>
    public static string Hello(string greeting, int hour)
    {
        var part = hour switch
        {
            >= 5 and < 12 => "صبح بخیر",
            >= 12 and < 17 => "ظهر بخیر",
            >= 17 and < 21 => "عصر بخیر",
            _ => "شب بخیر",
        };
        return $"{part}، {greeting}";
    }
}
