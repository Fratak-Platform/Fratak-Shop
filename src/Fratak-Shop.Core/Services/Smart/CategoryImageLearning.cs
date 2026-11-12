using System.Globalization;
using Fratak_Shop.Core.Services.Learning;

namespace Fratak_Shop.Core.Services.Smart;

/// <summary>
/// دور ۳۰ (#۵) – یادداشت یک تصویر پیشنهادی دسته که همین الان ذخیره شد (برای یادگیری از تصمیم بعدی ادمین).
/// </summary>
/// <param name="ImageName">نام فایل ذخیره‌شده در پوشهٔ دسته‌ها (همان نامی که در فهرست پیشنهادها می‌رود).</param>
/// <param name="Url">آدرس اصلی عکس در اینترنت (فقط کلید هش آن ذخیره می‌شود).</param>
/// <param name="Hash">اثر انگشت dHash عکس.</param>
/// <param name="Source">کلید منبع (مثلاً «openverse» یا «bing:example.com»).</param>
/// <param name="Query">عبارت جست‌وجویی که این عکس را پیدا کرد.</param>
/// <param name="Features">بردار ویژگی (<see cref="CategoryImageLearning.Features"/>).</param>
public sealed record CategoryImageNote(string ImageName, string? Url, ulong Hash, string? Source, string? Query, double[] Features);

/// <summary>
/// دور ۳۰ (#۵) – یک تصمیم ثبت‌شدهٔ ادمین روی تصویر دسته (ورودی یادگیری).
/// </summary>
/// <param name="CategoryId">دسته.</param>
/// <param name="UrlKey">کلید هش آدرس اصلی (یا null).</param>
/// <param name="Hash">اثر انگشت dHash (۰ = نامعلوم).</param>
/// <param name="Source">کلید منبع (یا null).</param>
/// <param name="Features">بردار ویژگی ذخیره‌شده (متن) یا null.</param>
/// <param name="Approved">true = ادمین انتخابش کرد؛ false = حذفش کرد.</param>
public sealed record CategoryImageSample(Guid CategoryId, string? UrlKey, long Hash, string? Source, string? Features, bool Approved);

/// <summary>
/// دور ۳۰ (#۵) – «یادگیری از تصویرهای تأییدشده و ردشدهٔ دسته» (کاملاً داخلی؛ بدون سرویس بیرونی).
/// <list type="number">
/// <item>هر تصویر پیشنهادی با «بردار ویژگی» (سفیدی زمینه، سهم سوژه، مرکز بودن، رنگ پوست، کیفیت، نسبت ابعاد، تطابق عنوان،
/// کیفیت دوربری) ذخیره می‌شود.</item>
/// <item>وقتی ادمین تصویری را «انتخاب» می‌کند ← تأیید؛ «حذف» (یکی یا همه) ← رد.</item>
/// <item>از روی همهٔ تصمیم‌ها یک مدل لجستیک کوچک (<see cref="LogisticModel"/>) آموزش می‌بیند (وقتی از هر کلاس دست‌کم
/// <see cref="MinPerClass"/> نمونه باشد) و تصویرهای تازه‌ای که «شبیه ردشده‌ها» هستند پیش از ذخیره کنار می‌روند.</item>
/// <item>آدرس و اثر انگشت عکس ردشده دیگر هرگز برای همان دسته پیشنهاد نمی‌شود؛ عکسی که در ۲ دستهٔ مختلف رد شده برای هیچ دسته‌ای.</item>
/// <item>هر منبع (مثلاً یک سایت) امتیاز تأیید خودش را می‌گیرد؛ منبعی که بیشتر عکس‌هایش رد شده کنار می‌رود.</item>
/// </list>
/// مثال: ادمین ۸ عکس «آدم‌دار/صحنه‌ای» را حذف و ۸ عکس «کالا روی سفید» را انتخاب کرده ← عکس صحنه‌ای تازه امتیاز یادگرفتهٔ ۰٫۱ می‌گیرد و رد می‌شود.
/// </summary>
public static class CategoryImageLearning
{
    /// <summary>تعداد ویژگی‌های بردار.</summary>
    public const int FeatureCount = 11;

    /// <summary>کمترین تعداد نمونهٔ هر کلاس (تأیید/رد) برای آموزش مدل.</summary>
    public const int MinPerClass = 6;

