using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Notifications;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Data.Seeders;

/// <summary>
/// داده‌های پیش‌فرض دور هفتم (پیام‌های آماده و ...).
/// <para>
/// هر بخش فقط وقتی اجرا می‌شود که جدولش کاملاً خالی باشد؛ پس ویرایش یا حذف این رکوردها از پنل
/// هرگز با اجرای دوبارهٔ برنامه برنمی‌گردد. محل صدا زدن: انتهای <see cref="EngagementDefaultsSeeder"/>.
/// </para>
/// </summary>
public static class Round7DefaultsSeeder
{
    public static async Task SeedAsync(FratakShopDbContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        await SeedReadyMessagesAsync(context, cancellationToken);
        await SeedHelpArticlesAsync(context, cancellationToken);
        await BackfillOrderTimelineAsync(context, cancellationToken);
        await SeedOrderTemplatesAsync(context, cancellationToken);
    }

    /// <summary>
    /// قالب پیام رویدادهای جدید سفارش (لینک پرداخت و پیام فروشگاه). فقط رویدادهایی که قالب ندارند اضافه می‌شوند؛
    /// پس اگر مدیر متن را در «قالب‌های پیام» ویرایش کرده باشد، دست نمی‌خورد.
    /// </summary>
    private static async Task SeedOrderTemplatesAsync(FratakShopDbContext context, CancellationToken cancellationToken)
    {
        var existing = await context.NotificationTemplates.IgnoreQueryFilters().Select(t => t.Event).ToListAsync(cancellationToken);
        var templates = new[]
        {
            new NotificationTemplate
            {
                Event = NotificationEvent.OrderPaymentLink,
                Title = "سفارش {OrderNumber} آمادهٔ پرداخت است",
                Body = "{Name} عزیز، سفارش شما به مبلغ {Amount} تومان در {SiteName} ثبت شد. برای نهایی شدن، تا {LinkExpiry} از طریق لینک پرداخت امن اقدام کنید.",
                LinkText = "پرداخت سفارش",
                SendSms = true,
                SmsText = "سفارش {OrderNumber} به مبلغ {Amount} تومان ثبت شد. پرداخت تا {LinkExpiry}: {PaymentLink}",
                SendPush = true,
            },
            new NotificationTemplate
            {
                Event = NotificationEvent.OrderMessage,
                Title = "پیام فروشگاه دربارهٔ سفارش {OrderNumber}",
                Body = "{MessageText}",
                LinkText = "مشاهده و پاسخ",
                SendSms = true,
                SmsText = "پیام جدید دربارهٔ سفارش {OrderNumber} در {SiteName}. لطفاً صفحهٔ سفارش را ببینید.",
                SendPush = true,
            },
        };

        var missing = templates.Where(t => !existing.Contains(t.Event)).ToList();
        if (missing.Count == 0)
            return;

        context.NotificationTemplates.AddRange(missing);
        await context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// پرکردن زمان «شروع آماده‌سازی» و «ارسال» برای سفارش‌هایی که قبل از اضافه شدن این ستون‌ها ثبت شده‌اند.
    /// <para>
    /// چرا: محاسبهٔ زمان‌بندی (SLA) به <c>ProcessingAt</c> و <c>ShippedAt</c> نیاز دارد. برای سفارش‌های قدیمی
    /// بهترین تخمین، آخرین زمان ویرایش سفارش (<c>UpdatedAt</c>) است؛ برای سفارش تحویل‌شده هم زمان تحویل.
    /// این کار فقط ردیف‌هایی را که خالی‌اند تغییر می‌دهد، پس اجرای دوباره در هر راه‌اندازی بی‌خطر و سریع است.
    /// </para>
    /// </summary>
    private static async Task BackfillOrderTimelineAsync(FratakShopDbContext context, CancellationToken cancellationToken)
    {
        await context.Orders
            .Where(o => o.ProcessingAt == null && o.Status == Entities.Orders.OrderStatus.Processing)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.ProcessingAt, o => (DateTimeOffset?)o.UpdatedAt), cancellationToken);

