using Fratak_Shop.Core.DTOs.Admin;
using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Core.Services.Settings;
using Fratak_Shop.Core.Services.Suppliers;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Admin;
using Fratak_Shop.Data.Entities.Orders;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Core.Services.Orders;

// ═══════════════════════════════════════════════════════════════════════════
//  سرویس رهگیری مرسوله (مرحلهٔ هفتم – فاز ۶)
//  جریان کار:
//   ۱) کارمند در جزئیات سفارش کد رهگیری و شرکت حمل را ثبت می‌کند → RegisterAsync.
//   ۲) سرویس پس‌زمینه هر چند دقیقه RunDueAsync را صدا می‌زند؛ هر مرسولهٔ پستی فعال در هر «نوبت» روز
//      (مثلاً ۴ بار: ساعت ۰، ۶، ۱۲، ۱۸) یک بار خوانده می‌شود.
//   ۳) رویدادهای تازه ذخیره می‌شوند؛ با «تحویل به گیرنده» سفارش خودکار «تحویل‌شده» می‌شود (اگر در تنظیمات فعال باشد)
//      و با «برگشت به فرستنده» یا طولانی شدن بیش از حد، به کارکنان هشدار داده می‌شود.
//   ۴) مشتری در جزئیات سفارش و صفحهٔ پیگیری سفارش، خط زمانی همین رویدادها را می‌بیند → GetTimelineAsync.
//  صفحه‌های استفاده: /admin/orders/{id}، /admin/orders/shipments، /profile/orders/{number}، /track-order.
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>یک رویداد برای نمایش در خط زمانی.</summary>
public sealed record ShipmentEventDto(DateTimeOffset? OccurredAt, string? RawTime, string Status, string? Location, PostEventKind Kind);

/// <summary>همهٔ اطلاعات رهگیری یک سفارش برای نمایش (پنل و مشتری).</summary>
public sealed record ShipmentTimelineDto(
    Guid OrderId,
    ShippingCarrier Carrier,
    string Code,
    ShipmentTrackingState State,
    string? LastStatus,
    string? LastLocation,
    DateTimeOffset? LastEventAt,
    DateTimeOffset? LastCheckedAt,
    DateTimeOffset? LastSuccessAt,
    DateTimeOffset? DeliveredAt,
    bool AutoDelivered,
    int FailureCount,
    string? LastError,
    IReadOnlyList<ShipmentEventDto> Events,
    IReadOnlyList<KeyValuePair<string, string>> Info)
{
    public string CarrierTitle => ShippingCarriers.Title(Carrier);
    public string StateTitle => ShippingCarriers.StateTitle(State);
    public bool AutoTracking => ShippingCarriers.SupportsAutoTracking(Carrier);
    public string? PublicUrl => ShippingCarriers.PublicTrackingUrl(Carrier, Code);
    public bool IsActive => AutoTracking && ShippingCarriers.IsActive(State);
}

/// <summary>یک ردیف فهرست «رهگیری مرسوله‌ها» در پنل.</summary>
public sealed record ShipmentRow(
    Guid OrderId,
    string OrderNumber,
    string CustomerName,
    OrderStatus OrderStatus,
    ShippingCarrier Carrier,
    string Code,
    ShipmentTrackingState State,
    string? LastStatus,
    string? LastLocation,
    DateTimeOffset? LastEventAt,
    DateTimeOffset? LastCheckedAt,
    int FailureCount,
    string? LastError,
    DateTimeOffset StartedAt,
    bool AutoDelivered);

/// <summary>زبانه‌های فهرست رهگیری.</summary>
public enum ShipmentTab
{
    /// <summary>در حال رهگیری.</summary>
    Active = 0,

    /// <summary>نیازمند توجه: برگشتی، خطای پشت سر هم، ثبت‌نشده بعد از ۲ روز، یا متوقف‌شده بدون تحویل.</summary>
    Problems = 1,

    /// <summary>تحویل‌شده.</summary>
    Delivered = 2,

    /// <summary>همه.</summary>
    All = 3,
}

public sealed record ShipmentQuery(ShipmentTab Tab = ShipmentTab.Active, ShippingCarrier? Carrier = null, string? Search = null, int Page = 1, int Take = 30);

