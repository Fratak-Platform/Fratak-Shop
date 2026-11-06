using Fratak_Shop.Data.Entities.SeoEngine;

namespace Fratak_Shop.Core.Services.Seo.Engine;

// ═══════════════════════════════════════════════════════════════════════════
//  نگارش متنوع متن صفحه و پرسش‌وپاسخ (مرحلهٔ یازدهم)
//  هدف: دو محصول مشابه (مثلاً دو هدفون از یک دسته) هرگز متن قالبی یکسان نگیرند.
//  روش «نگارنده» (Composer) برای هر صفحه:
//   ۱) هر جمله از چند الگوی هم‌معنی ساخته می‌شود و هر الگو با Spin ده‌ها شکل دارد.
//   ۲) نامزدها امتیاز می‌گیرند: روانی (مدل زبانی سایت) − «قالبی بودن» در صفحه‌های هم‌نوع (PhraseUsage)
//      − تکرار شروع جمله در همین صفحه − تکرار اسکلت در همین صفحه + نویز ثابت هر صفحه.
//   ۳) به محصول با عبارت‌های گوناگون اشاره می‌شود («این هدفون»، «هایلو T15»، «این مدل» …) تا نام کامل در هر جمله
//      تکرار نشود (تکرار زیاد = keyword stuffing).
//   ۴) محتوای واقعی هر محصول: توضیح معنای مشخصات (SpecKnowledge)، مقایسه با میانگین هم‌دسته‌ها در خود فروشگاه
//      (CategoryProfile) و «مناسب چه کسانی است» — همه فقط از داده‌های واقعی.
//   ۵) ترتیب بخش‌ها و اینکه کدام شرایط فروشگاه گفته شود، برای هر صفحه فرق می‌کند.
//   ۶) نگهبان نهایی: اگر متن ساخته‌شده هنوز به متن صفحهٔ دیگری خیلی شبیه بود (SimHash)، با دانهٔ دیگری دوباره نوشته می‌شود.
// ═══════════════════════════════════════════════════════════════════════════

public static partial class ContentWriter
{
    /// <summary>
    /// نگارندهٔ یک صفحه: وضعیت داخل صفحه (شروع جمله‌های استفاده‌شده، اشاره‌های قبلی) را نگه می‌دارد و از بین
    /// نامزدهای هر جمله، طبیعی‌ترین و کم‌تکرارترین را انتخاب می‌کند. برای هر (صفحه، نسخه، salt) نتیجه ثابت است.
    /// </summary>
    private sealed class Composer
    {
        private readonly WriterContext _ctx;
        private readonly Random _random;
        private readonly HashSet<string> _openers = new(StringComparer.Ordinal);
        private readonly HashSet<string> _skeletons = new(StringComparer.Ordinal);
        private readonly List<string> _mentions;
        private string? _lastMention;

        public Composer(WriterContext ctx, int salt)
        {
            _ctx = ctx;
            _random = new Random((int)(TextModels.Fnv1a($"{ctx.Subject.EntityId}:{ctx.Variant}:{salt}:compose") & 0x7FFFFFFF));
            _mentions = Mentions(ctx);
        }

        public WriterContext Ctx => _ctx;

        /// <summary>عدد تصادفی ثابت این صفحه (برای ترتیب بخش‌ها و چرخش شرایط فروشگاه).</summary>
        public int Next(int max) => _random.Next(max);

        /// <summary>
        /// یک جمله از بین الگوها. جای‌نگهدارها: {L} عبارت اصلی (کلمهٔ کلیدی)، {S} نام کوتاه، {N} اشارهٔ متنوع،
        /// {B} برند، {C} دسته، {T} نوع کالا، {SITE} نام فروشگاه. late = جای‌نگهدارهایی که بعد از انتخاب پر می‌شوند
        /// (مثلاً لینک‌های HTML که نباید در امتیازدهی اثر بگذارند).
        /// </summary>
        public string Say(IEnumerable<string> templates, IReadOnlyDictionary<string, string>? late = null)
        {
            var list = templates.Where(t => t.Length > 0).ToList();
            var mention = list.Any(t => t.Contains("{N}", StringComparison.Ordinal)) ? Mention() : "";
            var best = list.SelectMany(t => Spin.Expand(t))
                .Select(t => Fill(t, mention))
                .Distinct(StringComparer.Ordinal)
                .Select(text => (Text: text, Score: Score(text)))
                .OrderByDescending(x => x.Score)
                .ThenBy(x => x.Text, StringComparer.Ordinal)
                .First().Text;
            Remember(best);
            if (late is not null)
                foreach (var (key, value) in late)
                    best = best.Replace(key, value, StringComparison.Ordinal);
            return best;
        }

        public string Say(params string[] templates) => Say((IEnumerable<string>)templates);

        /// <summary>اشارهٔ بعدی به محصول (هیچ‌وقت دو بار پشت سر هم یکسان نیست).</summary>
        private string Mention()
        {
            var options = _mentions.Where(m => m != _lastMention).ToList();
            var pick = options[_random.Next(options.Count)];
            _lastMention = pick;
            return pick;
        }

        private double Score(string text)
        {
            var opener = Opener(text);
            return 0.6 * _ctx.Fluency(text)
                   + (_random.NextDouble() - 0.5) * 0.9
                   - 2.2 * _ctx.Commonness(text)
                   - (opener.Length > 0 && _openers.Contains(opener) ? 1.0 : 0)
                   - (_skeletons.Contains(PhraseUsage.Skeleton(text, _ctx.Terms)) ? 3.0 : 0)
                   - 0.3 * LongWords(text);
        }

