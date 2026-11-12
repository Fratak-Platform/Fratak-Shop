using Fratak_Shop.Core.Services.Seo.Engine;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Products;
using Fratak_Shop.Data.Entities.Smart;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Fratak_Shop.Core.Services.Smart;

/// <summary>
/// گزارش یک دور «دسته از صفحهٔ تأمین‌کننده». Examined = کالاهایی که صفحه‌شان خوانده شد، Found = کالاهایی که مسیر دسته داشتند،
/// Moved = کالاهایی که با این کار دسته گرفتند یا دقیق‌تر شدند، Created = دسته‌های تازه، Unplaced = هنوز بی‌دسته (از میان خوانده‌شده‌ها)،
/// Checked = شناسهٔ همهٔ کالاهایی که صفحه‌شان در این دور خوانده شد.
/// </summary>
public sealed record SupplierCategoryReport(int Examined, int Found, int Moved, IReadOnlyList<CreatedCategory> Created, IReadOnlyList<Guid> Unplaced,
    IReadOnlyList<Guid>? Checked = null)
{
    public static readonly SupplierCategoryReport Empty = new(0, 0, 0, [], []);

    /// <summary>متن کوتاه برای گزارش کار. مثال: «صفحهٔ تأمین‌کنندهٔ ۶ کالا خوانده شد: ۵ مسیر دسته، ۴ کالا دسته گرفت.»</summary>
    public string Summary => Examined == 0
        ? "کالای بی‌دسته‌ای با لینک تأمین‌کننده نبود."
        : $"صفحهٔ تأمین‌کنندهٔ {PersianText.Digits(Examined)} کالا خوانده شد: {PersianText.Digits(Found)} مسیر دسته، {PersianText.Digits(Moved)} کالا دسته گرفت"
          + (Unplaced.Count > 0 ? $"؛ {PersianText.Digits(Unplaced.Count)} کالا هنوز بی‌دسته" : "") + ".";
}

/// <summary>
/// دور ۳۱ (#۱) – قانون پروژه: «اگر سیستم خودش نتوانست دستهٔ کالا را بفهمد، لینک تأمین‌کنندهٔ همان کالا (SupplierUrl) را باز کند،
/// دسته‌ای را که تأمین‌کننده برای آن گذاشته بخواند و بر اساس آن دسته‌بندی کند؛ اگر دستهٔ تأمین‌کننده جزئی نبود، خودش دقیق‌ترش کند».
/// <list type="number">
/// <item>نامزدها: کالاهایی که هنوز در «دسته‌بندی نشده/سایر» هستند یا سیستم موقتاً در «دستهٔ کلی» گذاشته، لینک تأمین‌کنندهٔ معتبر (http/https)
/// دارند و صفحه‌شان در ۱۴ روز گذشته خوانده نشده. کالای دستی (دستهٔ واقعی ادمین) هرگز دست نمی‌خورد.</item>
/// <item>خواندن مسیر دسته از صفحهٔ تأمین‌کننده (تابع از لایهٔ وب: نوار مسیر، JSON-LD، API فروشگاه‌های شناخته‌شده) – مؤدبانه و کش‌شده.</item>
/// <item>ذخیرهٔ مسیر در <see cref="Product.SupplierCategory"/> و (اگر از شاهد قبلی کامل‌تر یا هم‌اندازه است) در SourceCategory.</item>
/// <item>دسته‌بندی دوبارهٔ همین کالاها با <see cref="ISmartCategoryService"/>؛ اگر دستهٔ تأمین‌کننده کلی بود، <see cref="CategoryRefiner"/>
/// دستهٔ دقیق‌تر را از نام کالا می‌سازد (مثلاً «کتری برقی» زیر «لوازم آشپزخانه»).</item>
/// <item>دستهٔ تازه ← «تکمیل دسته» (متن، تصویر) در صف.</item>
/// </list>
/// <para>محل استفاده: SmartWorker (بعد از ورود هوشمند برای کالاهای بی‌دسته‌مانده + هر ساعت برای بقیه) و «تکمیل هوشمند» یک کالا.</para>
/// </summary>
public interface ISupplierCategoryService
{
    /// <summary>
    /// یک دور خواندن دسته از صفحهٔ تأمین‌کننده.
    /// </summary>
    /// <param name="now">زمان فعلی (برای آزمون‌پذیری).</param>
    /// <param name="readSupplier">خواندن مسیر دستهٔ یک صفحه (آدرس، نام کالا) ← بخش‌های مسیر از کلی به جزئی، یا null/خالی. خطایش کار را خراب نمی‌کند.</param>
    /// <param name="only">فقط این کالاها (مثلاً کالاهای بی‌دسته‌ماندهٔ همین ورود هوشمند)؛ null = همهٔ نامزدها.</param>
    /// <param name="take">بیشترین کالا در این دور (مؤدب با سایت تأمین‌کننده).</param>
    /// <param name="force">بدون توجه به فاصلهٔ ۱۴ روزه (دکمهٔ دستی یا تکمیل هوشمند یک کالا).</param>
    Task<SupplierCategoryReport> RunAsync(DateTimeOffset now, Func<string, string, CancellationToken, Task<IReadOnlyList<string>?>> readSupplier,
        IReadOnlyCollection<Guid>? only = null, int take = SupplierCategoryService.DefaultTake, bool force = false, CancellationToken cancellationToken = default);
}