/// <summary>آمار بالای صفحهٔ رهگیری.</summary>
public sealed record ShipmentStats(int Active, int OutForDelivery, int Problems, int DeliveredLast7Days, int AutoDeliveredLast7Days, DateTimeOffset? LastCheckAt, DateTimeOffset NextSlot, bool Enabled);

public interface IShipmentTrackingService
{
    /// <summary>
    /// ثبت یا تغییر رهگیری بعد از ذخیرهٔ کد در سفارش. با کد/شرکت تازه، تاریخچهٔ قبلی پاک و از نو شروع می‌شود.
    /// <paramref name="code"/> باید قبلاً با <see cref="ShippingCarriers.TryNormalizeCode"/> تمیز شده باشد.
    /// </summary>
    Task<OperationResult> RegisterAsync(Guid orderId, ShippingCarrier carrier, string code);

    Task<ShipmentTimelineDto?> GetTimelineAsync(Guid orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// بررسی همهٔ مرسوله‌هایی که نوبتشان رسیده (سرویس پس‌زمینه). خروجی: تعداد مرسوله‌های بررسی‌شده.
    /// <paramref name="force"/> (دکمهٔ «همین الان بررسی کن» صفحهٔ رهگیری): همهٔ مرسوله‌های فعالی که ۱۰ دقیقهٔ اخیر
    /// بررسی نشده‌اند، حتی اگر رهگیری خودکار در تنظیمات خاموش باشد.
    /// </summary>
    Task<int> RunDueAsync(CancellationToken cancellationToken, bool force = false);

    /// <summary>دکمهٔ «همین الان بررسی کن» در جزئیات سفارش (حداکثر هر ۲ دقیقه یک بار).</summary>
    Task<OperationResult> CheckNowAsync(Guid orderId, CancellationToken cancellationToken = default);

    /// <summary>توقف یا ادامهٔ رهگیری خودکار یک سفارش (مثلاً وقتی کارمند تلفنی پیگیری کرده).</summary>
    Task<OperationResult> SetPausedAsync(Guid orderId, bool paused);

    Task<PagedList<ShipmentRow>> ListAsync(ShipmentQuery query, CancellationToken cancellationToken = default);

    Task<ShipmentStats> GetStatsAsync(CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public class ShipmentTrackingService(
    FratakShopDbContext context,
    IStoreSettingService settings,
    IPostTrackingClient client,
    IAdminOrderService adminOrders,
    IStaffNotificationService staff) : IShipmentTrackingService
{
    /// <summary>فقط یک بررسی گروهی در هر لحظه.</summary>
    private static readonly SemaphoreSlim Gate = new(1, 1);

    /// <summary>سقف مرسوله در یک اجرا (بقیه در اجرای بعدی).</summary>
    private const int MaxPerRun = 300;

    /// <summary>فاصلهٔ دو درخواست به سامانهٔ پست تا ربات مزاحم شناخته نشویم.</summary>
    private static readonly TimeSpan RequestDelay = TimeSpan.FromMilliseconds(1500);

    /// <summary>اگر این تعداد درخواست اول یک اجرا همه شکست خوردند، سامانه در دسترس نیست و اجرا متوقف می‌شود.</summary>
    private const int AbortAfterFailures = 3;

    /// <summary>بعد از چند خطای پشت سر هم، مرسوله در زبانهٔ «نیازمند توجه» می‌آید.</summary>
    public const int ProblemFailureCount = 3;

    public const string SystemName = "سیستم – رهگیری پست";

    // ── ثبت ─────────────────────────────────────────────────────────────

    public async Task<OperationResult> RegisterAsync(Guid orderId, ShippingCarrier carrier, string code)
    {
        if (!Enum.IsDefined(carrier))
            return OperationResult.Error("شرکت حمل معتبر نیست.");
        if (!ShippingCarriers.TryNormalizeCode(carrier, code, out var normalized, out var error))
            return OperationResult.Error(error!);
        var orderStatus = await context.Orders.Where(o => o.Id == orderId).Select(o => (OrderStatus?)o.Status).FirstOrDefaultAsync();
        if (orderStatus is null)
            return OperationResult.NotFound("سفارش یافت نشد.");
        // فقط مرسولهٔ در راه رهگیری خودکار می‌شود (مثلاً اصلاح کد سفارش تحویل‌شده فقط ذخیره می‌شود).
        var autoTrack = ShippingCarriers.SupportsAutoTracking(carrier) && orderStatus == OrderStatus.Shipped;

        var now = DateTimeOffset.UtcNow;
        var tracking = await context.ShipmentTrackings.AsTracking().Include(t => t.Events).FirstOrDefaultAsync(t => t.OrderId == orderId);
        if (tracking is not null && tracking.Carrier == carrier && tracking.Code == normalized)
            return OperationResult.Success("رهگیری بدون تغییر ماند.");

        if (tracking is null)
        {
            tracking = new ShipmentTracking { OrderId = orderId, Code = normalized };
            context.ShipmentTrackings.Add(tracking);
        }
        else
        {
            // کد یا شرکت عوض شد: تاریخچهٔ کد قبلی دیگر معتبر نیست.
            // (حذف نرم؛ ایندکس یکتای اثر انگشت فقط ردیف‌های حذف‌نشده را می‌شمارد).
            context.ShipmentEvents.RemoveRange(tracking.Events);
        }

        tracking.Carrier = carrier;
        tracking.Code = normalized;
        tracking.StartedAt = now;
        tracking.State = autoTrack ? ShipmentTrackingState.Pending : ShipmentTrackingState.Stopped;
        tracking.LastStatus = null;
        tracking.LastLocation = null;
        tracking.LastEventAt = null;
        tracking.LastCheckedAt = null;
        tracking.LastSuccessAt = null;
        tracking.CheckCount = 0;
        tracking.FailureCount = 0;
        tracking.LastError = null;
        tracking.DeliveredAt = null;
        tracking.AutoDelivered = false;
        tracking.ParcelInfo = null;
        await context.SaveChangesAsync();

        return OperationResult.Success(autoTrack
            ? "رهگیری خودکار پست فعال شد؛ وضعیت تا چند دقیقهٔ دیگر خوانده می‌شود."
            : ShippingCarriers.SupportsAutoTracking(carrier)
                ? "کد رهگیری ذخیره شد (سفارش در وضعیت «ارسال‌شده» نیست، پس رهگیری خودکار نمی‌شود)."
                : $"کد رهگیری {ShippingCarriers.Title(carrier)} ثبت شد (این شرکت رهگیری خودکار ندارد).");
    }

    // ── نمایش ───────────────────────────────────────────────────────────

    public async Task<ShipmentTimelineDto?> GetTimelineAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        var t = await context.ShipmentTrackings.AsNoTracking()
            .Where(x => x.OrderId == orderId)
            .Select(x => new
            {
                x.OrderId, x.Carrier, x.Code, x.State, x.LastStatus, x.LastLocation, x.LastEventAt, x.LastCheckedAt,
                x.LastSuccessAt, x.DeliveredAt, x.AutoDelivered, x.FailureCount, x.LastError, x.ParcelInfo,
                Events = x.Events.Select(e => new { e.OccurredAt, e.RawTime, e.Status, e.Location, e.Sequence, e.CreatedAt }).ToList(),
            })
            .FirstOrDefaultAsync(cancellationToken);
        if (t is null)
            return null;

        // جدیدترین اول: ترتیب سامانه (Sequence) معتبرتر از زمان است چون همهٔ رویدادها ساعت ندارند.
        var events = t.Events
            .OrderByDescending(e => e.Sequence)
            .ThenByDescending(e => e.CreatedAt)
            .Select(e => new ShipmentEventDto(e.OccurredAt, e.RawTime, e.Status, e.Location, PostStatusClassifier.Classify(e.Status)))
            .ToList();
        return new ShipmentTimelineDto(t.OrderId, t.Carrier, t.Code, t.State, t.LastStatus, t.LastLocation, t.LastEventAt,
            t.LastCheckedAt, t.LastSuccessAt, t.DeliveredAt, t.AutoDelivered, t.FailureCount, t.LastError, events, ParseInfo(t.ParcelInfo));
    }

    // ── بررسی ───────────────────────────────────────────────────────────

    public async Task<int> RunDueAsync(CancellationToken cancellationToken, bool force = false)
    {
        var current = await settings.GetAsync(cancellationToken);
        if (!current.IsPostTrackingEnabled && !force)
            return 0;
        if (!await Gate.WaitAsync(0, cancellationToken))
            return 0;

        try
        {
            var now = DateTimeOffset.UtcNow;
            await StopFinishedAsync(cancellationToken);
            await StopStaleAsync(now, current.PostTrackingMaxDays, cancellationToken);

            // نوبت فعلی روز؛ هر مرسوله‌ای که از شروع این نوبت بررسی نشده، باید بررسی شود.
            var slot = force ? now.AddMinutes(-10) : SupplierSyncSchedule.LatestSlot(now, current.PostTrackingRunsPerDay);
            var ids = await DueQuery(slot)
                .OrderBy(t => t.LastCheckedAt.HasValue)
                .ThenBy(t => t.LastCheckedAt)
                .Select(t => t.Id)
                .Take(MaxPerRun)
                .ToListAsync(cancellationToken);

            var checkedCount = 0;
            var failuresInRow = 0;
            var anySuccess = false;
            foreach (var id in ids)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (checkedCount > 0)
                    await Task.Delay(RequestDelay, cancellationToken);

                var outcome = await CheckCoreAsync(id, current.PostTrackingAutoDeliver, cancellationToken);
                checkedCount++;
                if (outcome == PostTrackingOutcome.Failed)
                {
                    failuresInRow++;
                    // سامانهٔ پست در دسترس نیست (مثلاً سرور خارج از ایران است): بقیه را بی‌جهت امتحان نکن.
                    if (!anySuccess && failuresInRow >= AbortAfterFailures)
                        break;
                }
                else
                {
                    anySuccess = true;
                    failuresInRow = 0;
                }
            }
            return checkedCount;
        }
        finally
        {
            Gate.Release();
        }
    }

    public async Task<OperationResult> CheckNowAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        var tracking = await context.ShipmentTrackings.AsNoTracking()
            .Where(t => t.OrderId == orderId)
            .Select(t => new { t.Id, t.Carrier, t.LastCheckedAt })
            .FirstOrDefaultAsync(cancellationToken);
        if (tracking is null)
            return OperationResult.NotFound("برای این سفارش رهگیری ثبت نشده است.");
        if (!ShippingCarriers.SupportsAutoTracking(tracking.Carrier))
            return OperationResult.Error("این شرکت حمل رهگیری خودکار ندارد.");
        if (tracking.LastCheckedAt is { } last && DateTimeOffset.UtcNow - last < TimeSpan.FromMinutes(2))
            return OperationResult.Error("همین چند لحظه پیش بررسی شد؛ دو دقیقهٔ دیگر دوباره امتحان کنید.");
        if (!await Gate.WaitAsync(0, cancellationToken))
            return OperationResult.Error("بررسی گروهی مرسوله‌ها در جریان است؛ چند دقیقهٔ دیگر دوباره امتحان کنید.");

        try
        {
            var current = await settings.GetAsync(cancellationToken);
            var outcome = await CheckCoreAsync(tracking.Id, current.PostTrackingAutoDeliver, cancellationToken);
            return outcome switch
            {
                PostTrackingOutcome.Found => OperationResult.Success("وضعیت مرسوله از سامانهٔ پست به‌روز شد."),
                PostTrackingOutcome.NotFound => OperationResult.Success("سامانهٔ پست هنوز اطلاعاتی برای این کد ندارد."),
                _ => OperationResult.Error("خواندن از سامانهٔ پست ناموفق بود: "
                                           + (await context.ShipmentTrackings.Where(t => t.Id == tracking.Id).Select(t => t.LastError).FirstOrDefaultAsync(cancellationToken) ?? "خطای نامشخص")),
            };
        }
        finally
        {
            Gate.Release();
        }
    }

