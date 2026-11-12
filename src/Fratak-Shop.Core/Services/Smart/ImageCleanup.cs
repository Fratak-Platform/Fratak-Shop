namespace Fratak_Shop.Core.Services.Smart;

// ═══════════════════════════════════════════════════════════════════════════
//  دور ۲۹ – «تمیزکاری عکس کالا» بدون هیچ سرویس یا مدل بیرونی (فقط حساب روی پیکسل‌ها):
//   • پاک کردن لوگو/واترمارک گوشه‌ها و نوشتهٔ کم‌رنگ روی زمینه (عکس با زمینهٔ یکدست).
//   • تشخیص «کلاژ» (چند عکس کنار هم در یک قاب) برای رد تصویر دسته.
//   • انتخاب بهترین عکس برای «عکس اصلی» (ترجیح زمینهٔ سفید، بدون آدم/واترمارک/صحنه).
//  این کلاس فقط روی آرایهٔ RGB یک نسخهٔ کوچک (مثلاً حداکثر ۱۶۰ پیکسل) کار می‌کند تا آزمون واحد ساده باشد؛
//  لایهٔ وب (SkiaSharp) نتیجه را روی عکس با اندازهٔ واقعی اعمال می‌کند (Web/Infrastructure/Smart/ProductImageCare.cs).
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>دور ۲۹: تصمیم تمیزکاری یک عکس.</summary>
public enum CleanupAction : byte
{
    /// <summary>نشانهٔ لوگو/واترمارک نبود؛ عکس دست نمی‌خورد.</summary>
    None = 0,

    /// <summary>لوگو/نوشته پیدا شد و قابل پاک کردن است (رنگ زمینه روی آن کشیده می‌شود).</summary>
    Clean = 1,

    /// <summary>واترمارک روی خود کالاست و پاک‌شدنی نیست ← عکس نباید استفاده شود.</summary>
    Reject = 2,
}

/// <summary>دور ۲۹: یک کادر لوگو/نوشته روی نسخهٔ کوچک (پیکسل).</summary>
public sealed record MarkBox(int X, int Y, int Width, int Height);

/// <summary>
/// دور ۲۹: نتیجهٔ بررسی تمیزکاری.
/// </summary>
/// <param name="Action">تصمیم نهایی.</param>
/// <param name="Boxes">کادر لوگوها/نوشته‌های جدا از کالا (روی نسخهٔ کوچک).</param>
/// <param name="Background">رنگ زمینه که روی کادرها کشیده می‌شود.</param>
/// <param name="FillGhost">نوشتهٔ کم‌رنگ (واترمارک شفاف) روی زمینهٔ سفید پیدا شد ← پیکسل‌های تقریباً زمینه یکدست می‌شوند.</param>
/// <param name="Width">عرض نسخهٔ کوچک.</param>
/// <param name="Height">ارتفاع نسخهٔ کوچک.</param>
/// <param name="Reason">توضیح فارسی کوتاه برای پنل.</param>
public sealed record CleanupPlan(CleanupAction Action, IReadOnlyList<MarkBox> Boxes, (byte R, byte G, byte B) Background, bool FillGhost,
    int Width, int Height, string Reason)
{
    /// <summary>بدون تغییر.</summary>
    public static CleanupPlan Nothing(int width, int height, string reason) => new(CleanupAction.None, [], (255, 255, 255), false, width, height, reason);
}

/// <summary>دور ۲۹: نتیجهٔ بررسی کلاژ (چند عکس/چند کالای جدا در یک قاب).</summary>
public sealed record CollageVerdict(bool IsCollage, int Parts, string Reason);

