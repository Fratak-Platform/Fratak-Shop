using System.Globalization;
using System.Net.Mail;
using System.Text.RegularExpressions;
using Fratak_Shop.Core.DTOs.Reports;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Entities.Insights;

namespace Fratak_Shop.Core.Services.Insights;

/// <summary>
/// قواعد خالص گزارش‌های زمان‌بندی‌شده (بدون دیتابیس و شبکه؛ همه در تست‌های واحد پوشش داده شده‌اند):
/// محاسبهٔ زمان اجرای بعدی به وقت ایران، بازهٔ گزارش، اعتبارسنجی ایمیل و شناسهٔ گفت‌وگو، و قالب‌بندی عدد فارسی.
/// </summary>
public static partial class ScheduledReportRules
{
    private static readonly PersianCalendar Persian = new();

    /// <summary>حداکثر گیرندهٔ ایمیل برای هر گزارش (جلوگیری از سوءاستفاده به‌عنوان اسپم).</summary>
    public const int MaxEmailRecipients = 10;

    /// <summary>سقف طول پیام ربات‌ها (تلگرام و بله ۴۰۹۶ نویسه؛ کمی حاشیه).</summary>
    public const int MaxBotMessageLength = 4000;

    /// <summary>
    /// زمان اجرای بعدی (UTC) که حتماً بعد از <paramref name="now"/> است.
    /// <list type="bullet">
    /// <item>روزانه: امروز ساعت H اگر هنوز نرسیده، وگرنه فردا.</item>
    /// <item>هفتگی: نزدیک‌ترین روز هفتهٔ انتخابی (۰ = شنبه) در ساعت H.</item>
    /// <item>ماهانه: روز اول ماه شمسی بعد (یا همین امروز اگر اول ماه است و ساعت نرسیده).</item>
    /// </list>
    /// مثال: روزانه ساعت ۸، اکنون ۱۴۰۵/۰۸/۰۴ ساعت ۹ تهران → ۱۴۰۵/۰۸/۰۵ ساعت ۸ تهران.
    /// </summary>
    public static DateTimeOffset NextRun(ReportFrequency frequency, int hour, int weekDay, DateTimeOffset now)
    {
        hour = Math.Clamp(hour, 0, 23);
        var time = new TimeOnly(hour, 0);
        var today = IranClock.Today(now);

        switch (frequency)
        {
            case ReportFrequency.Weekly:
            {
                var target = Math.Clamp(weekDay, 0, 6);
                for (var i = 0; i <= 7; i++)
                {
                    var day = today.AddDays(i);
                    if (IranWeekDay(day) == target && IranClock.At(day, time) > now)
                        return IranClock.At(day, time);
                }
                return IranClock.At(today.AddDays(7), time); // دست‌نیافتنی؛ فقط برای کامل بودن
            }
            case ReportFrequency.Monthly:
            {
                var (year, month) = JalaliMonth.Of(today);
                var thisMonth = JalaliMonth.Range(year, month).Start;
                if (thisMonth == today && IranClock.At(today, time) > now)
                    return IranClock.At(today, time);
                var (ny, nm) = JalaliMonth.Add(year, month, 1);
                return IranClock.At(JalaliMonth.Range(ny, nm).Start, time);
            }
            default:
            {
                var at = IranClock.At(today, time);
                return at > now ? at : IranClock.At(today.AddDays(1), time);
            }
        }
    }

    /// <summary>روز هفتهٔ ایرانی: شنبه = ۰ … جمعه = ۶.</summary>
    public static int IranWeekDay(DateOnly day) => ((int)day.DayOfWeek + 1) % 7;

