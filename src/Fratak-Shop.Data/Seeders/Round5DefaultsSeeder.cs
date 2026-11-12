using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Loyalty;
using Fratak_Shop.Data.Entities.Notifications;
using Fratak_Shop.Data.Entities.Orders;
using Fratak_Shop.Data.Entities.Searches;
using Fratak_Shop.Data.Entities.Support;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Data.Seeders;

/// <summary>
/// Defaults of the round-5 features (returns, tickets, smart search, gift cards, alerts …).
/// Every block only adds what is missing – existing installations get the new rows once and
/// later edits from the admin panel are never overwritten. Everything here can be edited or
/// removed from the database.
/// </summary>
public static class Round5DefaultsSeeder
{
    public static async Task SeedAsync(FratakShopDbContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        await SeedTextsAsync(context, cancellationToken);
        await SeedNotificationTemplatesAsync(context, cancellationToken);
        await SeedLoyaltyRuleAsync(context, cancellationToken);
        await SeedReturnReasonsAsync(context, cancellationToken);
        await SeedSupportDepartmentsAsync(context, cancellationToken);
        await SeedSearchAsync(context, cancellationToken);
    }

    private static async Task SeedTextsAsync(FratakShopDbContext context, CancellationToken cancellationToken)
    {
        var settings = await context.StoreSettings.AsTracking().OrderBy(s => s.CreatedAt).FirstOrDefaultAsync(cancellationToken);
        if (settings is null)
            return;

        var changed = false;
        if (settings.GiftCardTerms is null)
        {
            settings.GiftCardTerms = "کارت هدیه پس از پرداخت فعال می‌شود و کد آن فقط یک بار قابل استفاده است. با وارد کردن کد، مبلغ کارت به کیف پول گیرنده اضافه می‌شود و برای خرید از فروشگاه قابل استفاده است (این اعتبار قابل برداشت به حساب بانکی نیست). کد را مانند پول نقد نگه دارید؛ هر کسی که کد را داشته باشد می‌تواند از آن استفاده کند. مهلت استفاده از تاریخ خرید یک سال است.";
            changed = true;
        }

        if (settings.ReferralShareText is null)
        {
            settings.ReferralShareText = "با لینک من در {SiteName} عضو شو و هدیه ثبت‌نام بگیر: {Link} (کد معرف: {Code})";
            changed = true;
        }

        if (settings.ReturnPolicyText is null)
        {
            settings.ReturnPolicyText = "کالا تا ۷ روز پس از تحویل قابل مرجوع است، به شرط سالم بودن کالا، بسته‌بندی و لوازم همراه. برای کالای معیوب یا مغایر، تصویر کالا را ضمیمه کنید. پس از دریافت و بررسی کالا، مبلغ به کیف پول شما برگشت داده می‌شود.";
            changed = true;
        }

        if (changed)
            await context.SaveChangesAsync(cancellationToken);
    }

