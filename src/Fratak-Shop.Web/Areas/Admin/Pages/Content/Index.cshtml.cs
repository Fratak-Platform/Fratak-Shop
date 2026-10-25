using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Web.Infrastructure.Admin;
using Fratak_Shop.Web.Infrastructure.Admin.Manage;

namespace Fratak_Shop.Web.Areas.Admin.Pages.Content;

/// <summary>Content hub: site pages, home page sections, menus, FAQ, banners, alerts, trust badges.</summary>
[AdminPermission(AdminPermissions.ContentManage, AdminPermissions.MarketingManage, AdminPermissions.BlogPublish)]
public class IndexModel(ManageEngine engine) : AdminPageModel
{
    private static readonly (string Key, string Hint)[] Sections =
    [
        ("home-sections", "ترتیب و نمایش بخش‌های صفحه اصلی (اسلایدر، پیشنهادها، پرفروش‌ها، …)"),
        ("banners", "اسلایدر و بنرهای تبلیغاتی با زمان‌بندی نمایش"),
        ("content-pages", "درباره ما، قوانین، حریم خصوصی، راهنمای خرید و سایر صفحات"),
        ("menus", "منوی اصلی، میانبرهای هدر و منوی فوتر"),
        ("menu-items", "آیتم‌های هر منو (لینک، آیکون، برچسب، زمان‌بندی)"),
        ("faq-categories", "دسته‌بندی پرسش‌های متداول"),
        ("faq-items", "پرسش و پاسخ‌های متداول"),
        ("page-alerts", "پیام‌ها و اطلاعیه‌های بالای صفحات (مثلاً تعطیلات)"),
        ("site-features", "مزایای فروشگاه (ارسال سریع، ضمانت اصالت، …)"),
        ("trust-badges", "نماد اعتماد و مجوزها در فوتر"),
        ("blog-categories", "دسته‌های وبلاگ"),
    ];

    public List<(MDef Def, int Count, string Hint)> Items { get; private set; } = [];

    public async Task OnGetAsync()
    {
        foreach (var (key, hint) in Sections)
        {
            var def = ManageRegistry.All.FirstOrDefault(d => d.Key == key);
            if (def is not null && Access.HasAny(def.Permissions))
                Items.Add((def, await engine.CountAsync(def), hint));
        }
    }
}
