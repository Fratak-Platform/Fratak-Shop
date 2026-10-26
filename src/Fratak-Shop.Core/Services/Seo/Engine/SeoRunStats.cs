namespace Fratak_Shop.Core.Services.Seo.Engine;

/// <summary>
/// آمار یک بار اجرای موتور سئو و متن «گزارش کوتاه» آن (مرحلهٔ هفتم – اصلاحیه).
/// <para>
/// هدف: بعد از هر اجرا کارشناس سئو بداند چه شد و <b>کیفیت تحلیل رقبا</b> چقدر بوده است. مثلاً اگر موتورهای جست‌وجو از
/// سرور در دسترس نبوده‌اند، پیشنهادها فقط با داده‌های داخلی ساخته شده‌اند و این باید صادقانه گفته شود.
/// </para>
/// پر شدن: <see cref="SeoEngineService"/> در RunCoreAsync (هر صفحهٔ صف یک بار <see cref="Observe"/>).
/// نمایش: ستون «توضیح» اجراها در /admin/seo/engine و متن اعلان «گزارش اجرای موتور سئو».
/// <example>
/// نمونهٔ خروجی:
/// «۴۲۰ صفحه نمره‌دهی شد (میانگین ۷۸، ۱۲۰ صفحه ۱۰۰/۱۰۰) · ۳۰ صفحه در صف · ۲۸ پیشنهاد تازه (میانگین +۱۴ نمره) ·
///  تحلیل رقبا: ۲۲ از ۲۸ عبارت با صفحهٔ رقیب (میانگین ۶٫۴ صفحه)، ۴ فقط پیشنهاد جست‌وجو، ۲ فقط دادهٔ داخلی · منبع‌ها: digikala.com، torob.com».
/// </example>
/// </summary>
public sealed class SeoRunStats
{
    /// <summary>تعداد صفحه‌های نمره‌دهی‌شده.</summary>
    public int Scored { get; init; }

    /// <summary>میانگین نمرهٔ همهٔ صفحه‌ها.</summary>
    public double AverageScore { get; init; }

    /// <summary>صفحه‌های ۱۰۰/۱۰۰.</summary>
    public int Perfect { get; init; }

    /// <summary>صفحه‌هایی که در این اجرا برای بازنویسی انتخاب شدند.</summary>
    public int Queued { get; init; }

    /// <summary>صفحه‌هایی که ساخت پیشنهادشان خطا داد.</summary>
    public int Failed { get; set; }

    /// <summary>بهبود نمرهٔ هر پیشنهاد ساخته‌شده (نمرهٔ بعد − قبل).</summary>
    public List<int> Gains { get; } = [];

    /// <summary>منبع‌هایی که در دسترس نبودند (مثلاً «Bing»).</summary>
    public HashSet<string> ResearchErrors { get; } = new(StringComparer.Ordinal);

    /// <summary>عبارت‌هایی که تحقیق شدند.</summary>
    public int Researched { get; private set; }

    /// <summary>عبارت‌هایی که حداقل یک صفحهٔ رقیب واقعاً خوانده و تحلیل شد.</summary>
    public int WithCompetitors { get; private set; }

    /// <summary>عبارت‌هایی که فقط پیشنهاد جست‌وجو (autocomplete) داشتند و صفحهٔ رقیب نه.</summary>
    public int SuggestionsOnly { get; private set; }

    /// <summary>جمع صفحه‌های رقیب تحلیل‌شده (برای میانگین).</summary>
    public int CompetitorPages { get; private set; }

    private readonly Dictionary<string, int> _hosts = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// ثبت نتیجهٔ تحقیق یک صفحه. null یعنی تحقیق انجام نشد (خاموش در تنظیمات یا صفحهٔ محتوایی).
    /// </summary>
    public void Observe(CompetitorInsight? insight)
    {
        if (insight is null)
            return;
        Researched++;
        if (insight.PagesAnalyzed > 0)
        {
            WithCompetitors++;
            CompetitorPages += insight.PagesAnalyzed;
            foreach (var host in insight.Hosts.Take(10))
                _hosts[host] = _hosts.GetValueOrDefault(host) + 1;
        }
        else if (insight.Suggestions.Count > 0)
        {
            SuggestionsOnly++;
        }
    }