    public async Task<OperationResult> SetPausedAsync(Guid orderId, bool paused)
    {
        var tracking = await context.ShipmentTrackings.AsTracking().FirstOrDefaultAsync(t => t.OrderId == orderId);
        if (tracking is null)
            return OperationResult.NotFound("برای این سفارش رهگیری ثبت نشده است.");
        if (!ShippingCarriers.SupportsAutoTracking(tracking.Carrier))
            return OperationResult.Error("این شرکت حمل رهگیری خودکار ندارد.");

        if (paused)
        {
            if (!ShippingCarriers.IsActive(tracking.State))
                return OperationResult.Error("رهگیری این مرسوله فعال نیست.");
            tracking.State = ShipmentTrackingState.Stopped;
            await context.SaveChangesAsync();
            return OperationResult.Success("رهگیری خودکار این مرسوله متوقف شد.");
        }

        if (tracking.State != ShipmentTrackingState.Stopped)
            return OperationResult.Error("رهگیری این مرسوله متوقف نیست.");
        var status = await context.Orders.Where(o => o.Id == orderId).Select(o => o.Status).FirstAsync();
        if (status != OrderStatus.Shipped)
            return OperationResult.Error("فقط سفارش «ارسال‌شده» رهگیری می‌شود.");
        tracking.State = tracking.LastStatus is null ? ShipmentTrackingState.Pending : ShipmentTrackingState.InTransit;
        tracking.StartedAt = DateTimeOffset.UtcNow; // مهلت «حداکثر روز» از نو شمرده شود.
        tracking.FailureCount = 0;
        tracking.LastCheckedAt = null;              // در اولین نوبت سرویس پس‌زمینه بررسی شود.
        await context.SaveChangesAsync();
        return OperationResult.Success("رهگیری خودکار دوباره فعال شد.");
    }

