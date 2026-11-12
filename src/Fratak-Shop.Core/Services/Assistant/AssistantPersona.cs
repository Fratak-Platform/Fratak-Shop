using System.Text.RegularExpressions;
using Fratak_Shop.Core.Services.Seo.Engine;
using Fratak_Shop.Data.Entities.Assistant;

namespace Fratak_Shop.Core.Services.Assistant;

// ═══════════════════════════════════════════════════════════════════════════
//  دور ۲۴ – «شخصیت» دستیار: با چه اسمی و با چه لحنی با هر مشتری صحبت کند.
//   • نسل از تاریخ تولد: زیر ۳۰ (خودمانی)، ۳۰ تا ۴۴ (دوستانه)، ۴۵ تا ۵۹ (محترمانه)، ۶۰ به بالا (رسمی).
//   • طرز نوشتن خود مشتری (رسمی/خودمانی) لحن را یک پله جابه‌جا می‌کند (بخش‌بندی پویا؛ با هر پیام به‌روز می‌شود).
//   • خواستهٔ صریح مشتری («منو علی صدا کن»، «رسمی صحبت کن») همیشه مقدم است.
//  جنسیت در حساب کاربری ثبت نمی‌شود؛ پس «آقا/خانم» به کار نمی‌رود تا هرگز اشتباه خطاب نکنیم.
//  محل استفاده: AssistantService (همهٔ پاسخ‌ها) و تست‌های Unit/AssistantPersonaTests.
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>گروه سنی مشتری (از تاریخ تولد).</summary>
public enum AssistantGeneration
{
    /// <summary>تاریخ تولد نداریم.</summary>
    Unknown = 0,

    /// <summary>زیر ۳۰ سال.</summary>
    Young = 1,

    /// <summary>۳۰ تا ۴۴ سال.</summary>
    Adult = 2,

    /// <summary>۴۵ تا ۵۹ سال.</summary>
    Middle = 3,

    /// <summary>۶۰ سال و بالاتر.</summary>
    Senior = 4,
}

/// <summary>
/// «صدای» دستیار برای یک مشتری: اسم خطاب (Address)، نام کوچک (First) و لحن (Tone).
/// <para>مثال: Address = «علی عزیز»، Tone = Friendly ← Comma = «، علی عزیز»؛ Hello(...) = «سلام علی عزیز! من آوا هستم…».</para>
/// </summary>
public sealed record AssistantVoice(string? Address, string? First, AssistantTone Tone)
{
    /// <summary>صدای خنثی (مهمان بی‌نام): بدون اسم و با لحن محترمانه.</summary>
    public static readonly AssistantVoice Neutral = new(null, null, AssistantTone.Respectful);

    /// <summary>«، علی عزیز» یا خالی. برای وسط جمله‌ها: $"به سبد اضافه شد{voice.Comma}."</summary>
    public string Comma => Address is null ? "" : "، " + Address;

    /// <summary>خودمانی است؟ (برای جمله‌های کوتاه‌تر و ایموجی ملایم).</summary>
    public bool IsCasual => Tone == AssistantTone.Casual;

    /// <summary>
    /// سلام و معرفی. مثال‌ها (نام دستیار «آوا»، فروشگاه «فراتک»):
    /// Casual ← «سلام علی! 👋 من آوا‌ام، دستیار هوشمند فراتک.»؛ Formal ← «سلام و وقت بخیر، علی ناظری گرامی. آوا هستم، دستیار هوشمند فراتک؛ در خدمت شما هستم.»
    /// </summary>
    public string Hello(string botName, string siteName, int hour) => Tone switch
    {
        AssistantTone.Casual => $"سلام{(First is null ? "" : " " + First)}! 👋 من {botName} هستم، دستیار هوشمند {siteName}.",
        AssistantTone.Friendly => $"سلام{(Address is null ? "" : " " + Address)}! من {botName} هستم، دستیار هوشمند {siteName}.",
        AssistantTone.Formal => $"سلام و {AssistantPersona.DayPart(hour)} بخیر{Comma}. {botName} هستم، دستیار هوشمند {siteName}؛ در خدمت شما هستم.",
        _ => $"سلام، {AssistantPersona.DayPart(hour)} بخیر{Comma}. من {botName} هستم، دستیار هوشمند {siteName}.",
    };

    /// <summary>جواب «ممنون». Casual ← «خواهش می‌کنم علی! 🙂 کاری بود بگو.»</summary>
    public string Thanks() => Tone switch
    {
        AssistantTone.Casual => $"خواهش می‌کنم{(First is null ? "" : " " + First)}! 🙂 کاری بود بگو.",
        AssistantTone.Formal => $"خواهش می‌کنم{Comma}؛ هر زمان امری بود در خدمت هستم.",
        _ => $"خواهش می‌کنم{Comma}! اگر سؤال دیگری بود در خدمتم.",
    };