    private static async Task SeedNotificationTemplatesAsync(FratakShopDbContext context, CancellationToken cancellationToken)
    {
        var existing = await context.NotificationTemplates.IgnoreQueryFilters().Select(t => t.Event).ToListAsync(cancellationToken);
        var templates = new[]
        {
            Template(NotificationEvent.BackInStock, "«{ProductName}» موجود شد", "کالایی که منتظرش بودید دوباره موجود شد. قیمت فعلی: {Price} تومان. تا تمام نشده سفارش دهید.", "مشاهده کالا", push: true),
            Template(NotificationEvent.PriceDrop, "قیمت «{ProductName}» کاهش یافت", "قیمت از {OldPrice} به {Price} تومان رسید ({Saving} تومان ارزان‌تر).", "مشاهده کالا", push: true),
            Template(NotificationEvent.GiftCardPurchased, "کارت هدیه {Amount} تومانی آماده است", "کارت هدیه شما (کد پیگیری {Reference}) فعال شد. کد کارت را از صفحه کارت‌های هدیه ببینید و برای دوستتان بفرستید.", "کارت‌های هدیه"),
            Template(NotificationEvent.GiftCardRedeemed, "کارت هدیه استفاده شد", "مبلغ {Amount} تومان از کارت هدیه به کیف پول اضافه شد.", "مشاهده کیف پول"),
            Template(NotificationEvent.NewDeviceLogin, "ورود از دستگاه جدید", "ورود به حساب شما از «{Device}» با آی‌پی {Ip} در {Time} انجام شد. اگر شما نبودید، فوراً رمز عبور را تغییر دهید و دستگاه را از بخش «دستگاه‌های فعال» خارج کنید.", "دستگاه‌های فعال", push: true),
            Template(NotificationEvent.ReferralJoined, "{FriendName} با لینک شما عضو شد", "دوست شما با کد معرف شما ثبت‌نام کرد. با اولین خرید او، امتیاز معرفی به حساب شما اضافه می‌شود.", "دعوت از دوستان"),
            Template(NotificationEvent.ReturnRequested, "درخواست مرجوعی {RequestNumber} ثبت شد", "درخواست مرجوعی سفارش {OrderNumber} ثبت شد و پس از بررسی نتیجه اعلام می‌شود.", "مشاهده درخواست"),
            Template(NotificationEvent.ReturnApproved, "درخواست مرجوعی {RequestNumber} تأیید شد", "لطفاً کالا را با بسته‌بندی کامل ارسال کنید و کد رهگیری مرسوله را در صفحه درخواست وارد کنید. {Note}", "مشاهده درخواست", push: true),
            Template(NotificationEvent.ReturnRejected, "درخواست مرجوعی {RequestNumber} رد شد", "درخواست مرجوعی سفارش {OrderNumber} رد شد. {Note}", "مشاهده درخواست"),
            Template(NotificationEvent.ReturnRefunded, "مبلغ مرجوعی {RequestNumber} برگشت داده شد", "مبلغ {Amount} تومان بابت مرجوعی سفارش {OrderNumber} به کیف پول شما برگشت.", "مشاهده کیف پول", push: true),
            Template(NotificationEvent.OrderFeedbackRequest, "از خرید {OrderNumber} راضی بودید؟", "با ثبت امتیاز تجربه خرید به ما کمک کنید بهتر شویم{Points}.", "ثبت امتیاز"),
            Template(NotificationEvent.TicketCreated, "تیکت {TicketNumber} ثبت شد", "درخواست «{Subject}» دریافت شد و کارشناسان ما به‌زودی پاسخ می‌دهند.", "مشاهده تیکت", push: false),
            Template(NotificationEvent.TicketReplied, "پاسخ جدید برای تیکت {TicketNumber}", "به درخواست «{Subject}» پاسخ داده شد.", "مشاهده پاسخ", push: true),
            // دور ۲۵: پاسخ به پرسش کالا (اعلان سایت + پوش؛ پیامک خاموش).
            Template(NotificationEvent.QuestionAnswered, "به پرسش شما دربارهٔ «{ProductName}» پاسخ داده شد", "پرسش «{Question}» پاسخ گرفت: {Answer}", "دیدن پاسخ", push: true),
        };

        var missing = templates.Where(t => !existing.Contains(t.Event)).ToList();
        if (missing.Count == 0)
            return;

        context.NotificationTemplates.AddRange(missing);
        await context.SaveChangesAsync(cancellationToken);
    }

    private static async Task SeedLoyaltyRuleAsync(FratakShopDbContext context, CancellationToken cancellationToken)
    {
        if (await context.LoyaltyRules.IgnoreQueryFilters().AnyAsync(r => r.Action == LoyaltyAction.OrderFeedback, cancellationToken))
            return;

        context.LoyaltyRules.Add(new LoyaltyRule
        {
            Action = LoyaltyAction.OrderFeedback,
            Title = "امتیاز تجربه خرید",
            Description = "ثبت امتیاز تجربه خرید پس از تحویل هر سفارش",
            Points = 30,
            DisplayOrder = 9,
        });
        await context.SaveChangesAsync(cancellationToken);
    }