    /// <summary>عبارت‌هایی که هیچ دادهٔ بیرونی نداشتند (پیشنهاد فقط با دادهٔ داخلی سایت ساخته شد).</summary>
    public int InternalOnly => Researched - WithCompetitors - SuggestionsOnly;

    /// <summary>میانگین بهبود نمرهٔ پیشنهادها (گرد شده).</summary>
    public int AverageGain => Gains.Count == 0 ? 0 : (int)Math.Round(Gains.Average());

    /// <summary>پرتکرارترین سایت‌های رقیب این اجرا (حداکثر ۴).</summary>
    public IReadOnlyList<string> TopHosts => _hosts.OrderByDescending(h => h.Value).ThenBy(h => h.Key, StringComparer.Ordinal).Take(4).Select(h => h.Key).ToList();

    /// <summary>عدد اعشاری با یک رقم اعشار و رقم فارسی. مثال: 6.43 → «۶٫۴»، 5.0 → «۵».</summary>
    public static string Decimal1(double value)
    {
        var text = value.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
        return new string(text.Select(c => c switch
        {
            >= '0' and <= '9' => (char)('۰' + (c - '0')),
            '.' => '٫',
            _ => c,
        }).ToArray());
    }

    /// <summary>
    /// متن گزارش کوتاه (حداکثر حدود ۱۰۰۰ نویسه، مناسب ستون Message اجرا). بخش‌ها با « · » جدا می‌شوند.
    /// </summary>
    public string Report()
    {
        static string D(long n) => PersianText.Digits(n);
        var parts = new List<string>
        {
            $"{D(Scored)} صفحه نمره‌دهی شد (میانگین {D((long)Math.Round(AverageScore))}، {D(Perfect)} صفحه ۱۰۰/۱۰۰)",
        };
        if (Queued == 0)
        {
            parts.Add("صفحه‌ای برای بازنویسی در صف نبود (همه یا کامل‌اند یا پیشنهادشان منتظر بررسی/اخیراً بررسی‌شده است)");
        }
        else
        {
            parts.Add($"{D(Queued)} صفحه در صف");
            parts.Add(Gains.Count > 0 ? $"{D(Gains.Count)} پیشنهاد تازه (میانگین +{D(AverageGain)} نمره)" : "پیشنهاد تازه‌ای ساخته نشد");
        }
        if (Failed > 0)
            parts.Add($"{D(Failed)} صفحه خطا داد");

        if (Researched > 0)
        {
            var quality = $"تحلیل رقبا: {D(WithCompetitors)} از {D(Researched)} عبارت با صفحهٔ رقیب";
            if (WithCompetitors > 0)
                quality += $" (میانگین {Decimal1(CompetitorPages / (double)WithCompetitors)} صفحه)";
            if (SuggestionsOnly > 0)
                quality += $"، {D(SuggestionsOnly)} فقط پیشنهاد جست‌وجو";
            if (InternalOnly > 0)
                quality += $"، {D(InternalOnly)} فقط دادهٔ داخلی";
            parts.Add(quality);
            if (TopHosts.Count > 0)
                parts.Add("رقبای پرتکرار: " + string.Join("، ", TopHosts));
        }
        else if (Queued > 0)
        {
            parts.Add("تحلیل رقبا انجام نشد (در تنظیمات خاموش است یا صفحه‌ها محتوایی بودند)");
        }

        if (ResearchErrors.Count > 0)
            parts.Add($"در دسترس نبود: {string.Join("، ", ResearchErrors.Take(5))}");

        var text = string.Join(" · ", parts) + ".";
        return text.Length <= 1000 ? text : text[..999] + "…";
    }
}
