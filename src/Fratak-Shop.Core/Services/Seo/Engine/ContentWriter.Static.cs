namespace Fratak_Shop.Core.Services.Seo.Engine;

// ═══════════════════════════════════════════════════════════════════════════
//  نویسندهٔ عنوان و توضیحات صفحه‌های ثابت + رتبه‌بندی عمومی (مرحلهٔ شانزدهم)
//  صفحهٔ اصلی، همهٔ محصولات، وبلاگ، سؤالات متداول، تماس با ما و باشگاه مشتریان متن قابل ویرایش ندارند؛
//  پس فقط عنوان، توضیحات متا و کلمهٔ کلیدی‌شان نوشته می‌شود. همهٔ ادعاها از تنظیمات واقعی فروشگاه می‌آیند
//  (مرجوعی فقط اگر فعال است، پرداخت در محل فقط اگر فعال است …) تا متن گمراه‌کننده ساخته نشود.
// ═══════════════════════════════════════════════════════════════════════════

public static partial class ContentWriter
{
    /// <summary>
    /// چند عنوان برتر به ترتیب امتیاز (برای «سئوی هوشمند درجا» که چند گزینه نشان می‌دهد).
    /// مثال: take=3 → سه عنوانی که BestTitle از بینشان اولی را برمی‌داشت.
    /// </summary>
    public static List<string> RankedTitles(WriterContext ctx, int take) => Rank(TitleOptions(ctx), ctx, ScoreTitle).Take(take).ToList();

    /// <summary>چند توضیحات متای برتر به ترتیب امتیاز.</summary>
    public static List<string> RankedMetas(WriterContext ctx, int take) => Rank(MetaOptions(ctx), ctx, ScoreMeta).Take(take).ToList();

    /// <summary>
    /// عنوان‌های نامزد صفحهٔ ثابت. قالب سایت « | نام‌سایت» را خودش اضافه می‌کند؛ پس نام سایت فقط جایی می‌آید که
    /// بخشی از معنا باشد (مثلاً «تماس با فراتک»). مثال برای وبلاگ: «وبلاگ فراتک؛ راهنمای خرید و مقاله‌های آموزشی».
    /// </summary>
    private static IEnumerable<string> StaticTitleOptions(WriterContext ctx)
    {
        var s = ctx.Subject;
        var page = StaticSeoPages.Find(s.EntityId);
        var site = PersianText.Clean(ctx.Store.SiteName);
        var name = PersianText.Clean(s.Name);
        var keyword = ctx.Keyword;
        var options = new List<string> { PersianText.Clean(s.DefaultTitle) };
        switch (page?.Role)
        {
            case "home":
                var tagline = PersianText.Clean(ctx.Store.Tagline);
                options.Add(name);
                if (keyword.Length > 0)
                {
                    options.Add($"{keyword} با قیمت مناسب و ارسال سریع");
                    options.Add($"{keyword}؛ خرید آنلاین با قیمت روز");
                    options.Add($"خرید آنلاین {keyword}");
                }
                if (tagline.Length > 0 && tagline != name)
                    options.Add(PersianText.Truncate($"{name}؛ {tagline}", ctx.Store.TitleBudget).TrimEnd('…'));
                options.Add($"{name}؛ خرید آنلاین با قیمت روز");
                break;
            case "products":
                options.Add("همه محصولات با قیمت روز");
                options.Add("خرید آنلاین محصولات با قیمت روز");
                options.Add($"فهرست محصولات {site}؛ مقایسه و خرید");
                options.Add("محصولات فروشگاه؛ قیمت، مشخصات و خرید");
                break;
            case "blog":
                options.Add($"وبلاگ {site}؛ راهنمای خرید و مقاله‌های آموزشی");
                options.Add($"مجلهٔ {site}: راهنمای خرید، آموزش و اخبار");
                options.Add("مقاله‌های آموزشی و راهنمای خرید");
                options.Add("وبلاگ؛ راهنمای خرید، نکته‌ها و اخبار");
                break;
            case "faq":
                options.Add($"سؤالات متداول خرید از {site}");
                options.Add("سؤالات متداول؛ سفارش، ارسال، پرداخت و مرجوعی");
                options.Add("پاسخ پرسش‌های رایج دربارهٔ خرید و ارسال");
                break;
            case "contact":
                options.Add($"تماس با {site}؛ راه‌های ارتباط با پشتیبانی");
                options.Add($"ارتباط با پشتیبانی {site}");
                options.Add("تماس با ما؛ شماره تماس، آدرس و فرم پیام");
                break;
            case "club":
                options.Add($"{name}؛ با هر خرید امتیاز بگیرید");
                options.Add($"{name} {site}: امتیاز خرید و جایزه");
                options.Add($"عضویت در {name} و دریافت امتیاز");
                break;
        }
        if (keyword.Length > 0 && page?.Role != "home" && !options.Any(o => KeywordMatcher.Contains(o, keyword)))
            options.Add($"{keyword}؛ {name}");
        return options.Where(o => o.Length > 0 && !o.Contains("  ", StringComparison.Ordinal));
    }

