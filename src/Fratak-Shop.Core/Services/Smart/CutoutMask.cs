namespace Fratak_Shop.Core.Services.Smart;

/// <summary>
/// دور ۳۰ – نتیجهٔ «اعتبارسنجی دوربری»: آیا ماسک سوژه سالم است و می‌شود از آن عکس بدون زمینه ساخت؟
/// </summary>
/// <param name="Accept">قبول نهایی. رد ← عکس بدون زمینه ساخته نمی‌شود و عکس اصلی دست‌نخورده می‌ماند.</param>
/// <param name="Coverage">سهم سوژه از کل تصویر (۰ تا ۱).</param>
/// <param name="EdgeContrast">میانهٔ اختلاف رنگ دو طرف مرز ماسک (۰ تا ۴۴۲). کم = مرز از وسط یک سطح یکدست (یعنی از وسط خود کالا) رد شده.</param>
/// <param name="Uncertain">سهم پیکسل‌های «نامطمئن» مدل نسبت به سطح سوژه. زیاد = مدل مطمئن نیست سوژه کجاست.</param>
/// <param name="BorderTouch">سهم پیکسل‌های قاب بیرونی که جزو سوژه‌اند. زیاد = سوژه بریده شده یا زمینه جزو سوژه حساب شده.</param>
/// <param name="CenterCover">سهم کادر میانی (۲۰٪ وسط) که سوژه است. کم = مدل چیز دیگری غیر از کالای وسط را برداشته.</param>
/// <param name="WeakEdges">سهم نقطه‌های مرزی که کنتراست ندارند (کمتر از ۱۵). زیاد = بخشی از مرز از وسط کالا رد شده.</param>
/// <param name="Recall">چه سهمی از «سوژهٔ مرجع» (پر کردن از لبه روی زمینهٔ سفید) در ماسک مانده (۱ = هیچ چیز از کالا پاک نشده؛ بدون مرجع ۱).</param>
/// <param name="Reason">دلیل فارسی کوتاه برای گزارش.</param>
public sealed record MatteVerdict(bool Accept, double Coverage, double EdgeContrast, double Uncertain, double BorderTouch, double CenterCover, double WeakEdges, double Recall, string Reason);

/// <summary>
/// دور ۳۰ (#۳) – منطق خالص «دوربری مطمئن» (بدون SkiaSharp و بدون مدل؛ آزمون‌پذیر). لایهٔ وب (SubjectMatting) خروجی مدل
/// U²-Net کوچک (u2netp) را می‌دهد و این کلاس ماسک را تمیز و «اعتبارسنجی» می‌کند تا هرگز بخشی از خود کالا پاک نشود.
/// <list type="number">
/// <item><see cref="Normalize"/> و <see cref="Resize"/>: خروجی ۳۲۰×۳۲۰ مدل به بازهٔ ۰ تا ۱ و اندازهٔ عکس.</item>
/// <item><see cref="KeepMain"/>: فقط تکهٔ اصلی و تکه‌های بزرگ همراهش می‌مانند (مثل دو لنگهٔ هدفون)؛ تکه‌های ریز سرگردان
/// (نخ، خرده‌زمینه، لوگوی گوشه) حذف می‌شوند.</item>
/// <item><see cref="FillHoles"/>: سوراخ‌های داخل سوژه پر می‌شوند (مدل گاهی بخش روشن کالا را زمینه می‌بیند)، مگر سوراخ «واقعی» که
/// زمینه از آن دیده می‌شود (مثل داخل دستهٔ لیوان: هم‌رنگ زمینهٔ یکدست و بزرگ).</item>
/// <item><see cref="Check"/>: سهم سوژه، اطمینان مدل، تماس با قاب، پوشش وسط تصویر، کنتراست مرز و (روی زمینهٔ سفید) مقایسه با
/// «پر کردن از لبه». هر کدام بد بود ← رد (عکس اصلی دست‌نخورده می‌ماند).</item>
/// </list>
/// مثال: کتری مشکی روی میز چوبی ← ماسک کتری با پوشش ۳۵٪، کنتراست مرز ۶۰ ← قبول.
/// مثال: ماسکی که نصف بدنهٔ یکدست کالا را بریده ← کنتراست مرز ۵ ← رد.
/// </summary>
public static class CutoutMask
{
    /// <summary>ضلع ورودی/خروجی مدل U²-Net.</summary>
    public const int ModelSide = 320;

