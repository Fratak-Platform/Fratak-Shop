using Fratak_Shop.Web.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Pages.Profile;

/// <summary>
/// دور ۲۹ – بخش «پیغام‌ها» به خواست کاربر از پروفایل حذف شد (تیکت‌های پشتیبانی همان کار را کامل‌تر انجام می‌دهند).
/// آدرس قدیمی /profile/messages برای لینک‌های ذخیره‌شده از کار نمی‌افتد: انتقال دائمی (۳۰۱) به تیکت‌ها،
/// یا اگر تیکت خاموش است به صفحهٔ «تماس با ما».
/// </summary>
public class MessagesModel(ISiteContext site) : ShopPageModel
{
    public IActionResult OnGet() =>
        RedirectPermanent(site.Settings.IsTicketEnabled ? "/profile/tickets" : "/contact-us");
}