    /// <summary>
    /// توضیحات نامزد صفحهٔ ثابت (۱۲۰ تا ۱۶۰ نویسه اگر ممکن باشد) با شرایط واقعی فروشگاه.
    /// مثال برای سؤالات متداول: «پاسخ پرسش‌های رایج دربارهٔ ثبت سفارش، ارسال، پرداخت در محل، مرجوعی ۷ روزه و پیگیری سفارش در فراتک.»
    /// </summary>
    private static IEnumerable<string> StaticMetaOptions(WriterContext ctx)
    {
        var s = ctx.Subject;
        var page = StaticSeoPages.Find(s.EntityId);
        var site = PersianText.Clean(ctx.Store.SiteName);
        var name = PersianText.Clean(s.Name);
        var facts = StoreFactPhrases(ctx);
        var factList = facts.Count > 0 ? PersianText.JoinList(facts.Take(3).ToList()) : "ارسال به سراسر کشور";
        var keyword = ctx.Keyword;
        var options = new List<string>();
        if (PersianText.Clean(s.DefaultDescription) is { Length: > 0 } fallback)
            options.Add(fallback);

        switch (page?.Role)
        {
            case "home":
                var tagline = PersianText.Clean(ctx.Store.Tagline);
                var lead = keyword.Length > 0 ? keyword : name;
                options.Add($"{site}؛ {lead}. خرید آنلاین با قیمت روز، {factList}. محصولات را مقایسه کنید و مطمئن سفارش دهید.");
                options.Add($"خرید آنلاین از {site} با {factList}. {lead} را با قیمت روز و مشخصات کامل ببینید و سفارش دهید.");
                options.Add($"{lead} در {site}: قیمت روز، مشخصات کامل و تصاویر واقعی محصولات، همراه با {factList}.");
                if (tagline.Length > 0)
                    options.Add($"{site}؛ {tagline}. {factList} و پشتیبانی پیش و پس از خرید.");
                break;
            case "products":
                options.Add($"فهرست کامل محصولات {site} با قیمت روز؛ بر اساس دسته‌بندی، برند و قیمت فیلتر کنید و با {factList} آنلاین سفارش دهید.");
                options.Add($"همهٔ محصولات {site} را یک‌جا ببینید: مقایسهٔ قیمت و مشخصات، فیلتر بر اساس برند و دسته و خرید آنلاین با {factList}.");
                break;
            case "blog":
                options.Add($"مقاله‌های آموزشی، راهنمای خرید و تازه‌ترین اخبار را در وبلاگ {site} بخوانید و آگاهانه‌تر انتخاب و خرید کنید.");
                options.Add($"وبلاگ {site}: راهنمای خرید، مقایسهٔ محصولات، آموزش استفاده و نکته‌های نگهداری؛ پیش از خرید بخوانید.");
                break;
            case "faq":
                var topics = new List<string> { "ثبت سفارش", "ارسال" };
                topics.Add(ctx.Store.CashOnDelivery ? "پرداخت در محل" : "پرداخت");
                if (ctx.Store.ReturnEnabled && ctx.Store.ReturnDays > 0)
                    topics.Add($"مرجوعی {PersianText.Digits(ctx.Store.ReturnDays)} روزه");
                topics.Add("پیگیری سفارش");
                options.Add($"پاسخ پرسش‌های رایج دربارهٔ {PersianText.JoinList(topics)} در {site}. پیش از تماس با پشتیبانی، جواب سؤالتان را اینجا پیدا کنید.");
                options.Add($"سؤالات متداول {site}: {PersianText.JoinList(topics)}؛ راهنمای کامل و کوتاه برای خریدی بی‌دغدغه.");
                break;
            case "contact":
                options.Add($"راه‌های ارتباط با پشتیبانی {site}: شماره تماس، ایمیل، آدرس و فرم ارسال پیام. برای پیگیری سفارش یا مشاوره پیش از خرید با ما در تماس باشید.");
                options.Add($"تماس با {site}؛ پاسخ‌گوی سؤال‌های شما دربارهٔ سفارش، ارسال و خرید هستیم. شماره تماس، آدرس و فرم پیام را اینجا ببینید.");
                break;
            case "club":
                options.Add($"با هر خرید، ثبت دیدگاه و معرفی دوستان در {name} {site} امتیاز بگیرید و امتیازها را به جایزه و تخفیف تبدیل کنید.");
                options.Add($"عضو {name} {site} شوید؛ با خرید، دیدگاه و دعوت دوستان امتیاز جمع کنید و از جایزه‌ها و پیشنهادهای ویژه بهره ببرید.");
                break;
        }
        if (keyword.Length > 0)
            foreach (var option in options.ToList().Where(o => !KeywordMatcher.Contains(o, keyword)).Take(2))
                options.Add($"{keyword}: {option}");
        return options.Select(o => o.Length <= SeoLimits.MetaMax + 15 ? o : PersianText.Truncate(o, SeoLimits.MetaMax - 2));
    }
}