    private static async Task SeedReturnReasonsAsync(FratakShopDbContext context, CancellationToken cancellationToken)
    {
        if (await context.ReturnReasons.IgnoreQueryFilters().AnyAsync(cancellationToken))
            return;

        context.ReturnReasons.AddRange(
            new ReturnReason { Title = "کالا معیوب یا خراب است", Hint = "تصویر ایراد کالا را ضمیمه کنید.", RequiresImage = true, DisplayOrder = 1 },
            new ReturnReason { Title = "کالای ارسالی با سفارش مغایرت دارد", Hint = "تصویر کالا و برچسب آن را ضمیمه کنید.", RequiresImage = true, DisplayOrder = 2 },
            new ReturnReason { Title = "کالا در حمل آسیب دیده است", Hint = "تصویر کالا و بسته‌بندی را ضمیمه کنید.", RequiresImage = true, DisplayOrder = 3 },
            new ReturnReason { Title = "کسری قطعات یا لوازم همراه", DisplayOrder = 4 },
            new ReturnReason { Title = "از خرید منصرف شده‌ام", Hint = "کالا باید سالم و در بسته‌بندی اصلی باشد.", DisplayOrder = 5 });
        await context.SaveChangesAsync(cancellationToken);
    }

    private static async Task SeedSupportDepartmentsAsync(FratakShopDbContext context, CancellationToken cancellationToken)
    {
        if (await context.SupportDepartments.IgnoreQueryFilters().AnyAsync(cancellationToken))
            return;

        context.SupportDepartments.AddRange(
            new SupportDepartment { Title = "پیگیری سفارش", Description = "وضعیت ارسال، تأخیر، تغییر آدرس", DisplayOrder = 1 },
            new SupportDepartment { Title = "مالی و پرداخت", Description = "پرداخت، کیف پول، بازگشت وجه، کارت هدیه", DisplayOrder = 2 },
            new SupportDepartment { Title = "مرجوعی و گارانتی", Description = "خرابی کالا، گارانتی، مرجوعی", DisplayOrder = 3 },
            new SupportDepartment { Title = "مشاوره خرید", Description = "انتخاب کالا و پرسش‌های فنی", DisplayOrder = 4 },
            new SupportDepartment { Title = "حساب کاربری و سایر موارد", DisplayOrder = 5 });
        await context.SaveChangesAsync(cancellationToken);
    }

