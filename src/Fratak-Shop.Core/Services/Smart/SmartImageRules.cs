using System.Security.Cryptography;
using System.Text;

namespace Fratak_Shop.Core.Services.Smart;

// ═══════════════════════════════════════════════════════════════════════════
//  دور بیستم – قانون‌های «عکس درست»
//  مشکل: عکس اشتباه یعنی مشتری یک چیز می‌بیند و چیز دیگری می‌خرد. پس:
//   • هر عکسی که کارشناس رد کند برای همان محصول در «فهرست سیاه» می‌رود (با کلید آدرس و اثر انگشت تصویری dHash)
//     و دیگر هرگز پیشنهاد نمی‌شود؛ حتی اگر همان عکس از سایت دیگری با آدرس دیگری بیاید.
//   • هر منبع عکس (دیجی‌کالا، صفحهٔ تأمین‌کننده، یک فروشگاه خاص، باسلام، بینگ) امتیاز تأیید دارد:
//     (تأییدشده + ۱) ÷ (کل + ۲) – «هموارسازی لاپلاس» تا منبع تازه با یک رد یا یک تأیید به صفر یا یک نچسبد.
//     منبعی که عکس‌هایش بیشتر تأیید شده دفعهٔ بعد بالاتر نشان داده می‌شود (یادگیری از تصمیم‌های خود کارشناس).
//  محل استفاده: SmartCatalogService (ذخیره/خواندن)، SmartImageFinder (فیلتر و مرتب‌سازی) و صفحهٔ «محتوای هوشمند ← محصول».
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// فهرست سیاه عکس‌های یک محصول. مثال: کارشناس عکس «https://a.ir/x.jpg» را رد کرد ← BlocksUrl همان آدرس = true و
/// هر عکسی با dHash نزدیک (≤ ۶ بیت تفاوت) هم BlocksHash = true.
/// </summary>
public sealed class ImageBlacklist
{
    public static readonly ImageBlacklist Empty = new([], []);

    private readonly HashSet<string> _urlKeys;
    private readonly List<ulong> _hashes;

    public ImageBlacklist(IEnumerable<string> urlKeys, IEnumerable<long> hashes)
    {
        _urlKeys = new HashSet<string>(urlKeys.Where(k => !string.IsNullOrWhiteSpace(k)), StringComparer.OrdinalIgnoreCase);
        _hashes = hashes.Where(h => h != 0).Select(h => unchecked((ulong)h)).Distinct().ToList();
    }

    /// <summary>تعداد موارد (آدرس + اثر انگشت).</summary>
    public int Count => _urlKeys.Count + _hashes.Count;

    /// <summary>آیا این آدرس قبلاً رد شده است؟ (آدرس اول تمیز می‌شود تا ?x-oss-process و // هم یکی حساب شوند.)</summary>
    public bool BlocksUrl(string? url) => url is not null && _urlKeys.Contains(SmartImageRules.UrlKey(url));

    /// <summary>آیا تصویری تقریباً یکسان با این اثر انگشت قبلاً رد شده است؟ (۰ = نامعلوم ← false)</summary>
    public bool BlocksHash(ulong hash) => hash != 0 && _hashes.Any(h => ImageHash.IsDuplicate(h, hash));
}

/// <summary>ابزارهای کلید آدرس، کلید منبع و امتیاز منبع عکس.</summary>
public static class SmartImageRules
{
    /// <summary>
    /// کلید ۶۴ نویسه‌ای (SHA-256 هگز کوچک) آدرس تمیزشدهٔ عکس. آدرس نامعتبر ← هش متن کوتاه‌شدهٔ خودش (باز هم یکتا و ثابت).
    /// مثال: «//a.ir/x.jpg» و «https://a.ir/x.jpg» ← یک کلید.
    /// </summary>
    public static string UrlKey(string url)
    {
        var clean = SmartParsers.CleanImageUrl(url) ?? url.Trim();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(clean.ToLowerInvariant()))).ToLowerInvariant();
    }

    /// <summary>
    /// کلید منبع برای رتبه‌بندی. منبع‌های کلی همان نام خودشان‌اند؛ برای «serp» و «bing» نام دامنهٔ صفحه/تصویر اضافه می‌شود
    /// تا هر فروشگاه امتیاز جدا بگیرد. مثال: ("serp"، "https://www.mobile.ir/p/1") ← «serp:mobile.ir»؛ ("digikala"، …) ← «digikala».
    /// </summary>
    public static string SourceKey(string source, string? url)
    {
        source = string.IsNullOrWhiteSpace(source) ? "unknown" : source.Trim().ToLowerInvariant();
        if (source.Contains(':') || source is not ("serp" or "bing"))
            return Cut(source);
        if (url is null || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return source;
        var host = uri.Host.ToLowerInvariant();
        if (host.StartsWith("www.", StringComparison.Ordinal))
            host = host[4..];
        return Cut(source + ":" + host);

        static string Cut(string value) => value.Length <= 100 ? value : value[..100];
    }

    /// <summary>
    /// امتیاز منبع با هموارسازی لاپلاس: (تأیید + ۱) ÷ (کل + ۲). مثال: منبع تازه ← ۰٫۵؛ ۹ تأیید از ۱۰ ← ۰٫۸۳؛ ۰ از ۸ ← ۰٫۱.
    /// </summary>
    public static double SourceScore(int approved, int total) =>
        total <= 0 ? 0.5 : (Math.Clamp(approved, 0, total) + 1.0) / (total + 2.0);

    /// <summary>امتیاز پیش‌فرض منبعی که هنوز بازخوردی ندارد (منبع‌های «همان کالا» کمی بالاتر شروع می‌کنند).</summary>
    public static double DefaultScore(string sourceKey) => sourceKey switch
    {
        "digikala" => 0.7,
        "supplier" => 0.68,
        "basalam" => 0.55,
        _ when sourceKey.StartsWith("serp:", StringComparison.Ordinal) => 0.55,
        _ => 0.45,
    };

    /// <summary>امتیاز نهایی یک منبع: از جدول بازخورد اگر هست، وگرنه پیش‌فرض.</summary>
    public static double ScoreOf(string sourceKey, IReadOnlyDictionary<string, double>? scores) =>
        scores is not null && scores.TryGetValue(sourceKey, out var score) ? score : DefaultScore(sourceKey);
}