        // مجموع واژه‌های اضافه بر ۲۰ در جمله‌های بلند (خوانایی: جمله‌های بیش از ۲۰ واژه نباید زیاد باشند).
        // مثال: یک جملهٔ ۲۶ واژه‌ای → ۶.
        private static int LongWords(string text) =>
            PersianText.Sentences(text).Sum(sentence => Math.Max(0, PersianText.WordCount(sentence) - 20));

        private void Remember(string text)
        {
            var opener = Opener(text);
            if (opener.Length > 0)
                _openers.Add(opener);
            _skeletons.Add(PhraseUsage.Skeleton(text, _ctx.Terms));
        }

        // دو واژهٔ اول اسکلت. مثال: «◊ دارای شارژدهی …» → «◊ دارای»؛ «این هدفون از …» → «این هدفون».
        private string Opener(string text) =>
            string.Join(' ', PhraseUsage.Skeleton(text, _ctx.Terms).Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2));

        private string Fill(string template, string mention)
        {
            var p = _ctx.Subject.Product;
            // اگر اشاره به عدد/حرف لاتین ختم شود و بلافاصله بعدش عدد یا واژهٔ لاتین بیاید («هایلو T15 45 گرم»، «P3 IPX5»)،
            // اشارهٔ فارسی جایگزین می‌شود تا دو رشتهٔ لاتین/عددی به هم نچسبند.
            var index = template.IndexOf("{N}", StringComparison.Ordinal);
            while (index >= 0)
            {
                var rest = template[(index + 3)..].TrimStart();
                var use = mention.Length > 0 && !PersianText.IsPersianLetter(mention[^1]) && rest.Length > 0 && (char.IsDigit(rest[0]) || char.IsAsciiLetter(rest[0]))
                    ? (_ctx.TypeWord is { } type ? "این " + type : "این محصول")
                    : mention;
                template = template[..index] + use + template[(index + 3)..];
                index = template.IndexOf("{N}", index + use.Length, StringComparison.Ordinal);
            }
            return template
                .Replace("{L}", _ctx.Lead, StringComparison.Ordinal)
                .Replace("{S}", _ctx.ShortName, StringComparison.Ordinal)
                .Replace("{B}", p?.BrandName ?? "", StringComparison.Ordinal)
                .Replace("{C}", p?.CategoryName ?? PersianText.Clean(_ctx.Subject.Name), StringComparison.Ordinal)
                .Replace("{T}", _ctx.TypeWord ?? "محصول", StringComparison.Ordinal)
                .Replace("{SITE}", _ctx.Store.SiteName, StringComparison.Ordinal);
        }