    /// <summary>آغاز عذرخواهی. Casual ← «ای وای»، بقیه ← «ببخشید».</summary>
    public string Sorry => IsCasual ? "ای وای" : "ببخشید";

    /// <summary>جملهٔ «چه کمکی از دستم برمی‌آید؟» به لحن مشتری.</summary>
    public string HowCanIHelp => Tone switch
    {
        AssistantTone.Casual => "بگو چی لازم داری؟",
        AssistantTone.Formal => "بفرمایید، چه کمکی از دستم برمی‌آید؟",
        _ => "چه کمکی از دستم برمی‌آید؟",
    };
}

/// <summary>
/// محاسبهٔ «صدای» دستیار و فهم خواسته‌های مربوط به خطاب و لحن. همه متدها خالص و بدون پایگاه داده‌اند.
/// </summary>
public static partial class AssistantPersona
{
    /// <summary>بیشترین/کمترین مقدار طرز نوشتن ذخیره‌شده.</summary>
    public const int MaxRegister = 3;

    /// <summary>
    /// گروه سنی. مثال: تولد ۱۳۸۰ (۲۰۰۱) و امروز ۲۰۲۶ ← ۲۵ سال ← Young؛ تولد ۱۹۶۰ ← Senior؛ null یا سن نامعقول (زیر ۱۰ یا بالای ۱۱۰) ← Unknown.
    /// </summary>
    public static AssistantGeneration Generation(DateOnly? birthDate, DateOnly today)
    {
        if (birthDate is not { } birth)
            return AssistantGeneration.Unknown;
        var age = today.Year - birth.Year - (today < birth.AddYears(today.Year - birth.Year) ? 1 : 0);
        return age switch
        {
            < 10 or > 110 => AssistantGeneration.Unknown,
            < 30 => AssistantGeneration.Young,
            < 45 => AssistantGeneration.Adult,
            < 60 => AssistantGeneration.Middle,
            _ => AssistantGeneration.Senior,
        };
    }

    /// <summary>
    /// لحن نهایی. ترتیب: خواستهٔ صریح ← نسل ← طرز نوشتن (یک پله).
    /// <para>مثال: Middle (محترمانه) + نوشتن خیلی خودمانی (register = 3) ← Friendly؛ Young + نوشتن رسمی (register = -2) ← Friendly؛
    /// نسل نامعلوم ← بر اساس طرز نوشتن: خودمانی ← Friendly، رسمی ← Formal، بقیه ← Respectful.</para>
    /// <para>سالمندان (Senior) هیچ‌وقت خودمانی خطاب نمی‌شوند (حداکثر Respectful).</para>
    /// </summary>
    public static AssistantTone ToneFor(AssistantGeneration generation, int register, AssistantTone preference)
    {
        if (preference != AssistantTone.Auto)
            return preference;
        register = Math.Clamp(register, -MaxRegister, MaxRegister);
        var baseTone = generation switch
        {
            AssistantGeneration.Young => AssistantTone.Casual,
            AssistantGeneration.Adult => AssistantTone.Friendly,
            AssistantGeneration.Middle => AssistantTone.Respectful,
            AssistantGeneration.Senior => AssistantTone.Formal,
            _ => register >= 2 ? AssistantTone.Friendly : register <= -2 ? AssistantTone.Formal : AssistantTone.Respectful,
        };
        if (generation == AssistantGeneration.Unknown)
            return baseTone;
        var shifted = (int)baseTone + (register >= 2 ? 1 : register <= -2 ? -1 : 0);
        var max = generation == AssistantGeneration.Senior ? (int)AssistantTone.Respectful : (int)AssistantTone.Casual;
        return (AssistantTone)Math.Clamp(shifted, (int)AssistantTone.Formal, max);
    }