    private static async Task SeedSearchAsync(FratakShopDbContext context, CancellationToken cancellationToken)
    {
        if (!await context.SearchSynonyms.IgnoreQueryFilters().AnyAsync(cancellationToken))
        {
            context.SearchSynonyms.AddRange(
                new SearchSynonym { Terms = "هدفون, هدست, headphone, headset" },
                new SearchSynonym { Terms = "هندزفری, ایرباد, ایرفون, earbuds, airpods, ایرپاد, هنذفری, handsfree" },
                new SearchSynonym { Terms = "لپ تاپ, لپتاپ, نوت بوک, laptop, notebook" },
                new SearchSynonym { Terms = "گوشی, موبایل, تلفن همراه, mobile, phone" },
                new SearchSynonym { Terms = "ساعت هوشمند, اسمارت واچ, smartwatch, watch" },
                new SearchSynonym { Terms = "پاوربانک, شارژر همراه, power bank, powerbank" },
                new SearchSynonym { Terms = "اسپیکر, بلندگو, speaker" },
                new SearchSynonym { Terms = "تلویزیون, تی وی, tv" },
                new SearchSynonym { Terms = "شیائومی, شیامی, xiaomi, mi" },
                new SearchSynonym { Terms = "سامسونگ, samsung" },
                new SearchSynonym { Terms = "اپل, apple, آیفون, iphone" },
                // مرحلهٔ سیزدهم: پیشنهادهای کامل‌تر برای یک فروشگاه کالای دیجیتال (املای رایج، فارسی/انگلیسی، نام برندها).
                new SearchSynonym { Terms = "شارژر, آداپتور, شارژ دیواری, charger, adapter" },
                new SearchSynonym { Terms = "کابل, سیم شارژ, کابل شارژ, cable" },
                new SearchSynonym { Terms = "ماوس, موس, mouse" },
                new SearchSynonym { Terms = "کیبورد, صفحه کلید, keyboard" },
                new SearchSynonym { Terms = "مانیتور, نمایشگر, monitor" },
                new SearchSynonym { Terms = "تبلت, tablet, آیپد, ipad" },
                new SearchSynonym { Terms = "کنسول, پلی استیشن, پلی‌استیشن, playstation, ps5, ps4, ایکس باکس, xbox" },
                new SearchSynonym { Terms = "دسته بازی, گیم پد, جوی استیک, gamepad, controller" },
                new SearchSynonym { Terms = "فلش, فلش مموری, فلش مموری usb, flash, flash memory" },
                new SearchSynonym { Terms = "کارت حافظه, مموری کارت, رم میکرو, micro sd, memory card" },
                new SearchSynonym { Terms = "هارد, هارد اکسترنال, هارد دیسک, hdd, ssd, external hard" },
                new SearchSynonym { Terms = "مودم, روتر, modem, router" },
                new SearchSynonym { Terms = "دوربین, camera, وبکم, webcam" },
                new SearchSynonym { Terms = "مچ بند هوشمند, دستبند هوشمند, smart band, mi band" },
                new SearchSynonym { Terms = "قاب, کاور, قاب گوشی, case, cover" },
                new SearchSynonym { Terms = "گلس, محافظ صفحه, گلس گوشی, screen protector" },
                new SearchSynonym { Terms = "هوآوی, هواوی, huawei, آنر, honor" },
                new SearchSynonym { Terms = "انکر, anker" },
                new SearchSynonym { Terms = "هایلو, haylou" },
                new SearchSynonym { Terms = "جی بی ال, jbl" },
                new SearchSynonym { Terms = "سونی, sony" },
                new SearchSynonym { Terms = "لنوو, lenovo" },
                new SearchSynonym { Terms = "ایسوس, asus" },
                new SearchSynonym { Terms = "ال جی, lg" },
                new SearchSynonym { Terms = "باسئوس, بیسوس, baseus" });
        }

        if (!await context.SearchQueryLogs.IgnoreQueryFilters().AnyAsync(cancellationToken))
        {
            // Pinned suggestions for a fresh shop (popular searches grow from real traffic afterwards).
            var now = DateTimeOffset.UtcNow;
            context.SearchQueryLogs.AddRange(
                Pinned("هندزفری بی سیم", now), Pinned("ساعت هوشمند", now), Pinned("لپ تاپ", now), Pinned("پاوربانک", now),
                Pinned("گوشی موبایل", now), Pinned("هدفون", now), Pinned("اسپیکر بلوتوثی", now), Pinned("شارژر", now),
                Pinned("کابل شارژ", now), Pinned("کنسول بازی", now), Pinned("تبلت", now), Pinned("فلش مموری", now));
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    private static SearchQueryLog Pinned(string query, DateTimeOffset now) => new()
    {
        Query = query,
        NormalizedQuery = query,
        IsPinned = true,
        LastSearchedAt = now,
    };

    private static NotificationTemplate Template(NotificationEvent @event, string title, string body, string? linkText, bool push = true) => new()
    {
        Event = @event,
        Title = title,
        Body = body,
        LinkText = linkText,
        SendPush = push,
    };
}