    /// <summary>کمترین سهم سوژه (کمتر = سوژه گم شده یا چیزی پیدا نشده).</summary>
    public const double MinCoverage = 0.04;

    /// <summary>بیشترین سهم سوژه (بیشتر = تقریباً همه‌چیز سوژه شده؛ زمینه‌ای حذف نمی‌شود).</summary>
    public const double MaxCoverage = 0.97;

    /// <summary>
    /// خروجی خام مدل به بازهٔ ۰ تا ۱ (کمینه ← ۰، بیشینه ← ۱؛ همان کار rembg). خروجی یکنواخت (بی‌اطلاعات) ← همه صفر.
    /// مثال: [۲، ۴، ۶] ← [۰، ۰٫۵، ۱].
    /// </summary>
    public static float[] Normalize(ReadOnlySpan<float> raw)
    {
        var result = new float[raw.Length];
        if (raw.Length == 0)
            return result;
        float min = float.MaxValue, max = float.MinValue;
        foreach (var v in raw)
        {
            if (float.IsNaN(v))
                continue;
            if (v < min) min = v;
            if (v > max) max = v;
        }
        var range = max - min;
        if (!(range > 1e-6f))
            return result;
        for (var i = 0; i < raw.Length; i++)
            result[i] = float.IsNaN(raw[i]) ? 0f : Math.Clamp((raw[i] - min) / range, 0f, 1f);
        return result;
    }

    /// <summary>تغییر اندازهٔ ماسک با درون‌یابی دوخطی. مثال: ماسک ۲×۲ ← ۴×۴ با لبه‌های نرم.</summary>
    public static float[] Resize(float[] mask, int sourceWidth, int sourceHeight, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(mask);
        var result = new float[Math.Max(0, width * height)];
        if (sourceWidth <= 0 || sourceHeight <= 0 || width <= 0 || height <= 0 || mask.Length < sourceWidth * sourceHeight)
            return result;
        var sx = sourceWidth / (double)width;
        var sy = sourceHeight / (double)height;
        for (var y = 0; y < height; y++)
        {
            var fy = Math.Clamp((y + 0.5) * sy - 0.5, 0, sourceHeight - 1);
            var y0 = (int)fy;
            var y1 = Math.Min(sourceHeight - 1, y0 + 1);
            var ty = (float)(fy - y0);
            for (var x = 0; x < width; x++)
            {
                var fx = Math.Clamp((x + 0.5) * sx - 0.5, 0, sourceWidth - 1);
                var x0 = (int)fx;
                var x1 = Math.Min(sourceWidth - 1, x0 + 1);
                var tx = (float)(fx - x0);
                var top = mask[y0 * sourceWidth + x0] * (1 - tx) + mask[y0 * sourceWidth + x1] * tx;
                var bottom = mask[y1 * sourceWidth + x0] * (1 - tx) + mask[y1 * sourceWidth + x1] * tx;
                result[y * width + x] = top * (1 - ty) + bottom * ty;
            }
        }
        return result;
    }

    /// <summary>ماسک سخت: مقدار ≥ آستانه ← سوژه. مثال: [۰٫۲، ۰٫۷] با ۰٫۵ ← [نه، بله].</summary>
    public static bool[] Threshold(float[] soft, float threshold = 0.5f)
    {
        ArgumentNullException.ThrowIfNull(soft);
        var result = new bool[soft.Length];
        for (var i = 0; i < soft.Length; i++)
            result[i] = soft[i] >= threshold;
        return result;
    }

