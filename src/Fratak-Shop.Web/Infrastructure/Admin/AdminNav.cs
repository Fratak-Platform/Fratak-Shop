using Fratak_Shop.Core.DTOs.Admin;
using P = Fratak_Shop.Core.Services.Admin.AdminPermissions;

namespace Fratak_Shop.Web.Infrastructure.Admin;

/// <param name="AnyOf">یکی از این مجوزها کافی است تا آیتم دیده شود.</param>
/// <param name="AllOf">مرحلهٔ ۱۴: علاوه بر AnyOf، همهٔ این مجوزها لازم است (صفحه‌ای که بدون هر دو کاری از آن برنمی‌آید؛
/// مثال: «درون‌ریزی از سایت» = مدیریت محصولات + اطلاعات تأمین‌کننده). بدون آن، نقش فروش لینکی می‌دید که به «دسترسی ندارید» می‌رسید.</param>
public sealed record AdminNavItem(string Title, string Url, string Icon, string[]? AnyOf = null, string? BadgeKey = null, bool OwnerOnly = false, string[]? AllOf = null)
{
    public bool IsVisible(AdminAccess access) => OwnerOnly
        ? access.IsOwner
        : (AnyOf is null || access.HasAny(AnyOf)) && (AllOf is null || AllOf.All(access.Has));
}

/// <summary>
/// گروه منو. دور ۳۰ (#۱۱): در نوار کناری «تاشو» و «پیش‌فرض بسته» است – حتی گروه صفحهٔ فعلی – و فقط با کلیک باز می‌شود
/// (admin-nav.js؛ با باز کردن یک گروه، بقیه بسته می‌شوند). گروه بی‌عنوان فقط «داشبورد» است و همیشه دیده می‌شود.
/// </summary>
public sealed record AdminNavGroup(string Title, params AdminNavItem[] Items);

