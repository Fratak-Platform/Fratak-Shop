using System.Text.RegularExpressions;
using Fratak_Shop.Core.Services.Seo.Engine;

namespace Fratak_Shop.Core.Services.Smart;

/// <summary>
/// اطلاعات لازم برای پیدا کردن «عکس فضاسازی» یک دسته: نام فارسی، نامک انگلیسی و دستهٔ والد.
/// مثال: («هدفون»، «headphones»، «لوازم جانبی موبایل»، «mobile-accessories»).
/// </summary>
public sealed record CategoryPhotoSubject(Guid Id, string Name, string Slug, string? ParentName = null, string? ParentSlug = null);

/// <summary>
/// دور ۲۶: یک نتیجهٔ جست‌وجوی «عکس فضاسازی». Width/Height = ابعاد اعلام‌شدهٔ منبع (۰ = نامعلوم؛ بینگ ابعاد نمی‌دهد).
/// Source = «openverse» یا «bing». مثال: («https://upload.wikimedia.org/…/kitchen.jpg»، «Modern kitchen»، صفحهٔ منبع، 4032، 3024، «openverse»).
/// </summary>
public sealed record PhotoHit(string Url, string? Title, string? PageUrl, int Width, int Height, string Source);

/// <summary>
/// سنجه‌های ظاهری یک عکس (روی نسخهٔ خیلی کوچک حساب می‌شود):
/// <list type="bullet">
/// <item>Colorfulness: رنگی بودن (معیار Hasler–Süsstrunk)؛ زیر ۱۵ یعنی تقریباً خاکستری/بی‌روح.</item>
/// <item>WhiteRatio: سهم پیکسل‌های تقریباً سفید؛ عکس کالا روی زمینهٔ سفید معمولاً بالای ۰٫۴ است.</item>
/// <item>Brightness: میانگین روشنایی ۰ تا ۲۵۵؛ خیلی تاریک یا خیلی روشن ← رد.</item>
/// <item>Contrast: انحراف معیار روشنایی؛ عکس تخت و یکدست ← کم.</item>
/// </list>
/// </summary>
public sealed record PhotoMetrics(double Colorfulness, double WhiteRatio, double Brightness, double Contrast);

/// <summary>
/// دور ۲۶ – «عکس دسته از اینترنت، نه از کالاهای خود دسته».
/// <para>
/// خواستهٔ کاربر: برای هر دسته با کلمه‌های گسترش‌یافته (فارسی + انگلیسی + حال‌وهوا) در وب جست‌وجو شود، عکس دانلود، WebP و محلی ذخیره شود؛
/// عکس ساده/سرد (کالا روی زمینهٔ سفید، آیکن، لوگو، وکتور) و عکس کالاهای خود دسته پذیرفته نمی‌شود.
/// </para>
/// <para>
/// این کلاس فقط منطق خالص (آزمون‌پذیر) را دارد: ساخت عبارت‌های جست‌وجو، رد نتیجه‌های نامناسب از روی عنوان/آدرس،
/// محاسبهٔ سنجه‌های ظاهری از پیکسل‌ها و امتیاز نهایی. دانلود و ذخیره در SmartImageFinder (لایهٔ وب) انجام می‌شود.
/// </para>
/// </summary>
public static class CategoryPhotos
{
    /// <summary>کمترین عرض عکس فضاسازی (پیکسل).</summary>
    public const int MinWidth = 900;

    /// <summary>کمترین و بیشترین نسبت عرض به ارتفاع (افقی؛ نه مربعی و نه نوار باریک).</summary>
    public const double MinRatio = 1.2, MaxRatio = 2.6;

    /// <summary>کمترین رنگی بودن (زیر این ← عکس بی‌روح/خاکستری).</summary>
    public const double MinColorfulness = 18;

    /// <summary>بیشترین سهم پیکسل سفید (بالاتر ← عکس کالا روی زمینهٔ سفید = «سرد»).</summary>
    public const double MaxWhiteRatio = 0.35;

