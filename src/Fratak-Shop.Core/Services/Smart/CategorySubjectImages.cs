using System.Text.RegularExpressions;
using Fratak_Shop.Core.Services.Seo.Engine;

namespace Fratak_Shop.Core.Services.Smart;

/// <summary>
/// دور ۲۸ (#۳۶ تا #۴۲، «اول دسته‌ها») – قواعد «عکس سوژهٔ دسته»: عکس دسته باید خود موضوع دسته را نشان دهد (مثلاً خود هدفون)،
/// ترجیحاً روی زمینهٔ سفید؛ عکس آدم، فضاسازی شلوغ، لوگو، آیکن و واترمارک رد می‌شود و جست‌وجو تا پیدا شدن عکس درست ادامه پیدا می‌کند.
/// <para>
/// ترتیب منبع‌ها: ۱) Openverse و بینگ با عبارت‌های «روی زمینهٔ سفید» ← ۲) اگر چیزی نماند: ترکیب ۲ تا ۴ عکس کالاهای خود دسته روی سفید
/// (تصمیم پیش‌فرض «ج»؛ هرگز تصویر ساده/خالی).
/// </para>
/// <para>
/// نام فایل: «{نامک}-photo-w-{رمز}.webp». بخش «-photo-» حفظ شده تا بقیهٔ سامانه (بنرها، انتخاب تصویر) مثل قبل آن را «عکس واقعی» بشناسد؛
/// بخش «-w-» یعنی «بررسی سوژهٔ دور ۲۸ را گذرانده». دسته‌هایی که فقط عکس قدیمی دارند یک بار دوباره بازبینی می‌شوند.
/// </para>
/// </summary>
public static class CategorySubjectImages
{
    /// <summary>نشانهٔ نام فایل عکسی که بررسی سوژهٔ دور ۲۸ را گذرانده.</summary>
    public const string Marker = "-photo-w-";

    /// <summary>کمترین ضلع کوتاه عکس سوژه (پیکسل).</summary>
    public const int MinSide = 500;

    /// <summary>بازهٔ مجاز نسبت عرض به ارتفاع (مربع تا افقی؛ عکس کاتالوگی معمولاً مربع است).</summary>
    public const double MinRatio = 0.7, MaxRatio = 2.4;

