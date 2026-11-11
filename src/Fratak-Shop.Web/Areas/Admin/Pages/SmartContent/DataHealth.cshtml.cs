using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Core.Services.Smart;
using Fratak_Shop.Web.Infrastructure.Admin;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Areas.Admin.Pages.SmartContent;

/// <summary>
/// دور ۲۸ (#۵۱ تا #۵۹) – «سلامت داده». آدرس: /admin/smart/data-health
/// <list type="bullet">
/// <item>امتیاز اطمینان ۰ تا ۱۰۰ برای هر کالا (نام، نام انگلیسی، دسته، قیمت/تخفیف/ضرر، موجودی، متن، عکس، آدرس تأمین‌کننده).</item>
/// <item>شمار هر نوع ایراد (کلیک ← فقط همان ایراد)، بدترین کالاها با دلیل و راه‌حل، و عکس‌های مشکوک (آدم، واترمارک، صحنه، امتیاز کم).</item>
/// <item>«درست کردن خودکار» ایرادهای بی‌خطر پیش‌نویس‌ها، «بررسی دوبارهٔ عکس‌ها» و «آماده‌سازی دوباره» یک کالا.</item>
/// </list>
/// GET هیچ تغییری نمی‌دهد. دیدن: مدیران محصول/کاتالوگ/گزارش؛ تغییر: فقط مدیر محصول.
/// </summary>
[AdminPermission(AdminPermissions.ProductsManage, AdminPermissions.CatalogManage, AdminPermissions.ReportsView)]
public class DataHealthModel(IDataHealthService health) : AdminPageModel
{
    /// <summary>گزارش ساخته‌شده در GET.</summary>
    public DataHealthReport Report { get; private set; } = null!;

    /// <summary>فیلتر کد ایراد (‎?issue=loss‎). فقط حروف لاتین، رقم و خط تیره پذیرفته می‌شود.</summary>
    [BindProperty(SupportsGet = true)] public string? Issue { get; set; }

    /// <summary>کاربر اجازهٔ تغییر دارد؟ (دکمه‌ها فقط برای او نشان داده می‌شوند.)</summary>
    public bool CanFix { get; private set; }

    /// <summary>متن نشانه‌های عکس. مثال: ۴ | ۸ ← «آدم، واترمارک».</summary>
    public static string FlagText(byte flags)
    {
        var parts = new List<string>();
        if ((flags & 4) != 0) parts.Add("آدم");
        if ((flags & 8) != 0) parts.Add("واترمارک");
        if ((flags & 16) != 0) parts.Add("صحنه");
        if (parts.Count == 0) parts.Add("امتیاز کم");
        return string.Join("، ", parts);
    }

    /// <summary>رنگ نشان شدت.</summary>
    public static string SeverityClass(DataSeverity severity) => severity switch
    {
        DataSeverity.Critical => "a-badge-red",
        DataSeverity.Warning => "a-badge-yellow",
        _ => "a-badge-gray",
    };

    public async Task<IActionResult> OnGetAsync()
    {
        if (Issue is not null && (Issue.Length > 30 || !Issue.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '-')))
            Issue = null;
        CanFix = Can(AdminPermissions.ProductsManage);
        Report = await health.ReportAsync(Issue, cancellationToken: HttpContext.RequestAborted);
        return Page();
    }

    /// <summary>درست کردن خودکار ایرادهای بی‌خطر (فقط پیش‌نویس‌ها).</summary>
    [AdminPermission(AdminPermissions.ProductsManage)]
    public async Task<IActionResult> OnPostFixAsync()
    {
        var count = await health.FixAsync(HttpContext.RequestAborted);
        SuccessMessage = count == 0 ? "ایراد بی‌خطری برای درست کردن خودکار نبود." : $"{Format.Number(count)} کالا خودکار درست شد.";
        return LocalRedirect("/admin/smart/data-health");
    }

    /// <summary>بررسی دوبارهٔ «سوژهٔ» عکس‌ها (یک کالا یا همه) در نوبت ساعتی بعد.</summary>
    [AdminPermission(AdminPermissions.ProductsManage)]
    public async Task<IActionResult> OnPostRecheckAsync(Guid? id)
    {
        var count = await health.RecheckImagesAsync(id, HttpContext.RequestAborted);
        SuccessMessage = count == 0 ? "عکسی برای بررسی دوباره نبود." : $"{Format.Number(count)} عکس در صف بررسی دوباره قرار گرفت (هر ساعت ۱۵۰ عکس).";
        return LocalRedirect("/admin/smart/data-health");
    }

    /// <summary>آماده‌سازی دوبارهٔ یک پیش‌نویس.</summary>
    [AdminPermission(AdminPermissions.ProductsManage)]
    public async Task<IActionResult> OnPostReprepareAsync(Guid id)
    {
        ShowResult(await health.ReprepareAsync(id, HttpContext.RequestAborted));
        return LocalRedirect("/admin/smart/data-health");
    }
}