    /// <summary>
    /// ساخت صدای دستیار برای یک مشتری.
    /// <para>مثال‌ها (علی ناظری): Casual ← «علی»؛ Friendly ← «علی عزیز»؛ Respectful ← «ناظری گرامی»؛ Formal ← «علی ناظری گرامی».
    /// «منو امین صدا کن» (callMe = امین) ← در همهٔ لحن‌ها «امین» به‌جای نام حساب (Respectful ← «امین گرامی»).</para>
    /// </summary>
    public static AssistantVoice Build(string? firstName, string? lastName, string? callMe, AssistantTone tone)
    {
        var first = CleanName(callMe) ?? CleanName(firstName);
        var last = CleanName(callMe) is null ? CleanName(lastName) : null;
        if (first is null && last is null)
            return new AssistantVoice(null, null, tone == AssistantTone.Auto ? AssistantTone.Respectful : tone);
        var address = tone switch
        {
            // دور ۳۱: «جان» هرگز (خواستهٔ صریح کاربر)؛ لحن خودمانی فقط نام کوچک.
            AssistantTone.Casual => first ?? last,
            AssistantTone.Friendly => (first ?? last) + " عزیز",
            AssistantTone.Formal => string.Join(' ', new[] { first, last }.Where(n => n is not null)) + " گرامی",
            _ => (last ?? first) + " گرامی",
        };
        return new AssistantVoice(address, first ?? last, tone == AssistantTone.Auto ? AssistantTone.Respectful : tone);
    }

    /// <summary>
    /// اسم تمیز (فقط حروف فارسی/انگلیسی و فاصله، حداکثر ۲۴ نویسه)؛ چیز دیگر ← null.
    /// مثال: « علی » ← «علی»؛ «<b>x</b>» ← null؛ «09121234567» ← null؛ «Ali» ← «Ali».
    /// </summary>
    public static string? CleanName(string? name)
    {
        name = name?.Trim();
        if (string.IsNullOrEmpty(name) || name.Length > 24 || !NameRegex().IsMatch(name))
            return null;
        name = SpaceRegex().Replace(name, " ");
        return BlockedNames.Contains(PersianText.Normalize(name)) ? null : name;
    }

    // واژه‌هایی که در جای «اسم» یعنی جمله پرسش است: «اسم من چیه»، «اسمم رو یادته».
    private static readonly HashSet<string> NotNames = new(StringComparer.Ordinal)
    {
        "چی", "چیه", "چیست", "چه", "کی", "کیه", "رو", "را", "یادته", "یادت", "بلدی", "میدونی", "میدانی", "نیست", "نه", "چطور", "کجا",
    };

    // اسم‌هایی که نباید خطاب شوند (توهین یا جعل نقش).
    private static readonly HashSet<string> BlockedNames = new(StringComparer.Ordinal)
    {
        "ادمین", "مدیر", "مدیر سایت", "پشتیبانی", "admin", "administrator", "root", "احمق", "خر", "الاغ", "گاو", "بیشعور",
    };

    /// <summary>
    /// تغییر طرز نوشتن با یک پیام: نشانه‌های خودمانی (+1) یا رسمی (-1)؛ حاصل بین ‎-3 و ‎+3.
    /// <para>مثال: (0، «سلام داداش اینو داری؟») ← 1؛ (0، «با سلام، خواهشمندم بفرمایید…») ← -1؛ پیام بی‌نشانه ← بدون تغییر.</para>
    /// </summary>
    public static int UpdateRegister(int register, string text)
    {
        var normalized = " " + string.Join(' ', PersianText.Tokenize(text ?? "")) + " ";
        var casual = CasualMarkers.Count(m => normalized.Contains(m, StringComparison.Ordinal)) + (EmojiRegex().IsMatch(text ?? "") ? 1 : 0);
        var formal = FormalMarkers.Count(m => normalized.Contains(m, StringComparison.Ordinal));
        var delta = Math.Sign(casual - formal);
        return Math.Clamp(register + delta, -MaxRegister, MaxRegister);
    }

    // نشانه‌ها با فاصلهٔ دو طرف تا وسط واژهٔ دیگر پیدا نشوند (روی متن نرمال‌شده).
    private static readonly string[] CasualMarkers =
    [
        " داداش ", " داش ", " رفیق ", " مرسی ", " مرسیی ", " اوکی ", " باحال ", " خفن ", " چطوری ", " میخوام ", " میخواستم ",
        " داری ", " اینو ", " اونو ", " چنده ", " بگو ", " بده ", " نشونم ", " قربونت ", " فدات ", " عشقی ", " حله ", " دمت گرم ",
        " لطفا بگو ", " هستی ", " جون ", " بیا ",
    ];

    private static readonly string[] FormalMarkers =
    [
        " با سلام ", " خواهشمندم ", " خواهشمند است ", " بفرمایید ", " جنابعالی ", " سرکار ", " حضرتعالی ", " ارادتمند ", " احتراما ",
        " احترام ", " مستدعی ", " بی زحمت ", " لطف بفرمایید ", " می فرمایید ", " عرض ", " تشکر می کنم ", " سپاسگزارم ",
    ];