    // مرز کلمه با «حرف لاتین نبودن» سنجیده می‌شود (نه \b) تا نام‌های با زیرخط مثل «kitchen_icon_set» هم گرفته شوند.
    // کلمه‌هایی که در عنوان/آدرس نتیجه یعنی «عکس فضاسازی نیست» (لوگو، آیکن، وکتور، کالای دوربری‌شده، قالب طراحی…).
    private static readonly Regex BadWords = new(
        @"(?<![a-z])(logo|logos|icon|icons|vector|vectors|clipart|clip-art|png|svg|psd|eps|illustration|cartoon|infographic|diagram|chart|mockup|template|isolated|white[\s_-]background|transparent|silhouette|sketch|wallpaper[\s_-]text|qr|barcode|screenshot)(?![a-z])|لوگو|آیکون|آیکن|وکتور|لایه[\s‌]?باز|کارتونی|طرح[\s‌]گرافیکی|دوربری|پس[\s‌]?زمینه[\s‌]سفید|اسکرین[\s‌]?شات",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    // سایت‌های عکس استوک واترمارک‌دار و سایت‌های PNG/وکتور: عکسشان یا واترمارک دارد یا دوربری‌شده است.
    private static readonly string[] BadHosts =
    [
        "shutterstock.", "istockphoto.", "gettyimages.", "dreamstime.", "alamy.", "123rf.", "depositphotos.", "freepik.",
        "stock.adobe.", "vectorstock.", "pngtree.", "cleanpng.", "pngwing.", "pngegg.", "kisspng.", "pngimg.", "favpng.",
        "vecteezy.", "canstockphoto.", "bigstockphoto.", "stockfresh.", "pond5.", "storyblocks.", "lovepik.", "pikbest.",
        "flaticon.", "iconfinder.", "icons8.", "seekpng.", "pngitem.", "clipart",
    ];

    /// <summary>
    /// عبارت‌های جست‌وجوی انگلیسی (برای Openverse و بینگ): از نامک انگلیسی دسته و والدش + کلمهٔ حال‌وهوا.
    /// مثال: نامک «home-appliances» ← [«home appliances lifestyle»، «home appliances interior»، «home appliances»].
    /// نامک غیرلاتین/خالی ← فقط از والد (اگر لاتین باشد) یا فهرست خالی.
    /// </summary>
    public static IReadOnlyList<string> EnglishQueries(CategoryPhotoSubject subject)
    {
        var list = new List<string>();
        var own = SlugWords(subject.Slug);
        var parent = SlugWords(subject.ParentSlug);
        if (own.Length > 0)
        {
            list.Add(own + " lifestyle");
            list.Add(own + " interior");
            // نام یک‌کلمه‌ای مبهم است (مثلاً «bags»)؛ با والد دقیق‌تر می‌شود: «bags women fashion».
            if (!own.Contains(' ') && parent.Length > 0 && !parent.Contains(own, StringComparison.Ordinal))
                list.Add(own + " " + parent);
            list.Add(own);
        }
        else if (parent.Length > 0)
        {
            list.Add(parent + " lifestyle");
        }
        return list.Distinct(StringComparer.Ordinal).Take(4).ToList();
    }

    /// <summary>
    /// عبارت‌های جست‌وجوی فارسی (برای بینگ با فیلتر «عکس واقعی، بزرگ، افقی»): نام دسته + والد + کلمه‌های فضاسازی.
    /// مثال: («لوازم خانگی»، والد «خانه و آشپزخانه») ← [«لوازم خانگی در خانه»، «لوازم خانگی خانه و آشپزخانه»، «لوازم خانگی»].
    /// </summary>
    public static IReadOnlyList<string> PersianQueries(CategoryPhotoSubject subject)
    {
        var name = PersianText.Clean(subject.Name ?? "");
        if (name.Length < 2)
            return [];
        var list = new List<string> { name + " در خانه" };
        var parent = PersianText.Clean(subject.ParentName ?? "");
        if (parent.Length >= 2 && !name.Contains(parent, StringComparison.Ordinal))
            list.Add(name + " " + parent);
        list.Add(name);
        return list.Distinct(StringComparer.Ordinal).Take(3).ToList();
    }

    /// <summary>
    /// نامک ← کلمه‌های انگلیسی (عددها و بخش‌های یک‌حرفی حذف). مثال: «home-appliances-2» ← «home appliances»؛ «هدفون» ← «».
    /// </summary>
    public static string SlugWords(string? slug)
    {
        if (string.IsNullOrWhiteSpace(slug))
            return "";
        var words = slug.ToLowerInvariant().Split(['-', '_', ' '], StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length > 1 && w.All(char.IsAsciiLetter))
            .Take(5);
        return string.Join(' ', words);
    }

    /// <summary>
    /// این نتیجهٔ جست‌وجو اصلاً ارزش دانلود دارد؟ رد: آدرس نامعتبر، سایت استوک/PNG، یا کلمهٔ «لوگو/آیکن/وکتور/دوربری…» در عنوان یا آدرس.
    /// مثال: («Modern kitchen with appliances»، «https://upload.wikimedia.org/…/kitchen.jpg») ← true؛
    /// («Kitchen icon set vector»، …) ← false؛ («…»، «https://www.shutterstock.com/…») ← false.
    /// </summary>
    public static bool IsUsefulHit(string? title, string? url, string? pageUrl = null)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            return false;
        foreach (var address in new[] { uri.Host, pageUrl is not null && Uri.TryCreate(pageUrl, UriKind.Absolute, out var page) ? page.Host : null })
        {
            if (address is not null && BadHosts.Any(h => address.Contains(h, StringComparison.OrdinalIgnoreCase)))
                return false;
        }
        try
        {
            if (BadWords.IsMatch(Uri.UnescapeDataString(uri.AbsolutePath)))
                return false;
            if (!string.IsNullOrWhiteSpace(title) && BadWords.IsMatch(title))
                return false;
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
        return true;
    }

    /// <summary>
    /// سنجه‌های ظاهری از پیکسل‌های RGB (۳ بایت برای هر پیکسل، ردیف به ردیف). ورودی نامعتبر ← همه صفر.
    /// <para>رنگی بودن (Hasler–Süsstrunk): rg = R−G و yb = (R+G)/2 − B؛ نتیجه = √(σrg²+σyb²) + ۰٫۳·√(μrg²+μyb²).</para>
    /// مثال: عکس تمام خاکستری ← Colorfulness = ۰؛ عکس سفید ← WhiteRatio = ۱.
    /// </summary>
    public static PhotoMetrics Measure(ReadOnlySpan<byte> rgb, int width, int height)
    {
        var count = width * height;
        if (width <= 0 || height <= 0 || rgb.Length < count * 3)
            return new PhotoMetrics(0, 0, 0, 0);
        double sumRg = 0, sumYb = 0, sumRg2 = 0, sumYb2 = 0, sumL = 0, sumL2 = 0;
        var white = 0;
        for (var i = 0; i < count; i++)
        {
            double r = rgb[i * 3], g = rgb[i * 3 + 1], b = rgb[i * 3 + 2];
            var rg = r - g;
            var yb = (r + g) / 2 - b;
            sumRg += rg;
            sumYb += yb;
            sumRg2 += rg * rg;
            sumYb2 += yb * yb;
            var l = 0.299 * r + 0.587 * g + 0.114 * b;
            sumL += l;
            sumL2 += l * l;
            if (r >= 235 && g >= 235 && b >= 235)
                white++;
        }
        var meanRg = sumRg / count;
        var meanYb = sumYb / count;
        var varRg = Math.Max(0, sumRg2 / count - meanRg * meanRg);
        var varYb = Math.Max(0, sumYb2 / count - meanYb * meanYb);
        var colorfulness = Math.Sqrt(varRg + varYb) + 0.3 * Math.Sqrt(meanRg * meanRg + meanYb * meanYb);
        var brightness = sumL / count;
        var contrast = Math.Sqrt(Math.Max(0, sumL2 / count - brightness * brightness));
        return new PhotoMetrics(Math.Round(colorfulness, 2), Math.Round(white / (double)count, 3), Math.Round(brightness, 1), Math.Round(contrast, 1));
    }

    /// <summary>
    /// قانون پذیرش عکس فضاسازی: افقی (نسبت ۱٫۲ تا ۲٫۶)، عرض ≥ ۹۰۰، رنگی (≥ ۱۸)، زمینهٔ سفید ≤ ۳۵٪، نه خیلی تاریک/روشن، کنتراست ≥ ۲۵.
    /// مثال: ۱۶۰۰×۱۰۰۰ رنگی ← قبول؛ ۱۲۰۰×۱۲۰۰ ← رد (مربع)؛ کالای روی زمینهٔ سفید ← رد.
    /// </summary>
    public static bool IsAcceptable(int width, int height, PhotoMetrics metrics)
    {
        if (width < MinWidth || height <= 0)
            return false;
        var ratio = width / (double)height;
        return ratio is >= MinRatio and <= MaxRatio
               && metrics.Colorfulness >= MinColorfulness
               && metrics.WhiteRatio <= MaxWhiteRatio
               && metrics.Brightness is >= 45 and <= 215
               && metrics.Contrast >= 25;
    }

    /// <summary>
    /// امتیاز ۰ تا ۱ برای مرتب کردن عکس‌های پذیرفته‌شده: رنگی بودن (۴۵٪)، کیفیت/تیزی (۳۵٪)، اندازه (۱۰٪) و نزدیکی نسبت به ۱٫۶ (۱۰٪).
    /// quality = امتیاز ۰ تا ۱۰۰ کیفیت (۰ = نامعلوم ← ۵۰ فرض می‌شود).
    /// </summary>
    public static double Score(int width, int height, PhotoMetrics metrics, int quality)
    {
        var color = Math.Clamp((metrics.Colorfulness - MinColorfulness) / 60.0, 0, 1);
        var sharp = (quality <= 0 ? 50 : Math.Clamp(quality, 0, 100)) / 100.0;
        var size = Math.Clamp((width - MinWidth) / 1100.0, 0, 1);
        var ratio = height <= 0 ? 0 : 1 - Math.Clamp(Math.Abs(width / (double)height - 1.6) / 1.0, 0, 1);
        return Math.Round(color * 0.45 + sharp * 0.35 + size * 0.10 + ratio * 0.10, 4);
    }

    /// <summary>
    /// نام فایل عکس فضاسازی (قرارداد «{نامک}-photo-{رمز}.webp» که <see cref="CategoryImages.IsPhoto"/> می‌شناسد).
    /// نامک غیرلاتین ← «category-photo-…». مثال: («headphones»، "1a2b3c4d") ← «headphones-photo-1a2b3c4d.webp».
    /// </summary>
    public static string FileName(string? slug, string? random = null)
    {
        var words = SlugWords(slug).Replace(' ', '-');
        // سقف ۴۰ نویسه تا بخش «-photo» هنگام کوتاه شدن نامک (سقف ۶۰) هرگز بریده نشود.
        if (words.Length > 40)
            words = words[..40].TrimEnd('-');
        return SeoImageName.Build((words.Length > 0 ? words : "category") + "-photo", ".webp", random)
               ?? SeoImageName.Build("category-photo", ".webp", random)!;
    }
}
