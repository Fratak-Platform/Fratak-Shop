using Fratak_Shop.Core.Services.Admin;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace Fratak_Shop.Web.Infrastructure;

/// <summary>
/// دور ۲۳ – «پیش‌نمایش کارکنان»: مقالهٔ پیش‌نویس، کالای غیرفعال و کمپین غیرفعال برای مشتری ۴۰۴ است، ولی کارمندِ واردشده
/// (با یکی از دسترسی‌های لازم) همان صفحه را با نوار «پیش‌نمایش – منتشر نشده» می‌بیند.
/// <list type="bullet">
/// <item>noindex, nofollow (هم متا، هم سرآیند X-Robots-Tag) ← گوگل هرگز نسخهٔ پیش‌نمایش را ایندکس نمی‌کند.</item>
/// <item>Cache-Control: no-store ← نه کش صفحه، نه مرورگر. (کاربر واردشده اصلاً از کش صفحه رد نمی‌شود – PageCachePolicy.)</item>
/// <item>بازدید شمرده نمی‌شود (صفحه‌ها خودشان با <see cref="IsActive"/> بررسی می‌کنند).</item>
/// </list>
/// استفاده: BlogPost، Product و Campaign. مثال: نویسنده در پنل «مشاهده» را می‌زند ← /blog/my-draft با نوار زرد پیش‌نمایش.
/// </summary>
public static class StaffPreview
{
    /// <summary>کلید ViewData برای متن نوار (لایهٔ اصلی نشانش می‌دهد).</summary>
    public const string ViewDataKey = "StaffPreview";

    private const string ItemKey = "fs:staff-preview";

    /// <summary>
    /// کاربر فعلی کارمند است و (مالک یا) یکی از <paramref name="anyOf"/> را دارد؟ مهمان یا مشتری عادی ← false (همان ۴۰۴).
    /// خطای سرویس دسترسی ← false (امن: پیش‌نویس هرگز به اشتباه نشان داده نمی‌شود).
    /// </summary>
    public static async Task<bool> AllowedAsync(HttpContext http, params string[] anyOf)
    {
        if (http.User.GetUserId() is not { } userId)
            return false;
        try
        {
            var access = await http.RequestServices.GetRequiredService<IAdminAccessService>().GetAccessAsync(userId);
            return access.IsStaff && (anyOf.Length == 0 || access.HasAny(anyOf));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return false;
        }
    }

    /// <summary>این درخواست پیش‌نمایش است؟ (مثلاً برای نشمردن بازدید.)</summary>
    public static bool IsActive(HttpContext http) => http.Items.ContainsKey(ItemKey);

    /// <summary>
    /// علامت‌گذاری پاسخ به‌عنوان پیش‌نمایش: نوار بالای صفحه، noindex و no-store. بعد از pageMeta.Apply صدا بزنید تا robots بازنویسی نشود.
    /// </summary>
    public static void Mark(HttpContext http, ViewDataDictionary viewData, string note)
    {
        http.Items[ItemKey] = true;
        viewData[ViewDataKey] = note;
        viewData[Fratak_Shop.Web.Services.PageMetaKeys.Robots] = "noindex, nofollow";
        http.Response.Headers.CacheControl = "no-store, private";
        http.Response.Headers["X-Robots-Tag"] = "noindex, nofollow";
    }
}