/// <summary>دور ۲۹: یک عکس کالا برای انتخاب عکس اصلی.</summary>
/// <param name="Id">شناسهٔ ردیف ProductImages.</param>
/// <param name="IsPrimary">الان عکس اصلی است؟</param>
/// <param name="Flags">نشانه‌های بررسی سوژه (۱ سفید، ۲ یکدست، ۴ آدم، ۸ واترمارک، ۱۶ صحنه) یا null = بررسی‌نشده.</param>
/// <param name="Score">امتیاز سوژه ۰ تا ۱۰۰ یا null.</param>
/// <param name="Quality">امتیاز کیفیت ۰ تا ۱۰۰ یا null.</param>
/// <param name="DisplayOrder">ترتیب نمایش فعلی.</param>
public sealed record PrimaryCandidate(Guid Id, bool IsPrimary, byte? Flags, byte? Score, byte? Quality, int DisplayOrder);

/// <summary>
/// دور ۲۹ – موتور تمیزکاری عکس (خالص و آزمون‌پذیر).
/// <para>محل استفاده: ProductImageCare (کار ساعتی عکس‌های موجود + عکس‌های تازهٔ واردشده)، تصویرهای دسته و آزمون‌های Unit/Round29*.</para>
/// </summary>
public static class ImageCleanup
{
    /// <summary>پهنای «نوار لبه» برای لوگوها (۲۲٪ هر طرف). لوگوی فروشگاه‌ها معمولاً در همین نوار است.</summary>
    public const double EdgeBand = 0.22;

    /// <summary>
    /// جزیره‌های «سرگردان» یک ماسک (لوگو، نوشتهٔ آدرس سایت، مهر) که باید حذف شوند.
    /// <para>قاعده: هر تکهٔ جدا از تکهٔ اصلی که کاملاً در نوار لبه است و از ۱۵٪ تکهٔ اصلی کوچک‌تر است ← حذف.
    /// تکه‌های بزرگ یا تکه‌های میانهٔ عکس (مثلاً قاشق‌های یک سرویس) دست نمی‌خورند.</para>
    /// مثال: کالا وسط + لوگوی کوچک گوشهٔ پایین راست ← فقط پیکسل‌های لوگو true می‌شوند.
    /// </summary>
    /// <param name="foreground">ماسک سوژه (true = غیرزمینه)، ردیف به ردیف.</param>
    /// <param name="width">عرض.</param>
    /// <param name="height">ارتفاع.</param>
    /// <returns>ماسک پیکسل‌هایی که باید حذف شوند (طول برابر ورودی).</returns>
    public static bool[] StrayIslands(bool[] foreground, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(foreground);
        var drop = new bool[foreground.Length];
        if (width <= 0 || height <= 0 || foreground.Length < width * height)
            return drop;
        var (labels, parts) = Components(foreground, width, height);
        if (parts.Count < 2)
            return drop;
        var main = parts.OrderByDescending(p => p.Size).First();
        var dropIds = new HashSet<int>();
        foreach (var part in parts)
        {
            if (part.Id == main.Id || part.Size >= main.Size * 0.15)
                continue;
            if (InEdgeBand(part, width, height) && !Overlaps(part, main))
                dropIds.Add(part.Id);
        }
        if (dropIds.Count == 0)
            return drop;
        for (var i = 0; i < width * height; i++)
            drop[i] = labels[i] > 0 && dropIds.Contains(labels[i]);
        return drop;
    }

