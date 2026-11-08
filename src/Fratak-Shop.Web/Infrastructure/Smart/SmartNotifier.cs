using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Core.Services.Seo.Engine;
using Fratak_Shop.Core.Services.Smart;
using Fratak_Shop.Data.Entities.Admin;
using Fratak_Shop.Data.Entities.Smart;

namespace Fratak_Shop.Web.Infrastructure.Smart;

/// <summary>
/// اعلان نتیجهٔ کارهای هوشمند به ادمین (دور بیستم، بخش D).
/// <para>چرا؟ کارشناس دکمه را می‌زند و می‌رود؛ وقتی کار تمام شد (اعمال شد، منتظر تأیید است، چیزی پیدا نشد یا ناموفق شد)
/// باید در زنگولهٔ پنل (و پوش مرورگر) خبر بگیرد و با یک کلیک دقیقاً به همان بخش برسد.</para>
/// <list type="bullet">
/// <item><b>دستی</b> (اولویت Manual): برای هر کار یک اعلان به خود درخواست‌دهنده؛ لینک کالا با لنگر همان کار:
/// <c>/admin/smart/product/{id}#task-{taskId}</c>.</item>
/// <item><b>گروهی</b> (Batch): فقط یک اعلان خلاصه وقتی آخرین کار آن درخواست‌دهنده تمام شد («۲۰ کار: ۱۴ اعمال، ۵ منتظر تأیید …»)
/// با لینک <c>/admin/smart?Status=NeedsReview</c> (زنگوله با ۲۰ اعلان پر نمی‌شود).</item>
/// <item><b>شبانه</b> (Nightly): تکمیل خودکار کالاها ← حداکثر یک خلاصه در هر ۲۰ ساعت برای دارندگان مجوز مدیریت کالا؛
/// مقاله‌های تقویم ← برای هر مقاله یک اعلان به دارندگان مجوز انتشار وبلاگ.</item>
/// </list>
/// <para>نوع اعلان <see cref="StaffNotificationKind.SmartResult"/> است (از تنظیمات شخصی قابل بی‌صدا شدن).
/// هیچ استثنایی بیرون نمی‌رود: اعلان «بهترین تلاش» است و هرگز کار صف را خراب نمی‌کند.</para>
/// <para>ثبت: Scoped؛ محل استفاده: <see cref="SmartWorker"/> بعد از ثبت نتیجه یا شکست نهایی (۳ تلاش).</para>
/// </summary>
public sealed class SmartNotifier(ISmartTaskQueue queue, IStaffNotificationService notifications, ILogger<SmartNotifier> logger)
{
    /// <summary>کمترین فاصلهٔ دو خلاصهٔ شبانه (کارشناس هر روز حداکثر یک خلاصه می‌بیند).</summary>
    public static readonly TimeSpan NightlyEvery = TimeSpan.FromHours(20);

    private static long _lastNightlyTicks;

    /// <summary>
    /// اعلان نتیجهٔ یک کار. status = وضعیت نهایی (Applied/NeedsReview/Skipped/Failed)؛ summary = خلاصهٔ فارسی کار.
    /// مثال: کار دستی «تکمیل هدفون X» منتظر تأیید ← «نتیجهٔ تکمیل هوشمند: هدفون X – منتظر تأیید شما».
    /// </summary>
    public async Task NotifyAsync(ClaimedSmartTask task, SmartTaskStatus status, string? summary, CancellationToken cancellationToken)
    {
        try
        {
            if (task.Priority >= SmartPriority.Manual)
            {
                await SendAsync(task.RequestedById, TaskTitle(task, status), Cut(summary, 300), LinkFor(task), PermissionFor(task.TargetKind));
                return;
            }
            if (task.Priority >= SmartPriority.Batch)
            {
                if (await queue.PendingCountAsync(task.RequestedById, task.Priority, cancellationToken) > 0)
                    return;
                var batch = await queue.BatchSummaryAsync(task.RequestedById, task.Priority, task.CreatedAt, cancellationToken);
                if (batch.Total <= 1)
                {
                    await SendAsync(task.RequestedById, TaskTitle(task, status), Cut(summary, 300), LinkFor(task), PermissionFor(task.TargetKind));
                    return;
                }
                await SendAsync(task.RequestedById, "کارهای گروهی هوشمند تمام شد", BatchBody(batch), BatchLink(batch), PermissionFor(task.TargetKind));
                return;
            }

            // شبانه
            if (task.TargetKind is SmartTarget.BlogPlan or SmartTarget.BlogPost)
            {
                if (status is SmartTaskStatus.Applied or SmartTaskStatus.NeedsReview or SmartTaskStatus.Failed)
                    await SendAsync(null, TaskTitle(task, status), Cut(summary, 300), LinkFor(task), AdminPermissions.BlogPublish);
                return;
            }
            if (await queue.PendingCountAsync(null, SmartPriority.Nightly, cancellationToken) > 0)
                return;
            var now = DateTimeOffset.UtcNow;
            var last = Interlocked.Read(ref _lastNightlyTicks);
            if (last > 0 && now.UtcTicks - last < NightlyEvery.Ticks)
                return;
            var nightly = await queue.BatchSummaryAsync(null, SmartPriority.Nightly, task.CreatedAt, cancellationToken);
            if (nightly.Total == 0 || Interlocked.CompareExchange(ref _lastNightlyTicks, now.UtcTicks, last) != last)
                return;
            await SendAsync(null, "گزارش تکمیل خودکار شبانه", BatchBody(nightly), BatchLink(nightly), PermissionFor(task.TargetKind));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Smart result notification for task {Task} failed", task.Id);
        }
    }