    /// <summary>
    /// بردار ویژگی (همه بین ۰ و حدود ۱؛ ترتیب ثابت – تغییرش داده‌های قبلی را بی‌معنا می‌کند):
    /// [اطمینان سوژه، سهم سوژه، زمینهٔ سفید، زمینهٔ یکدست، مرکز بودن، رنگ پوست، کیفیت، نسبت ابعاد، تطابق عنوان، کنتراست مرز دوربری، اطمینان مدل دوربری].
    /// </summary>
    public static double[] Features(SubjectVerdict verdict, int width, int height, int quality, double titleMatch, MatteVerdict? matte)
    {
        ArgumentNullException.ThrowIfNull(verdict);
        var aspect = width > 0 && height > 0 ? Math.Clamp(Math.Log(width / (double)height) / Math.Log(3) + 0.5, 0, 1) : 0.5;
        return
        [
            Math.Clamp(verdict.Confidence, 0, 1),
            Math.Clamp(verdict.Coverage, 0, 1),
            verdict.WhiteBackground ? 1 : 0,
            verdict.PlainBackground ? 1 : 0,
            verdict.Centered ? 1 : 0,
            Math.Clamp(verdict.Skin * 4, 0, 1),
            Math.Clamp(quality / 100.0, 0, 1),
            aspect,
            Math.Clamp(titleMatch, 0, 1),
            matte is null ? 0.5 : Math.Clamp(matte.EdgeContrast / 120.0, 0, 1),
            matte is null ? 0.5 : Math.Clamp(1 - matte.Uncertain * 2, 0, 1),
        ];
    }

    /// <summary>متن فشرده برای ذخیره (عدد با نقطهٔ اعشار و ۳ رقم). مثال: [0.5، 1] ← «0.5,1».</summary>
    public static string Serialize(IReadOnlyList<double> features) =>
        string.Join(',', features.Select(f => Math.Round(double.IsFinite(f) ? f : 0, 3).ToString(CultureInfo.InvariantCulture)));

    /// <summary>خواندن متن ذخیره‌شده؛ متن خراب/طول اشتباه ← null (آن نمونه در آموزش استفاده نمی‌شود).</summary>
    public static double[]? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        var parts = text.Split(',');
        if (parts.Length != FeatureCount)
            return null;
        var result = new double[parts.Length];
        for (var i = 0; i < parts.Length; i++)
        {
            if (!double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || !double.IsFinite(value))
                return null;
            result[i] = Math.Clamp(value, -1, 2);
        }
        return result;
    }
}

/// <summary>
/// دور ۳۰ (#۵) – «حافظهٔ یادگرفته» برای انتخاب تصویر یک دسته (از روی تصمیم‌های قبلی ادمین). ساخت: <see cref="ISmartCatalogService"/>.
/// </summary>
public sealed class CategoryImageMemory
{
    private readonly HashSet<string> _blockedUrls;
    private readonly List<ulong> _blockedHashes;
    private readonly Dictionary<string, (int Approved, int Total)> _sources;
    private readonly double _logitShift;

    /// <summary>حافظهٔ خالی (هنوز تصمیمی ثبت نشده).</summary>
    public static CategoryImageMemory Empty { get; } = new(Guid.Empty, []);