    /// <summary>مرسوله‌های پستی فعال که سفارششان «ارسال‌شده» است و از شروع نوبت فعلی بررسی نشده‌اند.</summary>
    private IQueryable<ShipmentTracking> DueQuery(DateTimeOffset slot) =>
        context.ShipmentTrackings.AsNoTracking().Where(t =>
            t.Carrier == ShippingCarrier.IranPost
            && (t.State == ShipmentTrackingState.Pending || t.State == ShipmentTrackingState.InTransit
                || t.State == ShipmentTrackingState.OutForDelivery || t.State == ShipmentTrackingState.NotFound)
            && t.Order.Status == OrderStatus.Shipped
            && (t.LastCheckedAt == null || t.LastCheckedAt < slot));

    /// <summary>
    /// سفارش‌هایی که دیگر «ارسال‌شده» نیستند (کارمند دستی تحویل/لغو/مرجوع کرد) رهگیری لازم ندارند.
    /// </summary>
    private async Task StopFinishedAsync(CancellationToken cancellationToken)
    {
        await context.ShipmentTrackings
            .Where(t => (t.State == ShipmentTrackingState.Pending || t.State == ShipmentTrackingState.InTransit
                         || t.State == ShipmentTrackingState.OutForDelivery || t.State == ShipmentTrackingState.NotFound)
                        && t.Order.Status != OrderStatus.Shipped
                        && t.Order.Status != OrderStatus.Confirmed && t.Order.Status != OrderStatus.Processing)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.State, ShipmentTrackingState.Stopped), cancellationToken);
    }

    /// <summary>
    /// مرسوله‌هایی که بیش از «حداکثر روزهای رهگیری» در راه مانده‌اند متوقف و یک هشدار جمعی برای کارکنان ارسال می‌شود.
    /// </summary>
    private async Task StopStaleAsync(DateTimeOffset now, int maxDays, CancellationToken cancellationToken)
    {
        var limit = now.AddDays(-Math.Clamp(maxDays, 3, 120));
        var stale = await context.ShipmentTrackings.AsNoTracking()
            .Where(t => t.Carrier == ShippingCarrier.IranPost
                        && (t.State == ShipmentTrackingState.Pending || t.State == ShipmentTrackingState.InTransit
                            || t.State == ShipmentTrackingState.OutForDelivery || t.State == ShipmentTrackingState.NotFound)
                        && t.StartedAt < limit)
            .Select(t => new { t.Id, t.Order.OrderNumber })
            .Take(200)
            .ToListAsync(cancellationToken);
        if (stale.Count == 0)
            return;

        var ids = stale.Select(s => s.Id).ToList();
        await context.ShipmentTrackings.Where(t => ids.Contains(t.Id))
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.State, ShipmentTrackingState.Stopped)
                .SetProperty(t => t.LastError, $"بیش از {maxDays} روز بدون تحویل؛ رهگیری خودکار متوقف شد."), cancellationToken);

        var numbers = string.Join("، ", stale.Take(5).Select(s => s.OrderNumber)) + (stale.Count > 5 ? " و …" : "");
        await NotifyAsync($"{stale.Count:N0} مرسوله بیش از {maxDays} روز تحویل نشده",
            $"رهگیری خودکار این سفارش‌ها متوقف شد؛ با پست یا مشتری پیگیری کنید: {numbers}",
            "/admin/orders/shipments?tab=problems");
    }

    /// <summary>
    /// خواندن یک مرسوله از سامانهٔ پست و اعمال نتیجه. خطاهای شبکه در خود رهگیری ثبت می‌شوند و بیرون پرتاب نمی‌شوند.
    /// </summary>
    private async Task<PostTrackingOutcome> CheckCoreAsync(Guid trackingId, bool autoDeliver, CancellationToken cancellationToken)
    {
        var tracking = await context.ShipmentTrackings.AsTracking()
            .Include(t => t.Events)
            .FirstOrDefaultAsync(t => t.Id == trackingId, cancellationToken);
        if (tracking is null)
            return PostTrackingOutcome.NotFound;

        PostTrackingResponse response;
        try
        {
            response = await client.TrackAsync(tracking.Code, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            response = PostTrackingResponse.Fail(ex.Message);
        }

        var now = DateTimeOffset.UtcNow;
        var previousState = tracking.State;
        tracking.LastCheckedAt = now;
        tracking.CheckCount++;

        switch (response.Outcome)
        {
            case PostTrackingOutcome.Failed:
                tracking.FailureCount++;
                tracking.LastError = Cut(response.Error ?? "خطای نامشخص", 500);
                await context.SaveChangesAsync(cancellationToken);
                return PostTrackingOutcome.Failed;

            case PostTrackingOutcome.NotFound:
                tracking.FailureCount = 0;
                tracking.LastError = null;
                tracking.LastSuccessAt = now;
                if (tracking.Events.Count == 0)
                    tracking.State = ShipmentTrackingState.NotFound;
                await context.SaveChangesAsync(cancellationToken);
                return PostTrackingOutcome.NotFound;
        }

        tracking.FailureCount = 0;
        tracking.LastError = null;
        tracking.LastSuccessAt = now;
        MergeEvents(tracking, response.Events);
        if (response.Info.Count > 0)
            tracking.ParcelInfo = Cut(string.Join("\n", response.Info.Select(i => $"{i.Key}: {i.Value}")), 2000);

        var kinds = response.Events.Select(e => e.Kind).ToList();
        if (response.Events.Count > 0)
        {
            var last = response.Events[^1];
            tracking.LastStatus = last.Status;
            tracking.LastLocation = last.Location;
            tracking.LastEventAt = last.OccurredAt;
            tracking.State = PostStatusClassifier.Overall(kinds);
        }
        else if (tracking.Events.Count == 0)
        {
            tracking.State = ShipmentTrackingState.NotFound;
        }

        if (tracking.State == ShipmentTrackingState.Delivered)
        {
            var delivered = response.Events.LastOrDefault(e => e.Kind == PostEventKind.Delivered);
            tracking.DeliveredAt ??= delivered?.OccurredAt is { } at && at <= now ? at : now;
        }
        await context.SaveChangesAsync(cancellationToken);

        if (tracking.State == ShipmentTrackingState.Delivered && previousState != ShipmentTrackingState.Delivered)
        {
            // تحویل خودکار روشن: سفارش «تحویل‌شده» می‌شود. خاموش (یا اگر تغییر وضعیت ممکن نشد): فقط به کارکنان خبر می‌دهیم تا دستی تأیید کنند.
            var done = autoDeliver && await AutoDeliverAsync(tracking, cancellationToken);
            if (!done)
                await NotifyDeliveredAsync(tracking.OrderId, cancellationToken);
        }
        if (tracking.State == ShipmentTrackingState.Returned && previousState != ShipmentTrackingState.Returned)
            await NotifyReturnedAsync(tracking.OrderId, tracking.LastStatus, cancellationToken);

        return PostTrackingOutcome.Found;
    }

    /// <summary>
    /// افزودن رویدادهای تازه با اثر انگشت (رویداد تکراری دوباره ذخیره نمی‌شود؛ ترتیب رویدادهای قبلی به‌روز می‌شود).
    /// </summary>
    internal void MergeEvents(ShipmentTracking tracking, IReadOnlyList<PostTrackingEvent> events)
    {
        // رویدادهای حذف‌شدهٔ کد قبلی (اگر در همین درخواست کد عوض شده باشد) هنوز در حافظه‌اند و نباید شمرده شوند.
        var existing = tracking.Events.Where(e => !e.IsDeleted)
            .GroupBy(e => e.Fingerprint)
            .ToDictionary(g => g.Key, g => g.First());
        foreach (var item in events)
        {
            var fingerprint = item.Fingerprint;
            if (existing.TryGetValue(fingerprint, out var known))
            {
                known.Sequence = item.Sequence;
                continue;
            }
            var entity = new ShipmentEvent
            {
                ShipmentTrackingId = tracking.Id,
                OccurredAt = item.OccurredAt,
                RawTime = item.RawTime,
                Status = item.Status,
                Location = item.Location,
                Sequence = item.Sequence,
                Fingerprint = fingerprint,
            };
            tracking.Events.Add(entity);
            existing[fingerprint] = entity;
        }
    }

    /// <summary>
    /// «تحویل‌شده» کردن خودکار سفارش از مسیر عادی (اعلان تحویل به مشتری، یادداشت در خط زمانی سفارش).
    /// زمان تحویل سفارش هم زمان واقعی پست می‌شود تا مهلت مرجوعی درست شمرده شود.
    /// </summary>
    /// <returns>true اگر سفارش واقعاً «تحویل‌شده» شد (یا از قبل بود)؛ false یعنی باید کارکنان دستی بررسی کنند.</returns>
    private async Task<bool> AutoDeliverAsync(ShipmentTracking tracking, CancellationToken cancellationToken)
    {
        var order = await context.Orders.AsNoTracking().Where(o => o.Id == tracking.OrderId)
            .Select(o => new { o.Status, o.ShippedAt }).FirstOrDefaultAsync(cancellationToken);
        if (order is null)
            return true;
        if (order.Status != OrderStatus.Shipped)
            return order.Status == OrderStatus.Delivered;

        var result = await adminOrders.ChangeStatusAsync(tracking.OrderId, OrderStatus.Delivered,
            "تأیید خودکار: سامانهٔ رهگیری پست «تحویل به گیرنده» را ثبت کرده است.", null, SystemName);
        if (!result.IsSuccess())
            return false;

        tracking.AutoDelivered = true;
        await context.SaveChangesAsync(cancellationToken);

        if (tracking.DeliveredAt is { } postTime && (order.ShippedAt is null || postTime >= order.ShippedAt))
            await context.Orders.Where(o => o.Id == tracking.OrderId && o.DeliveredAt > postTime)
                .ExecuteUpdateAsync(s => s.SetProperty(o => o.DeliveredAt, (DateTimeOffset?)postTime), cancellationToken);
        return true;
    }

    /// <summary>
    /// اعلان «پست تحویل را ثبت کرد» برای کارکنان سفارش وقتی تحویل خودکار خاموش است یا انجام نشد.
    /// </summary>
    private async Task NotifyDeliveredAsync(Guid orderId, CancellationToken cancellationToken)
    {
        var number = await context.Orders.Where(o => o.Id == orderId).Select(o => o.OrderNumber).FirstOrDefaultAsync(cancellationToken);
        await NotifyAsync($"مرسولهٔ سفارش {number} تحویل شد – تأیید کنید",
            "سامانهٔ پست «تحویل به گیرنده» را ثبت کرده است؛ وضعیت سفارش را بررسی و «تحویل‌شده» کنید.", $"/admin/orders/{orderId}#shipment");
    }

    private async Task NotifyReturnedAsync(Guid orderId, string? status, CancellationToken cancellationToken)
    {
        var number = await context.Orders.Where(o => o.Id == orderId).Select(o => o.OrderNumber).FirstOrDefaultAsync(cancellationToken);
        await NotifyAsync($"برگشت مرسولهٔ سفارش {number}", status ?? "سامانهٔ پست برگشت مرسوله به فرستنده را ثبت کرد.", $"/admin/orders/{orderId}#shipment");
    }

    private async Task NotifyAsync(string title, string body, string url)
    {
        try
        {
            await staff.NotifyAsync(new StaffAlert(StaffNotificationKind.ShipmentIssue, title, body, url, [AdminPermissions.OrdersManage]));
        }
        catch
        {
            // اعلان ناموفق نباید نتیجهٔ رهگیری را از بین ببرد.
        }
    }

    // ── فهرست و آمار ────────────────────────────────────────────────────

    public async Task<PagedList<ShipmentRow>> ListAsync(ShipmentQuery query, CancellationToken cancellationToken = default)
    {
        var take = Math.Clamp(query.Take, 10, 100);
        var page = Math.Max(1, query.Page);
        var source = Filter(context.ShipmentTrackings.AsNoTracking(), query.Tab, DateTimeOffset.UtcNow);
        if (query.Carrier is { } carrier)
            source = source.Where(t => t.Carrier == carrier);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = SearchText.NormalizeDigits(query.Search.Trim());
            if (term.Length > 100)
                term = term[..100];
            source = source.Where(t => t.Code.Contains(term) || t.Order.OrderNumber.Contains(term) || t.Order.CustomerName.Contains(term));
        }

        var total = await source.CountAsync(cancellationToken);
        var ordered = query.Tab == ShipmentTab.Delivered
            ? source.OrderByDescending(t => t.DeliveredAt)
            : source.OrderByDescending(t => t.StartedAt);
        var items = await ordered
            .Skip((page - 1) * take)
            .Take(take)
            .Select(t => new ShipmentRow(t.OrderId, t.Order.OrderNumber, t.Order.CustomerName, t.Order.Status, t.Carrier, t.Code, t.State,
                t.LastStatus, t.LastLocation, t.LastEventAt, t.LastCheckedAt, t.FailureCount, t.LastError, t.StartedAt, t.AutoDelivered))
            .ToListAsync(cancellationToken);
        return new PagedList<ShipmentRow>(items, total, page, take);
    }

    public async Task<ShipmentStats> GetStatsAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var current = await settings.GetAsync(cancellationToken);
        var all = context.ShipmentTrackings.AsNoTracking();
        var weekAgo = now.AddDays(-7);
        var active = await Filter(all, ShipmentTab.Active, now).CountAsync(cancellationToken);
        var outForDelivery = await all.CountAsync(t => t.State == ShipmentTrackingState.OutForDelivery, cancellationToken);
        var problems = await Filter(all, ShipmentTab.Problems, now).CountAsync(cancellationToken);
        var delivered = await all.CountAsync(t => t.State == ShipmentTrackingState.Delivered && t.DeliveredAt >= weekAgo, cancellationToken);
        var auto = await all.CountAsync(t => t.AutoDelivered && t.DeliveredAt >= weekAgo, cancellationToken);
        var lastCheck = await all.MaxAsync(t => t.LastCheckedAt, cancellationToken);
        return new ShipmentStats(active, outForDelivery, problems, delivered, auto, lastCheck,
            SupplierSyncSchedule.NextSlot(now, current.PostTrackingRunsPerDay), current.IsPostTrackingEnabled);
    }

    /// <summary>
    /// شرط هر زبانه. «نیازمند توجه»: برگشتی، ۳ خطای پشت سر هم، ۲ روز بعد از ثبت هنوز در سامانه نیست،
    /// یا متوقف‌شده‌ای که سفارشش هنوز «ارسال‌شده» است (یعنی تحویل تأیید نشده).
    /// </summary>
    private static IQueryable<ShipmentTracking> Filter(IQueryable<ShipmentTracking> source, ShipmentTab tab, DateTimeOffset now)
    {
        var twoDaysAgo = now.AddDays(-2);
        return tab switch
        {
            ShipmentTab.Active => source.Where(t => t.Carrier == ShippingCarrier.IranPost
                && (t.State == ShipmentTrackingState.Pending || t.State == ShipmentTrackingState.InTransit
                    || t.State == ShipmentTrackingState.OutForDelivery || t.State == ShipmentTrackingState.NotFound)),
            ShipmentTab.Problems => source.Where(t => t.State == ShipmentTrackingState.Returned
                || (t.FailureCount >= ProblemFailureCount && t.State != ShipmentTrackingState.Delivered && t.State != ShipmentTrackingState.Stopped)
                || (t.State == ShipmentTrackingState.NotFound && t.StartedAt < twoDaysAgo)
                || (t.State == ShipmentTrackingState.Stopped && t.Carrier == ShippingCarrier.IranPost && t.Order.Status == OrderStatus.Shipped)),
            ShipmentTab.Delivered => source.Where(t => t.State == ShipmentTrackingState.Delivered),
            _ => source,
        };
    }

    /// <summary>«عنوان: مقدار» در هر خط → فهرست جفت‌ها.</summary>
    internal static IReadOnlyList<KeyValuePair<string, string>> ParseInfo(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [];
        var list = new List<KeyValuePair<string, string>>();
        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var index = line.IndexOf(':');
            if (index > 0 && index < line.Length - 1)
                list.Add(new(line[..index].Trim(), line[(index + 1)..].Trim()));
        }
        return list;
    }

    private static string? Cut(string? text, int max) =>
        text is null ? null : text.Length <= max ? text : text[..max];
}