    /// <summary>
    /// لینک مستقیم به بخش بازبینی همان کار (خالص؛ آزمون‌پذیر).
    /// مثال: کالا ← «/admin/smart/product/{id}#task-{taskId}»؛ دسته ← «/admin/smart/categories»؛ تقویم مقاله ← «/admin/smart/blog»؛
    /// مقاله ← «/admin/blog/edit/{id}»؛ بقیه ← «/admin/smart».
    /// </summary>
    public static string LinkFor(ClaimedSmartTask task) => task.TargetKind switch
    {
        SmartTarget.Product when task.TargetId is { } id => $"/admin/smart/product/{id}#task-{task.Id}",
        SmartTarget.Category => "/admin/smart/categories",
        SmartTarget.BlogPlan => "/admin/smart/blog",
        SmartTarget.BlogPost when task.TargetId is { } id => $"/admin/blog/edit/{id}",
        _ => "/admin/smart",
    };

    /// <summary>لینک خلاصهٔ گروهی: اگر چیزی منتظر تأیید است مستقیم فیلتر «منتظر تأیید»، وگرنه صف کامل.</summary>
    public static string BatchLink(SmartBatchSummary batch) =>
        batch.NeedsReview > 0 ? "/admin/smart?Status=NeedsReview" : batch.Failed > 0 ? "/admin/smart?Status=Failed" : "/admin/smart";

    /// <summary>
    /// متن خلاصهٔ گروهی. مثال: (۲۰، ۱۴، ۵، ۱، ۰) ← «۲۰ کار: ۱۴ اعمال شد، ۵ منتظر تأیید شما، ۱ ناموفق».
    /// </summary>
    public static string BatchBody(SmartBatchSummary batch)
    {
        var parts = new List<string>();
        if (batch.Applied > 0)
            parts.Add($"{PersianText.Digits(batch.Applied)} اعمال شد");
        if (batch.NeedsReview > 0)
            parts.Add($"{PersianText.Digits(batch.NeedsReview)} منتظر تأیید شما");
        if (batch.Skipped > 0)
            parts.Add($"{PersianText.Digits(batch.Skipped)} بدون تغییر");
        if (batch.Failed > 0)
            parts.Add($"{PersianText.Digits(batch.Failed)} ناموفق");
        return $"{PersianText.Digits(batch.Total)} کار: " + (parts.Count == 0 ? "انجام شد" : string.Join("، ", parts));
    }

    /// <summary>عنوان اعلان یک کار. مثال: «توضیحات محصول هوشمند: هدفون X – منتظر تأیید شما».</summary>
    public static string TaskTitle(ClaimedSmartTask task, SmartTaskStatus status)
    {
        var kind = task.Kind switch
        {
            SmartTaskKind.ProductImages => "عکس هوشمند",
            SmartTaskKind.ProductSpecs => "مشخصات هوشمند",
            SmartTaskKind.ProductInsight => "خلاصهٔ خرید",
            SmartTaskKind.ProductComplete => "تکمیل هوشمند",
            SmartTaskKind.CategoryComplete => "تکمیل دسته",
            SmartTaskKind.BlogArticle => "مقالهٔ هوشمند",
            SmartTaskKind.BlogImage => "تصویر مقاله",
            SmartTaskKind.ProductReview => "توضیحات محصول هوشمند",
            _ => "کار هوشمند",
        };
        var state = status switch
        {
            SmartTaskStatus.Applied => "اعمال شد",
            SmartTaskStatus.NeedsReview => "منتظر تأیید شما",
            SmartTaskStatus.Failed => "ناموفق",
            SmartTaskStatus.Skipped => "تغییری لازم نشد",
            _ => "تمام شد",
        };
        var title = string.IsNullOrWhiteSpace(task.TargetTitle) ? kind : $"{kind}: {task.TargetTitle!.Trim()}";
        return Cut($"{title} – {state}", 200)!;
    }