/// <summary>Menu of the admin panel. Items are shown only to staff with one of the listed permissions.</summary>
/// <remarks>
/// دور ۲۹ (#۲۹ و #۱۹): نام همهٔ گروه‌ها و صفحه‌ها روشن و توصیفی شد (مثلاً «فنی» ← «فنی: سرور و امنیت»، «کانبان سفارش‌ها» ←
/// «تابلوی وضعیت سفارش‌ها»)؛ آدرس‌ها عوض نشده‌اند تا لینک‌ها، میانبرها و راهنماها سالم بمانند.
/// </remarks>
public static class AdminNav
{
    public static readonly AdminNavGroup[] Groups =
    [
        // دور ۳۰ (#۱۱): فقط «داشبورد» بیرون از گروه‌هاست؛ بقیهٔ موارد شخصی هم در یک گروه تاشو (پیش‌فرض بسته) رفتند.
        new("", new AdminNavItem("داشبورد", "/admin", "SquaresFour")),
        new("میز کار من", new AdminNavItem("صندوق اعلان‌های من", "/admin/inbox", "Tray"),
            new AdminNavItem("میانبرهای سنجاق‌شدهٔ من", "/admin/ux", "PushPin"),
            // مرحلهٔ هفتم – فاز ۱۰: کار تیمی و امنیت کارکنان.
            new AdminNavItem("کارهای سپرده‌شده به من", "/admin/tasks", "ListChecks", null, "tasks"),
            new AdminNavItem("تأیید دونفرهٔ کارهای حساس", "/admin/approvals", "ShieldCheck", [P.CustomersWallet, P.WithdrawalsManage, P.StaffManage], "approvals"),
            new AdminNavItem("امنیت و ورود حساب من", "/admin/account", "Fingerprint")),
        new("فروش و سفارش‌ها",
            new AdminNavItem("همهٔ سفارش‌ها", "/admin/orders", "Orders", [P.OrdersView], "orders"),
            new AdminNavItem("تابلوی وضعیت سفارش‌ها", "/admin/orders/board", "Kanban", [P.OrdersView]),
            new AdminNavItem("آماده‌سازی و بسته‌بندی سفارش‌ها", "/admin/orders/prep", "ListChecks", [P.OrdersView]),
            new AdminNavItem("رهگیری مرسوله‌های پستی", "/admin/orders/shipments", "Truck", [P.OrdersView]),
            new AdminNavItem("ثبت سفارش برای مشتری", "/admin/orders/new", "Plus", [P.OrdersCreate]),
            new AdminNavItem("فاکتور خرید از تأمین‌کننده", "/admin/orders/purchase-invoices", "Receipt", [P.PurchaseInvoices, P.FinanceView]),
            new AdminNavItem("درخواست‌های مرجوعی", "/admin/returns", "ArrowUUpLeft", [P.ReturnsManage], "returns"),
            new AdminNavItem("مشتریان و حساب‌هایشان", "/admin/customers", "Users", [P.CustomersView]),
            new AdminNavItem("نظرسنجی بعد از خرید", "/admin/feedback", "Smile", [P.OrdersView, P.TicketsManage])),
        new("محصولات و انبار",
            new AdminNavItem("فهرست محصولات", "/admin/products", "Package", [P.ProductsView]),
            new AdminNavItem("همگام‌سازی قیمت و موجودی تأمین‌کننده", "/admin/products/supplier-sync", "ArrowsClockwise", [P.ProductsSupplier], "supplier"),
            new AdminNavItem("موجودی انبار و پیش‌بینی اتمام", "/admin/products/inventory", "ChartLine", [P.ProductsView]),
            new AdminNavItem("مقایسهٔ قیمت با رقبا", "/admin/products/competitors", "Tag", [P.ProductsView]),
            new AdminNavItem("تغییر گروهی قیمت و تخفیف", "/admin/products/bulk-price", "Calculator", [P.ProductsManage]),
            new AdminNavItem("ورود محصول از فایل اکسل", "/admin/products/import", "UploadSimple", [P.ProductsManage]),
            new AdminNavItem("ورود محصول از سایت تأمین‌کننده", "/admin/products/site-import", "Storefront", [P.ProductsManage], AllOf: [P.ProductsManage, P.ProductsSupplier]),
            new AdminNavItem("دسته‌بندی محصولات", "/admin/manage/categories", "Stack", [P.CatalogManage]),
            new AdminNavItem("برندهای محصولات", "/admin/manage/brands", "Tag", [P.CatalogManage]),
            new AdminNavItem("تأمین‌کنندگان", "/admin/manage/suppliers", "Factory", [P.SuppliersManage]),
            new AdminNavItem("ویدیوهای معرفی محصول", "/admin/manage/product-videos", "VideoCamera", [P.ProductsManage]),
            new AdminNavItem("نظرات و پرسش‌های مشتریان", "/admin/comments", "ChatDots", [P.CommentsModerate], "comments")),
        // دور ۲۳: همهٔ صفحه‌های هوشمند یک‌جا (قبلاً بین کاتالوگ، گزارش‌ها، محتوا و تنظیمات پخش بودند).
        new("هوشمند (خودکار)",
            new AdminNavItem("صف کارهای هوشمند", "/admin/smart", "MagicWand", [P.ProductsManage, P.CatalogManage, P.BlogPublish]),
            new AdminNavItem("خلاصهٔ روزانهٔ فروشگاه", "/admin/smart/brief", "ListChecks", [P.ReportsView, P.ProductsManage]),
            new AdminNavItem("دسته‌بندی خودکار و درخت دسته‌ها", "/admin/smart/categories", "Stack", [P.CatalogManage]),
            new AdminNavItem("ویدیوهای پیشنهادی کالا", "/admin/smart/videos", "VideoCamera", [P.ProductsManage]),
            new AdminNavItem("تقویم مقاله‌های خودکار", "/admin/smart/blog", "Sparkle", [P.BlogPublish]),
            new AdminNavItem("پیشنهادهای خودکار رشد فروش", "/admin/smart/growth", "TrendUp", [P.ReportsView, P.MarketingManage, P.SeoManage]),
            new AdminNavItem("گزارش دستیار گفت‌وگوی مشتری", "/admin/smart/assistant", "Robot", [P.ProductsManage, P.ReportsView, P.MarketingManage, P.TicketsManage]),
            new AdminNavItem("گفت‌وگو با کارشناس (ارجاع از دستیار)", "/admin/smart/assistant/handoffs", "Chat", [P.TicketsManage, P.MessagesManage]),
            // دور ۲۵: جواب‌هایی که کارکنان به دستیار یاد می‌دهند.
            new AdminNavItem("آموزش جواب به دستیار", "/admin/smart/assistant/teach", "Brain", [P.ProductsManage, P.MarketingManage, P.TicketsManage]),
            // دور ۲۸: جدول «دانش دستیار» و «دستیار مدیریت» (پرسش و فرمان کارکنان به زبان ساده).
            new AdminNavItem("جدول دانش دستیار", "/admin/smart/assistant/knowledge", "Lightbulb", [P.ProductsManage, P.MarketingManage, P.TicketsManage]),
            new AdminNavItem("دستیار مدیریت (فرمان متنی)", "/admin/smart/assistant/ops", "Robot", [P.OrdersView, P.ProductsView, P.ReportsView]),
            // دور ۲۸ (#۵۱–۵۹): امتیاز اطمینان داده‌ها، عکس‌های مشکوک و درست کردن خودکار.
            new AdminNavItem("سلامت داده و عکس محصولات", "/admin/smart/data-health", "Pulse", [P.ProductsManage, P.CatalogManage, P.ReportsView]),
            new AdminNavItem("تنظیمات هوشمند، عکس‌ها و دستیار", "/admin/smart/settings", "Gear", [P.SettingsManage])),
        new("محتوا و سئو",
            new AdminNavItem("مقاله‌های وبلاگ", "/admin/blog", "Article", [P.BlogWrite, P.BlogPublish], "drafts"),
            new AdminNavItem("صفحه‌ها و محتوای سایت", "/admin/content", "Notebook", [P.ContentManage]),
            // دور ۲۹ (#۲۷): ویرایش سادهٔ لینک‌های نوار منوی هدر.
            new AdminNavItem("منوی بالای سایت", "/admin/content/header-menu", "List", [P.ContentManage]),
            new AdminNavItem("کتابخانهٔ عکس و فایل", "/admin/media", "Images", [P.ContentManage, P.BlogWrite, P.ProductsManage, P.MarketingManage]),
            new AdminNavItem("وضعیت سئو و ریدایرکت‌ها", "/admin/seo", "Globe", [P.SeoManage], "seo"),
            // دور سئوی خودکار: داشبورد، کارها، تاریخچه (برگشت) و ربات‌ها/سرعت؛ مشاور سئو (SeoView) فقط می‌بیند.
            new AdminNavItem("داشبورد سئوی خودکار", "/admin/seo/autopilot", "Gauge", [P.SeoView, P.SeoManage]),
            new AdminNavItem("مشکلات سئو برای رفع", "/admin/seo/tasks", "Warning", [P.SeoView, P.SeoManage]),
            new AdminNavItem("تاریخچه و برگشت تغییرات سئو", "/admin/seo/history", "Clock", [P.SeoView, P.SeoManage]),
            new AdminNavItem("خزش ربات‌ها و سرعت صفحه‌ها", "/admin/seo/signals", "Monitor", [P.SeoView, P.SeoManage]),
            // دور ۲۴ (۴۸ تا ۵۰): صفحه‌های کم‌بازدید، جست‌وجوهای بی‌نتیجه و هم‌نوع‌خواری کلمه.
            new AdminNavItem("فرصت‌های رشد سئو", "/admin/seo/opportunities", "Lightbulb", [P.SeoView, P.SeoManage]),
            new AdminNavItem("موتور هوشمند سئو", "/admin/seo/engine", "MagicWand", [P.SeoManage]),
            new AdminNavItem("تأیید پیشنهادهای سئو", "/admin/seo/review", "ListChecks", [P.SeoManage], "seo-review"),
            new AdminNavItem("ویرایش گروهی سئو", "/admin/seo/bulk", "PencilSimple", [P.SeoManage]),
            // مرحلهٔ شانزدهم: جدول دانش سئو و ویرایشگر هوشمند متن (همان مجوزهای صفحه).
            new AdminNavItem("جدول دانش سئو", "/admin/seo/knowledge", "Database", [P.SeoManage]),
            new AdminNavItem("ویرایشگر هوشمند متن", "/admin/seo/smart", "Sparkle", [P.ProductsManage, P.CatalogManage, P.BlogWrite, P.ContentManage, P.SeoManage]),
            new AdminNavItem("لینک‌های شکستهٔ سایت", "/admin/seo/links", "LinkBreak", [P.SeoManage]),
            new AdminNavItem("گوگل سرچ کنسول", "/admin/seo/search-console", "ChartLine", [P.SeoManage])),
        new("بازاریابی و تخفیف",
            new AdminNavItem("کدهای تخفیف (کوپن)", "/admin/manage/coupons", "Percent", [P.MarketingManage]),
            new AdminNavItem("کارت‌های هدیه", "/admin/gift-cards", "Gift", [P.GiftCardsManage]),
            new AdminNavItem("جایزه‌های باشگاه مشتریان", "/admin/manage/loyalty-rewards", "Crown", [P.LoyaltyManage]),
            new AdminNavItem("بنرهای صفحهٔ اصلی و دسته‌ها", "/admin/manage/banners", "Image", [P.MarketingManage, P.ContentManage]),
            // دور ۲۱: استودیوی طراحی (بنر، کاور مقاله، پست) و صفحه‌ساز کمپین.
            new AdminNavItem("استودیوی طراحی بنر و پست", "/admin/design", "Palette", [P.MarketingManage, P.ContentManage, P.BlogPublish]),
            new AdminNavItem("صفحه‌های کمپین و فرود", "/admin/campaigns", "Notebook", [P.MarketingManage, P.ContentManage]),
            new AdminNavItem("ارسال اعلان به مشتریان", "/admin/notifications", "Megaphone", [P.NotificationsSend]),
            new AdminNavItem("متن پیام‌های خودکار (پیامک و ایمیل)", "/admin/notifications/templates", "Bell", [P.NotificationsSend, P.SettingsManage]),
            new AdminNavItem("پاسخ‌های آماده", "/admin/ready-messages", "ChatCenteredText", [P.NotificationsSend, P.TicketsManage, P.CommentsModerate, P.MessagesManage, P.OrdersManage]),
            new AdminNavItem("سبدهای خرید رهاشده", "/admin/abandoned-carts", "CartFull", [P.MarketingManage]),
            // دور ۲۴ (پیشنهاد ۱۱): درخواست‌های «قیمت کمتر دیدید؟».
            new AdminNavItem("درخواست‌های تضمین قیمت", "/admin/marketing/price-match", "Tag", [P.MarketingManage])),
        new("پشتیبانی مشتریان",
            new AdminNavItem("تیکت‌های پشتیبانی", "/admin/tickets", "Lifebuoy", [P.TicketsManage], "tickets"),
            new AdminNavItem("پیام‌های فرم تماس با ما", "/admin/messages", "Envelope", [P.MessagesManage], "messages")),
        new("مالی و کیف پول",
            new AdminNavItem("خلاصهٔ مالی و فروش", "/admin/finance", "ChartLine", [P.FinanceView]),
            new AdminNavItem("پرداخت‌های درگاه بانکی", "/admin/finance/payments", "CreditCard", [P.FinanceView]),
            new AdminNavItem("برداشت از کیف پول مشتریان", "/admin/finance/withdrawals", "Bank", [P.WithdrawalsManage, P.FinanceView], "withdrawals"),
            new AdminNavItem("گردش کیف پول مشتریان", "/admin/finance/wallet", "Wallet", [P.FinanceView, P.CustomersWallet]),
            new AdminNavItem("هزینه‌ها و سود خالص", "/admin/finance/expenses", "Coins", [P.FinanceView, P.ExpensesManage])),
        // مرحلهٔ هفتم – فاز ۷: گزارش‌های تحلیلی (آمار بازدید داخلی).
        new("گزارش و آمار",
            new AdminNavItem("قیف بازدید تا خرید", "/admin/reports", "Funnel", [P.ReportsView, P.FinanceView, P.MarketingManage]),
            new AdminNavItem("فروش و بازدید هر محصول", "/admin/reports/products", "TrendUp", [P.ReportsView, P.FinanceView, P.MarketingManage]),
            new AdminNavItem("جست‌وجو و تقاضای مشتریان", "/admin/reports/demand", "MagnifyingGlass", [P.ReportsView, P.FinanceView, P.MarketingManage, P.ProductsManage]),
            new AdminNavItem("منبع بازدیدکننده‌ها و کمپین‌ها", "/admin/reports/campaigns", "Target", [P.ReportsView, P.FinanceView, P.MarketingManage]),
            new AdminNavItem("هدف فروش ماهانه", "/admin/reports/goals", "Trophy", [P.ReportsView, P.FinanceView, P.ExpensesManage]),
            new AdminNavItem("عملکرد کارکنان", "/admin/reports/staff", "UsersThree", [P.StaffManage, P.AuditView]),
            new AdminNavItem("ارسال خودکار گزارش‌ها", "/admin/reports/scheduled", "PaperPlaneTilt", [P.FinanceView, P.SettingsManage])),
        new("تنظیمات فروشگاه",
            new AdminNavItem("تنظیمات عمومی فروشگاه", "/admin/settings", "Gear", [P.SettingsManage]),
            // مرحلهٔ دوازدهم: لوگوی همهٔ محل‌ها + آیکون اپ.
            new AdminNavItem("لوگو و آیکون سایت", "/admin/settings/brand", "Palette", [P.SettingsManage]),
            new AdminNavItem("روش‌ها و هزینهٔ ارسال", "/admin/manage/shipping-methods", "Truck", [P.ShippingManage]),
            new AdminNavItem("کارکنان و نقش‌ها", "/admin/staff", "UserGear", [P.StaffManage]),
            new AdminNavItem("همهٔ جدول‌های داده (پیشرفته)", "/admin/manage", "Sliders"),
            new AdminNavItem("سطل بازیافت (موارد حذف‌شده)", "/admin/trash", "Trash", [P.ProductsDelete, P.BlogPublish, P.CatalogManage, P.ContentManage, P.LoyaltyManage,
                P.MarketingManage, P.ProductsManage, P.ReturnsManage, P.SeoManage, P.ShippingManage, P.SuppliersManage, P.TicketsManage, P.ExpensesManage]),
            new AdminNavItem("ویرایش راهنمای صفحه‌های پنل", "/admin/help", "Question", [P.SettingsManage])),
        new("فنی: سرور و امنیت",
            new AdminNavItem("وضعیت کلی سرور و سایت", "/admin/system", "Pulse", [P.MonitoringView]),
            new AdminNavItem("بازدیدکننده‌های آنلاین", "/admin/system/live", "Monitor", [P.MonitoringView]),
            // مرحلهٔ هفدهم: علت هر ۴۰۳/۴۲۹/۵۰۳/«دسترسی ندارید» و تاریخچهٔ روشن/خاموش شدن برنامه.
            new AdminNavItem("درخواست‌های ردشده (۴۰۳ و ۴۲۹)", "/admin/system/access", "Prohibit", [P.MonitoringView]),
            new AdminNavItem("خطاهای برنامه", "/admin/system/errors", "Bug", [P.MonitoringView], "errors"),
            new AdminNavItem("گزارش فعالیت کارکنان", "/admin/system/audit", "ClockCounterClockwise", [P.AuditView]),
            new AdminNavItem("رویدادهای امنیتی ورود و دسترسی", "/admin/system/security", "ShieldCheck", [P.AuditView, P.MonitoringView]),
            // دور ۲۴ (پیشنهاد ۷۴ و ۷۳): گزارش امنیتی هفتگی با امتیاز و دسترس‌پذیری (قطعی‌ها و درصد).
            new AdminNavItem("گزارش امنیتی هفتگی", "/admin/system/security-report", "ShieldWarning", [P.AuditView, P.MonitoringView]),
            new AdminNavItem("زمان در دسترس بودن سایت", "/admin/system/uptime", "Pulse", [P.MonitoringView]),
            // مرحلهٔ هفتم – فاز ۹: ابزارهای فنی.
            new AdminNavItem("پشتیبان‌گیری از دیتابیس", "/admin/system/backups", "Database", [P.MonitoringView, P.SystemManage]),
            new AdminNavItem("کارهای خودکار پس‌زمینه", "/admin/system/jobs", "ArrowsClockwise", [P.MonitoringView, P.SystemManage]),
            new AdminNavItem("مصرف حافظه و پردازنده", "/admin/system/resources", "Gauge", [P.MonitoringView, P.SystemManage]),
            new AdminNavItem("فایروال و مسدودسازی IP", "/admin/system/firewall", "Prohibit", [P.MonitoringView, P.SystemManage]),
            new AdminNavItem("هشدارهای خودکار سرور", "/admin/system/alerts", "BellRinging", [P.MonitoringView, P.SystemManage]),
            // دور هشتم: تنظیم و آزمون سرویس پیامک (SMS.ir).
            new AdminNavItem("تنظیم سرویس پیامک", "/admin/system/sms", "ChatDots", [P.MonitoringView, P.SystemManage]),
            // مرحلهٔ دوازدهم: تنظیم و آزمون سرویس ایمیل (SMTP).
            new AdminNavItem("تنظیم سرویس ایمیل", "/admin/system/email", "Envelope", [P.MonitoringView, P.SystemManage]),
            new AdminNavItem("لاگ‌های سرور", "/admin/system/logs", "Article", [P.MonitoringView])),
    ];
}
