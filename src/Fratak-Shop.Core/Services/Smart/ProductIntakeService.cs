using System.Collections.Concurrent;
using Fratak_Shop.Core.Services.Seo.Autopilot;
using Fratak_Shop.Core.Services.Seo.Engine;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.SeoEngine;
using Fratak_Shop.Data.Entities.Smart;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Fratak_Shop.Core.Services.Smart;

/// <summary>
/// کالای تازه‌ای که منتظر «ورود هوشمند» است (برای جست‌وجوی دستهٔ آن در سایت‌های دیگر).
/// مثال: (id، «ماساژور گردن برقی مدل X5»، null، null، null).
/// </summary>
public sealed record IntakeProduct(Guid Id, string Name, string? EnglishName, string? BrandEnglishName, string? SourceCategory);

/// <summary>
/// دور ۲۶: نتیجهٔ «دستهٔ این کالا در سایت‌های دیگر». Path = مسیر کامل دسته (مثلاً از دیجی‌کالا، با « > »)؛
/// MarketCategory = دسته‌ای که اکثریت سایت‌ها (≥ ۲) برای کالا گذاشته‌اند (<see cref="CategoryVote"/>)؛ هر دو ممکن است null باشند.
/// مثال: («کالای دیجیتال > لوازم جانبی > هدفون، هدست و هندزفری»، «هدفون هدست و هندزفری»).
/// </summary>
public sealed record MarketLookup(string? Path, string? MarketCategory = null);

/// <summary>
/// گزارش یک دور ورود هوشمند. Examined = کالای تازهٔ بررسی‌شده، Categorized = جابه‌جا شده به دستهٔ درست، Queued = «تکمیل هوشمند» در صف،
/// Unplaced = هنوز بی‌دسته (اعلان به ادمین)، Created = دسته‌های تازه، Looked = چند کالا دسته‌شان از سایت دیگر خوانده شد، Seo = سئوی بعد از انتشار.
/// </summary>
public sealed record IntakeReport(int Examined, int Categorized, int Queued, IReadOnlyList<Guid> Unplaced, IReadOnlyList<CreatedCategory> Created, int Looked, int Seo)
{
    public static readonly IntakeReport Empty = new(0, 0, 0, [], [], 0, 0);

    /// <summary>متن کوتاه برای گزارش کار. مثال: «۵ کالای تازه: ۴ دسته گرفت، ۵ تکمیل هوشمند در صف؛ ۱ کالا بی‌دسته ماند».</summary>
    public string Summary => Examined == 0
        ? (Seo > 0 ? $"سئوی {PersianText.Digits(Seo)} کالای تازه‌منتشرشده تکمیل شد." : "کالای تازه‌ای نبود.")
        : $"{PersianText.Digits(Examined)} کالای تازه: {PersianText.Digits(Categorized)} دسته گرفت، {PersianText.Digits(Queued)} تکمیل هوشمند در صف"
          + (Unplaced.Count > 0 ? $"؛ {PersianText.Digits(Unplaced.Count)} کالا بی‌دسته ماند" : "")
          + (Created.Count > 0 ? $"؛ دستهٔ تازه: {string.Join("، ", Created.Select(c => c.Name))}" : "") + ".";
}

/// <summary>
/// دور ۲۲ – «ورود هوشمند کالا»: هر کالای تازه (ساخت دستی در پنل، درون‌ریزی از سایت دیگر/تأمین‌کننده، فایل) چند دقیقه بعد از ساخت
/// خودکار دسته می‌گیرد و بلافاصله «تکمیل هوشمند» (نام، نام انگلیسی، نامک، توضیح کوتاه، توضیحات حرفه‌ای، عکس پیشنهادی، مشخصات، قیمت) در صف می‌رود.
/// <list type="number">
/// <item>کالاهای منتظر: SmartIntakeAt خالی و دست‌کم ۲ دقیقه از ساختشان گذشته (تا ادمین کارش با عکس و مشخصات تمام شود). هر دور حداکثر ۲۰ کالا.</item>
/// <item>کالای «دسته‌بندی نشده» که دستهٔ مبدأ کامل ندارد: دستهٔ همان کالا در دیجی‌کالا خوانده می‌شود (حداکثر ۸ کالا در هر دور؛ تابع از لایهٔ وب).</item>
/// <item>دسته‌بندی هوشمند (<see cref="ISmartCategoryService"/>) فقط روی همین کالاها؛ دستهٔ تازه «تکمیل دسته» (متن و ۴ تصویر پیشنهادی) را در صف می‌گیرد.</item>
/// <item>«تکمیل هوشمند کالا» برای هر کالا در صف (اولویت دسته‌ای؛ تکراری ساخته نمی‌شود).</item>
/// <item>علامت «انجام شد» (SmartIntakeAt) ← دور بعد دوباره سراغش نمی‌رود؛ کالای بی‌دسته را بازبینی روزانه دوباره امتحان می‌کند.</item>
/// <item>سئوی بعد از انتشار: کالای تازه‌منتشرشده (۲ روز اخیر) که توضیح متا ندارد، حداکثر ۵ کالا در هر دور (خواستهٔ کاربر: سئو بعد از انتشار).</item>
/// </list>
/// <para>اگر «دستیار هوشمند» در تنظیمات خاموش باشد هیچ کاری نمی‌کند. همه‌چیز سبک است (چند پرس‌وجو + حداکثر ۸ درخواست شبکه با فاصله).</para>
/// <para>محل استفاده: SmartWorker (هر ۳ دقیقه) و آزمون‌های یکپارچه.</para>
/// </summary>
public interface IProductIntakeService
{
    /// <summary>
    /// یک دور ورود هوشمند.
    /// </summary>
    /// <param name="now">زمان فعلی (برای آزمون‌پذیری).</param>
    /// <param name="findSourceCategory">جست‌وجوی دستهٔ کالا در سایت‌های دیگر (مسیر با « > » + دستهٔ رأی اکثریت) یا null. خطای آن کار را خراب نمی‌کند.</param>
    /// <param name="only">فقط این کالاها (آزمون‌ها)؛ null = همهٔ کالاهای منتظر.</param>
    /// <param name="take">بیشترین کالا در این دور.</param>
    Task<IntakeReport> RunAsync(DateTimeOffset now, Func<IntakeProduct, CancellationToken, Task<MarketLookup?>>? findSourceCategory = null,
        IReadOnlyCollection<Guid>? only = null, int take = ProductIntakeService.DefaultTake, CancellationToken cancellationToken = default);
}