public sealed class SupplierCategoryService(
    FratakShopDbContext context,
    ISmartCategoryService categorizer,
    ISmartTaskQueue queue,
    ISmartConfigService config,
    ILogger<SupplierCategoryService> logger) : ISupplierCategoryService
{
    /// <summary>بیشترین کالا در هر دور.</summary>
    public const int DefaultTake = 12;

    /// <summary>هر کالا حداکثر هر چند روز یک بار دوباره خوانده می‌شود.</summary>
    public static readonly TimeSpan RecheckAfter = TimeSpan.FromDays(14);

    public async Task<SupplierCategoryReport> RunAsync(DateTimeOffset now, Func<string, string, CancellationToken, Task<IReadOnlyList<string>?>> readSupplier,
        IReadOnlyCollection<Guid>? only = null, int take = DefaultTake, bool force = false, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(readSupplier);
        var options = await config.GetAsync(cancellationToken);
        if (!options.Enabled)
            return SupplierCategoryReport.Empty;
        if (only is { Count: 0 })
            return SupplierCategoryReport.Empty;

        // «بی‌دسته» = دستهٔ سیستمی (دسته‌بندی نشده) یا دستهٔ «سایر/متفرقه»؛ همان تعریف دسته‌بندی هوشمند.
        var categories = await context.Categories.Select(c => new { c.Id, c.Name, c.Slug, c.IsSystem }).ToListAsync(cancellationToken);
        var catchAll = categories.Where(c => c.IsSystem || CategoryClassifier.IsCatchAll(c.Name, c.Slug)).Select(c => c.Id).ToList();

        var due = now - RecheckAfter;
        var query = context.Products.Where(p => p.SupplierUrl != null && p.SupplierUrl != ""
                                                && (catchAll.Contains(p.CategoryId) || p.CategorySource == ProductCategorySource.General));
        if (!force)
            query = query.Where(p => p.SupplierCategoryAt == null || p.SupplierCategoryAt < due);
        if (only is not null)
            query = query.Where(p => only.Contains(p.Id));
        var rows = await query
            .OrderBy(p => p.SupplierCategoryAt == null ? 0 : 1).ThenBy(p => p.SupplierCategoryAt).ThenByDescending(p => p.CreatedAt)
            .Take(Math.Clamp(take, 1, 100))
            .Select(p => new { p.Id, p.Name, p.SupplierUrl, p.SourceCategory })
            .ToListAsync(cancellationToken);
        if (rows.Count == 0)
            return SupplierCategoryReport.Empty;

        var found = new List<Guid>();
        foreach (var row in rows)
        {
            IReadOnlyList<string>? path = null;
            if (IsWebUrl(row.SupplierUrl))
            {
                try
                {
                    path = await readSupplier(row.SupplierUrl!.Trim(), row.Name, cancellationToken);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    logger.LogDebug(exception, "Supplier category read failed for {Product}", row.Id);
                }
            }
            var clean = CategoryNames.Path(CategoryNames.Join(path ?? Array.Empty<string>()));
            var supplierText = clean.Count > 0 ? CategoryNames.Truncate(CategoryNames.Join(clean), 300) : null;
            var source = MergeSource(row.SourceCategory, clean);
            // علامت «خوانده شد» همیشه ثبت می‌شود (حتی ناموفق) تا صفحهٔ خراب هر ساعت دوباره خوانده نشود.
            await context.Products.Where(p => p.Id == row.Id)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(p => p.SupplierCategoryAt, now)
                    .SetProperty(p => p.SupplierCategory, p => supplierText ?? p.SupplierCategory)
                    .SetProperty(p => p.SourceCategory, p => source ?? p.SourceCategory), cancellationToken);
            if (supplierText is not null)
                found.Add(row.Id);
        }

        if (found.Count == 0)
            return new SupplierCategoryReport(rows.Count, 0, 0, [], rows.Select(r => r.Id).ToList(), rows.Select(r => r.Id).ToList());

        var report = await categorizer.RunAsync(found, found.Count, cancellationToken);
        foreach (var category in report.Created)
        {
            await queue.EnqueueAsync(SmartTaskKind.CategoryComplete, SmartTarget.Category, category.Id, category.Name, SmartPriority.Batch,
                null, null, ProductIntakeService.Actor, cancellationToken);
        }
        var unplaced = rows.Select(r => r.Id).Where(id => !found.Contains(id)).Concat(report.Unplaced ?? Array.Empty<Guid>()).Distinct().ToList();
        return new SupplierCategoryReport(rows.Count, found.Count, report.Moved, report.Created, unplaced, rows.Select(r => r.Id).ToList());
    }

    /// <summary>
    /// شاهد تازهٔ SourceCategory: مسیر تأمین‌کننده جای مسیر قبلی را می‌گیرد اگر مسیر قبلی نبود/تک‌بخشی بود یا مسیر تأمین‌کننده
    /// دست‌کم هم‌اندازهٔ آن است (تأمین‌کننده مرجع اصلی است). مسیر کوتاه‌تر از شاهد کامل قبلی ← null (همان قبلی می‌ماند).
    /// مثال: (null، [لوازم آشپزخانه، کتری]) ← «لوازم آشپزخانه > کتری»؛ («الف > ب > ج»، [الف]) ← null.
    /// </summary>
    public static string? MergeSource(string? existing, IReadOnlyList<string> supplier)
    {
        if (supplier.Count == 0)
            return null;
        var old = CategoryNames.Path(existing);
        if (old.Count >= 2 && supplier.Count < old.Count)
            return null;
        return CategoryNames.Truncate(CategoryNames.Join(supplier), 300);
    }

    /// <summary>فقط آدرس کامل http/https (لینک خراب یا «javascript:» هرگز باز نمی‌شود).</summary>
    public static bool IsWebUrl(string? url) =>
        !string.IsNullOrWhiteSpace(url) && Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp) && !string.IsNullOrEmpty(uri.Host);
}
