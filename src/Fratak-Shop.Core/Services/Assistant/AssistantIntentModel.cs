using Fratak_Shop.Core.Services.Learning;
using Fratak_Shop.Data.Entities.Assistant;

namespace Fratak_Shop.Core.Services.Assistant;

/// <summary>
/// دور ۲۴ – مدل یادگیرندهٔ «نیت پیام» دستیار (بیز ساده؛ <see cref="NaiveBayesText"/>).
/// <para>چرا؟ قانون‌های <see cref="AssistantNlu"/> دقیق‌اند ولی جمله‌بندی‌های تازه («سفارشم هنوز نیومده»، «کد تخفیف دارین؟»، «میشه با یه نفر حرف بزنم»)
/// را همیشه نمی‌شناسند. وقتی قانون‌ها پیام را «جست‌وجوی کالا» تشخیص دادند ولی هیچ کالایی پیدا نشد، این مدل نظر دوم می‌دهد؛
/// فقط اگر مطمئن بود (احتمال ≥ <see cref="MinConfidence"/>) پیام به آن نیت می‌رود. وگرنه همان رفتار قبلی (کالای درخواستی) می‌ماند.</para>
/// <para>یادگیری: با نمونه‌های پایهٔ زیر شروع می‌کند و هر شب از پیام‌های واقعی که قانون‌ها با اطمینان جواب داده‌اند دوباره آموزش می‌بیند
/// (<see cref="Retrain"/>؛ فراخوانی: AssistantService.RetrainIntentModelAsync از نگه‌داری شبانه). مدل فقط در حافظه است و با هر راه‌اندازی از نو ساخته می‌شود.</para>
/// </summary>
public static class AssistantIntentModel
{
    /// <summary>کمترین احتمال برای پذیرفتن نظر مدل.</summary>
    public const double MinConfidence = 0.72;

    /// <summary>نیت‌هایی که مدل اجازه دارد پیام را به آن‌ها بفرستد (همه بی‌خطر و فقط‌خواندنی؛ افزودن/حذف سبد هرگز از مدل نمی‌آید).</summary>
    public static readonly IReadOnlySet<AssistantIntent> Routable = new HashSet<AssistantIntent>
    {
        AssistantIntent.Help, AssistantIntent.Human, AssistantIntent.Track, AssistantIntent.CartShow, AssistantIntent.Checkout,
        AssistantIntent.Offers, AssistantIntent.Login, AssistantIntent.Finder, AssistantIntent.Greeting, AssistantIntent.Thanks, AssistantIntent.OffTopic,
    };

    private static readonly Lazy<NaiveBayesText> Base = new(() => NaiveBayesText.Train(Seeds()));
    private static NaiveBayesText? _learned;

    /// <summary>مدل فعلی (یادگرفته از پیام‌های واقعی، یا مدل پایه).</summary>
    public static NaiveBayesText Current => Volatile.Read(ref _learned) ?? Base.Value;

    /// <summary>
    /// نظر مدل دربارهٔ یک پیام یا null (نامطمئن، یا نیتی که مجاز نیست، یا «جست‌وجو»).
    /// <para>مثال: «سفارشم هنوز نیومده» ← Track؛ «یه هدفون خوب» ← null (جست‌وجو کار خود موتور جست‌وجوست).</para>
    /// </summary>
    public static AssistantIntent? Suggest(string text)
    {
        var model = Current;
        // بیشتر واژه‌ها باید برای مدل آشنا باشند؛ واژهٔ ناآشنا معمولاً نام کالاست و حدس زدن دربارهٔ آن خطرناک است.
        if (model.KnownWordRatio(text) < 0.6)
            return null;
        var distribution = model.Distribution(text);
        if (distribution.Count == 0)
            return null;
        var best = distribution[0];
        var margin = best.Probability - (distribution.Count > 1 ? distribution[1].Probability : 0);
        if (best.Probability < MinConfidence || margin < 0.3)
            return null;
        return Enum.TryParse<AssistantIntent>(best.Label, out var intent) && Routable.Contains(intent) ? intent : null;
    }

    /// <summary>
    /// آموزش دوباره با نمونه‌های پایه + نمونه‌های واقعی. هر نیت حداکثر ۲۰۰۰ نمونهٔ واقعی (تا یک نیت پرتکرار بقیه را نبلعد).
    /// خروجی: تعداد نمونه‌های واقعی استفاده‌شده.
    /// </summary>
    public static int Retrain(IEnumerable<(string Text, AssistantIntent Intent)> examples)
    {
        var real = examples
            .Where(e => !string.IsNullOrWhiteSpace(e.Text) && (Routable.Contains(e.Intent) || e.Intent == AssistantIntent.Search))
            .GroupBy(e => e.Intent)
            .SelectMany(g => g.Take(2000))
            .Select(e => (e.Text, e.Intent.ToString()))
            .ToList();
        Volatile.Write(ref _learned, NaiveBayesText.Train(Seeds().Concat(real)));
        return real.Count;
    }