    /// <summary>
    /// برنامهٔ تمیزکاری یک عکس (روی نسخهٔ کوچک RGB).
    /// <list type="number">
    /// <item>زمینه از حاشیهٔ عکس خوانده می‌شود؛ زمینهٔ شلوغ (عکس صحنه) ← بدون تغییر (این‌جا تشخیص مطمئن ممکن نیست).</item>
    /// <item>تکه‌های سرگردان نوار لبه (<see cref="StrayIslands"/>) ← کادر برای پاک کردن.</item>
    /// <item>نوشتهٔ کم‌رنگ زیاد روی زمینهٔ سفید (≥ ۳۰٪ زمینه «تقریباً سفید ولی نه کاملاً») ← یکدست کردن زمینه.</item>
    /// </list>
    /// مثال: هدفون روی سفید با «digikala» در گوشه ← Clean با یک کادر؛ عکس آشپزخانه ← None.
    /// </summary>
    public static CleanupPlan Plan(ReadOnlySpan<byte> rgb, int width, int height)
    {
        if (width < 16 || height < 16 || rgb.Length < width * height * 3)
            return CleanupPlan.Nothing(width, height, "عکس خوانده نشد");
        var (bg, white, plain) = Background(rgb, width, height);
        if (!plain)
            return CleanupPlan.Nothing(width, height, "زمینهٔ شلوغ؛ لوگوی قابل پاک کردن تشخیص داده نشد");

        var fg = new bool[width * height];
        int ghost = 0, bgCount = 0;
        for (var i = 0; i < width * height; i++)
        {
            var c = (rgb[i * 3], rgb[i * 3 + 1], rgb[i * 3 + 2]);
            var d = ImageSubjectCheck.Distance(c, bg);
            if (d > 38)
            {
                fg[i] = true;
            }
            else
            {
                bgCount++;
                if (d >= 10)
                    ghost++;
            }
        }

        var drop = StrayIslands(fg, width, height);
        var boxes = BoxesOf(drop, width, height);
        var fillGhost = white && bgCount > 0 && ghost / (double)bgCount >= 0.30;
        if (boxes.Count == 0 && !fillGhost)
            return new CleanupPlan(CleanupAction.None, [], bg, false, width, height, "لوگو یا واترمارک دیده نشد");
        var reason = boxes.Count > 0 && fillGhost ? "لوگوی گوشه و نوشتهٔ کم‌رنگ پاک می‌شود"
            : boxes.Count > 0 ? $"{boxes.Count} لوگو/نوشتهٔ جدا از کالا پاک می‌شود" : "نوشتهٔ کم‌رنگ روی زمینه پاک می‌شود";
        return new CleanupPlan(CleanupAction.Clean, boxes, bg, fillGhost, width, height, reason);
    }

