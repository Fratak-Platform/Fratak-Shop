namespace Fratak_Shop.Core.Services.Smart;

/// <summary>جزئیات امتیاز کیفیت یک تصویر (هر بخش ۰ تا ۱) و امتیاز کل ۰ تا ۱۰۰.</summary>
public sealed record ImageQualityScore(double Resolution, double Sharpness, double Brightness, double Contrast, double Background, int Total);

/// <summary>
/// دور ۲۴ (پیشنهاد ۵۸): امتیاز کیفیت عکس کالا بدون هیچ سرویس بیرونی (محاسبهٔ خالص روی پیکسل‌های خاکستری؛ رمزگشایی تصویر در لایهٔ وب با SkiaSharp است).
/// <list type="bullet">
/// <item>وضوح: کوتاه‌ترین ضلع اصلی (۳۰۰ ← ۰، ۱۲۰۰ و بیشتر ← ۱).</item>
/// <item>تیزی: واریانس لاپلاسین (معیار استاندارد تاری؛ عکس تار لبهٔ تیز ندارد و واریانس کمی دارد).</item>
/// <item>نور: میانگین روشنایی نزدیک به عکس استودیویی (حدود ۱۷۰ از ۲۵۵)؛ خیلی تیره یا سوخته امتیاز کم.</item>
/// <item>کنتراست: انحراف معیار روشنایی.</item>
/// <item>پس‌زمینه: قاب بیرونی عکس یکدست و روشن (عکس کالا روی زمینهٔ سفید بهترین عکس اصلی است؛ عکس شلوغ محیطی امتیاز کمتر).</item>
/// </list>
/// <para>وزن‌ها: وضوح ۲۵٪، تیزی ۳۰٪، نور ۱۵٪، کنتراست ۱۰٪، پس‌زمینه ۲۰٪.</para>
/// <para>مثال: عکس ۱۲۰۰×۱۲۰۰ تیز روی زمینهٔ سفید ≈ ۸۵ تا ۹۵؛ عکس ۶۰۰ پیکسلی تار و تیره ≈ ۲۰ تا ۳۵.</para>
/// <para>محل استفاده: SmartImageAnalyzer (لایهٔ وب) ← ستون ProductImages.Quality و انتخاب عکس اصلی (<see cref="PickPrimary"/>).</para>
/// </summary>
public static class ImageQuality
{
    /// <summary>
    /// امتیاز از آرایهٔ خاکستری (۰ تا ۲۵۵، ردیف به ردیف، <paramref name="width"/>×<paramref name="height"/> – معمولاً نسخهٔ کوچک‌شدهٔ حدود ۲۵۶ پیکسلی)
    /// و کوتاه‌ترین ضلع تصویر اصلی. ورودی نامعتبر ← امتیاز صفر.
    /// </summary>
    public static ImageQualityScore Score(byte[] gray, int width, int height, int originalMinSide)
    {
        if (width < 3 || height < 3 || gray.Length < width * height)
            return new ImageQualityScore(0, 0, 0, 0, 0, 0);

        var resolution = Math.Clamp((originalMinSide - 300) / 900.0, 0, 1);

        // میانگین و انحراف معیار کل.
        double sum = 0, sumSq = 0;
        var count = width * height;
        for (var i = 0; i < count; i++)
        {
            sum += gray[i];
            sumSq += gray[i] * (double)gray[i];
        }
        var mean = sum / count;
        var std = Math.Sqrt(Math.Max(0, sumSq / count - mean * mean));

        // واریانس لاپلاسین ۴-همسایه (بدون لبه‌های تصویر).
        double lapSum = 0, lapSq = 0;
        var lapCount = 0;
        for (var y = 1; y < height - 1; y++)
        {
            for (var x = 1; x < width - 1; x++)
            {
                var i = y * width + x;
                double lap = gray[i - 1] + gray[i + 1] + gray[i - width] + gray[i + width] - 4 * gray[i];
                lapSum += lap;
                lapSq += lap * lap;
                lapCount++;
            }
        }
        var lapMean = lapSum / lapCount;
        var lapVar = Math.Max(0, lapSq / lapCount - lapMean * lapMean);
        var sharpness = Math.Clamp(Math.Log10(1 + lapVar) / Math.Log10(1 + 800), 0, 1);

        var brightness = 1 - Math.Clamp((Math.Abs(mean - 170) - 40) / 100, 0, 1);
        var contrast = Math.Clamp(std / 60, 0, 1);

        // قاب بیرونی (۶٪ هر طرف، حداقل ۱ پیکسل).
        var bx = Math.Max(1, (int)(width * 0.06));
        var by = Math.Max(1, (int)(height * 0.06));
        double bSum = 0, bSq = 0;
        var bCount = 0;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                if (x >= bx && x < width - bx && y >= by && y < height - by)
                    continue;
                var v = gray[y * width + x];
                bSum += v;
                bSq += v * (double)v;
                bCount++;
            }
        }
        var bMean = bSum / bCount;
        var bStd = Math.Sqrt(Math.Max(0, bSq / bCount - bMean * bMean));
        var background = 0.6 * (1 - Math.Clamp(bStd / 40, 0, 1)) + 0.4 * (bMean / 255);

        var total = 0.25 * resolution + 0.30 * sharpness + 0.15 * brightness + 0.10 * contrast + 0.20 * background;
        return new ImageQualityScore(Math.Round(resolution, 3), Math.Round(sharpness, 3), Math.Round(brightness, 3), Math.Round(contrast, 3),
            Math.Round(background, 3), (int)Math.Round(Math.Clamp(total, 0, 1) * 100));
    }

    /// <summary>عکس اصلی فعلی ضعیف حساب می‌شود اگر امتیازش کمتر از این باشد.</summary>
    public const int WeakPrimary = 45;

    /// <summary>عکس جایگزین باید دست‌کم این مقدار بهتر باشد.</summary>
    public const int MinGain = 20;

    /// <summary>
    /// انتخاب عکس اصلی بهتر (محافظه‌کار؛ انتخاب ادمین فقط وقتی عوض می‌شود که عکس فعلی واقعاً ضعیف و جایگزین خیلی بهتر باشد).
    /// خروجی = شناسهٔ عکس تازهٔ اصلی یا null (تغییری لازم نیست). عکس بدون امتیاز نادیده گرفته می‌شود.
    /// مثال: اصلی ۳۰ و دیگری ۷۵ ← دیگری؛ اصلی ۶۰ و دیگری ۹۵ ← null (اصلی ضعیف نیست).
    /// </summary>
    public static Guid? PickPrimary(IReadOnlyList<(Guid Id, bool IsPrimary, int? Quality)> images)
    {
        var primary = images.FirstOrDefault(i => i.IsPrimary);
        var best = images.Where(i => i.Quality is not null && !i.IsPrimary).OrderByDescending(i => i.Quality).ThenBy(i => i.Id).FirstOrDefault();
        if (best.Id == Guid.Empty)
            return null;
        if (primary.Id == Guid.Empty)
            return best.Id;
        if (primary.Quality is null || primary.Quality >= WeakPrimary)
            return null;
        return best.Quality - primary.Quality >= MinGain ? best.Id : null;
    }
}
