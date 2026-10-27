using System.Globalization;
using System.Text.RegularExpressions;

namespace Fratak_Shop.Core.Services.Seo.Engine;

// ═══════════════════════════════════════════════════════════════════════════
//  دانش مشخصات فنی (مرحلهٔ یازدهم) – «توضیح دادن» به‌جای «فهرست کردن»
//  متن قبلی فقط مشخصات را جمله می‌کرد («شارژدهی این محصول ۴۰ ساعت است»). این بخش هر مشخصهٔ رایج کالاهای
//  دیجیتال را می‌شناسد و توضیح می‌دهد «این عدد برای خریدار چه معنایی دارد» (مثلاً IP68 یعنی چه، بلوتوث ۵ چه فرقی
//  دارد). چون مشخصات هر محصول فرق دارد، متن هر صفحه هم واقعاً فرق می‌کند و برای کاربر مفید است (همان چیزی که
//  گوگل «محتوای مفید» می‌نامد).
//  قانون‌ها: فقط دانش عمومی و درست؛ هیچ ادعای «بهترین/اصل/ارزان‌ترین» و هیچ عددی که در دادهٔ محصول نیست.
//  کاربرد: ContentWriter (متن، پرسش‌وپاسخ، عنوان و توضیحات متا).
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>موضوع یک مشخصه (برای انتخاب توضیح مناسب).</summary>
public enum SpecTopic
{
    Generic = 0,
    Battery, Bluetooth, NoiseCancel, Water, Weight, Port, WirelessCharge, FastCharge,
    Storage, Ram, ScreenSize, Refresh, Resolution, Camera, Processor, Os, Microphones, Power, Material, Color, Compatibility,
}

/// <summary>
/// یک مشخصهٔ خوانده‌شده. Direction: ۱ = عدد بیشتر بهتر (باتری، حافظه)، ۱- = کمتر بهتر (وزن)، ۰ = بی‌جهت.
/// مثال: («شارژدهی»، «40 ساعت») → Topic=Battery، Number=40، Direction=1.
/// </summary>
public sealed record SpecFact(SpecTopic Topic, string Key, string Value, bool IsYes, double? Number, int Direction)
{
    /// <summary>کلید یکسان‌شده (برای آمار دسته).</summary>
    public string NormalizedKey => PersianText.Normalize(Key);
}