    /// <summary>
    /// بازه‌ای که گزارش دربارهٔ آن است (روزهای کامل به وقت ایران) و بازهٔ قبلی هم‌طول برای مقایسه.
    /// روزانه = دیروز؛ هفتگی = ۷ روز کامل تا دیروز؛ ماهانه = ماه شمسی قبل.
    /// مثال: اجرای ماهانه در ۱ آذر ← کل آبان، مقایسه با کل مهر.
    /// </summary>
    public static (ReportPeriod Current, ReportPeriod Previous, string Label) Window(ReportFrequency frequency, DateTimeOffset runAt)
    {
        var today = IranClock.Today(runAt);
        switch (frequency)
        {
            case ReportFrequency.Weekly:
            {
                var start = today.AddDays(-7);
                return (Days(start, today), Days(start.AddDays(-7), start), $"{DateText(start)} تا {DateText(today.AddDays(-1))}");
            }
            case ReportFrequency.Monthly:
            {
                var (year, month) = JalaliMonth.Of(today);
                var (py, pm) = JalaliMonth.Add(year, month, -1);
                var (ppy, ppm) = JalaliMonth.Add(year, month, -2);
                var current = JalaliMonth.Range(py, pm);
                var previous = JalaliMonth.Range(ppy, ppm);
                return (Days(current.Start, current.End), Days(previous.Start, previous.End), JalaliMonth.Title(py, pm));
            }
            default:
            {
                var yesterday = today.AddDays(-1);
                return (Days(yesterday, today), Days(yesterday.AddDays(-1), yesterday), $"{DayName(yesterday)} {DateText(yesterday)}");
            }
        }

        static ReportPeriod Days(DateOnly start, DateOnly endExclusive) => new(IranClock.StartOfDay(start), IranClock.StartOfDay(endExclusive));
    }

    public static string FrequencyTitle(ReportFrequency frequency) => frequency switch
    {
        ReportFrequency.Weekly => "هفتگی",
        ReportFrequency.Monthly => "ماهانه",
        _ => "روزانه",
    };

    public static readonly string[] WeekDayNames = ["شنبه", "یکشنبه", "دوشنبه", "سه‌شنبه", "چهارشنبه", "پنجشنبه", "جمعه"];

    public static readonly (ReportSections Section, string Title)[] SectionTitles =
    [
        (ReportSections.Sales, "فروش و مقایسه با دورهٔ قبل"),
        (ReportSections.Orders, "کارهای مانده (سفارش، فاکتور، ارسال)"),
        (ReportSections.Visitors, "بازدیدکننده و نرخ تبدیل"),
        (ReportSections.TopProducts, "پرفروش‌ترین محصولات"),
        (ReportSections.Stock, "موجودی (ناموجود و رو به اتمام)"),
        (ReportSections.Expenses, "هزینه‌ها و سود خالص"),
        (ReportSections.Goal, "پیشرفت هدف فروش ماه"),
        (ReportSections.Support, "پشتیبانی (تیکت، مرجوعی، پیام)"),
    ];