        await context.Orders
            .Where(o => o.ShippedAt == null && o.Status == Entities.Orders.OrderStatus.Shipped)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.ShippedAt, o => (DateTimeOffset?)o.UpdatedAt), cancellationToken);

        await context.Orders
            .Where(o => o.ShippedAt == null && o.Status == Entities.Orders.OrderStatus.Delivered)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.ShippedAt, o => (DateTimeOffset?)(o.DeliveredAt ?? o.UpdatedAt)), cancellationToken);
    }

    /// <summary>
    /// راهنمای صفحه‌های پنل (<see cref="AdminHelpDefaults"/>).
    /// <para>
    /// برخلاف بقیه، این بخش در هر اجرا بررسی می‌شود: راهنمای صفحه‌های تازه ساخته می‌شود و متن پیش‌فرض‌هایی
    /// که مدیر ویرایش نکرده (<c>IsCustomized = false</c>) به آخرین نسخه به‌روز می‌شود.
    /// متنی که مدیر ویرایش یا حذف کرده دست نمی‌خورد.
    /// </para>
    /// </summary>
    /// <summary>
    /// همگام‌سازی راهنمای پنل با نسخهٔ فعلی برنامه، در هر شروع برنامه (مرحلهٔ سیزدهم).
    /// <para>راهنمای پنل «مستندات نرم‌افزار» است، نه دادهٔ فروشگاه: با هر نسخهٔ تازه، صفحهٔ تازه‌ای در پنل می‌آید که راهنما می‌خواهد.
    /// ردیفی که مدیر ویرایش یا حذف کرده دست نمی‌خورد. محل صدا زدن: FratakShopBootstrapper (وقتی پایگاه داده قبلاً راه‌اندازی شده).</para>
    /// </summary>
    public static Task SyncHelpArticlesAsync(FratakShopDbContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        return SeedHelpArticlesAsync(context, cancellationToken);
    }

    private static async Task SeedHelpArticlesAsync(FratakShopDbContext context, CancellationToken cancellationToken)
    {
        // اگر برای یک آدرس هم ردیف حذف‌شده و هم ردیف فعال باشد، ردیف فعال ملاک است.
        var existing = (await context.AdminHelpArticles.IgnoreQueryFilters().AsTracking().ToListAsync(cancellationToken))
            .GroupBy(h => h.Path)
            .ToDictionary(g => g.Key, g => g.OrderBy(h => h.IsDeleted).First());

        var changed = false;
        foreach (var item in AdminHelpDefaults.All)
        {
            if (!existing.TryGetValue(item.Path, out var row))
            {
                context.AdminHelpArticles.Add(new Entities.Admin.AdminHelpArticle { Path = item.Path, Title = item.Title, Body = item.Body });
                changed = true;
            }
            else if (!row.IsCustomized && !row.IsDeleted && (row.Title != item.Title || row.Body != item.Body))
            {
                row.Title = item.Title;
                row.Body = item.Body;
                changed = true;
            }
        }

        if (changed)
            await context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// چند پیام آمادهٔ پرکاربرد (اعلان تخفیف، پاسخ تیکت، پیام سفارش) به‌عنوان نمونه.
    /// برچسب‌هایی مثل {FirstName} هنگام ارسال با اطلاعات همان مشتری پر می‌شوند.
    /// </summary>
    private static async Task SeedReadyMessagesAsync(FratakShopDbContext context, CancellationToken cancellationToken)
    {
        if (await context.ReadyMessages.IgnoreQueryFilters().AnyAsync(cancellationToken))
            return;

        context.ReadyMessages.AddRange(
            new ReadyMessage
            {
                Name = "اطلاع‌رسانی تخفیف",
                Title = "تخفیف ویژهٔ {SiteName}",
                Body = "{FirstName} عزیز، تخفیف ویژهٔ این هفته شروع شد. تا پایان هفته فرصت دارید از قیمت‌های ویژه استفاده کنید.",
                LinkUrl = "/shop",
                Scopes = ReadyMessageScope.Broadcast | ReadyMessageScope.Direct,
                SortOrder = 1,
            },
            new ReadyMessage
            {
                Name = "پاسخ: پیگیری ارسال",
                Body = "سلام {FirstName} عزیز، سفارش شما در حال پیگیری است. به محض ثبت کد رهگیری، از طریق پیامک و اعلان به شما اطلاع می‌دهیم. از صبر و همراهی شما سپاسگزاریم.",
                Scopes = ReadyMessageScope.Ticket | ReadyMessageScope.Order,
                SortOrder = 2,
            },
            new ReadyMessage
            {
                Name = "پیام سفارش: کالای ناموجود",
                Title = "دربارهٔ سفارش {OrderNumber}",
                Body = "{FirstName} عزیز، متأسفانه یکی از کالاهای سفارش {OrderNumber} در حال حاضر ناموجود شده است. آیا موافقید این کالا حذف شود و بقیهٔ سفارش ارسال شود؟ مبلغ کالای حذف‌شده به کیف پول شما برگشت داده می‌شود.",
                Scopes = ReadyMessageScope.Order | ReadyMessageScope.Ticket,
                SortOrder = 3,
            },
            new ReadyMessage
            {
                Name = "پاسخ دیدگاه: تشکر",
                Body = "از اینکه تجربهٔ خود را با ما و دیگر خریداران به اشتراک گذاشتید سپاسگزاریم.",
                Scopes = ReadyMessageScope.Comment,
                SortOrder = 4,
            });

        await context.SaveChangesAsync(cancellationToken);
    }
}