    // لوگو، آیکن، وکتور، کارتون، اینفوگرافیک، اسکرین‌شات و … (برخلاف عکس فضاسازی، «isolated/white background» این‌جا خوب است).
    private static readonly Regex BadWords = new(
        @"(?<![a-z])(logo|logos|icon|icons|vector|vectors|clipart|clip-art|svg|psd|eps|illustration|cartoon|infographic|diagram|chart|mockup|template|silhouette|sketch|drawing|qr|barcode|screenshot|woman|man|girl|boy|people|person|hand|hands|wearing|lifestyle|family)(?![a-z])|لوگو|آیکون|آیکن|وکتور|لایه[\s‌]?باز|کارتونی|طرح[\s‌]گرافیکی|اسکرین[\s‌]?شات|مدل[\s‌]لباس|دختر|پسر|خانم|آقا",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    // سایت‌های استوک واترمارک‌دار و سایت‌های وکتور/آیکن.
    private static readonly string[] BadHosts =
    [
        "shutterstock.", "istockphoto.", "gettyimages.", "dreamstime.", "alamy.", "123rf.", "depositphotos.", "freepik.",
        "stock.adobe.", "vectorstock.", "pngtree.", "vecteezy.", "canstockphoto.", "bigstockphoto.", "stockfresh.", "pond5.",
        "storyblocks.", "lovepik.", "pikbest.", "flaticon.", "iconfinder.", "icons8.", "clipart",
    ];

    /// <summary>
    /// عبارت‌های انگلیسی جست‌وجو (Openverse و بینگ) برای عکس خود سوژه روی سفید.
    /// مثال: نامک «headphones» ← [«headphones white background»، «headphones isolated»، «headphones product»].
    /// نامک غیرلاتین ← از والد (اگر لاتین است) یا فهرست خالی.
    /// </summary>
    public static IReadOnlyList<string> EnglishQueries(CategoryPhotoSubject subject)
    {
        var own = CategoryPhotos.SlugWords(subject.Slug);
        if (own.Length == 0)
            own = CategoryPhotos.SlugWords(subject.ParentSlug);
        if (own.Length == 0)
            return [];
        return new[] { own + " white background", own + " isolated", own + " product" };
    }

    /// <summary>
    /// عبارت‌های فارسی (بینگ): نام دسته + «پس زمینه سفید» / «عکس محصول».
    /// مثال: «هدفون» ← [«هدفون پس زمینه سفید»، «عکس محصول هدفون»].
    /// </summary>
    public static IReadOnlyList<string> PersianQueries(CategoryPhotoSubject subject)
    {
        var name = PersianText.Clean(subject.Name ?? "");
        if (name.Length < 2)
            return [];
        return new[] { name + " پس زمینه سفید", "عکس محصول " + name };
    }

    /// <summary>
    /// ارزش دانلود دارد؟ رد: آدرس نامعتبر، سایت استوک/وکتور، یا واژه‌های «لوگو/آیکن/آدم/فضاسازی» در عنوان یا آدرس.
    /// مثال: («Black headphones isolated on white»، «https://upload.wikimedia.org/…jpg») ← true؛
    /// («Woman wearing headphones»، …) ← false؛ هر آدرسی از shutterstock ← false.
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
    /// ابعاد مناسب؟ ضلع کوتاه ≥ <see cref="MinSide"/> و نسبت بین <see cref="MinRatio"/> و <see cref="MaxRatio"/>.
    /// مثال: ۸۰۰×۸۰۰ ← true؛ ۱۶۰۰×۴۰۰ ← false؛ ۳۰۰×۳۰۰ ← false.
    /// </summary>
    public static bool IsSizeOk(int width, int height) =>
        height > 0 && Math.Min(width, height) >= MinSide && width / (double)height is >= MinRatio and <= MaxRatio;

    /// <summary>
    /// امتیاز مرتب‌سازی عکس‌های پذیرفته: اطمینان سوژه (۷۰٪) + کیفیت (۲۰٪) + اندازه (۱۰٪). زمینهٔ سفید در خود اطمینان لحاظ شده.
    /// </summary>
    public static double Score(SubjectVerdict verdict, int width, int height, int quality)
    {
        var q = (quality <= 0 ? 50 : Math.Clamp(quality, 0, 100)) / 100.0;
        var size = Math.Clamp((Math.Min(width, height) - MinSide) / 1000.0, 0, 1);
        return Math.Round(verdict.Confidence * 0.7 + q * 0.2 + size * 0.1, 4);
    }

    /// <summary>
    /// نام فایل عکس سوژه. مثال: («headphones»، «1a2b3c4d») ← «headphones-photo-w-1a2b3c4d.webp»؛ نامک فارسی ← «category-photo-w-…».
    /// </summary>
    public static string FileName(string? slug, string? random = null)
    {
        var words = CategoryPhotos.SlugWords(slug).Replace(' ', '-');
        if (words.Length > 38)
            words = words[..38].TrimEnd('-');
        return SeoImageName.Build((words.Length > 0 ? words : "category") + "-photo-w", ".webp", random)
               ?? SeoImageName.Build("category-photo-w", ".webp", random)!;
    }

    /// <summary>آیا این تصویر بررسی سوژهٔ دور ۲۸ را گذرانده؟ مثال: «x-photo-w-1a.webp» ← true؛ «x-photo-1a.webp» ← false.</summary>
    public static bool IsSubject(string? name) => CategoryImages.IsSafeName(name) && name!.Contains(Marker, StringComparison.Ordinal);
}