    /// <summary>
    /// جدا کردن و اعتبارسنجی ایمیل‌ها (ویرگول، نقطه‌ویرگول، فاصله یا خط جدید). تکراری‌ها حذف می‌شوند.
    /// خروجی: فهرست معتبر و فهرست نامعتبر. مثال: «a@x.ir, b@x» → معتبر [a@x.ir]، نامعتبر [b@x].
    /// </summary>
    public static (List<string> Valid, List<string> Invalid) ParseEmails(string? value)
    {
        var valid = new List<string>();
        var invalid = new List<string>();
        foreach (var part in (value ?? "").Split([',', ';', '،', ' ', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries))
        {
            var email = part.Trim();
            if (email.Length <= 200 && EmailPattern().IsMatch(email) && MailAddress.TryCreate(email, out var parsed) && parsed.Address == email)
            {
                if (!valid.Contains(email, StringComparer.OrdinalIgnoreCase))
                    valid.Add(email);
            }
            else
            {
                invalid.Add(email);
            }
        }
        return (valid, invalid);
    }

    /// <summary>شناسهٔ گفت‌وگوی ربات: عدد (کاربر/گروه، ممکن است منفی باشد) یا نام کانال با @. مثال: «-1001234567890» یا «@fratak_reports».</summary>
    public static bool IsValidChatId(string? value) => !string.IsNullOrWhiteSpace(value) && ChatIdPattern().IsMatch(value.Trim());

    /// <summary>بررسی کامل فرم گزارش؛ خروجی: پیام خطا یا null.</summary>
    public static string? Validate(ScheduledReportInput input)
    {
        if (string.IsNullOrWhiteSpace(input.Title) || input.Title.Trim().Length is < 2 or > 100)
            return "عنوان گزارش باید ۲ تا ۱۰۰ حرف باشد.";
        if (input.Frequency is not (ReportFrequency.Daily or ReportFrequency.Weekly or ReportFrequency.Monthly))
            return "دورهٔ ارسال نامعتبر است.";
        if (input.Hour is < 0 or > 23)
            return "ساعت ارسال باید بین ۰ تا ۲۳ باشد.";
        if (input.WeekDay is < 0 or > 6)
            return "روز هفته نامعتبر است.";
        if ((input.Channels & (ReportChannels.Email | ReportChannels.Bale | ReportChannels.Telegram)) == ReportChannels.None)
            return "حداقل یک کانال ارسال (ایمیل، بله یا تلگرام) انتخاب کنید.";
        if ((input.Sections & ReportSections.All) == ReportSections.None)
            return "حداقل یک بخش برای گزارش انتخاب کنید.";
        if (input.Channels.HasFlag(ReportChannels.Email))
        {
            var (valid, invalid) = ParseEmails(input.EmailTo);
            if (invalid.Count > 0)
                return $"ایمیل نامعتبر: {string.Join("، ", invalid.Take(3))}";
            if (valid.Count == 0)
                return "برای ارسال ایمیلی، حداقل یک ایمیل گیرنده وارد کنید.";
            if (valid.Count > MaxEmailRecipients)
                return $"حداکثر {MaxEmailRecipients} گیرندهٔ ایمیل مجاز است.";
        }
        if (input.Channels.HasFlag(ReportChannels.Bale) && !IsValidChatId(input.BaleChatId))
            return "شناسهٔ گفت‌وگوی بله نامعتبر است (عدد یا @نام‌کانال).";
        if (input.Channels.HasFlag(ReportChannels.Telegram) && !IsValidChatId(input.TelegramChatId))
            return "شناسهٔ گفت‌وگوی تلگرام نامعتبر است (عدد یا @نام‌کانال).";
        return null;
    }

    /// <summary>عدد با جداکنندهٔ هزارگان و رقم فارسی. مثال: 1250000 → «۱٬۲۵۰٬۰۰۰».</summary>
    public static string Number(decimal value) =>
        PersianDigits(Math.Round(value).ToString("#,0", CultureInfo.InvariantCulture).Replace(',', '٬'));

    /// <summary>مبلغ تومان. مثال: 1250000 → «۱٬۲۵۰٬۰۰۰ تومان».</summary>
    public static string Toman(decimal value) => Number(value) + " تومان";

    /// <summary>درصد تغییر نسبت به دورهٔ قبل. مثال: (120, 100) → «▲ ۲۰٪»؛ قبلی صفر → «جدید».</summary>
    public static string Change(decimal current, decimal previous)
    {
        if (previous == 0)
            return current == 0 ? "بدون تغییر" : "جدید";
        var percent = Math.Round((current - previous) * 100 / Math.Abs(previous), 1);
        if (percent == 0)
            return "بدون تغییر";
        return (percent > 0 ? "▲ " : "▼ ") + PersianDigits(Math.Abs(percent).ToString("0.#", CultureInfo.InvariantCulture)) + "٪";
    }

    public static string PersianDigits(string value)
    {
        var chars = value.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (chars[i] is >= '0' and <= '9')
                chars[i] = (char)('۰' + (chars[i] - '0'));
            else if (chars[i] == '.')
                chars[i] = '٫';
        }
        return new string(chars);
    }

    public static string DateText(DateOnly day)
    {
        var d = day.ToDateTime(TimeOnly.MinValue);
        return PersianDigits($"{Persian.GetYear(d):0000}/{Persian.GetMonth(d):00}/{Persian.GetDayOfMonth(d):00}");
    }

    private static string DayName(DateOnly day) => WeekDayNames[IranWeekDay(day)];

    [GeneratedRegex(@"^[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}$")]
    private static partial Regex EmailPattern();

    [GeneratedRegex(@"^(-?\d{1,20}|@[A-Za-z][A-Za-z0-9_]{3,63})$")]
    private static partial Regex ChatIdPattern();
}