    /// <param name="categoryId">دستهٔ فعلی (فهرست سیاه همین دسته).</param>
    /// <param name="samples">تصمیم‌های ثبت‌شده در همهٔ دسته‌ها (جدیدترین‌ها).</param>
    public CategoryImageMemory(Guid categoryId, IReadOnlyList<CategoryImageSample> samples)
    {
        ArgumentNullException.ThrowIfNull(samples);
        Decisions = samples.Count;
        var rejected = samples.Where(s => !s.Approved).ToList();
        _blockedUrls = rejected.Where(s => s.CategoryId == categoryId && !string.IsNullOrWhiteSpace(s.UrlKey))
            .Select(s => s.UrlKey!).ToHashSet(StringComparer.OrdinalIgnoreCase);
        // اثر انگشت: ردشده در همین دسته، یا ردشده در دست‌کم ۲ دستهٔ مختلف (عکس بد برای همه).
        var local = rejected.Where(s => s.CategoryId == categoryId && s.Hash != 0).Select(s => unchecked((ulong)s.Hash));
        var global = rejected.Where(s => s.Hash != 0)
            .GroupBy(s => s.Hash)
            .Where(g => g.Select(s => s.CategoryId).Distinct().Count() >= 2)
            .Select(g => unchecked((ulong)g.Key));
        _blockedHashes = local.Concat(global).Distinct().ToList();
        _sources = samples.Where(s => !string.IsNullOrWhiteSpace(s.Source))
            .GroupBy(s => s.Source!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => (g.Count(s => s.Approved), g.Count()), StringComparer.Ordinal);
        var training = samples
            .Select(s => (Features: CategoryImageLearning.Parse(s.Features), s.Approved))
            .Where(s => s.Features is not null)
            .Select(s => (s.Features!, s.Approved))
            .ToList();
        Model = LogisticModel.Train(training, CategoryImageLearning.MinPerClass, l2: 0.02, epochs: 500);
        // مدل احتمال را به نرخ واقعی تأیید برمی‌گرداند؛ برای «فیلتر» احتمال متوازن (۵۰/۵۰) می‌خواهیم تا زیاد بودن ردها
        // همهٔ عکس‌ها را رد نکند.
        _logitShift = Model is null || Model.BaseRate <= 0 || Model.BaseRate >= 1 ? 0 : -Math.Log(Model.BaseRate / (1 - Model.BaseRate));
    }

    /// <summary>تعداد تصمیم‌های خوانده‌شده.</summary>
    public int Decisions { get; }

    /// <summary>مدل آموزش‌دیده (یا null وقتی نمونه کافی نیست).</summary>
    public LogisticModel? Model { get; }

    /// <summary>آیا این عکس قبلاً رد شده؟ (همان آدرس برای این دسته، یا تصویر تقریباً یکسان.)</summary>
    public bool Blocks(string? url, ulong hash) =>
        (url is not null && _blockedUrls.Contains(SmartImageRules.UrlKey(url)))
        || (hash != 0 && _blockedHashes.Any(h => ImageHash.IsDuplicate(h, hash)));

    /// <summary>امتیاز تأیید منبع (هموارسازی لاپلاس؛ منبع بدون سابقه ۰٫۵).</summary>
    public double SourceScore(string? source) =>
        source is not null && _sources.TryGetValue(source, out var s) ? SmartImageRules.SourceScore(s.Approved, s.Total) : 0.5;

    /// <summary>منبعی که دست‌کم ۵ تصمیم داشته و کمتر از ۲۰٪ آن تأیید شده ← کنار گذاشته می‌شود.</summary>
    public bool SourceBlocked(string? source) =>
        source is not null && _sources.TryGetValue(source, out var s) && s.Total >= 5 && s.Approved / (double)s.Total < 0.2;

    /// <summary>احتمال متوازن «ادمین این را تأیید می‌کند» از مدل (۰ تا ۱)؛ بدون مدل ← null.</summary>
    public double? Learned(IReadOnlyList<double> features)
    {
        if (Model is null)
            return null;
        var z = Model.Intercept + _logitShift;
        for (var i = 0; i < Model.Weights.Count && i < features.Count; i++)
            z += Model.Weights[i] * features[i];
        return LogisticModel.Sigmoid(z);
    }

    /// <summary>مدل با اطمینان می‌گوید «این شبیه ردشده‌هاست» (احتمال متوازن کمتر از ۰٫۲۵).</summary>
    public bool Rejects(IReadOnlyList<double> features) => Learned(features) is { } p && p < 0.25;

    /// <summary>
    /// امتیاز نهایی رتبه‌بندی: امتیاز پایه (قواعد) ۵۵٪ + یادگرفته ۳۰٪ + منبع ۱۵٪. بدون مدل ← پایه ۸۵٪ + منبع ۱۵٪.
    /// مثال: پایهٔ ۰٫۸، یادگرفتهٔ ۰٫۹، منبع ۰٫۶ ← ۰٫۸۰.
    /// </summary>
    public double Rank(double baseScore, IReadOnlyList<double> features, string? source)
    {
        var learned = Learned(features);
        var sourceScore = SourceScore(source);
        return learned is { } p
            ? baseScore * 0.55 + p * 0.3 + sourceScore * 0.15
            : baseScore * 0.85 + sourceScore * 0.15;
    }
}