    /// <summary>
    /// نگه داشتن تکهٔ اصلی و تکه‌های بزرگ همراه (≥ ۱۲٪ بزرگ‌ترین تکه یا هم‌پوشان با کادر آن)؛ تکه‌های ریز دیگر حذف می‌شوند.
    /// تکهٔ کوچکِ جدا در نوار لبه (لوگو/واترمارک گوشه) هرگز نگه داشته نمی‌شود.
    /// مثال: کتری + نخ نازک کنارش (۲٪) ← فقط کتری. دو لنگهٔ هدفون (هر کدام ۴۵٪) ← هر دو.
    /// </summary>
    public static bool[] KeepMain(bool[] mask, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(mask);
        var result = new bool[mask.Length];
        if (width <= 0 || height <= 0 || mask.Length < width * height)
            return result;
        var (labels, parts) = ImageCleanup.Components(mask, width, height);
        if (parts.Count == 0)
            return result;
        var main = parts.OrderByDescending(p => p.Size).First();
        var stray = ImageCleanup.StrayIslands(mask, width, height);
        var keep = new HashSet<int> { main.Id };
        // کادر تکهٔ اصلی کمی بزرگ‌تر (۴٪) تا تکه‌های چسبیده به کالا (مثل درِ جدا افتاده) هم حساب شوند.
        var padX = (int)(width * 0.04);
        var padY = (int)(height * 0.04);
        foreach (var part in parts)
        {
            if (part.Id == main.Id)
                continue;
            var big = part.Size >= main.Size * 0.12;
            var inside = part.MinX >= main.MinX - padX && part.MaxX <= main.MaxX + padX && part.MinY >= main.MinY - padY && part.MaxY <= main.MaxY + padY;
            if ((big || (inside && part.Size >= main.Size * 0.004)) && part.Size >= 12)
                keep.Add(part.Id);
        }
        for (var i = 0; i < width * height; i++)
            result[i] = labels[i] > 0 && keep.Contains(labels[i]) && !stray[i];
        return result;
    }

    /// <summary>
    /// پر کردن سوراخ‌های داخل سوژه (ناحیه‌های غیرسوژه‌ای که به لبهٔ تصویر راه ندارند).
    /// <para>سوراخ «واقعی» (زمینه از آن پیداست) پر نمی‌شود: وقتی زمینه یکدست است، سوراخ بزرگ‌تر از ۰٫۵٪ سطح سوژه است و دست‌کم ۸۵٪
    /// پیکسل‌هایش هم‌رنگ زمینه‌اند (مثل داخل دستهٔ لیوان روی زمینهٔ سفید).</para>
    /// مثال: جعبهٔ هدفون با شکاف روشن وسطش (مدل آن را زمینه دیده) ← شکاف پر می‌شود و از کالا چیزی کم نمی‌شود.
    /// </summary>
    public static bool[] FillHoles(bool[] mask, ReadOnlySpan<byte> rgb, int width, int height, (byte R, byte G, byte B)? plainBackground)
    {
        ArgumentNullException.ThrowIfNull(mask);
        var result = (bool[])mask.Clone();
        if (width <= 0 || height <= 0 || mask.Length < width * height)
            return result;
        var inverse = new bool[width * height];
        for (var i = 0; i < inverse.Length; i++)
            inverse[i] = !mask[i];
        var (labels, parts) = ImageCleanup.Components(inverse, width, height);
        var subject = 0;
        for (var i = 0; i < width * height; i++)
            if (mask[i]) subject++;
        var fill = new HashSet<int>();
        foreach (var part in parts)
        {
            var touchesBorder = part.MinX == 0 || part.MinY == 0 || part.MaxX == width - 1 || part.MaxY == height - 1;
            if (!touchesBorder)
                fill.Add(part.Id);
        }
        if (fill.Count == 0)
            return result;
        // سوراخ‌های واقعی (زمینه پیداست) را از فهرست پر کردن کنار می‌گذاریم.
        if (plainBackground is { } bg && rgb.Length >= width * height * 3)
        {
            var size = new Dictionary<int, int>();
            var same = new Dictionary<int, int>();
            for (var i = 0; i < width * height; i++)
            {
                var label = labels[i];
                if (label == 0 || !fill.Contains(label))
                    continue;
                size[label] = size.GetValueOrDefault(label) + 1;
                var j = i * 3;
                if (ImageSubjectCheck.Distance((rgb[j], rgb[j + 1], rgb[j + 2]), bg) <= 30)
                    same[label] = same.GetValueOrDefault(label) + 1;
            }
            foreach (var (label, count) in size)
            {
                if (count >= Math.Max(30, subject * 0.005) && same.GetValueOrDefault(label) >= count * 0.85)
                    fill.Remove(label);
            }
        }
        for (var i = 0; i < width * height; i++)
        {
            if (labels[i] > 0 && fill.Contains(labels[i]))
                result[i] = true;
        }
        return result;
    }

    /// <summary>اجتماع دو ماسک (پیکسلی که هر کدام سوژه بداند، سوژه است). مثال: [بله، نه] ∪ [نه، نه] ← [بله، نه].</summary>
    public static bool[] Union(bool[] a, bool[] b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        var result = new bool[Math.Max(a.Length, b.Length)];
        for (var i = 0; i < result.Length; i++)
            result[i] = (i < a.Length && a[i]) || (i < b.Length && b[i]);
        return result;
    }