    /// <summary>
    /// اعمال برنامه روی همان نسخهٔ کوچک (برای آزمون و پیش‌نمایش): رنگ زمینه روی کادرها و در صورت لزوم یکدست کردن زمینهٔ کم‌رنگ.
    /// </summary>
    public static void Apply(Span<byte> rgb, int width, int height, CleanupPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.Action != CleanupAction.Clean || rgb.Length < width * height * 3)
            return;
        var (r, g, b) = plan.Background;
        foreach (var box in plan.Boxes)
        {
            for (var y = Math.Max(0, box.Y); y < Math.Min(height, box.Y + box.Height); y++)
            {
                for (var x = Math.Max(0, box.X); x < Math.Min(width, box.X + box.Width); x++)
                {
                    var i = (y * width + x) * 3;
                    rgb[i] = r;
                    rgb[i + 1] = g;
                    rgb[i + 2] = b;
                }
            }
        }
        if (!plan.FillGhost)
            return;
        for (var i = 0; i < width * height; i++)
        {
            var c = (rgb[i * 3], rgb[i * 3 + 1], rgb[i * 3 + 2]);
            if (ImageSubjectCheck.Distance(c, plan.Background) <= 38)
            {
                rgb[i * 3] = r;
                rgb[i * 3 + 1] = g;
                rgb[i * 3 + 2] = b;
            }
        }
    }

    /// <summary>
    /// دور ۲۹ – «کلاژ است؟» (برای تصویر دسته که باید تک‌سوژه و شبیه آیکون باشد):
    /// <list type="bullet">
    /// <item>زمینهٔ یکدست: دو یا چند تکهٔ بزرگ جدا از هم (هر کدام ≥ ۲۵٪ بزرگ‌ترین و ≥ ۱٪ قاب) ← چند کالا کنار هم = کلاژ.</item>
    /// <item>زمینهٔ شلوغ: خط جداکنندهٔ یکدست سراسری (افقی یا عمودی) در میانهٔ عکس ← چند عکس در یک شبکه = کلاژ.</item>
    /// </list>
    /// مثال: ۴ عکس کالا در شبکهٔ ۲×۲ روی سفید ← (true، ۴)؛ یک قابلمه روی سفید ← (false، ۱).
    /// </summary>
    public static CollageVerdict Collage(ReadOnlySpan<byte> rgb, int width, int height)
    {
        if (width < 16 || height < 16 || rgb.Length < width * height * 3)
            return new CollageVerdict(false, 0, "عکس خوانده نشد");
        var (bg, _, plain) = Background(rgb, width, height);
        if (plain)
        {
            var fg = new bool[width * height];
            for (var i = 0; i < width * height; i++)
                fg[i] = ImageSubjectCheck.Distance((rgb[i * 3], rgb[i * 3 + 1], rgb[i * 3 + 2]), bg) > 38;
            var (_, parts) = Components(fg, width, height);
            if (parts.Count == 0)
                return new CollageVerdict(false, 0, "سوژه‌ای دیده نشد");
            var largest = parts.Max(p => p.Size);
            var big = parts.Where(p => p.Size >= largest * 0.25 && p.Size >= width * height * 0.01).ToList();
            var separate = 0;
            for (var a = 0; a < big.Count; a++)
            {
                var alone = true;
                for (var b = 0; b < big.Count; b++)
                {
                    if (a != b && Overlaps(big[a], big[b]))
                        alone = false;
                }
                if (alone)
                    separate++;
            }
            return separate >= 2
                ? new CollageVerdict(true, separate, $"{separate} سوژهٔ جدا در یک قاب (کلاژ)")
                : new CollageVerdict(false, Math.Max(1, separate), "تک‌سوژه");
        }

        // زمینهٔ شلوغ: خط جداکنندهٔ سراسری یکدست (ناودان سفید/رنگی بین عکس‌ها).
        var gutters = 0;
        for (var x = width / 5; x < width * 4 / 5; x++)
        {
            if (UniformLine(rgb, width, height, x, vertical: true))
            {
                gutters++;
                x += Math.Max(2, width / 20);
            }
        }
        for (var y = height / 5; y < height * 4 / 5; y++)
        {
            if (UniformLine(rgb, width, height, y, vertical: false))
            {
                gutters++;
                y += Math.Max(2, height / 20);
            }
        }
        return gutters > 0
            ? new CollageVerdict(true, gutters + 1, "خط جداکنندهٔ سراسری بین چند عکس (کلاژ)")
            : new CollageVerdict(false, 1, "تک‌عکس");
    }

    /// <summary>
    /// دور ۲۹ – بهترین عکس برای «عکس اصلی» کالا. ترتیب ترجیح:
    /// زمینهٔ سفید و بدون آدم/واترمارک/صحنه ← زمینهٔ یکدست سالم ← بقیه؛ بعد امتیاز سوژه، کیفیت، اصلی فعلی و ترتیب نمایش.
    /// مثال: [عکس صحنه‌دار اصلی (امتیاز ۹۰)، عکس سفید (امتیاز ۷۰)] ← عکس سفید.
    /// </summary>
    public static Guid? PickPrimary(IReadOnlyList<PrimaryCandidate> images)
    {
        ArgumentNullException.ThrowIfNull(images);
        if (images.Count == 0)
            return null;
        return images
            .OrderByDescending(i => Tier(i.Flags))
            .ThenByDescending(i => i.Score ?? 0)
            .ThenByDescending(i => i.Quality ?? 0)
            .ThenByDescending(i => i.IsPrimary)
            .ThenBy(i => i.DisplayOrder)
            .First().Id;
    }

    /// <summary>بیت «تمیزکاری لوگو/واترمارک این عکس انجام شد» روی ProductImages.SubjectFlags (دوباره بررسی نمی‌شود).</summary>
    public const byte CareDone = 32;

    /// <summary>بیت «این عکس اصلیِ دوربری‌شده (زمینهٔ شفاف) است» روی ProductImages.SubjectFlags.</summary>
    public const byte CutoutDone = 64;

    /// <summary>بیت‌های مشکل (آدم ۴، واترمارک ۸، صحنه ۱۶).</summary>
    public const byte ProblemBits = 4 | 8 | 16;

    /// <summary>
    /// رتبهٔ زمینه برای عکس اصلی: ۴ دوربری‌شدهٔ سالم (زمینهٔ شفاف؛ خواستهٔ دور ۲۹)، ۳ سفید سالم، ۲ یکدست سالم، ۱ بررسی‌نشده،
    /// ۰ مشکل‌دار (آدم/واترمارک/صحنه).
    /// </summary>
    public static int Tier(byte? flags)
    {
        if (flags is not { } f)
            return 1;
        if ((f & ProblemBits) != 0)
            return 0;
        if ((f & CutoutDone) != 0)
            return 4;
        if ((f & 1) != 0)
            return 3;
        return (f & 2) != 0 ? 2 : 1;
    }

    // ── داخلی ────────────────────────────────────────────────────────────

    /// <summary>یک تکهٔ پیوسته از ماسک.</summary>
    internal sealed record Part(int Id, int Size, int MinX, int MinY, int MaxX, int MaxY);

    /// <summary>برچسب‌گذاری تکه‌های پیوسته (همسایگی ۴تایی، صف آرایه‌ای بدون بازگشت تا پشته پر نشود).</summary>
    internal static (int[] Labels, List<Part> Parts) Components(bool[] mask, int width, int height)
    {
        var labels = new int[width * height];
        var parts = new List<Part>();
        var queue = new int[width * height];
        var next = 0;
        for (var start = 0; start < width * height; start++)
        {
            if (!mask[start] || labels[start] != 0)
                continue;
            next++;
            int head = 0, tail = 0, size = 0, minX = width, minY = height, maxX = -1, maxY = -1;
            queue[tail++] = start;
            labels[start] = next;
            while (head < tail)
            {
                var i = queue[head++];
                int x = i % width, y = i / width;
                size++;
                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
                if (x > 0 && mask[i - 1] && labels[i - 1] == 0) { labels[i - 1] = next; queue[tail++] = i - 1; }
                if (x < width - 1 && mask[i + 1] && labels[i + 1] == 0) { labels[i + 1] = next; queue[tail++] = i + 1; }
                if (y > 0 && mask[i - width] && labels[i - width] == 0) { labels[i - width] = next; queue[tail++] = i - width; }
                if (y < height - 1 && mask[i + width] && labels[i + width] == 0) { labels[i + width] = next; queue[tail++] = i + width; }
            }
            parts.Add(new Part(next, size, minX, minY, maxX, maxY));
        }
        return (labels, parts);
    }

    private static bool InEdgeBand(Part part, int width, int height) =>
        part.MaxX < width * EdgeBand || part.MinX > width * (1 - EdgeBand) || part.MaxY < height * EdgeBand || part.MinY > height * (1 - EdgeBand);

    private static bool Overlaps(Part a, Part b) =>
        a.MinX <= b.MaxX && b.MinX <= a.MaxX && a.MinY <= b.MaxY && b.MinY <= a.MaxY;

    // کادر هر تکهٔ ماسک (با یک پیکسل حاشیه)؛ کادرهای هم‌پوشان یکی می‌شوند (نوشتهٔ چندحرفی = یک کادر).
    private static List<MarkBox> BoxesOf(bool[] drop, int width, int height)
    {
        var (_, parts) = Components(drop, width, height);
        var boxes = parts.Select(p => (MinX: Math.Max(0, p.MinX - 1), MinY: Math.Max(0, p.MinY - 1), MaxX: Math.Min(width - 1, p.MaxX + 1), MaxY: Math.Min(height - 1, p.MaxY + 1))).ToList();
        var merged = true;
        while (merged && boxes.Count > 1)
        {
            merged = false;
            for (var a = 0; a < boxes.Count && !merged; a++)
            {
                for (var b = a + 1; b < boxes.Count && !merged; b++)
                {
                    var (p, q) = (boxes[a], boxes[b]);
                    if (p.MinX <= q.MaxX + 2 && q.MinX <= p.MaxX + 2 && p.MinY <= q.MaxY + 2 && q.MinY <= p.MaxY + 2)
                    {
                        boxes[a] = (Math.Min(p.MinX, q.MinX), Math.Min(p.MinY, q.MinY), Math.Max(p.MaxX, q.MaxX), Math.Max(p.MaxY, q.MaxY));
                        boxes.RemoveAt(b);
                        merged = true;
                    }
                }
            }
        }
        return boxes.Select(b => new MarkBox(b.MinX, b.MinY, b.MaxX - b.MinX + 1, b.MaxY - b.MinY + 1)).ToList();
    }

    // رنگ زمینه (میانهٔ حاشیهٔ ۶٪)، سفید بودن و یکدست بودن (همان قاعدهٔ ImageSubjectCheck).
    private static ((byte R, byte G, byte B) Bg, bool White, bool Plain) Background(ReadOnlySpan<byte> rgb, int width, int height)
    {
        var ring = Math.Max(2, (int)Math.Round(Math.Min(width, height) * 0.06));
        var rs = new List<byte>();
        var gs = new List<byte>();
        var bs = new List<byte>();
        var whiteCount = 0;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                if (x >= ring && x < width - ring && y >= ring && y < height - ring)
                    continue;
                var i = (y * width + x) * 3;
                var c = (rgb[i], rgb[i + 1], rgb[i + 2]);
                rs.Add(c.Item1);
                gs.Add(c.Item2);
                bs.Add(c.Item3);
                if (ImageSubjectCheck.IsWhite(c))
                    whiteCount++;
            }
        }
        rs.Sort();
        gs.Sort();
        bs.Sort();
        var bg = (rs[rs.Count / 2], gs[gs.Count / 2], bs[bs.Count / 2]);
        // فهرست‌ها مرتب شده‌اند؛ برای یکدستی دوباره از خود عکس می‌خوانیم.
        var plainCount = 0;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                if (x >= ring && x < width - ring && y >= ring && y < height - ring)
                    continue;
                var i = (y * width + x) * 3;
                if (ImageSubjectCheck.Distance((rgb[i], rgb[i + 1], rgb[i + 2]), bg) <= 30)
                    plainCount++;
            }
        }
        var white = whiteCount / (double)rs.Count >= 0.80;
        var plain = white || plainCount / (double)rs.Count >= 0.75;
        return (bg, white, plain);
    }

    // خط سراسری یکدست: ≥ ۹۶٪ پیکسل‌های خط هم‌رنگ میانهٔ همان خط و خط کنار هم یکدست (ناودان، نه لبهٔ تصادفی).
    private static bool UniformLine(ReadOnlySpan<byte> rgb, int width, int height, int index, bool vertical)
    {
        var length = vertical ? height : width;
        var limit = vertical ? width : height;
        if (index + 1 >= limit)
            return false;
        var mid = PixelOnLine(rgb, width, vertical, index, length / 2, 0);
        var same = 0;
        for (var k = 0; k < length; k++)
        {
            if (ImageSubjectCheck.Distance(PixelOnLine(rgb, width, vertical, index, k, 0), mid) <= 12
                && ImageSubjectCheck.Distance(PixelOnLine(rgb, width, vertical, index, k, 1), mid) <= 12)
                same++;
        }
        return same >= length * 0.96;
    }

    // پیکسل k‌ام یک خط عمودی (ستون index+offset) یا افقی (ردیف index+offset).
    private static (byte R, byte G, byte B) PixelOnLine(ReadOnlySpan<byte> rgb, int width, bool vertical, int index, int k, int offset)
    {
        var x = vertical ? index + offset : k;
        var y = vertical ? k : index + offset;
        var i = (y * width + x) * 3;
        return (rgb[i], rgb[i + 1], rgb[i + 2]);
    }
}