public sealed class ProductIntakeService(
    FratakShopDbContext context,
    ISmartCategoryService categorizer,
    ISmartTaskQueue queue,
    ISmartConfigService config,
    ISeoAutopilotService seo,
    ILogger<ProductIntakeService> logger) : IProductIntakeService
{
    /// <summary>نام نمایشی سامانه در صف کارها (همان «دستیار هوشمند»).</summary>
    public const string Actor = "دستیار هوشمند";

    /// <summary>کالای تازه چند دقیقه صبر می‌کند تا ساختش کامل شود (عکس، مشخصات، ذخیرهٔ دوم ادمین).</summary>
    public static readonly TimeSpan SettleDelay = TimeSpan.FromMinutes(2);

    /// <summary>بیشترین کالا در هر دور.</summary>
    public const int DefaultTake = 20;

    /// <summary>بیشترین جست‌وجوی دسته در سایت دیگر در هر دور (مؤدبانه و سبک).</summary>
    public const int MaxLookups = 8;

    /// <summary>بیشترین «سئوی بعد از انتشار» در هر دور.</summary>
    public const int MaxSeoPerRun = 5;

    // کالاهایی که سئوی بعد از انتشار برایشان امتحان شد (تا اگر خلبان سئو چیزی ننوشت، هر ۳ دقیقه دوباره امتحان نشود).
    private static readonly ConcurrentDictionary<Guid, DateTimeOffset> SeoTried = new();

    public async Task<IntakeReport> RunAsync(DateTimeOffset now, Func<IntakeProduct, CancellationToken, Task<MarketLookup?>>? findSourceCategory = null,
        IReadOnlyCollection<Guid>? only = null, int take = DefaultTake, CancellationToken cancellationToken = default)
    {
        var options = await config.GetAsync(cancellationToken);
        if (!options.Enabled)
            return IntakeReport.Empty;

        var cutoff = now - SettleDelay;
        var query = context.Products.Where(p => p.SmartIntakeAt == null && p.CreatedAt <= cutoff);
        if (only is not null)
            query = query.Where(p => only.Contains(p.Id));
        var rows = await query.OrderBy(p => p.CreatedAt).Take(Math.Clamp(take, 1, 200))
            .Select(p => new
            {
                p.Id, p.Name, p.EnglishName, BrandEnglish = p.Brand != null ? p.Brand.EnglishName : null, p.SourceCategory, p.MarketCategory, p.CategoryId,
                CategoryName = p.Category.Name, CategorySlug = p.Category.Slug, CategorySystem = p.Category.IsSystem,
            })
            .ToListAsync(cancellationToken);

        var seoDone = await PublishedSeoAsync(now, only, cancellationToken);
        if (rows.Count == 0)
            return seoDone > 0 ? IntakeReport.Empty with { Seo = seoDone } : IntakeReport.Empty;

        // ۱) دستهٔ همین کالا در سایت دیگر (فقط کالای بی‌دسته که مسیر کامل ندارد).
        var looked = 0;
        if (findSourceCategory is not null)
        {
            // دور ۲۶: کالایی که مسیر کامل دارد ولی «رأی رقبا» ندارد هم پرسیده می‌شود (رأی اکثریت بر مسیر تأمین‌کننده مقدم است).
            foreach (var row in rows.Where(r => (r.CategorySystem || CategoryClassifier.IsCatchAll(r.CategoryName, r.CategorySlug))
                                                && (CategoryNames.Path(r.SourceCategory).Count < 2 || r.MarketCategory is null)).Take(MaxLookups))
            {
                MarketLookup? found;
                try
                {
                    found = await findSourceCategory(new IntakeProduct(row.Id, row.Name, row.EnglishName, row.BrandEnglish, row.SourceCategory), cancellationToken);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    logger.LogDebug(exception, "Source category lookup failed for {Product}", row.Id);
                    continue;
                }
                var clean = CategoryNames.Path(found?.Path);
                var market = CategoryNames.Truncate(CategoryNames.Proper(found?.MarketCategory), 150);
                // دور ۳۱ (#۱): مسیر تک‌بخشی (مثلاً فقط «لوازم آشپزخانه») هم وقتی کالا هیچ مسیری و هیچ «رأی رقبا»یی ندارد نگه داشته
                // می‌شود تا CategoryRefiner دستهٔ دقیق‌تر را از نام کالا زیر همان دستهٔ کلی بسازد. با رأی رقبا، فقط رأی ذخیره می‌شود (دور ۲۶).
                var text = clean.Count >= 2 ? CategoryNames.Truncate(CategoryNames.Join(clean), 300)
                    : clean.Count == 1 && market is null && string.IsNullOrWhiteSpace(row.SourceCategory) ? CategoryNames.Truncate(clean[0], 300)
                    : null;
                if (text is null && market is null)
                    continue;
                // مسیر کامل تازه فقط وقتی جای مسیر قبلی را می‌گیرد که چیزی داشته باشیم؛ رأی اکثریت جدا ذخیره می‌شود.
                await context.Products.Where(p => p.Id == row.Id)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(p => p.SourceCategory, p => text ?? p.SourceCategory)
                        .SetProperty(p => p.MarketCategory, p => market ?? p.MarketCategory), cancellationToken);
                looked++;
            }
        }

        // ۲) دسته‌بندی همین کالاها (کالای دستی در دستهٔ واقعی دست نمی‌خورد؛ فقط «بی‌دسته/سایر»).
        var ids = rows.Select(r => r.Id).ToList();
        var report = await categorizer.RunAsync(ids, ids.Count, cancellationToken);
        foreach (var category in report.Created)
        {
            await queue.EnqueueAsync(SmartTaskKind.CategoryComplete, SmartTarget.Category, category.Id, category.Name, SmartPriority.Batch,
                null, null, Actor, cancellationToken);
        }

        // ۳) تکمیل هوشمند هر کالا (اگر هنوز بی‌دسته است، خود کار تکمیل یک بار دیگر دسته‌بندی را امتحان می‌کند).
        var queued = 0;
        foreach (var row in rows)
        {
            await queue.EnqueueAsync(SmartTaskKind.ProductComplete, SmartTarget.Product, row.Id, row.Name, SmartPriority.Batch, null, null, Actor, cancellationToken);
            queued++;
        }

        // ۴) علامت «انجام شد».
        await context.Products.Where(p => ids.Contains(p.Id) && p.SmartIntakeAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.SmartIntakeAt, now), cancellationToken);

        return new IntakeReport(rows.Count, report.Moved, queued, report.Unplaced ?? [], report.Created, looked, seoDone);
    }

    /// <summary>
    /// سئوی بعد از انتشار (خواستهٔ کاربر: «فیلدهای سئو بعد از انتشار پر شود»): کالای فعالی که در ۲ روز اخیر منتشر شده و توضیح متا ندارد.
    /// هر کالا حداکثر هر ۶ ساعت یک بار امتحان می‌شود. خروجی: تعداد کالای پردازش‌شده.
    /// </summary>
    private async Task<int> PublishedSeoAsync(DateTimeOffset now, IReadOnlyCollection<Guid>? only, CancellationToken cancellationToken)
    {
        var since = now.AddDays(-2);
        var query = context.Products.Where(p => p.IsActive && p.PublishedAt != null && p.PublishedAt >= since
                                                && (p.Seo == null || p.Seo.MetaDescription == null || p.Seo.MetaDescription == ""));
        if (only is not null)
            query = query.Where(p => only.Contains(p.Id));
        var candidates = await query.OrderByDescending(p => p.PublishedAt).Select(p => p.Id).Take(30).ToListAsync(cancellationToken);
        foreach (var stale in SeoTried.Where(t => t.Value < now.AddDays(-3)).Select(t => t.Key).ToList())
            SeoTried.TryRemove(stale, out _);

        var done = 0;
        foreach (var id in candidates.Where(id => !SeoTried.TryGetValue(id, out var at) || at < now.AddHours(-6)).Take(MaxSeoPerRun))
        {
            SeoTried[id] = now;
            try
            {
                await seo.ProcessAsync(SeoEntityKind.Product, id, cancellationToken);
                done++;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning(exception, "Post-publish SEO failed for {Product}", id);
            }
        }
        return done;
    }
}