/// <summary>شناخت و توضیح مشخصات فنی رایج.</summary>
public static partial class SpecKnowledge
{
    /// <summary>
    /// اولین عدد داخل متن (ارقام فارسی/عربی و ممیز «٫» هم پشتیبانی می‌شود). مثال: «۵٫۳» → 5.3، «40 ساعت» → 40، «IPX5» → 5.
    /// </summary>
    public static double? Number(string? value)
    {
        var normalized = PersianText.Normalize(value).Replace('٫', '.').Replace('/', '.');
        var match = NumberPattern().Match(normalized);
        return match.Success && double.TryParse(match.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? number : null;
    }

    /// <summary>
    /// خواندن یک ویژگی. null = قابل استفاده نیست («ندارد»، خالی، خیلی بلند).
    /// </summary>
    public static SpecFact? Read(SeoFeature feature)
    {
        var key = Safe(PersianText.Clean(feature.Key));
        var value = Safe(PersianText.Clean(feature.Value));
        var nk = PersianText.Normalize(key);
        var nv = PersianText.Normalize(value);
        if (key.Length is < 2 or > 40 || nv.Length is 0 or > 40 || nv is "ندارد" or "خیر" or "no" or "false" or "-" or "0" or "ندارد." or "بدون")
            return null;
        var yes = nv is "دارد" or "بله" or "yes" or "true" or "✓" or "پشتیبانی می کند" or "دارد." or "فعال";
        var number = yes ? null : Number(value);
        var topic = TopicOf(nk, nv);
        var direction = topic switch
        {
            SpecTopic.Battery or SpecTopic.Storage or SpecTopic.Ram or SpecTopic.Refresh or SpecTopic.Microphones => 1,
            SpecTopic.Bluetooth => 1,
            SpecTopic.Weight => -1,
            _ => 0,
        };
        // باتری بر حسب «ساعت» و «میلی‌آمپر» قابل مقایسه با هم نیستند؛ واحد در CategoryProfile از روی کلید جداست.
        return new SpecFact(topic, key, value, yes, number, number is null ? 0 : direction);
    }

    private static SpecTopic TopicOf(string key, string value)
    {
        bool K(params string[] words) => words.Any(w => key.Contains(w, StringComparison.Ordinal));
        if (K("شارژ بی سیم", "wireless charg")) return SpecTopic.WirelessCharge;
        if (K("شارژ سریع", "fast charg", "فست شارژ")) return SpecTopic.FastCharge;
        if (K("درگاه", "پورت", "port", "کانکتور", "رابط شارژ", "نوع اتصال شارژ")) return SpecTopic.Port;
        if (K("زمان شارژ شدن", "مدت زمان شارژ شدن", "زمان لازم برای شارژ")) return SpecTopic.Generic;
        if (K("باتری", "شارژدهی", "شارژ دهی", "عمر شارژ", "زمان پخش", "مدت پخش", "battery", "playtime")) return SpecTopic.Battery;
        if (K("بلوتوث", "bluetooth")) return SpecTopic.Bluetooth;
        if (K("نویز", "anc", "حذف صدا")) return SpecTopic.NoiseCancel;
        if (K("مقاومت در برابر آب", "ضد آب", "مقاوم در برابر آب", "waterproof", "water resist", "ip rating", "استاندارد ip", "درجه حفاظت")
            || IpPattern().IsMatch(value) && K("مقاومت", "آب", "حفاظت", "استاندارد")) return SpecTopic.Water;
        if (K("وزن", "weight")) return SpecTopic.Weight;
        if (key is "رم" or "ram" || K("حافظه رم", "حافظه موقت", "ram ")) return SpecTopic.Ram;
        if (K("حافظه داخلی", "ظرفیت حافظه", "حافظه", "storage", "ظرفیت ذخیره")) return SpecTopic.Storage;
        if (K("نرخ نوسازی", "رفرش", "refresh", "نرخ بروزرسانی")) return SpecTopic.Refresh;
        if (K("رزولوشن", "وضوح", "resolution")) return SpecTopic.Resolution;
        if (K("اندازه صفحه", "سایز صفحه", "اندازه نمایشگر", "صفحه نمایش", "نمایشگر") && value.Contains("اینچ", StringComparison.Ordinal)) return SpecTopic.ScreenSize;
        if (K("دوربین", "camera")) return SpecTopic.Camera;
        if (K("پردازنده", "تراشه", "چیپست", "cpu", "chip", "پردازشگر")) return SpecTopic.Processor;
        if (K("سیستم عامل", "سیستم‌عامل") || key == "os") return SpecTopic.Os;
        if (K("میکروفون", "microphone")) return SpecTopic.Microphones;
        if (K("توان", "power")) return SpecTopic.Power;
        if (K("جنس", "متریال", "بدنه", "material")) return SpecTopic.Material;
        if (K("رنگ", "color")) return SpecTopic.Color;
        if (K("سازگار", "compatib")) return SpecTopic.Compatibility;
        return SpecTopic.Generic;
    }

    /// <summary>
    /// جمله‌های توضیحی نامزد برای یک مشخصه (با جایگزین‌های [..|..] و جای‌نگهدار {N} = اشاره به محصول).
    /// مثال (Water، IP68): «استاندارد IP68 یعنی {N} کاملاً در برابر ورود گردوغبار بسته است و …».
    /// </summary>
    public static List<string> Explain(SpecFact f)
    {
        var k = f.Key; var v = f.Value;
        var list = new List<string>();
        switch (f.Topic)
        {
            case SpecTopic.Battery when IsCapacity(f):
                list.Add($"ظرفیت باتری {{N}} {v} است؛ [ظرفیت بالاتر|هرچه این عدد بیشتر باشد،] [معمولاً|به‌طور معمول] فاصلهٔ بین دو شارژ را طولانی‌تر می‌کند، هرچند مصرف واقعی به نوع استفاده بستگی دارد.");
                list.Add($"{{N}} با باتری {v} عرضه می‌شود؛ [زمان واقعی کارکرد|مدت واقعی کار با یک بار شارژ] به روشنایی صفحه، برنامه‌های در حال اجرا و [شبکه|اتصال‌ها] بستگی دارد.");
                break;
            case SpecTopic.Battery when !f.IsYes:
                list.Add($"[طبق اعلام سازنده|بر اساس مشخصات سازنده]، {k} {{N}} [حدود|تا] {v} است؛ این عدد در شرایط استاندارد [سنجیده می‌شود|اندازه‌گیری می‌شود] و با صدای بلندتر یا روشن بودن قابلیت‌هایی مثل حذف نویز کمتر می‌شود.");
                list.Add($"{k} [اعلام‌شده|ثبت‌شده] برای {{N}} {v} است؛ [برای مقایسهٔ منصفانه|هنگام مقایسه] این عدد را با مدل‌های هم‌رده و در شرایط استفادهٔ مشابه بسنجید.");
                if (f.Number is >= 20)
                    list.Add($"{{N}} [حدود|تا] {v} {k} دارد و [نیاز به شارژ مکرر را کم می‌کند|کمتر به شارژر وابسته‌تان می‌کند].");
                break;
            case SpecTopic.Bluetooth when f.Number is >= 5:
                list.Add($"{{N}} از بلوتوث نسخهٔ {v} [استفاده می‌کند|بهره می‌برد]؛ نسخه‌های ۵ به بعد [معمولاً|به‌طور کلی] [اتصال پایدارتر و مصرف انرژی کمتری|برد بیشتر و اتصال پایدارتری] نسبت به نسل‌های قدیمی‌تر دارند.");
                list.Add($"اتصال {{N}} با بلوتوث {v} برقرار می‌شود؛ [این نسل از بلوتوث|بلوتوث نسل پنجم] [برای پخش صدا و مکالمهٔ بی‌وقفه|برای اتصال سریع و پایدار به گوشی] طراحی شده است.");
                break;
            case SpecTopic.Bluetooth:
                list.Add($"{{N}} با بلوتوث نسخهٔ {v} به گوشی و دستگاه‌های دیگر وصل می‌شود.");
                break;
            case SpecTopic.NoiseCancel when IsPassive(v):
                list.Add($"حذف نویز {{N}} از نوع [غیرفعال|پسیو] است؛ یعنی صدای محیط با طراحی فیزیکی و [جنس بالشتک‌ها|شکل قرارگیری در گوش] کم می‌شود[ و باتری مصرف نمی‌کند|].");
                break;
            case SpecTopic.NoiseCancel:
                list.Add("[قابلیت|فناوری] حذف نویز فعال (ANC) [صداهای|سروصدای] محیط اطراف را کم می‌کند و [در مترو، خیابان یا محیط کار شلوغ|در محیط‌های پرسروصدا] [تجربهٔ شنیداری آرام‌تری می‌دهد|شنیدن موسیقی و مکالمه را راحت‌تر می‌کند].");
                list.Add("{N} به حذف نویز [مجهز است|مجهز شده است]؛ [این قابلیت|این فناوری] با تولید موج صوتی مخالف، صدای یکنواخت محیط مثل [صدای موتور و کولر|همهمهٔ اطراف] را [کاهش می‌دهد|تا حد زیادی کم می‌کند].");
                break;
            case SpecTopic.Water when IpMeaning(v + " " + k) is { } ip:
                list.Add($"استاندارد {ip.Code} یعنی {{N}} {ip.Meaning}.");
                list.Add($"[درجهٔ حفاظت|رتبهٔ مقاومت] {{N}} {ip.Code} است[؛|، یعنی] {ip.Meaning}.");
                break;
            case SpecTopic.Water:
                list.Add($"{{N}} [در برابر آب مقاوم است|مقاومت در برابر آب دارد][؛ میزان دقیق آن در جدول مشخصات آمده است|].");
                break;
            case SpecTopic.Weight:
                list.Add($"وزن {{N}} {v} است[؛ وزن کمتر یعنی استفادهٔ طولانی‌تر بدون خستگی و جابه‌جایی راحت‌تر|].");
                list.Add($"{{N}} [حدود|] {v} وزن دارد[ و به‌راحتی جابه‌جا می‌شود|].");
                break;
            case SpecTopic.Port when IsUsbC(v):
                list.Add($"درگاه شارژ {{N}} {v} است؛ [این درگاه دوطرفه است و|درگاهی دوطرفه که] با [بیشتر کابل‌ها و شارژرهای امروزی|کابل‌های رایج امروزی] سازگار است.");
                list.Add($"{{N}} با درگاه {v} شارژ می‌شود[؛ یعنی می‌توانید از همان کابل گوشی‌های جدید استفاده کنید|].");
                break;
            case SpecTopic.Port:
                list.Add($"{k} {{N}} {v} است.");
                break;
            case SpecTopic.WirelessCharge when f.IsYes:
                list.Add("{N} از شارژ بی‌سیم پشتیبانی می‌کند؛ [کافی است|می‌توانید] آن را روی پد شارژ بی‌سیم سازگار بگذارید [تا بدون کابل شارژ شود|و بدون اتصال کابل شارژش کنید].");
                break;
            case SpecTopic.WirelessCharge:
                list.Add($"شارژ بی‌سیم {{N}}: {v}.");
                break;
            case SpecTopic.FastCharge when f.Number is > 0:
                list.Add($"{{N}} از شارژ سریع با توان {v} پشتیبانی می‌کند[ و در زمان کوتاه‌تری شارژ می‌شود|].");
                break;
            case SpecTopic.FastCharge:
                list.Add("{N} از شارژ سریع پشتیبانی می‌کند[ و در زمان کوتاه‌تری آمادهٔ استفاده می‌شود|].");
                break;
            case SpecTopic.Storage:
                list.Add($"{{N}} {v} حافظهٔ داخلی دارد[؛ فضایی برای عکس، ویدیو و برنامه‌ها|].");
                list.Add($"[حافظهٔ داخلی|فضای ذخیره‌سازی] {{N}} {v} است[ و برای نگهداری فایل‌ها و برنامه‌ها در نظر گرفته شده|].");
                break;
            case SpecTopic.Ram:
                list.Add($"{v} رم {{N}} [در اجرای هم‌زمان چند برنامه نقش مهمی دارد|روی سرعت جابه‌جایی بین برنامه‌ها اثر مستقیم دارد].");
                list.Add($"[حافظهٔ رم|رم] {{N}} {v} است؛ [رم بیشتر|حافظهٔ موقت بیشتر] یعنی برنامه‌های باز [دیرتر بسته می‌شوند|سریع‌تر دوباره در دسترس‌اند].");
                break;
            case SpecTopic.ScreenSize:
                list.Add($"صفحه نمایش {{N}} {v} است[؛ صفحهٔ بزرگ‌تر برای تماشای ویدیو راحت‌تر است ولی حمل دستگاه را کمی سخت‌تر می‌کند|].");
                break;
            case SpecTopic.Refresh when f.Number is > 0:
                list.Add($"نرخ نوسازی صفحهٔ {{N}} {v} است؛ یعنی تصویر در هر ثانیه {Display(f.Number.Value)} بار به‌روز می‌شود و [پیمایش و بازی روان‌تر دیده می‌شود|حرکت‌ها نرم‌تر دیده می‌شوند].");
                break;
            case SpecTopic.Resolution:
                list.Add($"وضوح تصویر {{N}} {v} است[؛ وضوح بالاتر یعنی جزئیات بیشتر و تصویر شفاف‌تر|].");
                break;
            case SpecTopic.Camera:
                list.Add($"{k} {{N}} {v} است[؛ البته کیفیت عکس فقط به مگاپیکسل وابسته نیست و حسگر و پردازش تصویر هم اثر زیادی دارند|].");
                break;
            case SpecTopic.Processor:
                list.Add($"{{N}} از {k} {v} [بهره می‌برد|استفاده می‌کند]؛ [این بخش|پردازنده] روی سرعت اجرای برنامه‌ها و روان بودن کار با دستگاه اثر مستقیم دارد.");
                list.Add($"[قلب|مغز] {{N}}، {k} {v} است[ که پردازش‌ها را انجام می‌دهد|].");
                break;
            case SpecTopic.Os:
                list.Add($"{{N}} با سیستم‌عامل {v} [عرضه می‌شود|کار می‌کند].");
                break;
            case SpecTopic.Microphones when f.Number is > 0:
                list.Add($"{{N}} {v} میکروفون دارد[؛ میکروفون‌های بیشتر معمولاً به ثبت واضح‌تر صدا در مکالمه کمک می‌کنند|].");
                break;
            case SpecTopic.Color:
                list.Add($"{{N}} در رنگ {v} [عرضه شده است|ارائه می‌شود].");
                break;
            case SpecTopic.Compatibility:
                list.Add($"{{N}} با {v} سازگار است.");
                break;
            case SpecTopic.Material:
                list.Add($"{k} {{N}} {v} است.");
                list.Add($"[در ساخت|برای ساخت] {{N}} از {v} استفاده شده است.");
                break;
        }

        if (list.Count == 0)
        {
            // توضیح عمومی (موضوع ناشناخته): چند ساختار متفاوت تا جمله‌ها یکنواخت نشوند.
            if (f.IsYes)
            {
                list.Add($"{{N}} [از {k} پشتیبانی می‌کند|{k} دارد].");
                list.Add($"{k} [هم|] از [قابلیت‌های|امکانات] {{N}} است.");
            }
            else
            {
                list.Add($"{k} {{N}} {v} است.");
                list.Add($"[در مشخصات|در جدول مشخصات] {{N}}، {k} {v} [ثبت شده است|اعلام شده است].");
                list.Add($"{{N}} [با|دارای] {k} {v} است.");
            }
        }
        return list;
    }

    /// <summary>
    /// عبارت کوتاه برای عنوان و توضیحات متا. null = این مشخصه برای «برجسته کردن» مناسب نیست.
    /// همه «گروه اسمی»اند تا بعد از «با» درست خوانده شوند.
    /// مثال: Battery «40 ساعت» → «40 ساعت شارژدهی»؛ NoiseCancel → «حذف نویز فعال»؛ Water IPX5 → «مقاومت در برابر آب (IPX5)».
    /// </summary>
    public static string? Highlight(SpecFact f) => f.Topic switch
    {
        SpecTopic.Battery when IsCapacity(f) => $"باتری {f.Value}",
        SpecTopic.Battery when f.Number is > 0 && f.Value.Contains("ساعت", StringComparison.Ordinal) => $"{f.Value} شارژدهی",
        SpecTopic.NoiseCancel when IsPassive(f.Value) => null,
        SpecTopic.NoiseCancel => "حذف نویز فعال",
        SpecTopic.Water when IpMeaning(f.Value + " " + f.Key) is { } ip => $"مقاومت در برابر آب ({ip.Code})",
        SpecTopic.Water => "مقاومت در برابر آب",
        SpecTopic.WirelessCharge when f.IsYes => "شارژ بی‌سیم",
        SpecTopic.FastCharge => "شارژ سریع",
        SpecTopic.Bluetooth when f.Number is > 0 => $"بلوتوث {f.Value}",
        SpecTopic.Storage when f.Number is > 0 => $"حافظهٔ {f.Value}",
        SpecTopic.Ram when f.Number is > 0 => $"رم {f.Value}",
        SpecTopic.Refresh when f.Number is > 0 => $"صفحهٔ {f.Value}",
        SpecTopic.Processor => $"{f.Key} {f.Value}",
        SpecTopic.Port when IsUsbC(f.Value) => $"درگاه {f.Value}",
        _ => null,
    };

    /// <summary>
    /// ارزش «برجسته کردن» هر موضوع در عنوان/معرفی (۱ تا ۳). ویژگی‌هایی که خریدار برایشان جست‌وجو می‌کند بالاترند.
    /// </summary>
    public static int Weight(SpecTopic topic) => topic switch
    {
        SpecTopic.NoiseCancel or SpecTopic.Water or SpecTopic.WirelessCharge or SpecTopic.Battery or SpecTopic.Storage => 3,
        SpecTopic.FastCharge or SpecTopic.Ram or SpecTopic.Refresh or SpecTopic.Processor or SpecTopic.Camera or SpecTopic.ScreenSize => 2,
        SpecTopic.Generic or SpecTopic.Color or SpecTopic.Material => 0,
        _ => 1,
    };

    /// <summary>
    /// پرسش‌وپاسخ مخصوص مشخصه (پرسش‌ها و پاسخ‌ها جدا، با {S} = نام کوتاه). null = پرسش عمومی کافی است.
    /// مثال (Water، IPX5): «{S} ضد آب است؟» → «{S} استاندارد IPX5 دارد؛ یعنی …».
    /// </summary>
    public static (string[] Questions, string[] Answers)? Faq(SpecFact f)
    {
        var k = f.Key; var v = f.Value;
        return f.Topic switch
        {
            SpecTopic.Battery when !f.IsYes => (
                [$"[شارژدهی|باتری] {{S}} چقدر است؟", $"{k} {{S}} چقدر است؟"],
                [$"طبق مشخصات سازنده، {k} {{S}} برابر با {v} است؛ [عدد واقعی|مقدار واقعی] به شیوهٔ استفاده بستگی دارد.", $"{k} {{S}} [طبق اعلام سازنده|بر اساس مشخصات ثبت‌شده] {v} است."]),
            SpecTopic.Water when IpMeaning(v + " " + k) is { } ip => (
                ["{S} ضد آب است؟", "{S} در برابر آب مقاوم است؟"],
                [$"{{S}} استاندارد {ip.Code} دارد؛ یعنی {ip.Meaning}."]),
            SpecTopic.NoiseCancel when !IsPassive(v) => (
                ["{S} حذف نویز دارد؟", "{S} نویز کنسلینگ دارد؟"],
                ["بله، {S} [قابلیت حذف نویز|حذف نویز فعال] دارد و صدای [محیط اطراف|یکنواخت محیط] را کم می‌کند."]),
            SpecTopic.WirelessCharge when f.IsYes => (
                ["{S} شارژ بی‌سیم دارد؟", "آیا {S} با شارژر بی‌سیم شارژ می‌شود؟"],
                ["بله، {S} روی پدهای شارژ بی‌سیم سازگار شارژ می‌شود."]),
            SpecTopic.Port => (
                ["{S} با چه کابلی شارژ می‌شود؟", $"{k} {{S}} چیست؟"],
                [$"{k} {{S}} برابر با {v} است."]),
            SpecTopic.Bluetooth when f.Number is > 0 => (
                ["{S} از چه نسخه‌ای از بلوتوث پشتیبانی می‌کند؟", "نسخهٔ بلوتوث {S} چند است؟"],
                [$"{{S}} از بلوتوث نسخهٔ {v} پشتیبانی می‌کند."]),
            SpecTopic.Storage => (
                [$"{k} {{S}} چقدر است؟", "[فضای ذخیره‌سازی|حافظهٔ] {S} چقدر است؟"],
                [$"{k} {{S}} برابر با {v} است.", $"{{S}} [حافظهٔ داخلی|فضای ذخیره‌سازی] {v} دارد."]),
            SpecTopic.Weight when f.Number is > 0 => (
                ["وزن {S} چقدر است؟", "{S} چقدر وزن دارد؟"],
                [$"وزن {{S}} برابر با {v} است.", $"{{S}} وزنی برابر با {v} دارد."]),
            _ => null,
        };
    }

    /// <summary>
    /// «مناسب چه کسانی است» از روی مشخصه (فقط وقتی داده واقعاً این را نشان می‌دهد). null = نشانه‌ای نیست.
    /// مثال: حذف نویز → «کسانی که در مسیرهای شلوغ یا محیط‌های پرسروصدا رفت‌وآمد می‌کنند».
    /// </summary>
    public static string? Audience(SpecFact f) => f.Topic switch
    {
        SpecTopic.NoiseCancel when !IsPassive(f.Value) => "کسانی که در مسیرها و محیط‌های پرسروصدا رفت‌وآمد می‌کنند",
        SpecTopic.Water when IpMeaning(f.Value + " " + f.Key) is { WaterLevel: >= 4 } => "ورزشکاران و کسانی که هنگام تمرین عرق زیادی می‌کنند",
        SpecTopic.Battery when !IsCapacity(f) && f.Number is >= 20 && f.Value.Contains("ساعت", StringComparison.Ordinal) => "کسانی که سفر یا روزهای کاری طولانی دارند",
        SpecTopic.Battery when IsCapacity(f) && f.Number is >= 5000 => "کسانی که روزهای پرکاری دارند و به شارژدهی طولانی نیاز دارند",
        SpecTopic.Storage when f.Number is >= 256 && f.Value.Contains("گیگ", StringComparison.Ordinal) => "کسانی که عکس، ویدیو و فایل زیادی روی دستگاه نگه می‌دارند",
        SpecTopic.Refresh when f.Number is >= 90 => "علاقه‌مندان به بازی و کسانی که پیمایش روان برایشان مهم است",
        SpecTopic.Microphones when f.Number is >= 4 => "کسانی که زیاد تماس صوتی یا جلسهٔ آنلاین دارند",
        _ => null,
    };

    /// <summary>
    /// معنای استاندارد IP (رقم اول = گردوغبار، رقم دوم = آب). مثال: «IP68» → «کاملاً در برابر ورود گردوغبار بسته است و
    /// برای غوطه‌وری عمیق‌تر از یک متر (طبق شرایط سازنده) طراحی شده است».
    /// </summary>
    public static (string Code, string Meaning, int WaterLevel)? IpMeaning(string? text)
    {
        var match = IpPattern().Match(text ?? "");
        if (!match.Success)
            return null;
        var code = match.Value.ToUpperInvariant();
        var dust = code[2]; var water = code[3];
        var parts = new List<string>();
        switch (dust)
        {
            case '5': parts.Add("در برابر گردوغبار تا حد زیادی محافظت می‌شود"); break;
            case '6': parts.Add("کاملاً در برابر ورود گردوغبار بسته است"); break;
        }
        var level = water is >= '0' and <= '9' ? water - '0' : 0;
        switch (level)
        {
            case 1 or 2 or 3: parts.Add("در برابر چکیدن یا پاشش محدود آب محافظت دارد"); break;
            case 4: parts.Add("در برابر پاشش آب و عرق از هر جهت مقاوم است"); break;
            case 5: parts.Add("جریان ملایم آب و عرق را تحمل می‌کند"); break;
            case 6: parts.Add("در برابر جریان قوی آب مقاوم است"); break;
            case 7: parts.Add("غوطه‌وری کوتاه‌مدت تا عمق حدود یک متر را تحمل می‌کند"); break;
            case 8: parts.Add("برای غوطه‌وری عمیق‌تر از یک متر (طبق شرایط اعلام‌شدهٔ سازنده) طراحی شده است"); break;
        }
        if (parts.Count == 0)
            return null;
        return (code, string.Join(" و ", parts), level);
    }

    private static bool IsCapacity(SpecFact f) =>
        f.Value.Contains("آمپر", StringComparison.Ordinal) || PersianText.Normalize(f.Value).Contains("mah", StringComparison.Ordinal);

    private static bool IsPassive(string value)
    {
        var n = PersianText.Normalize(value);
        return n.Contains("پسیو") || n.Contains("passive") || n.Contains("غیرفعال") || n.Contains("غیر فعال");
    }

    private static bool IsUsbC(string value)
    {
        var n = PersianText.Normalize(value).Replace(" ", "");
        return n.Contains("usb-c") || n.Contains("type-c") || n.Contains("typec") || n.Contains("usbc");
    }

    /// <summary>عدد برای متن: صحیح بدون اعشار، وگرنه یک رقم اعشار (ارقام لاتین مثل خود مقدار مشخصه).</summary>
    public static string Display(double number) =>
        Math.Abs(number - Math.Round(number)) < 0.05 || number >= 10
            ? Math.Round(number).ToString("0", CultureInfo.InvariantCulture)
            : number.ToString("0.#", CultureInfo.InvariantCulture);

    /// <summary>
    /// نویسه‌های ویژهٔ الگو ([ ] | { }) در مقدار ویژگی‌ها به نویسهٔ بی‌خطر تبدیل می‌شوند تا الگوی جمله خراب نشود.
    /// </summary>
    public static string Safe(string text) =>
        text.Replace('[', '(').Replace(']', ')').Replace('{', '(').Replace('}', ')').Replace('|', '/');

    [GeneratedRegex(@"\d+(\.\d+)?", RegexOptions.CultureInvariant, 200)]
    private static partial Regex NumberPattern();

    [GeneratedRegex(@"\bip[0-6x][0-9x]\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 200)]
    private static partial Regex IpPattern();
}