    /// <summary>مجوز گیرندگان وقتی درخواست‌دهندهٔ مشخصی نیست (مالک‌ها همیشه دریافت می‌کنند).</summary>
    public static string PermissionFor(SmartTarget target) => target switch
    {
        SmartTarget.Category => AdminPermissions.CatalogManage,
        SmartTarget.BlogPlan or SmartTarget.BlogPost => AdminPermissions.BlogPublish,
        _ => AdminPermissions.ProductsManage,
    };

    /// <summary>
    /// دور ۲۱: اعلان دستهٔ تازهٔ ساخته‌شده به دارندگان مجوز مدیریت دسته (فقط وقتی دسته‌ای ساخته شد؛ جابه‌جایی ساده اعلان نمی‌خواهد).
    /// مثال: «دسته‌بندی هوشمند: ۲ دستهٔ تازه» ← «۱۸ کالا دسته گرفت؛ ۲ دستهٔ تازه: پاوربانک، لیوان سرامیکی.» با لینک صفحهٔ دسته‌های هوشمند.
    /// </summary>
    public async Task NotifyCategoriesAsync(CategorizeReport report)
    {
        if (report.Created.Count == 0)
            return;
        try
        {
            await SendAsync(null, $"دسته‌بندی هوشمند: {PersianText.Digits(report.Created.Count)} دستهٔ تازه", Cut(report.Summary, 300),
                "/admin/smart/categories", AdminPermissions.CatalogManage);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Smart categorization notification failed");
        }
    }

    /// <summary>
    /// دور ۲۲ – ورود هوشمند کالا: دستهٔ تازه ← اعلان «دسته‌بندی هوشمند»؛ کالای تازه‌ای که هیچ دسته‌ای برایش پیدا نشد ← اعلان با لینک
    /// فهرست «کالاهای بدون دسته» (این کالاها تا دسته نگیرند منتشر نمی‌شوند). دورهای عادی بی‌صدا هستند تا زنگوله شلوغ نشود.
    /// </summary>
    public async Task NotifyIntakeAsync(IntakeReport report)
    {
        try
        {
            if (report.Created.Count > 0)
            {
                await SendAsync(null, $"دسته‌بندی هوشمند: {PersianText.Digits(report.Created.Count)} دستهٔ تازه",
                    Cut("دسته‌های تازه: " + string.Join("، ", report.Created.Select(c => c.Name)), 300), "/admin/smart/categories", AdminPermissions.CatalogManage);
            }
            if (report.Unplaced.Count > 0)
            {
                await SendAsync(null, $"{PersianText.Digits(report.Unplaced.Count)} کالای تازه دسته نگرفت",
                    "برای این کالاها دستهٔ مطمئنی پیدا نشد؛ یک دسته برایشان انتخاب کنید تا قابل انتشار شوند.", "/admin/products?missing=category",
                    AdminPermissions.CatalogManage);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Smart intake notification failed");
        }
    }

    /// <summary>
    /// دور ۲۱ – رشد خودکار: ایده‌های بنر تازه ← یک اعلان برای مدیران بازاریابی (لینک فهرست ایده‌ها)؛ روز کم‌بازدید ← یک اعلان خلاصه
    /// (روزهای عادی بی‌صدا تا زنگوله شلوغ نشود؛ گزارش کامل در صفحهٔ رشد خودکار هست).
    /// </summary>
    public async Task NotifyGrowthAsync(GrowthReport report, int bannerIdeas)
    {
        if (!report.Ran)
            return;
        try
        {
            if (bannerIdeas > 0)
            {
                await SendAsync(null, $"استودیوی طراحی: {PersianText.Digits(bannerIdeas)} ایدهٔ بنر تازه",
                    "پیش‌نمایش آماده است؛ با یک کلیک تأیید کنید تا روی سایت برود، ویرایش کنید یا رد کنید.", "/admin/design?status=Suggested",
                    AdminPermissions.MarketingManage);
            }
            if (report.Traffic.Low)
                await SendAsync(null, "رشد خودکار: بازدید کمتر از معمول بود", Cut(report.Summary, 300), "/admin/smart/growth", AdminPermissions.ContentManage);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Growth notification failed");
        }
    }

    private async Task SendAsync(Guid? userId, string title, string? body, string url, string permission)
    {
        if (userId is { } id)
            await notifications.NotifyUserAsync(id, StaffNotificationKind.SmartResult, title, body, url);
        else
            await notifications.NotifyAsync(new StaffAlert(StaffNotificationKind.SmartResult, title, body, url, [permission]));
    }

    private static string? Cut(string? text, int max) =>
        string.IsNullOrWhiteSpace(text) ? null : text.Length <= max ? text.Trim() : text[..(max - 1)].TrimEnd() + "…";
}
