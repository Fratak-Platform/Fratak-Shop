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
    }

    /// <summary>
    /// راهنمای صفحه‌های پنل (<see cref="AdminHelpDefaults"/>).
    /// <para>
    /// برخلاف بقیه، این بخش در هر اجرا بررسی می‌شود: راهنمای صفحه‌های تازه ساخته می‌شود و متن پیش‌فرض‌هایی
    /// که مدیر ویرایش نکرده (<c>IsCustomized = false</c>) به آخرین نسخه به‌روز می‌شود.
    /// متنی که مدیر ویرایش یا حذف کرده دست نمی‌خورد.
    /// </para>
    /// </summary>
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
