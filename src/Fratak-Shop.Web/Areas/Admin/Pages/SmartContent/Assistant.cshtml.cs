using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Core.Services.Assistant;
using Fratak_Shop.Data.Entities.Assistant;
using Fratak_Shop.Web.Helpers;
using Fratak_Shop.Web.Infrastructure.Admin;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Areas.Admin.Pages.SmartContent;

/// <summary>
/// پنل «دستیار فروشگاه» (دور ۲۳). آدرس: /admin/smart/assistant
/// <list type="bullet">
/// <item><b>آمار:</b> تعداد پیام، گفت‌وگو و درصد پاسخ‌داده‌شده در بازهٔ ۷/۳۰/۹۰ روز.</item>
/// <item><b>پرسش‌های پرتکرار:</b> متن‌های تکراری و موضوع‌های پرسیده‌شده (گارانتی، باتری، ارسال…) ← ایدهٔ «پرسش‌های متداول».</item>
/// <item><b>کالاهای درخواستی:</b> چیزهایی که مشتری خواست و نداشتیم ← ایدهٔ خرید کالای تازه.</item>
/// <item><b>مشکل کالاها:</b> کالا + موضوعی که جوابش در اطلاعات ما نبود ← لینک استودیوی کالا. دکمهٔ «تکمیل خودکار» آن‌ها را در صف
/// «دریافت مشخصات» هوشمند می‌گذارد (همین کار هر روز خودکار هم انجام می‌شود).</item>
/// <item><b>پیام‌های بی‌جواب:</b> ۲۰ پیام اخیری که دستیار نفهمید (شمارهٔ موبایل و کارت پیش از ذخیره پوشانده شده است).</item>
/// </list>
/// GET هیچ تغییری نمی‌دهد.
/// </summary>
[AdminPermission(AdminPermissions.ProductsManage, AdminPermissions.ReportsView, AdminPermissions.MarketingManage, AdminPermissions.TicketsManage)]
public class AssistantModel(IAssistantService assistant) : AdminPageModel
{
    public AssistantInsights Report { get; private set; } = new(30, 0, 0, 0, [], [], [], [], [], []);

    public int Days { get; private set; } = 30;

    public bool CanFeed => CanAny(AdminPermissions.ProductsManage);

    public async Task OnGetAsync(int? days)
    {
        Days = days is 7 or 30 or 90 ? days.Value : 30;
        Report = await assistant.InsightsAsync(Days, HttpContext.RequestAborted);
    }

    /// <summary>کالاهای دارای پرسش بی‌جواب ← صف «دریافت مشخصات» هوشمند (حداکثر ۲۰).</summary>
    [AdminPermission(AdminPermissions.ProductsManage)]
    public async Task<IActionResult> OnPostFeedAsync()
    {
        var count = await assistant.FeedGapsAsync(20, HttpContext.RequestAborted);
        if (count > 0)
            SuccessMessage = $"{Format.Number(count)} کالا برای تکمیل مشخصات در صف هوشمند قرار گرفت.";
        else
            ErrorMessage = "کالایی با دست‌کم ۲ پرسش بی‌جواب نبود (یا محتوای هوشمند خاموش است).";
        return LocalRedirect("/admin/smart/assistant");
    }

    /// <summary>عنوان فارسی نیت. مثال: Search ← «جست‌وجو».</summary>
    public static string IntentTitle(string key) => Enum.TryParse<AssistantIntent>(key, out var intent) ? IntentTitle(intent) : key;

    public static string IntentTitle(AssistantIntent intent) => intent switch
    {
        AssistantIntent.Greeting => "سلام",
        AssistantIntent.Search => "جست‌وجوی کالا",
        AssistantIntent.ProductQuestion => "پرسش دربارهٔ کالا",
        AssistantIntent.Compare => "مقایسه",
        AssistantIntent.CartAdd => "افزودن به سبد",
        AssistantIntent.CartRemove => "حذف از سبد",
        AssistantIntent.CartShow => "دیدن سبد",
        AssistantIntent.Checkout => "تسویه حساب",
        AssistantIntent.Address => "آدرس",
        AssistantIntent.Track => "پیگیری سفارش",
        AssistantIntent.Help => "راهنما",
        AssistantIntent.OffTopic => "بی‌ربط",
        AssistantIntent.Demand => "کالای ناموجود",
        AssistantIntent.Human => "درخواست پشتیبان",
        AssistantIntent.Thanks => "تشکر",
        _ => "نامشخص",
    };
}