    /// <summary>برگشت به مدل پایه (برای تست).</summary>
    public static void Reset() => Volatile.Write(ref _learned, null);

    /// <summary>نمونه‌های پایه (جمله‌بندی‌های رایج فارسی، رسمی و محاوره‌ای).</summary>
    public static IEnumerable<(string Text, string Label)> Seeds()
    {
        static IEnumerable<(string, string)> Of(AssistantIntent intent, params string[] texts) => texts.Select(t => (t, intent.ToString()));

        return Of(AssistantIntent.Track,
                "سفارشم کجاست", "سفارشم هنوز نیومده", "بسته ام نرسیده", "مرسوله من کی میرسه", "وضعیت سفارش من چیه", "سفارش من ارسال شد",
                "کد رهگیری پستی", "کی بسته ام میرسه", "خریدم رو کی میفرستید", "سفارشم دیر کرده", "پیگیری مرسوله", "هنوز به دستم نرسیده",
                "سفارشمو کی تحویل میدید", "بسته رو ارسال کردید", "کد پیگیری سفارش")
            .Concat(Of(AssistantIntent.Human,
                "میخوام با یه آدم حرف بزنم", "اپراتور", "با پشتیبانی صحبت کنم", "یه نفر جواب بده", "کارشناس", "با فروشنده حرف بزنم",
                "پشتیبان انسانی", "ربات نمیخوام", "با یک کارشناس صحبت کنم", "شماره پشتیبانی", "میشه با مسئول صحبت کنم", "تماس با پشتیبانی",
                "آدم واقعی", "وصلم کن به پشتیبانی"))
            .Concat(Of(AssistantIntent.Offers,
                "تخفیف دارید", "کد تخفیف دارین", "حراج دارید", "جشنواره دارید", "تخفیف ویژه", "کالاهای تخفیف دار", "کوپن تخفیف",
                "آف دارید", "چی تخفیف خورده", "ارزون تر نمیشه", "تخفیفاتون چیه", "پیشنهاد ویژه", "کد تخفیف بدید", "امتیاز باشگاه"))
            .Concat(Of(AssistantIntent.Login,
                "چطوری وارد بشم", "ورود به حساب", "لاگین", "رمزمو فراموش کردم", "وارد حسابم کن", "ثبت نام کنم", "عضو بشم",
                "نمیتونم وارد بشم", "رمز عبور یادم رفته", "ورود با کد پیامکی", "میخوام لاگین کنم", "حساب کاربری بسازم"))
            .Concat(Of(AssistantIntent.Finder,
                "کمکم کن انتخاب کنم", "نمیدونم چی بخرم", "چی پیشنهاد میدی", "یه پیشنهاد خوب بده", "برای هدیه چی بخرم", "کدوم بهتره برام",
                "راهنمایی برای خرید", "مشاوره خرید", "بهترین گزینه با بودجه من", "پیشنهاد خرید بده", "کمک تو انتخاب", "چی بخرم خوبه"))
            .Concat(Of(AssistantIntent.CartShow,
                "سبد خریدم", "سبد من", "چی تو سبدمه", "سبد خرید رو نشون بده", "محتویات سبد", "سبدم رو ببینم", "چند تا کالا تو سبدمه"))
            .Concat(Of(AssistantIntent.Checkout,
                "تسویه حساب", "میخوام پرداخت کنم", "نهایی کردن خرید", "ثبت سفارش", "پرداخت کنم", "خریدمو تموم کنم", "ادامه خرید و پرداخت"))
            .Concat(Of(AssistantIntent.Help,
                "چه کارایی میتونی بکنی", "کمک", "راهنما", "چطور کار میکنی", "تو کی هستی", "چیکار میکنی", "چی بلدی", "معرفی کن خودتو"))
            .Concat(Of(AssistantIntent.Greeting, "سلام", "درود", "سلام وقت بخیر", "صبح بخیر", "عصر بخیر", "سلام خوبی", "هلو", "سلام علیکم"))
            .Concat(Of(AssistantIntent.Thanks, "ممنون", "مرسی", "سپاس", "دستت درد نکنه", "خیلی ممنونم", "تشکر", "عالی بود ممنون", "لطف کردی"))
            .Concat(Of(AssistantIntent.OffTopic,
                "هوا چطوره", "فوتبال دیشب", "جوک بگو", "معنی زندگی چیه", "دلار چنده", "اخبار امروز", "شعر بگو", "نتیجه بازی"))
            .Concat(Of(AssistantIntent.Search,
                "هدفون بلوتوثی", "پاوربانک ۲۰ هزار", "گوشی سامسونگ", "کابل شارژ تایپ سی", "ساعت هوشمند", "اسپیکر قابل حمل", "کیف لپ تاپ",
                "شارژر فندکی", "ماوس بی سیم", "کیبورد گیمینگ", "فلش ۶۴ گیگ", "قاب گوشی آیفون", "محافظ صفحه", "هندزفری", "لامپ ال ای دی",
                "دوربین مداربسته", "مودم", "هارد اکسترنال", "پایه نگهدارنده موبایل", "چراغ قوه"));
    }
}
