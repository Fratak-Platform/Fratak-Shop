using Fratak_Shop.Core.DTOs.Admin;
using Fratak_Shop.Core.DTOs.Settings;
using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Core.Services.Settings;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Auditing;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Admin;
using Fratak_Shop.Data.Entities.Products;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Core.Services.Suppliers;

// ═══════════════════════════════════════════════════════════════════════════
//  سرویس بررسی خودکار لینک تأمین‌کننده (مرحلهٔ هفتم – فاز ۵)
//
//  کجا استفاده می‌شود:
//   • Web/Infrastructure/SupplierSyncWorker.cs : اجرای زمان‌بندی‌شده (N بار در روز) و «اجرای فوری».
//   • Areas/Admin/Pages/Products/SupplierSync  : داشبورد، تأیید/رد پیشنهادها.
//   • Areas/Admin/Pages/Products/Edit          : کارت «آخرین بررسی منبع».
//
//  روند یک اجرا:
//   ۱) محصولات دارای لینک تأمین‌کننده (به‌جز «ناموجود دستی» و تأمین‌کنندهٔ غیرفعال) خوانده می‌شوند.
//   ۲) بر اساس دامنه و روش بررسی گروه‌بندی و با آداپتور مناسب از سایت منبع خوانده می‌شوند (با مکث بین درخواست‌ها).
//   ۳) SupplierSyncRules برای هر محصول تصمیم می‌گیرد: ناموجودی خودکار، یا پیشنهاد منتظر تأیید.
//   ۴) هر ۵۰ محصول ذخیره می‌شود تا قطع شدن وسط کار چیزی را خراب نکند؛ در پایان به کارمندان اعلان می‌رود.
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>نتیجهٔ یک اجرا (برای پیام صفحه و لاگ).</summary>
public sealed record SupplierSyncRunResult(Guid RunId, int Checked, int AutoApplied, int NewProposals, int Errors);

/// <summary>یک مورد تأیید: شناسهٔ تغییر و (اختیاری) موجودی دلخواه کارمند به‌جای عدد پیشنهادی.</summary>
public sealed record SupplierApproval(Guid ChangeId, int? Quantity);

/// <summary>یک ردیف جدول پیشنهادها/تاریخچه.</summary>
public sealed record SupplierChangeRow(
    Guid Id,
    Guid ProductId,
    string ProductName,
    string? ProductSku,
    string? ImageName,
    string? SupplierName,
    string? SupplierUrl,
    SupplierChangeKind Kind,
    SupplierChangeStatus Status,
    decimal? OldValue,
    decimal? NewValue,
    int? SourceStock,
    string? Note,
    int CurrentStock,
    decimal? PurchasePrice,
    decimal Price,
    decimal? DiscountPrice,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? DecidedAt,
    string? DecidedByName)
{
    /// <summary>قیمت فروش فعلی مشتری (با تخفیف اگر هست).</summary>
    public decimal SalePrice => DiscountPrice is > 0 && DiscountPrice < Price ? DiscountPrice.Value : Price;

    /// <summary>اختلاف عدد تازه با قبلی؛ مثلاً ۴۰۰٬۰۰۰ → ۴۱۵٬۰۰۰ یعنی ۱۵٬۰۰۰+.</summary>
    public decimal? Delta => OldValue is { } o && NewValue is { } n ? n - o : null;

    /// <summary>درصد تغییر (برای قیمت).</summary>
    public decimal? DeltaPercent => OldValue is > 0 && Delta is { } d ? Math.Round(d * 100m / OldValue.Value, 1) : null;

    /// <summary>
    /// هشدار زیان: با قیمت خرید تازه، فروش فعلی سودی ندارد. فقط برای تغییر قیمت.
    /// </summary>
    public bool LossWarning => Kind == SupplierChangeKind.PriceChange && NewValue is { } n && n >= SalePrice;
}

/// <summary>فیلترهای فهرست تغییرها.</summary>
/// <param name="Tab">pending = منتظر تأیید (موجودی و قیمت)، problems = پیدا نشد/خطا، auto = اعمال خودکار، history = رسیدگی‌شده.</param>
/// <summary>
/// یک ردیف «وضعیت محصولات»: آخرین چیزی که از سایت منبع برای هر محصول زیر نظر خوانده شد، کنار موجودی/قیمت خودمان.
/// در زبانهٔ «وضعیت همهٔ محصولات» صفحهٔ /admin/products/supplier-sync?tab=status نمایش داده می‌شود تا بعد از هر اجرا
/// (حتی وقتی هیچ تغییری پیشنهاد نشده) معلوم باشد هر کالا در منبع چه وضعی دارد.
/// </summary>
public sealed record SupplierStateRow(
    Guid ProductId,
    string ProductName,
    string? ProductSku,
    string? ImageName,
    string? SupplierUrl,
    bool Excluded,
    bool IsActive,
    int OurStock,
    decimal? PurchasePrice,
    SupplierItemStatus? Status,
    decimal? SourcePrice,
    int? SourceStock,
    string? SourceName,
    DateTimeOffset? CheckedAt,
    DateTimeOffset? LastSuccessAt,
    int ConsecutiveErrors,
    string? LastError,
    bool AutoOutOfStock)
{
    /// <summary>اختلاف قیمت منبع با قیمت خرید ثبت‌شدهٔ ما (مثبت = گران‌تر شده). مثال: ۴۰۰٬۰۰۰ → ۴۱۵٬۰۰۰ = +۱۵٬۰۰۰.</summary>
    public decimal? PriceDelta => SourcePrice is { } s && PurchasePrice is > 0 ? s - PurchasePrice.Value : null;
}

/// <summary>فیلتر زبانهٔ وضعیت: all / instock / out / notfound / error / unchecked.</summary>
public sealed record SupplierStateQuery(string Filter = "all", string? Search = null, int Page = 1, int Take = 50);

public sealed record SupplierChangeQuery(string Tab = "pending", SupplierChangeKind? Kind = null, string? Search = null, int Page = 1, int Take = 50);

/// <summary>داشبورد صفحهٔ بررسی تأمین‌کننده.</summary>
public sealed record SupplierSyncDashboard(
    bool Enabled,
    int RunsPerDay,
    bool AutoOutOfStock,
    DateTimeOffset NextRun,
    bool IsRunning,
    int TrackedProducts,
    int ExcludedProducts,
    int ProductsWithoutUrl,
    int PendingCount,
    int ProblemCount,
    IReadOnlyDictionary<SupplierChangeKind, int> PendingByKind,
    IReadOnlyList<SupplierSyncRun> RecentRuns);

