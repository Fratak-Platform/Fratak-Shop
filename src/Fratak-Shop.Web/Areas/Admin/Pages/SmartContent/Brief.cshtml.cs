using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Core.Services.Smart;
using Fratak_Shop.Web.Infrastructure.Admin;

namespace Fratak_Shop.Web.Areas.Admin.Pages.SmartContent;

/// <summary>
/// خلاصهٔ روزانهٔ فروش (دور ۱۹، C24–C27). آدرس: /admin/smart/brief
/// <para>یک نگاه صبحگاهی: سفارش و فروش دیروز، کارهای منتظر، پیشنهاد سفارش دوباره (موجودی رو به اتمام با سرعت فروش واقعی)،
/// کالاهای راکد (سرمایهٔ خوابیده)، کالاهای زیر قیمت خرید و جست‌وجوهای بی‌نتیجهٔ مشتریان + فهرست «کارهای پیشنهادی امروز».</para>
/// <para>فقط خواندنی است (GET هیچ تغییری نمی‌دهد).</para>
/// </summary>
[AdminPermission(AdminPermissions.ReportsView, AdminPermissions.ProductsManage)]
public class BriefModel(ISalesBriefService briefs) : AdminPageModel
{
    public DailyBrief Brief { get; private set; } = new();

    public async Task OnGetAsync() => Brief = await briefs.GetAsync(HttpContext.RequestAborted);
}