    /// <summary>
    /// خواستهٔ مشتری دربارهٔ خطاب یا لحن، یا null.
    /// <para>مثال‌ها: «منو علی صدا کن» ← (CallMe: «علی»)؛ «اسمم سارا است» ← (CallMe: «سارا»)؛ «رسمی صحبت کن» ← (Tone: Formal)؛
    /// «خودمونی باش» ← (Tone: Casual)؛ «عادی صحبت کن» ← (Tone: Auto)؛ «اسمم رو فراموش کن» ← (ForgetName: true).</para>
    /// </summary>
    public static AssistantPreferenceChange? ParsePreference(string text)
    {
        var normalized = string.Join(' ', PersianText.Tokenize(text ?? ""));
        if (normalized.Length == 0 || normalized.Length > 80)
            return null;
        if (ForgetRegex().IsMatch(normalized))
            return new AssistantPreferenceChange(null, null, true);
        if (ToneRegex().Match(normalized) is { Success: true } tone)
        {
            var word = tone.Groups["tone"].Value;
            var value = word switch
            {
                "رسمی" or "رسمیتر" or "مودبانه" or "محترمانه" => AssistantTone.Formal,
                "خودمونی" or "خودمانی" or "صمیمی" or "راحت" or "دوستانه" or "خودمونیتر" => AssistantTone.Casual,
                _ => AssistantTone.Auto,
            };
            return new AssistantPreferenceChange(null, value, false);
        }
        var call = CallMeRegex().Match(normalized);
        if (!call.Success)
            call = MyNameRegex().Match(normalized);
        var raw = call.Success ? TrailingVerbRegex().Replace(call.Groups["name"].Value.Trim(), "") : null;
        // «اسم من چیه؟» یا «اسمم رو یادته؟» پرسش است، نه معرفی اسم.
        if (raw is not null && !raw.Split(' ').Any(NotNames.Contains) && CleanName(raw) is { } name)
            return new AssistantPreferenceChange(name, null, false);
        return null;
    }

    /// <summary>بخش روز برای سلام: «صبح» (۵ تا ۱۱)، «ظهر» (۱۲ تا ۱۴)، «عصر» (۱۵ تا ۱۸)، «شب»؛ مثال: 9 ← «صبح» ← «صبح بخیر».</summary>
    public static string DayPart(int hour) => hour switch
    {
        >= 5 and < 12 => "صبح",
        >= 12 and < 15 => "ظهر",
        >= 15 and < 19 => "عصر",
        _ => "شب",
    };

    [GeneratedRegex(@"\A[\p{L}\u200c ]{2,24}\z")]
    private static partial Regex NameRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex SpaceRegex();

    [GeneratedRegex(@"[\u2600-\u27BF]|\uD83C[\uDF00-\uDFFF]|\uD83D[\uDC00-\uDE4F\uDE80-\uDEFF]|\uD83E[\uDD00-\uDDFF]")]
    private static partial Regex EmojiRegex();

    [GeneratedRegex(@"(?:\A|\s)(?<tone>رسمی|رسمیتر|مودبانه|محترمانه|خودمونی|خودمانی|خودمونیتر|صمیمی|راحت|دوستانه|عادی|معمولی)\s*(?:تر\s*)?(?:صحبت|حرف)?\s*(?:کن|بکن|بزن|باش|کنید|بزنید|باشید)\b")]
    private static partial Regex ToneRegex();

    [GeneratedRegex(@"(?:\A|\s)(?:منو|من رو|من را|مرا)\s+(?<name>[\p{L}\u200c]{2,20}(?:\s[\p{L}\u200c]{2,20})?)\s+(?:صدا\s*(?:کن|بزن|کنید|بزنید)|خطاب\s*(?:کن|کنید)|بگو|بگید|بگویید)")]
    private static partial Regex CallMeRegex();

    [GeneratedRegex(@"(?:\A|\s)(?:اسمم|اسم من|نام من|نامم)\s+(?<name>[\p{L}\u200c]{2,20}(?:\s[\p{L}\u200c]{2,20})?)\s*(?:است|هست|ه)?\s*\z")]
    private static partial Regex MyNameRegex();

    [GeneratedRegex(@"\s+(?:است|هست|هستم)\z")]
    private static partial Regex TrailingVerbRegex();

    [GeneratedRegex(@"(?:اسمم|اسم من|نامم|نام من)\s*(?:رو|را)?\s*(?:فراموش|پاک)\s*(?:کن|کنید)")]
    private static partial Regex ForgetRegex();
}

/// <summary>یک خواستهٔ تازه دربارهٔ خطاب/لحن. CallMe = اسم تازه؛ Tone = لحن تازه؛ ForgetName = برگشت به نام حساب.</summary>
public sealed record AssistantPreferenceChange(string? CallMe, AssistantTone? Tone, bool ForgetName);