        /// <summary>
        /// عبارت‌های اشاره به محصول. مثال برای «هدفون بی‌سیم هایلو مدل T15»: «این هدفون»، «هایلو T15»، «هدفون T15»،
        /// «این مدل»، «این محصول».
        /// </summary>
        private static List<string> Mentions(WriterContext ctx)
        {
            var list = new List<string>();
            var type = ctx.TypeWord;
            var model = ctx.ModelName;
            var brand = ctx.Subject.Product?.BrandName;
            if (ctx.Subject.Kind == SeoEntityKind.Product)
            {
                if (type is not null) list.Add("این " + type);
                if (brand is not null && model is not null) list.Add(PersianText.Clean(brand + " " + model));
                if (type is not null && model is not null) list.Add(type + " " + model);
                list.Add("این مدل");
                list.Add("این محصول");
            }
            else
            {
                list.Add(ctx.ShortName);
                list.Add(ctx.Subject.Kind == SeoEntityKind.Brand ? "این برند" : "این دسته");
            }
            return list.Distinct(StringComparer.Ordinal).ToList();
        }
    }

    // ── متن محصول ────────────────────────────────────────────────────────

    private static List<string> ProductSections(Composer c)
    {
        var ctx = c.Ctx;
        var p = ctx.Subject.Product;
        var facts = ctx.Distinctive;
        var sections = new List<string>();

        // ۱) معرفی (همیشه اول و شامل کلمهٔ کلیدی).
        var intro = new List<string>();
        if (p?.BrandName is not null && p.CategoryName is not null)
            intro.Add(c.Say(
                "{L} از [محصولات|مدل‌های] برند {B} در دستهٔ {C} است.",
                "{L} [یکی از|از جمله] [محصولات|مدل‌های] دستهٔ {C} است که برند {B} [آن را عرضه کرده است|تولیدش کرده است].",
                "{L} محصولی از برند {B} است و در گروه {C} قرار می‌گیرد.",
                "برند {B} در دستهٔ {C}، {L} را [عرضه کرده است|به بازار آورده است].",
                "[در این صفحه|اینجا] با {L}، [یکی از|از] [مدل‌های|محصولات] {C} برند {B}، آشنا می‌شوید.",
                "{L} [گزینه‌ای|انتخابی] از برند {B} برای کسانی است که دنبال {C} هستند."));
        else if (p?.CategoryName is not null)
            intro.Add(c.Say(
                "{L} از محصولات دستهٔ {C} است.",
                "{L} در گروه {C} قرار می‌گیرد.",
                "[در این صفحه|اینجا] با {L} از دستهٔ {C} آشنا می‌شوید."));
        else
            intro.Add(c.Say(
                "[در این صفحه|اینجا] [مشخصات و شرایط خرید|ویژگی‌ها و شرایط خرید] {L} را می‌بینید.",
                "{L} را [در این صفحه|اینجا] با جزئیات [بشناسید|بررسی کنید]."));

        var top = facts.Take(3).Select(FactPhrase).ToList();
        if (top.Count >= 2)
        {
            var joined = PersianText.JoinList(top);
            intro.Add(c.Say(
                $"[مهم‌ترین|برجسته‌ترین] ویژگی‌های {{N}} {joined} است.",
                $"{joined} از [نکات|ویژگی‌های] [قابل توجه|اصلی] {{N}} [به شمار می‌رود|است].",
                $"{{N}} با {joined} عرضه می‌شود.",
                $"[در نگاه اول|به‌طور خلاصه]، {{N}} [با|به خاطر] {joined} [شناخته می‌شود|جلب توجه می‌کند]."));
        }
        else if (top.Count == 1)
            intro.Add(c.Say($"{{N}} [با|دارای] {top[0]} است.", $"{top[0]} از ویژگی‌های {{N}} است."));

        if (WarrantyPhrase(p) is { } warranty)
        {
            var w = SpecKnowledge.Safe(warranty);
            intro.Add(c.Say(
                $"{{N}} [با|همراه با] {w} [عرضه می‌شود|به دست شما می‌رسد].",
                $"{w} [برای|همراه] {{N}} [در نظر گرفته شده است|ارائه می‌شود].",
                $"[خرید|سفارش] {{N}} [با|همراه با] {w} [انجام می‌شود|همراه است]."));
        }
        sections.Add(P(string.Join(' ', intro)));

        // ۲) توضیح مشخصات (فقط با حداقل ۲ مشخصهٔ قابل استفاده).
        if (facts.Count >= 2)
        {
            sections.Add(H2(c.Say(
                "ویژگی‌های {S}", "نگاهی به مشخصات {S}", "مشخصات کلیدی {S}", "{S} از نظر فنی",
                "[بررسی|معرفی] ویژگی‌های {S}", "{S} چه ویژگی‌هایی دارد؟")));
            var sentences = new List<string>();
            // ترتیب: موضوع‌های شناخته‌شده (با توضیح واقعی) اول، سپس بقیه.
            foreach (var fact in facts.OrderBy(f => f.Topic == SpecTopic.Generic ? 1 : 0).Take(7))
                sentences.Add(c.Say(SpecKnowledge.Explain(fact)));
            if (ctx.Subject.Product?.Features.Count > 7 || c.Next(3) == 0)
                sentences.Add(c.Say(
                    "[فهرست کامل|جزئیات کامل] مشخصات [فنی|] در [جدول مشخصات|بخش مشخصات] همین صفحه آمده است.",
                    "بقیهٔ مشخصات را در جدول [مشخصات فنی|مشخصات] [همین صفحه|پایین صفحه] ببینید."));
            // پاراگراف‌های کوتاه‌تر خواناترند (هر پاراگراف حداکثر ۴ جمله).
            for (var i = 0; i < sentences.Count; i += 4)
                sections.Add(P(string.Join(' ', sentences.Skip(i).Take(4))));
        }

        // ۳ و ۴) مقایسه با هم‌دسته‌ها و «مناسب چه کسانی است» (ترتیبشان برای هر صفحه فرق می‌کند).
        var extras = new List<List<string>>();
        var compare = CompareSentences(c);
        if (compare.Count > 0)
            extras.Add([
                H2(c.Say("{S} در مقایسه با مدل‌های مشابه", "مقایسهٔ {S} با [سایر|دیگر] [مدل‌های|محصولات] {C}", "{S} [در کنار|در برابر] [هم‌رده‌ها|مدل‌های هم‌رده]")),
                P(string.Join(' ', compare)),
            ]);
        // حداکثر ۲ گروه تا جمله بلند و سنگین نشود.
        var audience = facts.Select(SpecKnowledge.Audience).OfType<string>().Distinct(StringComparer.Ordinal).Take(2).ToList();
        if (audience.Count > 0)
        {
            // هر گروه در جملهٔ جدا تا جمله بیش از حد بلند نشود.
            var first = c.Say(
                $"{{N}} [به‌ویژه|بیشتر] برای {audience[0]} [مناسب است|گزینهٔ مناسبی است].",
                $"[ویژگی‌های|مشخصات] {{N}} آن را برای {audience[0]} مناسب می‌کند.",
                $"{{N}} [انتخاب سنجیده‌ای|گزینهٔ قابل اعتمادی] برای {audience[0]} است.");
            var second = audience.Count > 1
                ? " " + c.Say(
                    $"[همچنین|علاوه بر این،] برای {audience[1]} [انتخاب خوبی است|به کار می‌آید].",
                    $"{audience[1]} [هم|نیز] می‌توانند از آن [بهره ببرند|استفاده کنند].")
                : "";
            extras.Add([
                H2(c.Say("{S} مناسب چه کسانی است؟", "[این|] {T} برای چه کسانی مناسب است؟", "[چه کسانی|چه کاربرانی] از {S} [بیشتر لذت می‌برند|بیشترین بهره را می‌برند]؟")),
                P(first + second),
            ]);
        }
        if (extras.Count == 2 && c.Next(2) == 1)
            extras.Reverse();
        foreach (var extra in extras)
            sections.AddRange(extra);

        // ۵) شرایط خرید (کلمهٔ کلیدی در تیتر) + فقط بخشی از شرایط فروشگاه (چرخشی) + لینک داخلی.
        var buy = new List<string>
        {
            c.Say(
                "برای خرید {L} [کافی است|فقط کافی است] آن را به سبد خرید اضافه کنید و سفارش را آنلاین ثبت کنید.",
                "خرید {L} از {SITE} [به‌صورت آنلاین و|] در چند مرحلهٔ ساده انجام می‌شود.",
                "{L} را می‌توانید [همین حالا|] [به سبد خرید اضافه کنید|آنلاین سفارش دهید] و [روند|مراحل] [سفارش|ارسال] را از حساب کاربری [دنبال کنید|پیگیری کنید]."),
        };
        buy.AddRange(StoreSentences(c, 2));
        var links = new List<string>();
        if (p?.CategorySlug is { } categorySlug && p.CategoryName is { } categoryName)
            links.Add(A("/category/" + categorySlug, categoryName));
        if (p?.BrandSlug is { } brandSlug && p.BrandName is { } brandName)
            links.Add(A("/brand/" + brandSlug, "محصولات " + brandName));
        if (links.Count > 0)
            buy.Add(c.Say(
                [
                    "برای دیدن [مدل‌های مشابه|گزینه‌های دیگر]، [صفحهٔ|به صفحهٔ] {LINKS} [را ببینید|سر بزنید].",
                    "[اگر می‌خواهید مقایسه کنید|برای مقایسه]، [سایر محصولات|مدل‌های دیگر] {LINKS} را هم ببینید.",
                    "[مدل‌های|گزینه‌های] دیگر را در {LINKS} [پیدا می‌کنید|ببینید].",
                ],
                new Dictionary<string, string> { ["{LINKS}"] = PersianText.JoinList(links) }));
        sections.Add(H2(c.Say("خرید {L}", "خرید {L} از {SITE}", "[سفارش|خرید] آنلاین {L}", "شرایط خرید {L}")));
        sections.Add(P(string.Join(' ', buy)));
        return sections;
    }

    /// <summary>
    /// مقایسه با بقیهٔ محصولات همان دسته در خود فروشگاه (فقط عددهای واقعی و فقط با اختلاف معنادار ≥۱۵٪).
    /// مثال: «شارژدهی این هدفون (40 ساعت) از میانگین مدل‌های هدفون بی‌سیم در فراتک (حدود 30 ساعت) بیشتر است.»
    /// ویژگی «دارد» که کمتر از ۳۵٪ هم‌دسته‌ها دارند هم گفته می‌شود.
    /// </summary>
    private static List<string> CompareSentences(Composer c)
    {
        var ctx = c.Ctx;
        var profile = ctx.Profile;
        var category = ctx.Subject.Product?.CategoryName;
        var list = new List<string>();
        if (profile is null || profile.Count < 4 || category is null)
            return list;
        foreach (var fact in ctx.Facts)
        {
            if (list.Count >= 2)
                break;
            if (fact.Direction != 0 && fact.Number is > 0 && profile.Average(ctx.Subject.EntityId, fact.Key) is { } average && average.Average > 0)
            {
                var ratio = fact.Number.Value / average.Average;
                var unit = Unit(fact.Value);
                var avgText = SpecKnowledge.Display(average.Average) + (unit.Length > 0 ? " " + unit : "");
                if (fact.Direction > 0 && ratio >= 1.15)
                    list.Add(c.Say(
                        $"{fact.Key} {{N}} ({fact.Value}) از میانگین [مدل‌های|محصولات] {{C}} در {{SITE}} (حدود {avgText}) [بیشتر|بالاتر] است.",
                        $"[با|با داشتن] {fact.Key} {fact.Value}، {{N}} [بالاتر از|بیش از] میانگین [مدل‌های|محصولات] {{C}} [این فروشگاه|در {{SITE}}] (حدود {avgText}) قرار می‌گیرد."));
                else if (fact.Direction < 0 && ratio <= 0.85)
                    list.Add(c.Say(
                        $"[با|با وزن] {fact.Value}، {{N}} از میانگین [مدل‌های|محصولات] {{C}} در {{SITE}} (حدود {avgText}) سبک‌تر است.",
                        $"{{N}} [با|] {fact.Value} [وزن|]، سبک‌تر از میانگین [مدل‌های|محصولات] {{C}} [این فروشگاه|در {{SITE}}] (حدود {avgText}) است."));
            }
            else if (fact.IsYes && profile.Share(ctx.Subject.EntityId, fact.Key, fact.Value) <= 0.35)
            {
                list.Add(c.Say(
                    $"[برخلاف|بر خلاف] بیشتر [مدل‌های|محصولات] {{C}} در {{SITE}}، {{N}} {fact.Key} دارد.",
                    $"{fact.Key} [ویژگی‌ای است که|قابلیتی است که] [در بیشتر|در اغلب] [مدل‌های|محصولات] {{C}} [این فروشگاه|در {{SITE}}] [نیست|دیده نمی‌شود] ولی {{N}} آن را دارد."));
            }
        }
        return list;
    }

    // واحد مقدار (بخش غیرعددی). مثال: «40 ساعت» → «ساعت»؛ «5.3» → «».
    private static string Unit(string value) =>
        PersianText.Clean(new string(value.Where(ch => !char.IsDigit(ch) && ch is not ('.' or '٫' or '/')).ToArray())).Trim('،', ',', '-', ' ');

    /// <summary>
    /// جمله‌های شرایط فروشگاه، فقط «max» تا و با چرخش بین صفحه‌ها (نه همهٔ شرایط در همهٔ صفحه‌ها) و هر کدام با چند بیان.
    /// </summary>
    private static List<string> StoreSentences(Composer c, int max)
    {
        var store = c.Ctx.Store;
        var pool = new List<string[]>();
        if (store.DeliveryMaxDays > 0)
        {
            var a = PersianText.Digits(store.DeliveryMinDays);
            var b = PersianText.Digits(store.DeliveryMaxDays);
            pool.Add([
                $"سفارش پس از ارسال [معمولاً|به‌طور معمول] ظرف {a} تا {b} روز کاری [به دستتان می‌رسد|تحویل داده می‌شود][ و وضعیت مرسوله را می‌توانید در حساب کاربری پیگیری کنید|].",
                $"[زمان تحویل|مدت رسیدن مرسوله] [معمولاً|به‌طور معمول] {a} تا {b} روز کاری پس از ارسال است[ و کد رهگیری در حساب کاربری نمایش داده می‌شود|].",
            ]);
        }
        if (store.ReturnEnabled && store.ReturnDays > 0)
        {
            var d = PersianText.Digits(store.ReturnDays);
            pool.Add([
                $"تا {d} روز پس از تحویل [هم|] امکان ثبت درخواست مرجوعی وجود دارد.",
                $"[اگر کالا مطابق انتظارتان نبود،|در صورت نیاز،] تا {d} روز پس از تحویل می‌توانید درخواست مرجوعی ثبت کنید.",
            ]);
        }
        if (store.FreeShippingThreshold > 0)
        {
            var m = PersianText.Money(store.FreeShippingThreshold);
            pool.Add([
                $"ارسال سفارش‌های بالای {m} تومان رایگان است.",
                $"[برای|در] سفارش‌های بیش از {m} تومان، [هزینهٔ ارسال|ارسال] [رایگان است|با فروشگاه است].",
            ]);
        }
        if (store.CashOnDelivery)
            pool.Add([
                "در صورت فراهم بودن شرایط، پرداخت در محل هم ممکن است.",
                "[امکان|گزینهٔ] پرداخت در محل [هم|] برای سفارش‌های واجد شرایط [فراهم است|وجود دارد].",
            ]);

        // چرخش: شروع از یک نقطهٔ ثابت برای هر صفحه؛ صفحه‌های مختلف شرایط متفاوتی را ذکر می‌کنند.
        var list = new List<string>();
        if (pool.Count == 0)
            return list;
        var start = c.Next(pool.Count);
        for (var i = 0; i < Math.Min(max, pool.Count); i++)
            list.Add(c.Say(pool[(start + i) % pool.Count]));
        return list;
    }

    /// <summary>عبارت اسمی یک مشخصه. مثال: («نویز کنسلینگ»، دارد) → «نویز کنسلینگ»؛ («شارژدهی»، «40 ساعت») → «شارژدهی 40 ساعت».</summary>
    private static string FactPhrase(SpecFact fact) => fact.IsYes ? fact.Key : fact.Key + " " + fact.Value;

    // ── متن دسته و برند ──────────────────────────────────────────────────

    private static List<string> ListingSections(Composer c)
    {
        var ctx = c.Ctx;
        var s = ctx.Subject;
        var listing = s.Listing;
        if (listing is null || listing.ProductCount == 0)
            return [];
        var name = SpecKnowledge.Safe(PersianText.Clean(s.Name));
        var sections = new List<string>();
        var count = listing.ProductCount >= 10 ? $"بیش از {PersianText.Digits(listing.ProductCount / 10 * 10)} مدل" : "مدل‌های مختلف";
        var groups = listing.RelatedGroups.Take(4).ToList();
        var groupLinks = new Dictionary<string, string> { ["{GROUPS}"] = PersianText.JoinList(groups.Select(g => A(g.Path, g.Text)).ToList()) };

        var intro = new List<string>();
        if (s.Kind == SeoEntityKind.Category)
        {
            intro.Add(c.Say(
                $"در این صفحه {count} از {{L}} را با قیمت روز و مشخصات کامل می‌بینید.",
                $"{{L}} در {{SITE}} با {count} و امکان مقایسهٔ مشخصات عرضه می‌شود.",
                $"این صفحه {count} از {{L}} را یک‌جا نشان می‌دهد تا راحت‌تر [مقایسه و انتخاب کنید|انتخاب کنید].",
                $"[برای انتخاب|برای خرید] {{L}}، {count} را در این صفحه [کنار هم|یک‌جا] می‌بینید."));
            if (groups.Count >= 2)
                intro.Add(c.Say(
                    [
                        "محصولات این دسته از برندهایی مانند {GROUPS} عرضه می‌شوند.",
                        "برندهای {GROUPS} از جمله برندهای این دسته‌اند.",
                        "[در این دسته|اینجا] [محصولات|مدل‌هایی] از برندهای {GROUPS} [را پیدا می‌کنید|عرضه شده است].",
                    ], groupLinks));
            if (listing.ParentName is { } parent)
                intro.Add(c.Say($"این دسته زیرمجموعهٔ {SpecKnowledge.Safe(parent)} است.", $"{{S}} [بخشی از|از زیرمجموعه‌های] {SpecKnowledge.Safe(parent)} است."));
        }
        else
        {
            intro.Add(c.Say(
                $"در این صفحه {count} از {{L}} را با قیمت روز و مشخصات کامل می‌بینید.",
                $"{{SITE}} {count} از محصولات {name} را با امکان مقایسهٔ مشخصات عرضه می‌کند.",
                $"[محصولات|کالاهای] {name} در {{SITE}} [با|در] {count} [عرضه می‌شوند|در دسترس‌اند]؛ این صفحه همه را یک‌جا نشان می‌دهد."));
            if (groups.Count >= 1)
                intro.Add(c.Say(
                    [
                        $"محصولات {name} در {{SITE}} در دسته‌های {{GROUPS}} قرار دارند.",
                        $"[کالاهای|محصولات] {name} را در دسته‌های {{GROUPS}} [پیدا می‌کنید|می‌بینید].",
                    ], groupLinks));
        }
        sections.Add(P(string.Join(' ', intro)));

        if (listing.SubCategories.Count >= 2)
        {
            sections.Add(H2(c.Say("زیردسته‌های این بخش", "[انواع|دسته‌بندی] محصولات این بخش", "[زیرمجموعه‌ها|بخش‌های این دسته]")));
            sections.Add(P(c.Say(
                [
                    "برای پیدا کردن سریع‌تر محصول مورد نظر، یکی از زیردسته‌ها را انتخاب کنید: {SUBS}.",
                    "[زیردسته‌های|بخش‌های] این دسته {SUBS} [هستند|است]؛ [با انتخاب هرکدام|با رفتن به هرکدام] [سریع‌تر|راحت‌تر] به محصول مورد نظر می‌رسید.",
                ],
                new Dictionary<string, string> { ["{SUBS}"] = PersianText.JoinList(listing.SubCategories.Take(8).Select(x => A(x.Path, x.Text)).ToList()) })));
        }

        // راهنمای انتخاب: تنها تیتری که نام دسته/برند را دارد (برای «کلمهٔ کلیدی در تیتر»)؛ جمله‌ها بدون تکرار نام
        // تا تراکم کلمهٔ کلیدی در متن کوتاه صفحه‌های فهرستی بالا نرود.
        var guide = new List<string>();
        if (listing.CommonFeatureKeys.Count >= 2)
        {
            var keys = PersianText.JoinList(listing.CommonFeatureKeys.Take(4).Select(SpecKnowledge.Safe).ToList());
            guide.Add(c.Say(
                $"هنگام انتخاب به {keys} توجه کنید؛ این مشخصات برای هر محصول در صفحهٔ همان محصول آمده است.",
                $"مهم‌ترین مشخصاتی که [در این دسته|بین این مدل‌ها] باید مقایسه کنید {keys} است.",
                $"[پیش از خرید|قبل از انتخاب]، {keys} را [در مدل‌های مختلف|بین گزینه‌ها] [مقایسه کنید|کنار هم بگذارید]."));
        }
        guide.Add(c.Say(
            "با ابزار مقایسه می‌توانید چند مدل را کنار هم ببینید و تفاوت‌هایشان را بسنجید.",
            "[ابزار مقایسه|بخش مقایسه] [کمک می‌کند|به شما کمک می‌کند] چند مدل را [هم‌زمان|کنار هم] بررسی کنید."));
        guide.Add(c.Say(
            "[مرتب‌سازی|چیدن] فهرست بر اساس قیمت یا جدیدترین محصولات، [پیدا کردن گزینهٔ مناسب|انتخاب] را [سریع‌تر|ساده‌تر] می‌کند.",
            "با فیلتر کالاهای موجود، فقط محصولاتی را می‌بینید که همین حالا قابل سفارش‌اند."));
        if (listing.WarrantyPercent >= 60)
            guide.Add(c.Say(
                $"حدود {PersianText.Digits(listing.WarrantyPercent / 10 * 10)}٪ [محصولات|کالاهای] این بخش گارانتی دارند؛ نوع و مدت گارانتی هر کالا در صفحهٔ آن نوشته شده است.",
                "[اغلب|بیشتر] کالاهای این بخش [همراه با|با] گارانتی عرضه می‌شوند و [جزئیات|نوع و مدت] آن در صفحهٔ هر محصول آمده است."));
        sections.Add(H2(s.Kind == SeoEntityKind.Category
            ? c.Say($"راهنمای خرید {name}", $"نکات انتخاب {name}", $"[چطور|چگونه] {name} مناسب را انتخاب کنیم؟")
            : c.Say($"راهنمای خرید محصولات {name}", $"انتخاب از میان محصولات {name}", $"[چطور|چگونه] از میان محصولات {name} انتخاب کنیم؟")));
        sections.Add(P(string.Join(' ', guide)));

        if (listing.TopProducts.Count >= 2)
        {
            sections.Add(H2(c.Say("پرطرفدارترین مدل‌ها", "محصولات محبوب این بخش", "[پربازدیدترین|پرطرفدارترین] [مدل‌ها|محصولات]")));
            sections.Add("<ul>" + string.Concat(listing.TopProducts.Take(6).Select(t => "<li>" + A(t.Path, t.Text) + "</li>")) + "</ul>");
        }

        var buy = new List<string>
        {
            c.Say(
                "[سفارش|خرید] از {SITE} [به‌صورت آنلاین|آنلاین] و در چند مرحلهٔ ساده انجام می‌شود.",
                "برای خرید، محصول مورد نظر را به سبد خرید اضافه کنید و سفارش را آنلاین ثبت کنید.",
                "هر محصولی را که انتخاب کردید به سبد خرید اضافه کنید و [سفارش|خرید] را آنلاین [ثبت|تکمیل] کنید."),
        };
        buy.AddRange(StoreSentences(c, 3));
        sections.Add(H2(c.Say("شرایط خرید از {SITE}", "[خرید|سفارش] آنلاین از {SITE}", "ارسال، پرداخت و مرجوعی")));
        sections.Add(P(string.Join(' ', buy)));
        return sections;
    }

    // ── پرسش‌وپاسخ متنوع ─────────────────────────────────────────────────

    /// <summary>
    /// پرسش‌وپاسخ محصول/دسته/برند با بیان‌های گوناگون: پرسش‌های مخصوص مشخصات (ضد آب است؟ شارژدهی چقدر است؟)،
    /// و از شرایط فروشگاه فقط مرجوعی (اگر فعال است) + یکی از بقیه به‌صورت چرخشی تا همهٔ صفحه‌ها پرسش‌های یکسان نداشته باشند.
    /// </summary>
    private static List<SeoFaqItem> CommercialFaq(WriterContext ctx)
    {
        var s = ctx.Subject;
        var c = new Composer(ctx, 7);
        var items = new List<SeoFaqItem>();
        var store = ctx.Store;

        switch (s.Kind)
        {
            case SeoEntityKind.Product:
            {
                if (WarrantyPhrase(s.Product) is { } warranty)
                {
                    var w = SpecKnowledge.Safe(warranty);
                    items.Add(new(
                        c.Say("{S} گارانتی دارد؟", "آیا {S} گارانتی دارد؟", "گارانتی {S} چیست؟"),
                        c.Say($"{{S}} با {w} عرضه می‌شود.", $"{{S}} [همراه با|با] {w} [ارائه می‌شود|به دست شما می‌رسد].")));
                }
                var specific = 0;
                foreach (var fact in ctx.Facts.OrderBy(f => SpecKnowledge.Faq(f) is null ? 1 : 0))
                {
                    if (specific >= 3)
                        break;
                    if (SpecKnowledge.Faq(fact) is { } known)
                        items.Add(new(c.Say(known.Questions), c.Say(known.Answers)));
                    else if (!fact.IsYes && specific < 2)
                    {
                        var asks = fact.Value.Any(char.IsDigit) ? "چقدر است" : "چیست";
                        items.Add(new(
                            c.Say($"{fact.Key} {{S}} {asks}؟", $"[در|] {{S}} {fact.Key} {asks}؟"),
                            c.Say($"{{S}} دارای {fact.Key} {fact.Value} است.", $"طبق مشخصات [ثبت‌شده|سازنده]، {fact.Key} {{S}} برابر با {fact.Value} است.")));
                    }
                    else
                        continue;
                    specific++;
                }
                break;
            }
            case SeoEntityKind.Category when s.Listing is not null:
            {
                var name = SpecKnowledge.Safe(s.Name);
                var brands = s.Listing.RelatedGroups.Select(g => SpecKnowledge.Safe(g.Text)).Take(5).ToList();
                if (brands.Count >= 2)
                    items.Add(new(
                        c.Say($"{name} از چه برندهایی موجود است؟", $"[چه برندهایی|کدام برندها] در دستهٔ {name} [هست|عرضه می‌شود]؟"),
                        c.Say($"در این دسته محصولات برندهای {PersianText.JoinList(brands)} عرضه می‌شود.", $"[محصولاتی از|مدل‌هایی از] برندهای {PersianText.JoinList(brands)} در این دسته [هست|عرضه می‌شود].")));
                var subs = s.Listing.SubCategories.Select(g => SpecKnowledge.Safe(g.Text)).Take(6).ToList();
                if (subs.Count >= 2)
                    items.Add(new(
                        c.Say($"انواع {name} کدام‌اند؟", $"{name} [چه انواعی|چند نوع] دارد؟"),
                        c.Say($"این دسته شامل {PersianText.JoinList(subs)} است.", $"{name} [در|به] زیردسته‌های {PersianText.JoinList(subs)} [تقسیم می‌شود|دسته‌بندی شده است].")));
                if (s.Listing.WarrantyPercent >= 60)
                    items.Add(new(
                        c.Say($"محصولات دستهٔ {name} گارانتی دارند؟", $"آیا [محصولات|کالاهای] دستهٔ {name} گارانتی دارند؟"),
                        c.Say("بیشتر محصولات این دسته گارانتی دارند؛ نوع و مدت گارانتی هر کالا در صفحهٔ همان محصول آمده است.",
                              "[اغلب|بیشتر] کالاهای این دسته [با|همراه با] گارانتی عرضه می‌شوند و جزئیات آن در صفحهٔ هر محصول آمده است.")));
                break;
            }
            case SeoEntityKind.Brand when s.Listing is not null:
            {
                var name = SpecKnowledge.Safe(s.Name);
                var categories = s.Listing.RelatedGroups.Select(g => SpecKnowledge.Safe(g.Text)).Take(5).ToList();
                if (categories.Count >= 1)
                    items.Add(new(
                        c.Say($"محصولات {name} در چه دسته‌هایی عرضه می‌شود؟", $"[چه محصولاتی|چه کالاهایی] از {name} در {{SITE}} هست؟"),
                        c.Say($"محصولات {name} در {{SITE}} در دسته‌های {PersianText.JoinList(categories)} عرضه می‌شود.", $"[کالاهای|محصولات] {name} را در دسته‌های {PersianText.JoinList(categories)} [پیدا می‌کنید|می‌بینید].")));
                var top = s.Listing.TopProducts.Select(t => SpecKnowledge.Safe(t.Text)).Take(3).ToList();
                if (top.Count >= 2)
                    items.Add(new(
                        c.Say($"پرطرفدارترین محصولات {name} کدام‌اند؟", $"[پربازدیدترین|محبوب‌ترین] [محصولات|مدل‌های] {name} کدام‌اند؟"),
                        c.Say($"در حال حاضر {PersianText.JoinList(top)} از پربازدیدترین محصولات {name} در {{SITE}} هستند.")));
                break;
            }
        }

        // شرایط فروشگاه: مرجوعی همیشه (اگر فعال است)، از بقیه یکی به‌صورت چرخشی (و اگر پرسش کم بود، بیشتر).
        var subject = SpecKnowledge.Safe(s.Kind == SeoEntityKind.Product ? ctx.ShortName : s.Kind == SeoEntityKind.Brand ? "محصولات " + s.Name : s.Name);
        var policies = new List<(string[] Q, string[] A)>();
        if (store.DeliveryMaxDays > 0)
        {
            var a = PersianText.Digits(store.DeliveryMinDays); var b = PersianText.Digits(store.DeliveryMaxDays);
            policies.Add((
                [$"ارسال {subject} چقدر طول می‌کشد؟", $"زمان تحویل {subject} چقدر است؟", $"{subject} چند روزه به دستم می‌رسد؟"],
                [$"پس از [آماده‌سازی و ارسال سفارش|ارسال سفارش]، مرسوله [معمولاً|به‌طور معمول] ظرف {a} تا {b} روز کاری [به دست شما می‌رسد|تحویل داده می‌شود][ و کد رهگیری آن در حساب کاربری نمایش داده می‌شود|]."]));
        }
        if (store.CashOnDelivery)
            policies.Add((
                [$"پرداخت در محل برای {subject} امکان‌پذیر است؟", $"می‌توانم هزینهٔ {subject} را در محل پرداخت کنم؟"],
                ["اگر سفارش شما شرایط پرداخت در محل را داشته باشد، این گزینه هنگام تکمیل خرید نمایش داده می‌شود.",
                 "[بله، برای سفارش‌های واجد شرایط؛|برای سفارش‌های واجد شرایط بله؛] [گزینهٔ|امکان] پرداخت در محل در مرحلهٔ تکمیل خرید [نمایش داده می‌شود|دیده می‌شود]."]));
        else if (store.FreeShippingThreshold > 0)
        {
            var m = PersianText.Money(store.FreeShippingThreshold);
            policies.Add((
                [$"ارسال {subject} رایگان است؟", $"آیا ارسال {subject} هزینه دارد؟"],
                [$"برای سفارش‌هایی که مبلغشان از {m} تومان بیشتر باشد، ارسال رایگان است.", $"[ارسال سفارش‌های|سفارش‌های] بالای {m} تومان [رایگان است|بدون هزینهٔ ارسال است]."]));
        }
        var picked = new List<(string[] Q, string[] A)>();
        if (store.ReturnEnabled && store.ReturnDays > 0)
        {
            var d = PersianText.Digits(store.ReturnDays);
            picked.Add((
                [$"امکان مرجوع کردن {subject} وجود دارد؟", $"اگر از خرید {subject} راضی نبودم، امکان مرجوعی هست؟", $"شرایط مرجوعی {subject} چیست؟"],
                [$"تا {d} روز پس از تحویل می‌توانید از بخش سفارش‌های حساب کاربری درخواست مرجوعی ثبت کنید.",
                 $"[تا|حداکثر تا] {d} روز پس از تحویل، درخواست مرجوعی را از [بخش سفارش‌های|صفحهٔ سفارش‌های] حساب کاربری ثبت کنید."]));
        }
        if (policies.Count > 0)
        {
            var start = c.Next(policies.Count);
            var wanted = items.Count + picked.Count >= 3 ? 1 : policies.Count;
            for (var i = 0; i < Math.Min(wanted, policies.Count); i++)
                picked.Add(policies[(start + i) % policies.Count]);
        }
        foreach (var (q, a) in picked)
            items.Add(new(c.Say(q), c.Say(a)));

        items = items.Take(6).ToList();
        return items.Count >= 2 ? items : [];
    }

    /// <summary>
    /// نگهبان تکراری نبودن متن: اثر انگشت (SimHash) متن ساخته‌شده با متن بقیهٔ صفحه‌ها مقایسه می‌شود.
    /// خروجی: کمترین فاصلهٔ همینگ (۶۴ = هیچ صفحهٔ دیگری متن کافی ندارد). ≤۳ یعنی تقریباً تکراری.
    /// </summary>
    private static int NearestDistance(WriterContext ctx, string html)
    {
        if (ctx.Site is null || ctx.Site.BodyFingerprints.Count == 0)
            return 64;
        var fingerprint = TextModels.SimHash(PersianText.HtmlToText(html));
        var nearest = 64;
        foreach (var (id, other) in ctx.Site.BodyFingerprints)
        {
            if (id == ctx.Subject.EntityId)
                continue;
            nearest = Math.Min(nearest, TextModels.Hamming(fingerprint, other));
        }
        return nearest;
    }
}