/// <summary>وضعیت منبع یک محصول برای کارت صفحهٔ ویرایش.</summary>
public sealed record SupplierProductStatusDto(
    SupplierItemStatus Status,
    decimal? SourcePrice,
    int? SourceStock,
    string? SourceName,
    DateTimeOffset? CheckedAt,
    DateTimeOffset? LastSuccessAt,
    int ConsecutiveErrors,
    string? LastError,
    bool AutoOutOfStock,
    int PendingChanges);

public interface ISupplierSyncService
{
    /// <summary>آیا الان اجرایی در جریان است (در همین پروسه).</summary>
    bool IsRunning { get; }

    /// <summary>آیا نوبت اجرای زمان‌بندی‌شده رسیده است (و بررسی فعال است).</summary>
    Task<bool> IsDueAsync(DateTimeOffset now, CancellationToken cancellationToken = default);

    /// <summary>
    /// یک بار بررسی کامل. اگر اجرای دیگری در جریان باشد یا (برای اجرای زمان‌بندی‌شده) بررسی خاموش باشد null برمی‌گردد.
    /// </summary>
    Task<SupplierSyncRunResult?> RunAsync(SupplierSyncTrigger trigger, string? startedByName, CancellationToken cancellationToken);

    Task<SupplierSyncDashboard> GetDashboardAsync(CancellationToken cancellationToken = default);

    Task<PagedList<SupplierChangeRow>> ListChangesAsync(SupplierChangeQuery query, CancellationToken cancellationToken = default);

    /// <summary>وضعیت فعلی همهٔ محصولات زیر نظر (آخرین بررسی هر کدام) با فیلتر و صفحه‌بندی.</summary>
    Task<PagedList<SupplierStateRow>> ListStatesAsync(SupplierStateQuery query, CancellationToken cancellationToken = default);

    /// <summary>شمارش وضعیت‌ها برای برچسب فیلترها؛ کلید همان مقدار فیلتر (instock, out, notfound, error, unchecked).</summary>
    Task<IReadOnlyDictionary<string, int>> CountStatesAsync(CancellationToken cancellationToken = default);

    /// <summary>تأیید گروهی. <paramref name="adjustSalePrice"/>: در تغییر قیمت، همان اختلاف به قیمت فروش (و تخفیف) هم اضافه شود.</summary>
    Task<OperationResult> ApproveAsync(IReadOnlyList<SupplierApproval> approvals, bool adjustSalePrice, Guid userId, string userName);

    Task<OperationResult> RejectAsync(IReadOnlyList<Guid> changeIds, Guid userId, string userName);

    Task<SupplierProductStatusDto?> GetProductStatusAsync(Guid productId, CancellationToken cancellationToken = default);