    /// <summary>
    /// رنگ میانهٔ قاب بیرونی (۴٪ هر لبه) و یکدست بودنش (≥ ۷۵٪ پیکسل‌های قاب نزدیک میانه). برای تشخیص «سوراخ واقعی».
    /// مثال: عکس روی زمینهٔ سفید ← (#fafafa، یکدست).
    /// </summary>
    public static ((byte R, byte G, byte B) Color, bool Plain) BorderColor(ReadOnlySpan<byte> rgb, int width, int height)
    {
        if (width <= 0 || height <= 0 || rgb.Length < width * height * 3)
            return ((255, 255, 255), false);
        var band = Math.Max(1, (int)Math.Round(Math.Min(width, height) * 0.04));
        var rs = new List<byte>();
        var gs = new List<byte>();
        var bs = new List<byte>();
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                if (x >= band && x < width - band && y >= band && y < height - band)
                    continue;
                var j = (y * width + x) * 3;
                rs.Add(rgb[j]);
                gs.Add(rgb[j + 1]);
                bs.Add(rgb[j + 2]);
            }
        }
        if (rs.Count == 0)
            return ((255, 255, 255), false);
        rs.Sort();
        gs.Sort();
        bs.Sort();
        var median = (rs[rs.Count / 2], gs[gs.Count / 2], bs[bs.Count / 2]);
        // شمارش پیکسل‌های قاب که به رنگ میانه نزدیک‌اند (یکدست بودن زمینه).
        var near = 0;
        var total = 0;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                if (x >= band && x < width - band && y >= band && y < height - band)
                    continue;
                total++;
                var j = (y * width + x) * 3;
                if (ImageSubjectCheck.Distance((rgb[j], rgb[j + 1], rgb[j + 2]), median) <= 28)
                    near++;
            }
        }
        return (median, total > 0 && near >= total * 0.75);
    }

    /// <summary>
    /// اعتبارسنجی نهایی ماسک (همهٔ شرط‌ها باید برقرار باشند):
    /// <list type="bullet">
    /// <item>سهم سوژه بین <see cref="MinCoverage"/> و <see cref="MaxCoverage"/>.</item>
    /// <item>پیکسل‌های نامطمئن مدل (۰٫۱۵ تا ۰٫۸۵) کمتر از ۴۵٪ سطح سوژه.</item>
    /// <item>تماس سوژه با قاب بیرونی کمتر از ۳۵٪ (کالای بریده یا زمینهٔ «سوژه‌شده» رد می‌شود).</item>
    /// <item>دست‌کم ۳۰٪ کادر میانی تصویر سوژه باشد (کالا تقریباً همیشه وسط عکس است).</item>
    /// <item>میانهٔ کنتراست دو طرف مرز ≥ ۱۴ و نقطه‌های مرزیِ بی‌کنتراست ≤ ۲۲٪ (مرزی که از وسط سطح یکدست کالا رد شود کنتراست ندارد).</item>
    /// <item>با «مرجع» (پر کردن از لبه روی زمینهٔ سفید): دست‌کم ۹۲٪ مرجع در ماسک مانده باشد.</item>
    /// </list>
    /// </summary>
    public static MatteVerdict Check(bool[] mask, float[]? soft, ReadOnlySpan<byte> rgb, int width, int height, bool[]? reference = null)
    {
        ArgumentNullException.ThrowIfNull(mask);
        if (width <= 0 || height <= 0 || mask.Length < width * height)
            return new MatteVerdict(false, 0, 0, 1, 1, 0, 1, 0, "ماسک نامعتبر");
        var total = width * height;
        int count = 0, uncertain = 0, border = 0, borderHit = 0, center = 0, centerHit = 0;
        int cx0 = (int)(width * 0.4), cx1 = (int)Math.Ceiling(width * 0.6), cy0 = (int)(height * 0.4), cy1 = (int)Math.Ceiling(height * 0.6);
        var band = Math.Max(1, (int)Math.Round(Math.Min(width, height) * 0.01));
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var i = y * width + x;
                var on = mask[i];
                if (on) count++;
                if (soft is not null && i < soft.Length && soft[i] > 0.15f && soft[i] < 0.85f)
                    uncertain++;
                if (x < band || y < band || x >= width - band || y >= height - band)
                {
                    border++;
                    if (on) borderHit++;
                }
                if (x >= cx0 && x < cx1 && y >= cy0 && y < cy1)
                {
                    center++;
                    if (on) centerHit++;
                }
            }
        }
        var coverage = count / (double)total;
        var uncertainShare = count == 0 ? 1 : uncertain / (double)count;
        var borderTouch = border == 0 ? 0 : borderHit / (double)border;
        var centerCover = center == 0 ? 0 : centerHit / (double)center;
        var (contrast, weak) = rgb.Length >= total * 3 ? EdgeStats(mask, rgb, width, height) : (255.0, 0.0);
        var recall = 1.0;
        if (reference is not null && reference.Length >= total)
        {
            int refCount = 0, kept = 0;
            for (var i = 0; i < total; i++)
            {
                if (!reference[i])
                    continue;
                refCount++;
                if (mask[i]) kept++;
            }
            recall = refCount == 0 ? 1 : kept / (double)refCount;
        }

        string? reason = null;
        if (coverage < MinCoverage)
            reason = "سوژه پیدا نشد یا خیلی کوچک است";
        else if (coverage > MaxCoverage)
            reason = "تقریباً همهٔ عکس سوژه است؛ زمینه‌ای برای حذف نیست";
        else if (uncertainShare > 0.45)
            reason = "مدل دربارهٔ مرز سوژه مطمئن نیست";
        else if (borderTouch > 0.35)
            reason = "سوژه به لبه‌های عکس چسبیده (بریده یا زمینه جزو سوژه شده)";
        else if (centerCover < 0.3)
            reason = "وسط عکس جزو سوژه نیست (چیز دیگری انتخاب شده)";
        else if (contrast < 14 || weak > 0.22)
            reason = "مرز سوژه از وسط یک سطح یکدست رد شده (خطر بریدن خود کالا)";
        else if (recall < 0.92)
            reason = "بخشی از کالا پاک می‌شد";
        return new MatteVerdict(reason is null, coverage, contrast, uncertainShare, borderTouch, centerCover, weak, recall, reason ?? "دوربری سالم");
    }

    /// <summary>
    /// میانهٔ «اختلاف رنگ دو طرف مرز»: برای هر پیکسل مرزی (نمونه‌برداری‌شده)، میانگین رنگ پیکسل‌های سوژه و غیرسوژه در پنجرهٔ ۷×۷
    /// مقایسه می‌شود. مرز درست (لبهٔ کالا) کنتراست دارد؛ مرزی که سطح یکدست را بریده، نه.
    /// </summary>
    /// <returns>(میانهٔ کنتراست، سهم نقطه‌های مرزی با کنتراست کمتر از ۱۵). بدون مرز ← (۰، ۱).</returns>
    public static (double Median, double WeakShare) EdgeStats(bool[] mask, ReadOnlySpan<byte> rgb, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(mask);
        if (width < 3 || height < 3 || rgb.Length < width * height * 3)
            return (0, 1);
        var edges = new List<int>();
        for (var y = 1; y < height - 1; y++)
        {
            for (var x = 1; x < width - 1; x++)
            {
                var i = y * width + x;
                if (mask[i] && (!mask[i - 1] || !mask[i + 1] || !mask[i - width] || !mask[i + width]))
                    edges.Add(i);
            }
        }
        if (edges.Count == 0)
            return (0, 1);
        var step = Math.Max(1, edges.Count / 600);
        var values = new List<double>();
        for (var k = 0; k < edges.Count; k += step)
        {
            int ex = edges[k] % width, ey = edges[k] / width;
            double ir = 0, ig = 0, ib = 0, outR = 0, outG = 0, outB = 0;
            int ni = 0, no = 0;
            for (var dy = -3; dy <= 3; dy++)
            {
                var y = ey + dy;
                if (y < 0 || y >= height)
                    continue;
                for (var dx = -3; dx <= 3; dx++)
                {
                    var x = ex + dx;
                    if (x < 0 || x >= width)
                        continue;
                    var i = y * width + x;
                    var j = i * 3;
                    if (mask[i])
                    {
                        ir += rgb[j]; ig += rgb[j + 1]; ib += rgb[j + 2]; ni++;
                    }
                    else
                    {
                        outR += rgb[j]; outG += rgb[j + 1]; outB += rgb[j + 2]; no++;
                    }
                }
            }
            if (ni == 0 || no == 0)
                continue;
            double dr = ir / ni - outR / no, dg = ig / ni - outG / no, db = ib / ni - outB / no;
            values.Add(Math.Sqrt(dr * dr + dg * dg + db * db));
        }
        if (values.Count == 0)
            return (0, 1);
        var weakCount = values.Count(v => v < 15);
        values.Sort();
        return (values[values.Count / 2], weakCount / (double)values.Count);
    }

    /// <summary>
    /// «سوژهٔ مرجع» روی زمینهٔ یکدست (هر رنگی، نه فقط سفید): پیکسل‌های هم‌رنگ زمینه که به لبهٔ عکس وصل‌اند زمینه‌اند و بقیه سوژه.
    /// این روش هرگز داخل کالا را پاک نمی‌کند (چون به لبه راه ندارد)؛ برای همین با ماسک مدل «اجتماع» می‌شود تا اگر مدل بخشی از
    /// کالا را جا انداخت، آن بخش برگردد. مثال: هدفون مشکی روی زمینهٔ خاکستری ← ماسک هدفون (+ سایهٔ تیره‌تر اگر باشد).
    /// </summary>
    /// <param name="rgb">پیکسل‌ها (۳ بایت برای هر پیکسل).</param>
    /// <param name="width">عرض.</param>
    /// <param name="height">ارتفاع.</param>
    /// <param name="background">رنگ زمینه (از <see cref="BorderColor"/>).</param>
    /// <param name="tolerance">بیشترین فاصلهٔ رنگ از زمینه (پیش‌فرض ۲۶).</param>
    public static bool[] FloodSubject(ReadOnlySpan<byte> rgb, int width, int height, (byte R, byte G, byte B) background, int tolerance = 26)
    {
        var subject = new bool[Math.Max(0, width * height)];
        if (width <= 0 || height <= 0 || rgb.Length < width * height * 3)
            return subject;
        var total = width * height;
        var near = new bool[total];
        for (var i = 0; i < total; i++)
        {
            var j = i * 3;
            near[i] = ImageSubjectCheck.Distance((rgb[j], rgb[j + 1], rgb[j + 2]), background) <= tolerance;
        }
        var flooded = new bool[total];
        var queue = new int[total];
        int head = 0, tail = 0;
        void Seed(int i)
        {
            if (near[i] && !flooded[i])
            {
                flooded[i] = true;
                queue[tail++] = i;
            }
        }
        for (var x = 0; x < width; x++)
        {
            Seed(x);
            Seed((height - 1) * width + x);
        }
        for (var y = 0; y < height; y++)
        {
            Seed(y * width);
            Seed(y * width + width - 1);
        }
        while (head < tail)
        {
            var i = queue[head++];
            int x = i % width, y = i / width;
            if (x > 0) Seed(i - 1);
            if (x < width - 1) Seed(i + 1);
            if (y > 0) Seed(i - width);
            if (y < height - 1) Seed(i + width);
        }
        for (var i = 0; i < total; i++)
            subject[i] = !flooded[i];
        return subject;
    }

    /// <summary>
    /// شفافیت نهایی هر پیکسل (۰ تا ۲۵۵): داخل سوژه کدر؛ پیکسل‌های مرزی نرم (از مقدار مدل، حداقل نیمه‌کدر)؛ بیرون شفاف
    /// به‌جز حلقهٔ یک‌پیکسلی دور مرز که کمی از مقدار مدل را می‌گیرد (لبهٔ دندانه‌دار نمی‌شود).
    /// </summary>
    public static byte[] Alpha(bool[] mask, float[]? soft, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(mask);
        var alpha = new byte[Math.Max(0, width * height)];
        if (width <= 0 || height <= 0 || mask.Length < width * height)
            return alpha;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var i = y * width + x;
                var s = soft is not null && i < soft.Length ? soft[i] : (mask[i] ? 1f : 0f);
                var nearOff = (x > 0 && !mask[i - 1]) || (x < width - 1 && !mask[i + 1]) || (y > 0 && !mask[i - width]) || (y < height - 1 && !mask[i + width]);
                var nearOn = (x > 0 && mask[i - 1]) || (x < width - 1 && mask[i + 1]) || (y > 0 && mask[i - width]) || (y < height - 1 && mask[i + width]);
                double a;
                if (mask[i])
                    a = nearOff ? Math.Max(0.55, s) : 1.0;
                else
                    a = nearOn ? Math.Min(0.45, s * 0.6) : 0.0;
                alpha[i] = (byte)Math.Clamp((int)Math.Round(a * 255), 0, 255);
            }
        }
        return alpha;
    }
}