    /// <summary>دور ۲۳ (مورد ۷۴): اعتبار هر تأمین‌کننده از آخرین وضعیت کالاها و تغییر قیمت‌های ۳۰ روز اخیر (<see cref="SupplierReliability"/>).</summary>
    Task<IReadOnlyList<SupplierReliabilityRow>> GetReliabilityAsync(CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public class SupplierSyncService(
    FratakShopDbContext context,
    IStoreSettingService settings,
    ISupplierHttp http,
    IStaffNotificationService staff) : ISupplierSyncService
{
    /// <summary>فقط یک اجرا در هر لحظه (چه زمان‌بندی‌شده چه دستی).</summary>
    private static readonly SemaphoreSlim Gate = new(1, 1);

    /// <summary>سقف محصولات در یک اجرا (محافظ کارایی).</summary>
    private const int MaxProducts = 5000;

    /// <summary>هر چند محصول یک بار ذخیره شود.</summary>
    private const int SaveBatch = 50;

    /// <summary>سقف تعداد در یک تأیید/رد گروهی.</summary>
    private const int MaxBulk = 500;

    private const string SystemName = "سیستم – بررسی تأمین‌کننده";

    public bool IsRunning => Gate.CurrentCount == 0;

    public async Task<bool> IsDueAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var current = await settings.GetAsync(cancellationToken);
        if (!current.IsSupplierSyncEnabled)
            return false;
        var last = await context.SupplierSyncRuns.AsNoTracking()
            .Where(r => r.Trigger == SupplierSyncTrigger.Scheduled)
            .OrderByDescending(r => r.StartedAt)
            .Select(r => (DateTimeOffset?)r.StartedAt)
            .FirstOrDefaultAsync(cancellationToken);
        return SupplierSyncSchedule.IsDue(now, current.SupplierSyncRunsPerDay, last);
    }

    // ── اجرا ────────────────────────────────────────────────────────────

    private sealed record Candidate(Guid Id, string Url, SupplierSyncMode Mode, int Stock = 0);

    /// <summary>آخرین رسیدگی کارمند به یک نوع تغییر برای یک محصول (برای جلوگیری از پیشنهاد تکراری).</summary>
    private sealed record DecidedInfo(SupplierChangeStatus Status, decimal? NewValue, DateTimeOffset? DecidedAt);

    public async Task<SupplierSyncRunResult?> RunAsync(SupplierSyncTrigger trigger, string? startedByName, CancellationToken cancellationToken)
    {
        // هر تغییر موجودی در این اجرا با دلیل «تأمین‌کننده» در دفتر موجودی ثبت می‌شود.
        using var stockReason = Fratak_Shop.Data.Utilities.StockContext.Begin(StockMovementReason.Supplier, "بررسی خودکار تأمین‌کننده");
        var current = await settings.GetAsync(cancellationToken);
        if (trigger == SupplierSyncTrigger.Scheduled && !current.IsSupplierSyncEnabled)
            return null;
        if (!await Gate.WaitAsync(0, cancellationToken))
            return null;

        // همهٔ تغییرهای این اجرا در «تاریخچهٔ تغییرات» به نام سیستم ثبت می‌شوند.
        using var audit = AuditContext.Begin(new AuditActor(null,
            startedByName is null ? SystemName : $"{SystemName} (به درخواست {startedByName})", null, "/admin/products/supplier-sync"));
        var run = new SupplierSyncRun
        {
            Trigger = trigger,
            StartedByName = startedByName,
            StartedAt = DateTimeOffset.UtcNow,
        };
        try
        {
            context.SupplierSyncRuns.Add(run);
            await context.SaveChangesAsync(cancellationToken);

            var policy = new SupplierSyncPolicy(current.SupplierAutoOutOfStock, current.SupplierStockMarginPercent, current.SupplierStockMaxQuantity,
                RepricePolicy(current));
            var delay = TimeSpan.FromMilliseconds(Math.Clamp(current.SupplierRequestDelayMs, 200, 30000));
            await ProcessAsync(run, policy, delay, cancellationToken);

            run.FinishedAt = DateTimeOffset.UtcNow;
            await context.SaveChangesAsync(CancellationToken.None);
            await NotifyAsync(run);
            await CleanupAsync();
            return new SupplierSyncRunResult(run.Id, run.Checked, run.AutoApplied, run.NewProposals, run.Errors);
        }
        catch (Exception ex)
        {
            // ثبت علت توقف روی همان اجرا تا در داشبورد دیده شود؛ خطا برای ثبت در «کارهای پس‌زمینه» بالا می‌رود.
            await RecordFailureAsync(run, ex is OperationCanceledException ? "اجرا متوقف شد (خاموش شدن سرور یا لغو)." : $"خطای کلی: {ex.Message}");
            throw;
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>
    /// پاک‌سازی سوابق قدیمی تا جدول‌ها بی‌نهایت بزرگ نشوند: اجراهای قدیمی‌تر از ۹۰ روز و
    /// تغییرهای رسیدگی‌شده/خودکار/جایگزین‌شدهٔ قدیمی‌تر از ۱۸۰ روز. پیشنهادهای منتظر هرگز پاک نمی‌شوند.
    /// </summary>
    private async Task CleanupAsync()
    {
        try
        {
            var runsBefore = DateTimeOffset.UtcNow.AddDays(-90);
            var changesBefore = DateTimeOffset.UtcNow.AddDays(-180);
            await context.SupplierSyncRuns.IgnoreQueryFilters().Where(r => r.StartedAt < runsBefore).ExecuteDeleteAsync();
            await context.SupplierChanges.IgnoreQueryFilters()
                .Where(c => c.Status != SupplierChangeStatus.Pending && c.CreatedAt < changesBefore)
                .ExecuteDeleteAsync();
        }
        catch
        {
            // پاک‌سازی اختیاری است و نباید نتیجهٔ اجرا را خراب کند.
        }
    }

    private async Task RecordFailureAsync(SupplierSyncRun run, string message)
    {
        try
        {
            if (context.Entry(run).State == EntityState.Detached)
                return;
            run.FinishedAt = DateTimeOffset.UtcNow;
            run.Message = message.Length <= 1000 ? message : message[..1000];
            await context.SaveChangesAsync(CancellationToken.None);
        }
        catch
        {
            // ذخیرهٔ پیام خطا نباید خطای اصلی را بپوشاند.
        }
    }

    /// <summary>بدنهٔ اجرا: خواندن، مقایسه و ذخیرهٔ دسته‌ای.</summary>
    private async Task ProcessAsync(SupplierSyncRun run, SupplierSyncPolicy policy, TimeSpan delay, CancellationToken cancellationToken)
    {
        var candidates = await context.Products.AsNoTracking()
            .Where(p => p.SupplierUrl != null && p.SupplierUrl != "" && !p.ExcludeFromSupplierSync
                        && (p.SupplierId == null || (p.Supplier!.IsActive && p.Supplier.SyncMode != SupplierSyncMode.Disabled)))
            .OrderBy(p => p.SupplierId).ThenBy(p => p.Id)
            .Select(p => new Candidate(p.Id, p.SupplierUrl!, p.Supplier == null ? SupplierSyncMode.Auto : p.Supplier.SyncMode, p.StockQuantity))
            .Take(MaxProducts)
            .ToListAsync(cancellationToken);

        var states = await context.SupplierProductStates.AsTracking().ToDictionaryAsync(s => s.ProductId, cancellationToken);

        // دور ۲۳ (مورد ۷۰): بررسی تطبیقی – فقط در اجرای زمان‌بندی‌شده کالای پایدار/مشکل‌دار این نوبت رد می‌شود (SupplierAdaptiveSync).
        var skipped = 0;
        var reachedLimit = candidates.Count >= MaxProducts;
        if (run.Trigger == SupplierSyncTrigger.Scheduled && states.Count > 0)
        {
            var changeSince = DateTimeOffset.UtcNow.AddDays(-7);
            var recentlyChanged = (await context.SupplierChanges.AsNoTracking()
                    .Where(c => c.CreatedAt >= changeSince)
                    .GroupBy(c => c.ProductId)
                    .Select(g => new { ProductId = g.Key, Last = g.Max(c => c.CreatedAt) })
                    .ToListAsync(cancellationToken))
                .ToDictionary(x => x.ProductId, x => x.Last);
            var clock = DateTimeOffset.UtcNow;
            var kept = new List<Candidate>(candidates.Count);
            foreach (var candidate in candidates)
            {
                // لینک عوض‌شده (ResolvedUrl با لینک فعلی فرق دارد) هرگز رد نمی‌شود.
                if (states.TryGetValue(candidate.Id, out var known) && (known.ResolvedUrl is null || known.ResolvedUrl == candidate.Url)
                    && SupplierAdaptiveSync.Skip(known.Status, known.CheckedAt, known.ConsecutiveErrors,
                        recentlyChanged.TryGetValue(candidate.Id, out var last) ? last : null, candidate.Stock, clock))
                {
                    skipped++;
                    continue;
                }
                kept.Add(candidate);
            }
            candidates = kept;
        }
        var pending = (await context.SupplierChanges.AsTracking()
                .Where(c => c.Status == SupplierChangeStatus.Pending)
                .ToListAsync(cancellationToken))
            .GroupBy(c => (c.ProductId, c.Kind))
            .ToDictionary(g => g.Key, g => g.OrderByDescending(c => c.UpdatedAt).First());
        var since = DateTimeOffset.UtcNow.AddDays(-90);
        var decided = (await context.SupplierChanges.AsNoTracking()
                .Where(c => (c.Status == SupplierChangeStatus.Rejected || c.Status == SupplierChangeStatus.Approved) && c.DecidedAt >= since)
                .Select(c => new { c.ProductId, c.Kind, c.Status, c.NewValue, c.DecidedAt })
                .ToListAsync(cancellationToken))
            .GroupBy(c => (c.ProductId, c.Kind))
            .ToDictionary(g => g.Key, g => g.OrderByDescending(c => c.DecidedAt).Select(c => new DecidedInfo(c.Status, c.NewValue, c.DecidedAt)).First());

        var registry = new SupplierAdapterRegistry();
        var contexts = new Dictionary<string, SupplierFetchContext>(StringComparer.OrdinalIgnoreCase);

        foreach (var group in candidates.GroupBy(c => (Host: Host(c.Url), c.Mode)))
        {
            if (group.Key.Host is null)
            {
                // لینک خراب: بدون درخواست، به‌عنوان «پیدا نشد» گزارش می‌شود.
                foreach (var chunk in group.Chunk(SaveBatch))
                    await ApplyChunkAsync(run, policy, chunk.ToDictionary(c => c.Id, _ => SupplierSnapshot.NotFound("لینک تأمین‌کننده معتبر نیست.")), chunk, states, pending, decided, cancellationToken);
                continue;
            }

            if (!contexts.TryGetValue(group.Key.Host, out var fetch))
                contexts[group.Key.Host] = fetch = new SupplierFetchContext(http, delay, cancellationToken);

            var items = group.ToList();
            ISupplierAdapter? adapter;
            try
            {
                adapter = await registry.ResolveAsync(group.Key.Mode, items[0].Url, fetch);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                adapter = null;
                foreach (var chunk in items.Chunk(SaveBatch))
                    await ApplyChunkAsync(run, policy, chunk.ToDictionary(c => c.Id, _ => SupplierSnapshot.Failed($"تشخیص نوع سایت ناموفق بود: {ex.Message}")), chunk, states, pending, decided, cancellationToken);
            }
            if (adapter is null)
                continue;

            foreach (var chunk in items.Chunk(SaveBatch))
            {
                var refs = chunk.Select(c => new SupplierProductRef(c.Id, c.Url,
                    states.TryGetValue(c.Id, out var s) && s.ResolvedUrl == c.Url ? s.ExternalId : null)).ToList();
                Dictionary<Guid, SupplierSnapshot> snapshots;
                try
                {
                    snapshots = await adapter.FetchAsync(refs, fetch);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    snapshots = chunk.ToDictionary(c => c.Id, _ => SupplierSnapshot.Failed($"خطای آداپتور: {ex.Message}"));
                }
                await ApplyChunkAsync(run, policy, snapshots, chunk, states, pending, decided, cancellationToken);
            }
        }

        var notes = new List<string>(2);
        if (reachedLimit)
            notes.Add($"فقط {MaxProducts:N0} محصول اول بررسی شد.");
        if (skipped > 0)
            notes.Add($"{skipped:N0} کالای پایدار یا مشکل‌دار طبق بررسی تطبیقی در این نوبت بررسی نشد.");
        run.Message = notes.Count == 0 ? null : string.Join(" ", notes);
    }

    /// <summary>
    /// سیاست قیمت‌گذاری دوباره از تنظیمات فروشگاه (تب «تأمین‌کننده»). مثال پیش‌فرض: افزایش‌ها خودکار، حفظ درصد سود، کف ۵٪.
    /// </summary>
    internal static SupplierRepricePolicy RepricePolicy(StoreSettingDto current) => new(
        current.SupplierPriceAutoMode,
        current.SupplierRepriceMode,
        current.SupplierRepriceMarkupPercent,
        current.SupplierRepriceMarkupAmount,
        current.SupplierRepriceMinMarginPercent,
        current.SupplierRepriceRounding,
        current.SupplierRepriceOnDecrease,
        current.SupplierRepriceMaxJumpPercent);

    /// <summary>اعمال نتیجهٔ یک دسته محصول و ذخیره.</summary>
    private async Task ApplyChunkAsync(
        SupplierSyncRun run,
        SupplierSyncPolicy policy,
        Dictionary<Guid, SupplierSnapshot> snapshots,
        IReadOnlyCollection<Candidate> chunk,
        Dictionary<Guid, SupplierProductState> states,
        Dictionary<(Guid, SupplierChangeKind), SupplierChange> pending,
        IReadOnlyDictionary<(Guid, SupplierChangeKind), DecidedInfo> decided,
        CancellationToken cancellationToken)
    {
        var ids = chunk.Select(c => c.Id).ToList();
        var products = await context.Products.AsTracking().Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        Dictionary<Guid, Pricing.CategoryMarginNode>? marginNodes = null; // دور ۲۳: فقط وقتی قیمتی عوض شود خوانده می‌شود

        foreach (var candidate in chunk)
        {
            if (!products.TryGetValue(candidate.Id, out var product))
                continue; // در همین فاصله حذف شده
            var snapshot = snapshots.TryGetValue(candidate.Id, out var s) ? s : SupplierSnapshot.Failed("آداپتور برای این محصول نتیجه‌ای برنگرداند.");

            if (!states.TryGetValue(candidate.Id, out var state))
            {
                state = new SupplierProductState { ProductId = candidate.Id };
                context.SupplierProductStates.Add(state);
                states[candidate.Id] = state;
            }

            var view = new SupplierProductView(product.StockQuantity, product.PurchasePrice, state.AutoOutOfStock, state.ConsecutiveErrors,
                WasNotFound: state.CheckedAt is not null && state.Status == SupplierItemStatus.NotFound);
            UpdateState(state, snapshot, candidate.Url, now);
            Count(run, snapshot.Status);

            foreach (var decision in SupplierSyncRules.Evaluate(snapshot, view, policy))
            {
                var key = (candidate.Id, decision.Kind);
                pending.TryGetValue(key, out var existing);
                switch (decision.Action)
                {
                    case SupplierDecisionAction.Supersede:
                        if (existing is not null)
                        {
                            existing.Status = SupplierChangeStatus.Superseded;
                            existing.DecidedAt = now;
                            existing.DecidedByName = SystemName;
                            pending.Remove(key);
                        }
                        break;

                    case SupplierDecisionAction.AutoApply:
                        var note = decision.Note;
                        if (decision.Kind == SupplierChangeKind.PriceChange)
                        {
                            // دور ۱۰: قیمت خرید تازه + قیمت فروش طبق سیاست تنظیمات (تاریخچهٔ قیمت را DbContext خودکار ثبت می‌کند).
                            var newCost = decision.NewValue ?? 0;
                            // دور ۲۳ (مورد ۶۹): کف سود دستهٔ کالا (و دسته‌های بالادستی) – فقط بالاتر از کف سراسری.
                            var basePolicy = policy.Reprice ?? SupplierRepricePolicy.Default;
                            marginNodes ??= await Pricing.CategoryMargins.NodesAsync(context, cancellationToken);
                            var categoryPolicy = basePolicy with { MinMarginPercent = Pricing.CategoryMargins.Effective(basePolicy.MinMarginPercent, product.CategoryId, marginNodes) };
                            var priced = SupplierRepricing.Calculate(product.PurchasePrice, newCost, product.Price, product.DiscountPrice, categoryPolicy);
                            product.PurchasePrice = newCost;
                            product.Price = priced.Price;
                            product.DiscountPrice = priced.DiscountPrice;
                            note = Clip(priced.Note, 480);
                        }
                        else
                        {
                            product.StockQuantity = (int)(decision.NewValue ?? 0);
                            if (decision.Kind == SupplierChangeKind.OutOfStock)
                                state.AutoOutOfStock = true;
                        }
                        context.SupplierChanges.Add(new SupplierChange
                        {
                            ProductId = candidate.Id,
                            RunId = run.Id,
                            Kind = decision.Kind,
                            Status = SupplierChangeStatus.AutoApplied,
                            OldValue = decision.OldValue,
                            NewValue = decision.NewValue,
                            SourceStock = snapshot.Stock,
                            Note = note,
                            DecidedAt = now,
                            DecidedByName = SystemName,
                        });
                        if (existing is not null)
                        {
                            existing.Status = SupplierChangeStatus.Superseded;
                            existing.DecidedAt = now;
                            existing.DecidedByName = SystemName;
                            pending.Remove(key);
                        }
                        run.AutoApplied++;
                        break;

                    case SupplierDecisionAction.Propose:
                        if (existing is not null)
                        {
                            // همان ردیف منتظر با عدد تازه به‌روز می‌شود (فهرست شلوغ نمی‌شود).
                            if (existing.NewValue != decision.NewValue || existing.OldValue != decision.OldValue || existing.SourceStock != snapshot.Stock || existing.Note != decision.Note)
                            {
                                existing.OldValue = decision.OldValue;
                                existing.NewValue = decision.NewValue;
                                existing.SourceStock = snapshot.Stock;
                                existing.Note = decision.Note;
                                existing.RunId = run.Id;
                            }
                            break;
                        }
                        if (WasAlreadyDecided(decided, key, decision, state))
                            break;
                        var change = new SupplierChange
                        {
                            ProductId = candidate.Id,
                            RunId = run.Id,
                            Kind = decision.Kind,
                            Status = SupplierChangeStatus.Pending,
                            OldValue = decision.OldValue,
                            NewValue = decision.NewValue,
                            SourceStock = snapshot.Stock,
                            Note = decision.Note,
                        };
                        context.SupplierChanges.Add(change);
                        pending[key] = change;
                        run.NewProposals++;
                        break;
                }
            }
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// جلوگیری از تکرار پیشنهادی که کارمند قبلاً رسیدگی کرده:
    /// موجودی/قیمت: اگر آخرین «رد» همین عدد بوده، دوباره پیشنهاد نمی‌شود.
    /// پیدا نشد/خطا: اگر بعد از آخرین خواندن موفق کارمند آن را دیده (تأیید یا رد)، تکرار نمی‌شود.
    /// </summary>
    private static bool WasAlreadyDecided(IReadOnlyDictionary<(Guid, SupplierChangeKind), DecidedInfo> decided, (Guid, SupplierChangeKind) key, SupplierDecision decision, SupplierProductState state)
    {
        if (!decided.TryGetValue(key, out var last))
            return false;
        if (decision.Kind is SupplierChangeKind.NotFound or SupplierChangeKind.Error)
            return state.LastSuccessAt is null || last.DecidedAt > state.LastSuccessAt;
        return last.Status == SupplierChangeStatus.Rejected && last.NewValue == decision.NewValue;
    }

    /// <summary>به‌روزرسانی آخرین وضعیت منبع یک محصول.</summary>
    private static void UpdateState(SupplierProductState state, SupplierSnapshot snapshot, string url, DateTimeOffset now)
    {
        state.CheckedAt = now;
        switch (snapshot.Status)
        {
            case SupplierItemStatus.Error:
                // آخرین وضعیت معلوم حفظ می‌شود؛ فقط خطا شمرده می‌شود.
                state.ConsecutiveErrors++;
                state.LastError = Clip(snapshot.Error, 500);
                break;
            case SupplierItemStatus.NotFound:
                state.Status = SupplierItemStatus.NotFound;
                state.ExternalId = null;
                state.ResolvedUrl = null;
                state.ConsecutiveErrors = 0;
                state.LastError = Clip(snapshot.Error, 500);
                break;
            default:
                state.Status = snapshot.Status;
                state.SourcePrice = snapshot.Price;
                state.SourceStock = snapshot.Stock;
                if (snapshot.Name is not null)
                    state.SourceName = Clip(snapshot.Name, 300);
                if (snapshot.ExternalId is not null)
                {
                    state.ExternalId = Clip(snapshot.ExternalId, 100);
                    state.ResolvedUrl = Clip(url, 1000);
                }
                state.LastSuccessAt = now;
                state.ConsecutiveErrors = 0;
                state.LastError = null;
                // پرچم AutoOutOfStock («سیستم صفر کرده») فقط با تأیید کارمند در ApproveAsync پاک می‌شود.
                break;
        }
    }

    private static void Count(SupplierSyncRun run, SupplierItemStatus status)
    {
        run.Checked++;
        switch (status)
        {
            case SupplierItemStatus.InStock: run.InStock++; break;
            case SupplierItemStatus.OutOfStock: run.OutOfStock++; break;
            case SupplierItemStatus.NotFound: run.NotFound++; break;
            default: run.Errors++; break;
        }
    }

    /// <summary>
    /// اعلان خلاصهٔ اجرا به کارمندان مسئول تأمین.
    /// <list type="bullet">
    /// <item>اجرای دستی («بررسی همین الان»): <b>همیشه</b> گزارش می‌دهد، حتی اگر هیچ تغییری نبود – کسی که دکمه را زده منتظر نتیجه است.</item>
    /// <item>اجرای زمان‌بندی‌شده: فقط وقتی چیزی برای دیدن هست (تغییر خودکار، پیشنهاد تازه، خطا یا محصول پیدانشده) تا اعلان‌ها شلوغ نشود.</item>
    /// </list>
    /// مثال متن: «۱۲۰ محصول بررسی شد: ۱۰۵ موجود، ۱۲ ناموجود، ۳ پیدا نشد · ۲ تغییر خودکار · ۴ پیشنهاد تازه · مجموع منتظر تأیید: ۶».
    /// </summary>
    private async Task NotifyAsync(SupplierSyncRun run)
    {
        var attention = run.NewProposals + run.AutoApplied + run.Errors + run.NotFound;
        if (run.Trigger != SupplierSyncTrigger.Manual && attention == 0)
            return;

        var parts = new List<string>
        {
            run.Checked == 0
                ? "هیچ محصولی برای بررسی نبود (لینک منبع روی محصولات ثبت نشده یا همه از بررسی خارج شده‌اند)"
                : $"{run.Checked:N0} محصول بررسی شد: {run.InStock:N0} موجود، {run.OutOfStock:N0} ناموجود" +
                  (run.NotFound > 0 ? $"، {run.NotFound:N0} پیدا نشد" : "") + (run.Errors > 0 ? $"، {run.Errors:N0} خطای خواندن" : ""),
        };
        if (run.AutoApplied > 0)
            parts.Add($"{run.AutoApplied:N0} تغییر خودکار (ناموجود/کاهش موجودی)");
        if (run.NewProposals > 0)
            parts.Add($"{run.NewProposals:N0} پیشنهاد تازه");
        int pendingTotal = 0;
        try
        {
            pendingTotal = await context.SupplierChanges.AsNoTracking().CountAsync(c => c.Status == SupplierChangeStatus.Pending);
        }
        catch
        {
            // شمارش اختیاری است.
        }
        if (pendingTotal > 0)
            parts.Add($"مجموع منتظر تأیید: {pendingTotal:N0}");
        if (attention == 0)
            parts.Add("تغییری نسبت به قبل نبود");

        var url = pendingTotal > 0 ? "/admin/products/supplier-sync"
            : run.AutoApplied > 0 ? "/admin/products/supplier-sync?tab=auto"
            : "/admin/products/supplier-sync?tab=status";
        try
        {
            await staff.NotifyAsync(new StaffAlert(
                StaffNotificationKind.SupplierReport,
                run.Trigger == SupplierSyncTrigger.Manual ? "نتیجهٔ بررسی دستی تأمین‌کننده" : "گزارش بررسی تأمین‌کننده",
                string.Join(" · ", parts),
                url,
                [AdminPermissions.ProductsSupplier, AdminPermissions.ProductsManage]));
        }
        catch
        {
            // اعلان ناموفق نباید نتیجهٔ اجرا را از بین ببرد.
        }
    }

    private static string? Host(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" ? uri.Host : null;

    private static string? Clip(string? text, int max) => text is null ? null : text.Length <= max ? text : text[..max];

    // ── داشبورد و فهرست ─────────────────────────────────────────────────

    public async Task<SupplierSyncDashboard> GetDashboardAsync(CancellationToken cancellationToken = default)
    {
        var current = await settings.GetAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;

        var withUrl = context.Products.AsNoTracking().Where(p => p.SupplierUrl != null && p.SupplierUrl != "");
        var tracked = await withUrl.CountAsync(p => !p.ExcludeFromSupplierSync, cancellationToken);
        var excluded = await withUrl.CountAsync(p => p.ExcludeFromSupplierSync, cancellationToken);
        var withoutUrl = await context.Products.AsNoTracking().CountAsync(p => p.SupplierUrl == null || p.SupplierUrl == "", cancellationToken);

        var byKind = await context.SupplierChanges.AsNoTracking()
            .Where(c => c.Status == SupplierChangeStatus.Pending)
            .GroupBy(c => c.Kind)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, cancellationToken);
        var problems = byKind.Where(k => k.Key is SupplierChangeKind.NotFound or SupplierChangeKind.Error).Sum(k => k.Value);

        var runs = await context.SupplierSyncRuns.AsNoTracking()
            .OrderByDescending(r => r.StartedAt)
            .Take(10)
            .ToListAsync(cancellationToken);

        return new SupplierSyncDashboard(
            current.IsSupplierSyncEnabled,
            current.SupplierSyncRunsPerDay,
            current.SupplierAutoOutOfStock,
            SupplierSyncSchedule.NextSlot(now, current.SupplierSyncRunsPerDay),
            IsRunning,
            tracked,
            excluded,
            withoutUrl,
            byKind.Values.Sum() - problems,
            problems,
            byKind,
            runs);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SupplierReliabilityRow>> GetReliabilityAsync(CancellationToken cancellationToken = default)
    {
        // فقط شمارش‌های گروهی در SQL (سبک حتی با هزاران کالا).
        var statuses = await context.SupplierProductStates.AsNoTracking()
            .Where(s => s.CheckedAt != null)
            .GroupBy(s => new { s.Product.SupplierId, s.Status })
            .Select(g => new { g.Key.SupplierId, g.Key.Status, Count = g.Count() })
            .ToListAsync(cancellationToken);
        var since = DateTimeOffset.UtcNow.AddDays(-30);
        var priceChanges = (await context.SupplierChanges.AsNoTracking()
                .Where(c => c.Kind == SupplierChangeKind.PriceChange && c.CreatedAt >= since)
                .GroupBy(c => c.Product.SupplierId)
                .Select(g => new { SupplierId = g.Key, Count = g.Count() })
                .ToListAsync(cancellationToken))
            .ToDictionary(x => x.SupplierId ?? Guid.Empty, x => x.Count);
        var names = await context.Suppliers.AsNoTracking().ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);

        var rows = new List<SupplierReliabilityRow>();
        foreach (var group in statuses.GroupBy(x => x.SupplierId))
        {
            int Of(SupplierItemStatus status) => group.Where(x => x.Status == status).Sum(x => x.Count);
            var inStock = Of(SupplierItemStatus.InStock);
            var outOfStock = Of(SupplierItemStatus.OutOfStock);
            var notFound = Of(SupplierItemStatus.NotFound);
            var errors = group.Sum(x => x.Count) - inStock - outOfStock - notFound;
            var checkedCount = inStock + outOfStock + notFound + errors;
            var changes = priceChanges.TryGetValue(group.Key ?? Guid.Empty, out var count) ? count : 0;
            var score = SupplierReliability.Score(inStock, outOfStock, notFound, errors, changes);
            var name = group.Key is { } id && names.TryGetValue(id, out var supplierName) ? supplierName : "بدون تأمین‌کنندهٔ تعریف‌شده (فقط لینک)";
            rows.Add(new SupplierReliabilityRow(group.Key, name, checkedCount, inStock, outOfStock, notFound, errors, changes, score,
                SupplierReliability.Verdict(score, checkedCount)));
        }
        return rows.OrderBy(r => r.Products < SupplierReliability.MinProducts).ThenBy(r => r.Score).ToList();
    }

    public async Task<PagedList<SupplierChangeRow>> ListChangesAsync(SupplierChangeQuery query, CancellationToken cancellationToken = default)
    {
        var take = Math.Clamp(query.Take, 10, 200);
        var page = Math.Max(1, query.Page);
        var changes = context.SupplierChanges.AsNoTracking();
        changes = query.Tab switch
        {
            "problems" => changes.Where(c => c.Status == SupplierChangeStatus.Pending && (c.Kind == SupplierChangeKind.NotFound || c.Kind == SupplierChangeKind.Error)),
            "auto" => changes.Where(c => c.Status == SupplierChangeStatus.AutoApplied),
            "history" => changes.Where(c => c.Status == SupplierChangeStatus.Approved || c.Status == SupplierChangeStatus.Rejected),
            _ => changes.Where(c => c.Status == SupplierChangeStatus.Pending && c.Kind != SupplierChangeKind.NotFound && c.Kind != SupplierChangeKind.Error),
        };
        if (query.Kind is { } kind)
            changes = changes.Where(c => c.Kind == kind);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = SearchText.NormalizeDigits(query.Search.Trim());
            changes = changes.Where(c => c.Product.Name.Contains(term) || (c.Product.SKU != null && c.Product.SKU.Contains(term)));
        }

        var total = await changes.CountAsync(cancellationToken);
        var ordered = query.Tab is "auto" or "history"
            ? changes.OrderByDescending(c => c.DecidedAt)
            : changes.OrderBy(c => c.Kind).ThenByDescending(c => c.UpdatedAt);
        var items = await ordered
            .Skip((page - 1) * take)
            .Take(take)
            .Select(c => new SupplierChangeRow(
                c.Id,
                c.ProductId,
                c.Product.Name,
                c.Product.SKU,
                c.Product.Images.OrderByDescending(i => i.IsPrimary).ThenBy(i => i.DisplayOrder).Select(i => i.ImageName).FirstOrDefault(),
                c.Product.Supplier != null ? c.Product.Supplier.Name : null,
                c.Product.SupplierUrl,
                c.Kind,
                c.Status,
                c.OldValue,
                c.NewValue,
                c.SourceStock,
                c.Note,
                c.Product.StockQuantity,
                c.Product.PurchasePrice,
                c.Product.Price,
                c.Product.DiscountPrice,
                c.CreatedAt,
                c.UpdatedAt,
                c.DecidedAt,
                c.DecidedByName))
            .ToListAsync(cancellationToken);
        return new PagedList<SupplierChangeRow>(items, total, page, take);
    }

    public async Task<PagedList<SupplierStateRow>> ListStatesAsync(SupplierStateQuery query, CancellationToken cancellationToken = default)
    {
        var take = Math.Clamp(query.Take, 10, 200);
        var page = Math.Max(1, query.Page);
        var states = context.SupplierProductStates.AsNoTracking();
        var products = context.Products.AsNoTracking().Where(p => p.SupplierUrl != null && p.SupplierUrl != "");
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = SearchText.NormalizeDigits(query.Search.Trim());
            products = products.Where(p => p.Name.Contains(term) || (p.SKU != null && p.SKU.Contains(term)) || p.SupplierUrl!.Contains(term));
        }
        products = query.Filter switch
        {
            "instock" => products.Where(p => states.Any(s => s.ProductId == p.Id && s.Status == SupplierItemStatus.InStock)),
            "out" => products.Where(p => states.Any(s => s.ProductId == p.Id && s.Status == SupplierItemStatus.OutOfStock)),
            "notfound" => products.Where(p => states.Any(s => s.ProductId == p.Id && s.Status == SupplierItemStatus.NotFound)),
            "error" => products.Where(p => states.Any(s => s.ProductId == p.Id && s.ConsecutiveErrors > 0)),
            "unchecked" => products.Where(p => !p.ExcludeFromSupplierSync && !states.Any(s => s.ProductId == p.Id)),
            "excluded" => products.Where(p => p.ExcludeFromSupplierSync),
            _ => products,
        };

        var total = await products.CountAsync(cancellationToken);
        // مشکل‌دارها اول (خطا، پیدا نشد، ناموجود)، بعد بقیه به ترتیب نام.
        var items = await products
            .OrderByDescending(p => states.Where(s => s.ProductId == p.Id).Select(s => s.ConsecutiveErrors > 0 ? 4 : s.Status == SupplierItemStatus.NotFound ? 3 : s.Status == SupplierItemStatus.OutOfStock ? 2 : 1).FirstOrDefault())
            .ThenBy(p => p.Name)
            .Skip((page - 1) * take)
            .Take(take)
            .Select(p => new
            {
                p.Id, p.Name, p.SKU, p.SupplierUrl, p.ExcludeFromSupplierSync, p.IsActive, p.StockQuantity, p.PurchasePrice,
                Image = p.Images.OrderByDescending(i => i.IsPrimary).ThenBy(i => i.DisplayOrder).Select(i => i.ImageName).FirstOrDefault(),
                State = states.FirstOrDefault(s => s.ProductId == p.Id),
            })
            .ToListAsync(cancellationToken);

        var rows = items.Select(x => new SupplierStateRow(
            x.Id, x.Name, x.SKU, x.Image, x.SupplierUrl, x.ExcludeFromSupplierSync, x.IsActive, x.StockQuantity, x.PurchasePrice,
            x.State?.Status, x.State?.SourcePrice, x.State?.SourceStock, x.State?.SourceName, x.State?.CheckedAt, x.State?.LastSuccessAt,
            x.State?.ConsecutiveErrors ?? 0, x.State?.LastError, x.State?.AutoOutOfStock ?? false)).ToList();
        return new PagedList<SupplierStateRow>(rows, total, page, take);
    }

    public async Task<IReadOnlyDictionary<string, int>> CountStatesAsync(CancellationToken cancellationToken = default)
    {
        var tracked = context.Products.AsNoTracking().Where(p => p.SupplierUrl != null && p.SupplierUrl != "" && !p.ExcludeFromSupplierSync).Select(p => p.Id);
        var states = context.SupplierProductStates.AsNoTracking().Where(s => tracked.Contains(s.ProductId));
        var byStatus = await states.GroupBy(s => s.Status).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(cancellationToken);
        var errors = await states.CountAsync(s => s.ConsecutiveErrors > 0, cancellationToken);
        var checkedCount = await states.CountAsync(cancellationToken);
        var trackedCount = await tracked.CountAsync(cancellationToken);
        return new Dictionary<string, int>
        {
            ["instock"] = byStatus.Where(x => x.Key == SupplierItemStatus.InStock).Sum(x => x.Count),
            ["out"] = byStatus.Where(x => x.Key == SupplierItemStatus.OutOfStock).Sum(x => x.Count),
            ["notfound"] = byStatus.Where(x => x.Key == SupplierItemStatus.NotFound).Sum(x => x.Count),
            ["error"] = errors,
            ["unchecked"] = Math.Max(0, trackedCount - checkedCount),
        };
    }

    public async Task<SupplierProductStatusDto?> GetProductStatusAsync(Guid productId, CancellationToken cancellationToken = default)
    {
        var state = await context.SupplierProductStates.AsNoTracking().FirstOrDefaultAsync(s => s.ProductId == productId, cancellationToken);
        if (state is null)
            return null;
        var pendingCount = await context.SupplierChanges.AsNoTracking()
            .CountAsync(c => c.ProductId == productId && c.Status == SupplierChangeStatus.Pending, cancellationToken);
        return new SupplierProductStatusDto(state.Status, state.SourcePrice, state.SourceStock, state.SourceName, state.CheckedAt,
            state.LastSuccessAt, state.ConsecutiveErrors, state.LastError, state.AutoOutOfStock, pendingCount);
    }

    // ── تأیید و رد ──────────────────────────────────────────────────────

    public async Task<OperationResult> ApproveAsync(IReadOnlyList<SupplierApproval> approvals, bool adjustSalePrice, Guid userId, string userName)
    {
        using var stockReason = Fratak_Shop.Data.Utilities.StockContext.Begin(StockMovementReason.Supplier, "تأیید پیشنهاد تأمین‌کننده");
        if (approvals.Count == 0)
            return OperationResult.Error("هیچ موردی انتخاب نشده است.");
        if (approvals.Count > MaxBulk)
            return OperationResult.Error($"حداکثر {MaxBulk:N0} مورد در یک بار قابل تأیید است.");
        if (approvals.Any(a => a.Quantity is < 0 or > 100000))
            return OperationResult.Error("موجودی واردشده باید بین ۰ تا ۱۰۰٬۰۰۰ باشد.");

        var ids = approvals.Select(a => a.ChangeId).Distinct().ToList();
        var changes = await context.SupplierChanges.AsTracking()
            .Include(c => c.Product)
            .Where(c => ids.Contains(c.Id) && c.Status == SupplierChangeStatus.Pending)
            .ToListAsync();
        if (changes.Count == 0)
            return OperationResult.Error("موارد انتخاب‌شده دیگر منتظر تأیید نیستند (شاید کس دیگری رسیدگی کرده یا بررسی تازه‌تری جایگزینشان شده).");

        var productIds = changes.Select(c => c.ProductId).Distinct().ToList();
        var states = await context.SupplierProductStates.AsTracking()
            .Where(s => productIds.Contains(s.ProductId))
            .ToDictionaryAsync(s => s.ProductId);
        var quantities = approvals.GroupBy(a => a.ChangeId).ToDictionary(g => g.Key, g => g.Last().Quantity);
        var now = DateTimeOffset.UtcNow;
        var priceAdjusted = 0;

        // اگر برای یک محصول چند پیشنهاد موجودی انتخاب شده باشد، فقط تازه‌ترین اعمال می‌شود.
        var stockWinners = changes.Where(c => SupplierSyncRules.IsStockKind(c.Kind))
            .GroupBy(c => c.ProductId)
            .Select(g => g.OrderByDescending(c => c.UpdatedAt).First().Id)
            .ToHashSet();

        foreach (var change in changes)
        {
            var product = change.Product;
            if (SupplierSyncRules.IsStockKind(change.Kind))
            {
                if (!stockWinners.Contains(change.Id))
                {
                    change.Status = SupplierChangeStatus.Superseded;
                    change.DecidedAt = now;
                    change.DecidedByName = userName;
                    continue;
                }
                var quantity = quantities.GetValueOrDefault(change.Id) ?? (int)(change.NewValue ?? 0);
                product.StockQuantity = quantity;
                change.NewValue = quantity;
                if (quantity > 0 && states.TryGetValue(product.Id, out var state))
                    state.AutoOutOfStock = false;
            }
            else if (change.Kind == SupplierChangeKind.PriceChange && change.NewValue is > 0)
            {
                var newPurchase = change.NewValue.Value;
                var oldPurchase = product.PurchasePrice;
                product.PurchasePrice = newPurchase;
                if (adjustSalePrice && oldPurchase is > 0 && newPurchase != oldPurchase)
                {
                    // همان اختلاف ریالی به قیمت فروش و تخفیف اضافه می‌شود؛ مثال: خرید ۴۰۰→۴۱۵ هزار، فروش ۵۰۰→۵۱۵ هزار.
                    // تاریخچهٔ قیمت را DbContext خودکار ثبت می‌کند.
                    var delta = newPurchase - oldPurchase.Value;
                    product.Price = Math.Max(1, product.Price + delta);
                    if (product.DiscountPrice is { } discount)
                    {
                        var adjusted = discount + delta;
                        product.DiscountPrice = adjusted > 0 && adjusted < product.Price ? adjusted : null;
                    }
                    priceAdjusted++;
                }
            }
            // پیدا نشد / خطا: تأیید یعنی «دیدم»؛ تغییری روی محصول نمی‌دهد.

            change.Status = SupplierChangeStatus.Approved;
            change.DecidedAt = now;
            change.DecidedByUserId = userId;
            change.DecidedByName = userName;
        }

        await context.SaveChangesAsync();
        var message = $"{changes.Count:N0} مورد تأیید و اعمال شد.";
        if (priceAdjusted > 0)
            message += $" قیمت فروش {priceAdjusted:N0} محصول هم به همان اندازه تغییر کرد.";
        if (changes.Count < ids.Count)
            message += $" {ids.Count - changes.Count:N0} مورد دیگر منتظر تأیید نبود.";
        return OperationResult.Success(message);
    }

    public async Task<OperationResult> RejectAsync(IReadOnlyList<Guid> changeIds, Guid userId, string userName)
    {
        if (changeIds.Count == 0)
            return OperationResult.Error("هیچ موردی انتخاب نشده است.");
        if (changeIds.Count > MaxBulk)
            return OperationResult.Error($"حداکثر {MaxBulk:N0} مورد در یک بار قابل رد است.");
        var ids = changeIds.Distinct().ToList();
        var changes = await context.SupplierChanges.AsTracking()
            .Where(c => ids.Contains(c.Id) && c.Status == SupplierChangeStatus.Pending)
            .ToListAsync();
        if (changes.Count == 0)
            return OperationResult.Error("موارد انتخاب‌شده دیگر منتظر تأیید نیستند.");
        var now = DateTimeOffset.UtcNow;
        foreach (var change in changes)
        {
            change.Status = SupplierChangeStatus.Rejected;
            change.DecidedAt = now;
            change.DecidedByUserId = userId;
            change.DecidedByName = userName;
        }
        await context.SaveChangesAsync();
        return OperationResult.Success($"{changes.Count:N0} مورد رد شد؛ همین عدد دوباره پیشنهاد نمی‌شود.");
    }
}
